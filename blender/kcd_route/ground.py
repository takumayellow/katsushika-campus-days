"""route.json の読み取り・選別と、西へ足す地面の帯（#41）。

このモジュールは bpy を import しない。build_route.py を走らせる前に、素の Python で

    python -c "import sys; sys.path.insert(0, 'blender'); from kcd_route import ground; ground.report()"

と打てば、敷く道の本数・棟数・門の位置まで全部確かめられる（Blender は要らない）。
"""

import json
import math
import os

# --------------------------------------------------------------------------- #
#  高さレイヤ — kcd_lib/site.py の写し（あちらは props → mesh → bpy を引くので
#  素の Python から import できない）。build_route.py の check_layers() が
#  Blender 上で site.py の値と一致することを毎回確かめる。
# --------------------------------------------------------------------------- #
Z_GROUND = 0.000
Z_ROAD = 0.012
Z_LINE = 0.015
Z_FOOT = 0.018
PATH_THICKNESS = 0.06

# kcd_lib/site.py の build_ground が (-E..E)^2 を grass_dark で 1 枚張っている。
# 帯はこの外側だけを埋める（二重に敷かない）。
GROUND_E = 350.0

# 西へ足す地面の帯 (x0, z0, x1, z1)。
# 東端は GROUND_E ちょうど。天面も Z_GROUND ちょうど・マテリアルも同じ grass_dark なので、
# x = -350 の継ぎ目は「重なり 0・段差 0」になる。
# z は既存の地盤とまったく同じ範囲にして、継ぎ目が端から端までそろうようにする。
BAND = (-560.0, -GROUND_E, -GROUND_E, GROUND_E)
BAND_GRID = 30.0        # split_by_grid の目。どの三角形の辺も 50 m 以下になる（PhysX 対策）
BAND_MAX_EDGE = 50.0

# 歩ける西区画（Annex）と、Wall_West に開ける門。Unity 側 RouteStage.cs と 1 対 1。
# 変えたら route.json（サイドカー）経由で Unity に伝わる。
# Unity の Runtime/World/DormRoute.cs の Default* と 1 対 1（あちらは sidecar が無いときの
# 受け皿）。ずらすと門柱の位置と、見えない壁 Wall_West に開ける口がずれる。両方を直すこと。
ANNEX = (-490.0, 156.0, -340.0, 262.0)      # DefaultAnnex{XMin,ZMin,XMax,ZMax}
GATE_HALF = 12.0                            # DefaultGateHalfWidth
GATE_CENTER_Z = 239.83                      # DefaultGateZ（nw_gate が x=-340 を横切る z）
GATE_Z = (GATE_CENTER_Z - GATE_HALF, GATE_CENTER_Z + GATE_HALF)
WALL_X = -340.0         # = WorldBounds.HalfExtent。Annex の東端でもある

# 寮（別担当が建てる）。この矩形には何も置かない。
DORM_ID = "bld_community_house"
# 寮のフットプリントから何 m 空けるか。隣家は OSM どおり実際に 1 m まで寄っているので
# bbox ではなく多角形どうしの距離で測る。
DORM_KEEPOUT = 0.5

# 道の幅が route.json に無かったときの既定
WIDTH_FALLBACK = {"tertiary": 9.0, "residential": 6.0, "unclassified": 6.0,
                  "service": 4.0, "footway": 2.5, "path": 2.5}

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
DEFAULT_JSON = os.path.join(ROOT, "data", "osm", "route.json")


# --------------------------------------------------------------------------- #
#  読み取り
# --------------------------------------------------------------------------- #
def load(path=None):
    """route.json を読んで座標をタプルにする。"""
    with open(path or DEFAULT_JSON, "r", encoding="utf-8") as fp:
        data = json.load(fp)
    for r in data["roads"]:
        r["points"] = [tuple(p) for p in r["points"]]
    for b in data["buildings"]:
        b["footprint"] = [tuple(p) for p in b["footprint"]]
    for a in data["areas"]:
        a["polygon"] = [tuple(p) for p in a["polygon"]]
    for rt in data["routes"]:
        rt["points"] = [tuple(p) for p in rt["points"]]
    data["dormitory"]["footprint"] = [tuple(p) for p in data["dormitory"]["footprint"]]
    return data


def excluded(road):
    """campus.json と重なる道か。true なら敷かない（z ファイティング防止）。

    メインセッションの方針どおり `in_campus_json` が true のもの、および
    頂点を 2 つ以上共有しているもの（= 線分が 1 本以上まるごと重なる）を落とす。
    共有が 1 頂点だけのものは交差点を 1 点共有しているだけなので敷く。"""
    return bool(road.get("in_campus_json")) or int(road.get("campus_shared_vertices") or 0) >= 2


def pick_roads(data):
    return [r for r in data["roads"] if not excluded(r)]


def dropped_roads(data):
    return [r for r in data["roads"] if excluded(r)]


def road_width(road):
    return float(road.get("width") or WIDTH_FALLBACK.get(road.get("kind"), 4.0))


def pick_houses(data):
    """campus.json に無い建物（= campus.fbx が建てていない建物）。寮は buildings[] に入っていない。"""
    return [b for b in data["buildings"] if not b.get("in_campus_json")]


def dorm(data):
    return data["dormitory"]


# --------------------------------------------------------------------------- #
#  幾何の小道具（kcd_lib.geom を引かずに済む範囲だけ）
# --------------------------------------------------------------------------- #
def dedup(poly, eps=1e-4):
    out = []
    for p in poly:
        if not out or abs(p[0] - out[-1][0]) > eps or abs(p[1] - out[-1][1]) > eps:
            out.append((float(p[0]), float(p[1])))
    while len(out) > 1 and abs(out[0][0] - out[-1][0]) < eps and abs(out[0][1] - out[-1][1]) < eps:
        out.pop()
    return out


def polyline_length(pts):
    return sum(math.hypot(pts[i + 1][0] - pts[i][0], pts[i + 1][1] - pts[i][1])
               for i in range(len(pts) - 1))


def in_rect(p, rect, margin=0.0):
    x0, z0, x1, z1 = rect
    return (x0 - margin <= p[0] <= x1 + margin) and (z0 - margin <= p[1] <= z1 + margin)


def rect_poly(rect):
    x0, z0, x1, z1 = rect
    return [(x0, z0), (x1, z0), (x1, z1), (x0, z1)]


def bbox_of(points):
    xs = [p[0] for p in points]
    zs = [p[1] for p in points]
    return (min(xs), min(zs), max(xs), max(zs))


def point_in_poly(pt, poly):
    x, y = pt[0], pt[1]
    inside = False
    n = len(poly)
    for i in range(n):
        ax, ay = poly[i]
        bx, by = poly[(i + 1) % n]
        if (ay > y) != (by > y):
            t = (y - ay) / (by - ay)
            if x < ax + t * (bx - ax):
                inside = not inside
    return inside


def dist_point_segment(p, a, b):
    ax, ay = a
    bx, by = b
    dx, dy = bx - ax, by - ay
    dd = dx * dx + dy * dy
    if dd < 1e-12:
        return math.hypot(p[0] - ax, p[1] - ay)
    t = max(0.0, min(1.0, ((p[0] - ax) * dx + (p[1] - ay) * dy) / dd))
    return math.hypot(p[0] - (ax + t * dx), p[1] - (ay + t * dy))


def dist_polyline_polyline(u, v):
    """折れ線どうしの最短距離（総当たり。点数が小さいので十分）。"""
    best = float("inf")
    for i in range(len(u)):
        for j in range(len(v) - 1):
            best = min(best, dist_point_segment(u[i], v[j], v[j + 1]))
    for j in range(len(v)):
        for i in range(len(u) - 1):
            best = min(best, dist_point_segment(v[j], u[i], u[i + 1]))
    return best


def seg_cross(a, b, c, d):
    """線分 ab と cd の交点のパラメータ (t on ab, s on cd)。交わらなければ None。"""
    r = (b[0] - a[0], b[1] - a[1])
    s = (d[0] - c[0], d[1] - c[1])
    den = r[0] * s[1] - r[1] * s[0]
    if abs(den) < 1e-12:
        return None
    t = ((c[0] - a[0]) * s[1] - (c[1] - a[1]) * s[0]) / den
    u = ((c[0] - a[0]) * r[1] - (c[1] - a[1]) * r[0]) / den
    if -1e-9 <= t <= 1 + 1e-9 and -1e-9 <= u <= 1 + 1e-9:
        return t, u
    return None


def crossing_z(polyline, x):
    """折れ線が x を横切る z（最初の 1 つ）。無ければ None。"""
    for i in range(len(polyline) - 1):
        a, b = polyline[i], polyline[i + 1]
        if (a[0] - x) * (b[0] - x) <= 0.0 and abs(b[0] - a[0]) > 1e-9:
            t = (x - a[0]) / (b[0] - a[0])
            if -1e-9 <= t <= 1 + 1e-9:
                return a[1] + t * (b[1] - a[1])
    return None


# --------------------------------------------------------------------------- #
#  地面の帯
# --------------------------------------------------------------------------- #
def build_band(mb):
    """西へ足す地面。既存の地盤とまったく同じ高さ・同じマテリアルの 1 枚。

    スカート（側面）は付けない。既存の site_ground も付けていないし、Unity 側には
    ±2000 m の OuterGround が y = -0.05 に敷いてあるので、帯の外縁に穴は開かない。
    側面を付けると x = -350 の継ぎ目に余計な立ち上がりが入るだけになる。"""
    x0, z0, x1, z1 = BAND
    mb.add_ngon_flat([(x0, z0), (x1, z0), (x1, z1), (x0, z1)], Z_GROUND, "grass_dark")
    return (x1 - x0) * (z1 - z0)


# --------------------------------------------------------------------------- #
#  検算
# --------------------------------------------------------------------------- #
def check(data, verbose=True):
    """数字が動いたら落ちるようにしておく。戻り値は報告用の dict。"""
    roads = pick_roads(data)
    drops = dropped_roads(data)
    houses = pick_houses(data)
    dm = dorm(data)

    n_in_campus = sum(1 for r in drops if r.get("in_campus_json"))
    n_shared = len(drops) - n_in_campus
    assert len(roads) + len(drops) == len(data["roads"])
    assert len(roads) == 24, "敷く道が %d 本（24 のはず）" % len(roads)
    assert len(drops) == 40, "落とす道が %d 本（40 のはず）" % len(drops)
    assert len(houses) == 46, "沿道の建物が %d 棟（46 のはず）" % len(houses)

    # 帯は既存の地盤の外側だけ。二重に敷かない。
    assert BAND[2] == -GROUND_E, "帯の東端が地盤の西端と一致していない"
    assert BAND[1] == -GROUND_E and BAND[3] == GROUND_E, "帯の z 範囲が地盤と一致していない"
    assert BAND[0] < BAND[2]

    # 敷くもの全部が帯 ∪ 既存の地盤に載っていること
    pts = [p for r in roads for p in r["points"]] + [p for b in houses for p in b["footprint"]]
    bx0, bz0, bx1, bz1 = bbox_of(pts)
    assert bx0 >= BAND[0], "x = %.2f が帯の西端 %.1f より外" % (bx0, BAND[0])
    assert bx1 <= GROUND_E, "x = %.2f が地盤の東端より外" % bx1
    assert bz0 >= -GROUND_E and bz1 <= GROUND_E, "z が地盤の外 (%.2f..%.2f)" % (bz0, bz1)

    # 門: nw_gate のルートが Wall_West を横切る z は門の中か
    nw = [r for r in data["routes"] if r["key"] == "nw_gate"][0]
    zc = crossing_z(nw["points"], WALL_X)
    assert zc is not None, "nw_gate が x = %.0f を横切っていない" % WALL_X
    assert abs(zc - GATE_CENTER_Z) < 0.05, (
        "門の中心が %.2f（DormRoute.DefaultGateZ = %.2f）とずれている" % (zc, GATE_CENTER_Z))
    assert GATE_Z[0] + 4.0 < zc < GATE_Z[1] - 4.0, (
        "門 z %.2f..%.2f の中心から外れている (z = %.2f)" % (GATE_Z[0], GATE_Z[1], zc))

    # 寮と、壁より西のルートが Annex に収まるか
    for p in dm["footprint"]:
        assert in_rect(p, ANNEX), "寮の角 (%.2f, %.2f) が Annex の外" % p
    west = [p for p in nw["points"] if p[0] <= WALL_X]
    for p in west:
        assert in_rect(p, ANNEX), "壁より西のルート点 (%.2f, %.2f) が Annex の外" % p

    # 寮の敷地は空けておく（ここで建てるのは 46 棟だけ。寮は別担当）。
    # 寮は data["dormitory"] にあって buildings[] には入っていないので、46 棟に寮は含まれない。
    dorm_fp = dedup(dm["footprint"])
    dorm_ring = dorm_fp + [dorm_fp[0]]
    assert all(b["osm_id"] != dm["osm_id"] for b in houses), "46 棟の中に寮が混じっている"
    worst_h = None
    for b in houses:
        fp = dedup(b["footprint"])
        gap = dist_polyline_polyline(dorm_ring, fp + [fp[0]])
        if any(point_in_poly(p, dorm_fp) for p in fp):
            gap = -gap
        if worst_h is None or gap < worst_h[0]:
            worst_h = (gap, b["id"])
    assert worst_h[0] >= DORM_KEEPOUT, \
        "建物 %s が寮に %.2f m まで寄っている（下限 %.1f m）" % (worst_h[1], worst_h[0], DORM_KEEPOUT)

    # 道のリボンが寮のフットプリントに食い込まないか
    worst = None
    for r in roads:
        gap = dist_polyline_polyline(dorm_ring, r["points"]) - road_width(r) * 0.5
        if worst is None or gap < worst[0]:
            worst = (gap, r["osm_id"], r["kind"])
    assert worst[0] > 0.5, "道 %s (%s) が寮に %.2f m まで寄っている" % (worst[1], worst[2], worst[0])

    info = {
        "roads_total": len(data["roads"]),
        "roads_kept": len(roads),
        "roads_dropped": len(drops),
        "dropped_in_campus_json": n_in_campus,
        "dropped_shared_ge2": n_shared,
        "road_vertices": sum(len(r["points"]) for r in roads),
        "road_length_m": round(sum(polyline_length(r["points"]) for r in roads), 1),
        "houses": len(houses),
        "house_vertices": sum(len(dedup(b["footprint"])) for b in houses),
        "content_bbox": [round(v, 2) for v in bbox_of(pts)],
        "gate_crossing_z": round(zc, 2),
        "dorm_clearance_road_m": round(worst[0], 2),
        "dorm_clearance_house_m": round(worst_h[0], 2),
    }
    if verbose:
        for k, v in info.items():
            print("[route/check] %-24s %s" % (k, v))
    return info


def report(path=None):
    return check(load(path))
