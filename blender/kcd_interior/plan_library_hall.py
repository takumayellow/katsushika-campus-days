"""図書館棟 3F〜4F: ホール（前室・4F ロビー・扇形の側壁・多面体の天井）と舞台。

客席の床・座席・2 階席は plan_library_seating が作る。ここでは客席を囲む外殻と、
その内側の壁（木のルーバーの三角の折り面）・天井・舞台を作る。
"""

import math

from . import kit
from . import furn_library as FL
from . import plan_library_dims as D

WOOD = FL.WOOD
DARK = FL.DARK
PART = "library_partition_white"
LOUVER = "library_hall_louver"
JOINT_K = "library_joint_black"
CRIMSON = "library_carpet_crimson"
CEIL = "library_hall_ceiling"
WASH = "library_wall_washer"
WHITE = "library_ceiling_white"
EXIT = "sign_exit_green"

ZB = D.Z3 - 0.22          # 3F スラブの下面（壁の足元）
Z_TOP = D.Z_HALL_TOP
Z_CEIL = 21.2             # 天井の縁の高さ（側壁の上端）
FOLD_Z = (ZB, 13.6, 17.4, Z_CEIL)
FOLD_YS = (40.0, 42.4, 44.7, 45.9, 48.5, 51.0, D.ROWS_FRONT[1], D.STAGE[1])
DOOR_SEG = (D.CROSS[0], D.CROSS[1])
REAR_DOORS = (-4.3, 1.1)  # 前室から客席へのスライド扉の中心
LOBBY_HOLES = ((11.4, 13.2), (22.0, 23.8))   # 4F ロビーの壁の開口（a からの距離）
SKY = (D.HALL_AXIS, 40.75, 2.2, 20.98, 19.9)  # 天窓（中心 x, y, 半幅, 縁の高さ, 頂点）


def _exit_sign(mb, x0, x1, y0, y1, z0, z1):
    kit.box(mb, x0, y0, z0, x1, y1, z1, EXIT)


# ---- 外殻 ----
def _outer(mb):
    """客席を囲む外壁・舞台の左右の壁・奥の壁（ホールの外形の線の内側）。"""
    x0, y0, x1, _ = D.HALL
    xi0, yi0, xi1, _ = D.HALL_IN
    yn = D.HALL_NARROW[1]
    kit.box(mb, x0, y0, ZB, xi0, yn, Z_TOP, PART)
    kit.box(mb, xi1, y0, ZB, x1, yn, Z_TOP, PART)
    kit.box(mb, x0, yn - 0.3, ZB, D.STAGE[0], yn, Z_TOP, PART)
    kit.box(mb, D.STAGE[2] + 4.1, yn - 0.3, ZB, x1, yn, Z_TOP, PART)
    kit.box(mb, D.STAGE[0] - 0.3, D.STAGE[1], ZB, D.STAGE[0], D.STAGE[3], Z_TOP, PART)
    kit.box(mb, D.STAGE[2], D.STAGE[1], ZB, D.STAGE[2] + 0.3, 62.8, Z_TOP, PART)
    # 前室の正面の壁（ホワイエからの入口と、4F ロビーへの入口）
    ya = (y0 + yi0) * 0.5
    FL.holed_wall(mb, (x0, ya), (x1, ya), ZB, Z_TOP, PART, thick=0.3,
                  holes=((14.6, 24.4, D.Z_ANTE, 15.4), (31.9, 35.1, D.Z4, 19.4)))


# ---- 前室（3F から上がった先、2 階席の下） ----
def _ante(c, mb):
    xi0, yi0, xi1, _ = D.HALL_IN
    y1 = D.ANTE_Y1
    kit.box(mb, xi0, yi0, ZB, xi1, y1, D.Z_ANTE, PART, top=CRIMSON)
    for x, y in ((-12.0, 24.0), (-1.6, 24.0), (9.0, 24.0), (-12.0, 30.0), (9.0, 30.0)):
        c.light(x, y, D.C_ANTE - 0.4, 250.0, 1.4)
    for x in (-10.0, -1.6, 7.0):
        c.light(x, 32.7, D.C_UNDER - 0.4, 200.0, 1.2)
    # 客席との境の壁（扉の上は 2 階席の下面まで）
    yw = y1 - 0.1
    holes = tuple((x - xi0 - 1.1, x - xi0 + 1.1, D.Z_ANTE, 15.0) for x in REAR_DOORS)
    FL.holed_wall(mb, (xi0, yw), (xi1, yw), D.Z_ANTE, D.C_UNDER, PART, thick=0.2, holes=holes)
    for x in REAR_DOORS:
        FL.door(mb, x, yw, D.Z_ANTE, 0.0, w=2.2, h=2.13, leaf=WOOD, open_=1.0)
    _rear_face(mb, y1 + 0.01)


def _rear_face(mb, y):
    """客席の側から見た後ろの壁: ルーバー・扉の上の誘導灯・壁を洗う光の線。"""
    xi0, _, xi1, _ = D.HALL_IN
    edges = [xi0]
    for x in REAR_DOORS:
        edges += [x - 1.1, x + 1.1]
    edges.append(xi1)
    spans = list(zip(edges[0::2], edges[1::2]))
    for a, b in spans:
        kit.vplate(mb, (b, y), (a, y), D.Z_ANTE, D.C_UNDER, LOUVER)
    for x in REAR_DOORS:
        kit.vplate(mb, (x + 1.1, y), (x - 1.1, y), 15.0, D.C_UNDER, LOUVER)
        _exit_sign(mb, x - 0.31, x + 0.31, y - 0.01, y + 0.05, 15.14, 15.4)
    x = -18.0
    while x <= 15.0:
        if all(abs(x - d) >= 1.4 for d in REAR_DOORS):
            kit.box(mb, x - 0.15, y, 15.45, x + 0.15, y + 0.09, 15.55, WASH)
        x += 2.4


# ---- 4F ロビー（2 階席の後ろ） ----
def _lobby(c, mb):
    xi0, yi0, xi1, _ = D.HALL_IN
    ya, yb = D.BALC_AISLE
    kit.box(mb, xi0, yi0, D.C_ANTE, xi1, ya, D.Z4, PART, top=CRIMSON, bottom=WHITE)
    kit.box(mb, xi0, ya, D.C_ANTE, xi1, yb, D.Z4, PART, top="library_aisle_carpet",
            bottom=WHITE)
    yw = ya - 0.1
    holes = tuple((s0, s1, D.Z4, 19.4) for s0, s1 in LOBBY_HOLES)
    FL.holed_wall(mb, (xi0, yw), (xi1, yw), D.Z4, Z_TOP, PART, thick=0.2, holes=holes)
    for s0, s1 in LOBBY_HOLES:
        _exit_sign(mb, xi0 + s0 + 0.59, xi0 + s1 - 0.59, ya, ya + 0.06, 19.55, 19.81)
    # 調光室の窓
    kit.vplate(mb, (2.8, ya + 0.01), (-6.0, ya + 0.01), 19.9, 20.9, "library_glass_dark")
    kit.plate(mb, xi0, yi0, xi1, yw - 0.1, D.C4, WHITE, flip=True)
    for x in (-12.0, -1.6, 9.0):
        c.light(x, 24.0, D.C4 - 0.3, 250.0, 1.4)


# ---- 客席の側壁（ルーバーの三角の折り面） ----
def _fold_line(line):
    """輪郭の点の列を、内側（客席）が左になる向きに並べ、折り面の区切りを入れる。"""
    pts = []
    for y in FOLD_YS:
        pts.append((D.wall_x(line, y, upper=True), y))
    return pts


def _side_wall(mb, line, west):
    """一方の側壁。west は西（X が小さい側）。"""
    x_rear = line[0][0]
    x_in = line[2][0]
    y0 = line[0][1]
    yk = line[1][1]
    pts = _fold_line(line)
    if west:
        pts = pts[::-1]
        rear = [(x_in, yk), (x_rear, yk), (x_rear, y0)]
    else:
        rear = [(x_rear, y0), (x_rear, yk), (x_in, yk)]
    # 側方の 2 階席の下と脇は平らなルーバー（足元は後ろの列の床より下から）
    for a, b in zip(rear, rear[1:]):
        kit.vplate(mb, b, a, ZB, Z_CEIL, LOUVER)
    for i, (a, b) in enumerate(zip(pts, pts[1:])):
        ys = sorted((a[1], b[1]))
        door = abs(ys[0] - DOOR_SEG[0]) < 1e-6 and abs(ys[1] - DOOR_SEG[1]) < 1e-6
        for j, (z0, z1) in enumerate(zip(FOLD_Z, FOLD_Z[1:])):
            if door and j == 0:
                _side_door(mb, a, b, z0, z1)
                continue
            FL.folded_panel(mb, a, b, z0, z1, 0.2 if (i + j) % 2 == 0 else 0.05, LOUVER)
    for x, y in pts + rear[1:]:
        kit.box_nb(mb, x - 0.04, y - 0.04, ZB, x + 0.04, y + 0.04, Z_CEIL, JOINT_K)


def _side_door(mb, a, b, z0, z1):
    """横通路の突き当たりの扉と誘導灯（扉は閉じている）。"""
    L = math.hypot(b[0] - a[0], b[1] - a[1])
    ang = math.atan2(b[1] - a[1], b[0] - a[0])
    z = D.CROSS[2]
    FL.holed_wall(mb, a, b, z0, z1, LOUVER, thick=0.06,
                  holes=((L * 0.5 - 0.62, L * 0.5 + 0.62, z, z + 2.17),))
    mx, my = (a[0] + b[0]) * 0.5, (a[1] + b[1]) * 0.5
    FL.door(mb, mx, my, z, ang, open_=0.0, leaf=DARK)
    kit.T(mx, my, z, ang).box(mb, -0.31, 0.03, 2.25, 0.31, 0.09, 2.51, EXIT)


# ---- 天井（クリーム色の多面体）と天窓 ----
def _down_face(mb, tri, mat):
    (ax, ay, _), (bx, by, _), (cx, cy, _) = tri
    if (bx - ax) * (cy - ay) - (by - ay) * (cx - ax) > 0.0:
        tri = tri[::-1]
    mb.add_face(list(tri), mat)


def _ceil_grid(c, mb, ys, xl, xr, edge_rows, nx=7):
    rows = []
    for j, y in enumerate(ys):
        x0, x1 = xl(y), xr(y)
        row = []
        for i in range(nx):
            x = x0 + (x1 - x0) * i / (nx - 1)
            edge = j in edge_rows or i in (0, nx - 1)
            h = ((i * 73856093) ^ (j * 19349663)) % 1000 / 1000.0
            row.append((x, y, Z_CEIL if edge else 21.0 + 0.45 * h, edge))
        rows.append(row)
    for j in range(len(ys) - 1):
        for i in range(nx - 1):
            p00, p10 = rows[j][i][:3], rows[j][i + 1][:3]
            p01, p11 = rows[j + 1][i][:3], rows[j + 1][i + 1][:3]
            _down_face(mb, (p00, p10, p11), CEIL)
            _down_face(mb, (p00, p11, p01), CEIL)
    for row in rows:
        for x, y, z, edge in row:
            if not edge:
                kit.plate(mb, x - 0.12, y - 0.12, x + 0.12, y + 0.12, z - 0.02, WASH, flip=True)


def _ceiling(c, mb):
    ya = D.BALC_AISLE[0]
    yk = D.HALL_WALL_W[1][1]
    xi0, xi1 = D.HALL_IN[0], D.HALL_IN[2]
    _ceil_grid(c, mb, (ya, D.ANTE_Y1, yk), lambda y: xi0, lambda y: xi1, (0, 2))
    ys = (yk, 45.0, 50.3, D.ROWS_FRONT[1], D.STAGE[1], 59.2, 62.8)
    _ceil_grid(c, mb, ys, lambda y: D.wall_x(D.HALL_WALL_W, y, upper=True),
               lambda y: D.wall_x(D.HALL_WALL_E, y, upper=True), (0, len(ys) - 1))
    # 天窓（ドームの真下の逆ピラミッド）
    cx, cy, r, zr, za = SKY
    ring = [(cx - r, cy - r), (cx + r, cy - r), (cx + r, cy + r), (cx - r, cy + r)]
    for k in range(4):
        (ax, ay), (bx, by) = ring[k], ring[(k + 1) % 4]
        _down_face(mb, ((ax, ay, zr), (bx, by, zr), (cx, cy, za)), "library_skylight")
    for x in (-12.0, -1.6, 8.8):
        for y in (37.0, 42.0, 47.0, 52.0):
            c.light(x, y, 19.5, 600.0, 2.0)


def build_shell(c, mb):
    """mb は furn_library_13_hallshell。"""
    _outer(mb)
    _ante(c, mb)
    _lobby(c, mb)
    _side_wall(mb, D.HALL_WALL_W, True)
    _side_wall(mb, D.HALL_WALL_E, False)
    _ceiling(c, mb)


# ---- 舞台 ----
def _deck(mb):
    x0, y0, x1, _, z = D.STAGE
    kit.box(mb, x0, y0, ZB, x1, 62.8, z, DARK, top="library_floor_wood_gloss")
    kit.box(mb, x0, y0 - 0.03, z - 0.1, x1, y0 + 0.03, z + 0.02, DARK)
    zf = D.row_z(1)
    for sx0 in (-11.8, D.HALL_AXIS * 2.0 + 10.4):
        FL.stair(mb, sx0, y0 - 1.5, sx0 + 1.4, y0, zf, z, 5, "+y")


def _back(mb):
    x0, _, x1, _, z = D.STAGE
    kit.box(mb, x0, 62.8, z, x1, 63.0, Z_TOP, "library_stage_wall")
    for zb in (13.4, 16.2, 19.0):
        kit.box(mb, x0, 62.74, zb, x1, 62.8, zb + 0.08, JOINT_K)


def _wings(mb):
    """舞台の左右の縦のルーバーと、袖への扉（閉じている）。"""
    x0, y0, x1, _, z = D.STAGE
    yd0, yd1 = 60.38, 61.62
    for x, sgn in ((x0 + 0.01, 1), (x1 - 0.01, -1)):
        for ya, yb, za in ((y0, yd0, z), (yd1, 62.8, z), (yd0, yd1, 12.92)):
            a, b = ((x, ya), (x, yb)) if sgn > 0 else ((x, yb), (x, ya))
            kit.vplate(mb, a, b, za, Z_CEIL, LOUVER)
    FL.door(mb, x0, 61.0, z, -math.pi * 0.5, open_=0.0)
    FL.door(mb, x1, 61.0, z, math.pi * 0.5, open_=0.0)


def _piano(mb):
    """上手のグランドピアノ。鍵盤は -X を向き、屋根は客席（-Y）の側へ開く。"""
    t = kit.T(7.0, 58.0, D.STAGE[4], -math.pi * 0.5)
    black = "library_counter_black"
    body = [(-0.75, 0.0), (0.75, 0.0), (0.75, 1.0), (0.55, 1.6), (0.15, 2.0),
            (-0.35, 2.2), (-0.75, 2.0)]
    mb.add_prism([t.p2(x, y) for x, y in body], t.z + 0.62, t.z + 0.95, black, black)
    t.box(mb, -0.75, -0.25, 0.72, 0.75, 0.0, 0.80, black)
    t.plate(mb, -0.72, -0.24, 0.72, -0.02, 0.805, "plastic_white")
    for lx, ly in ((-0.6, 0.15), (0.6, 0.15), (-0.45, 1.95)):
        t.box_nb(mb, lx - 0.05, ly - 0.05, 0.0, lx + 0.05, ly + 0.05, 0.62, black)
    kit.thick_quad(mb, t.p(-0.75, 0.1, 0.95), t.p(-0.75, 2.1, 0.95), t.p(0.3, 1.7, 1.7),
                   t.p(0.6, 0.2, 1.55), 0.02, black)
    kit.tube(mb, t.p(0.45, 1.0, 0.95), t.p(0.4, 1.0, 1.62), 0.012, "stainless", seg=4)
    t.box(mb, -0.4, -1.0, 0.0, 0.4, -0.65, 0.5, black)


def _props(mb):
    z = D.STAGE[4]
    for x, y in ((D.HALL_AXIS, 57.0), (-10.5, 57.2)):
        kit.T(x, y, z, math.pi).box(mb, -0.35, -0.25, 0.0, 0.35, 0.25, 1.05, WOOD)
    for y in (57.0, 59.0, 61.0):
        kit.box(mb, -13.0, y - 0.04, 19.5, 10.0, y + 0.04, 19.62, "library_louver_black")


def _chandelier(mb):
    """舞台の前の上の、輪を重ねた球のシャンデリア。"""
    cx, cy = D.HALL_AXIS, 54.5
    mat = "library_chandelier"
    rings = ((3.0, 19.0), (2.2, 18.6), (1.4, 18.2))
    n = 12
    for r, z in rings:
        pts = [(cx + r * math.cos(2 * math.pi * k / n), cy + r * math.sin(2 * math.pi * k / n), z)
               for k in range(n)]
        for k in range(n):
            kit.tube(mb, pts[k], pts[(k + 1) % n], 0.04, mat, seg=4)
    for k in range(3):
        a = 2 * math.pi * k / 3
        p = (cx + 3.0 * math.cos(a), cy + 3.0 * math.sin(a), 19.0)
        kit.tube(mb, p, (p[0], p[1], 21.0), 0.015, "stainless", seg=4)
        for (r0, z0), (r1, z1) in zip(rings, rings[1:]):
            kit.tube(mb, (cx + r0 * math.cos(a), cy + r0 * math.sin(a), z0),
                     (cx + r1 * math.cos(a), cy + r1 * math.sin(a), z1), 0.02, mat, seg=4)
    kit.tube(mb, (cx, cy, 18.2), (cx, cy, 17.6), 0.06, mat, seg=6)


def build_stage(c, mb):
    """mb は furn_library_16_stage。"""
    _deck(mb)
    _back(mb)
    _wings(mb)
    _piano(mb)
    _props(mb)
    _chandelier(mb)
    for x in (-9.0, D.HALL_AXIS, 6.0):
        c.light(x, 58.5, 19.0, 500.0, 2.0)
