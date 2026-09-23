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

# モール北側の花壇（航空写真では舗装の北に幅 3〜4 m の植栽帯が u -10..170 で続く。#56）
BED_V = (MALL_V + MALL_HW - 3.6, MALL_V + MALL_HW)   # v -21.6..-18.0
BED_LEN = 9.0        # 1 基の長さ
BED_GAP = 3.0        # 花壇どうしの間（モールから北へ抜けられる）
BED_WALL = 0.2       # 縁石の厚み
BED_TOP = 0.45       # 縁石の天端。stepOffset 0.40 より高いので、歩いては上がれない
BED_SOIL = 0.38      # 土の面
BED_KEEP = 1.5       # 通路・入口の脇に空ける幅
FLOWERS = ("flower_red", "flower_yellow", "flower_white", "flower_pink")

# 図書館を囲む堀のような水盤（水面の矩形 (u0, v0, u1, v1) のリスト）。
# 実物は図書館の東面と南面に沿う幅 約 10 m の帯で、モール（v -30..-18）が橋になって
# 入口へ渡る。航空写真（国土地理院 z18）に campus.json の外形を重ねて測った (#56)。
# 以前は図書館の東 60 m の芝生に 60 x 25 m の池を 1 枚置いていたが、実物にそんな池は無い。
# 縁石（BASIN_RIM）の外がモールの舗装・図書館の北東角・南の歩道に掛からないよう、
# 矩形の端はそこから縁石の幅だけ引いてある。
# Unity 側の CampusStage.Basins と同じ数値にすること（BasinKeepoutTests が突き合わせる）。
BASINS = [
    (-59.5, -16.4, -50.0, 24.0),    # 東の堀（モールの北）
    (-59.5, -78.0, -50.0, -31.6),   # 東の堀（モールの南）
    (-100.0, -78.0, -59.5, -66.0),  # 南の池（図書館の南面）
]
BASIN_RIM = 1.6     # 縁石の幅（腰かけられる）
# basin_edges の許容差（m）。辺が同じ直線上にあるとみなす差と、捨てる切れ端の長さ。
# CampusStage.UncoveredSpans と同じ値にする（blender/tests/fixtures/basin_edges.json で突き合わせる）
BASIN_EDGE_EPS = 1e-4
# 図書館の東の芝生広場 (u0, v0, u1, v1)。以前の池の跡。実物も木の無い芝生
LIBRARY_LAWN = (-46.0, -100.0, 8.0, -32.0)


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


def _overlaps(a, b):
    """uv 矩形どうしが面積を持って重なるか（辺で接するだけなら False）。"""
    return a[0] < b[2] and b[0] < a[2] and a[1] < b[3] and b[1] < a[3]


def basin_edges(rects=None, rim=BASIN_RIM):
    """水際の辺のうち、隣の水面と接していない部分（＝縁石と見えない壁を立てる所）。

    返り値は (axis, c, t0, t1, out, ext0, ext1) のリスト。
      axis "u": u = c の辺（t は v）。axis "v": v = c の辺（t は u）
      out  : 水の外へ向かう側（+1 / -1）
      ext0 / ext1: 端を縁石の幅だけ延ばすか。出隅は延ばして角を埋める。入隅（L 字の内側）の
                   ように、延ばした先が隣の水面に掛かる所は延ばさない。
    矩形が 1 枚なら 4 辺そのまま。Unity 側の CampusStage.BasinEdges が同じ計算をする
    （ext0 / ext1 を除く）。両者の一致は blender/tests/fixtures/basin_edges.json を介して
    pytest（test_site.py）と EditMode テスト（BasinEdgeAgreementTests）が確かめる。"""
    rects = list(BASINS if rects is None else rects)
    edges = []
    for i, r in enumerate(rects):
        u0, v0, u1, v1 = r
        for axis, c, t0, t1, out in (("v", v0, u0, u1, -1), ("v", v1, u0, u1, +1),
                                     ("u", u0, v0, v1, -1), ("u", u1, v0, v1, +1)):
            # 同じ直線上で、外側に接する別の水面が覆う区間を引く
            spans = [(t0, t1)]
            for j, o in enumerate(rects):
                if j == i:
                    continue
                if axis == "v":
                    face, a, b = (o[1] if out > 0 else o[3]), o[0], o[2]
                else:
                    face, a, b = (o[0] if out > 0 else o[2]), o[1], o[3]
                if abs(face - c) > BASIN_EDGE_EPS:
                    continue
                nxt = []
                for s0, s1 in spans:
                    if b <= s0 or a >= s1:
                        nxt.append((s0, s1))
                        continue
                    if a > s0:
                        nxt.append((s0, a))
                    if b < s1:
                        nxt.append((b, s1))
                spans = nxt
            for s0, s1 in spans:
                if s1 - s0 <= BASIN_EDGE_EPS:
                    continue
                ext = []
                for t, sgn in ((s0, -1), (s1, +1)):
                    # 延ばした先の角（縁石の幅の正方形）が水に掛かるなら延ばさない
                    ta, tb = sorted((t, t + sgn * rim))
                    ca, cb = sorted((c, c + out * rim))
                    sq = (ta, ca, tb, cb) if axis == "v" else (ca, ta, cb, tb)
                    ext.append(not any(_overlaps(sq, o) for o in rects))
                edges.append((axis, c, s0, s1, out, ext[0], ext[1]))
    return edges


def _edge_xy(frame, axis, c, t):
    return frame.xy(c, t) if axis == "u" else frame.xy(t, c)


def _mall_bed_gaps(data, frame, ctx):
    """花壇を置かない u の区間。モール北縁を横切る歩道と、北側の建物の入口の前。"""
    vc = sum(BED_V) * 0.5
    gaps = []
    for p in data.get("paths", []):
        w = p.get("width") or 2.5
        uv = [frame.uv(q) for q in p["points"]]
        for a, b in zip(uv, uv[1:]):
            if (a[1] - vc) * (b[1] - vc) >= 0:
                continue
            t = (vc - a[1]) / (b[1] - a[1])
            u = a[0] + t * (b[0] - a[0])
            gaps.append((u - w * 0.5 - BED_KEEP, u + w * 0.5 + BED_KEEP))
    for dr in (ctx.get("door_frames") or {}).values():
        u, v = frame.uv(dr["origin"])
        if v < BED_V[1] or v > BED_V[1] + 12.0:
            continue   # モールの北に面した入口だけ
        half = dr["APRON_S"] + BED_KEEP
        gaps.append((u - half, u + half))
    return gaps


def build_mall_beds(mb, frame, occ, data, ctx):
    """モール北側の立ち上がり花壇（石の縁石 + 土 + 花のかたまり）。

    長さ BED_LEN の花壇を BED_GAP おきに並べ、歩道と入口の前は空ける。縁石の天端 BED_TOP は
    stepOffset より高いので、プレイヤーも NPC も花壇の上は歩かない（ジャンプなら乗れる）。
    ctx["mall_beds"] に置いた花壇の (u0, u1) を返す。"""
    gaps = _mall_bed_gaps(data, frame, ctx)
    v0, v1 = BED_V
    w = BED_WALL
    placed = []
    u = MALL_U0 + 4.0
    k = 0
    while u + BED_LEN <= MALL_U1 - 14.0:
        u0, u1 = u, u + BED_LEN
        hit = [g for g in gaps if g[0] < u1 and g[1] > u0]
        if hit:
            # 通り道の手前で切る。短すぎれば通り道の先から始め直す
            cut = min(g[0] for g in hit)
            if cut - u0 >= 3.0:
                u1 = cut
            else:
                u = max(g[1] for g in hit)
                continue
        for r in ((u0, v0, u1, v0 + w), (u0, v1 - w, u1, v1),
                  (u0, v0 + w, u0 + w, v1 - w), (u1 - w, v0 + w, u1, v1 - w)):
            mb.add_prism(frame.rect(*r), Z_MALL, BED_TOP, "stone_dark", "stone_light")
        mb.add_ngon_flat(frame.rect(u0 + w, v0 + w, u1 - w, v1 - w), BED_SOIL, "bed_soil")
        _flowers(mb, frame, u0 + w, v0 + w, u1 - w, v1 - w, k)
        occ.stamp_poly(frame.rect(u0, v0, u1, v1))
        placed.append((u0, u1))
        k += 1
        u = u1 + BED_GAP
    ctx["mall_beds"] = placed


def _flowers(mb, frame, u0, v0, u1, v1, k):
    """花壇の植え込み。葉の丸い株を隙間なく 3 列に並べ、株の上と肩に小さな花を散らす。

    株は隣と少し重なる大きさにして土を隠す。花の色は株ごとに 1 色で、3 株ずつの塊にして
    列と花壇でずらす（一色の帯にしない）。"""
    rows = 3
    n = max(1, int(round((u1 - u0) / 0.95)))
    du = (u1 - u0) / n
    dv = (v1 - v0) / rows
    rad = 0.52 * max(du, dv)
    for r in range(rows):
        v = v0 + dv * (r + 0.5)
        for i in range(n):
            j = i * 7 + r * 13 + k * 5
            x, y = frame.xy(u0 + du * (i + 0.5) + 0.1 * ((j % 3) - 1), v)
            h = 0.20 + 0.05 * (j % 3)
            _plant(mb, x, y, rad, h, j, FLOWERS[(k + r + i // 3) % len(FLOWERS)])


# 株の断面（半径の割合, 高さの割合）。下から順に
_PLANT_RINGS = ((1.0, 0.0), (0.8, 0.6), (0.4, 1.0))


def _plant_z(t):
    """株の中心から半径の割合 t の所の表面の高さ（高さの割合）。"""
    for (ta, za), (tb, zb) in zip(_PLANT_RINGS[::-1], _PLANT_RINGS[-2::-1]):
        if t <= tb:
            return za if t <= ta else za + (zb - za) * (t - ta) / (tb - ta)
    return 0.0


def _plant(mb, x, y, rad, h, j, col, seg=7, blooms=7):
    """葉の丸い株（7 角の 2 段）と、その表面に付く小さな 3 角錐の花。"""
    rot = j * 0.9
    rings = [[(x + rad * t * math.cos(rot + math.pi * 2 * q / seg),
               y + rad * t * math.sin(rot + math.pi * 2 * q / seg),
               BED_SOIL + h * zf) for q in range(seg)] for t, zf in _PLANT_RINGS]
    for lo, hi in zip(rings, rings[1:]):
        for q in range(seg):
            q1 = (q + 1) % seg
            mb.add_quad(lo[q], lo[q1], hi[q1], hi[q], "flower_leaf")
    mb.add_face(rings[-1], "flower_leaf")
    for q in range(blooms):
        # 黄金角で散らし、半径は外ほど疎に（肩にも咲く）
        a = rot + q * 2.39996
        t = 0.82 * math.sqrt((q + 0.5) / blooms)
        fx, fy = x + rad * t * math.cos(a), y + rad * t * math.sin(a)
        fz = BED_SOIL + h * _plant_z(t) + 0.01
        br = 0.09
        base = [(fx + br * math.cos(a + math.pi * 2 * m / 3),
                 fy + br * math.sin(a + math.pi * 2 * m / 3), fz) for m in range(3)]
        top = (fx, fy, fz + 0.06)
        for m in range(3):
            mb.add_face([base[m], base[(m + 1) % 3], top], col)


def build_basin(mb, frame, occ):
    """図書館を囲む浅い水盤（石の底 + 縁石）。水面は build_water（site_water）。

    縁石は腰かけられる高さ（天端 Z_BASIN_RIM = 0.36、幅 BASIN_RIM）のまま残す。水に入れないのは
    Unity 側の CampusStage.BuildBasinKeepout が水際（basin_edges の辺）に見えない壁を立てるから
    であって、ここを柵で囲っているからではない（#46）。"""
    for u0, v0, u1, v1 in BASINS:
        mb.add_ngon_flat(frame.rect(u0, v0, u1, v1), Z_BASIN_FLOOR, "stone_dark")
    for axis, c, t0, t1, out, e0, e1 in basin_edges():
        co = c + out * BASIN_RIM
        s0 = t0 - (BASIN_RIM if e0 else 0.0)
        s1 = t1 + (BASIN_RIM if e1 else 0.0)
        # 縁石の天端（水際 c から外 co まで、s0..s1）
        mb.add_ngon_flat(_rect_uv(frame, axis, c, co, s0, s1), Z_BASIN_RIM, "stone_dark")
        # 内側（水の方）の立ち上がりは水際の区間だけ、外側は一番下の層から
        # （どの層の上に載っても隙間を出さない）。面は外（out）の向きに見える
        _wall(mb, frame, axis, c, t0, t1, -out, Z_BASIN_FLOOR, Z_BASIN_RIM)
        _wall(mb, frame, axis, co, s0, s1, out, Z_GROUND, Z_BASIN_RIM)
        # 延ばした端の小口
        if e0:
            _cap(mb, frame, axis, c, co, s0, -1)
        if e1:
            _cap(mb, frame, axis, c, co, s1, +1)
    for u0, v0, u1, v1 in BASINS:
        occ.stamp_poly(frame.rect(u0 - BASIN_RIM, v0 - BASIN_RIM, u1 + BASIN_RIM, v1 + BASIN_RIM),
                       margin=2.0)
    # 図書館の東、モールの南は木の無い芝生広場（航空写真）。散布の木を入れない
    occ.stamp_poly(frame.rect(*LIBRARY_LAWN))


def _rect_uv(frame, axis, c0, c1, t0, t1):
    ca, cb = sorted((c0, c1))
    return frame.rect(ca, t0, cb, t1) if axis == "u" else frame.rect(t0, ca, t1, cb)


def _wall(mb, frame, axis, c, t0, t1, facing, z0, z1):
    """u = c（axis "u"）または v = c の直線に立つ t0..t1 の壁。facing の側（+1 / -1）を表にする。"""
    a = _edge_xy(frame, axis, c, t0)
    b = _edge_xy(frame, axis, c, t1)
    # frame.xy は右手系（u → v が反時計回り）。u 辺で t（= v）が増える向きに進むと +u は右手、
    # v 辺で t（= u）が増える向きに進むと +v は左手。表は進行方向の右手に来る。
    right = +1 if axis == "u" else -1
    if facing != right:
        a, b = b, a
    mb.add_quad((a[0], a[1], z0), (b[0], b[1], z0), (b[0], b[1], z1), (a[0], a[1], z1), "stone_dark")


def _cap(mb, frame, axis, c, co, t, sgn):
    """縁石を延ばした端の小口（t の外側 sgn を表にする）。"""
    other = "v" if axis == "u" else "u"
    ca, cb = sorted((c, co))
    _wall(mb, frame, other, t, ca, cb, sgn, Z_GROUND, Z_BASIN_RIM)


def build_water(mb, frame):
    """水盤の水面だけを別メッシュ（site_water）にする。Unity 側で反射・屈折を付けるため。

    site_water は Unity 側で MeshCollider を付けない（CampusStage.DressCampus）。名前に water を
    含むメッシュは床にしないし、NavMesh にも焼かない（#46）。名前を変えるときは向こうも直すこと。"""
    for u0, v0, u1, v1 in BASINS:
        mb.add_ngon_flat(frame.rect(u0, v0, u1, v1), Z_WATER, "water")


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
    for u0, v0, u1, v1 in BASINS:
        hard.stamp_poly(frame.rect(u0 - 2.4, v0 - 2.4, u1 + 2.4, v1 + 2.4), margin=0.5)


def build_street_furniture(mb, frame, hard):
    """ベンチと照明柱。どちらもモール舗装の内側に置く（DESIGN §3.2: 20 m 間隔）。"""
    ang = math.atan2(frame.u[1], frame.u[0])
    u = MALL_U0 + 6.0
    while u < MALL_U1 - 6.0:
        for v, a in ((MALL_V - MALL_HW + 3.4, ang + math.pi),
                     (BED_V[0] - 1.6, ang)):          # 北は花壇の手前
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
