"""第1研究棟の西と東の EV ホール: EV のシャフト・直階段・2F の床と手すり・2F の壁。

2F は EV ホールだけを作る（吹き抜けを見下ろす帯窓まで歩ける）。2F の室は閉じた扉だけ。
"""

import math

from . import kit, shell
from . import plan_research1_dims as D
from .plan_research1_shell import CONC, GLASS, NAVY, ev_door, part, room_door

TREAD = "research1_tile_light"
RAIL = "research1_sus_hairline"
RISE = 0.175
HALF_PI = math.pi * 0.5
Z_UNDER = D.Z2 - 0.23        # 1F の EV ホールの天井（2F の床の下）
Z_EVTOP = D.C2               # シャフトの箱の上端


def steps(mb, x0, x1, y0, y1, z0, z1, mat=TREAD):
    """箱を積んだ直階段。y1 < y0 なら -Y へ上る。"""
    n = max(1, int(round((z1 - z0) / RISE)))
    run = (y1 - y0) / n
    for k in range(n):
        ya, yb = y0 + run * k, y0 + run * (k + 1)
        kit.box(mb, x0, min(ya, yb), z0, x1, max(ya, yb), z0 + (z1 - z0) * (k + 1) / n, mat)


def _hall(c, rect, hole, mouth, rail_pts, lights):
    """EV ホールの共通部: 2F の床（穴あき）、1F と 2F の天井、2F の壁、廊下の口の上の壁、手すり、照明。"""
    x0, y0, x1, y1 = rect
    shell.floor(c.floor, x0, y0, x1, y1, D.Z2, "research1_floor_navy", side_mat=CONC,
                holes=(hole,))
    shell.ceiling(c.wall, x0, y0, x1, y1, Z_UNDER, "research1_ceiling", holes=(hole,))
    shell.ceiling(c.wall, x0, y0, x1, y1, D.C2, "research1_ceiling")
    part(c, (x0, y0), (x1, y0), D.Z2, D.C2, mat=NAVY)
    part(c, (x0, y1), (x1, y1), D.Z2, D.C2, mat=NAVY)
    wall_x = x0 if x0 < 0 else x1
    part(c, (wall_x, y0), (wall_x, y1), D.Z2, D.C2, mat=NAVY)
    part(c, (mouth[0], y0), (mouth[1], y0), D.C_COR, D.Z_WALL, mat=CONC)
    shell.railing(c.wall, rail_pts, D.Z2, h=1.1, mat=RAIL, glass=GLASS)
    for x, y in lights:
        c.light(x, y, Z_UNDER - 0.2, energy=220.0, radius=1.4)
        c.light(x, y, D.C2 - 0.2, energy=220.0, radius=1.4)


def _west(c):
    mb = c.wall
    for x0, y0, x1, y1 in D.W_SHAFTS + (D.W_ROOM,):
        kit.box(mb, x0, y0, D.Z1, x1, y1, Z_EVTOP, NAVY)
    xe = D.W_SHAFTS[0][2]
    for x0, y0, x1, y1 in D.W_SHAFTS:
        for z in (D.Z1, D.Z2):
            ev_door(mb, xe, (y0 + y1) * 0.5, -HALF_PI, z=z)
    sx0, sx1 = D.W_STAIR
    (a0, a1, za, zb), (b0, b1, zc, zd) = D.W_FLIGHTS
    steps(mb, sx0, sx1, a0, a1, za, zb)
    kit.box(mb, sx0, D.W_LANDING[0], D.Z1, sx1, D.W_LANDING[1], zb, TREAD)
    steps(mb, sx0, sx1, b0, b1, zc, zd)
    _hall(c, D.W_HALL, D.W_HOLE, (xe, -D.X_A),
          [(sx0, b1), (sx0, a0), (sx1, a0), (sx1, b1)],
          ((-13.6, 25.0), (-9.0, 22.0)))
    # 2F の学生実験室（閉じた扉。南の壁の北面）
    room_door(mb, -13.9, D.W_HALL[1] + shell.PART * 0.5, 0.0, z=D.Z2)


def _east(c):
    mb = c.wall
    x0, y0, x1, y1 = D.E_SHAFT
    kit.box(mb, x0, y0, D.Z1, x1, y1, Z_EVTOP, NAVY)
    for z in (D.Z1, D.Z2):
        ev_door(mb, x1, (y0 + y1) * 0.5, -HALF_PI, z=z)
    f0x0, f0x1, f0y0, f0y1 = D.E_FLIGHT1
    steps(mb, f0x0, f0x1, f0y0, f0y1, D.Z1, 2.25)
    lx0, ly0, lx1, ly1 = D.E_LANDING
    kit.box(mb, lx0, ly0, D.Z1, lx1, ly1, 2.25, TREAD)
    f1x0, f1x1, f1y0, f1y1 = D.E_FLIGHT2
    steps(mb, f1x0, f1x1, f1y1, f1y0, 2.25, D.Z2)
    kit.box(mb, f0x1, f0y0, D.Z1, f1x0, f0y1, D.Z2, CONC)       # 2 本の段の間の壁
    _hall(c, D.E_HALL, D.E_HOLE, (D.E_SHAFT[2] + 0.2, D.E_HALL[2]),
          [(f1x0, f0y0), (f0x0, f0y0), (f0x0, ly1), (lx1, ly1), (lx1, f0y0)],
          ((13.4, 25.0), (12.6, 20.4)))


def build(c):
    _west(c)
    _east(c)
