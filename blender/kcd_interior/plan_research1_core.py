"""第1研究棟の西と東の EV ホール: ガラスの昇降路・直階段・2F の床と手すり・2F の壁。

2F は EV ホールだけを作る（吹き抜けを見下ろす帯窓まで歩ける）。2F の室は閉じた扉だけ。
昇降路はステンレスの角の柱と 4 面のガラスで、中にガイドレール 2 本と 1F に止まったかごが見える。
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
Z_EVTOP = D.C2 - 0.01        # 昇降路の上端（2F の天井の 1 cm 下。上は天井がふさぐ）
STEEL = "research1_steel_leg"
CAR_H = 2.4                  # かごの高さ


def steps(mb, x0, x1, y0, y1, z0, z1, mat=TREAD):
    """箱を積んだ直階段。y1 < y0 なら -Y へ上る。"""
    n = max(1, int(round((z1 - z0) / RISE)))
    run = (y1 - y0) / n
    for k in range(n):
        ya, yb = y0 + run * k, y0 + run * (k + 1)
        kit.box(mb, x0, min(ya, yb), z0, x1, max(ya, yb), z0 + (z1 - z0) * (k + 1) / n, mat)


def _hall(c, rect, holes, mouth, rail_pts, lights):
    """EV ホールの共通部: 2F の床と 1F の天井（階段と昇降路の穴あき）、2F の天井、2F の壁、
    廊下の口の上の壁、手すり、照明。"""
    x0, y0, x1, y1 = rect
    shell.floor(c.floor, x0, y0, x1, y1, D.Z2, "research1_floor_navy", side_mat=CONC,
                holes=holes)
    shell.ceiling(c.wall, x0, y0, x1, y1, Z_UNDER, "research1_ceiling", holes=holes)
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


def _shaft(mb, x0, y0, x1, y1, door_x, south=True):
    """ガラスの昇降路 1 基: 角の柱 4 本、4 面のガラス、ガイドレール 2 本、1F に止まったかご。

    扉は x = door_x の面（ev_door が外側に付ける）。かごは奥行き 2.0 m で扉に寄せ、レールはかごの
    両脇に立てる。south=False なら南の面のガラスと柱を省く（隣の昇降路と共有する面）。
    """
    z0, z1 = D.Z1, Z_EVTOP
    posts = [(x, y1) for x in (x0, x1)] + ([(x, y0) for x in (x0, x1)] if south else [])
    for x, y in posts:
        kit.box(mb, x - 0.05, y - 0.05, z0, x + 0.05, y + 0.05, z1, RAIL)
    g = 0.01
    panes = [(x0 + 0.05, y1 - g, x1 - 0.05, y1 + g), (x0 - g, y0 + 0.05, x0 + g, y1 - 0.05),
             (x1 - g, y0 + 0.05, x1 + g, y1 - 0.05)]
    if south:
        panes.append((x0 + 0.05, y0 - g, x1 - 0.05, y0 + g))
    for px0, py0, px1, py1 in panes:
        kit.box(mb, px0, py0, z0, px1, py1, z1, GLASS)
    inward = 1.0 if door_x == x0 else -1.0      # 扉から奥へ向かう X の向き
    yc = (y0 + y1) * 0.5
    half = (y1 - y0) * 0.5 - 0.45                # かごの幅の半分
    xd = door_x + inward * 0.12                  # かごの扉側の面
    xb = xd + inward * min(2.0, abs(x1 - x0) - 0.42)
    xm = (xd + xb) * 0.5
    for y in (yc - half - 0.1, yc + half + 0.1):
        kit.box(mb, xm - 0.05, y - 0.04, z0, xm + 0.05, y + 0.04, z1, STEEL)
    ca, cb = sorted((xd, xb))
    kit.box(mb, ca, yc - half, z0 + 0.05, cb, yc + half, z0 + CAR_H, RAIL)
    kit.box(mb, xm - 0.08, yc - half - 0.1, z0 + CAR_H, xm + 0.08, yc + half + 0.1,
            z0 + CAR_H + 0.2, STEEL)


def _west(c):
    mb = c.wall
    (ax0, ay0, ax1, ay1), (bx0, by0, bx1, by1) = D.W_SHAFTS
    _shaft(mb, ax0, ay0, ax1, ay1, ax1)
    _shaft(mb, bx0, by0, bx1, by1, bx1, south=False)
    kit.box(mb, *D.W_ROOM[:2], D.Z1, *D.W_ROOM[2:], Z_EVTOP, NAVY)
    xe = D.W_SHAFTS[0][2]
    for x0, y0, x1, y1 in D.W_SHAFTS:
        for z in (D.Z1, D.Z2):
            ev_door(mb, xe, (y0 + y1) * 0.5, -HALF_PI, z=z)
    sx0, sx1 = D.W_STAIR
    (a0, a1, za, zb), (b0, b1, zc, zd) = D.W_FLIGHTS
    steps(mb, sx0, sx1, a0, a1, za, zb)
    kit.box(mb, sx0, D.W_LANDING[0], D.Z1, sx1, D.W_LANDING[1], zb, TREAD)
    steps(mb, sx0, sx1, b0, b1, zc, zd)
    _hall(c, D.W_HALL, (D.W_HOLE, (ax0, ay0, bx1, by1)), (xe, -D.X_A),
          [(sx0, b1), (sx0, a0), (sx1, a0), (sx1, b1)],
          ((-13.6, 25.0), (-9.0, 22.0)))
    # 2F の学生実験室（閉じた扉。南の壁の北面）
    room_door(mb, -13.9, D.W_HALL[1] + shell.PART * 0.5, 0.0, z=D.Z2)


def _east(c):
    mb = c.wall
    x0, y0, x1, y1 = D.E_SHAFT
    _shaft(mb, x0, y0, x1, y1, x1)
    for z in (D.Z1, D.Z2):
        ev_door(mb, x1, (y0 + y1) * 0.5, -HALF_PI, z=z)
    f0x0, f0x1, f0y0, f0y1 = D.E_FLIGHT1
    steps(mb, f0x0, f0x1, f0y0, f0y1, D.Z1, 2.25)
    lx0, ly0, lx1, ly1 = D.E_LANDING
    kit.box(mb, lx0, ly0, D.Z1, lx1, ly1, 2.25, TREAD)
    f1x0, f1x1, f1y0, f1y1 = D.E_FLIGHT2
    steps(mb, f1x0, f1x1, f1y1, f1y0, 2.25, D.Z2)
    kit.box(mb, f0x1, f0y0, D.Z1, f1x0, f0y1, D.Z2, CONC)       # 2 本の段の間の壁
    _hall(c, D.E_HALL, (D.E_HOLE, D.E_SHAFT), (D.E_SHAFT[2] + 0.2, D.E_HALL[2]),
          [(f1x0, f0y0), (f0x0, f0y0), (f0x0, ly1), (lx1, ly1), (lx1, f0y0)],
          ((13.4, 25.0), (12.6, 20.4)))


def build(c):
    _west(c)
    _east(c)
