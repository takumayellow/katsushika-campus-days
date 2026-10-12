"""エントランスホール（風除室・ロッカー・靴箱・ベンチ）・2F への階段・ホール・書庫。"""

import math

from . import furniture as F
from . import kit, shell
from . import plan_gym_dims as D
from . import plan_gym_shell as S


def vestibule(c):
    """玄関の内側の風除室。両側がガラス、奥（y 7.7）にも戸口つきのガラス壁。"""
    mb = c.wall
    x0, x1, y0, y1 = D.VEST
    ceil = D.C_STAND
    for x in (x0, x1):
        kit.box(mb, x - 0.02, y0, 0.0, x + 0.02, y1, 0.9, S.WHITE)
        kit.vplate(mb, (x, y0), (x, y1), 0.9, ceil, "glass_interior")
        kit.vplate(mb, (x, y0), (x, y1), 0.9, ceil, "glass_interior", flip=True)
        for y in (y0, (y0 + y1) * 0.5, y1):
            kit.box(mb, x - 0.04, y - 0.04, 0.0, x + 0.04, y + 0.04, ceil, S.BLACK)
    gx0, gx1 = D.DOOR
    S.xwall(mb, x0, x1, y1 - 0.02, y1 + 0.02, 0.0, ceil, "glass_interior",
            gaps=[(gx0, gx1)], lintel=D.DOOR_H)
    for x in (x0, gx0, gx1, x1):
        kit.box(mb, x - 0.04, y1 - 0.05, 0.0, x + 0.04, y1 + 0.05, ceil, S.BLACK)
    kit.box(mb, gx0, y1 - 0.05, D.DOOR_H - 0.04, gx1, y1 + 0.05, D.DOOR_H + 0.04, S.BLACK)
    # 床マット
    kit.box(mb, gx0, y0 + 0.2, 0.0, gx1, y1 - 0.2, 0.012, "gym_mat_dark")


def fittings(c):
    mb = c.furn("foyer")
    # ロッカー・靴箱（壁 B の東面に沿う）
    for y0, y1 in ((9.8, 13.8), (30.0, 33.0)):
        kit.box(mb, -3.7, y0, 0.0, -3.3, y1, 1.8, "gym_locker_white")
        n = int((y1 - y0) / 0.4)
        for k in range(1, n):
            y = y0 + (y1 - y0) * k / n
            kit.box(mb, -3.3, y - 0.01, 0.1, -3.29, y + 0.01, 1.7, "gym_joint_metal")
    for y0, y1 in ((15.8, 19.8), (34.6, 38.2)):
        kit.box(mb, -3.7, y0, 0.0, -3.3, y1, 1.0, "gym_shoe_rack_wood")
        for z in (0.33, 0.66):
            kit.box(mb, -3.3, y0, z, -3.28, y1, z + 0.02, "gym_joint_metal")
    # 戸口のマット
    for y0, y1 in (D.B_DOOR_S, (D.B_DOOR_N[0] - 0.2, D.B_DOOR_N[1] + 0.2)):
        kit.box(mb, -3.6, y0, 0.0, -2.4, y1, 0.012, "gym_mat_dark")
    # 長いベンチ（東の窓際）と TV
    x0, y0, x1, y1 = D.BENCH_F
    kit.box(mb, x0, y0, 0.40, x1, y1, 0.45, "gym_bench_wood")
    for y in (y0 + 0.3, (y0 + y1) * 0.5, y1 - 0.3):
        kit.box(mb, x0 + 0.05, y - 0.04, 0.0, x1 - 0.05, y + 0.04, 0.40, "gym_rail_metal")
    kit.box(mb, 6.85, 21.3, 0.0, 7.2, 22.7, 0.08, "metal_dark")
    kit.box(mb, 6.95, 21.9, 0.08, 7.05, 22.1, 1.4, "metal_dark")
    kit.box(mb, 6.88, 21.2, 1.4, 7.0, 22.8, 2.05, "plastic_black")
    kit.box(mb, 6.85, 21.25, 1.45, 6.88, 22.75, 2.0, "screen_blue")
    # 掲示の壁（オレンジ）
    kit.box(mb, 8.3, 18.0, 0.9, 8.4, 24.0, 2.4, "gym_accent_orange")
    # 消毒液・AED
    kit.cyl(mb, -3.0, 7.9, 0.0, 1.0, 0.04, "metal_gray", seg=6)
    kit.box(mb, -3.7, 20.2, 1.3, -3.62, 20.8, 1.7, "fire_red")
    shell.fire_extinguisher(mb, -3.45, 27.0)


def stair(c):
    mb = c.wall
    x0, y0, x1, y1 = D.STAIR
    cx, w = (x0 + x1) * 0.5, x1 - x0
    shell.stair_flight(mb, cx, y0, y1, 0.0, D.Z2, w, D.STAIR_N, "gym_stand_concrete",
                       "gym_concrete_nose", rail=False)
    shell.railing(mb, [(x0, y1), (x0, y0), (x1, y0), (x1, y1)], D.Z2, h=1.0,
                  mat="gym_rail_metal", glass="gym_rail_mesh", post=2.0)


def hall_and_shelf(c):
    mb = c.wall
    # 書庫・トレーニングルーム・ホールの仕切り
    sx0, sy0, sx1, sy1 = D.SHELF_ROOM
    z0, z1 = D.Z2, D.C_2F
    S.xwall(mb, 2.3, 8.7, 11.6, 11.9, z0, z1, S.WHITE)
    S.xwall(mb, 2.3, 8.7, 29.6, 29.9, z0, z1, S.WHITE)
    # 書庫の棚
    m2 = c.furn("shelf")
    for k in range(4):
        x = 3.0 + 1.4 * k
        kit.box(m2, x, 10.9, z0, x + 0.9, 11.5, z0 + 1.9, "gym_shoe_rack_wood")
        for z in (0.6, 1.2):
            kit.box(m2, x, 10.85, z0 + z, x + 0.9, 10.9, z0 + z + 0.03, "gym_joint_metal")
    kit.box(m2, 3.0, 5.2, z0, 8.2, 5.8, z0 + 1.1, "gym_box_green")
    # ホール
    base = len(m2.verts)
    for y in (31.0, 33.5, 36.0):
        F.bench(m2, 3.2, y, -math.pi / 2, 1.2, mat="gym_bench_wood", leg="gym_rail_metal")
    kit.lift(m2, base, z0)
    kit.box(m2, 6.78, 40.0, z0 + 0.9, 6.88, 46.0, z0 + 2.6, "gym_accent_orange")


def connector(c):
    mb = c.wall
    # 連絡通路の入口の枠
    kit.box(mb, 8.55, 9.7, 3.0, 8.65, 14.5, 3.4, S.BLACK)


def build(c):
    vestibule(c)
    fittings(c)
    stair(c)
    hall_and_shelf(c)
    connector(c)
