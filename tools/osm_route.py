"""キャンパスから 葛飾コミュニティハウス (提携学生寮) までの道・沿道・寮を JSON に書き出す。

隠しエンド (Issue #41) 用のデータ。プレイヤーは北西口からキャンパスを出て、
**実在の道**を歩いて寮まで行く。その道と沿道の見た目をここで作る。

入力: data/osm/raw_route.json  (Overpass API `out geom;` の出力, 約 2.3 MB。gitignore)
出力: data/osm/route.json

キャンパス本体は tools/osm_extract.py が data/osm/campus.json に出す。**そちらは触らない**:
関心領域 (敷地 bbox + 120 m) を広げると背景建物が増えて campus.json 全体が揺れるため、
経路まわりだけを別ファイルにする。原点と投影は tools/osm_common.py で共有しているので座標は互換。

座標系: 原点 = キャンパス敷地ポリゴン (way 175463006) の重心, x = 東 (m), z = 北 (m)。

生データの取り直し (data/osm/route_query.ql と同じ内容):

    [out:json][timeout:300];
    (
      way(175463006);
      way["highway"](35.7678,139.8543,35.7762,139.8682);
      way["building"](35.7678,139.8543,35.7762,139.8682);
      way["amenity"](35.7678,139.8543,35.7762,139.8682);
      way["landuse"](35.7678,139.8543,35.7762,139.8682);
      way["leisure"](35.7678,139.8543,35.7762,139.8682);
      way["natural"](35.7678,139.8543,35.7762,139.8682);
      way["waterway"](35.7678,139.8543,35.7762,139.8682);
      way["barrier"](35.7678,139.8543,35.7762,139.8682);
      node["natural"](35.7678,139.8543,35.7762,139.8682);
      node["highway"](35.7678,139.8543,35.7762,139.8682);
      node["amenity"](35.7678,139.8543,35.7762,139.8682);
      node["shop"](35.7678,139.8543,35.7762,139.8682);
      node["entrance"](35.7678,139.8543,35.7762,139.8682);
      node["man_made"](35.7678,139.8543,35.7762,139.8682);
      relation["building"](35.7678,139.8543,35.7762,139.8682);
    );
    out geom;

    curl -s -X POST https://overpass-api.de/api/interpreter \
         --data-urlencode "data@data/osm/route_query.ql" -o data/osm/raw_route.json

bbox (35.7678, 139.8543) - (35.7762, 139.8682) はキャンパスと寮の両方を含む最小の矩形 +α。
2026-09-22 時点で 3016 要素 / 2.29 MB。

やっていること:
  1. highway の way から歩けるグラフを組む (節点 = OSM node id)。
     access=private / foot=no は重み 8 倍, steps 1.6 倍, service=parking_aisle 1.6 倍。
  2. 北西口 / 正門前の交差点 それぞれから寮の重心に一番近い節点まで Dijkstra (2 本とも出す)。
  3. 2 本の合併コリドー (道 32 m / 建物 45 m / 面 65 m) で沿道を拾い、道の折れ線は 60 m で切る。
     切らないと幹線 way 175463024 が北へ 910 m 伸びて総延長が 12 km になる。
  4. 寮の玄関を幾何から決める (各辺の中点から沿道の道の中心線までの距離。長い辺を 0.15×辺長 優遇)。
  5. campus.json に既にある建物・道には in_campus_json を立てる (二重に建てない・敷かないため)。
     建物は osm_id で照合できるが、campus.json の paths は osm_id を持たないので道は頂点で照合する。

決定性: 出力に効く集合 (set) は「含むか」と「何個か」にしか使わない。
要素は (type, id) 昇順に読み、同点の並びは osm_id / part で割る。hash() は使わない。
座標は投影で 3 桁、出力で 2 桁に丸める。同じ生データからは必ず同じバイト列が出る。

使い方:
  python tools/osm_route.py
  python tools/osm_route.py --raw <生データ> --out <出力先>
"""
from __future__ import annotations

import argparse
import heapq
import json
import math
import sys
from collections import defaultdict
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))

from osm_common import (  # noqa: E402
    CAMPUS_WAY_ID, ROOT, ccw, centroid, geom, load_elements, make_projector, origin_from, ring_area,
)

RAW = ROOT / "data" / "osm" / "raw_route.json"
OUT = ROOT / "data" / "osm" / "route.json"
CAMPUS_JSON = ROOT / "data" / "osm" / "campus.json"
QUERY_QL = ROOT / "data" / "osm" / "route_query.ql"

# raw_route.json はリポジトリに無い (gitignore)。無ければ route_query.ql を Overpass に投げて作る。

BBOX_LATLON = [35.7678, 139.8543, 35.7762, 139.8682]

DORM_WAY = 1552203022          # 葛飾コミュニティハウス (building=apartments, levels=5)
INTL_DORM_WAY = 1092671162     # 東京理科大学 葛飾国際学生寮 (参考。大学直営はこちら)
GATE_NODE = 700638657          # 交差点「理科大学前」= キャンパス正門 (東門) の前
NW_GATE_NODE = 700638663       # キャンパス北西口。敷地内の対角プロムナード way 565475714 の西端
DORM_FRONT_ROAD = 58360717     # 寮の前の道 (tertiary)。終点の節点はこの way の上にある

ROAD_CORRIDOR = 32.0           # 経路からこの距離内の道を出す (m)
BLD_CORRIDOR = 45.0            # 経路からこの距離内の建物を出す (m)
AREA_CORRIDOR = BLD_CORRIDOR + 20.0   # 面 (公園・工場跡地) はもう少し広く拾う
ROAD_CLIP = 60.0               # 道の折れ線はこの距離内だけ残す (長い幹線を切る)
BISECT_STEPS = 24              # コリドー境界の交点を求める二分法の回数
ENTRANCE_LONG_EDGE_BONUS = 0.15       # 玄関の辺を選ぶとき、長い辺をこの割合だけ優遇する
ENTRANCE_ROAD_RANGE = 60.0     # 玄関の向きを決めるのに使う道は寮の重心からこの距離内
HALF_EXTENT = 340.0            # 今の WorldBounds.HalfExtent。はみ出し量を測るのに使う

DEFAULT_WIDTH = {
    "trunk": 14.0, "primary": 12.0, "secondary": 10.0, "tertiary": 9.0,
    "unclassified": 6.0, "residential": 6.0, "living_street": 5.0,
    "service": 4.0, "track": 3.0, "pedestrian": 6.0,
    "footway": 2.5, "path": 2.0, "steps": 2.0, "cycleway": 2.5,
}
DEFAULT_SURFACE = {
    "footway": "asphalt", "path": "asphalt", "steps": "concrete", "cycleway": "asphalt",
}
# 歩きにくい道は重み付け (最短経路の見た目を実際の歩行に近づける)
WALK_WEIGHT = {
    "footway": 1.0, "path": 1.05, "steps": 1.6, "pedestrian": 1.0, "residential": 1.0,
    "living_street": 1.0, "unclassified": 1.0, "tertiary": 1.05, "secondary": 1.15,
    "service": 1.1, "cycleway": 1.0, "track": 1.3,
}
AREA_KINDS = ("park", "pitch", "playground", "garden", "grass", "recreation_ground",
              "water", "wood", "scrub", "parking", "school", "industrial", "commercial",
              "retail", "cemetery", "forest")
COMPASS16 = ["北", "北北東", "北東", "東北東", "東", "東南東", "南東", "南南東",
             "南", "南南西", "南西", "西南西", "西", "西北西", "北西", "北北西"]
ENTRANCE_ROAD_KINDS = ("tertiary", "residential", "unclassified", "service", "living_street", "footway")


# --------------------------------------------------------------------------
# 幾何
# --------------------------------------------------------------------------

def seg_dist(p, a, b) -> float:
    """点 p と線分 a-b の距離。"""
    ax, ay = a
    bx, by = b
    px, py = p
    dx, dy = bx - ax, by - ay
    L2 = dx * dx + dy * dy
    t = 0.0 if L2 == 0 else max(0.0, min(1.0, ((px - ax) * dx + (py - ay) * dy) / L2))
    return math.hypot(px - (ax + t * dx), py - (ay + t * dy))


def poly_dist(pts, route) -> float:
    """pts (点列) と route (折れ線) の最小距離。"""
    best = 1e18
    for p in pts:
        for a, b in zip(route, route[1:]):
            d = seg_dist(p, a, b)
            if d < best:
                best = d
    for p in route:
        for a, b in zip(pts, pts[1:]):
            d = seg_dist(p, a, b)
            if d < best:
                best = d
    return best


def polyline_length(pts) -> float:
    return sum(math.dist(a, b) for a, b in zip(pts, pts[1:]))


# --------------------------------------------------------------------------
# 道路グラフ
# --------------------------------------------------------------------------

def build_graph(ways: list[dict], project):
    """歩けるグラフ。node id -> 座標 と 隣接リストを返す。

    ways は id 昇順で渡すこと (同じコストの経路が複数あるときの選ばれ方を入力順に依らせないため)。
    """
    np_: dict[int, tuple[float, float]] = {}
    adj: dict[int, list] = defaultdict(list)
    for e in ways:
        t = e["tags"]
        kind = t["highway"]
        if kind not in WALK_WEIGHT:
            continue
        w = WALK_WEIGHT[kind]
        if t.get("service") == "parking_aisle":
            w = 1.6
        if t.get("access") in ("private", "no") or t.get("foot") == "no":
            w = 8.0
        nids = e["nodes"]
        pts = [project(p["lon"], p["lat"]) for p in e["geometry"]]
        for a, b, pa, pb in zip(nids, nids[1:], pts, pts[1:]):
            np_[a] = pa
            np_[b] = pb
            d = math.dist(pa, pb)
            adj[a].append((b, d * w, e["id"], d))
            adj[b].append((a, d * w, e["id"], d))
    return np_, adj


def nearest_node(np_: dict[int, tuple[float, float]], x: float, z: float):
    """(x, z) に一番近い節点。同着は node id の小さいほうを取る。"""
    best, bd = None, 1e18
    for nid in sorted(np_):
        d = math.dist(np_[nid], (x, z))
        if d < bd:
            best, bd = nid, d
    return best, bd


def dijkstra(adj, src: int, dst: int):
    """重み付き最短経路。返り値は (節点列, 通った way id 列, 実距離 m)。"""
    dist = {src: 0.0}
    prev = {}
    pq = [(0.0, src)]
    while pq:
        d, u = heapq.heappop(pq)
        if u == dst:
            break
        if d > dist.get(u, 1e18):
            continue
        for v, c, wid, raw in adj[u]:
            nd = d + c
            if nd < dist.get(v, 1e18):
                dist[v] = nd
                prev[v] = (u, wid, raw)
                heapq.heappush(pq, (nd, v))
    if dst not in dist:
        return None
    path, wids, total = [dst], [], 0.0
    cur = dst
    while cur != src:
        u, wid, raw = prev[cur]
        wids.append(wid)
        total += raw
        path.append(u)
        cur = u
    path.reverse()
    wids.reverse()
    return path, wids, total


# --------------------------------------------------------------------------
# コリドー (経路からの距離で沿道を拾う / 折れ線を切る)
# --------------------------------------------------------------------------

class Corridor:
    """複数本の経路の合併コリドー。"""

    def __init__(self, routes: list[dict]):
        self.keys = [r["key"] for r in routes]
        self.lines = [r["_pts"] for r in routes]

    def point_dist(self, p) -> float:
        return min(min(seg_dist(p, a, b) for a, b in zip(line, line[1:])) for line in self.lines)

    def dist(self, pts) -> float:
        return min(poly_dist(pts, line) for line in self.lines)

    def near_keys(self, pts, limit: float) -> list[str]:
        return [self.keys[i] for i, line in enumerate(self.lines) if poly_dist(pts, line) <= limit]

    def clip(self, pts, limit: float) -> list[list[tuple[float, float]]]:
        """折れ線のうち経路から limit 以内の部分だけを取り出す (複数の切れ端になりうる)。

        境界をまたぐ辺は二分法で交点を挿し込むので、切り口が経路から limit のところに来る。
        """
        keep = [self.point_dist(p) <= limit for p in pts]
        out, cur = [], []
        for i, p in enumerate(pts):
            if keep[i]:
                if not cur and i > 0:
                    cur.append(self._bisect(pts[i - 1], p, limit))
                cur.append(p)
            else:
                if cur:
                    cur.append(self._bisect(pts[i - 1], p, limit))
                    out.append(cur)
                    cur = []
        if cur:
            out.append(cur)
        return [c for c in out if len(c) >= 2]

    def _bisect(self, a, b, limit: float):
        """a, b の間で経路からの距離 = limit になる点 (どちらか一方が内側)。"""
        if self.point_dist(a) > limit:
            a, b = b, a
        lo, hi = 0.0, 1.0   # lo = 内側
        for _ in range(BISECT_STEPS):
            m = (lo + hi) / 2
            p = (a[0] + (b[0] - a[0]) * m, a[1] + (b[1] - a[1]) * m)
            if self.point_dist(p) <= limit:
                lo = m
            else:
                hi = m
        return (a[0] + (b[0] - a[0]) * lo, a[1] + (b[1] - a[1]) * lo)


# --------------------------------------------------------------------------
# 要素 → 出力レコード
# --------------------------------------------------------------------------

def _f(value, fallback):
    try:
        return float(str(value).replace("m", "").strip())
    except (TypeError, ValueError):
        return fallback


def building_entry(e: dict, xy: list[tuple[float, float]], campus_ids, prefix: str = "rt") -> dict:
    t = e["tags"]
    fp = ccw(xy)
    lv = t.get("building:levels")
    try:
        levels = int(float(lv)) if lv else None
    except ValueError:
        levels = None
    height = _f(t.get("height"), None) if t.get("height") else None
    if height is None:
        height = levels * 3.2 if levels else 8.0
    xs = [p[0] for p in fp]
    zs = [p[1] for p in fp]
    return {
        "id": f"{prefix}_{e['id']}",
        "osm_id": e["id"],
        "name": t.get("name"),
        "kind": t.get("building"),
        "levels": levels,
        "height": round(height, 2),
        "area": round(abs(ring_area(fp)), 1),
        "bbox": [round(min(xs), 2), round(min(zs), 2), round(max(xs), 2), round(max(zs), 2)],
        "in_campus_json": e["id"] in campus_ids,
        "footprint": [[round(x, 2), round(z, 2)] for x, z in fp],
    }


def entrance_of(dorm_fp, roads, center) -> dict:
    """寮の玄関: フットプリントの各辺の中点から沿道の道の中心線までの距離が一番近い辺。

    長い辺 (建物の正面) を少し優遇する。外向き法線が玄関の向き (CCW なので (dy, -dx) が外側)。
    """
    near_roads = []
    for r in roads:
        if r["kind"] not in ENTRANCE_ROAD_KINDS:
            continue
        pts = [tuple(p) for p in r["points"]]
        if min(seg_dist(center, a, b) for a, b in zip(pts, pts[1:])) < ENTRANCE_ROAD_RANGE:
            near_roads.append((r["osm_id"], r["kind"], pts))

    best = None
    for i in range(len(dorm_fp)):
        a = dorm_fp[i]
        b = dorm_fp[(i + 1) % len(dorm_fp)]
        mid = ((a[0] + b[0]) / 2, (a[1] + b[1]) / 2)
        elen = math.dist(a, b)
        for rid, rkind, rpts in near_roads:
            d = min(seg_dist(mid, p, q) for p, q in zip(rpts, rpts[1:]))
            score = d - elen * ENTRANCE_LONG_EDGE_BONUS
            if best is None or score < best[0]:
                dx, dy = b[0] - a[0], b[1] - a[1]
                n = math.hypot(dx, dy)
                best = (score, i, mid, elen, d, (dy / n, -dx / n), rid, rkind)

    _, ei, emid, elen, edist, (enx, enz), erid, erkind = best
    bearing = (math.degrees(math.atan2(enx, enz)) + 360) % 360   # 0 = 北, 90 = 東
    return {
        "edge_index": ei,
        "point": [round(emid[0], 2), round(emid[1], 2)],
        "edge_length": round(elen, 2),
        "facing_bearing": round(bearing, 1),
        "facing": COMPASS16[int(((bearing + 11.25) % 360) // 22.5)],
        "faces_osm_way": erid,
        "faces_road_kind": erkind,
        "dist_to_road_center": round(edist, 2),
        "how": "フットプリントの各辺の中点から沿道の道の中心線までの距離を測り、長い辺を少し優遇して一番近い辺を玄関とした。",
    }


# --------------------------------------------------------------------------
# 本体
# --------------------------------------------------------------------------

def extract(raw: Path, campus_json: Path = CAMPUS_JSON) -> dict:
    elements = load_elements(raw)
    lat0, lon0 = origin_from(elements)
    project = make_projector(lat0, lon0)

    # (type, id) 昇順。Overpass の出力も元からこの順だが、入力順に結果を依らせない。
    ways = sorted((e for e in elements if e["type"] == "way" and "geometry" in e), key=lambda e: e["id"])
    by_way = {e["id"]: e for e in ways}
    xy_cache: dict[int, list[tuple[float, float]]] = {}

    def xy(e) -> list[tuple[float, float]]:
        pts = xy_cache.get(e["id"])
        if pts is None:
            pts = [project(*p) for p in geom(e)]
            xy_cache[e["id"]] = pts
        return pts

    cj = json.loads(campus_json.read_text(encoding="utf-8"))
    campus_ids = {b["osm_id"] for b in cj["buildings"]}
    # campus.json の paths は osm_id を持たないので、道の重複は **頂点** で見るしかない。
    # campus.json は座標 3 桁、route.json は 2 桁なので、必ず 2 桁に丸めてから突き合わせる
    # (丸めずに比べると 64 本中 1 本しか一致しない)。
    campus_path_pts = {(round(x, 2), round(z, 2)) for p in cj["paths"] for x, z in p["points"]}

    # ---- 1) 経路 ---------------------------------------------------------
    highways = [e for e in ways if e.get("tags", {}).get("highway") in WALK_WEIGHT and "nodes" in e]
    np_, adj = build_graph(highways, project)

    dorm = by_way[DORM_WAY]
    dorm_fp = ccw(xy(dorm))
    dcx, dcz = centroid(dorm_fp)

    end_node, end_gap = nearest_node(np_, dcx, dcz)
    on_front_road = any(wid == DORM_FRONT_ROAD for _, _, wid, _ in adj[end_node])
    if not on_front_road:
        print(f"!! 終点の節点 {end_node} が寮前の道 way {DORM_FRONT_ROAD} の上にない")

    def build_route(start_node: int, key: str, label: str) -> dict:
        nodes, way_ids, length = dijkstra(adj, start_node, end_node)
        pts = [np_[n] for n in nodes]
        ordered = []
        for w in way_ids:
            if not ordered or ordered[-1] != w:
                ordered.append(w)
        return {
            "key": key,
            "from": {"name": label, "osm_node": start_node,
                     "x": round(np_[start_node][0], 2), "z": round(np_[start_node][1], 2)},
            "to": {"name": f"葛飾コミュニティハウス 前の道 ({DORM_FRONT_ROAD})", "osm_node": end_node,
                   "x": round(np_[end_node][0], 2), "z": round(np_[end_node][1], 2)},
            "length_m": round(length, 1),
            "straight_m": round(math.dist(np_[start_node], (dcx, dcz)), 1),
            "way_ids": ordered,
            "points": [[round(x, 2), round(z, 2)] for x, z in pts],
            "_pts": pts,
        }

    routes = [
        build_route(NW_GATE_NODE, "nw_gate", "キャンパス北西口 (敷地内の対角プロムナード way 565475714 の西端)"),
        build_route(GATE_NODE, "east_gate", "交差点「理科大学前」(正門 / 東門の前)"),
    ]
    corridor = Corridor(routes)
    route_way_ids = {w for r in routes for w in r["way_ids"]}   # 「経路上か」の判定にだけ使う

    # ---- 2) 沿道の道 -----------------------------------------------------
    roads = []
    for e in ways:
        t = e.get("tags", {})
        hw = t.get("highway")
        if hw not in DEFAULT_WIDTH:
            continue
        pts = xy(e)
        on_route = e["id"] in route_way_ids
        if corridor.dist(pts) > ROAD_CORRIDOR and not on_route:
            continue
        width = _f(t.get("width"), DEFAULT_WIDTH[hw]) if t.get("width") else DEFAULT_WIDTH[hw]
        for k, seg in enumerate(corridor.clip(pts, ROAD_CLIP)):
            out_pts = [[round(x, 2), round(z, 2)] for x, z in seg]
            shared = sum(1 for x, z in out_pts if (x, z) in campus_path_pts)
            roads.append({
                "osm_id": e["id"],
                "part": k,
                "kind": hw,
                "name": t.get("name"),
                "width": round(width, 2),
                "surface": t.get("surface") or DEFAULT_SURFACE.get(hw, "asphalt"),
                "bridge": bool(t.get("bridge")),
                "oneway": t.get("oneway") == "yes",
                "on_route": on_route,
                # campus.json の paths と頂点がいくつ重なるか / 全部重なるか。
                # in_campus_json が true の道は campus.json 側で既に敷かれているので **敷かない**
                # (敷くと z ファイティングする)。0 < campus_shared_vertices < 頂点数 の道は
                # 一部だけ重なっている (ROI や ROAD_CLIP の切り方が違うため)。捨てずに重なった区間を外す。
                "in_campus_json": shared == len(out_pts),
                "campus_shared_vertices": shared,
                "dist_to_route": round(corridor.dist(seg), 1),
                "near_route": corridor.near_keys(seg, ROAD_CORRIDOR),
                "length": round(polyline_length(seg), 1),
                "points": out_pts,
            })
    roads.sort(key=lambda r: (not r["on_route"], r["dist_to_route"], r["osm_id"], r["part"]))

    # ---- 3) 沿道の建物 ---------------------------------------------------
    buildings = []
    for e in ways:
        t = e.get("tags", {})
        if "building" not in t or e["id"] == DORM_WAY:
            continue
        fp = xy(e)
        if len(fp) < 4:
            continue
        d = corridor.dist(fp)
        if d > BLD_CORRIDOR:
            continue
        b = building_entry(e, fp, campus_ids)
        b["dist_to_route"] = round(d, 1)
        b["near_route"] = corridor.near_keys(fp, BLD_CORRIDOR)
        buildings.append(b)
    buildings.sort(key=lambda b: (b["dist_to_route"], b["osm_id"]))

    # ---- 3b) 沿道の面 (公園・工場跡地・駐車場など) -----------------------
    areas = []
    for e in ways:
        t = e.get("tags", {})
        if "building" in t:
            continue
        kind = t.get("leisure") or t.get("landuse") or t.get("natural") or t.get("amenity")
        if kind not in AREA_KINDS:
            continue
        pts = ccw(xy(e))
        if len(pts) < 3:
            continue
        d = corridor.dist(pts)
        if d > AREA_CORRIDOR:
            continue
        xs = [p[0] for p in pts]
        zs = [p[1] for p in pts]
        areas.append({
            "id": f"area_{e['id']}",
            "osm_id": e["id"],
            "kind": kind,
            "name": t.get("name"),
            "area": round(abs(ring_area(pts)), 1),
            "bbox": [round(min(xs), 2), round(min(zs), 2), round(max(xs), 2), round(max(zs), 2)],
            "dist_to_route": round(d, 1),
            "near_route": corridor.near_keys(pts, AREA_CORRIDOR),
            "polygon": [[round(x, 2), round(z, 2)] for x, z in pts],
        })
    areas.sort(key=lambda a: (-a["area"], a["osm_id"]))

    # ---- 4) 寮本体 + 玄関の向き -----------------------------------------
    dxs = [p[0] for p in dorm_fp]
    dzs = [p[1] for p in dorm_fp]
    # 主軸の向き (一番長い辺)
    lens = [(math.dist(dorm_fp[i], dorm_fp[(i + 1) % len(dorm_fp)]), i) for i in range(len(dorm_fp))]
    lens.sort(reverse=True)
    la, lb = dorm_fp[lens[0][1]], dorm_fp[(lens[0][1] + 1) % len(dorm_fp)]
    long_bearing = (math.degrees(math.atan2(lb[0] - la[0], lb[1] - la[1])) + 360) % 180

    dorm_entry = building_entry(dorm, xy(dorm), campus_ids, prefix="bld")
    dorm_entry.update({
        "id": "bld_community_house",
        "display": "葛飾コミュニティハウス",
        "official_name": "東京理科大学 葛飾コミュニティハウス",
        "address": "東京都葛飾区南水元1-8-13",
        "operator": "株式会社共立メンテナンス (学生会館ドーミー)",
        "note": "大学直営ではなく大学提携の学生寮。RC 5 階建、全 100 室 (女子 23 / 男子 77)、朝夕食付。2013 年 4 月開設。",
        "centroid": [round(dcx, 2), round(dcz, 2)],
        "size": [round(max(dxs) - min(dxs), 2), round(max(dzs) - min(dzs), 2)],
        "long_axis_bearing": round(long_bearing, 1),
        "entrance": entrance_of(dorm_fp, roads, (dcx, dcz)),
    })

    intl = building_entry(by_way[INTL_DORM_WAY], xy(by_way[INTL_DORM_WAY]), campus_ids, prefix="bld")
    intl.update({
        "id": "bld_intl_dorm",
        "display": "東京理科大学 葛飾国際学生寮",
        "address": "東京都葛飾区東金町2-17-22",
        "operator": "東京理科大学",
        "note": "大学直営の国際寮。OSM 上は building=dormitory で名前付き。キャンパスのすぐ東。",
    })

    # ---- 5) 数値 ---------------------------------------------------------
    # bbox は **出力した座標そのもの** (2 桁に丸めたあと) から作る。
    # 丸める前の値で作ると、消費側が points から測り直したときに bbox の外へ出る点ができる。
    rxs = [p[0] for r in routes for p in r["points"]]
    rzs = [p[1] for r in routes for p in r["points"]]
    bxs = [p[0] for b in buildings for p in b["footprint"]] + dxs
    bzs = [p[1] for b in buildings for p in b["footprint"]] + dzs
    road_xs = [p[0] for r in roads for p in r["points"]]
    road_zs = [p[1] for r in roads for p in r["points"]]
    # 面 (公園・工場跡地) のポリゴンは道と違って切らずにそのまま出すので、コリドーよりずっと外まで伸びる。
    # industrial way 153722332 は南へ z=-387 まで届く。地面や NavMesh の大きさを決める値には必ず入れる。
    area_xs = [p[0] for a in areas for p in a["polygon"]]
    area_zs = [p[1] for a in areas for p in a["polygon"]]
    solidx = rxs + bxs + road_xs          # 面を除いた「立てるもの」
    solidz = rzs + bzs + road_zs
    allx = solidx + area_xs
    allz = solidz + area_zs

    def over(vals, sign):
        return round(max(0.0, (max(vals) if sign > 0 else -min(vals)) - HALF_EXTENT), 1)

    def bbox_of(xs, zs):
        return [round(min(xs), 1), round(min(zs), 1), round(max(xs), 1), round(max(zs), 1)]

    stats = {
        "routes": {r["key"]: {"length_m": r["length_m"], "straight_m": r["straight_m"],
                              "nodes": len(r["points"]), "ways": len(r["way_ids"]),
                              "from": [r["from"]["x"], r["from"]["z"]]} for r in routes},
        "route_bbox": bbox_of(rxs, rzs),
        "content_bbox": bbox_of(allx, allz),
        "content_bbox_no_areas": bbox_of(solidx, solidz),
        "area_bbox": bbox_of(area_xs, area_zs),
        "outside_bounds_m": {
            "half_extent": HALF_EXTENT,
            "note": "今の WorldBounds.HalfExtent=340 の正方形 (x,z とも -340..340) から何 m はみ出すか。"
                    "all_content_* は areas を含む (面を敷かないなら no_areas_* を見る)",
            "route_polyline_west": over(rxs, -1),
            "route_polyline_north": over(rzs, +1),
            "dorm_footprint_west": round(max(0.0, -min(dxs) - HALF_EXTENT), 1),
            "all_content_west": over(allx, -1),
            "all_content_north": over(allz, +1),
            "all_content_east": over(allx, +1),
            "all_content_south": over(allz, -1),
            "no_areas_west": over(solidx, -1),
            "no_areas_north": over(solidz, +1),
            "no_areas_east": over(solidx, +1),
            "no_areas_south": over(solidz, -1),
        },
        "roads": len(roads),
        "road_osm_ways": len({r["osm_id"] for r in roads}),
        "roads_on_route": sum(1 for r in roads if r["on_route"]),
        "road_osm_ways_on_route": len({r["osm_id"] for r in roads if r["on_route"]}),
        "roads_in_campus_json": sum(1 for r in roads if r["in_campus_json"]),
        "roads_partly_in_campus_json": sum(
            1 for r in roads if not r["in_campus_json"] and r["campus_shared_vertices"] >= 2
        ),
        "roads_new": sum(1 for r in roads if r["campus_shared_vertices"] < 2),
        "roads_total_length_m": round(sum(r["length"] for r in roads), 1),
        "road_vertex_count": sum(len(r["points"]) for r in roads),
        "areas": len(areas),
        "area_vertex_count": sum(len(a["polygon"]) for a in areas),
        "buildings": len(buildings),
        "buildings_new": sum(not b["in_campus_json"] for b in buildings),
        "buildings_outside_bounds": sum(
            1 for b in buildings
            if any(abs(p[0]) > HALF_EXTENT or abs(p[1]) > HALF_EXTENT for p in b["footprint"])
        ),
        "building_vertex_count": sum(len(b["footprint"]) for b in buildings),
        "footprint_vertex_count_incl_dorm": sum(len(b["footprint"]) for b in buildings) + len(dorm_entry["footprint"]),
        "dorm_size": dorm_entry["size"],
        "dorm_height": dorm_entry["height"],
        "dorm_levels": dorm_entry["levels"],
        "dorm_area": dorm_entry["area"],
        "dorm_centroid": dorm_entry["centroid"],
        "dorm_entrance": dorm_entry["entrance"]["point"],
        "end_node_gap_m": round(end_gap, 1),
    }

    for r in routes:
        r.pop("_pts", None)

    return {
        "meta": {
            "source": "OpenStreetMap (ODbL) via Overpass API, 2026-09-23 (timestamp_osm_base 2026-09-22T08:45:51Z)",
            "raw": "data/osm/raw_route.json",
            "query": "data/osm/route_query.ql",
            "generator": "tools/osm_route.py",
            "bbox_latlon": BBOX_LATLON,
            "origin": {"lat": lat0, "lon": lon0},
            "axes": "x=east(m), z=north(m); Unity: (x, y=up, z); Blender: (x, y=z_north, z=up)",
            "campus_osm_way": CAMPUS_WAY_ID,
            "transform": "tools/osm_extract.py と同一 (tools/osm_common.py で共有)。"
                         "campus.json の 239 棟 + 敷地ポリゴン 1575 頂点で最大誤差 0.000000 m "
                         "(python tools/osm_common.py --selfcheck)",
            "corridor_m": {"roads": ROAD_CORRIDOR, "buildings": BLD_CORRIDOR, "road_clip": ROAD_CLIP},
            "license": "OpenStreetMap contributors, ODbL 1.0 (建物の高さ・階数は MLIT PLATEAU 由来)",
            "notes": [
                "座標は小数 2 桁 (1 cm)。campus.json は 3 桁 (1 mm)。"
                "同じ way が両方に入っていても座標は一致しないので、突き合わせるときは必ず round(v, 2) してから比べる。",
                "routes[].straight_m は『門 → 寮の重心』の直線距離。"
                "経路の両端 (points[0] と points[-1]) の距離ではない (そちらは nw_gate 339.8 m / east_gate 698.2 m)。",
                "routes[].length_m はグラフ上の実距離。丸めた points から折れ線長を測り直すと east_gate は 846.3 になる。",
                "stats.end_node_gap_m は経路の終点 (道の上の節点) から寮の重心までの距離。"
                "玄関 (dormitory.entrance.point) までは 8.7 m、footprint までは 8.6 m。",
                "areas のポリゴンは道と違って切っていない。industrial way 153722332 は南へ z=-387 まで伸びる。"
                "地面や NavMesh の大きさは stats.content_bbox (areas 込み) で決める。",
                "roads は 1 つの way が複数の part に分かれることがある (roads 64 本 / road_osm_ways 63)。"
                "id で突き合わせるときは (osm_id, part) で見る。",
                "roads[].in_campus_json が true の道は campus.json の paths と頂点が全部重なる。敷くと z ファイティングするので敷かない。"
                "0 < campus_shared_vertices < 頂点数 の道は一部だけ重なっている (ROI と ROAD_CLIP の切り方が違うため)。"
                "campus.json の paths には osm_id が無いので、この照合は頂点でやるしかない。",
            ],
        },
        "stats": stats,
        "routes": routes,
        "dormitory": dorm_entry,
        "intl_dormitory": intl,
        "roads": roads,
        "buildings": buildings,
        "areas": areas,
    }


def resolve_raw(arg: Path | None) -> Path:
    # --raw を明示したのに無い場合は黙って別のファイルに落ちない (打ち間違えたまま出力ができてしまうため)
    if arg is not None:
        if not Path(arg).exists():
            raise SystemExit(f"--raw に渡した生データが無い: {arg}")
        return Path(arg)
    if RAW.exists():
        return RAW
    raise SystemExit(
        f"生データが無い: {RAW}\n"
        f"  {QUERY_QL} を Overpass に投げて作る:\n"
        f'  curl -s -X POST https://overpass-api.de/api/interpreter --data-urlencode "data@{QUERY_QL}" -o {RAW}'
    )


def main() -> None:
    ap = argparse.ArgumentParser(description="寮への道を data/osm/route.json に書き出す")
    ap.add_argument("--raw", type=Path, default=None, help=f"Overpass の生データ (既定: {RAW})")
    ap.add_argument("--out", type=Path, default=OUT, help=f"出力先 (既定: {OUT})")
    ap.add_argument("--campus-json", type=Path, default=CAMPUS_JSON, help="in_campus_json の判定に使う campus.json")
    args = ap.parse_args()

    raw = resolve_raw(args.raw)
    data = extract(raw, args.campus_json)
    args.out.write_text(json.dumps(data, ensure_ascii=False, indent=1), encoding="utf-8")

    s = data["stats"]
    print(f"raw  {raw}")
    print(f"out  {args.out}")
    print(f"origin lat={data['meta']['origin']['lat']:.6f} lon={data['meta']['origin']['lon']:.6f}")
    for key, r in s["routes"].items():
        print(f"  route {key:<9} {r['length_m']:6.1f} m (直線 {r['straight_m']:.1f} m, 節点 {r['nodes']}, way {r['ways']})")
    d = data["dormitory"]
    print(f"  寮 {d['display']} osm={d['osm_id']} 重心 {d['centroid']} 玄関 {d['entrance']['point']} "
          f"{d['entrance']['facing']} ({d['entrance']['facing_bearing']}°)")
    print(f"  roads の campus.json との重なり: 全部重なる {s['roads_in_campus_json']} / 一部 {s['roads_partly_in_campus_json']} / "
          f"重ならない {s['roads_new']}")
    print(f"  roads={s['roads']} ({s['road_osm_ways']} way, {s['roads_total_length_m']} m, 頂点 {s['road_vertex_count']}), "
          f"buildings={s['buildings']} (新規 {s['buildings_new']}, 頂点 {s['building_vertex_count']}), "
          f"areas={s['areas']} (頂点 {s['area_vertex_count']})")
    ob = s["outside_bounds_m"]
    print(f"  content_bbox={s['content_bbox']} (areas 込み)  はみ出し 西 {ob['all_content_west']} m / "
          f"北 {ob['all_content_north']} m / 東 {ob['all_content_east']} m / 南 {ob['all_content_south']} m")
    print(f"  content_bbox_no_areas={s['content_bbox_no_areas']}  はみ出し 西 {ob['no_areas_west']} m / "
          f"北 {ob['no_areas_north']} m / 東 {ob['no_areas_east']} m / 南 {ob['no_areas_south']} m")


if __name__ == "__main__":
    main()
