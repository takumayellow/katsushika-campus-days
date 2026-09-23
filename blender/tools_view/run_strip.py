"""Run（走り）の 1 周期を前・横・後ろから連番で描き、1 枚のフィルムストリップにする。

腕の振りや重心のブレのように「動きの形」を見たいときは、1 コマのプレビューでは
足りないのでこれを使う。`build_characters.py` と同じ手順でキャラを組み立てるので、
docs/previews の他の PNG と同じ画づくりになる。

使い方:
  "/c/Program Files/Blender Foundation/Blender 4.5/blender.exe" -b \
      --python blender/tools_view/run_strip.py -- \
      --id mirai --out-dir docs/previews

出力: <out-dir>/<id>_run_{front,side,back}.png（横に nframes コマ並べた 1 枚）

--arm / --elbow を渡すと Run の腕の振り幅だけ差し替えて描ける（修正前後の比較用）。
  --arm 52 --elbow 66,34   # #43 を直す前の値
"""

from __future__ import annotations

import argparse
import math
import os
import shutil
import sys
import tempfile

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
sys.path.insert(0, os.path.join(ROOT, "blender"))

import bpy  # noqa: E402
import numpy as np  # noqa: E402
from mathutils import Vector  # noqa: E402

import build_characters as BC  # noqa: E402
from kcd_chara import anim, render  # noqa: E402

#: 前・横・後ろ（カメラの方位角）
ANGLES = (("front", 0.0), ("side", 90.0), ("back", 180.0))


def parse_args(argv):
    argv = argv[argv.index("--") + 1:] if "--" in argv else []
    ap = argparse.ArgumentParser(prog="run_strip.py")
    ap.add_argument("--id", default="mirai")
    ap.add_argument("--out-dir", default=os.path.join(ROOT, "docs", "previews"))
    ap.add_argument("--tag", default="", help="ファイル名に付ける接尾辞（比較用）")
    ap.add_argument("--cell", type=int, default=420, help="1 コマの横幅 px")
    ap.add_argument("--arm", type=float, default=None,
                    help="Run の肩の片振幅を度で上書きする")
    ap.add_argument("--elbow", default=None,
                    help="Run の肘を 'e0,e1' で上書きする（度）")
    return ap.parse_args(argv)


def override_gait(arm_deg, elbow):
    """Gait の Run だけ腕の値を差し替える（修正前の絵を出すため）。"""
    orig = anim.Gait.__init__

    def patched(self, armature, kind="Walk"):
        orig(self, armature, kind)
        if kind != "Run":
            return
        if arm_deg is not None:
            self.arm = math.radians(arm_deg)
        if elbow is not None:
            self.elbow = (math.radians(elbow[0]), math.radians(elbow[1]))

    anim.Gait.__init__ = patched


def _load(path):
    img = bpy.data.images.load(path)
    w, h = img.size
    buf = np.empty(w * h * 4, dtype=np.float32)
    img.pixels.foreach_get(buf)
    bpy.data.images.remove(img)
    return buf.reshape(h, w, 4)


def make_strip(paths: list[str], out: str) -> str:
    """PNG を横につなげて 1 枚にする（Blender の画素は下から上なのでそのまま連結）。"""
    cells = [_load(p) for p in paths]
    h, w = cells[0].shape[:2]
    big = np.zeros((h, w * len(cells), 4), dtype=np.float32)
    for i, a in enumerate(cells):
        big[:, i * w:(i + 1) * w] = a
    img = bpy.data.images.new("strip", width=w * len(cells), height=h, alpha=True)
    img.pixels.foreach_set(big.reshape(-1))
    img.filepath_raw = out
    img.file_format = "PNG"
    img.save()
    bpy.data.images.remove(img)
    return out


def main():
    args = parse_args(sys.argv)
    elbow = None
    if args.elbow:
        a, b = args.elbow.split(",")
        elbow = (float(a), float(b))
    if args.arm is not None or elbow is not None:
        override_gait(args.arm, elbow)

    # Blender は相対パスを blend ファイル基準に直すので、ここで絶対パスにする
    out_dir = os.path.abspath(args.out_dir)
    os.makedirs(out_dir, exist_ok=True)
    # 仮 FBX とコマ単位の PNG は OS の一時ディレクトリへ。out_dir の下に置くと、
    # 途中で失敗したとき docs/previews に作業用フォルダが残る。
    tmp = tempfile.mkdtemp(prefix="kcd_run_strip_")

    info = BC.build_character(args.id, tmp, 1024, dict(BC.FBX_OPTS),
                              with_outline=True)
    p, armature, obj = info["params"], info["arm"], info["obj"]
    act = bpy.data.actions.get("Run")
    if act is None:
        raise SystemExit("Run の Action が無い")

    floor_z = min(float((obj.matrix_world @ Vector(c))[2])
                  for c in obj.bound_box) - p["height"] * 0.004
    render.setup_scene(p, floor_z=floor_z)
    scene = bpy.context.scene
    scene.render.resolution_x = args.cell
    scene.render.resolution_y = int(args.cell * 4 / 3)
    cam = render.make_camera(p)
    shown = [o for o in scene.objects
             if o.type == "MESH" and o.name != "PreviewFloor"]
    span = render._bounds(shown)

    if armature.animation_data is None:
        armature.animation_data_create()
    armature.animation_data.action = act
    try:
        armature.animation_data.action_slot = \
            armature.animation_data.action_suitable_slots[0]
    except Exception:  # noqa: BLE001 - 4.3 以前はスロットが無い
        pass
    lo, hi = act.frame_range
    # 最終フレームは先頭と同じ姿勢（ループ）なので外す
    frames = list(range(int(lo), int(hi)))

    suffix = ("_" + args.tag) if args.tag else ""
    made = []
    for tag, ang in ANGLES:
        cells = []
        for f in frames:
            scene.frame_set(f)
            render.place_camera(cam, p, ang, span=span)
            q = os.path.join(tmp, f"{args.id}_run_{tag}_{f:02d}.png")
            render.render_to(q)
            cells.append(q)
        made.append(make_strip(
            cells, os.path.join(out_dir, f"{args.id}_run_{tag}{suffix}.png")))
    armature.animation_data.action = None
    shutil.rmtree(tmp, ignore_errors=True)   # コマ単位の PNG と仮 FBX は残さない

    print("frames=%d cell=%dx%d" % (len(frames), scene.render.resolution_x,
                                    scene.render.resolution_y))
    for m in made:
        print("strip: %s" % m.replace("\\", "/"))


main()
