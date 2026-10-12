"""第2研究棟 1F: 入口ホール・窓際のカウンター・配膳口・島・返却口・客席・柱・2 か所の階段。

看板の文字は plan_research2_signs が置く。ここは板の裏に当たる面（打放しの壁・黒い案内板・
えんじの帯）まで作る。座標は plan_research2_dims を参照。
"""

import math

from . import furn_research2 as FR
from . import kit
from . import plan_research2_dims as D
from .kit import T

ENJI = "research2_band_enji"
CONCRETE = "research2_wall_concrete"
WOOD_CHAIRS = ("research2_chair_wood_light", "research2_chair_walnut", "research2_chair_wood_orange",
               "research2_chair_wood_light")

# 長机 2 卓つなぎの組（左の卓の中心 x, 列の y）
GROUPS1 = tuple((x, 15.0) for x in (-21.0, -15.0, -9.0)) + tuple(
    (x, 19.0) for x in (-21.0, -15.0, -9.0, -1.5)) + tuple(
    (x, y) for y in (23.0, 26.6) for x in (-21.0, -15.0, -9.0, -3.0, 3.0, 11.0, 17.0)) + (
    (-37.0, 19.0), (-37.0, 23.0), (-37.0, 26.6),
    (-30.0, 15.0), (-30.0, 19.0), (-30.0, 23.0), (-30.0, 26.6))
# 窓ぎわの丸テーブル（辺, 辺の上の x）。外面から 1.5 m
ROUNDS1 = ((4, -47.9), (5, -45.2), (6, -39.0), (6, -35.6), (6, -32.4),
           (7, -28.0), (7, -24.6), (7, -22.0))
ROUND_OFF = 1.5
STOOL_OFF = 1.05


def _chair_mats(k):
    """椅子の色を決まった順に混ぜる（隣り合う組で色の並びがずれる）。"""
    return tuple(WOOD_CHAIRS[(k + i) % len(WOOD_CHAIRS)] for i in range(4))


# ---- 入口ホール ----
def _hall(mb):
    x0, y0, _x1, y1 = D.HALL
    wx = D.HALL_WALL_X
    # 打放しの壁（西の面に端末、東の面にえんじのパネル）
    kit.box(mb, wx, y0, D.Z1, wx + 0.15, y1, D.C1, CONCRETE)
    # 黒い案内板（自立の壁）
    bx0, bx1, by, bh = D.BLACK_BOARD
    kit.box(mb, bx0, by - 0.1, D.Z1, bx1, by + 0.1, bh, "research2_sign_black")
    # えんじのパネルと券売機 2 台（卓上型・床置き型）
    px, py0, py1 = D.ENJI_PANEL
    kit.box_nb(mb, px, py0, D.Z1, px + 0.03, py1, 2.4, ENJI)
    FR.ticket_desk(mb, px + 0.35, py0 + 0.5, D.Z1, D.FACE_E)
    FR.ticket_floor(mb, px + 0.36, py1 - 0.5, D.Z1, D.FACE_E)
    # SUNMI 端末 3 台（壁の西の面を背に、西を向く）
    for y in D.KIOSK_Y:
        FR.kiosk(mb, D.KIOSK_X - 0.05, y, D.Z1, D.FACE_W)
    # 案内板の白い線の階段の絵（左下から右上へ 5 段。文字の看板の下）
    sx, sz, run, rise = 0.35, 0.15, 0.24, 0.12
    for k in range(5):
        x, z = sx + run * k, sz + rise * k
        kit.box_nb(mb, x, by - 0.105, z, x + run + 0.03, by - 0.1, z + 0.03, "plastic_white")
        kit.box_nb(mb, x + run, by - 0.105, z, x + run + 0.03, by - 0.1, z + rise + 0.03,
                   "plastic_white")
    # 立て看板（南を向く。前の板の上 0.9 m は文字の地で plan_research2_signs が置く。
    # ここは下の白い板、文字の地の裏の板、後ろへ開く脚）
    ax, ay = D.A_FRAME
    kit.box_nb(mb, ax - 0.3, ay - 0.06, D.Z1 + 0.04, ax + 0.3, ay - 0.03, 0.46, "research2_kiosk_white")
    kit.box_nb(mb, ax - 0.34, ay - 0.03, 0.46, ax + 0.34, ay, 1.44, "research2_kiosk_white")
    t = T(ax, ay, D.Z1, D.FACE_S)
    kit.thick_quad(mb, t.p(-0.28, -0.35, 0.0), t.p(0.28, -0.35, 0.0),
                   t.p(0.28, 0.0, 1.38), t.p(-0.28, 0.0, 1.38), 0.03, "research2_kiosk_white")


def _window_counter(mb):
    """窓ぎわのカウンター（白い天板、高さ 1.0）と白いハイスツール 12 脚。"""
    for i, x0, x1 in D.WINDOW_COUNTER_1F:
        a, b = D.edge_pt(i, x0, 0.3), D.edge_pt(i, x1, 0.3)
        FR.edge_counter(mb, a, b, D.Z1, 1.0, 0.45, "research2_table_white",
                        body="research2_kiosk_white")
    for i, x in D.STOOLS_1F:
        sx, sy = D.edge_pt(i, x, STOOL_OFF)
        FR.high_stool(mb, sx, sy, D.Z1)


# ---- 配膳口・島・ワゴン・返却口 ----
def _serving(mb):
    sx0, sx1 = D.SERVE_X
    y = D.SERVE_Y
    # 腰壁（えんじ）と受け渡しの台（ステンレス）
    kit.box_nb(mb, sx0, y, D.Z1, sx1, D.KITCHEN_Y, 1.0, ENJI)
    kit.box(mb, sx0, y - 0.1, 1.0, sx1, D.KITCHEN_Y + 0.3, 1.04, FR.STAINLESS)
    # 上のえんじの帯と、帯の下端の光
    b0, b1 = D.BAND1
    kit.box(mb, sx0, y, b0, sx1, D.KITCHEN_Y, b1, ENJI)
    kit.box_nb(mb, sx0, y - 0.04, b0 - 0.04, sx1, y, b0 + 0.02, "research2_band_light")
    # 厨房の見える所（奥のステンレスの台と、台の上の箱）
    back = 35.0
    kit.box_nb(mb, sx0 + 0.2, back, D.Z1, sx1 - 0.2, 35.4, 0.9, FR.STAINLESS)
    for k, x in enumerate(range(int(sx0) + 2, int(sx1) - 1, 4)):
        h = 0.3 + 0.12 * (k % 3)
        kit.box_nb(mb, x, back + 0.05, 0.9, x + 0.6, 35.35, 0.9 + h, FR.STAINLESS)


def _islands(mb):
    for x in D.ISLAND_X:
        FR.island(mb, x, (D.ISLAND_Y[0] + D.ISLAND_Y[1]) * 0.5, D.Z1, D.ISLAND_L)
    for x in D.WAGON_X:
        FR.wagon(mb, x, D.WAGON_Y, D.Z1, D.FACE_S)
    # 配膳前の通路の赤いテープ（2 本）
    ax0, _ay0, ax1, _ay1 = D.AISLE
    for y in (29.7, 33.15):
        kit.plate(mb, ax0, y - 0.03, min(ax1, D.TRAY_RETURN[2]), y + 0.03, D.Z1 + 0.003,
                  "research2_floor_tape_red")


def _tray_return(mb):
    x0, y0, x1, y1 = D.TRAY_RETURN
    FR.return_counter(mb, x0, x1, y0 + 0.1, D.KITCHEN_Y + 0.7, D.Z1)
    for x in (x0 + 1.2, x1 - 1.2):
        FR.tray_stack(mb, x, y0 + 0.45, D.Z1 + 0.9)
    # 洗い場の奥の流し台と棚
    yb = y1 - 0.3
    kit.box_nb(mb, x0, yb - 0.7, D.Z1, x1 + 2.0, yb, 0.9, FR.STAINLESS)
    kit.box_nb(mb, x0 + 0.5, yb - 0.4, 1.4, x1 + 1.5, yb, 1.44, FR.STAINLESS)


# ---- 客席 ----
def _seating(mb):
    """長机の組と窓ぎわの丸テーブル。置いた椅子の数を返す。"""
    n = 0
    for k, (x, y) in enumerate(GROUPS1):
        mats = _chair_mats(k)
        n += FR.table_set(mb, x, y, D.Z1, mats)
        n += FR.table_set(mb, x + 1.6, y, D.Z1, mats[2:] + mats[:2])
    for k, (i, x) in enumerate(ROUNDS1):
        cx, cy = D.edge_pt(i, x, ROUND_OFF)
        a_in = D.edge_ang(i) + math.pi * 0.5 + math.pi    # 室内を向く法線の角度（数学の角）
        n += FR.round_set(mb, cx, cy, D.Z1, 3, _chair_mats(k + 1), a0=a_in)
    return n


def _columns(mb):
    for x, y in D.COLS1:
        FR.column(mb, x, y, D.Z1, D.C1, D.COL1, "research2_column_concrete")


# ---- 階段 ----
def _front_stair(mb):
    x0, x1 = D.FRONT_STAIR_X
    r0, r1, z0, z1, n = D.FRONT_RUN1
    FR.stair(mb, x0, r0, x1, r1, z0, z1, n, "+y")
    FR.stair_rails(mb, x0, r0, x1, r1, z0, z1, "+y")
    l0, l1, lz = D.FRONT_LANDING
    kit.box(mb, x0, l0, lz - 0.22, x1, l1, lz, FR.TREAD, bottom=FR.SOFFIT)
    FR.glass_rail(mb, ((x0 + 0.04, l0), (x0 + 0.04, l1)), lz, h=0.95)
    r0, r1, z0, z1, n = D.FRONT_RUN2
    FR.stair(mb, x0, r0, x1, r1, z0, z1, n, "+y")
    FR.stair_rails(mb, x0, r0, x1, r1, z0, z1, "+y", sides=(0,))
    hx0, hy0, hx1, hy1 = D.FRONT_HOLE
    FR.glass_rail(mb, ((hx0, hy1), (hx0, hy0), (hx1, hy0), (hx1, hy1)), D.Z2)


def _back_stair(mb):
    x0, x1, y0, y1, z0, z1, n = D.BACK_RUN1
    FR.stair(mb, x0, y0, x1, y1, z0, z1, n, "+y")
    FR.stair_rails(mb, x0, y0, x1, y1, z0, z1, "+y", sides=(1,))
    lx0, ly0, lx1, ly1, lz = D.BACK_LANDING
    kit.box(mb, lx0, ly0, lz - 0.22, lx1, ly1, lz, FR.TREAD, bottom=FR.SOFFIT)
    x0, x1, ya, yb, z0, z1, n = D.BACK_RUN2
    FR.stair(mb, x0, yb, x1, ya, z0, z1, n, "-y")
    FR.stair_rails(mb, x0, yb, x1, ya, z0, z1, "-y", sides=(0,))
    hx0, hy0 = D.BACK_HOLE[0], D.BACK_HOLE[1]
    FR.glass_rail(mb, ((hx0, hy0), (D.BACK_RUN1[1], hy0)), D.Z2)


def build(c, M):
    """1F 一式。客席の椅子とスツールの数を返す。"""
    _hall(M["hall"])
    _window_counter(M["hall"])
    _serving(M["serving"])
    _islands(M["serving"])
    _tray_return(M["serving"])
    seats = _seating(M["seats1"]) + len(D.STOOLS_1F)
    _columns(M["stairs"])
    _front_stair(M["stairs"])
    _back_stair(M["stairs"])
    return seats
