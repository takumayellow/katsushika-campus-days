"""図書館棟 1F: 風除室・入館ゲート・メディアラウンジ・カウンター・諸室・ロビー。

看板の板の裏に当たる面（案内板・ゲートの柱・背面の壁・展示パネル）はここで作る。
看板そのものは plan_library_signs が置く。座標は plan_library_dims を参照。
"""

import math

from . import kit
from . import furn_library as FL
from . import plan_library_dims as D
from . import plan_library_shell as S

PART = "library_partition_white"
DARKGREY = "library_wall_darkgrey"
RED = "library_counter_red"
BLACK = "library_counter_black"
WHITE = "library_table_white"
GREY = "library_info_grey"
IVORY = "library_wall_ivory"
SCREEN = "library_signage_screen"
WF = FL.WF
GLASS = FL.GLASS

ROOM_TOP = D.C1          # 諸室の壁は 1F の天井まで
DOOR_H = 2.1
HEX_COLUMNS = ((-6.0, 26.0), (6.0, 29.0), (-1.0, 16.5), (-6.0, 38.5))
LOUNGE_TABLES = ((-15.5, 6.0), (-10.0, 6.0), (-15.5, 10.05), (-10.0, 10.05))
LOBBY_TABLES = ((-3.6, 21.2), (-0.5, 19.3), (-5.4, 29.2), (2.8, 28.3), (6.2, 33.5),
                (-1.5, 37.0))


def _wall(mb, a, b, doors=(), mat=PART, thick=0.16, top=ROOM_TOP):
    """諸室の壁（a→b の中心線）。doors は a からの距離で測った扉の中心。"""
    holes = tuple((s - 0.62, s + 0.62, 0.0, DOOR_H + 0.07) for s in doors)
    FL.holed_wall(mb, a, b, 0.0, top, mat, thick=thick, holes=holes)


# ---- 風除室・ゲート ----
def _vestibule(mb, rng):
    x0, y0, x1, y1 = D.VEST
    # 西: 濃いグレーの案内板の壁（掲示物と黄緑の帯）。東: ガラス
    kit.box(mb, x0 - 0.2, y0, 0.0, x0, y1, 3.2, DARKGREY)
    S.cap(mb, (x0 - 0.2, y1), (x0 - 0.2, y0), 3.2, 0.0, 0.2)
    kit.box(mb, x0 - 0.01, y0, 2.5, x0 + 0.005, y1, 2.62, "library_tabchair_green")
    for ya in (y0 + 0.25, y0 + 0.62, y1 - 0.62):
        zb = rng.uniform(1.2, 1.6)
        kit.box(mb, x0, ya, zb, x0 + 0.004, ya + 0.3, zb + 0.42, WHITE)
    kit.box(mb, x1, y0, 0.0, x1 + 0.06, y1, 3.2, GLASS)
    for ya in (y0, (y0 + y1) * 0.5, y1 - 0.06):
        kit.box(mb, x1, ya, 0.0, x1 + 0.08, ya + 0.06, 3.2, WF)
    S.cap(mb, (x1 + 0.08, y0), (x1 + 0.08, y1), 3.2, 0.0, 0.08)
    # 奥の開口の上の垂れ壁
    kit.box(mb, x0, y1 - 0.1, 2.6, x1, y1, 3.2, WF)


def _gates(mb):
    """入館ゲート 3 レーン（グレーの筐体 4 本、中の 2 本に案内の柱）。"""
    g0, g1 = D.GATES_Y
    for x in (-2.0, -0.65, 0.65, 2.0):
        kit.box(mb, x - 0.15, g0, 0.0, x + 0.15, g1, 1.0, GREY, top=BLACK)
        kit.box(mb, x - 0.12, g0 + 0.3, 0.98, x + 0.12, g0 + 0.6, 1.02, SCREEN)
        if abs(x) < 1.0:
            kit.box(mb, x - 0.18, g0, 1.0, x + 0.18, g0 + 0.15, 1.45, GREY)
    # 各レーンの両側からガラスのフラップ
    for xa, xb in ((-2.0, -0.65), (-0.65, 0.65), (0.65, 2.0)):
        for xe, d in ((xa + 0.15, 0.32), (xb - 0.15, -0.32)):
            kit.box_nb(mb, xe, g0 + 0.5, 0.75, xe + d, g0 + 0.52, 1.0, GLASS)


# ---- メディアラウンジ ----
def _lounge(mb):
    for tx, ty in LOUNGE_TABLES:
        kit.cyl(mb, tx, ty, 0.36, 0.40, 0.42, WHITE, seg=10)
        kit.cyl(mb, tx, ty, 0.0, 0.36, 0.05, WF, seg=6, cap_top=False)
        FL.box_sofa(mb, tx, ty - 1.05, 0.0, 0.0)
        FL.box_sofa(mb, tx, ty + 1.05, 0.0, math.pi)
    # 窓際の白いカウンターと丸椅子
    kit.box(mb, -17.8, 4.4, 0.70, -11.0, 4.9, 0.74, WHITE)
    for x in (-17.6, -11.2):
        kit.box_nb(mb, x, 4.45, 0.0, x + 0.04, 4.85, 0.70, WF)


# ---- カウンター・背面 ----
def _lockers(mb):
    x0, x1 = D.LOCKER_FACE_X - 0.6, D.LOCKER_FACE_X
    kit.box(mb, x0, 16.0, 0.0, x1, 19.5, 2.4, "library_partition_white")
    for k in range(7):
        y = 16.0 + 0.5 * k
        for zb in (0.1, 0.85, 1.6):
            kit.box_nb(mb, x1, y + 0.03, zb, x1 + 0.01, y + 0.47, zb + 0.68, GREY)


def _back_wall(mb, rng):
    xf = D.COUNTER_BACK_X
    kit.box(mb, xf - 0.16, 19.8, 0.0, xf, 31.42, D.C1, IVORY)
    kit.box(mb, xf, 19.8, 3.9, xf + 0.06, 31.42, 4.3, "library_louver_black")
    for y in (20.6 + 0.4 * k for k in range(27)):
        kit.box_nb(mb, xf + 0.06, y, 3.9, xf + 0.1, y + 0.05, 4.3, "library_louver_black")
    # 2 x 2 のサイネージ（看板「OPAC の使い方」の左右）
    for ya, yb in ((21.0, 23.9), (27.1, 30.0)):
        for za, zb in ((1.55, 2.3), (2.45, 3.2)):
            kit.vplate(mb, (xf + 0.015, ya), (xf + 0.015, yb), za, zb, SCREEN)
    # 黒い棚（背面の壁の前、低い）
    t = kit.T(xf + 0.03, 22.4, 0.0, -math.pi * 0.5)
    FL.wall_shelf(mb, t, 3.2, 1.1, 0.4, rng, levels=3, frame=BLACK)



def _counter(mb):
    """L 字のカウンター（赤い腰・黒い天板、高さ 1.0）と職員側の机。"""
    top = D.Z1 + 1.0
    kit.box(mb, -14.0, 20.0, 0.0, -13.05, 31.0, top - 0.04, RED)
    kit.box(mb, -17.0, 30.0, 0.0, -14.0, 31.0, top - 0.04, RED)
    kit.box(mb, -14.1, 20.0, top - 0.04, -12.95, 31.05, top, BLACK)
    kit.box(mb, -17.0, 29.95, top - 0.04, -14.1, 31.05, top, BLACK)
    for y in (20.9 + 1.0 * k for k in range(10)):
        kit.box_nb(mb, -13.05, y, 0.12, -13.03, y + 0.06, 0.9, BLACK)
    # 受付の札の台（看板「受付」の裏）
    kit.box(mb, -13.14, 24.68, top, -13.0, 25.32, top + 0.36, BLACK)
    # 職員側: 低い机・モニター・椅子
    kit.box(mb, -14.75, 20.4, 0.70, -14.05, 29.6, 0.74, WHITE)
    for y in (21.6, 24.0, 27.2):
        t = kit.T(-14.35, y, 0.74, -math.pi * 0.5)
        t.box(mb, -0.25, -0.02, 0.02, 0.25, 0.02, 0.36, BLACK)
        t.vplate(mb, 0.23, 0.025, -0.23, 0.025, 0.05, 0.34, SCREEN)
        FL.chair(mb, -15.1, y, 0.0, -math.pi * 0.5, mat=BLACK)
    # 返却ポスト
    kit.box(mb, -12.9, 30.2, 0.0, -12.4, 30.9, 0.95, BLACK)
    kit.box(mb, -12.42, 30.35, 0.75, -12.39, 30.75, 0.8, "stainless")


def _service(mb, rng):
    """自動貸出機・OPAC・新着図書のトラックと掲示板。"""
    # 自動貸出機の背の衝立（看板「自動貸出機」の裏）
    kit.box(mb, -12.36, 20.9, 0.0, -12.2, 23.1, 2.2, PART)
    for y in (21.5, 22.5):
        FL.opac(mb, -11.97, y, 0.0, -math.pi * 0.5)
    for x in (-9.4, -8.4, -7.4):
        FL.opac(mb, x, 23.0, 0.0, math.pi)
    # 新着図書の掲示板（看板「新着図書」の裏）とトラック
    kit.box(mb, -7.45, 19.0, 0.3, -5.55, 19.08, 2.0, DARKGREY)
    for x in (-7.4, -5.64):
        kit.box_nb(mb, x, 19.0, 0.0, x + 0.04, 19.08, 0.3, WF)
    for k, x in enumerate((-7.2, -6.75, -6.3, -5.85)):
        kit.box(mb, x, 18.99, 0.6, x + 0.36, 18.995, 1.1, FL.BOOKS[(k * 3) % 8])
    FL.truck(mb, -6.5, 18.55, 0.0, math.pi, rng)


# ---- 諸室 ----
def _rooms(mb, rng):
    xw, xf = -D.X_P + 0.08, D.ROOMS_FACE_X - 0.08
    _wall(mb, (xw, 31.5), (D.ROOMS_FACE_X, 31.5))
    _wall(mb, (xf, 31.42), (xf, 41.08), doors=(34.8 - 31.42, 39.8 - 31.42))
    _wall(mb, (xw, 35.5), (xf - 0.08, 35.5))
    _wall(mb, (xw, 41.0), (D.ROOMS_FACE_X, 41.0))
    _wall(mb, (-16.5, 41.08), (-16.5, 43.95), doors=(42.5 - 41.08,))
    # 多目的室: 南の壁（1 は白い壁、2 はガラス）・間・北
    ym = D.MP_FACE_Y + 0.08
    _wall(mb, (xw, ym), (-14.3, ym), doors=(-15.4 - xw,))
    _wall(mb, (-14.3, ym), (-9.0, ym), doors=(-10.2 + 14.3,), mat=GLASS, thick=0.1)
    _wall(mb, (-14.3, ym), (-14.3, 52.0))
    _wall(mb, (-9.0, ym), (-9.0, 52.0), mat=GLASS, thick=0.1)
    _wall(mb, (xw, 52.0), (-9.0, 52.0))
    for y in (45.5, 47.5, 49.5, 51.5):          # 多目的室 2 のガラスの方立
        kit.box_nb(mb, -9.04, y - 0.03, 0.0, -8.96, y + 0.03, ROOM_TOP, WF)
    # 扉
    up, west = math.pi * 0.5, 0.0     # 扉の面: 東西の壁 / 南北の壁
    FL.door(mb, xf, 34.8, 0.0, up)
    FL.door(mb, xf, 39.8, 0.0, up)
    FL.door(mb, -16.5, 42.5, 0.0, up)
    FL.door(mb, -15.4, ym, 0.0, west)
    FL.door(mb, -10.2, ym, 0.0, west, glass=GLASS)
    # 中の什器（扉の隙間とガラス越しに見える分）
    FL.desk(mb, -19.2, 32.0, -18.5, 35.0, 0.0)
    for y in (32.6, 33.6, 34.6):
        t = kit.T(-18.85, y, 0.72, math.pi * 0.5)
        t.box(mb, -0.22, -0.02, 0.02, 0.22, 0.02, 0.32, BLACK)
        FL.chair(mb, -18.1, y, 0.0, math.pi * 0.5)
    FL.round_table(mb, -17.0, 38.2, 0.0, r=0.6)
    for k in range(4):
        a = k * math.pi * 0.5 + 0.4
        x, y = -17.0 + 1.0 * math.cos(a), 38.2 + 1.0 * math.sin(a)
        FL.chair(mb, x, y, 0.0, kit.face_ang(x, y, -17.0, 38.2))
    kit.box(mb, -19.3, 42.0, 0.0, -18.5, 43.2, 1.1, GREY, top=WHITE)
    for row, y in enumerate((46.0, 48.2, 50.4)):
        for x0 in (-13.9, -11.6):
            FL.desk(mb, x0, y, x0 + 1.8, y + 0.5, 0.0, top=WHITE)
            for dx in (0.45, 1.35):
                FL.chair(mb, x0 + dx, y - 0.4, 0.0, 0.0)
        FL.desk(mb, -19.0, y, -15.0, y + 0.5, 0.0, top=WHITE)
    kit.box(mb, -13.0, 51.75, 0.9, -10.4, 51.9, 2.1, WHITE)


def _syllabus(mb, rng):
    """シラバス指定図書の書架 3 連と見出しの板（看板の裏）。"""
    for y in (32.4, 34.4, 36.4):
        t = kit.T(-10.5, y, 0.0, 0.0)
        FL.wall_shelf(mb, t, 4.8, 1.6, 0.35, rng, levels=4, frame=BLACK)
        tb = kit.T(-10.5, y, 0.0, math.pi)
        FL.wall_shelf(mb, tb, 4.8, 1.6, 0.35, rng, levels=4, frame=BLACK)
    kit.box(mb, -11.85, 31.9, 1.6, -9.15, 31.98, 2.5, BLACK)
    for x in (-11.8, -9.24):
        kit.box_nb(mb, x, 31.9, 0.0, x + 0.04, 31.98, 1.6, BLACK)


# ---- ロビー ----
def _columns(mb):
    for x, y in HEX_COLUMNS:
        kit.cyl(mb, x, y, 0.0, D.C2 - 0.4, 0.42, "library_concrete", seg=6, cap_top=False)
        kit.cyl(mb, x, y, D.C2 - 0.4, D.C2, 0.5, "library_joint_black", seg=6, cap_top=False)
        kit.cyl(mb, x, y, 0.0, 0.08, 0.5, "library_joint_black", seg=6)
    # 柱のポスター（2 本）
    for (x, y), mat in zip(HEX_COLUMNS[:2], ("library_lounge_magenta", "library_tabchair_green")):
        t = kit.T(x, y, 0.0, math.pi)
        t.box(mb, -0.3, 0.37, 1.3, 0.3, 0.39, 2.1, mat)


def _islands(mb):
    gap = 0.9
    for (cx, cy), r, opening in D.ISLANDS:
        FL.arc_sofa(mb, cx, cy, 0.0, r, opening + gap, opening + 2.0 * math.pi - gap, 9)
        kit.cyl(mb, cx, cy, 0.38, 0.42, 0.35, WHITE, seg=10)
        kit.cyl(mb, cx, cy, 0.0, 0.38, 0.06, WF, seg=6, cap_top=False)
        # 床の吹き出し口
        ox, oy = cx + 1.2 * math.cos(opening), cy + 1.2 * math.sin(opening)
        kit.cyl(mb, ox, oy, 0.0, 0.03, 0.2, GREY, seg=8)


def _lobby_tables(mb):
    for tx, ty in LOBBY_TABLES:
        FL.round_table(mb, tx, ty, 0.0)
        for dx, dy in ((0.75, 0.0), (-0.75, 0.0)):
            x, y = tx + dx, ty + dy
            FL.chair(mb, x, y, 0.0, kit.face_ang(x, y, tx, ty), mat="library_desk_top_dark")


def _displays(mb, rng):
    # 企画展示のパネル（看板「企画展示」の裏）と展示ケース
    kit.box(mb, 1.15, 20.0, 0.08, 2.85, 20.12, 1.8, WHITE)
    for x in (1.2, 2.76):
        kit.box(mb, x - 0.05, 19.85, 0.0, x + 0.09, 20.3, 0.08, WF)
    for (x, y, ang) in ((5.8, 26.5, math.pi * 0.5), (-7.2, 31.5, -math.pi * 0.5)):
        t = kit.T(x, y, 0.0, ang)
        t.box(mb, -0.6, -0.35, 0.0, 0.6, 0.35, 0.85, WHITE)
        t.box(mb, -0.58, -0.33, 0.85, 0.58, 0.33, 1.15, GLASS)
        for k in range(3):
            t.box(mb, -0.45 + 0.32 * k, -0.1, 0.85, -0.25 + 0.32 * k, 0.12, 0.88,
                  FL.BOOKS[rng.randrange(8)])
    for x, y in ((-7.5, 36.5), (7.2, 37.5)):
        FL.truck(mb, x, y, 0.0, 0.0, rng, mat=WHITE)


def build(c, M):
    """M は "entry" "counter" "lobby" の MeshBuilder。"""
    rng = c.rng
    entry, counter, lobby = M["entry"], M["counter"], M["lobby"]
    _vestibule(entry, rng)
    _gates(entry)
    _lounge(entry)
    _lockers(counter)
    _back_wall(counter, rng)
    _counter(counter)
    _service(counter, rng)
    _rooms(counter, rng)
    _syllabus(counter, rng)
    _columns(lobby)
    _islands(lobby)
    _lobby_tables(lobby)
    _displays(lobby, rng)
    for x, y in HEX_COLUMNS:
        c.light(x, y, D.C2 - 0.6, 150.0, 1.5)
