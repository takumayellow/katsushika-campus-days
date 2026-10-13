"""書き出した FBX を読み直し、歩き・走りの腕と胴の動きを実測する。

角度はボーンの向きから測る。上腕は肩 → 肘、前腕は肘 → 手首のベクトルで、

- 胸から見た角度: UpperChest のレストからの回転を外した向き（胴のひねりと前傾を含めない）
- ワールドの角度: アーマチュアの軸のまま（胴のひねりと前傾を含む。画面で見える振り）

の 2 通りを出す。両者の差が、胴の回旋・前傾が腕の振りに足している分になる。

使い方:
  "/c/Program Files/Blender Foundation/Blender 4.5/blender.exe" -b \
      --python blender/tools_view/gait_arms.py -- \
      --fbx-dir unity/KatsushikaCampusDays/Assets/Models/Characters \
      --ids mirai,botchan --json out.json

出力（キャラ → Action → 値）:
  upper_chest / upper_world   上腕の前後の角度（前が正、鉛直下向きが 0）の min / max / 振幅
  elbow                       肘の曲がり（0 = 伸び切り）の min / max
  fore_world                  前腕の前後の角度の min / max / 振幅
  hand_fb_chest / hand_fb_world  手首の前後の移動量（m）
  hand_fb_noyaw               胸の水平回旋だけを外した手首の前後の移動量（m）。world との差が回旋の寄与
  hand_over_bust              手首がバストの高さを超えた最大量（m、胸から見た高さ。負なら下）
  hand_cross                  手首が体の中心線を越えた最大量（m、胸から見た横位置。負なら越えない）
  hand_z_max                  手首のワールドの最高 Z（m）
  chest_yaw / pelvis_yaw / shoulder_yaw  胸・骨盤・肩の線の水平回旋の振幅（度）
  shoulder_fb                 肩関節（上腕の根元）の前後の移動量（m）
"""

from __future__ import annotations

import argparse
import json
import math
import os
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
sys.path.insert(0, os.path.join(ROOT, "blender"))

import bpy  # noqa: E402
from mathutils import Matrix, Vector  # noqa: E402

from kcd_chara import anim, body, params, rig  # noqa: E402

FORWARD = Vector((0.0, -1.0, 0.0))


def parse_args(argv):
    argv = argv[argv.index("--") + 1:] if "--" in argv else []
    ap = argparse.ArgumentParser(prog="gait_arms.py")
    ap.add_argument("--fbx-dir", default=os.path.join(
        ROOT, "unity", "KatsushikaCampusDays", "Assets", "Models", "Characters"))
    ap.add_argument("--ids", default=",".join(params.ALL_IDS))
    ap.add_argument("--actions", default="Walk,Run")
    ap.add_argument("--json", default="")
    ap.add_argument("--build", action="store_true",
                    help="FBX を読まず、アーマチュアと Action だけをその場で組んで測る（調整用。メッシュは作らない）")
    return ap.parse_args(argv)


def _span(vals, nd=2):
    lo, hi = min(vals), max(vals)
    return dict(min=round(lo, nd), max=round(hi, nd), amp=round(hi - lo, nd))


def _pitch(v) -> float:
    """横から見て鉛直下向きから前（-y）へ何度か。"""
    return math.degrees(math.atan2(-v.y, -v.z))


def _yaw(v) -> float:
    """上から見た向き（度）。"""
    return math.degrees(math.atan2(v.x, -v.y))


def _bust_below_upper_chest(cid: str) -> float:
    """UpperChest の頭からバストの高さまでの落差（m）。rig.spine_z の式から。"""
    p = params.resolve(cid)
    z = p["z"]
    return rig.spine_z(p)["UpperChest"] - z["bust"]


def measure(arm_obj, action, bust_drop: float) -> dict:
    ad = arm_obj.animation_data or arm_obj.animation_data_create()
    ad.action = action
    try:
        ad.action_slot = ad.action_suitable_slots[0]
    except Exception:  # noqa: BLE001 - 4.3 以前はスロットが無い
        pass
    scene = bpy.context.scene
    lo, hi = (int(f) for f in action.frame_range)
    frames = range(lo, hi)              # 最終フレームは先頭と同じ（ループ）
    W = arm_obj.matrix_world
    W3 = W.to_3x3()
    pb = arm_obj.pose.bones
    uc = pb["UpperChest"]
    uc_rest = uc.bone.matrix_local.to_3x3()
    rows: dict[str, list[float]] = {}

    def put(k, v):
        rows.setdefault(k, []).append(v)

    for f in frames:
        scene.frame_set(f)
        C = (W3 @ uc.matrix.to_3x3() @ uc_rest.inverted()).normalized()
        Ci = C.inverted()
        uc_head = W @ uc.head
        cy = _yaw(C @ FORWARD)
        put("chest_yaw", cy)
        # 胸の水平回旋だけを外す回転（前傾は残す）
        Y = Matrix.Rotation(-math.radians(cy), 3, "Z")
        hips = pb["Hips"]
        H = (W3 @ hips.matrix.to_3x3() @ hips.bone.matrix_local.to_3x3().inverted())
        put("pelvis_yaw", _yaw(H @ FORWARD))
        shl = W @ pb["LeftUpperArm"].head
        shr = W @ pb["RightUpperArm"].head
        d = shl - shr
        put("shoulder_yaw", math.degrees(math.atan2(d.y, d.x)))
        for side, sgn in (("Left", 1.0), ("Right", -1.0)):
            sh = W @ pb[side + "UpperArm"].head
            el = W @ pb[side + "LowerArm"].head
            wr = W @ pb[side + "Hand"].head
            u, fo = el - sh, wr - el
            put(side + "_upper_world", _pitch(u))
            put(side + "_upper_chest", _pitch(Ci @ u))
            put(side + "_fore_world", _pitch(fo))
            put(side + "_elbow", math.degrees(u.angle(fo)))
            put(side + "_hand_y_world", wr.y)
            put(side + "_shoulder_y", sh.y)
            put(side + "_hand_z", wr.z)
            hc = Ci @ (wr - uc_head)
            put(side + "_hand_y_chest", hc.y)
            put(side + "_hand_y_noyaw", (Y @ (wr - uc_head)).y)
            put(side + "_hand_over_bust", hc.z + bust_drop)
            put(side + "_hand_cross", -sgn * hc.x)

    out = {k: _span(rows[k]) for k in ("chest_yaw", "pelvis_yaw", "shoulder_yaw")}
    for side in ("Left", "Right"):
        s = {
            "upper_chest": _span(rows[side + "_upper_chest"]),
            "upper_world": _span(rows[side + "_upper_world"]),
            "fore_world": _span(rows[side + "_fore_world"]),
            "elbow": _span(rows[side + "_elbow"]),
            "hand_fb_world": _span(rows[side + "_hand_y_world"], 3)["amp"],
            "hand_fb_chest": _span(rows[side + "_hand_y_chest"], 3)["amp"],
            "hand_fb_noyaw": _span(rows[side + "_hand_y_noyaw"], 3)["amp"],
            "hand_over_bust": round(max(rows[side + "_hand_over_bust"]), 3),
            "hand_cross": round(max(rows[side + "_hand_cross"]), 3),
            "hand_z_max": round(max(rows[side + "_hand_z"]), 3),
            "shoulder_fb": _span(rows[side + "_shoulder_y"], 3)["amp"],
        }
        out[side] = s
    out["frames"] = len(frames)
    ad.action = None
    return out


def _build(cid: str):
    """build_characters.build_character と同じ引数でアーマチュアと Action だけを作る。"""
    p = params.resolve(cid)
    arm, _ = rig.build_armature(p, body.Anatomy(p), cid)
    anim.build_actions(arm, body.arm_drop(p), p.get("gait"), body.elbow_open(p),
                       body.arm_angles(p)[0])
    return arm


def main():
    args = parse_args(sys.argv)
    result = {}
    for cid in [c for c in args.ids.split(",") if c]:
        bpy.ops.wm.read_homefile(use_empty=True)
        if args.build:
            arm = _build(cid)
        else:
            path = os.path.join(os.path.abspath(args.fbx_dir), cid, cid + ".fbx")
            bpy.ops.import_scene.fbx(filepath=path)
            arm = next(o for o in bpy.context.scene.objects if o.type == "ARMATURE")
        drop = _bust_below_upper_chest(cid)
        result[cid] = {}
        for name in [a for a in args.actions.split(",") if a]:
            act = next((a for a in bpy.data.actions
                        if a.name.rsplit("|", 1)[-1] == name), None)
            if act is None:
                raise SystemExit("%s に %s の Action が無い" % (cid, name))
            result[cid][name] = measure(arm, act, drop)
    text = json.dumps(result, ensure_ascii=False, indent=1)
    if args.json:
        with open(args.json, "w", encoding="utf-8", newline="\n") as fh:
            fh.write(text + "\n")
    print("gait_arms: %d キャラ" % len(result))


main()
