"""講義棟 1F の中廊下・コア・南の入口ロビー・吹き抜け（spec §1・§2）。

南の室（101・102）と北の室のあいだの帯 Y 19.8〜35.1 を作る。中廊下は暗い天井 3.0 m に
ダウンライト、コア（EV・トイレ）は黒い箱で上端に青白い間接照明、吹き抜けは 1F から 25.1 まで
各階に白い手すり壁。2F 以上の床・エスカレータ・東端ホール・北の室の中は別のモジュールが作る。
"""

import math

from . import kit, shell
from . import plan_lecture_dims as D
from . import plan_lecture_shell as SH

T = shell.PART
Y_CS = D.Y_ROOM_S1 - T * 0.5          # 南の室の壁の中心（面 19.64 / 19.80）
Y_CN = D.Y_ROOM_N0 + T * 0.5          # 北の室の壁の中心（面 35.10 / 35.26）
X_IN_W = D.X_W + shell.WALL           # 西の外周壁の内面
Y_IN_S = D.Y_S + shell.WALL           # 南の外周壁の内面
X_LOBBY_W = D.LOBBY_X[0] - T * 0.5    # ロビーの西の壁の中心（面 -3.41 / -3.25）
X_LOBBY_E = D.LOBBY_X[1] + T * 0.5    # ロビーの東の壁の中心（面 7.20 / 7.36）
X_101_E, T_101_E = 28.425, 0.35       # 101 と東端ホールの間の壁（面 28.25 / 28.60）
X_CORR_E = D.X_STEP                   # 中廊下の東端（ここから東端ホール）
X_GLASS = 17.65                       # 仕切りのガラスの線
ESC_HALL = (X_GLASS, D.Y_CORE0, X_CORR_E, D.Y_CORE1)   # エスカレータのホール（上は 2F の床）
Z_SLAB2_UNDER = D.F2 - D.SLAB         # 2F の床スラブの下面（エスカレータのホールの天井）
ATRIUM_RING = (-5.8, 20.5, 9.6, 34.5)  # 吹き抜けの各階の輪（歩けない。奥の壁の内面まで）
PARAPET_T, PARAPET_H = 0.15, 1.2

CORR = "lecture_concrete"
DOOR_WOOD = "lecture_door_wood"
FRAME = "metal_dark"
FRAME_W = "metal_white"
CORE = "lecture_core_black"
WHITE = "lecture_rail_white"
CEIL = "lecture_ceiling_dark"
GLASS = "lecture_glass_curtain"
COOL = "lecture_light_cool"
DOWN = 0.30                           # ダウンライトの一辺
DOWN_CLEAR = 0.8                      # 吊り看板の吊り元からダウンライトまでの最小距離


def _door(mb, x, y, ang, w=D.DOOR2_W, h=D.DOOR2_H, open_=0.0):
    shell.door(mb, x, y, ang=ang, w=w, h=h, leaf=DOOR_WOOD, frame=FRAME, open_=open_)


def _header(mb, x0, x1, yc, z0, z1, thick=T, mat=CORR):
    """東西に走る壁の開口の上の垂れ壁（中心 yc）。"""
    kit.box(mb, x0, yc - thick * 0.5, z0, x1, yc + thick * 0.5, z1, mat)


def _gaps(x_start, centers, half):
    return [(cx - half - x_start, cx + half - x_start) for cx in centers]


def _south_wall(c):
    """南の室の壁 Y 19.72: 102 の閉じた扉 4 枚、101 の開いた扉 4 枚、101 の東の壁。"""
    x_w1 = X_LOBBY_W
    SH.part(c, (X_IN_W - 0.1, Y_CS), (x_w1, Y_CS), 0.0, D.F2, mat=CORR, top=False)
    for x in D.R102_DOORS:
        _door(c.wall, x, Y_CS + T * 0.5, 0.0)
    hw = D.DOOR2_W * 0.5
    x_e0 = X_LOBBY_E
    SH.part(c, (x_e0, Y_CS), (X_CORR_E, Y_CS), 0.0, D.F2, mat=CORR,
            gaps=_gaps(x_e0, D.R101_DOORS, hw), top=False)
    for x in D.R101_DOORS:
        _header(c.wall, x - hw, x + hw, Y_CS, D.DOOR2_H, D.F2)
        # 東の端の扉は西へ開ける（戸が壁の端の外へ出ないように）
        _door(c.wall, x, Y_CS, math.pi if x > D.R101_X[1] - 2.0 else 0.0, open_=0.86)
    SH.part(c, (X_101_E, D.Y_EAST_S), (X_101_E, D.Y_ROOM_S1), 0.0, D.F2, mat=CORR,
            thick=T_101_E, top=False)


def _north_wall(c):
    """北の室の壁 Y 35.18: 104 の開いた扉、北の出口の通路、キオスクの窓、閉じた扉。"""
    x0, x1 = X_IN_W - 0.1, D.COOP_KIOSK_X[1]
    hw = D.DOOR2_W * 0.5
    nx0, nx1 = D.N_EXIT_X
    k0, k1 = D.COOP_KIOSK_X[0] + 1.0, D.COOP_KIOSK_X[1] - 0.8
    gaps = _gaps(x0, D.R104_DOORS, hw) + [(nx0 - x0, nx1 - x0), (k0 - x0, k1 - x0)]
    SH.part(c, (x0, Y_CN), (x1, Y_CN), 0.0, D.F2, mat=CORR, gaps=gaps, top=False)
    for x in D.R104_DOORS:
        _header(c.wall, x - hw, x + hw, Y_CN, D.DOOR2_H, D.F2)
        _door(c.wall, x, Y_CN, math.pi, open_=0.86)
    _header(c.wall, nx0, nx1, Y_CN, D.CEIL_CORR, D.F2)
    # キオスクの窓: 下はカウンターの腰、上は垂れ壁
    _header(c.wall, k0, k1, Y_CN, 0.0, 1.0, thick=0.5, mat="lecture_coop_counter")
    _header(c.wall, k0, k1, Y_CN, 2.2, D.F2)
    y_face = D.Y_ROOM_N0
    for x, w in ((-25.2, 1.0), (D.HEALTH_DOOR_X, 1.0), (-12.4, 1.6), (-4.8, 1.6),
                 (23.3, 1.0)):
        _door(c.wall, x, y_face, math.pi, w=w, h=D.DOOR2_H if w > 1.2 else 2.1)
    # 北の室の間仕切り（104 の両側・北の出口の通路の両側）
    for x in (D.R104_X[0], D.R104_X[1], nx0, nx1):
        SH.part(c, (x, Y_CN), (x, D.north_y(x) - shell.WALL + 0.1), 0.0, D.F2, mat=CORR,
                top=False)


def _west(c):
    """西ロビーの学生食堂への閉じた扉 2 枚（西の外周壁の内面）。"""
    for y in D.CAFE_DOORS_Y:
        _door(c.wall, X_IN_W + 0.05, y, -math.pi * 0.5, w=1.8)


def _core(c, mb):
    """黒いコアの箱 2 つ（EV・トイレ）、EV 2 基、トイレの入口、上端の間接照明。"""
    y0, y1, h = D.Y_CORE0, D.Y_CORE1, D.CEIL_CORR
    for x0, x1 in ((D.CORE_EV[0], D.CORE_WC_W[1]), D.CORE_WC_M):
        mb.add_prism([(x0, y0), (x1, y0), (x1, y1), (x0, y1)], 0.0, h, CORE)
        for a, b in (((x0, y0), (x1, y0)), ((x1, y1), (x0, y1))):
            sgn = -1.0 if a[1] == y0 else 1.0
            ya, yb = sorted((a[1], a[1] + sgn * 0.06))
            kit.box(mb, x0, ya, h - 0.10, x1, yb, h - 0.04, COOL)
        for x in (x0, x1):
            sgn = -1.0 if x == x0 else 1.0
            xa, xb = sorted((x, x + sgn * 0.06))
            kit.box(mb, xa, y0, h - 0.10, xb, y1, h - 0.04, COOL)
    shell.elevator_bank(mb, D.CORE_EV[0] + 1.6, y0, count=2, ang=0.0, pitch=3.6)
    _door(mb, -17.6, y1 + 0.05, 0.0, w=1.2, h=2.1)                 # 多目的トイレ
    for x in (-7.8, 14.0):                                          # 女子・男子トイレの入口
        _door(mb, x, y0 - 0.05, math.pi, w=1.0, h=2.1)


def _glass_screen(c):
    """コアの東の通り抜けとエスカレータのホールの仕切り: 黒い腰・ガラス・黒い垂れ壁。"""
    x0, x1 = D.SCREEN_X
    y0, y1 = D.Y_CORE0, D.Y_CORE1
    kit.box(c.wall, x0, y0, 0.0, x1, y1, 1.0, CORE)
    kit.box(c.wall, X_GLASS - 0.01, y0, 1.0, X_GLASS + 0.01, y1, D.CEIL_CORR, GLASS)
    kit.box(c.wall, X_GLASS - 0.08, y0, D.CEIL_CORR, X_GLASS + 0.08, y1, Z_SLAB2_UNDER, CORE)


def _ceilings(c):
    """中廊下・ロビーの暗い天井 3.0、エスカレータのホールの縁の垂れ壁、101 の天井 4.1。"""
    z = D.CEIL_CORR
    shell.ceiling(c.wall, X_IN_W, D.Y_ROOM_S1, X_GLASS, D.Y_ROOM_N0, z, CEIL,
                  holes=(D.ATRIUM,))
    shell.ceiling(c.wall, X_GLASS, D.Y_ROOM_S1, X_CORR_E, D.Y_CORE0, z, CEIL)
    shell.ceiling(c.wall, X_GLASS, D.Y_CORE1, X_CORR_E, D.Y_ROOM_N0, z, CEIL)
    shell.ceiling(c.wall, D.LOBBY_X[0], Y_IN_S, D.LOBBY_X[1], D.Y_ROOM_S1, z, CEIL)
    for y in (D.Y_CORE0, D.Y_CORE1):
        _header(c.wall, X_GLASS, X_CORR_E, y, z, Z_SLAB2_UNDER, thick=0.1, mat=CORR)
    for ya, yb in ((D.Y_ROOM_S1, D.Y_CORE0), (D.Y_CORE1, D.Y_ROOM_N0)):
        kit.box(c.wall, X_CORR_E - 0.05, ya, z, X_CORR_E + 0.05, yb, 4.0, CORR)
    shell.ceiling(c.wall, D.R101_X[0], Y_IN_S, D.R101_X[1], D.Y_101_N, D.CEIL_101,
                  "ceiling_white")
    shell.ceiling(c.wall, D.R104_X[0], D.Y_ROOM_N0, D.R104_X[1],
                  D.north_y(D.R104_X[1]) - shell.WALL, D.CEIL_104, "ceiling_white")


def _clear(x, y, roots):
    return all(math.hypot(x - rx, y - ry) >= DOWN_CLEAR for rx, ry, _ in roots)


def downlight_centers(roots):
    """ダウンライトの中心 (x, y)。吊り看板の吊り元の近くは避ける。"""
    pts = []
    for y in (21.65, 33.3):
        pts += [(-27.0 + 3.0 * i, y) for i in range(19)]
    pts += [(-26.5, 25.5), (-23.5, 25.5), (-26.5, 29.5), (-23.5, 29.5)]   # 西ロビー
    pts += [(16.75, 25.5), (16.75, 29.5)]                               # 通り抜け
    pts += [(x, y) for x in (-1.0, 2.0, 5.0) for y in (9.5, 12.5, 15.5, 18.5)]  # ロビー
    return [p for p in pts if _clear(p[0], p[1], roots)]


def _downlights(c, roots):
    pts = downlight_centers(roots)
    z = D.CEIL_CORR - 0.005
    h = DOWN * 0.5
    for x, y in pts:
        kit.plate(c.wall, x - h, y - h, x + h, y + h, z, "light_panel", flip=True)
    c.lights_from(pts, D.CEIL_CORR, step=2)


def _lobby(c, mb):
    """南の入口ロビー: レンガの両側の壁、掲示ケース 4 台、風除室、玄関マット。"""
    for x in (X_LOBBY_W, X_LOBBY_E):
        # 見張り線 Y 4.6 をまたぐので天面に蓋（2F の床が無い所）
        SH.part(c, (x, D.Y_S + 0.15), (x, D.Y_ROOM_S1), 0.0, D.F2, mat="lecture_brick")
    w, h = 2.4, 1.3
    for x, ang in ((D.LOBBY_X[0], -math.pi * 0.5), (D.LOBBY_X[1], math.pi * 0.5)):
        for y in (11.0, 15.5):
            shell.notice_board(mb, x, y, 1.0, ang, w=w, h=h, sheets=6)
            t = kit.T(x, y, 1.0, ang)
            t.box(mb, -w * 0.5 - 0.05, 0.0, -0.05, w * 0.5 + 0.05, 0.14, 0.0, FRAME)
            t.box(mb, -w * 0.5 - 0.05, 0.0, h, w * 0.5 + 0.05, 0.14, h + 0.05, FRAME)
            t.box(mb, -w * 0.5, 0.12, 0.0, w * 0.5, 0.13, h, "lecture_glass_rail")
            t.box(mb, -w * 0.5, 0.02, h + 0.05, w * 0.5, 0.10, h + 0.09, "light_strip")
            c.light(*t.p(0.0, 0.5, h + 0.3), energy=15.0, radius=0.6)
    _vestibule(c)


def _vestibule(c):
    """風除室（X -2〜2、Y 4.3〜7.0）。奥のガラスの中央は開いた自動扉。"""
    x0, y0, x1, y1 = D.VESTIBULE
    zt = D.CEIL_CORR - 0.01      # 天井の 0.01 下まで（上に立てる隙間を残さない）
    door = 0.9
    for x in (x0, x1):
        kit.box(c.wall, x - 0.02, y0 - 0.15, 0.0, x + 0.02, y1, zt, GLASS)
        kit.box(c.wall, x - 0.05, y1 - 0.05, 0.0, x + 0.05, y1 + 0.05, zt, FRAME_W)
    for a, b in ((x0, -door), (door, x1)):
        kit.box(c.wall, a, y1 - 0.02, 0.0, b, y1 + 0.02, D.DOOR_H, GLASS)
    for x in (-door, door):
        kit.box(c.wall, x - 0.04, y1 - 0.05, 0.0, x + 0.04, y1 + 0.05, D.DOOR_H, FRAME_W)
    kit.box(c.wall, x0, y1 - 0.05, D.DOOR_H, x1, y1 + 0.05, zt, FRAME_W)
    kit.plate(c.floor, x0 + 0.15, y0 + 0.1, x1 - 0.15, y1 + 1.6, 0.008, "floor_carpet_grey")


def _atrium(c, mb):
    """吹き抜け: 1F の天井の穴から 25.1 まで。各階の輪の床・白い手すり壁・奥の壁・扉。"""
    hx0, hy0, hx1, hy1 = D.ATRIUM
    rx0, ry0, rx1, ry1 = ATRIUM_RING
    z0 = D.CEIL_CORR
    t = 0.10
    kit.box(mb, hx0 - t, hy0, z0, hx0, hy1, D.FL[1] - 0.3, WHITE)
    kit.box(mb, hx1, hy0, z0, hx1 + t, hy1, D.FL[1] - 0.3, WHITE)
    kit.box(mb, hx0, hy0 - t, z0, hx1, hy0, D.FL[1] - 0.3, WHITE)
    kit.box(mb, hx0, hy1, z0, hx1, hy1 + t, D.FL[1] - 0.3, WHITE)
    p = PARAPET_T
    for L in D.FL[1:6]:
        shell.floor(c.floor, rx0, ry0, rx1, ry1, L, "lecture_carpet_corridor", thickness=0.3,
                    side_mat=WHITE, holes=(D.ATRIUM,))
        zt = L + PARAPET_H
        kit.box(mb, hx0 - p, hy0 - p, L, hx0, hy1 + p, zt, WHITE)
        kit.box(mb, hx1, hy0 - p, L, hx1 + p, hy1 + p, zt, WHITE)
        kit.box(mb, hx0, hy0 - p, L, hx1, hy0, zt, WHITE)
        kit.box(mb, hx0, hy1, L, hx1, hy1 + p, zt, WHITE)
        # 奥の壁の白い扉（南北に 2 枚ずつ）
        for x in (hx0 + 1.6, hx1 - 1.6):
            kit.vplate(mb, (x - 0.5, ry0 + 0.01), (x + 0.5, ry0 + 0.01), L, L + 2.1, "plastic_white",
                       flip=True)
            kit.vplate(mb, (x + 0.5, ry1 - 0.01), (x - 0.5, ry1 - 0.01), L, L + 2.1, "plastic_white",
                       flip=True)
    # 輪の外の縁: 内向きの打放しの壁（4.3〜25.1）
    top = D.VOID_TOP
    ring = [(rx0, ry0), (rx0, ry1), (rx1, ry1), (rx1, ry0)]
    for i in range(4):
        kit.vplate(mb, ring[i], ring[(i + 1) % 4], D.FL[1] - 0.3, top, CORR)
    shell.ceiling(c.wall, rx0, ry0, rx1, ry1, top, "ceiling_white")
    for y in (25.5, 27.5, 29.5):
        shell.light_strip(c.wall, hx0 + 0.3, y - 0.1, hx1 - 0.3, y + 0.1, top)
    cx, cy = (hx0 + hx1) * 0.5, (hy0 + hy1) * 0.5
    for z, e in ((7.0, 350.0), (15.0, 450.0), (23.5, 550.0)):
        c.light(cx, cy, z, energy=e, radius=2.0)


def build(c, M, roots):
    """M は区画名 -> 家具の MeshBuilder（"corridor"・"lobby" を使う）。roots は吊り元の列。"""
    _south_wall(c)
    _north_wall(c)
    _west(c)
    _core(c, M["corridor"])
    _glass_screen(c)
    _ceilings(c)
    _downlights(c, roots)
    _lobby(c, M["lobby"])
    _atrium(c, M["corridor"])
