"""書き出し済みの屋内 FBX を読み戻し、外周から外へ抜けられる穴が無いかを確かめる（#45）。

使い方:
  "/c/Program Files/Blender Foundation/Blender 4.5/blender.exe" -b \
      --python blender/check_interior_walls.py -- --ids all

判定のしかたは kcd_interior/closure.py の冒頭を参照。外周に沿って --max-gap
（既定 0.25 m）より長い穴が 1 つでもあれば exit 1。--two-sided を付けると面の向きを
問わずに当てる（片面の当たり判定が原因かどうかを見分ける比較用）。
"""

import argparse
import json
import os
import sys
import time

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

import bpy  # noqa: E402

from kcd_interior import closure, registry  # noqa: E402


def parse_args(argv):
    argv = argv[argv.index("--") + 1:] if "--" in argv else []
    root = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
    p = argparse.ArgumentParser(prog="check_interior_walls.py")
    p.add_argument("--dir",
                   default=os.path.join(root, "unity", "KatsushikaCampusDays",
                                        "Assets", "Models", "Interiors"))
    p.add_argument("--ids", default="all")
    p.add_argument("--max-gap", type=float, default=0.25,
                   help="これより長い穴があれば失敗（m）")
    p.add_argument("--step", type=float, default=0.05)
    p.add_argument("--two-sided", action="store_true",
                   help="面の向きを問わずに当てる（比較用）")
    p.add_argument("--report", default="",
                   help="結果を JSON で書き出す先（省略可）")
    p.add_argument("--verbose", action="store_true",
                   help="穴を 1 つずつ出す（既定は辺と床の高さごとにまとめる）")
    return p.parse_args(argv)


def load(path):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=path)
    return [o for o in bpy.context.scene.objects if o.type == "MESH"]


def check(bid, fbx, meta, max_gap, two_sided=False, step=0.05):
    """1 棟ぶん。(穴の列, 失敗扱いの穴の列) を返す。"""
    gaps = closure.measure(load(fbx), meta, two_sided=two_sided, step=step)
    bad = [g for g in gaps if g["width"] > max_gap]
    return gaps, bad


def main():
    args = parse_args(sys.argv)
    ids = registry.ORDER if args.ids == "all" else \
        [s.strip() for s in args.ids.split(",") if s.strip()]
    mode = "両面" if args.two_sided else "片面（PhysX と同じ）"
    print("[walls] 当たり判定=%s  穴の許容=%.2f m  刻み=%.2f m"
          % (mode, args.max_gap, args.step))
    ok = True
    report = {}
    t_all = time.time()
    for bid in ids:
        fbx = os.path.join(args.dir, "%s.fbx" % bid)
        side = os.path.join(args.dir, "%s.json" % bid)
        if not (os.path.isfile(fbx) and os.path.isfile(side)):
            print("[walls] NG %-11s FBX か JSON がありません" % bid)
            ok = False
            continue
        with open(side, encoding="utf-8") as fp:
            meta = json.load(fp)
        t0 = time.time()
        gaps, bad = check(bid, fbx, meta, args.max_gap, args.two_sided,
                          args.step)
        total = sum(g["width"] for g in bad)
        longest = max((g["width"] for g in bad), default=0.0)
        levels = sorted({g["z"] for g in bad})
        print("[walls] %s %-11s 穴 %3d か所  計 %7.2f m  最大 %6.2f m  床 %s  (%.1f s)"
              % ("OK" if not bad else "NG", bid, len(bad), total, longest,
                 ", ".join("%.1f" % z for z in levels) or "-",
                 time.time() - t0))
        if args.verbose:
            for g in bad:
                print("             %-8s z=%5.2f  (%.2f, %.2f) -> (%.2f, %.2f)  %.2f m"
                      % (g["name"], g["z"], g["a"][0], g["a"][1], g["b"][0],
                         g["b"][1], g["width"]))
        else:
            # 方立で細切れになるので、辺と床の高さごとに「どこからどこまで」でまとめる
            groups = {}
            for g in bad:
                groups.setdefault((g["edge"], g["z"]), []).append(g)
            for (ei, z), gs in sorted(groups.items()):
                xs = [p[0] for g in gs for p in (g["a"], g["b"])]
                ys = [p[1] for g in gs for p in (g["a"], g["b"])]
                print("             %-8s z=%5.2f  x %.2f〜%.2f  y %.2f〜%.2f  "
                      "%d か所 計 %.2f m"
                      % (gs[0]["name"], z, min(xs), max(xs), min(ys), max(ys),
                         len(gs), sum(g["width"] for g in gs)))
        if bad:
            ok = False
        report[bid] = {"gaps": bad, "count": len(bad),
                       "total_m": round(total, 2), "max_m": round(longest, 2)}
    print("[walls] %s  %d 棟 / %.1f s"
          % ("OK: 外周は閉じている" if ok else "NG: 外へ抜けられる穴がある",
             len(ids), time.time() - t_all))
    if args.report:
        with open(args.report, "w", encoding="utf-8") as fp:
            json.dump(report, fp, ensure_ascii=False, indent=1)
    if not ok:
        sys.exit(1)


if __name__ == "__main__":
    main()
