"""寮の 2F（女子フロア）と屋上の躯体。居室階の廊下・サンプルの居室・ランドリー・ラウンジ・キッチンコーナー。

公式の記載: 居室 100 室（女子 2F 23 室・男子 3–5F 77 室）、居室 11.34 ㎡、ラウンジは 1F と女子フロア、
キッチンコーナーは各階、ランドリーは居室階の各階、屋上テラス。作るのは 2F のうち共用部と
サンプルの居室 1 室だけ。3–5F は作らない（屋上は 2F の真上に置く。推定）。

座標は dorm_interior.py と同じ建物ローカル。上階の什器は z=0 で組んでから kit.lift で Z2 だけ持ち上げる。
"""

import contextlib
import math
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))

from kcd_interior import kit                          # noqa: E402
from kcd_interior import furniture as F               # noqa: E402
from kcd_interior import shell as sh                  # noqa: E402
from kcd_interior import signs                        # noqa: E402
from kcd_route import dorm_interior_props as P        # noqa: E402
from kcd_route import dorm_rooms_props as R           # noqa: E402
from kcd_route import dorm_stairs                     # noqa: E402
from kcd_route.dorm_levels import (  # noqa: E402
    CORE, CORR_X, SLAB, X_NOTCH_WALL, Y_EV, Y_KCORNER, Y_NOTCH_WALL, Z2, ZC2, ZR,
    Z_HEAD)

Y2_S = 9.00                       # 2F の廊下の南端（ここから南がラウンジ）
Y_LAUN0, Y_LAUN1 = 10.00, 16.00   # ランドリー
ROOM_Y0, ROOM_Y1 = 10.65, 13.35   # サンプルの居室（幅 2.7 m）
ROOM_X1 = -5.88                   # 居室の窓側の壁（奥行き 4.2 m）
ROOM_YC = (ROOM_Y0 + ROOM_Y1) * 0.5
LAUN_YC = 13.00


@contextlib.contextmanager
def upper(mb, dz=Z2):
    """このブロックで積んだ頂点を dz だけ持ち上げる（z=0 前提の什器用。看板は外で置く）。"""
    base = len(mb.verts)
    yield
    kit.lift(mb, base, dz)


def _wall(c, a, b, gaps=(), mat="wall_white", z0=Z2, z1=ZC2, thick=sh.PART):
    """2F の間仕切り + 開口の上の垂れ壁。"""
    sh.partition(c.wall, a, b, z0, z1, mat=mat, thick=thick, gaps=gaps)
    L = math.hypot(b[0] - a[0], b[1] - a[1])
    d = ((b[0] - a[0]) / L, (b[1] - a[1]) / L)
    for s0, s1 in gaps:
        p0 = (a[0] + d[0] * s0, a[1] + d[1] * s0)
        p1 = (a[0] + d[0] * s1, a[1] + d[1] * s1)
        sh.partition(c.wall, p0, p1, z0 + Z_HEAD, z1, mat=mat, thick=thick)


# --------------------------------------------------------------------------- #
#  躯体（2F の床・天井・外周壁・間仕切り、屋上の床・パラペット・フェンス）
# --------------------------------------------------------------------------- #
def shell(c, xw):
    s = c.spec
    ix0, iy0, ix1, iy1 = s.inner()
    hole = dorm_stairs.roof_hole()

    sh.floor(c.floor, s.x0, s.y_face, s.x1, s.y_back, Z2, "floor_tile_grey",
             thickness=SLAB, side_mat="concrete_grey", holes=[CORE])
    sh.ceiling(c.wall, ix0, iy0, ix1, iy1, ZC2, "ceiling_white", grid=2.4, holes=[hole])
    sh.perimeter(c.wall, s, Z2, ZR, wall="wall_white", glass="glass_clear", sill=0.95,
                 header=ZR - Z2 - 2.55, seg=3.2)

    # 屋上: 床（塔屋の階段の穴だけ）・パラペット・フェンス
    sh.floor(c.floor, s.x0, s.y_face, s.x1, s.y_back, ZR, "deck_dark", thickness=SLAB,
             side_mat="concrete_grey", holes=[hole])
    sh.perimeter(c.wall, s, ZR, ZR + 1.20, wall="wall_white", solid_edges=(0, 1, 2, 3),
                 seg=3.2)
    xa, xb = s.x0 + sh.WALL * 0.5, s.x1 - sh.WALL * 0.5
    ya, yb = s.y_face + sh.WALL * 0.5, s.y_back - sh.WALL * 0.5
    path = [(xa, ya), (xb, ya), (xb, yb), (xa, yb), (xa, ya)]
    path_notch = [(xa, Y_NOTCH_WALL), (X_NOTCH_WALL, Y_NOTCH_WALL), (X_NOTCH_WALL, yb)]
    # 切り欠き（外形の外）に出られないよう、屋上にも同じ位置にパラペットを立てる
    sh.partition(c.wall, (ix0, Y_NOTCH_WALL), (X_NOTCH_WALL, Y_NOTCH_WALL), ZR, ZR + 1.20,
                 thick=sh.WALL)
    sh.partition(c.wall, (X_NOTCH_WALL, Y_NOTCH_WALL), (X_NOTCH_WALL, iy1), ZR, ZR + 1.20,
                 thick=sh.WALL)
    mb = c.furn("roofrail")
    for pts in (path, path_notch):
        for p, q in zip(pts, pts[1:]):
            R.mesh_fence(mb, p, q, ZR + 1.20, h=1.20)

    # 2F の間仕切り
    _wall(c, (ix0, Y_NOTCH_WALL), (-CORR_X, Y_NOTCH_WALL), thick=sh.WALL)
    _wall(c, (X_NOTCH_WALL, Y_NOTCH_WALL), (X_NOTCH_WALL, iy1), thick=sh.WALL)
    _wall(c, (ix0, Y2_S), (-CORR_X, Y2_S))
    _wall(c, (CORR_X, Y2_S), (ix1, Y2_S))
    # 廊下の壁: 西 = サンプルの居室、東 = ランドリー・キッチンコーナー
    g_w = [(ROOM_YC - 0.55 - Y2_S, ROOM_YC + 0.55 - Y2_S)]
    g_e = [(LAUN_YC - 0.55 - Y2_S, LAUN_YC + 0.55 - Y2_S), (30.20 - Y2_S, 32.20 - Y2_S)]
    _wall(c, (-CORR_X, Y2_S), (-CORR_X, Y_EV), gaps=g_w)
    _wall(c, (CORR_X, Y2_S), (CORR_X, Y_EV), gaps=g_e)
    _wall(c, (xw, Y_EV), (-CORR_X, Y_EV))
    _wall(c, (CORR_X, Y_EV), (ix1, Y_EV))
    _wall(c, (CORR_X, Y_LAUN0), (ix1, Y_LAUN0))
    _wall(c, (CORR_X, Y_LAUN1), (ix1, Y_LAUN1))
    _wall(c, (CORR_X, Y_KCORNER), (ix1, Y_KCORNER))
    return mb


# --------------------------------------------------------------------------- #
#  居室階の共用部
# --------------------------------------------------------------------------- #
def lounge(c):
    """女子フロアのラウンジ（南、x ±7.7, y 3.1 – 9.0）。1F と同じ黄色のソファ・ベージュのカーペット。"""
    s = c.spec
    ix0, iy0, ix1, _iy1 = s.inner()
    mb = c.furn("lounge2")
    y_n = Y2_S - sh.PART * 0.5
    kit.plate(c.floor, ix0, iy0, ix1, y_n, Z2 + 0.006, "floor_carpet_gold")
    with upper(mb):
        kit.box(mb, ix0, y_n - 0.02, 0.0, -CORR_X, y_n, 2.70, "wallpaper_orange")
        kit.box(mb, CORR_X, y_n - 0.02, 0.0, ix1, y_n, 2.70, "wallpaper_orange")
        for sx, ang in ((-5.0, 0.0), (5.0, 0.0)):
            F.sofa(mb, sx, 7.70, ang=math.pi, w=2.10, mat="fabric_yellow",
                   cushion="fabric_yellow")
            F.sofa(mb, sx, 4.30, ang=0.0, w=2.10, mat="fabric_yellow",
                   cushion="fabric_yellow")
            F.table(mb, sx, 6.00, ang=0.0, w=1.20, d=0.60, h=0.42, top="counter_wood",
                    leg="metal_dark")
        F.lounge_chair(mb, -7.00, 6.00, ang=-math.pi * 0.5, mat="fabric_yellow")
        F.lounge_chair(mb, 7.00, 6.00, ang=math.pi * 0.5, mat="fabric_yellow")
        sh.planter(mb, -7.10, 8.40, r=0.40, h=0.44, leaf_h=1.5)
        sh.planter(mb, 7.10, 8.40, r=0.40, h=0.44, leaf_h=1.5)
        sh.planter(mb, 0.0, 3.60, r=0.40, h=0.44, leaf_h=1.5)
    sh.ceiling_lights(c.wall, ix0, iy0, ix1, y_n, ZC2, sx=3.2, sy=3.0)
    signs.hanging(c, mb, 0.0, 8.60, ZC2, ang=math.pi, ja="ラウンジ", en="Lounge",
                  w=1.40, h=0.30, drop=0.22)
    c.poi("lounge2", 0.0, 6.00, Z2)
    return mb


def corridor(c):
    """居室階の中廊下（x ±1.6, y 9.0 – 34.4）。居室の扉が並ぶ（サンプルの 1 室だけ開いている）。"""
    mb = c.furn("corridor2")
    xw = -CORR_X + sh.PART * 0.5
    xe = CORR_X - sh.PART * 0.5
    with upper(mb):
        # 西側の居室の扉（閉まっている）。サンプルの居室は別
        for y in (14.70, 17.40, 20.10, 22.80, 25.50, 28.20, 30.90, 33.50):
            sh.door(mb, xw + 0.05, y, ang=-math.pi * 0.5, w=0.90, h=2.05, open_=0.0)
        # 東側の居室の扉
        for y in (18.40, 21.10, 23.80, 26.50):
            sh.door(mb, xe - 0.05, y, ang=math.pi * 0.5, w=0.90, h=2.05, open_=0.0)
        sh.door(mb, xw + 0.05, 9.90, ang=-math.pi * 0.5, w=0.90, h=2.05, open_=0.0)
        sh.fire_extinguisher(mb, xe - 0.30, 9.80, ang=math.pi * 0.5)
        sh.fire_extinguisher(mb, xw + 0.30, 27.50, ang=-math.pi * 0.5)
    for y in (14.00, 22.00, 30.00):
        sh.exit_sign(c.wall, 0.0, y, ZC2 - 0.06, ang=math.pi)
    sh.light_strip(c.wall, -0.40, Y2_S, 0.40, Y_EV, ZC2)
    signs.wall(c, mb, -CORR_X + 0.09, 15.60, Z2 + 2.45, ang=-math.pi * 0.5, ja="居室",
               en="Rooms", w=1.0, h=0.28)
    return mb


def sample_room(c):
    """居室のサンプル 1 室（x -5.88 – -1.68, y 10.65 – 13.35 = 11.34 ㎡）。公式の間取り図と写真の並び。

    入口（東）の左手前に冷蔵庫とロッカー、奥（北）の壁にすのこベッド、手前（南）の壁にタンスと机・棚・椅子・
    ライト、窓の壁（西）にエアコンとカーテン。窓の外はベランダ（入れない）。
    """
    mb = c.furn("room2")
    x_in = -CORR_X - sh.PART * 0.5
    yn = ROOM_Y1 - sh.PART * 0.5
    ys = ROOM_Y0 + sh.PART * 0.5
    xw = ROOM_X1 + sh.PART * 0.5
    # 壁（廊下側は 2F の共通壁に開口あり）。窓の壁は腰高の壁 + ガラス
    for y in (ROOM_Y0, ROOM_Y1):
        _wall(c, (-CORR_X, y), (ROOM_X1, y))
    sh.partition(c.wall, (ROOM_X1, ROOM_Y0), (ROOM_X1, ROOM_Y1), Z2, ZC2,
                 glass_top="glass_clear", glass_z=Z2 + 0.95)
    kit.plate(c.floor, xw, ys, x_in, yn, Z2 + 0.006, "floor_wood_light")
    with upper(mb):
        sh.door(mb, -CORR_X, ROOM_YC, ang=math.pi * 0.5, w=1.10, h=2.05, open_=0.86)
        R.mini_fridge(mb, x_in - 0.30, ys + 0.30, ang=math.pi * 0.5)
        kit.box(mb, x_in - 0.50, ys + 0.62, 0.0, x_in - 0.04, ys + 1.02, 1.10, "metal_gray")
        R.bed(mb, -4.55, yn - 0.52, ang=math.pi * 0.5)
        R.wardrobe(mb, -2.50, ys + 0.30, ang=0.0)
        F.desk(mb, -4.40, ys + 0.38, ang=0.0, w=1.20, d=0.60, h=0.72, top="desk_white")
        R.shelf_over_desk(mb, -4.98, ys, -3.82, ys + 0.26, 1.15)
        F.chair(mb, -4.40, ys + 0.98, ang=math.pi, mat="chair_grey")
        F.desk_lamp(mb, -4.85, ys + 0.30, ang=0.0)
        R.aircon(mb, xw + 0.0, ROOM_YC + 0.55, 2.00, ang=-math.pi * 0.5)
        R.curtain(mb, xw, ROOM_Y0 + 0.15, ROOM_Y1 - 0.15, 0.30, 2.25, side=+1)
    sh.ceiling_lights(c.wall, xw, ys, x_in, yn, ZC2, sx=4.0, sy=2.6)
    signs.wall(c, mb, -CORR_X + 0.09, ROOM_YC + 1.0, Z2 + 2.10, ang=-math.pi * 0.5,
               ja="居室", en="Room", w=0.60, h=0.20)
    c.poi("room", -3.60, ROOM_YC, Z2)
    return mb


def laundry(c):
    """ランドリー（東、x 1.7 – 7.7, y 10.0 – 16.0）。黄色い床・白い洗濯機・棚の上の乾燥機・白い扉。"""
    ix1 = c.spec.inner()[2]
    mb = c.furn("laundry2")
    x_in = CORR_X + sh.PART * 0.5
    y_s = Y_LAUN0 + sh.PART * 0.5
    y_n = Y_LAUN1 - sh.PART * 0.5
    kit.plate(c.floor, x_in, y_s, ix1, y_n, Z2 + 0.006, "floor_laundry_yellow")
    with upper(mb):
        sh.door(mb, CORR_X, LAUN_YC, ang=math.pi * 0.5, w=1.10, h=2.05, leaf="metal_white",
                open_=0.86)
        for i in range(6):
            R.washer(mb, ix1 - 0.34, y_s + 0.45 + 0.78 * i, ang=math.pi * 0.5)
        R.laundry_table(mb, 4.00, y_n - 0.50, ang=0.0, w=1.60, d=0.70)
        F.bench(mb, 3.20, y_s + 0.40, ang=0.0, w=1.60, mat="counter_wood")
    sh.ceiling_lights(c.wall, x_in, y_s, ix1, y_n, ZC2, sx=3.0, sy=3.0)
    signs.wall(c, mb, CORR_X - 0.09, 12.00, Z2 + 2.45, ang=math.pi * 0.5, ja="ランドリー",
               en="Laundry", w=1.2, h=0.28)
    c.poi("laundry", 4.60, LAUN_YC, Z2)
    return mb


def kitchen_corner(c):
    """キッチンコーナー（東、x 1.7 – 7.7, y 28.9 – 34.3）。1F と同じ配置で、居室階にもある。"""
    ix1 = c.spec.inner()[2]
    mb = c.furn("kitchen2")
    x_in = CORR_X + sh.PART * 0.5
    y_s = Y_KCORNER + sh.PART * 0.5
    y_n = Y_EV - sh.PART * 0.5
    kit.plate(c.floor, x_in, y_s, ix1, y_n, Z2 + 0.006, "floor_wood_light")
    with upper(mb):
        P.kitchen_run(mb, 2.60, ix1, y_s, +1, sinks=(3.70,), hobs=(5.00, 6.60))
        P.kitchen_run(mb, 2.60, ix1, y_n, -1, sinks=(6.40,), hobs=(3.60, 5.20))
        P.microwave(mb, 2.90, y_s + 0.33, 0.90, ang=0.0)
        P.pot(mb, 4.45, y_s + 0.30, 0.90)
        F.table(mb, 5.10, 31.60, ang=0.0, w=1.40, d=0.75, h=0.72, top="desk_dark",
                leg="metal_dark")
        for dx in (-0.36, 0.36):
            P.cafe_chair(mb, 5.10 + dx, 31.60 - 0.60, ang=0.0)
            P.cafe_chair(mb, 5.10 + dx, 31.60 + 0.60, ang=math.pi)
    sh.ceiling_lights(c.wall, x_in, y_s, ix1, y_n, ZC2, sx=2.6, sy=2.6)
    signs.wall(c, mb, CORR_X - 0.09, 31.20, Z2 + 2.45, ang=math.pi * 0.5, ja="キッチン",
               en="Kitchen", w=1.2, h=0.28)
    c.poi("kitchen2", 4.20, 31.60, Z2)
    return mb


def hall(c, xw):
    """2F のエレベーターホール（奥、x -4.4 – 7.7, y 34.5 – 40.5）。東に階段室、西に屋上への階段。"""
    ix1, iy1 = c.spec.inner()[2], c.spec.inner()[3]
    mb = c.furn("hall2")
    y_s = Y_EV + sh.PART * 0.5
    with upper(mb):
        sh.elevator_bank(mb, -1.30, iy1 - 0.06, count=2, ang=0.0, pitch=2.60, w=1.05,
                         h=2.25)
        F.bench(mb, 2.40, iy1 - 0.40, ang=math.pi, w=1.80, back=True)
        sh.notice_board(mb, 3.50, y_s, 0.95, ang=0.0, w=2.00, h=1.10, sheets=5, rng=c.rng)
        sh.planter(mb, 0.0, 38.60, r=0.40, h=0.44, leaf_h=1.4)
    signs.hanging(c, mb, 0.0, iy1 - 2.60, ZC2, ang=math.pi, ja="エレベーター",
                  en="Elevators", w=1.80, h=0.34, drop=0.22)
    signs.wall(c, mb, xw + 0.01, 37.60, Z2 + 2.10, ang=-math.pi * 0.5, ja="屋上",
               en="Roof", w=0.90, h=0.26)
    signs.hanging(c, mb, 5.60, y_s - 0.50, ZC2, ang=math.pi, ja="階段", en="Stairs",
                  w=0.80, h=0.26, drop=0.22, both=False)
    sh.ceiling_lights(c.wall, xw, y_s, ix1, iy1, ZC2, sx=3.0, sy=3.0,
                      holes=[dorm_stairs.roof_hole(), CORE])
    c.poi("hall2", 0.0, 36.40, Z2)
    return mb
