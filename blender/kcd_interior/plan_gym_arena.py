"""メインアリーナ: 壁 B・北の仕切り・コートの線・吊りゴール・屋根のトラス・照明・用具。"""

import math

from . import furniture as F
from . import kit, shell
from . import plan_gym_dims as D
from . import plan_gym_shell as S

LINE_Z = 0.013
LW = 0.05
Z_HANG = 12.0       # 吊りゴールの吊り元（トラスの下弦）


# ---- コートの線 ----
def _rect(mb, x0, y0, x1, y1, mat, lw=LW, z=LINE_Z):
    kit.plate(mb, x0, y0, x1, y0 + lw, z, mat)
    kit.plate(mb, x0, y1 - lw, x1, y1, z, mat)
    kit.plate(mb, x0, y0 + lw, x0 + lw, y1 - lw, z, mat)
    kit.plate(mb, x1 - lw, y0 + lw, x1, y1 - lw, z, mat)


def _arc(mb, cx, cy, r, a0, span, mat, seg=14, lw=LW, z=LINE_Z):
    def p(t, rr):
        return (cx + rr * math.cos(t), cy + rr * math.sin(t), z)
    for i in range(seg):
        t0, t1 = a0 + span * i / seg, a0 + span * (i + 1) / seg
        mb.add_quad(p(t0, r), p(t1, r), p(t1, r - lw), p(t0, r - lw), mat)


def _half(mb, cx, y_base, sign):
    """バスケットボールコートの片側（ゴール側）の線。sign=+1 で +Y 向き。"""
    m = "gym_line_white"
    lane = 2.45
    ft = y_base + sign * 5.8
    lo, hi = sorted((y_base, ft))
    kit.plate(mb, cx - lane, lo, cx - lane + LW, hi, LINE_Z, m)
    kit.plate(mb, cx + lane - LW, lo, cx + lane, hi, LINE_Z, m)
    kit.plate(mb, cx - lane, ft - LW / 2, cx + lane, ft + LW / 2, LINE_Z, m)
    base = 0.0 if sign > 0 else math.pi
    _arc(mb, cx, ft, 1.8, base, math.pi, m, seg=12)
    hoop_y = y_base + sign * 1.575
    r, half = 6.75, 6.6
    t3 = math.acos(half / r)
    y_end = hoop_y + sign * r * math.sin(t3)
    lo, hi = sorted((y_base, y_end))
    kit.plate(mb, cx - half - LW, lo, cx - half, hi, LINE_Z, m)
    kit.plate(mb, cx + half, lo, cx + half + LW, hi, LINE_Z, m)
    _arc(mb, cx, hoop_y, r, base + t3 if sign > 0 else math.pi + t3, math.pi - 2 * t3, m, seg=18)
    _arc(mb, cx, hoop_y, 1.25, base, math.pi, m, seg=8)


def court(c):
    mb = c.floor
    cx, cy = D.COURT_C
    x0, x1 = cx - D.COURT_W / 2, cx + D.COURT_W / 2
    y0, y1 = cy - D.COURT_L / 2, cy + D.COURT_L / 2
    _rect(mb, x0, y0, x1, y1, "gym_line_white")
    kit.plate(mb, x0, cy - LW / 2, x1, cy + LW / 2, LINE_Z, "gym_line_white")
    _arc(mb, cx, cy, 1.8, 0.0, math.pi * 2, "gym_line_white", seg=14)
    _half(mb, cx, y0, 1.0)
    _half(mb, cx, y1, -1.0)
    # 横向きの 3 面（赤）
    for yy in D.CROSS_Y:
        _rect(mb, -25.0, yy - 5.3, -4.5, yy + 5.3, "gym_line_red", lw=0.04)
        kit.plate(mb, -25.0, yy - 0.02, -4.5, yy + 0.02, LINE_Z, "gym_line_red")
    # バドミントン（緑）・バレーボール（黄）・黒と青の補助線
    for gx in (-24.0, -17.0, -10.0):
        _rect(mb, gx, 9.0, gx + 6.1, 22.4, "gym_line_green", lw=0.04)
    _rect(mb, -24.0, 12.0, -6.0, 21.0, "gym_line_yellow", lw=0.04)
    _rect(mb, -23.0, 24.0, -8.0, 33.0, "gym_line_black", lw=0.04)
    _rect(mb, -22.0, 25.0, -7.0, 32.0, "gym_line_blue", lw=0.04)


# ---- 吊りゴール ----
def hung_hoop(mb, x, y, ang, rim_z=3.05):
    """天井から吊るバスケットゴール。ang はバックボードの向き（コート側）。"""
    t = kit.T(x, y, 0.0, ang)
    t.box(mb, -0.9, -0.05, rim_z - 0.15, 0.9, 0.0, rim_z + 0.90, "backboard_white")
    t.box(mb, -0.3, -0.055, rim_z - 0.02, 0.3, -0.05, rim_z + 0.45, "hoop_orange")
    for sx in (-0.7, 0.7):
        a = t.p(sx, -0.03, rim_z + 0.9)
        kit.tube(mb, a, (a[0], a[1], Z_HANG), 0.03, "gym_truss_steel", seg=4)
    seg = 8
    ring = [t.p(0.225 * math.sin(2 * math.pi * k / seg),
                0.15 + 0.225 * (1 - math.cos(2 * math.pi * k / seg)), rim_z)
            for k in range(seg)]
    for k in range(seg):
        kit.tube(mb, ring[k], ring[(k + 1) % seg], 0.012, "hoop_orange", seg=4)
    for k in range(0, seg, 2):
        bottom = t.p(0.1 * math.sin(2 * math.pi * k / seg), 0.375, rim_z - 0.40)
        kit.tube(mb, ring[k], bottom, 0.006, "net_white", seg=3)


def hoops(c):
    mb = c.furn("hoops")
    cx = D.COURT_C[0]
    hung_hoop(mb, cx, 6.85, 0.0)
    hung_hoop(mb, cx, 36.8, math.pi)
    for y in D.CROSS_Y:
        hung_hoop(mb, D.HOOP_G_X, y, -math.pi / 2)
        hung_hoop(mb, D.HOOP_B_X, y, math.pi / 2)
    hung_hoop(mb, D.HOOP_B_X - 0.1, 14.5, math.pi / 2, rim_z=6.8)
    hung_hoop(mb, D.HOOP_B_X - 0.1, 29.0, math.pi / 2, rim_z=6.8)


# ---- 壁 B・北の仕切り・南壁の付属 ----
def walls(c):
    mb = c.wall
    x0, x1 = D.WALL_B
    S.ywall(mb, x0, x1, 4.7, 6.9, 0.0, 4.8)
    S.ywall(mb, x0, x1, 6.9, 37.0, 0.0, D.Z_FRONT, gaps=[D.B_DOOR_S], lintel=D.DOOR_H)
    S.ywall(mb, x0, x1, 37.0, 38.85, 0.0, 4.8)
    S.ywall(mb, x0, x1, 38.85, 47.6, 0.0, 3.0, gaps=[D.B_DOOR_N], lintel=D.DOOR_H)
    # 北の仕切り（アリーナと用具庫の間）
    gaps = [D.N_STORE_CLOSED, D.N_STORE_OPEN, D.N_OPEN]
    S.xwall(mb, -27.8, -4.0, 38.85, 39.3, 0.0, D.C_HALL, S.CONC, gaps=gaps, lintel=D.DOOR_H)
    kit.box(mb, D.N_STORE_CLOSED[0], 38.80, 0.0, D.N_STORE_CLOSED[1], 38.85, 2.2, "gym_door_dark")
    for a, b in D.S_DOORS:
        y = D.south_y((a + b) / 2)
        kit.box(mb, a, y, 0.0, b, y + 0.04, 2.2, "gym_door_navy")
    for k in range(10):
        x = -25.0 + 2.4 * k
        y = D.south_y(x)
        kit.box(mb, x, y, 0.3, x + 0.03, y + 0.02, 3.0, "gym_joint_metal")
    for x in (-22.0, -16.0, -10.0):
        y = D.south_y(x)
        kit.box(mb, x - 0.3, y, 8.7, x + 0.3, y + 0.3, 9.3, "gym_vent_dark")
    y = D.south_y(-6.0)
    kit.box(mb, -6.4, y, 1.1, -5.4, y + 0.2, 2.1, "gym_door_dark")
    kit.box(mb, -5.75, y + 0.2, 1.9, -5.65, y + 0.22, 2.0, "fire_red")
    shell.clock(mb, -3.68, 30.0, 3.4, -math.pi / 2, 0.3)


# ---- 屋根・照明・ネット ----
def roof(c):
    mb = c.furn("roof")
    for i in range(6):
        F.roof_truss(mb, -27.0, 1.5, 6.5 + 5.7 * i, 13.5, depth=1.5, seg=9,
                     mat="gym_truss_steel")
    for k in range(4):
        x = -24.0 + 7.0 * k
        kit.tube(mb, (x, 5.5, 12.1), (x, 38.5, 12.1), 0.07, "gym_truss_steel", seg=4)
    pts = shell.ceiling_lights(mb, -24.0, 8.0, -5.0, 36.0, 11.85, sx=4.8, sy=5.6,
                               w=1.0, l=0.6, mat="gym_light_arena", limit=24)
    c.lights_from(pts, 11.85, energy=800.0, step=4, radius=4.0)
    for y in D.NET_Y:
        kit.vplate(mb, (-24.8, y), (-4.4, y), 7.0, 12.0, "gym_net")
        kit.vplate(mb, (-24.8, y), (-4.4, y), 7.0, 12.0, "gym_net", flip=True)


# ---- 床の用具 ----
def items(c):
    mb = c.furn("arena_items")
    kit.box(mb, -6.6, 36.2, 0.0, -5.4, 37.0, 0.5, "gym_box_green")
    kit.box(mb, -26.4, 34.0, 0.15, -25.6, 35.0, 0.9, "gym_cart_blue")
    kit.box(mb, -26.2, 35.2, 0.0, -25.4, 36.0, 0.8, "gym_rail_metal")
    for y in (12.0, 18.0, 24.0, 30.0):
        F.bench(mb, -26.95, y, -math.pi / 2, 1.2, mat="gym_bench_wood", leg="gym_rail_metal")
    kit.box(mb, -6.4, 38.3, 0.0, -4.6, 38.8, 0.72, "gym_cart_blue")


def build(c):
    court(c)
    hoops(c)
    walls(c)
    roof(c)
    items(c)
