"""外構: 地面・キャンパスモール・道路・水盤・公園・小物（DESIGN.md §3.2）。"""

import math
import random

from . import geom, props

# 高さレイヤ（z ファイティング回避のため 3 mm ずつ段を付ける）
# 歩ける面どうしの段差は最大でも モール 0.021 − 外周の地面 0.000 = 2.1 cm。
# 半径 0.28 m のカプセルが 2 cm の段に当たる角度は約 22 度なので、坂と同じに越えられる。
# 以前は 1.0〜7.5 cm の段と、敷地の縁に 31 cm の落差（外周 -0.30）があり、
# 歩くだけでジャンプ・着地の判定が出ていた（#30）。
# 意図した段差は 水盤の縁石（天端 0.36）だけ。入口の石張りは kcd_lib.entrances.APRON_Z。
Z_GROUND = 0.000     # 外周の地面（敷地の外）
Z_PARK = 0.003       # 公園・空地（敷地の芝より下。敷地の中では芝に隠れる）
Z_CAMPUS = 0.006     # キャンパス敷地の芝
Z_AREA = 0.009       # グラウンド・広場・駐車場
Z_ROAD = 0.012       # 車道（帯の厚み 0.06 m は地面の下へ埋まる）
Z_LINE = 0.015       # 車道の白線（厚みなし。車道から 3 mm）
Z_FOOT = 0.018       # 歩道・水盤の前の石張り
Z_MALL = 0.021       # キャンパスモールと正門前の広場
Z_WATER = 0.060      # 水盤の水面（縁石 0.36 の内側だけ）
# 水盤の底。水は半透明（mats.py の water: alpha 0.80、Unity 側も 0.58）なので底が透けて見える。
# 以前は Z_WATER - 0.45 = -0.390 で、敷地の芝（Z_CAMPUS = 0.006）と歩道（Z_FOOT = 0.018）が
# その上を覆っていた。実測では水面の下に見える面の 96.7% が grass、3.3% が stone_light で、
# 底の stone_dark は 1 点も見えていなかった（#46）。どの地面レイヤーより上（Z_MALL = 0.021 の上）
# かつ水面より下に置いて、石の底が水越しに見えるようにする。水深 3 cm の浅い水盤。
Z_BASIN_FLOOR = 0.030
Z_BASIN_RIM = 0.36   # 水盤の縁石の天端（意図した段差）
PATH_THICKNESS = 0.06   # 道路・歩道の帯の厚み（縁の隙間を隠す）

# キャンパスモール（OSM の直線 footway が v = -24 を u = -56..199 で走る）
MALL_V = -24.0
MALL_HW = 6.0
MALL_U0 = -57.0
MALL_U1 = 197.0

# 図書館前の水盤（DESIGN §3.2: 60 x 25 m）
BASIN_U = (-60.0, 0.0)
BASIN_V = (-60.0, -35.0)


class Occupancy:
    """2.5 m グリッドの占有マップ。樹木散布の除外判定に使う。"""

    CELL = 2.5

    def __init__(self, lo=-360.0, hi=360.0):
        self.lo = lo
        self.n = int((hi - lo) / self.CELL) + 1
        self.grid = bytearray(self.n * self.n)

    def _idx(self, x, y):
        i = int((x - self.lo) / self.CELL)
        j = int((y - self.lo) / self.CELL)
        if 0 <= i < self.n and 0 <= j < self.n:
            return j * self.n + i
        return None

    def stamp_disc(self, x, y, r):
        r_cells = int(r / self.CELL) + 1
        ci = int((x - self.lo) / self.CELL)
        cj = int((y - self.lo) / self.CELL)
        for j in range(cj - r_cells, cj + r_cells + 1):
            if not (0 <= j < self.n):
                continue
            for i in range(ci - r_cells, ci + r_cells + 1):
                if 0 <= i < self.n:
                    self.grid[j * self.n + i] = 1

    def stamp_poly(self, poly, margin=0.0):
        if len(poly) < 3:
            return
        x0, y0, x1, y1 = geom.bbox(poly)
        x0 -= margin
        y0 -= margin
        x1 += margin
        y1 += margin
        i0 = max(0, int((x0 - self.lo) / self.CELL))
        i1 = min(self.n - 1, int((x1 - self.lo) / self.CELL))
        j0 = max(0, int((y0 - self.lo) / self.CELL))
        j1 = min(self.n - 1, int((y1 - self.lo) / self.CELL))
        for j in range(j0, j1 + 1):
            py = self.lo + (j + 0.5) * self.CELL
            for i in range(i0, i1 + 1):
                px = self.lo + (i + 0.5) * self.CELL
                if geom.point_in_poly((px, py), poly) or (
                        margin > 0 and geom.dist_point_poly_edges((px, py), poly) < margin):
                    self.grid[j * self.n + i] = 1

    def stamp_polyline(self, pts, r):
        for p in geom.resample(pts, self.CELL * 0.8):
            self.stamp_disc(p[0], p[1], r)

    def blocked(self, x, y):
        k = self._idx(x, y)
        return True if k is None else bool(self.grid[k])


def _clip_polyline(pts, limit):
    """±limit の外に出る点で折れ線を分割する。"""
    out = []
    cur = []
    for p in pts:
        if abs(p[0]) <= limit and abs(p[1]) <= limit:
            cur.append(p)
        else:
            if len(cur) >= 2:
                out.append(cur)
            cur = []
    if len(cur) >= 2:
        out.append(cur)
    return out


def build_ground(mb, data, frame, occ=None):
    # 全体を覆う地面
    E = 350.0
    mb.add_ngon_flat([(-E, -E), (E, -E), (E, E), (-E, E)], Z_GROUND, "grass_dark")
    # キャンパス敷地
    mb.add_ngon_flat(geom.ensure_ccw(geom.dedup(data["campus_boundary"])), Z_CAMPUS, "grass")
    kind_mat = {"park": "grass", "pitch": "soil", "playground": "sand",
                "parking": "asphalt", "university": None}
    for a in data["areas"]:
        mat = kind_mat.get(a["kind"], "grass")
        if mat is None:
            continue
        poly = geom.ensure_ccw(geom.dedup(a["polygon"]))
        hard_kind = a["kind"] in ("pitch", "playground", "parking")
        z = Z_AREA if hard_kind else Z_PARK
        mb.add_ngon_flat(poly, z, mat)
        if hard_kind and occ is not None:
            # グラウンド・駐車場に木を生やさない
            occ.stamp_poly(poly, margin=1.0)


def build_paths(mb, data, occ):
    for p in data["paths"]:
        kind = p["kind"]
        w = p.get("width") or 2.5
        if kind in ("tertiary", "residential", "service"):
            mat, z = "asphalt", Z_ROAD
        else:
            mat, z = "stone_light", Z_FOOT
        for chunk in _clip_polyline(p["points"], 348.0):
            mb.add_ribbon(chunk, w, z, mat, thickness=PATH_THICKNESS)
            occ.stamp_polyline(chunk, w * 0.5 + 2.2)
            if kind == "tertiary":
                # センターラインの破線
                samples = geom.resample(chunk, 9.0)
                for i in range(0, len(samples) - 1):
                    a = samples[i]
                    b = geom.lerp(samples[i], samples[i + 1], 0.45)
                    mb.add_ribbon([a, b], 0.16, Z_LINE, "line_white")
                # 外側線
                for side in (-1, 1):
                    off = []
                    for i, q in enumerate(chunk):
                        nxt = chunk[min(i + 1, len(chunk) - 1)]
                        prv = chunk[max(i - 1, 0)]
                        d = geom.normalize(geom.sub(nxt, prv))
                        if d == (0.0, 0.0):
                            continue
                        nrm = (-d[1], d[0])
                        off.append(geom.add(q, geom.mul(nrm, side * (w * 0.5 - 0.45))))
                    if len(off) >= 2:
                        mb.add_ribbon(off, 0.14, Z_LINE, "line_white")


def build_mall(mb, frame, occ):
    """石畳（グレー 2 色の市松）の キャンパスモール。"""
    tile = 2.0
    nu = int((MALL_U1 - MALL_U0) / tile)
    nv = int((MALL_HW * 2) / tile)
    for iu in range(nu):
        u0 = MALL_U0 + iu * tile
        for iv in range(nv):
            v0 = MALL_V - MALL_HW + iv * tile
            mat = "stone_light" if (iu + iv) % 2 == 0 else "stone_dark"
            mb.add_ngon_flat(frame.rect(u0, v0, u0 + tile, v0 + tile), Z_MALL, mat)
    occ.stamp_poly(frame.rect(MALL_U0 - 2, MALL_V - MALL_HW - 2,
                              MALL_U1 + 2, MALL_V + MALL_HW + 2))
    # 正門前の広場
    plaza = frame.rect(MALL_U1, MALL_V - 11.0, MALL_U1 + 14.0, MALL_V + 11.0)
    mb.add_ngon_flat(plaza, Z_MALL, "stone_light")
    occ.stamp_poly(plaza)


def build_basin(mb, frame, occ):
    """図書館南の浅い水盤（石の縁石 + 水面）。

    縁石は腰かけられる高さ（天端 Z_BASIN_RIM = 0.36、幅 1.6 m）のまま残す。水に入れないのは
    Unity 側の CampusStage.BuildBasinKeepout が水際（内側矩形の 4 辺）に見えない壁を立てるから
    であって、ここを柵で囲っているからではない（#46）。"""
    inner = frame.rect(BASIN_U[0], BASIN_V[0], BASIN_U[1], BASIN_V[1])
    outer = geom.offset_polygon(inner, 1.6)
    mb.add_ngon_flat(inner, Z_BASIN_FLOOR, "stone_dark")   # 水面は build_water（site_water）
    n = len(inner)
    for i in range(n):
        a, b = inner[i], inner[(i + 1) % n]
        ao, bo = outer[i], outer[(i + 1) % n]
        mb.add_quad((ao[0], ao[1], Z_BASIN_RIM), (bo[0], bo[1], Z_BASIN_RIM),
                    (b[0], b[1], Z_BASIN_RIM), (a[0], a[1], Z_BASIN_RIM), "stone_dark")
        mb.add_quad((a[0], a[1], Z_BASIN_RIM), (b[0], b[1], Z_BASIN_RIM),
                    (b[0], b[1], Z_BASIN_FLOOR), (a[0], a[1], Z_BASIN_FLOOR), "stone_dark")
        # 外側の立ち上がりは一番下の層から（どの層の上に載っても隙間を出さない）
        mb.add_quad((ao[0], ao[1], Z_GROUND), (bo[0], bo[1], Z_GROUND),
                    (bo[0], bo[1], Z_BASIN_RIM), (ao[0], ao[1], Z_BASIN_RIM), "stone_dark")
    occ.stamp_poly(outer, margin=2.0)
    # 水盤とモールの間の石張り
    deck = frame.rect(BASIN_U[0], BASIN_V[1] + 1.8, BASIN_U[1], MALL_V - MALL_HW)
    mb.add_ngon_flat(deck, Z_FOOT, "stone_light")
    occ.stamp_poly(deck)


def build_water(mb, frame):
    """水盤の水面だけを別メッシュ（site_water）にする。Unity 側で反射・屈折を付けるため。

    site_water は Unity 側で MeshCollider を付けない（CampusStage.DressCampus）。名前に water を
    含むメッシュは床にしないし、NavMesh にも焼かない（#46）。名前を変えるときは向こうも直すこと。"""
    inner = frame.rect(BASIN_U[0], BASIN_V[0], BASIN_U[1], BASIN_V[1])
    mb.add_ngon_flat(inner, Z_WATER, "water")


def build_signs(mb, frame, ctx):
    """各建物の入口脇に立て看板。ctx["sign_placed"] に (id, (x, y), z, yaw) を返す。

    ctx["sign"] の要素が (id, (x, y), z, yaw) なら、その位置と向きにそのまま立てる
    （kcd_lib.entrances が扉の脇に計画したもの）。(id, (x, y), z) なら、板の正面を
    建物の重心から入口へ向かう向き（＝建物から離れる向き）にして 3.5 m 横へずらす。"""
    cents = {fid: geom.centroid(fp) for fid, fp, _h in ctx.get("footprints", [])}
    placed = []
    for item in ctx["sign"]:
        if len(item) == 4:
            sid, p, z, yaw = item
            props.add_pylon_sign(mb, p[0], p[1], yaw, z)
            placed.append((sid, p, z, yaw))
            continue
        sid, pos, z = item
        c = cents.get(sid)
        d = geom.sub(pos, c) if c else (0.0, -1.0)
        if geom.length(d) < 1e-6:
            d = (0.0, -1.0)
        d = geom.normalize(d)
        side = (-d[1], d[0])
        p = (pos[0] + side[0] * 3.5, pos[1] + side[1] * 3.5)
        yaw = math.atan2(d[1], d[0])
        props.add_pylon_sign(mb, p[0], p[1], yaw, z)
        placed.append((sid, p, z, yaw))
    ctx["sign_placed"] = placed


def build_bike_sheds(mb, frame, occ, hard, ctx, roads=None):
    """屋根付き駐輪場。講義棟の北側（駐車場の縁）と、正門（モール東端）の近く。
    campus.json に学生寮は無いので、寮の分は正門側で代替する。
    roads: 道路・歩道だけの占有。講義棟北は OSM の駐車場エリアなので occ では全面
    ブロックされる。そこは建物 (hard) と道路 (roads) だけを避ける。"""
    ang = math.atan2(frame.u[1], frame.u[0])
    uvbb = ctx.get("uvbb", {})
    cands = []
    if "lecture" in uvbb:
        u0, v0, u1, v1 = uvbb["lecture"]
        cands.append(("lecture_north", (u0 + u1) * 0.5, v1 + 5.4, 14.0, ang,
                      [hard] + ([roads] if roads else [])))
    cands.append(("main_gate", MALL_U1 - 10.0, MALL_V + MALL_HW + 9.0, 12.0, ang, [occ]))
    placed = []
    for name, uc, vc, length, a, checks in cands:
        done = False
        for dv in (0.0, 3.0, 6.0, 9.0):
            for du in (0.0, -6.0, 6.0, -12.0, 12.0, -18.0, 18.0):
                u, v = uc + du, vc + dv
                rect = frame.rect(u - length * 0.5 - 0.5, v - 1.7, u + length * 0.5 + 0.5, v + 1.7)
                probe = list(rect) + [frame.xy(u, v), frame.xy(u - length * 0.25, v),
                                      frame.xy(u + length * 0.25, v)]
                if any(o.blocked(p[0], p[1]) for o in checks for p in probe):
                    continue
                p = frame.xy(u, v)
                props.add_bike_shed(mb, p[0], p[1], a, length=length)
                occ.stamp_poly(rect, margin=1.5)
                hard.stamp_poly(rect, margin=1.0)
                placed.append((name, u, v))
                done = True
                break
            if done:
                break
    ctx["bike_sheds"] = placed


def build_amenities(mb_vend, mb_trash, frame, ctx):
    """食堂（第2研究棟）とコンビニ（共創棟）の入口脇に自販機 2 台とゴミ箱 1 個。

    ctx["door_frames"]（kcd_lib.entrances）があれば、扉の脇（看板と反対側）の外壁に
    背中を付けて並べる。無ければ従来どおり外接矩形から決める。"""
    doors = ctx.get("door_frames") or {}
    placed = set()
    for bid in ("research2", "kyoso"):
        dr = doors.get(bid)
        if dr is None:
            continue
        o, n, t = dr["origin"], dr["n"], dr["t"]
        side = -dr["sign_side"]
        s0 = dr["APRON_S"] + 0.7          # 足元の石張りの外から

        def at(s, d):
            return (o[0] + t[0] * s + n[0] * d, o[1] + t[1] * s + n[1] * d)

        for k, col in enumerate(("vending_red", "vending_blue")):
            p = at(side * (s0 + k * 1.2), 0.45)
            props.add_vending(mb_vend, p[0], p[1], dr["yaw"], col)
        p = at(side * (s0 + 2.5), 0.45)
        props.add_trash_can(mb_trash, p[0], p[1])
        placed.add(bid)

    uvbb = ctx.get("uvbb", {})
    ang_u = math.atan2(frame.u[1], frame.u[0])
    spots = []
    if "research2" in uvbb and "research2" not in placed:
        u0, v0, u1, v1 = uvbb["research2"]
        spots.append(((u0 + u1) * 0.5 + 7.0, v0 - 0.95, ang_u - math.pi * 0.5))   # 正面 -v
    if "kyoso" in uvbb and "kyoso_mall_v" in ctx and "kyoso" not in placed:
        u0, v0, u1, v1 = uvbb["kyoso"]
        sb_u = u0 + (u1 - u0) * 0.30
        spots.append((sb_u - 3.0, ctx["kyoso_mall_v"] + 1.0, ang_u + math.pi * 0.5))  # 正面 +v
    for u, v, face in spots:
        for k, col in enumerate(("vending_red", "vending_blue")):
            p = frame.xy(u + k * 1.2, v)
            props.add_vending(mb_vend, p[0], p[1], face, col)
        p = frame.xy(u + 2.6, v)
        props.add_trash_can(mb_trash, p[0], p[1])


def build_basin_keepout(frame, hard):
    """水盤（と縁石）を「絶対に木を生やさない」側の占有に登録する。"""
    inner = frame.rect(BASIN_U[0], BASIN_V[0], BASIN_U[1], BASIN_V[1])
    hard.stamp_poly(geom.offset_polygon(inner, 2.4), margin=0.5)


def build_street_furniture(mb, frame, hard):
    """ベンチと照明柱。どちらもモール舗装の内側に置く（DESIGN §3.2: 20 m 間隔）。"""
    ang = math.atan2(frame.u[1], frame.u[0])
    u = MALL_U0 + 6.0
    while u < MALL_U1 - 6.0:
        for v, a in ((MALL_V - MALL_HW + 3.4, ang + math.pi),
                     (MALL_V + MALL_HW - 3.4, ang)):
            p = frame.xy(u, v)
            if not hard.blocked(p[0], p[1]):
                props.add_bench(mb, p[0], p[1], a)
        u += 20.0
    u = MALL_U0 + 16.0
    while u < MALL_U1 - 6.0:
        for v in (MALL_V - MALL_HW + 0.8, MALL_V + MALL_HW - 0.8):
            p = frame.xy(u, v)
            if not hard.blocked(p[0], p[1]):
                props.add_lamp(mb, p[0], p[1], 4.0)
        u += 20.0


def collect_trees(data, frame, occ, hard, rng, park_density=1.0 / 150.0, max_trees=1200):
    """(x, y, species_index, scale, rot) のリストを返す。

    occ  : 舗装や道路も含む全占有（散布の抑制に使う）
    hard : 建物・水盤だけの占有（列植でも絶対に侵入してはいけない領域）
    """
    trees = []

    # 1) モール両脇の列植（8 m 間隔）。舗装の内側 1.4 m に植栽帯を取るので、
    #    モールの外の建物へ入り込むことはない。
    u = MALL_U0 + 4.0
    while u < MALL_U1 - 4.0:
        for v, off in ((MALL_V - MALL_HW + 1.4, 0.0), (MALL_V + MALL_HW - 1.4, 4.0)):
            p = frame.xy(u + off, v)
            if hard.blocked(p[0], p[1]):
                continue
            trees.append((p[0], p[1], 0, rng.uniform(0.92, 1.12), rng.uniform(0, 6.28)))
        u += 8.0

    # 2) 敷地境界沿いの列植
    bd = geom.ensure_ccw(geom.dedup(data["campus_boundary"]))
    inner = geom.offset_polygon(bd, -7.0)
    for i in range(len(inner)):
        a, b = inner[i], inner[(i + 1) % len(inner)]
        for p in geom.resample([a, b], 9.0):
            if not occ.blocked(p[0], p[1]):
                trees.append((p[0], p[1], rng.choice([0, 0, 1, 2]),
                              rng.uniform(0.85, 1.15), rng.uniform(0, 6.28)))

    # 3) 公園・空地へのランダム散布
    polys = [geom.ensure_ccw(geom.dedup(a["polygon"])) for a in data["areas"]
             if a["kind"] in ("park",)]
    polys.append(bd)
    pts = props.scatter_positions(polys, park_density, occ.blocked, rng)
    for p in pts:
        trees.append((p[0], p[1], rng.choice([0, 1, 1, 2]),
                      rng.uniform(0.8, 1.25), rng.uniform(0, 6.28)))

    # 重なりを間引く（4 m グリッドに 1 本）
    seen = set()
    out = []
    for t in trees:
        key = (int(t[0] / 4.0), int(t[1] / 4.0))
        if key in seen:
            continue
        seen.add(key)
        out.append(t)
        if len(out) >= max_trees:
            break
    return out
