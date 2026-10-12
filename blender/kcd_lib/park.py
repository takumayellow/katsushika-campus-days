"""にいじゅくみらい公園（キャンパスの南西の A 街区と北東の E 街区）の池・せせらぎ・木・遊具 (#168)。

配置は data/osm/park.json（tools/park_layout.py が OSM と葛飾区の「公園全体図」から作る）。
座標は campus.json と同じ (x = 東, y = 北)。

池とせせらぎは浅い水で、歩いて入れる。水面（site_water）には当たり判定が無く、底（site_ground）を歩く。
OSM で建物になっている地球釜とパーゴラは、8 m と 3 m の箱ではなく形を作って site_props_park に置く。
"""

import json
import math
import os

from . import geom, props, site

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
PARK_JSON = os.path.join(ROOT, "data", "osm", "park.json")

# 池とせせらぎの底。歩道（Z_FOOT 0.018）より上で水面（Z_WATER 0.060）より下。芝（Z_PARK）からは 2.1 cm 上がる。
Z_BED = 0.024
BED_MAT = "stone_dark"

# 園路が水を渡るところの木の橋。天端は水面の 2 cm 上、両端は BRIDGE_RAMP の坂で歩道へ下りる。
BRIDGE_Z = site.Z_WATER + 0.02
BRIDGE_RAMP = 1.5        # 坂の長さ [m]（6 cm を上る 4 % の勾配。段差にしない。#30）
BRIDGE_MARGIN = 0.5      # 水際から坂の上端までの平らな部分 [m]
BRIDGE_WIDEN = 0.4       # 園路より広げる幅 [m]
RAIL_H = 0.85

KAMA_H = 1.0             # 地球釜の縁の高さ [m]（全体図に高さは無い。腰の高さの水鉢として置く）
KAMA_WALL = 0.18
KAMA_SEG = 24

PERGOLA_H = 2.8
PLAY_CLEAR = 7.0         # 遊具のまわりで木の幹を置かない半径 [m]（葉が遊具に被らないように）
BENCH_STEP = 28.0        # 公園の園路沿いのベンチの間隔 [m]
PATH_KINDS = ("footway", "path", "pedestrian", "cycleway", "steps")

# 全体図の木の種類 → ゲームの樹種（props.TREE_SPECIES の番号）と大きさ。
# ゲームに季節が無いので、桜は花を付けず、けやきを小さくして置く。
KEYAKI, ROUND, PINE = 0, 1, 2
MIN_TREE_GAP = 2.0       # 全体図の木どうしの最小間隔 [m]


def load(path=PARK_JSON):
    with open(path, encoding="utf-8") as fp:
        park = json.load(fp)
    park["water"] = [tuple(p) for p in park["water"]]
    park["pergola"]["footprint"] = [tuple(p) for p in park["pergola"]["footprint"]]
    return park


def apply_buildings(data, park):
    """背景ビルの高さを公園の実物に合わせ、形を作り直す地球釜とパーゴラを外した data を返す（元は変えない）。"""
    drop = {park["kama"]["id"], park["pergola"]["id"]}
    heights = park.get("heights", {})
    blds = []
    for b in data["buildings"]:
        if b["id"] in drop:
            continue
        blds.append(dict(b, height=heights[b["id"]]) if b["id"] in heights else b)
    return dict(data, buildings=blds)


def water_poly(park):
    return geom.ensure_ccw(geom.dedup(park["water"]))


def park_polys(data):
    return [geom.ensure_ccw(geom.dedup(a["polygon"])) for a in data["areas"] if a["kind"] == "park"]


# --------------------------------------------------------------------------- #
#  池とせせらぎ


def build_bed(mb, park, occ, hard):
    """水の底（site_ground）。木・ベンチを水の中に置かないよう占有にも入れる。"""
    w = water_poly(park)
    mb.add_ngon_flat(w, Z_BED, BED_MAT)
    occ.stamp_poly(w, margin=1.5)
    hard.stamp_poly(w, margin=0.5)


def build_water(mb, park):
    """水面（site_water）。Unity では当たり判定も NavMesh も付かない（CampusStage.IsWaterMesh）。"""
    mb.add_ngon_flat(water_poly(park), site.Z_WATER, "water")


def bridge_spans(data, park, step=0.5):
    """園路が水を渡る区間を (入口, 出口, 園路の幅) で返す。両端は水際から BRIDGE_MARGIN だけ外。"""
    w = water_poly(park)
    spans = []
    for p in data["paths"]:
        if p["kind"] not in PATH_KINDS:
            continue
        pts = geom.resample(p["points"], step)
        inside = [geom.point_in_poly(q, w) for q in pts]
        i = 0
        while i < len(pts):
            if not inside[i]:
                i += 1
                continue
            j = i
            while j + 1 < len(pts) and inside[j + 1]:
                j += 1
            a, b = pts[max(i - 1, 0)], pts[min(j + 1, len(pts) - 1)]
            d = geom.normalize(geom.sub(b, a))
            if d != (0.0, 0.0):
                spans.append((geom.add(a, geom.mul(d, -BRIDGE_MARGIN)),
                              geom.add(b, geom.mul(d, BRIDGE_MARGIN)),
                              float(p.get("width") or 2.5)))
            i = j + 1
    return spans


def _deck_profile(a, b):
    """橋の中心線の点 (x, y, z)。坂の下端は歩道の 2 mm 上（同じ高さだと面がちらつく）。"""
    d = geom.normalize(geom.sub(b, a))
    z0 = site.Z_FOOT + 0.002
    return [geom.add(a, geom.mul(d, -BRIDGE_RAMP)) + (z0,), tuple(a) + (BRIDGE_Z,),
            tuple(b) + (BRIDGE_Z,), geom.add(b, geom.mul(d, BRIDGE_RAMP)) + (z0,)]


def build_bridge(ground_mb, props_mb, a, b, width, hard):
    """板の橋。床（坂を含む）は site_ground、手すりは site_props_park。"""
    prof = _deck_profile(a, b)
    d = geom.normalize(geom.sub(b, a))
    n = (-d[1], d[0])
    hw = width * 0.5 + BRIDGE_WIDEN * 0.5
    left = [(p[0] + n[0] * hw, p[1] + n[1] * hw, p[2]) for p in prof]
    right = [(p[0] - n[0] * hw, p[1] - n[1] * hw, p[2]) for p in prof]
    for i in range(len(prof) - 1):
        la, lb, ra, rb = left[i], left[i + 1], right[i], right[i + 1]
        # 天面（上から見て反時計回り）と左右の側面（地面まで。外を向く）。add_ribbon と同じ巻き方
        ground_mb.add_quad(ra, rb, lb, la, "wood")
        ground_mb.add_quad(la, lb, lb[:2] + (site.Z_GROUND,), la[:2] + (site.Z_GROUND,), "wood")
        ground_mb.add_quad(ra[:2] + (site.Z_GROUND,), rb[:2] + (site.Z_GROUND,), rb, ra, "wood")
    ang = math.atan2(d[1], d[0])
    length = geom.length(geom.sub(b, a))
    for side in (-1.0, 1.0):
        v0 = side * hw - (0.08 if side > 0 else 0.0)
        props.add_box_rot(props_mb, a[0], a[1], ang, 0.0, v0, BRIDGE_Z + RAIL_H - 0.08,
                          length, v0 + 0.08, BRIDGE_Z + RAIL_H, "wood")
        k = max(1, int(length / 1.2))
        for i in range(k + 1):
            u = length * i / k
            props.add_box_rot(props_mb, a[0], a[1], ang, u - 0.05, v0, BRIDGE_Z,
                              u + 0.05, v0 + 0.08, BRIDGE_Z + RAIL_H - 0.08, "wood")
    hard.stamp_poly([p[:2] for p in left] + [p[:2] for p in reversed(right)], margin=0.5)


# --------------------------------------------------------------------------- #
#  地球釜・パーゴラ・遊具


def build_kama(mb, park, occ, hard):
    """地球釜: 煉瓦の丸い釜に水を張ったもの。"""
    k = park["kama"]
    x, y, r = k["x"], k["z"], k["r"]
    ri = r - KAMA_WALL
    for i in range(KAMA_SEG):
        t0 = 2.0 * math.pi * i / KAMA_SEG
        t1 = 2.0 * math.pi * (i + 1) / KAMA_SEG
        c0, s0, c1, s1 = math.cos(t0), math.sin(t0), math.cos(t1), math.sin(t1)
        wedge = [(x + r * c0, y + r * s0), (x + r * c1, y + r * s1),
                 (x + ri * c1, y + ri * s1), (x + ri * c0, y + ri * s0)]
        mb.add_prism(wedge, 0.0, KAMA_H, "brick_red", "brick_red", None)
    disc = [(x + ri * math.cos(2.0 * math.pi * i / KAMA_SEG), y + ri * math.sin(2.0 * math.pi * i / KAMA_SEG))
            for i in range(KAMA_SEG)]
    mb.add_ngon_flat(disc, KAMA_H - 0.15, "water")
    occ.stamp_disc(x, y, r + 1.5)
    hard.stamp_disc(x, y, r + 0.8)


def _rect_frame(poly):
    """外形の長い辺に沿った向きと、その向きで測った外接矩形 (原点, 角度, 長さ, 幅)。"""
    i = geom.longest_edge(poly)
    a, b = poly[i], poly[(i + 1) % len(poly)]
    ang = math.atan2(b[1] - a[1], b[0] - a[0])
    c, s = math.cos(ang), math.sin(ang)
    us = [c * (p[0] - a[0]) + s * (p[1] - a[1]) for p in poly]
    vs = [-s * (p[0] - a[0]) + c * (p[1] - a[1]) for p in poly]
    u0, v0 = min(us), min(vs)
    o = (a[0] + c * u0 - s * v0, a[1] + s * u0 + c * v0)
    return o, ang, max(us) - u0, max(vs) - v0


def build_pergola(mb, park, occ, hard):
    """パーゴラ: 柱と梁の上に細い角材を並べた日よけ。外形は OSM の建物のもの。"""
    poly = geom.ensure_ccw(geom.dedup(park["pergola"]["footprint"]))
    o, ang, L, W = _rect_frame(poly)
    post, beam = 0.16, 0.20
    n = max(1, int(round(L / 3.0)))
    for i in range(n + 1):
        u = (L - post) * i / n
        for v in (0.0, W - post):
            props.add_box_rot(mb, o[0], o[1], ang, u, v, 0.0, u + post, v + post, PERGOLA_H - beam, "wood")
    for v in (0.0, W - post):
        props.add_box_rot(mb, o[0], o[1], ang, -0.3, v, PERGOLA_H - beam, L + 0.3, v + post, PERGOLA_H, "wood")
    k = int(L / 0.5)
    for i in range(k + 1):
        u = (L - 0.06) * i / k
        props.add_box_rot(mb, o[0], o[1], ang, u, -0.3, PERGOLA_H, u + 0.06, W + 0.3, PERGOLA_H + 0.12, "wood")
    occ.stamp_poly(poly, margin=1.5)
    hard.stamp_poly(poly, margin=0.8)


def add_swing(mb, x, y, ang):
    """2 人乗りのぶらんこ（幅 3.2 m、高さ 2.4 m）。"""
    for du in (-1.6, 1.6):
        for dv in (-0.8, 0.8):
            # 脚を A 字に開く代わりに、上端を中央へ寄せた細い柱 2 本
            props.add_box_rot(mb, x, y, ang, du - 0.05, dv * 0.95 - 0.05, 0.0,
                              du + 0.05, dv * 0.95 + 0.05, 2.4, "metal_grey")
    props.add_box_rot(mb, x, y, ang, -1.7, -0.06, 2.35, 1.7, 0.06, 2.47, "metal_grey")
    for du in (-0.8, 0.8):
        for s in (-0.22, 0.22):
            props.add_box_rot(mb, x, y, ang, du + s - 0.01, -0.01, 0.45, du + s + 0.01, 0.01, 2.35, "metal_white")
        props.add_box_rot(mb, x, y, ang, du - 0.25, -0.12, 0.40, du + 0.25, 0.12, 0.45, "vending_red")


def add_slide(mb, x, y, ang):
    """すべり台（台の高さ 1.5 m、長さ 4 m）。"""
    h = 1.5
    props.add_box_rot(mb, x, y, ang, -0.6, -0.6, h - 0.08, 0.6, 0.6, h, "wood")
    for du in (-0.55, 0.5):
        for dv in (-0.55, 0.5):
            props.add_box_rot(mb, x, y, ang, du, dv, 0.0, du + 0.05, dv + 0.05, h, "metal_grey")
    for i in range(5):
        z = h * (i + 1) / 6.0
        props.add_box_rot(mb, x, y, ang, -0.6 - 0.05, -0.4, z - 0.03, -0.6, 0.4, z, "metal_grey")
    c, s = math.cos(ang), math.sin(ang)

    def P(du, dv, z):
        return (x + c * du - s * dv, y + s * du + c * dv, z)

    # 滑る面（上を向く）と両脇の縁
    mb.add_quad(P(0.6, -0.35, h), P(4.0, -0.35, 0.25), P(4.0, 0.35, 0.25), P(0.6, 0.35, h), "vending_blue")
    mb.add_quad(P(0.6, 0.35, h - 0.06), P(4.0, 0.35, 0.19), P(4.0, -0.35, 0.19), P(0.6, -0.35, h - 0.06),
                "vending_blue")
    for dv in (-0.35, 0.35):
        o = 0.05 if dv > 0 else -0.05
        mb.add_quad(P(0.6, dv, h), P(4.0, dv, 0.25), P(4.0, dv, 0.40), P(0.6, dv, h + 0.15), "vending_blue")
        mb.add_quad(P(0.6, dv + o, h + 0.15), P(4.0, dv + o, 0.40), P(4.0, dv + o, 0.25), P(0.6, dv + o, h),
                    "vending_blue")
    props.add_box_rot(mb, x, y, ang, 3.9, -0.35, 0.0, 4.0, 0.35, 0.25, "metal_grey")


def build_play(mb, park, occ, hard):
    """遊具。1 か所目はぶらんこ、2 か所目はすべり台、3 か所目（砂場の遊び場）は両方。"""
    placed = []
    for i, (x, y) in enumerate(park["play"]):
        if hard.blocked(x, y):
            print("[park] play spot (%.0f, %.0f) is blocked; skipped" % (x, y))
            continue
        if i == 0:
            add_swing(mb, x, y, 0.0)
        elif i == 1:
            add_slide(mb, x - 2.0, y, 0.0)
        else:
            add_swing(mb, x, y + 3.0, 0.0)
            add_slide(mb, x - 2.0, y - 3.0, 0.0)
        occ.stamp_disc(x, y, PLAY_CLEAR + 1.0)
        hard.stamp_disc(x, y, PLAY_CLEAR)
        placed.append((x, y))
    return placed


def build_bridges(ground_mb, props_mb, data, park, hard):
    """園路が水を渡るところに橋を架ける。架けた数を返す。"""
    spans = bridge_spans(data, park)
    for a, b, w in spans:
        build_bridge(ground_mb, props_mb, a, b, w, hard)
    return len(spans)


def build_props(mb, park, occ, hard):
    """地球釜・パーゴラ・遊具を置く。建物を占有に入れた後に呼ぶ（遊具の場所が建物に掛かれば置かない）。"""
    build_kama(mb, park, occ, hard)
    build_pergola(mb, park, occ, hard)
    return len(build_play(mb, park, occ, hard))


# --------------------------------------------------------------------------- #
#  ベンチ・照明・木


def build_furniture(mb, data, hard, step=BENCH_STEP):
    """公園の園路沿いにベンチと照明柱を交互の側に置く（site_furniture）。置いた数を返す。"""
    parks = park_polys(data)
    n_bench = n_lamp = 0
    k = 0
    for p in data["paths"]:
        if p["kind"] not in PATH_KINDS:
            continue
        w = float(p.get("width") or 2.5)
        pts = geom.resample(p["points"], step)
        for i in range(1, len(pts) - 1):
            q = pts[i]
            if not any(geom.point_in_poly(q, poly) for poly in parks):
                continue
            d = geom.normalize(geom.sub(pts[i + 1], pts[i - 1]))
            if d == (0.0, 0.0):
                continue
            side = 1.0 if k % 2 == 0 else -1.0
            k += 1
            n = (-d[1] * side, d[0] * side)
            b = geom.add(q, geom.mul(n, w * 0.5 + 0.9))
            lamp = geom.add(q, geom.mul(n, -(w * 0.5 + 0.5)))
            if not hard.blocked(b[0], b[1]):
                # 背もたれ（ローカル +v）を園路の外へ向け、園路を向いて座る
                props.add_bench(mb, b[0], b[1], math.atan2(-n[0], n[1]))
                hard.stamp_disc(b[0], b[1], 1.0)
                n_bench += 1
            if k % 2 == 0 and not hard.blocked(lamp[0], lamp[1]):
                props.add_lamp(mb, lamp[0], lamp[1], 4.0)
                hard.stamp_disc(lamp[0], lamp[1], 0.5)
                n_lamp += 1
    return n_bench, n_lamp


def tree_species(kind, rng):
    """全体図の木の種類 → (樹種の番号, 大きさ)。"""
    if kind == "sakura":
        return KEYAKI, rng.uniform(0.72, 0.85)
    if kind == "evergreen":
        return (PINE if rng.random() < 0.25 else ROUND), rng.uniform(0.85, 1.05)
    return KEYAKI, rng.uniform(0.85, 1.1)


def park_trees(park, hard, rng):
    """全体図の木を (x, y, 樹種, 大きさ, 回転) で返す。建物・水・橋・遊具の上と、近すぎる木は除く。"""
    out = []
    for x, y, kind in park["trees"]:
        sp, sc = tree_species(kind, rng)
        rot = rng.uniform(0.0, 2.0 * math.pi)
        if hard.blocked(x, y):
            continue
        if any((x - t[0]) ** 2 + (y - t[1]) ** 2 < MIN_TREE_GAP ** 2 for t in out):
            continue
        out.append((x, y, sp, sc, rot))
    return out
