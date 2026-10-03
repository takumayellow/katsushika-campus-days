"""図書館の家具と部品（本の壁・書架・ガラス手すり・階段・椅子・ソファ・扉）。

材質は mats_library の library_* と、共有の glass_partition・metal_white・stainless・
book_a〜h だけを使う。座面の記録は SeatSink に積み、プランが表の番号順に並べ直す。
"""

import math

from . import furniture as F, kit
from .kit import T

BOOKS = tuple("book_%s" % k for k in "abcdefgh")
GLASS = "glass_partition"
WF = "metal_white"
CHAIR = "library_chair_white"
WOOD = "library_wood_light"
DARK = "library_wenge"


class SeatSink:
    """座面の記録の置き場（furniture._seat が mb.seats に積む形をまねる）。"""

    def __init__(self):
        self.seats = []


def seat_mark(sink, x, y, z, ang, hw=0.25, hd=0.25, inner=0.5, depth=0.20):
    """(x, y) を外形の中心にした座面を 1 つ記録する。ang は座ったときの正面（kit.T と同じ）。"""
    F._seat(sink, T(x, y, z, ang), 0, hw, -hd, hd, inner, depth)
    return sink.seats[-1]


# ---- 椅子・机・ソファ ----
def chair(mb, x, y, z, ang, mat=CHAIR, leg=WF):
    """背もたれつきの椅子（正面 +Y）。座面・背・左右の脚の板で 48 tris。"""
    t = T(x, y, z, ang)
    t.box(mb, -0.23, -0.22, 0.42, 0.23, 0.22, 0.47, mat)
    t.box(mb, -0.23, -0.26, 0.47, 0.23, -0.21, 0.88, mat)
    for sx in (-0.21, 0.19):
        t.box(mb, sx, -0.20, 0.0, sx + 0.02, 0.20, 0.42, leg)


def tab_chair(mb, x, y, z, ang, mat="library_tabchair_green"):
    """メモ台つきの椅子（M2 の黄緑）。"""
    chair(mb, x, y, z, ang, mat=mat)
    t = T(x, y, z, ang)
    t.box(mb, 0.18, -0.05, 0.66, 0.46, 0.30, 0.69, mat)


def slipper_chair(mb, x, y, z, ang, mat=CHAIR):
    """肘のない低い椅子（黙考書院）。"""
    t = T(x, y, z, ang)
    t.box(mb, -0.30, -0.30, 0.0, 0.30, 0.30, 0.42, mat)
    t.box(mb, -0.30, -0.36, 0.0, 0.30, -0.24, 0.92, mat)


def round_table(mb, x, y, z, r=0.45, h=0.72, top="library_table_white", leg=WF):
    kit.cyl(mb, x, y, z + h - 0.04, z + h, r, top, seg=12)
    kit.cyl(mb, x, y, z, z + h - 0.04, 0.04, leg, seg=6, cap_top=False)
    kit.cyl(mb, x, y, z, z + 0.03, r * 0.55, leg, seg=8)


def desk(mb, x0, y0, x1, y1, z, h=0.72, top="library_desk_top_dark", body=WF):
    """天板と両端の脚板の机（軸平行）。長い方向の両端に脚を付ける。"""
    kit.box(mb, x0, y0, z + h - 0.04, x1, y1, z + h, top)
    if x1 - x0 >= y1 - y0:
        for xa in (x0 + 0.04, x1 - 0.08):
            kit.box_nb(mb, xa, y0 + 0.05, z, xa + 0.04, y1 - 0.05, z + h - 0.04, body)
    else:
        for ya in (y0 + 0.04, y1 - 0.08):
            kit.box_nb(mb, x0 + 0.05, ya, z, x1 - 0.05, ya + 0.04, z + h - 0.04, body)


def box_sofa(mb, x, y, z, ang, w=1.6, d=0.8, mat="library_lounge_magenta"):
    """箱形のソファ（正面 +Y）。"""
    t = T(x, y, z, ang)
    hw, hd = w * 0.5, d * 0.5
    t.box_nb(mb, -hw, -hd, 0.0, hw, hd, 0.42, mat)
    t.box_nb(mb, -hw, -hd, 0.42, hw, -hd + 0.2, 0.80, mat)
    for sx in (-hw, hw - 0.16):
        t.box_nb(mb, sx, -hd + 0.2, 0.42, sx + 0.16, hd, 0.62, mat)


def arc_sofa(mb, cx, cy, z, r_mid, a0, a1, n, mat="library_sofa_red", depth=0.6):
    """円弧のソファ。座る人は円の中心を向く。r_mid は座面の奥行きの中央の半径。"""
    for i in range(n):
        a = a0 + (a1 - a0) * (i + 0.5) / n
        half = r_mid * abs(a1 - a0) / n * 0.5 + 0.02
        x, y = cx + r_mid * math.cos(a), cy + r_mid * math.sin(a)
        t = T(x, y, z, a + math.pi * 0.5)   # 正面（ローカル +Y）が中心向き
        t.box_nb(mb, -half, -depth * 0.5, 0.0, half, depth * 0.5, 0.42, mat)
        t.box_nb(mb, -half - 0.03, -depth * 0.5 - 0.2, 0.0, half + 0.03,
                 -depth * 0.5, 0.82, mat)


# ---- 本 ----
def books_face(mb, t, x0, x1, y, z0, z1, rng, seg=(0.28, 0.75)):
    """ローカル +Y を向いた本の背の帯。幅と高さを揺らした縦長の板を並べる。"""
    x = x0
    while x < x1 - 0.06:
        w = min(x1 - x, rng.uniform(*seg))
        top = z0 + (z1 - z0) * rng.uniform(0.70, 0.98)
        t.vplate(mb, x + w, y, x, y, z0, top, BOOKS[rng.randrange(len(BOOKS))])
        x += w


def _levels(z0, z1, levels):
    step = (z1 - z0) / levels
    return [(z0 + step * k, z0 + step * (k + 1)) for k in range(levels)]


def wall_shelf(mb, t, w, h, d, rng, levels=5, frame=DARK, z0=0.0):
    """片面の書架（原点は背板の中央、正面 +Y、奥行き d）。"""
    hw = w * 0.5
    t.box(mb, -hw, 0.0, z0, hw, 0.03, z0 + h, frame)
    for sx in (-hw, hw - 0.03):
        t.box_nb(mb, sx, 0.0, z0, sx + 0.03, d, z0 + h, frame)
    t.box_nb(mb, -hw, 0.0, z0 + h - 0.03, hw, d, z0 + h, frame)
    for a, b in _levels(z0 + 0.08, z0 + h - 0.03, levels):
        t.box_nb(mb, -hw + 0.03, 0.0, a - 0.02, hw - 0.03, d, a, frame)
        books_face(mb, t, -hw + 0.04, hw - 0.04, d - 0.04, a, b - 0.03, rng)


def stack_range(mb, t, length, h, d, rng, levels=5, frame=DARK, end="library_counter_black"):
    """両面の書架 1 連（原点は中央、長さはローカル X、奥行きはローカル Y）。"""
    hl, hd = length * 0.5, d * 0.5
    t.box_nb(mb, -hl, -hd + 0.02, 0.0, hl, hd - 0.02, h - 0.02, frame)
    for sx in (-hl - 0.03, hl):
        t.box_nb(mb, sx, -hd - 0.01, 0.0, sx + 0.03, hd + 0.01, h, end)
    t.box_nb(mb, -hl, -hd - 0.01, h - 0.03, hl, hd + 0.01, h, end)
    for a, b in _levels(0.10, h - 0.05, levels):
        for side in (1, -1):
            y = side * (hd - 0.01)
            if side > 0:
                books_face(mb, t, -hl + 0.02, hl - 0.02, y, a, b - 0.03, rng,
                           seg=(0.5, 1.2))
            else:
                tt = T(*t.p(0.0, 0.0), ang=math.atan2(t.s, t.c) + math.pi)
                books_face(mb, tt, -hl + 0.02, hl - 0.02, hd - 0.01, a, b - 0.03,
                           rng, seg=(0.5, 1.2))
            t.vplate(mb, hl, y + side * 0.012, -hl, y + side * 0.012, a - 0.025, a,
                     frame, flip=side < 0)


def book_wall(mb, a, b, z0, z1, rng, levels=3, frame=DARK, plinth="library_wood_slat"):
    """壁の面に沿った本の壁（a→b の左を向く）。下に木のスラットの台輪を付ける。"""
    dx, dy = b[0] - a[0], b[1] - a[1]
    L = math.hypot(dx, dy)
    t = T(a[0], a[1], 0.0, math.atan2(dy, dx))
    t.box_nb(mb, 0.0, 0.0, z0, L, 0.06, z0 + 0.16, plinth)
    for lo, hi in _levels(z0 + 0.16, z1 - 0.04, levels):
        t.box_nb(mb, 0.0, 0.0, lo - 0.025, L, 0.30, lo, frame)
        books_face(mb, t, 0.02, L - 0.02, 0.26, lo, hi - 0.02, rng)
    t.box_nb(mb, 0.0, 0.0, z1 - 0.04, L, 0.30, z1, frame)


def truck(mb, x, y, z, ang, rng, mat="library_counter_black"):
    """ブックトラック（2 段・両面）。"""
    t = T(x, y, z, ang)
    t.box_nb(mb, -0.45, -0.2, 0.12, 0.45, 0.2, 0.16, mat)
    t.box_nb(mb, -0.45, -0.03, 0.16, 0.45, 0.03, 0.95, mat)
    for lo in (0.16, 0.56):
        books_face(mb, t, -0.42, 0.42, 0.19, lo, lo + 0.32, rng)
    for sx in (-0.45, 0.42):
        t.box_nb(mb, sx, -0.2, 0.0, sx + 0.03, 0.2, 0.12, mat)


# ---- 機器 ----
def opac(mb, x, y, z, ang, body=WF, screen="library_signage_screen"):
    """検索端末（立ち台 + 画面。正面 +Y）。"""
    t = T(x, y, z, ang)
    t.box_nb(mb, -0.08, -0.08, 0.0, 0.08, 0.08, 1.0, body)
    t.box(mb, -0.30, -0.22, 1.0, 0.30, 0.18, 1.06, body)
    t.box_nb(mb, -0.26, -0.18, 1.06, 0.26, -0.13, 1.42, body)
    t.vplate(mb, 0.23, -0.125, -0.23, -0.125, 1.09, 1.39, screen)


def desk_light(mb, x, y, z, ang, mat=WF, glow="library_wall_washer"):
    t = T(x, y, z, ang)
    t.box_nb(mb, -0.02, -0.02, 0.0, 0.02, 0.02, 0.42, mat)
    t.box(mb, -0.18, -0.06, 0.42, 0.18, 0.06, 0.46, mat)
    t.plate(mb, 0.16, -0.05, -0.16, 0.05, 0.415, glow)


# ---- 手すり・階段・扉・壁 ----
def glass_rail(mb, pts, z, h=1.05, frame=WF, cap=WOOD, glass=GLASS, post=2.0):
    """ガラス手すり（白い枠の柱 + 木の笠木）。pts の折れ線に沿う。"""
    for (ax, ay), (bx, by) in zip(pts, pts[1:]):
        L = math.hypot(bx - ax, by - ay)
        if L < 1e-3:
            continue
        t = T(ax, ay, z, math.atan2(by - ay, bx - ax))
        n = max(1, int(math.ceil(L / 6.0)))
        for k in range(n):
            s0, s1 = L * k / n, L * (k + 1) / n
            kit.thick_quad(mb, t.p(s0, 0.0, 0.08), t.p(s1, 0.0, 0.08),
                           t.p(s1, 0.0, h - 0.06), t.p(s0, 0.0, h - 0.06), 0.02, glass)
        m = max(1, int(round(L / post)))
        for k in range(m + 1):
            s = min(max(L * k / m, 0.03), L - 0.03)
            t.box_nb(mb, s - 0.03, -0.03, 0.0, s + 0.03, 0.03, h - 0.04, frame)
        t.box(mb, -0.02, -0.05, h - 0.04, L + 0.02, 0.05, h, cap)


def stair(mb, x0, y0, x1, y1, z0, z1, n, up, tread=WOOD, riser="library_wall_ivory",
          side="library_partition_white"):
    """直階段。up は上る向き（'+x' '-x' '+y' '-y'）。範囲は踏面の外形。"""
    if up in ("+y", "-y"):
        a0, a1, b0, b1 = y0, y1, x0, x1
    else:
        a0, a1, b0, b1 = x0, x1, y0, y1
    run = (a1 - a0) / n
    rise = (z1 - z0) / n
    sgn = 1 if up[0] == "+" else -1

    def rect(p, q, za, zb, mat):
        # p, q は上る向きの座標の範囲。幅方向は b0..b1
        if up in ("+y", "-y"):
            kit.box(mb, b0, min(p, q), za, b1, max(p, q), zb, mat)
        else:
            kit.box(mb, min(p, q), b0, za, max(p, q), b1, zb, mat)

    start = a0 if sgn > 0 else a1
    for i in range(n):
        p = start + sgn * run * i
        q = p + sgn * run
        zt = z0 + rise * (i + 1)
        rect(p, q, zt - 0.05, zt, tread)
        rect(p, p + sgn * 0.03, zt - rise, zt - 0.05, riser)
    end = start + sgn * (a1 - a0)
    for bb in (b0 - 0.06, b1):
        if up in ("+y", "-y"):
            pa, pb = (bb, start), (bb, end)
        else:
            pa, pb = (start, bb), (end, bb)
        kit.thick_quad(mb, (pa[0], pa[1], z0 - 0.35), (pb[0], pb[1], z1 - 0.35),
                       (pb[0], pb[1], z1 + 0.02), (pa[0], pa[1], z0 + 0.02), 0.06, side)


def stair_rails(mb, x0, y0, x1, y1, z0, z1, up, h=0.95, sides=(0, 1)):
    """階段の両脇のガラス手すり（斜めの笠木 + ガラス）。sides は 0=幅の小さい側、1=大きい側。"""
    for k in sides:
        if up in ("+y", "-y"):
            b = (x0 + 0.04) if k == 0 else (x1 - 0.04)
            ya, yb = (y0, y1) if up == "+y" else (y1, y0)
            pa, pb = (b, ya), (b, yb)
        else:
            b = (y0 + 0.04) if k == 0 else (y1 - 0.04)
            xa, xb = (x0, x1) if up == "+x" else (x1, x0)
            pa, pb = (xa, b), (xb, b)
        kit.thick_quad(mb, (pa[0], pa[1], z0 + 0.10), (pb[0], pb[1], z1 + 0.10),
                       (pb[0], pb[1], z1 + h - 0.06), (pa[0], pa[1], z0 + h - 0.06),
                       0.02, GLASS)
        kit.tube(mb, (pa[0], pa[1], z0 + h), (pb[0], pb[1], z1 + h), 0.03, WF, seg=6)


def door(mb, x, y, z, ang, w=1.1, h=2.1, leaf="library_wenge", frame=WF, glass=None,
         open_=0.86):
    """壁の中の建具（shell.door を床の高さ z に置けるようにしたもの）。open_=0 で閉める。"""
    t = T(x, y, z, ang)
    hw = w * 0.5
    dx = w * open_
    t.box(mb, -hw - 0.07, -0.06, 0.0, -hw, 0.06, h + 0.07, frame)
    t.box(mb, hw, -0.06, 0.0, hw + 0.07, 0.06, h + 0.07, frame)
    t.box(mb, -hw - 0.07, -0.06, h, hw + 0.07, 0.06, h + 0.07, frame)
    if glass:
        t.box(mb, dx - hw + 0.04, -0.02, 0.05, dx + hw - 0.04, 0.02, h - 0.05, glass)
        t.box_nb(mb, dx - hw + 0.04, -0.03, 0.05, dx - hw + 0.10, 0.03, h - 0.05, frame)
        t.box_nb(mb, dx + hw - 0.10, -0.03, 0.05, dx + hw - 0.04, 0.03, h - 0.05, frame)
    else:
        t.box(mb, dx - hw + 0.02, -0.025, 0.02, dx + hw - 0.02, 0.025, h - 0.02, leaf)
        t.box_nb(mb, dx + hw - 0.22, 0.03, 1.02, dx + hw - 0.14, 0.07, 1.10, "stainless")


def holed_wall(mb, a, b, z0, z1, mat, thick=0.16, holes=(), top=None):
    """a→b を中心線にした壁。holes は (s0, s1, h0, h1)（a からの距離と絶対高さ）の開口。

    開口の縁で縦に切った列ごとに、開口でない高さの区間だけ箱を立てる。
    """
    dx, dy = b[0] - a[0], b[1] - a[1]
    L = math.hypot(dx, dy)
    if L < 1e-6:
        return
    t = T(a[0], a[1], 0.0, math.atan2(dy, dx))
    cuts = sorted({0.0, L} | {min(max(s, 0.0), L) for h in holes for s in h[:2]})
    hw = thick * 0.5
    for s, e in zip(cuts, cuts[1:]):
        if e - s < 1e-4:
            continue
        m = (s + e) * 0.5
        gaps = sorted((h[2], h[3]) for h in holes if h[0] <= m <= h[1])
        z = z0
        for g0, g1 in gaps + [(z1, z1)]:
            if g0 - z > 1e-4:
                t.box(mb, s, -hw, z, e, hw, min(g0, z1), mat, top)
            z = max(z, g1)


def folded_panel(mb, a, b, z0, z1, push, mat):
    """縦の四角い面を 4 枚の三角形に折る（中央の点を a→b の左へ push だけ出す）。左を向く。"""
    mx, my = (a[0] + b[0]) * 0.5, (a[1] + b[1]) * 0.5
    dx, dy = b[0] - a[0], b[1] - a[1]
    L = math.hypot(dx, dy) or 1.0
    nx, ny = -dy / L, dx / L
    c = (mx + nx * push, my + ny * push, (z0 + z1) * 0.5)
    p00, p10 = (a[0], a[1], z0), (b[0], b[1], z0)
    p11, p01 = (b[0], b[1], z1), (a[0], a[1], z1)
    # 左（法線 n の側）を向く巻き順
    for u, v in ((p10, p00), (p11, p10), (p01, p11), (p00, p01)):
        mb.add_face([u, v, c], mat)
