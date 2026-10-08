"""第2研究棟 2F: 受け渡しのカウンター A〜D・木の壁・オレンジの窓・藤色の壁・窓ぎわのカウンター・
客席・柱・テラスの家具。

窓口の札の文字は plan_research2_signs が置く。ここは帯の板（札の裏）まで作る。
座標は plan_research2_dims を参照。
"""

import math

from . import furn_research2 as FR
from . import kit
from . import plan_research2_dims as D
from . import plan_research2_poly as P
from . import plan_research2_shell as S

ORANGE = "research2_counter_orange"
SHELLS = ("research2_chair_shell_white", "research2_chair_shell_green", "research2_chair_shell_orange",
          "research2_chair_shell_dark", "research2_chair_shell_red")
Y_FRONT = D.COUNTER2_Y
Y_WALL = S.KITCHEN2_Y
TOP_H = 0.95
BAND = (2.3, 2.9)        # 上の帯（床からの高さ）

# 長机 2 卓つなぎの組（左の卓の中心 x, 列の y）
GROUPS2 = tuple((x, 12.6) for x in (-20.0, -11.8, -6.0)) + tuple(
    (x, 16.6) for x in (-27.6, -21.0, -15.0, -9.0, -3.0, 5.8)) + tuple(
    (x, 24.75) for x in (-27.6, -21.0, -15.0, -9.0, -3.0, 3.0)) + tuple(
    (x, y) for y in (22.4, 26.9) for x in (10.0, 16.0, 20.0))
# ガラスの線（GLASS2）に沿った丸テーブル（線上の比率）。ガラスの内面から ROUND_IN
ROUNDS2 = (0.05, 0.19, 0.28, 0.37, 0.46, 0.56, 0.66, 0.76, 0.94)
ROUND_IN = 1.45


def _shells(k):
    return tuple(SHELLS[(k + 2 * i) % len(SHELLS)] for i in range(4))


# ---- 受け渡しのカウンター A〜D ----
def _counters(mb):
    x0, x1 = D.COUNTERS2[0][1], D.COUNTERS2[-1][2]
    z = D.Z2
    # 本体（オレンジ）と前面のリブ、ステンレスの天板
    kit.box_nb(mb, x0, Y_FRONT + 0.05, z, x1, Y_WALL, z + TOP_H - 0.04, ORANGE)
    n = int((x1 - x0) / 0.4)
    for k in range(n):
        x = x0 + 0.15 + (x1 - x0 - 0.3) * k / (n - 1)
        kit.box_nb(mb, x - 0.03, Y_FRONT, z + 0.1, x + 0.03, Y_FRONT + 0.05, z + TOP_H - 0.06, ORANGE)
    kit.box_nb(mb, x0, Y_FRONT + 0.05, z, x1, Y_FRONT + 0.1, z + 0.1, "research2_steel_dark")
    kit.box(mb, x0, Y_FRONT - 0.05, z + TOP_H - 0.04, x1, Y_WALL + 0.05, z + TOP_H, FR.STAINLESS)
    # 上の帯と、帯の下端の光
    kit.box(mb, x0, Y_FRONT, z + BAND[0], x1, Y_WALL, z + BAND[1], ORANGE)
    kit.box_nb(mb, x0, Y_FRONT - 0.04, z + BAND[0] - 0.04, x1, Y_FRONT, z + BAND[0] + 0.02,
               "research2_band_light")
    # 窓口の仕切り（A|B|C|D の間の細い柱）
    for _name, a, _b in D.COUNTERS2[1:]:
        kit.box_nb(mb, a - 0.06, Y_FRONT + 0.02, z + TOP_H, a + 0.06, Y_WALL, z + BAND[0],
                   "research2_wall_charcoal")
    _open_kitchen(mb)


def _open_kitchen(mb):
    """くぼみの奥: ステンレスの台、保温ジャー、フード。"""
    qx0, qx1, _qz0, _qz1, qy = S.POCKET2
    z = D.Z2
    kit.box_nb(mb, qx0 + 0.1, qy - 0.65, z, qx1 - 0.1, qy, z + 0.9, FR.STAINLESS)
    for k, x in enumerate(range(int(qx0) + 1, int(qx1), 2)):
        kit.cyl(mb, x + 0.5, qy - 0.33, z + 0.9, z + 1.25 + 0.05 * (k % 2), 0.17,
                FR.STAINLESS if k % 3 else "research2_kiosk_white", seg=6)
    for _name, a, b in D.COUNTERS2:
        kit.box(mb, a + 0.6, qy - 0.75, z + 2.0, b - 0.6, qy, z + 2.45, FR.STAINLESS)
        kit.box_nb(mb, (a + b) * 0.5 - 0.15, qy - 0.25, z + 2.45, (a + b) * 0.5 + 0.15, qy - 0.05,
                   D.C2, FR.STAINLESS)


# ---- 木の壁・オレンジの窓・藤色の壁 ----
def _wood_wall(mb):
    x0, x1, y, h = D.WOOD_WALL
    z = D.Z2
    kit.box(mb, x0, y, z, x1, Y_WALL, z + h, "research2_wood_wall")
    for k in range(1, 10):
        zk = z + h * k / 10
        kit.box_nb(mb, x0, y - 0.01, zk - 0.008, x1, y, zk + 0.008, "research2_chair_walnut")
    # 灰色の鉄の扉
    kit.box_nb(mb, x0 + 0.8, y - 0.04, z, x0 + 1.8, y, z + 2.2, "research2_door_steel")
    kit.box_nb(mb, x0 + 1.6, y - 0.08, z + 1.0, x0 + 1.66, y - 0.04, z + 1.1, FR.CHROME)
    # 緑の枠のコルクボード
    bx0, bx1, bz0, bz1 = x0 + 3.2, x0 + 5.4, z + 1.0, z + 2.1
    kit.box_nb(mb, bx0, y - 0.04, bz0, bx1, y, bz1, "research2_board_green")
    kit.vplate(mb, (bx0 + 0.06, y - 0.041), (bx1 - 0.06, y - 0.041), bz0 + 0.06, bz1 - 0.06,
               "research2_cork")


def _tray_window(mb):
    """オレンジの受け渡しの窓（白いトレーの札、ワイヤーの棚に灰色〜黒のトレー）。"""
    w0, w1 = D.ORANGE_WIN
    y, z = Y_FRONT, D.Z2
    for c0, c1 in D.CHARCOAL:
        kit.box(mb, c0, y, z, c1, Y_WALL, z + 3.0, "research2_wall_charcoal")
    kit.box(mb, w0, y, z, w1, Y_WALL, z + 0.9, "research2_wall_orange")
    kit.box(mb, w0, y, z + 2.2, w1, Y_WALL, z + 3.0, "research2_wall_orange")
    kit.box(mb, w0, y - 0.05, z + 0.9, w1, y + 0.35, z + 0.94, FR.STAINLESS)
    # 窓の奥（暗い面）とワイヤーの棚
    kit.vplate(mb, (w0, Y_WALL - 0.01), (w1, Y_WALL - 0.01), z + 0.94, z + 2.2, "research2_tray_black")
    for sx in (w0 + 0.3, w1 - 0.33):
        kit.box_nb(mb, sx, y + 0.3, z + 0.94, sx + 0.03, y + 0.33, z + 2.0, FR.CHROME)
    for k in range(3):
        zk = z + 1.25 + 0.3 * k
        kit.box_nb(mb, w0 + 0.3, y + 0.3, zk, w1 - 0.3, y + 0.75, zk + 0.02, FR.CHROME)
        for j in range(4):
            tx = w0 + 0.75 + j * 0.5
            FR.tray_stack(mb, tx, y + 0.52, zk + 0.02, n=3 - (j + k) % 2,
                          mat="research2_tray_black" if (j + k) % 2 else "research2_panel_grey")


def _lilac(mb):
    """藤色の壁（入口の辺の東の面。ポスターの地）。"""
    a, b = D.INNER[D.E_WC], D.INNER[D.E_WC + 1]
    (lx0, ly0), (lx1, ly1), h = D.LILAC
    L = math.hypot(b[0] - a[0], b[1] - a[1])
    u = ((b[0] - a[0]) / L, (b[1] - a[1]) / L)
    n = (-u[1], u[0])
    t0 = ((lx0 - a[0]) * u[0] + (ly0 - a[1]) * u[1]) + 0.3
    t1 = ((lx1 - a[0]) * u[0] + (ly1 - a[1]) * u[1]) - 0.3
    pa = (a[0] + u[0] * t0 + n[0] * 0.02, a[1] + u[1] * t0 + n[1] * 0.02)
    pb = (a[0] + u[0] * t1 + n[0] * 0.02, a[1] + u[1] * t1 + n[1] * 0.02)
    kit.vplate(mb, pb, pa, D.Z2, D.Z2 + h, "research2_wall_lilac")


# ---- 窓ぎわのカウンター ----
def _window_counters(mb):
    for i, x0, x1, h in D.WINDOW_COUNTER_2F:
        x0 = max(x0, D.G1[0] + 0.6) if i == D.E_SW else x0
        a, b = D.edge_pt(i, x0, 0.3), D.edge_pt(i, x1, 0.3)
        FR.edge_counter(mb, a, b, D.Z2, h, D.COUNTER_D, "research2_chair_wood_light",
                        body="research2_chair_wood_light")
    for i, x in D.BENCHES2:
        bx, by = D.edge_pt(i, x, D.BENCH_OFF)
        FR.bench(mb, bx, by, D.Z2, D.edge_ang(i), w=D.BENCH_W)
    for i, x in D.HIGH_CHAIRS:
        cx, cy = D.edge_pt(i, x, D.CHAIR_OFF)
        FR.high_chair(mb, cx, cy, D.Z2, D.edge_ang(i))


# ---- 客席 ----
def round_centers():
    """ガラスの線の内側に並ぶ丸テーブルの中心。"""
    out = []
    for t in ROUNDS2:
        p, u = P.along(D.GLASS2, t)
        out.append((p[0] - u[1] * ROUND_IN, p[1] + u[0] * ROUND_IN))
    return tuple(out)


def _seating(mb):
    for k, (x, y) in enumerate(GROUPS2):
        mats = _shells(k)
        FR.table_set(mb, x, y, D.Z2, mats)
        FR.table_set(mb, x + 1.6, y, D.Z2, mats[1:] + mats[:1])
    for k, (x, y) in enumerate(round_centers()):
        FR.round_set(mb, x, y, D.Z2, 4, _shells(k + 3), a0=math.pi * 0.25)


def _columns(mb):
    for x, y in D.COLS2:
        FR.column(mb, x, y, D.Z2, D.C2, D.COL2, "research2_column_2f")


# ---- テラス ----
def _terrace(mb):
    w = "research2_terrace_white"
    for k, (x, y) in enumerate(D.TERRACE_SETS):
        FR.round_set(mb, x, y, D.Z2, 2, (w,), a0=0.6 * k, r_chair=0.7, top=w, leg=w)


def build(c, M):
    _counters(M["counters2"])
    _wood_wall(M["counters2"])
    _tray_window(M["counters2"])
    _lilac(M["counters2"])
    _window_counters(M["seats2"])
    _seating(M["seats2"])
    _columns(M["seats2"])
    _terrace(M["terrace"])
