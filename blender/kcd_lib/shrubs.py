"""低木と草むらの形と置き場所 (#78)。

形は Kenney の Nature Kit（CC0 1.0）から取った頂点と三角形（data/shrubs/kenney_shrubs.json。
tools/fetch_shrubs.py が作る）。Kenney の材質（単色）は使わない。ここで決めるのは次の 3 つ。

- 向きと大きさ: Kenney は Y が上で、1 点が 0.2〜0.6 m の置物の大きさ。Blender の Z 上へ回し、植え込みの大きさへ拡大する。
- 色: 面ごとに、木の葉と同じ CampusColors の色の折れ線（低木は leaf_dark..leaf_top、草むらは
  grass_dark..leaf_top）から選び、材質 `leaf` の面の色に焼く。Unity に新しい材質は要らず、
  PaletteAgreementTests が突き合わせる色も増えない。
- 置き場所: 建物の足元の列植（壁から WALL_OFFSET）と、建物の近くの木の根元の草むら。
  扉の前・歩道・メインストリート・水盤・公園・グラウンド・看板・他の建物・木の幹を避ける。

木と同じく「原型 + Empty」で配る（build_campus.py が shrubs.fbx と shrubs.json に書く）。
乱数は木の乱数列と別の random.Random を使う。共有の rng を使うと、木の位置が動く。
"""

import collections
import json
import math
import os

from . import buildings, entrances, geom, mats, site
from . import trees as treelib
from .mesh import MeshBuilder

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
DATA_PATH = os.path.join(ROOT, "data", "shrubs", "kenney_shrubs.json")

SEED_OFFSET = 78         # random.Random(--seed + SEED_OFFSET)

# ---- 大きさ ----
# Kenney の低木は幅 0.4 m 前後。植え込みの低木（高さ 0.6〜1.1 m、幅 1.3〜2 m）にする。
BUSH_SCALE = 3.6
# 草むらは高さ 0.25 m。実物の草むら（0.35〜0.5 m）にする。
GRASS_SCALE = 2.0
# 他より大きい・小さいモデルの補正
MODEL_SCALE = {"plant_bushDetailed": 3.0, "grass_leafs": 2.4, "grass_leafsLarge": 2.4}
JITTER = (0.85, 1.2)     # 1 本ごとの大きさの揺らぎ（原型の大きさに掛ける）

# ---- 色 ----
BUSH_T_BASE = 0.12       # 足元の t。木の葉の折れ線（0 = leaf_dark、1 = leaf_top）の上で、
BUSH_T_HEIGHT = 0.45     # 高さに応じて明るく、
BUSH_T_UP = 0.25         # 上を向いた面ほど明るく
BUSH_T_MAX = 0.85        # ただし leaf_top には届かせない
GRASS_RAMP = ("grass_dark", "grass", "leaf_top")

# ---- 形 ----
# Kenney の板は、同じ位置に巻きが逆の三角形を重ねた両面。重なったままだと、アウトラインのパス（裏向きの面を
# 暗い色で描く）が表と同じ深さで描いてしまい、面ごとに暗い色が混ざる。表と裏をこの幅だけ離して、
# 手前の面が深さで勝つようにする。0.1 mm の溶接（MeshBuilder.to_object）に潰されず、24 bit の深度でも
# 100 m 先まで分かれる幅。
PAIR_GAP = 0.006

# ---- 置き場所 ----
WALL_OFFSET = 1.35       # 壁から低木の中心まで [m]
WALL_JITTER = 0.15
BUSH_STEP = (2.1, 2.7)   # 植え込みの中の間隔 [m]
BED_LEN = (7.0, 16.0)    # 植え込み 1 つの長さ
GAP_LEN = (6.0, 14.0)    # 植え込みの間の空き
CORNER_SKIP = 1.0        # 壁の端から最初の低木まで
END_TUFT = 1.3           # 植え込みの端から草むらまで（壁に沿って）
END_TUFT_OFFSET = 1.0    # その草むらの、壁からの距離
TREE_TUFT_RADIUS = (1.0, 1.6)   # 木の根元の草むらまでの距離
TREE_TUFT_NEAR = 25.0    # 建物からこの距離までの木にだけ草むらを置く
TREE_TUFT_RATE = 0.30    # 該当する木のうち根元に草むらを置く割合
MAX_BUSHES = 140
MAX_GRASS = 100

# 避ける距離 [m]。低木と草むらで分ける（草むらは小さく、路肩にも寄れる）
CLEAR = {
    "bush": {"building": 1.1, "path": 0.9, "tree": 1.5, "crowd": 1.6},
    "grass": {"building": 0.8, "path": 0.6, "tree": 0.8, "crowd": 1.0},
}
DOOR_CLEAR = 7.0         # 扉の中心から。看板・自販機・ゴミ箱は扉の脇 6 m 以内に並ぶ
LANE_CLEAR = 1.5         # 扉の前の通り道（entrances.corridor）から
SIGN_CLEAR = 3.0
MALL_CLEAR = 1.0         # メインストリート・正門前の広場・水盤の縁石から
LAWN_CLEAR = 0.5         # 図書館の東の芝生広場から
GROUND_CLEAR = 1.0       # グラウンド・遊具広場・駐車場から
BED_CLEAR = 1.0          # メインストリート北の花壇から

GRID_CELL = 20.0         # 線分の検索格子


def load(path=DATA_PATH):
    with open(path, encoding="utf-8") as fp:
        return json.load(fp)


# --------------------------------------------------------------------------- #
#  形と色
# --------------------------------------------------------------------------- #
def _ramp(names):
    return [tuple(treelib._to_srgb(v) for v in mats.PALETTE[n][0]) for n in names]


_GRASS_RAMP = _ramp(GRASS_RAMP)


def _along(colors, t):
    t = min(1.0, max(0.0, t)) * (len(colors) - 1)
    i = min(int(t), len(colors) - 2)
    f = t - i
    a, b = colors[i], colors[i + 1]
    return tuple(a[k] + (b[k] - a[k]) * f for k in range(3))


def face_color(group, h, nz):
    """面の色（sRGB）。h は面の中心の高さ（モデルの高さに対する割合）、nz は面の法線の z 成分。"""
    if group == "bush":
        t = BUSH_T_BASE + BUSH_T_HEIGHT * h + BUSH_T_UP * max(0.0, nz)
        return treelib.leaf_color(min(BUSH_T_MAX, t))
    return _along(_GRASS_RAMP, h)


def base_scale(name, group):
    return MODEL_SCALE.get(name, BUSH_SCALE if group == "bush" else GRASS_SCALE)


def _normal(pts):
    a, b, c = pts
    return _unit3(_cross3(_sub3(b, a), _sub3(c, a)))


def _sub3(a, b):
    return (a[0] - b[0], a[1] - b[1], a[2] - b[2])


def _cross3(a, b):
    return (a[1] * b[2] - a[2] * b[1], a[2] * b[0] - a[0] * b[2], a[0] * b[1] - a[1] * b[0])


def _unit3(v):
    n = math.sqrt(v[0] * v[0] + v[1] * v[1] + v[2] * v[2])
    return (0.0, 0.0, 0.0) if n < 1e-12 else (v[0] / n, v[1] / n, v[2] / n)


def to_z_up(vertices, scale):
    """Kenney の (x, y, z)（Y が上）を Blender の (x, -z, y)（Z が上）にして拡大する。
    X 軸まわりの +90 度の回転なので、面の巻き方向（外向き）は変わらない。"""
    return [(x * scale, -z * scale, y * scale) for x, y, z in vertices]


def model_mesh(name, model, scale):
    """Kenney のモデル 1 点を MeshBuilder にする。足元は z = 0、面の色は材質 `leaf` に焼く。

    面は自分の法線の側へ PAIR_GAP / 2 だけ動かす。重なった表と裏は PAIR_GAP だけ離れ、どちらの側から
    見ても手前の面が深さで勝つ。色は、動かす前の高さと向きで決める。"""
    verts = to_z_up(model["vertices"], scale)
    top = max(v[2] for v in verts)
    half = PAIR_GAP / 2.0
    mb = MeshBuilder(name)
    for tri in model["triangles"]:
        pts = [verts[k] for k in tri]
        n = _normal(pts)
        h = sum(p[2] for p in pts) / (3.0 * top)
        front = [(p[0] + n[0] * half, p[1] + n[1] * half, p[2] + n[2] * half) for p in pts]
        mb.add_face(front, "leaf", face_color(model["group"], h, n[2]))
    return mb


def build_prototypes(models=None, collection=None):
    """原型を Blender のオブジェクトにする。[(モデル名, オブジェクト)]（名前順）。"""
    if models is None:
        models = load()["models"]
    out = []
    for name in sorted(models):
        model = models[name]
        mb = model_mesh("shrub_mesh_%s" % name, model, base_scale(name, model["group"]))
        out.append((name, mb.to_object(collection)))
    return out


# --------------------------------------------------------------------------- #
#  置いてはいけない所
# --------------------------------------------------------------------------- #
class _Segments:
    """線分の検索格子。線分ごとの reach（その距離以内は避ける）を持つ。"""

    def __init__(self):
        self.cells = {}

    def add(self, a, b, reach):
        item = (a, b, reach)
        i0, i1 = sorted((int(math.floor(a[0] / GRID_CELL)), int(math.floor(b[0] / GRID_CELL))))
        j0, j1 = sorted((int(math.floor(a[1] / GRID_CELL)), int(math.floor(b[1] / GRID_CELL))))
        for j in range(j0 - 1, j1 + 2):
            for i in range(i0 - 1, i1 + 2):
                self.cells.setdefault((i, j), []).append(item)

    def near(self, p, extra=0.0):
        """p から reach + extra 以内にある線分があれば True。"""
        key = (int(math.floor(p[0] / GRID_CELL)), int(math.floor(p[1] / GRID_CELL)))
        for a, b, reach in self.cells.get(key, ()):
            if geom.dist_point_segment(p, a, b) < reach + extra:
                return True
        return False


def host_loops(data):
    """足元に低木を植える建物の外形（反時計回り）。本体と、本体に付く部分。"""
    out = []
    for b in data["buildings"]:
        style = b.get("style") or "background"
        if not (b.get("on_campus") and style in buildings.BUILDERS):
            continue
        for fp in [b["footprint"]] + [pt["footprint"] for pt in b.get("parts") or []]:
            loop = geom.ensure_ccw(geom.dedup(fp))
            if len(loop) >= 3:
                out.append((b["id"], loop))
    return out


class Keepout:
    """低木と草むらを置いてはいけない所。reason() が理由（無ければ None）を返す。

    data      : build_campus.load_data + parklib.apply_buildings を通した campus.json
    ctx       : build_campus の ctx（door_frames, sign_placed, mall_beds を読む）
    hard_pre  : 建物の前に作った hard の占有（水盤の縁石・公園の池と橋）
    trees     : [(x, y, species, scale, rot)]
    """

    def __init__(self, data, frame, ctx, hard_pre, trees):
        self.frame = frame
        self.hard_pre = hard_pre
        self.boundary = geom.ensure_ccw(geom.dedup(data["campus_boundary"]))
        self.rects = (
            [("mall", (site.MALL_U0, site.MALL_V - site.MALL_HW, site.MALL_U1, site.MALL_V + site.MALL_HW), MALL_CLEAR),
             ("mall", (site.MALL_U1, site.MALL_V - 11.0, site.MALL_U1 + 14.0, site.MALL_V + 11.0), MALL_CLEAR),
             ("lawn", site.LIBRARY_LAWN, LAWN_CLEAR)]
            + [("basin", (u0 - site.BASIN_RIM, v0 - site.BASIN_RIM, u1 + site.BASIN_RIM, v1 + site.BASIN_RIM), MALL_CLEAR)
               for u0, v0, u1, v1 in site.BASINS]
            + [("bed", (u0, site.BED_V[0], u1, site.BED_V[1]), BED_CLEAR) for u0, u1 in ctx.get("mall_beds") or []])
        self.parks = []
        self.grounds = []
        for a in data["areas"]:
            poly = geom.ensure_ccw(geom.dedup(a["polygon"]))
            if a["kind"] == "park":
                self.parks.append(poly)
            elif a["kind"] in ("pitch", "playground", "parking"):
                self.grounds.append(poly)

        self.hosts = host_loops(data)
        # 外形ごとの bbox と、辺の検索格子（すべての建物。背景の建物も避ける）
        self.loops = []
        self.walls = _Segments()
        for b in data["buildings"]:
            for fp in [b["footprint"]] + [pt["footprint"] for pt in b.get("parts") or []]:
                loop = geom.ensure_ccw(geom.dedup(fp))
                if len(loop) < 3:
                    continue
                self.loops.append((loop, geom.bbox(loop)))
                for k in range(len(loop)):
                    self.walls.add(loop[k], loop[(k + 1) % len(loop)], 0.0)
        self.paths = _Segments()
        for p in data["paths"]:
            reach = (p.get("width") or 2.5) * 0.5
            pts = p["points"]
            for k in range(len(pts) - 1):
                self.paths.add(pts[k], pts[k + 1], reach)

        doors = list((ctx.get("door_frames") or {}).values())
        self.doors = [tuple(dr["origin"]) for dr in doors]
        self.lanes = [entrances.corridor(dr) for dr in doors]
        self.signs = [tuple(s[1]) for s in ctx.get("sign_placed") or []]
        self.trees = [(t[0], t[1]) for t in trees]
        self.rejected = collections.Counter()    # 避けた理由ごとの回数（候補の試行ごと）

    @staticmethod
    def _in_rect(u, v, rect, margin):
        u0, v0, u1, v1 = rect
        return u0 - margin <= u <= u1 + margin and v0 - margin <= v <= v1 + margin

    def reason(self, x, y, kind):
        why = self._reason(x, y, kind)
        if why is not None:
            self.rejected[why] += 1
        return why

    def _reason(self, x, y, kind):
        p = (x, y)
        clear = CLEAR[kind]
        if not geom.point_in_poly(p, self.boundary):
            return "boundary"
        u, v = self.frame.uv(p)
        for name, rect, margin in self.rects:
            if self._in_rect(u, v, rect, margin):
                return name
        if self.hard_pre.blocked(x, y):
            return "water"
        for poly in self.parks:
            if geom.point_in_poly(p, poly):
                return "park"
        for poly in self.grounds:
            if geom.point_in_poly(p, poly) or geom.dist_point_poly_edges(p, poly) < GROUND_CLEAR:
                return "ground"
        for loop, (x0, y0, x1, y1) in self.loops:
            if x0 <= x <= x1 and y0 <= y <= y1 and geom.point_in_poly(p, loop):
                return "building"
        if self.walls.near(p, clear["building"]):
            return "building"
        if self.paths.near(p, clear["path"]):
            return "path"
        for o in self.doors:
            if math.hypot(x - o[0], y - o[1]) < DOOR_CLEAR:
                return "door"
        for lane in self.lanes:
            if geom.point_in_poly(p, lane) or geom.dist_point_poly_edges(p, lane) < LANE_CLEAR:
                return "door"
        for s in self.signs:
            if math.hypot(x - s[0], y - s[1]) < SIGN_CLEAR:
                return "sign"
        for t in self.trees:
            if math.hypot(x - t[0], y - t[1]) < clear["tree"]:
                return "tree"
        return None


# --------------------------------------------------------------------------- #
#  配置
# --------------------------------------------------------------------------- #
class _Placed:
    """置いた物の重なり判定（同じ種類どうしは CLEAR[kind]["crowd"] 以上離す）。"""

    def __init__(self):
        self.by_kind = {"bush": [], "grass": []}

    def crowded(self, x, y, kind):
        r = CLEAR[kind]["crowd"]
        return any(math.hypot(x - q[0], y - q[1]) < r for q in self.by_kind[kind])

    def add(self, x, y, kind):
        self.by_kind[kind].append((x, y))


def _pick(rng, names, weights):
    return rng.choices(names, weights=weights, k=1)[0]


# 植え込みに使う低木の重み。大きさと形の幅が出るよう、ありふれた形を多めにする
BUSH_WEIGHTS = {
    "plant_bush": 3, "plant_bushLarge": 3, "plant_bushDetailed": 2,
    "plant_bushSmall": 2, "plant_bushTriangle": 1, "plant_bushLargeTriangle": 1,
}
GRASS_WEIGHTS = {"grass_leafs": 3, "grass": 2, "grass_large": 1, "grass_leafsLarge": 1}


class _Pool:
    def __init__(self, models, weights):
        self.names = sorted(n for n in weights if n in models)
        self.weights = [weights[n] for n in self.names]
        self.models = models

    def pick(self, rng):
        return _pick(rng, self.names, self.weights)

    def item(self, rng, x, y, name=None):
        """(x, y, モデル名, 大きさの揺らぎ, 向き)。原型は base_scale で作るので、Empty の大きさは揺らぎだけ。"""
        name = name or self.pick(rng)
        return (x, y, name, rng.uniform(*JITTER), rng.uniform(0.0, 2.0 * math.pi))


def _walk_edge(keep, rng, pools, placed, a, b):
    """壁 a->b（反時計回りの外形の辺）に沿って、植え込みと空きを交互に作る。植え込みのリストを返す。"""
    length = geom.length(geom.sub(b, a))
    d = geom.normalize(geom.sub(b, a))
    n = geom.outward_normal(a, b)
    beds = []
    s = rng.uniform(0.0, GAP_LEN[1])

    def at(t, off):
        return (a[0] + d[0] * t + n[0] * off, a[1] + d[1] * t + n[1] * off)

    while s < length - CORNER_SKIP:
        end = min(s + rng.uniform(*BED_LEN), length - CORNER_SKIP)
        base = pools["bush"].pick(rng)
        bed = {"bushes": [], "tufts": []}
        stops = []
        t = s + CORNER_SKIP
        while t < end:
            p = at(t, WALL_OFFSET + rng.uniform(-WALL_JITTER, WALL_JITTER))
            name = base if rng.random() < 0.7 else pools["bush"].pick(rng)
            if keep.reason(p[0], p[1], "bush") is None and not placed.crowded(p[0], p[1], "bush"):
                bed["bushes"].append(pools["bush"].item(rng, p[0], p[1], name))
                placed.add(p[0], p[1], "bush")
                stops.append(t)
            t += rng.uniform(*BUSH_STEP)
        if stops:
            # 植え込みの両端の外に草むらを 1 つずつ
            for tt in (stops[0] - END_TUFT, stops[-1] + END_TUFT):
                p = at(tt, END_TUFT_OFFSET)
                if not 0.0 < tt < length:
                    continue
                if keep.reason(p[0], p[1], "grass") is None and not placed.crowded(p[0], p[1], "grass"):
                    bed["tufts"].append(pools["grass"].item(rng, p[0], p[1]))
                    placed.add(p[0], p[1], "grass")
            beds.append(bed)
        s = end + rng.uniform(*GAP_LEN)
    return beds


def _near_hosts(hosts, x, y, reach):
    return any(geom.dist_point_poly_edges((x, y), loop) < reach for _bid, loop in hosts)


def plan(data, frame, ctx, trees, hard_pre, rng, models=None,
         max_bushes=MAX_BUSHES, max_grass=MAX_GRASS, stats=None):
    """低木と草むらの置き場所を決める。[(x, y, モデル名, 大きさ, 向き)]（低木の後に草むら）。

    stats に dict を渡すと、避けた理由ごとの数を入れる（"rejected"）。
    """
    if models is None:
        models = load()["models"]
    pools = {"bush": _Pool(models, BUSH_WEIGHTS), "grass": _Pool(models, GRASS_WEIGHTS)}
    keep = Keepout(data, frame, ctx, hard_pre, trees)
    placed = _Placed()

    beds = []
    for _bid, loop in keep.hosts:
        for k in range(len(loop)):
            a, b = loop[k], loop[(k + 1) % len(loop)]
            if geom.length(geom.sub(b, a)) >= 2.0 * CORNER_SKIP + BUSH_STEP[0]:
                beds.extend(_walk_edge(keep, rng, pools, placed, a, b))

    # 上限を超えるときは、植え込みを丸ごと間引く（壁の途中で途切れた列を作らない）
    def counts():
        return sum(len(b["bushes"]) for b in beds), sum(len(b["tufts"]) for b in beds)

    wanted_bushes = counts()[0]
    while beds and counts()[0] > max_bushes:
        beds.pop(rng.randrange(len(beds)))

    bushes = [it for b in beds for it in b["bushes"]]
    grasses = [it for b in beds for it in b["tufts"]]

    # 木の根元: 建物の近くの木のうち TREE_TUFT_RATE
    near_trees = [t for t in trees if _near_hosts(keep.hosts, t[0], t[1], TREE_TUFT_NEAR)]
    for t in near_trees:
        if rng.random() >= TREE_TUFT_RATE:
            continue
        ang = rng.uniform(0.0, 2.0 * math.pi)
        r = rng.uniform(*TREE_TUFT_RADIUS)
        x, y = t[0] + math.cos(ang) * r, t[1] + math.sin(ang) * r
        if keep.reason(x, y, "grass") is None and not placed.crowded(x, y, "grass"):
            grasses.append(pools["grass"].item(rng, x, y))
            placed.add(x, y, "grass")
    wanted_grass = len(grasses)
    while len(grasses) > max_grass:
        grasses.pop(rng.randrange(len(grasses)))

    if stats is not None:
        stats["hosts"] = len(keep.hosts)
        stats["near_trees"] = len(near_trees)
        stats["wanted"] = (wanted_bushes, wanted_grass)    # 上限で間引く前の数
        stats["rejected"] = dict(keep.rejected)
    return bushes + grasses


# --------------------------------------------------------------------------- #
#  集計と書き出し
# --------------------------------------------------------------------------- #
def triangle_total(items, models):
    return sum(len(models[it[2]]["triangles"]) for it in items)


def sidecar(items, models):
    """shrubs.json の中身（trees.json と同じ作り）。"""
    return {
        "models": sorted(models),
        "shrubs": [{"i": i, "x": round(x, 3), "y": round(y, 3), "model": name,
                    "kind": models[name]["group"], "scale": round(sc, 3), "rot": round(rot, 4)}
                   for i, (x, y, name, sc, rot) in enumerate(items)],
    }
