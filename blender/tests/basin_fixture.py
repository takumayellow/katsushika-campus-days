"""堀の縁の答え合わせ用 JSON（blender/tests/fixtures/basin_edges.json）を作る (#68)。

水際の辺の計算は Blender 側 kcd_lib.site.basin_edges と Unity 側 CampusStage.BasinEdges の
2 か所にある。ここで Python の答えを JSON に書き、
  - pytest（test_site.py）が JSON と今の site.py の答えが同じかを見る
  - EditMode テスト（BasinEdgeAgreementTests.cs）が同じ入力を CampusStage.BasinEdges に通して
    JSON と同じ辺が同じ順で出るかを見る
ので、どちらか片方だけを直すとどちらかのテストが落ちる。

site.BASINS か basin_edges を変えたら、作り直してコミットする:
  python blender/tests/basin_fixture.py
"""

import json
import os
import re
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
BLENDER = os.path.dirname(HERE)
FIXTURE = os.path.join(HERE, "fixtures", "basin_edges.json")

for _p in (BLENDER, HERE):
    if _p not in sys.path:
        sys.path.insert(0, _p)

import stubs  # noqa: E402

stubs.install()

from kcd_lib import site  # noqa: E402  (site → props → mesh が bpy を import する)

# (名前, 矩形 (u0, v0, u1, v1) のリスト, 何を確かめる形か)
SYNTHETIC = [
    ("single", [(0.0, 0.0, 10.0, 4.0)],
     "1 枚だけ。4 辺そのままで、角はすべて延ばす"),
    ("l_shape", [(0.0, 0.0, 10.0, 4.0), (0.0, 4.0, 4.0, 12.0)],
     "L 字。接する辺を引き、入隅（内側の角）は延ばさない"),
    ("t_shape", [(0.0, 0.0, 12.0, 4.0), (4.0, 4.0, 8.0, 10.0)],
     "T 字。1 本の辺が中ほどを覆われて 2 本に割れる"),
    ("partial_share", [(0.0, 0.0, 10.0, 4.0), (6.0, 4.0, 16.0, 8.0)],
     "ずれて接する。互いの辺の一部だけが消える"),
    ("full_share", [(0.0, 0.0, 10.0, 4.0), (0.0, 4.0, 10.0, 8.0)],
     "同じ幅で接する。継ぎ目の辺は両側とも消える"),
    ("side_by_side", [(0.0, 0.0, 4.0, 4.0), (4.0, 0.0, 8.0, 4.0)],
     "u 方向に並ぶ。外周の辺は同じ直線上で 2 本に分かれたまま残る"),
    ("near_touch", [(0.0, 0.0, 10.0, 4.0), (0.0, 4.00005, 10.0, 8.0)],
     "5e-5 m の隙間は許容差（1e-4）の内なので、接しているとみなす"),
    ("near_miss", [(0.0, 0.0, 10.0, 4.0), (0.0, 4.001, 10.0, 8.0)],
     "1 mm の隙間は許容差の外なので、両方の辺が残る"),
    ("sliver", [(0.0, 0.0, 10.0, 4.0), (0.00005, 4.0, 10.0, 8.0)],
     "覆われ残りが 5e-5 m の切れ端は捨てる"),
    ("library_moat", [(-59.5, -16.4, -50.0, 24.0), (-59.5, -78.0, -50.0, -31.6),
                      (-100.0, -78.0, -59.5, -66.0), (-100.0, -66.0, -90.0, -40.0)],
     "今の堀に西の帯を足した U 字。入隅が 2 か所"),
]


def _edge(e):
    axis, c, t0, t1, out, ext0, ext1 = e
    return {"axis": axis, "c": c, "t0": t0, "t1": t1, "out": out, "ext0": ext0, "ext1": ext1}


def _case(name, rects, note):
    return {
        "name": name,
        "note": note,
        "rects": [list(r) for r in rects],
        "edges": [_edge(e) for e in site.basin_edges(rects)],
    }


def build():
    """JSON に書く中身（dict）。1 件目は今の site.BASINS。"""
    cases = [_case("basins", site.BASINS, "site.BASINS そのもの（CampusStage.Basins と同じはず）")]
    cases += [_case(*c) for c in SYNTHETIC]
    return {
        "about": "site.basin_edges の答え。python blender/tests/basin_fixture.py で作り直す (#68)",
        "rim": site.BASIN_RIM,
        "eps": site.BASIN_EDGE_EPS,
        "cases": cases,
    }


# 入れ子の無い [..] / {..} を 1 行にまとめる（辺 1 本・矩形 1 枚が 1 行になって差分が読みやすい）
_FLAT = re.compile(r"([\[{])\n\s+([^\[\]{}]*?)\n\s*([\]}])")


def dumps(obj):
    text = json.dumps(obj, ensure_ascii=False, indent=1)
    text = _FLAT.sub(lambda m: m.group(1) + re.sub(r",\n\s+", ", ", m.group(2)) + m.group(3), text)
    return text + "\n"


def main():
    os.makedirs(os.path.dirname(FIXTURE), exist_ok=True)
    with open(FIXTURE, "w", encoding="utf-8", newline="\n") as fp:
        fp.write(dumps(build()))
    data = build()
    print("wrote %s (%d cases, %d edges)"
          % (FIXTURE, len(data["cases"]), sum(len(c["edges"]) for c in data["cases"])))


if __name__ == "__main__":
    main()
