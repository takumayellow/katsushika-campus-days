"""第2研究棟の仕上げ: 1F の混ぜ張りの床板、2F の幅の広い板とコンセントの蓋、風除室の黒いマット、
平天井の 4 方向カセットエアコンと黒いガラリ、1F の曲面のガラスの白いロールスクリーン。

躯体（plan_research2_shell）の床・天井の上に薄く重ねる。座標は plan_research2_dims を参照。
床板は躯体の床の色（1F は floor_1f_wood、2F は floor_2f_oak）を地にして、その上に別の色の板を置く。
"""

import math

from . import kit, shell
from . import plan_research2_2f as P2
from . import plan_research2_dims as D
from . import plan_research2_poly as P
from . import plan_research2_shell as S

DARK = "research2_floor_1f_dark"
HONEY = "research2_floor_1f_honey"
OAK2 = "research2_floor_2f_honey"
MAT = "research2_floor_mat_black"
GRILLE = "research2_grille_black"
CASSETTE = "research2_cassette_white"
ROLL = "research2_roll_white"
OUTLET = "research2_stainless"

BOARD_UP = 0.002
BOARD1 = (0.3, 2.0, 4.0)       # 1F の板（幅, 長さの下限, 上限）
BOARD2 = (0.45, 2.4, 3.6)      # 2F の幅の広い板
GLASS_GAP2 = 0.3               # 2F の板をガラスの線から離す距離
VEST_MAT = (-1.5, 6.1, 1.5, 8.1)
WC_RECT = (4.6, 6.2, 9.2, D.WC[3] + 0.16)
HALL_WALL_RECT = (-5.2, 5.9, -3.85, 13.6)
CASSETTE_HALF = 0.42
ROLL_OFF = 0.34                # 外面から布の面まで
ROLL_Z = (1.8, D.SOFFIT_Z)
ROLL_BOX = (0.32, 0.44, 3.08, D.SOFFIT_Z - 0.005)   # 巻き取りの箱（外面からの距離 2 つ, z 2 つ）
ROLL_EDGES = D.E_TERRACE + (D.E_SW,)
ROLL_SEG = 1.5                 # 桟の間隔（plan_research2_shell.wall の既定）


def _hash(a, b):
    return ((a * 73856093) ^ (b * 19349663)) & 0xFFFF


# --------------------------------------------------------------------------- #
#  床板
# --------------------------------------------------------------------------- #
def _xspans(poly, y):
    """水平線 y が多角形の内側を通る x の区間（偶奇則）。"""
    xs = []
    n = len(poly)
    for i in range(n):
        a, b = poly[i], poly[(i + 1) % n]
        if (a[1] <= y < b[1]) or (b[1] <= y < a[1]):
            xs.append(a[0] + (b[0] - a[0]) * (y - a[1]) / (b[1] - a[1]))
    xs.sort()
    return list(zip(xs[0::2], xs[1::2]))


def _isect(spans_a, spans_b):
    out = []
    for a0, a1 in spans_a:
        for b0, b1 in spans_b:
            lo, hi = max(a0, b0), min(a1, b1)
            if hi - lo > 1e-6:
                out.append((lo, hi))
    return sorted(out)


def _minus(spans, lo, hi):
    out = []
    for a0, a1 in spans:
        if hi <= a0 or lo >= a1:
            out.append((a0, a1))
            continue
        if lo > a0:
            out.append((a0, lo))
        if hi < a1:
            out.append((hi, a1))
    return out


def row_spans(polys, y0, y1, x_lo, x_hi, rects=()):
    """幅 y0〜y1 の帯のうち、すべての多角形の内側にあり rects に掛からない x の区間。"""
    spans = [(x_lo, x_hi)]
    for poly in polys:
        for y in (y0 + 1e-3, y1 - 1e-3):
            spans = _isect(spans, _xspans(poly, y))
    for rx0, ry0, rx1, ry1 in rects:
        if ry0 < y1 and ry1 > y0:
            spans = _minus(spans, rx0, rx1)
    return spans


def _off_glass(xa, y0, y1, xb):
    """2F の帯の西端をガラスの線（D.GLASS2）から GLASS_GAP2 以上離れるまで東へ寄せる。"""
    segs = list(zip(D.GLASS2, D.GLASS2[1:]))
    while xa < xb:
        if all(P.seg_dist((xa, y), a, b) >= GLASS_GAP2 for y in (y0, y1) for a, b in segs):
            return xa
        xa += 0.05
    return xb


def boards(mb, polys, z, y_lo, y_hi, x_lo, x_hi, rects, size, pick, glass=False):
    """帯を幅 size[0] で並べ、長さ size[1]〜size[2] の板に切って pick(ハッシュ) の材質で貼る。

    pick が None を返した板は貼らない（躯体の床の色がそのまま見える）。行ごとに継ぎ目をずらす。
    """
    width, l_min, l_max = size
    z += BOARD_UP
    r = 0
    y = y_lo
    while y + width <= y_hi + 1e-6:
        for xa, xb in row_spans(polys, y, y + width, x_lo, x_hi, rects):
            if glass:
                xa = _off_glass(xa, y, y + width, xb)
            x = xa - (_hash(r, 7) % 100) / 100.0 * l_min
            k = 0
            while x < xb:
                length = l_min + (_hash(r, k) % 101) / 100.0 * (l_max - l_min)
                s0, s1 = max(x, xa), min(x + length, xb)
                mat = pick(_hash(k + 31, r + 1000))
                if mat and s1 - s0 > 0.2:
                    kit.plate(mb, s0 + 0.003, y + 0.003, s1 - 0.003, y + width - 0.003, z, mat)
                x += length
                k += 1
        y += width
        r += 1


def _pick1(h):
    return (None, DARK, HONEY, HONEY, DARK, None, HONEY)[h % 7]


def _pick2(h):
    return OAK2 if h % 2 else None


def _boards_1f(c):
    rects = (WC_RECT, D.VEST, D.FRONT_HOLE, D.BACK_HOLE, S.KITCHEN_RECT)
    boards(c.floor, (S.FLOOR1, D.INNER), D.Z1, D.CLIP_Y, D.KITCHEN_Y, D.CLIP_X, D.PART_X,
           rects, BOARD1, _pick1)


def _boards_2f(c):
    pad = 0.1
    rects = tuple((h[0] - pad, h[1] - pad, h[2] + pad, h[3] + pad)
                  for h in (D.FRONT_HOLE, D.BACK_HOLE, D.R_STAIR_HOLE))
    y_lo = min(p[1] for p in D.FLOOR2)
    boards(c.floor, (D.FLOOR2, D.INNER), D.Z2, y_lo, D.COUNTER2_Y, min(p[0] for p in D.FLOOR2),
           D.PART_X, rects, BOARD2, _pick2, glass=True)


def outlet_points():
    """2F の床のコンセントの蓋（長机の組の手前の通路）。"""
    holes = (D.FRONT_HOLE, D.BACK_HOLE, D.R_STAIR_HOLE)
    out = []
    for x, y in P2.GROUPS2:
        p = (x + 0.8, y - 1.6)
        if P.inside(p, D.FLOOR2) and P.inside(p, D.INNER) and not S._in_any(p, holes, 0.3):
            out.append(p)
    return out


def _outlets(c):
    r, n = 0.075, 10
    for cx, cy in outlet_points():
        pts = [(cx + r * math.cos(2 * math.pi * k / n), cy + r * math.sin(2 * math.pi * k / n))
               for k in range(n)]
        c.floor.add_ngon_flat(pts, D.Z2 + 0.006, OUTLET)


def _vest_mat(c):
    x0, y0, x1, y1 = VEST_MAT
    kit.plate(c.floor, x0, y0, x1, y1, D.Z1 + 0.004, MAT)


# --------------------------------------------------------------------------- #
#  天井のカセットエアコンとガラリ
# --------------------------------------------------------------------------- #
def _cassette(mb, x, y, z, grille_dy):
    """4 方向のカセット（0.84 角、白い本体、中央の吸い込みと 4 本の吹き出し口は黒）と黒いガラリ。"""
    h = CASSETTE_HALF
    kit.box(mb, x - h, y - h, z - 0.035, x + h, y + h, z - 0.001, CASSETTE)
    zb = z - 0.036
    kit.plate(mb, x - 0.18, y - 0.18, x + 0.18, y + 0.18, zb, GRILLE, flip=True)
    for sx0, sy0, sx1, sy1 in ((-0.25, 0.26, 0.25, 0.32), (-0.25, -0.32, 0.25, -0.26),
                               (0.26, -0.25, 0.32, 0.25), (-0.32, -0.25, -0.26, 0.25)):
        kit.plate(mb, x + sx0, y + sy0, x + sx1, y + sy1, zb, GRILLE, flip=True)
    if grille_dy:
        gy = y + grille_dy
        kit.plate(mb, x - 0.5, gy - 0.15, x + 0.5, gy + 0.15, z - 0.006, GRILLE, flip=True)


def _cassette_ok(p, floor_poly, avoid, cols, chain, chain_gap, x_lo, x_hi):
    x, y = p
    if not x_lo < x < x_hi:
        return False
    h = CASSETTE_HALF
    for q in ((x - h, y - h), (x + h, y - h), (x + h, y + h), (x - h, y + h)):
        if not (P.inside(q, floor_poly) and P.inside(q, D.INNER)):
            return False
    inner = D.INNER
    if any(P.seg_dist(p, inner[i], inner[(i + 1) % len(inner)]) < 1.0 for i in range(len(inner))):
        return False
    if S._in_any(p, avoid, 0.6):
        return False
    if any(math.hypot(x - cx, y - cy) < 1.2 for cx, cy in cols):
        return False
    return all(P.seg_dist(p, a, b) > chain_gap for a, b in zip(chain, chain[1:]))


def cassette_points(floor_poly, z, step, avoid, cols, chain, chain_gap, x_lo, x_hi, y_hi):
    """照明の格子（step おき）の中間点を 1 つおきに取った 2*step おきの格子のうち、置ける点。"""
    xs = [p[0] for p in floor_poly]
    ys = [p[1] for p in floor_poly]
    out = []
    y = min(ys) + step
    while y < min(max(ys), y_hi):
        x = min(xs) + step
        while x < max(xs):
            if _cassette_ok((x, y), floor_poly, avoid, cols, chain, chain_gap, x_lo, x_hi):
                out.append((x, y))
            x += step * 2
        y += step * 2
    return out


def cassettes_1f(hangs=()):
    holes = (D.FRONT_HOLE, D.BACK_HOLE, D.R_STAIR_HOLE)
    avoid = holes + (S.KITCHEN_RECT, WC_RECT, HALL_WALL_RECT, D.VEST) + S._keep_out(D.C1, hangs)
    return cassette_points(S.FLOOR1, D.C1, 3.0, avoid, D.COLS1, S.SOFFIT_CHAIN,
                           shell.WALL + D.SOFFIT_W + 0.6, D.CLIP_X + 1.0, D.PART_X - 0.8,
                           D.KITCHEN_Y - 0.6)


def cassettes_2f(hangs=()):
    holes = (D.FRONT_HOLE, D.BACK_HOLE, D.R_STAIR_HOLE)
    kitchen2 = (D.NW_X[1], S.KITCHEN2_Y, S.STAIR_EX[1], D.Y_BACK)
    avoid = holes + (kitchen2, D.WOOD_CEIL) + S._keep_out(D.C2, hangs)
    return cassette_points(D.FLOOR2, D.C2, 3.6, avoid, D.COLS2, S.GLASS2_CHAIN, 1.6,
                           -1e9, D.PART_X - 0.8, D.COUNTER2_Y - 0.6)


def _cassettes(c, hangs):
    for x, y in cassettes_1f(hangs):
        _cassette(c.wall, x, y, D.C1, 1.5)
    for x, y in cassettes_2f(hangs):
        _cassette(c.wall, x, y, D.C2, 1.8)


# --------------------------------------------------------------------------- #
#  ロールスクリーン
# --------------------------------------------------------------------------- #
def _at(a, d, n, s, off):
    return (a[0] + d[0] * s + n[0] * off, a[1] + d[1] * s + n[1] * off)


def _usable(p):
    return p[0] > D.CLIP_X + 0.05 and p[1] > D.CLIP_Y + 0.05


def roll_bays(i):
    """外周の辺 i の桟の間（辺に沿った距離 s0, s1）のうち、尖りの切り口の外に出ないもの。"""
    a, b = D.OUTLINE[i], D.OUTLINE[(i + 1) % len(D.OUTLINE)]
    L = math.hypot(b[0] - a[0], b[1] - a[1])
    d = ((b[0] - a[0]) / L, (b[1] - a[1]) / L)
    n = (-d[1], d[0])
    cuts = [0.0]
    k = ROLL_SEG
    while k < L - 0.2:
        cuts.append(k)
        k += ROLL_SEG
    cuts.append(L)
    out = []
    for s0, s1 in zip(cuts, cuts[1:]):
        s0, s1 = s0 + 0.05 + (0.04 if s0 > 0 else 0.3), s1 - 0.05 - (0.04 if s1 < L else 0.3)
        if s1 - s0 < 0.3:
            continue
        if _usable(_at(a, d, n, s0, ROLL_OFF)) and _usable(_at(a, d, n, s1, ROLL_OFF)):
            out.append((s0, s1))
    return a, d, n, out


def _rolls(c):
    mb = c.wall
    o0, o1, z0, z1 = ROLL_BOX
    for i in ROLL_EDGES:
        a, d, n, bays = roll_bays(i)
        for s0, s1 in bays:
            pa, pb = _at(a, d, n, s0, ROLL_OFF), _at(a, d, n, s1, ROLL_OFF)
            kit.vplate(mb, pb, pa, ROLL_Z[0], ROLL_Z[1], ROLL)
        if bays:
            s0, s1 = bays[0][0], bays[-1][1]
            box = [_at(a, d, n, s0, o0), _at(a, d, n, s1, o0), _at(a, d, n, s1, o1),
                   _at(a, d, n, s0, o1)]
            mb.add_prism(box, z0, z1, ROLL)


def build(c, hangs=()):
    """仕上げ一式。hangs は吊り看板の吊り元 (x, y, z_ceil) の列（その周りにカセットを置かない）。"""
    _boards_1f(c)
    _boards_2f(c)
    _outlets(c)
    _vest_mat(c)
    _cassettes(c, hangs)
    _rolls(c)
