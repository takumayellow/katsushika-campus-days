"""第1研究棟の中央吹き抜け: トップライト・東のガラス壁・西と北の壁・風除室の内側・案内板の壁・受付・
丸テーブルと椅子・パンフレットラック・時計・アート・FJR710・窓口・事務室。
"""

import math

from . import furniture as F, kit, shell
from . import plan_research1_dims as D
from .kit import T
from .plan_research1_shell import BLACK, CONC, GLASS, NAVY, WHITE, wall

MULL = "research1_mullion_white"
STEEL = "research1_steel_leg"
PI = math.pi
HALF_PI = math.pi * 0.5
X_COL_FACE = -D.X_A + D.COL * 0.5     # 西の柱の東面


def _skylight(c):
    """Z 9.0 のトップライト（発光面）と 1.8 m 格子の黒い鉄骨。"""
    mb = c.wall
    x0, x1, y0, y1 = -D.X_A, D.X_A, D.Y_DOOR, D.Y_NC1
    shell.ceiling(mb, x0, y0, x1, y1, D.Z_SKY, "research1_skylight_glow")
    z = D.Z_SKY - 0.01
    n = int(round((x1 - x0) / 1.8))
    for k in range(1, n):
        x = x0 + (x1 - x0) * k / n
        kit.box(mb, x - 0.06, y0, z - 0.30, x + 0.06, y1, z, BLACK)
    m = int(round((y1 - y0) / 1.8))
    for k in range(1, m):
        y = y0 + (y1 - y0) * k / m
        kit.box(mb, x0, y - 0.05, z - 0.20, x1, y + 0.05, z, BLACK)
    for x, y in ((-4.0, 21.0), (4.0, 21.0), (-4.0, 28.5), (4.0, 28.5)):
        c.light(x, y, 8.4, energy=900.0, radius=3.0)


def _east(c):
    """東のガラス壁（Z 0〜7.6、5 列 × 3 段、白いマリオン）と、その上と廊下の口の上の打放し。"""
    mb = c.wall
    x = D.X_A
    y0, y1 = D.EAST_GLASS
    a, b = (x, y1), (x, y0)                       # 左手 = +X（EV ホール側）に厚み
    top = D.GLASS_TIERS[-1]
    shell.outer_wall(mb, a, b, D.Z1, top, wall=CONC, glass=GLASS, sill=0.0, header=0.0,
                     seg=(y1 - y0) / 5.0)
    for z in D.GLASS_TIERS[1:]:
        kit.box(mb, x, y0, z - 0.06, x + 0.20, y1, z + 0.06, MULL)
    wall(c, (a, b), top, D.Z_SKY, mat=CONC)
    wall(c, ((x, y0), (x, D.Y_DOOR)), D.C_COR, D.Z_SKY, mat=CONC)
    wall(c, ((x, D.Y_NC1), (x, y1)), D.C_COR, D.Z_SKY, mat=CONC)


def _west(c):
    """西の壁: 0.8 m 角の柱、1F の引っ込んだ白い壁と紺の閉じた扉、2F の帯窓。"""
    mb = c.wall
    x = -D.X_A
    y0, y1 = D.EAST_GLASS
    wall(c, ((x, y0), (x, y1)), D.Z1, D.Z_WALL, mat=WHITE)       # 厚みは -X（ホール側）
    shell.outer_wall(mb, (x, y0), (x, y1), D.Z_WALL, D.Z_SKY, wall=CONC, glass=GLASS,
                     sill=0.5, header=1.4, seg=(y1 - y0) / 5.0)
    wall(c, ((x, D.Y_DOOR), (x, y0)), D.C_COR, D.Z_SKY, mat=CONC)
    wall(c, ((x, y1), (x, D.Y_NC1)), D.C_COR, D.Z_SKY, mat=CONC)
    cols = D.WEST_COLS
    for y in cols:
        shell.column(mb, x, y, D.Z1, D.Z_SKY, size=D.COL, mat=CONC, base=False)
    for y in cols[1:-1]:
        kit.box_c(mb, x + 0.1, y, D.Z1, 1.0, 1.0, 1.2, "research1_cabinet_white")
    for ya, yb in zip(cols[1:-2], cols[2:-1]):
        yc = (ya + yb) * 0.5
        kit.box(mb, x, yc - 0.6, D.Z1, x + 0.04, yc + 0.6, 2.2, NAVY)
    kit.plate(c.floor, x, y0, x + 0.25, y1, 0.015, "research1_floor_strip")


def _north(c):
    """北の壁（Y 33.1）: 窓口のスクリーン、チャコールの帯、打放し、Z 4.8〜7.8 の窓 6 枚。"""
    mb = c.wall
    a, b = (-D.X_A, D.Y_NC1), (D.X_A, D.Y_NC1)    # 左手 = +Y（事務室側）に厚み
    shell.outer_wall(mb, a, b, D.Z1, 2.6, wall="research1_counter_front", glass=GLASS,
                     sill=1.0, header=0.0, seg=2.0 * D.X_A / 6.0)
    wall(c, (a, b), 2.6, 3.2, mat="research1_band_charcoal", top=False)
    wall(c, (a, b), 3.2, D.Z_WALL, mat=CONC, top=False)
    shell.outer_wall(mb, a, b, D.Z_WALL, D.Z_SKY, wall=CONC, glass=GLASS, sill=0.3,
                     header=1.2, seg=2.0 * D.X_A / 6.0)
    # 窓の奥の明るい面（窓から空洞が見えないように）
    kit.box(mb, -D.X_A, D.Y_NC1 + 1.6, 4.6, D.X_A, D.Y_NC1 + 1.65, 7.95, WHITE)
    F.counter(mb, *D.COUNTER, h=1.0, body="research1_counter_front", top="research1_counter_top")


def _vestibule(c):
    """風除室の側面のガラスと内側の自動扉（開いたまま。両脇の固定ガラスと無目）。"""
    mb = c.wall
    x0, x1 = D.DOOR
    y_out, y = D.Y_DOOR + shell.WALL, D.VEST_Y1
    z = 3.2
    for x in (x0, x1):
        kit.box(mb, x - 0.02, y_out, D.Z1, x + 0.02, y, z, GLASS)
        kit.box(mb, x - 0.05, y - 0.05, D.Z1, x + 0.05, y + 0.05, z, BLACK)
    for xa, xb in ((x0, -0.9), (0.9, x1)):
        kit.box(mb, xa, y - 0.02, D.Z1, xb, y + 0.02, z, GLASS)
    for x in (-0.9, 0.9):
        kit.box(mb, x - 0.04, y - 0.05, D.Z1, x + 0.04, y + 0.05, z, BLACK)
    kit.box(mb, x0, y - 0.06, 2.6, x1, y + 0.06, z, BLACK)
    kit.box(mb, x0 - 0.05, y_out, z, x1 + 0.05, y + 0.05, z + 0.08, BLACK)


def _guide(c):
    """案内板の自立壁（打放し）と脇の柱。案内板の文字は plan_research1_signs。"""
    mb = c.wall
    x0, y0, x1, y1, h = D.GUIDE_WALL
    kit.box(mb, x0, y0, D.Z1, x1, y1, h, CONC)
    gx, gy, s = D.GUIDE_COL
    shell.column(mb, gx, gy, D.Z1, D.Z_SKY, size=s, mat=CONC, base=False)


def _lobby(c):
    """受付台・丸テーブルと椅子・ラック・時計・アート・掲示板。"""
    mb = c.furn("atrium")
    x, y = D.RECEPTION
    # 白い長机（長手を Y、職員は +X の側）と、来訪者の側（-X）の幕板・職員の椅子・モニター
    F.desk(mb, x, y, ang=-HALF_PI, w=2.4, d=0.6, h=0.72, top="research1_table_white", leg=STEEL)
    T(x, y, D.Z1, -HALF_PI).box(mb, -1.2, -0.30, 0.05, 1.2, -0.27, 0.68, "research1_cabinet_white")
    F.monitor(mb, x - 0.1, y, 0.72, ang=HALF_PI)
    F.keyboard(mb, x + 0.15, y, 0.72, ang=HALF_PI)
    F.chair(mb, x + 0.65, y - 0.4, ang=HALF_PI, mat="research1_chair_white", frame=STEEL)
    for tx, ty in D.TABLES:
        F.round_table(mb, tx, ty, r=0.45, h=0.72, top="research1_table_white", leg=STEEL)
        for th in D.CHAIR_ANGLES:
            F.chair(mb, tx + D.CHAIR_R * math.cos(th), ty + D.CHAIR_R * math.sin(th),
                    ang=th + HALF_PI, mat="research1_chair_white", frame=STEEL)
    rx, ry = D.RACK
    kit.box(mb, rx - 0.2, ry - 1.2, D.Z1, rx + 0.2, ry + 1.2, 1.1, "research1_cabinet_white")
    brochures = ("research1_door_lightblue", "research1_chair_green", "research1_red_outlet",
                 "research1_navy_panel")
    for k, mat in enumerate(brochures):
        yy = ry - 0.9 + 0.6 * k
        kit.box(mb, rx - 0.23, yy - 0.13, 0.72, rx - 0.2, yy + 0.13, 1.06, mat)
    shell.clock(c.wall, X_COL_FACE + 0.01, 23.0, 4.0, ang=-HALF_PI, r=0.175)
    art = ("research1_chair_green", "research1_door_lightblue", "research1_red_outlet",
           "research1_whiteboard")
    for k, mat in enumerate(art):
        y0, z0 = 26.3 + 0.3 * (k % 2), 1.6 + 0.4 * (k // 2)
        kit.box(c.wall, X_COL_FACE, y0, z0, X_COL_FACE + 0.03, y0 + 0.3, z0 + 0.4, mat)
    kit.box(c.wall, -6.15, 16.76, D.Z1, -3.65, 16.84, 2.7, BLACK)
    shell.notice_board(c.wall, -4.9, 16.84, 1.0, ang=0.0, w=2.4, h=1.3, sheets=9, rng=c.rng)


def _fjr710(c):
    """FJR710（長手は Y）: 黒い架台、銀のファンケース、暗いコア、排気コーン、説明板の台。"""
    mb = c.furn("fjr710")
    x, y = D.FJR
    kit.box(mb, x - 0.9, y - 1.5, D.Z1, x + 0.9, y + 1.5, 0.35, "research1_engine_dark")
    for dy in (-0.7, 0.7):
        kit.box(mb, x - 0.3, y + dy - 0.1, 0.35, x + 0.3, y + dy + 0.1, 0.55, BLACK)
    zc, y0 = 1.0, y - 1.175
    kit.tube(mb, (x, y0, zc), (x, y0 + 0.95, zc), 0.65, "research1_sash_silver", seg=14)
    kit.tube(mb, (x, y0 + 0.95, zc), (x, y0 + 2.0, zc), 0.45, "research1_engine_metal", seg=12)
    kit.tube(mb, (x, y0 + 2.0, zc), (x, y0 + 2.35, zc), 0.28, "research1_engine_dark", seg=10)
    _disc(mb, x, y0, zc, 0.65, "research1_engine_dark", 14, front=True)
    _disc(mb, x, y0 - 0.01, zc, 0.16, "research1_engine_metal", 8, front=True)
    _disc(mb, x, y0 + 2.35, zc, 0.28, "research1_engine_dark", 10, front=False)
    px, py = D.FJR_PLATE
    T(px, py, 0.0, PI * 0.75).box(c.wall, -0.85, -0.08, D.Z1, 0.85, 0.0, 1.35, BLACK)
    c.light(x - 1.6, y, 3.0, energy=160.0, radius=1.0)


def _disc(mb, x, y, z, r, mat, seg, front):
    """Y に垂直な円板。front=True で -Y を向く。"""
    pts = [(x + r * math.cos(2 * PI * k / seg), y, z + r * math.sin(2 * PI * k / seg))
           for k in range(seg)]
    mb.add_face(pts if front else list(reversed(pts)), mat)


def _office(c):
    """事務室（窓口の奥）: 机の島 4 つ・灰色のロッカー・水色の扉。中には入れない。"""
    mb = c.furn("office")
    for ix in (-4.0, 4.0):
        for iy in (36.0, 39.6):
            for sgn in (-1, 1):
                for dx in (-0.75, 0.75):
                    F.desk(mb, ix + dx, iy + sgn * 0.36, ang=0.0 if sgn > 0 else PI,
                           top="research1_table_white", leg=STEEL)
    for x0, x1 in ((-7.6, -3.4), (2.8, 5.2)):   # 2 列目は北の 2 枚の扉の間
        F.locker_bank(mb, x0, x1, D.Y_N - 0.3, ang=PI, h=1.8, mat="research1_locker_grey")
    for x in (2.0, 6.0):
        T(x, D.Y_N, 0.0, PI).box(mb, -0.5, 0.0, D.Z1, 0.5, 0.04, 2.1, "research1_door_lightblue")


def build(c):
    _skylight(c)
    _east(c)
    _west(c)
    _north(c)
    _vestibule(c)
    _guide(c)
    _lobby(c)
    _fjr710(c)
    _office(c)
