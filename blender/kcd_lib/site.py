"""外構: 地面・キャンパスモール・道路・水盤・公園・小物（DESIGN.md §3.2）。"""

import math
import random

from . import geom, props

# 高さレイヤ（z ファイティング回避のため段を付ける）
Z_GROUND = -0.30
Z_CAMPUS = 0.010
Z_AREA = 0.020
Z_ROAD = 0.040
Z_FOOT = 0.055
Z_LINE = 0.070
Z_MALL = 0.085
Z_WATER = 0.060

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
        z = Z_AREA if hard_kind else Z_CAMPUS - 0.004
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
            mb.add_ribbon(chunk, w, z, mat, thickness=0.06)
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
    """図書館南の浅い水盤（石の縁石 + 水面）。"""
    inner = frame.rect(BASIN_U[0], BASIN_V[0], BASIN_U[1], BASIN_V[1])
    outer = geom.offset_polygon(inner, 1.6)
    mb.add_ngon_flat(inner, Z_WATER, "water")
    n = len(inner)
    for i in range(n):
        a, b = inner[i], inner[(i + 1) % n]
        ao, bo = outer[i], outer[(i + 1) % n]
        mb.add_quad((ao[0], ao[1], 0.36), (bo[0], bo[1], 0.36),
                    (b[0], b[1], 0.36), (a[0], a[1], 0.36), "stone_dark")
        mb.add_quad((a[0], a[1], 0.36), (b[0], b[1], 0.36),
                    (b[0], b[1], Z_WATER), (a[0], a[1], Z_WATER), "stone_dark")
        mb.add_quad((ao[0], ao[1], Z_CAMPUS), (bo[0], bo[1], Z_CAMPUS),
                    (bo[0], bo[1], 0.36), (ao[0], ao[1], 0.36), "stone_dark")
    occ.stamp_poly(outer, margin=2.0)
    # 水盤とモールの間の石張り
    deck = frame.rect(BASIN_U[0], BASIN_V[1] + 1.8, BASIN_U[1], MALL_V - MALL_HW)
    mb.add_ngon_flat(deck, Z_FOOT, "stone_light")
    occ.stamp_poly(deck)


def build_basin_keepout(frame, hard):
    """水盤（と縁石）を「絶対に木を生やさない」側の占有に登録する。"""
    inner = frame.rect(BASIN_U[0], BASIN_V[0], BASIN_U[1], BASIN_V[1])
    hard.stamp_poly(geom.offset_polygon(inner, 2.4), margin=0.5)


def build_street_furniture(mb, frame, hard):
    """ベンチと照明柱。どちらもモール舗装の内側に置く（DESIGN §3.2: 20 m 間隔）。"""
    ang = math.atan2(frame.u[1], frame.u[0])
    u = MALL_U0 + 6.0
    while u < MALL_U1 - 6.0:
        for v, a in ((MALL_V - MALL_HW + 3.4, ang),
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
