"""にいじゅくみらい公園（キャンパスの南西の A 街区と北東の E 街区）の配置を data/osm/park.json に書く (#168)。

入力:
  --osm  公園まわりの Overpass `out geom;` の出力（池とせせらぎの形、パーゴラ・地球釜の位置）
  --map  葛飾区の「公園全体図」PDF（kkk.pdf）の 2 ページ目を 300 dpi で画像にしたもの（木の位置と種類）
出力: data/osm/park.json（座標は campus.json と同じ x = 東, z = 北 [m]）

全体図は北が上の縮尺つきの絵で、OSM の園路と重ねて相似変換（拡大・回転・平行移動）を合わせてある
（GEOREF、園路上の点の平均誤差 0.86 m）。木の記号（円）を Hough 変換で拾い、ゲーム座標へ戻してから、
公園の外・水の中・建物の中・運動場の中・園路の上・文字の上のものを捨てる。手順の詳細は docs/ref/park.md。

使い方:
  python tools/park_layout.py --osm <park_all.json> --map <kkk_p2_300dpi.png>
"""
from __future__ import annotations

import argparse
import json
import math
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))

from osm_common import ROOT, load_elements, make_projector, point_in_poly  # noqa: E402

OUT = ROOT / "data" / "osm" / "park.json"
ORIGIN = (35.771909746153845, 139.862967261538472)

# ゲーム座標 (x, z) → 全体図 300 dpi の画素 (px, py)。
#   px = tx + s (cos th x - sin th z),  py = ty - s (sin th x + cos th z)
GEOREF = {"s": 4.6114316, "th": -0.0098694, "tx": 1510.2265, "ty": 1173.0397, "mean_error_m": 0.86}

PARK_WAYS = (492228028, 175463018)        # A 街区, E 街区（leisure=park）
WATER_WAY = 556082279                      # 池からせせらぎまでの水路（landuse=basin）
KAMA_WAY = 556082281                       # 地球釜
PERGOLA_WAY = 1552199144                   # パーゴラ（PLATEAU の建物）
PATH_KINDS = ("footway", "path", "pedestrian", "cycleway", "steps", "service")

# 全体図の文字（地名・施設名）の上に出る円を捨てる箱 (x0, z0, x1, z1)。桜（桃色）は文字と見分けが付くので残す。
LABELS = [
    (-160, -93, -122, -82),    # 見晴らしの丘
    (-38, -138, -15, -127),    # 原っぱ
    (0, -143, 36, -132),       # せせらぎ
    (-42, -116, -12, -106),    # パーゴラ
    (-73, -145, -51, -136),    # A 街区
    (-82, -158, -50, -150),    # 屋外トイレ棟
    (95, 125, 140, 137),       # 多目的広場
    (110, 90, 140, 100),       # E 街区
    (60, 145, 110, 152),       # 防災備蓄倉庫
    (186, 93, 206, 101),       # 遊具（テニスコートの北）
    (200, 24, 216, 32),        # 遊具（管理棟の東）
    (205, 0, 225, 75),         # 管理棟の東の木道（斜線）
]

# 地球釜（池の西の端の丸い煉瓦の釜）の中心と半径。OSM の外形は 8 x 13 m の四角で台座ごと囲っているので、
# 全体図の茶色の円（直径 約 4.8 m）から読む。
KAMA = (-131.0, -136.5, 2.4)

# 全体図から読んだ遊具の場所（記号の中心）と、OSM の遊び場で公園の中にあるもの。
PLAY_SPOTS = [(190.0, 97.0), (208.0, 36.0), (88.0, 188.0)]

# 背景ビルの高さ [m] の上書き。OSM に高さが無いものは osm_extract が 8 m にしている。
HEIGHTS = {
    "bg_771657621": 3.2,      # 屋外トイレ棟
    "bg_771657622": 3.2,      # 屋外トイレ棟
    "bg_687969466": 5.5,      # 管理棟（平屋。更衣室とトイレ）
    "bg_771657600": 4.5,      # 防災備蓄倉庫
}


def to_px(x: float, z: float, g: dict = GEOREF) -> tuple[float, float]:
    c, s = math.cos(g["th"]), math.sin(g["th"])
    return (g["tx"] + g["s"] * (c * x - s * z), g["ty"] - g["s"] * (s * x + c * z))


def to_game(px: float, py: float, g: dict = GEOREF) -> tuple[float, float]:
    c, s = math.cos(g["th"]), math.sin(g["th"])
    u, v = (px - g["tx"]) / g["s"], (g["ty"] - py) / g["s"]
    return (c * u + s * v, -s * u + c * v)


def classify(rgb) -> str:
    """木の記号の塗り（中央値の RGB）から種類を決める。桃色 = 桜、濃い緑 = 常緑樹、ほかは落葉樹。
    境の値は、全体図の各色の記号の塗りを測って決めた。"""
    r, g, b = rgb
    if b > g + 8 and r > g + 8:
        return "sakura"
    if g - r > 22 and g - b > 45 and g < 215:
        return "evergreen"
    return "deciduous"


def _seg_dist(p, a, b) -> float:
    ax, az = b[0] - a[0], b[1] - a[1]
    n = ax * ax + az * az
    t = 0.0 if n == 0 else max(0.0, min(1.0, ((p[0] - a[0]) * ax + (p[1] - a[1]) * az) / n))
    return math.hypot(p[0] - a[0] - t * ax, p[1] - a[1] - t * az)


def _in_box(p, box) -> bool:
    return box[0] <= p[0] <= box[2] and box[1] <= p[1] <= box[3]


def reject_reason(p, kind, parks, water, buildings, paths, pitches=(), path_clear=2.0):
    """木を置かない理由（置いてよければ None）。pitches は運動場・テニスコート（leisure=pitch）。"""
    if not any(point_in_poly(p[0], p[1], q) for q in parks):
        return "outside"
    if point_in_poly(p[0], p[1], water):
        return "water"
    if any(point_in_poly(p[0], p[1], q) for q in buildings):
        return "building"
    if any(point_in_poly(p[0], p[1], q) for q in pitches):
        return "pitch"
    if any(_seg_dist(p, a, b) < path_clear for line in paths for a, b in zip(line, line[1:])):
        return "path"
    if kind != "sakura" and any(_in_box(p, box) for box in LABELS):
        return "label"
    return None


def detect_circles(map_png: Path):
    """全体図の木の記号（半径 4〜11 px の円）を拾い、(px, py, 塗りの RGB) を返す。"""
    import cv2  # noqa: PLC0415 （ツールを使うときだけ要る）
    import numpy as np  # noqa: PLC0415

    bgr = cv2.imread(str(map_png))
    if bgr is None:
        raise SystemExit(f"{map_png} を読めない")
    rgb = bgr[..., ::-1].astype(int)
    gray = cv2.GaussianBlur(cv2.cvtColor(bgr, cv2.COLOR_BGR2GRAY), (3, 3), 0.8)
    # 閾値と最小間隔は、隣り合う木を 1 つにまとめず、文字の丸みを拾い過ぎない値を全体図で試して決めた。
    found = cv2.HoughCircles(gray, cv2.HOUGH_GRADIENT, dp=1, minDist=8, param1=90, param2=14,
                             minRadius=4, maxRadius=11)
    yy, xx = np.mgrid[-12:13, -12:13]
    d = np.hypot(xx, yy)
    out = []
    for x, y, r in ([] if found is None else found[0]):
        xi, yi = int(round(x)), int(round(y))
        if not (12 <= xi < rgb.shape[1] - 12 and 12 <= yi < rgb.shape[0] - 12):
            continue
        fill = rgb[yi - 12:yi + 13, xi - 12:xi + 13][(d > 0.3 * r) & (d < 0.7 * r)]
        out.append((float(x), float(y), tuple(int(v) for v in np.median(fill, 0))))
    return out


def _polygon(geo, way):
    pts = geo[way]
    return pts[:-1] if pts[0] == pts[-1] else pts


def build(elements, circles) -> dict:
    project = make_projector(*ORIGIN)
    geo = {e["id"]: [project(q["lon"], q["lat"]) for q in e["geometry"]]
           for e in elements if "geometry" in e}
    tags = {e["id"]: e.get("tags", {}) for e in elements}
    parks = [_polygon(geo, w) for w in PARK_WAYS]
    water = _polygon(geo, WATER_WAY)
    buildings = [_polygon(geo, w) for w in geo if tags[w].get("building")]
    paths = [geo[w] for w in geo if tags[w].get("highway") in PATH_KINDS]
    pitches = [_polygon(geo, w) for w in geo if tags[w].get("leisure") == "pitch"]

    trees, dropped = [], {}
    for px, py, rgb in circles:
        x, z = to_game(px, py)
        kind = classify(rgb)
        why = reject_reason((x, z), kind, parks, water, buildings, paths, pitches)
        if why:
            dropped[why] = dropped.get(why, 0) + 1
            continue
        trees.append([round(x, 1), round(z, 1), kind])
    trees.sort()

    return {
        "meta": {
            "issue": 168,
            "sources": [
                "OpenStreetMap contributors (ODbL): 公園・水路・建物・園路",
                "葛飾区「にいじゅくみらい公園 公園全体図」: 木の位置と種類、遊具の場所",
            ],
            "georef": GEOREF,
            "dropped": dict(sorted(dropped.items())),
        },
        "water": [[round(x, 2), round(z, 2)] for x, z in water],
        "trees": trees,
        "kama": {"id": f"bg_{KAMA_WAY}", "x": KAMA[0], "z": KAMA[1], "r": KAMA[2]},
        "pergola": {"id": f"bg_{PERGOLA_WAY}",
                    "footprint": [[round(x, 2), round(z, 2)] for x, z in _polygon(geo, PERGOLA_WAY)]},
        "play": [list(p) for p in PLAY_SPOTS],
        "heights": HEIGHTS,
    }


def main(argv=None) -> int:
    ap = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    ap.add_argument("--osm", type=Path, required=True)
    ap.add_argument("--map", type=Path, required=True)
    ap.add_argument("--out", type=Path, default=OUT)
    a = ap.parse_args(argv)
    data = build(load_elements(a.osm), detect_circles(a.map))
    a.out.write_text(json.dumps(data, ensure_ascii=False, indent=1) + "\n", encoding="utf-8")
    kinds = {}
    for t in data["trees"]:
        kinds[t[2]] = kinds.get(t[2], 0) + 1
    print(f"{a.out}: 木 {len(data['trees'])} 本 {kinds}、捨てた円 {data['meta']['dropped']}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
