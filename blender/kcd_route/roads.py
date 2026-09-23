"""回廊の道路（#41）。

見た目は kcd_lib/site.py の build_paths とそろえる。マテリアル名も高さレイヤも同じ。
違うのは 2 つだけ:
  * キャンパス用の Occupancy（樹木散布の占有格子）を持たない
  * ±348 m でクリップしない（西の帯まで敷くため。帯は x = -560 まである）
"""

from kcd_lib import geom

from .ground import PATH_THICKNESS, Z_FOOT, Z_LINE, Z_ROAD, dedup, road_width

# 舗装のマテリアルと高さレイヤ
HARD_KINDS = ("tertiary", "secondary", "primary", "residential", "unclassified",
              "service", "living_street")

DASH_PITCH = 9.0        # tertiary のセンターライン（破線）の間隔
DASH_RATIO = 0.45
LINE_W_DASH = 0.16
LINE_W_EDGE = 0.14
EDGE_INSET = 0.45       # 外側線を車道の縁から何 m 内側に引くか


def surface(kind):
    if kind in HARD_KINDS:
        return "asphalt", Z_ROAD
    return "stone_light", Z_FOOT


def _center_dashes(mb, pts):
    samples = geom.resample(pts, DASH_PITCH)
    n = 0
    for i in range(len(samples) - 1):
        a = samples[i]
        b = geom.lerp(samples[i], samples[i + 1], DASH_RATIO)
        mb.add_ribbon([a, b], LINE_W_DASH, Z_LINE, "line_white")
        n += 1
    return n


def _edge_lines(mb, pts, width):
    n = 0
    for side in (-1, 1):
        off = []
        for i, q in enumerate(pts):
            nxt = pts[min(i + 1, len(pts) - 1)]
            prv = pts[max(i - 1, 0)]
            d = geom.normalize(geom.sub(nxt, prv))
            if d == (0.0, 0.0):
                continue
            nrm = (-d[1], d[0])
            off.append(geom.add(q, geom.mul(nrm, side * (width * 0.5 - EDGE_INSET))))
        if len(off) >= 2:
            mb.add_ribbon(off, LINE_W_EDGE, Z_LINE, "line_white")
            n += 1
    return n


def build_roads(mb, roads):
    """roads（route.json の road dict の列）を敷く。戻り値は報告用の dict。"""
    laid = 0
    length = 0.0
    dashes = 0
    lines = 0
    by_kind = {}
    for r in roads:
        pts = dedup(r["points"])
        if len(pts) < 2:
            continue
        w = road_width(r)
        mat, z = surface(r["kind"])
        mb.add_ribbon(pts, w, z, mat, thickness=PATH_THICKNESS)
        laid += 1
        length += sum(geom.length(geom.sub(pts[i + 1], pts[i])) for i in range(len(pts) - 1))
        by_kind[r["kind"]] = by_kind.get(r["kind"], 0) + 1
        # 白線は車道のうち幅の広いものだけ（歩道と路地には引かない）
        if r["kind"] in HARD_KINDS and w >= 7.0:
            dashes += _center_dashes(mb, pts)
            lines += _edge_lines(mb, pts, w)
    return {"laid": laid, "length_m": round(length, 1), "kinds": by_kind,
            "center_dashes": dashes, "edge_lines": lines}
