"""第2研究棟の家具と部品（椅子・机・券売機・島型カウンター・ワゴン・返却口・階段・手すり）。

材質は mats_research2 の research2_* だけを使う。共有の家具関数は材質を引数で渡せる
F.table / F.round_table / F.chair_canteen だけを使い、材質が固定のもの（stool・
ticket_machine・tray_rack・return_counter・monitor・elevator_bank）はここで作り直す。
座面の記録は SeatSink に積み、プランが D.SEATS の順に並べる。
"""

import math

from . import furniture as F, kit, shell
from .kit import T

GLASS = "research2_glass"
CHROME = "research2_chrome"
STAINLESS = "research2_stainless"
WHITE = "research2_kiosk_white"
TABLE = "research2_table_white"
STEEL = "research2_steel_dark"
TREAD = "research2_chair_walnut"
SOFFIT = "research2_ceiling_white"


class SeatSink:
    """座面の記録の置き場（furniture._seat が mb.seats に積む形をまねる）。"""

    def __init__(self):
        self.seats = []


def seat_mark(sink, x, y, z, ang, hw=0.25, hd=0.25, inner=0.5, depth=0.20):
    """(x, y) を外形の中心にした座面を 1 つ記録する。ang は座ったときの正面（kit.T と同じ）。"""
    F._seat(sink, T(x, y, z, ang), 0, hw, -hd, hd, inner, depth)
    return sink.seats[-1]


# ---- 椅子・机 ----
def chair(mb, x, y, z, ang, mat):
    """成形合板・シェルの椅子（1 本脚。28 tris）。"""
    F.chair_canteen(mb, T(x, y, z, ang), mat, CHROME)


def table(mb, x, y, z, ang=0.0, w=1.6, d=0.8):
    F.table(mb, x, y, ang, w=w, d=d, h=0.72, top=TABLE, leg=CHROME, z=z)


def table_set(mb, x, y, z, mats, w=1.6):
    """長机 1 卓（長手が X）と椅子 4 脚。mats は 4 脚ぶんの材質の列。置いた椅子の数を返す。"""
    table(mb, x, y, z, w=w)
    k = 0
    for dy in (-0.75, 0.75):
        for dx in (-0.4, 0.4):
            chair(mb, x + dx, y + dy, z, kit.face_ang(x + dx, y + dy, x + dx, y), mats[k % len(mats)])
            k += 1
    return k


def round_set(mb, x, y, z, n, mats, a0=0.0, r_chair=0.75, top=TABLE, leg=CHROME):
    """丸テーブル（1 本脚）と、まわりの椅子 n 脚（角度 a0 から等分）。置いた椅子の数を返す。"""
    F.round_table(mb, x, y, r=0.42, h=0.72, top=top, leg=leg, z=z)
    for k in range(n):
        a = a0 + math.tau * k / n
        cx, cy = x + math.cos(a) * r_chair, y + math.sin(a) * r_chair
        chair(mb, cx, cy, z, kit.face_ang(cx, cy, x, y), mats[k % len(mats)])
    return n


def high_stool(mb, x, y, z, seat=WHITE, leg=CHROME, h=0.75):
    """白い丸座のハイスツール（クロームの脚と足掛けの輪）。"""
    kit.cyl(mb, x, y, z + h - 0.05, z + h, 0.19, seat, seg=8)
    kit.cyl(mb, x, y, z + 0.03, z + h - 0.05, 0.03, leg, seg=4, cap_top=False)
    kit.cyl(mb, x, y, z + 0.30, z + 0.33, 0.16, leg, seg=8, cap_top=False)
    kit.cyl(mb, x, y, z, z + 0.03, 0.2, leg, seg=8)


def high_chair(mb, x, y, z, ang, seat="research2_chair_walnut", leg=STEEL, h=0.75):
    """こげ茶の座と背のハイチェア（4 本脚の箱）。"""
    t = T(x, y, z, ang)
    t.box_nb(mb, -0.2, -0.2, h - 0.05, 0.2, 0.2, h, seat)
    t.box_nb(mb, -0.19, -0.22, h, 0.19, -0.18, h + 0.32, seat)
    for sx in (-0.17, 0.15):
        for sy in (-0.17, 0.15):
            t.box_nb(mb, sx, sy, 0.0, sx + 0.02, sy + 0.02, h - 0.05, leg)
    t.box_nb(mb, -0.17, 0.15, 0.28, 0.17, 0.17, 0.30, leg)


def bench(mb, x, y, z, ang, w=1.8, mat="research2_chair_wood_light", leg=STEEL):
    """背なしの木のベンチ（座面 + 両端の脚の板）。"""
    t = T(x, y, z, ang)
    hw = w * 0.5
    t.box(mb, -hw, -0.2, 0.40, hw, 0.2, 0.45, mat)
    for sx in (-hw + 0.08, hw - 0.12):
        t.box_nb(mb, sx, -0.17, 0.0, sx + 0.04, 0.17, 0.40, leg)


def edge_counter(mb, a, b, z, h, d, top, body=None):
    """a -> b の線（室内は左手）に沿った奥行き d の天板。body を与えると前板も付ける。"""
    L = math.hypot(b[0] - a[0], b[1] - a[1])
    t = T(a[0], a[1], z, math.atan2(b[1] - a[1], b[0] - a[0]))
    t.box(mb, 0.0, 0.0, h - 0.04, L, d, h, top)
    if body:
        t.box_nb(mb, 0.0, d - 0.06, 0.12, L, d - 0.03, h - 0.04, body)
    n = max(2, int(L / 1.6) + 1)
    for k in range(n):
        s = 0.05 + (L - 0.13) * k / (n - 1)
        t.box_nb(mb, s, 0.03, 0.0, s + 0.03, d - 0.06, h - 0.04, body or CHROME)


# ---- 券売機・端末 ----
def kiosk(mb, x, y, z, ang):
    """床置きの白い縦長の注文端末（正面に青い画面）。"""
    t = T(x, y, z, ang)
    t.box(mb, -0.2, -0.18, 0.0, 0.2, 0.18, 0.06, WHITE)
    t.box_nb(mb, -0.06, -0.06, 0.06, 0.06, 0.06, 0.75, WHITE)
    t.box_nb(mb, -0.225, -0.1, 0.75, 0.225, 0.1, 1.5, WHITE)
    t.vplate(mb, 0.19, 0.101, -0.19, 0.101, 0.84, 1.42, "research2_display_blue")


def ticket_desk(mb, x, y, z, ang):
    """卓上型の券売機（下に現金のユニットの箱）。"""
    t = T(x, y, z, ang)
    t.box_nb(mb, -0.35, -0.28, 0.0, 0.35, 0.28, 0.85, "research2_kiosk_black")
    t.box_nb(mb, -0.3, -0.24, 0.85, 0.3, 0.2, 1.45, WHITE)
    t.vplate(mb, 0.24, 0.201, -0.24, 0.201, 1.0, 1.36, "research2_display_blue")
    t.box_nb(mb, 0.1, 0.28, 0.45, 0.3, 0.3, 0.7, STAINLESS)


def ticket_floor(mb, x, y, z, ang):
    """床置き型の券売機（白い箱、上に画面、下に硬貨と紙幣の口）。"""
    t = T(x, y, z, ang)
    t.box_nb(mb, -0.36, -0.3, 0.0, 0.36, 0.3, 1.75, WHITE)
    t.vplate(mb, 0.28, 0.301, -0.28, 0.301, 1.15, 1.6, "research2_display_blue")
    t.box_nb(mb, -0.28, 0.3, 0.8, 0.28, 0.34, 1.0, "research2_kiosk_black")


# ---- 島型カウンター・ワゴン・返却口 ----
def island(mb, x, y, z, length, depth=0.8):
    """灰色の島型カウンター（中心 (x, y)、長手が X）と給茶機・給水機・黄緑の板。"""
    hl, hd = length * 0.5, depth * 0.5
    kit.box_nb(mb, x - hl + 0.04, y - hd + 0.04, z, x + hl - 0.04, y + hd - 0.04, z + 0.86,
               "research2_island_front")
    kit.box(mb, x - hl, y - hd, z + 0.86, x + hl, y + hd, z + 0.9, "research2_island_top")
    for dx in (-hl + 0.45, hl - 0.95):
        kit.box_nb(mb, x + dx, y - 0.22, z + 0.9, x + dx + 0.5, y + 0.22, z + 1.55, WHITE)
        kit.box_nb(mb, x + dx + 0.12, y - 0.24, z + 1.2, x + dx + 0.38, y - 0.22, z + 1.45,
                   "research2_kiosk_black")
    kit.plate(mb, x - 0.55, y - 0.3, x + 0.55, y + 0.3, z + 0.905, "research2_tray_lime")


def wagon(mb, x, y, z, ang, levels=4):
    """ステンレスのトレーのワゴン（0.6 x 0.5 x 1.2。四隅の柱と上下の枠）にライムグリーンのトレーを積む。"""
    t = T(x, y, z, ang)
    for sx in (-0.3, 0.27):
        for sy in (-0.25, 0.22):
            t.box_nb(mb, sx, sy, 0.08, sx + 0.03, sy + 0.03, 1.17, STAINLESS)
    t.box(mb, -0.3, -0.25, 1.17, 0.3, 0.25, 1.2, STAINLESS)
    t.box_nb(mb, -0.3, -0.25, 0.06, 0.3, 0.25, 0.1, STAINLESS)
    for k in range(levels):
        zt = 0.22 + 0.24 * k
        t.box_nb(mb, -0.25, -0.2, zt, 0.25, 0.2, zt + 0.12, "research2_tray_lime")
    for sx in (-0.24, 0.24):
        t.cyl(mb, sx, 0.0, 0.0, 0.06, 0.04, "research2_tray_black", seg=4, cap_top=False)


def tray_stack(mb, x, y, z, n=8, mat="research2_tray_lime"):
    """トレーの山（板を n 枚。上ほど少しずれる）。"""
    for k in range(n):
        dx = 0.006 * ((k * 7) % 5 - 2)
        kit.box_nb(mb, x - 0.23 + dx, y - 0.17, z + 0.025 * k, x + 0.23 + dx, y + 0.17,
                   z + 0.025 * k + 0.02, mat)


def return_counter(mb, x0, x1, y0, y1, z, h=0.9):
    """ステンレスの食器返却口（前面 y0 から奥 y1、天板の高さ h）。"""
    kit.box_nb(mb, x0, y0 + 0.05, z, x1, y1, z + h - 0.04, STAINLESS)
    kit.box(mb, x0, y0, z + h - 0.04, x1, y1, z + h, STAINLESS)
    kit.box_nb(mb, x0 + 0.3, y0 + 0.1, z + h, x1 - 0.3, y0 + 0.16, z + h + 0.12,
               "research2_tray_black")


# ---- 柱・EV ----
def column(mb, x, y, z0, z1, size, mat):
    """丸い打放しの柱（台座なし）。"""
    shell.column(mb, x, y, z0, z1, size, mat, round_=True, base=False)


def ev_doors(mb, x, y, z, ang, w=1.1, h=2.3):
    """EV の両引きの扉と枠（正面 +Y を ang で回す。中は作らない）。"""
    t = T(x, y, z, ang)
    t.box(mb, -w * 0.5 - 0.16, -0.06, 0.0, w * 0.5 + 0.16, 0.06, h + 0.3, STAINLESS)
    t.box_nb(mb, -w * 0.5, 0.06, 0.0, -0.01, 0.1, h, "research2_door_steel")
    t.box_nb(mb, 0.01, 0.06, 0.0, w * 0.5, 0.1, h, "research2_door_steel")
    t.box_nb(mb, w * 0.5 + 0.22, 0.06, 1.0, w * 0.5 + 0.32, 0.09, 1.3, WHITE)


# ---- 手すり・階段 ----
def glass_rail(mb, pts, z, h=1.1, frame=CHROME, cap=STAINLESS, glass=GLASS, post=2.0):
    """ガラスの手すり（柱 + 笠木）。pts の折れ線に沿う。"""
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
        t.box(mb, -0.02, -0.04, h - 0.04, L + 0.02, 0.04, h, cap)


def stair(mb, x0, y0, x1, y1, z0, z1, n, up, tread=TREAD, riser=SOFFIT, side=SOFFIT):
    """直階段。up は上る向き（'+y' '-y' '+x' '-x'）。範囲は踏面の外形。

    段裏に斜めの板を張る（材質は side、side=None なら riser）。side=None なら両脇の板は張らない。
    """
    ax, sgn = up[1], (1 if up[0] == "+" else -1)
    lo, hi = (y0, y1) if ax == "y" else (x0, x1)
    w0, w1 = (x0, x1) if ax == "y" else (y0, y1)

    def at(s, w, z):
        """上る向きの座標 s と、横の座標 w から 3D の点。"""
        return (w, s, z) if ax == "y" else (s, w, z)

    def block(s0, s1, za, zb, mat, open_bottom=False):
        (xa, ya, _), (xb, yb, _) = at(s0, w0, 0.0), at(s1, w1, 0.0)
        make = kit.box_nb if open_bottom else kit.box
        make(mb, min(xa, xb), min(ya, yb), za, max(xa, xb), max(ya, yb), zb, mat)

    run = (hi - lo) / n
    rise = (z1 - z0) / n
    start = lo if sgn > 0 else hi
    end = start + sgn * (hi - lo)
    for i in range(n):
        p = start + sgn * run * i
        q = p + sgn * run
        zt = z0 + rise * (i + 1)
        block(min(p, q), max(p, q), zt - 0.05, zt, tread)
        block(min(p, p + sgn * 0.03), max(p, p + sgn * 0.03), zt - rise, zt - 0.05, riser,
              open_bottom=True)
    if side is not None:
        for wb in (w0 - 0.06, w1):
            kit.thick_quad(mb, at(start, wb, z0 - 0.35), at(end, wb, z1 - 0.35),
                           at(end, wb, z1 + 0.02), at(start, wb, z0 + 0.02), 0.06, side)
    wa, wb = (w1, w0) if ax == "y" else (w0, w1)
    kit.thick_quad(mb, at(start, wa, z0 - 0.3), at(start, wb, z0 - 0.3),
                   at(end, wb, z1 - 0.3), at(end, wa, z1 - 0.3), 0.03,
                   riser if side is None else side)


def stair_rails(mb, x0, y0, x1, y1, z0, z1, up, h=0.95, sides=(0, 1)):
    """階段の脇のガラスの手すり（斜めの笠木 + ガラス）。sides は 0=X の小さい側、1=大きい側。"""
    ya, yb = (y0, y1) if up == "+y" else (y1, y0)
    for k in sides:
        b = (x0 + 0.04) if k == 0 else (x1 - 0.04)
        kit.thick_quad(mb, (b, ya, z0 + 0.10), (b, yb, z1 + 0.10),
                       (b, yb, z1 + h - 0.06), (b, ya, z0 + h - 0.06), 0.02, GLASS)
        kit.tube(mb, (b, ya, z0 + h), (b, yb, z1 + h), 0.03, CHROME, seg=6)
