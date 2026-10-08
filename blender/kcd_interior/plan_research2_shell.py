"""第2研究棟の躯体: 外周 15 辺の壁、1F・2F の床と天井、窓際の下がり天井、2F のガラスとテラス。

外周の閉じ（closure）は外接矩形の内面線で測る。西（P4）と南（P8）の尖りは測る線の外へ
出るので、床は CLIP_X / CLIP_Y で切り、切った先は柱状の面でふさぐ。壁の天面には下向きの
蓋を 0.01 上に貼り、壁の天面に立てないようにする。
"""

import math

from . import kit, shell
from . import plan_research2_dims as D
from . import plan_research2_poly as P

CONCRETE = "research2_wall_concrete"
GLASS = "research2_glass"
SASH1 = "research2_mullion_dark"
SASH2 = "research2_sash_2f"
STEEL = "research2_steel_dark"
CAP_UP = 0.01
KITCHEN_T = 0.16
# 1F の配膳口・2F のカウンターの奥の厨房のくぼみ（x0, x1, 開口の下端, 上端, 奥の面の y）
POCKET1 = (-26.0, 10.0, 1.0, 2.2, 35.4)
POCKET2 = (-20.0, 4.0, 0.95, 2.3, 35.6)
KITCHEN2_Y = 33.9            # 2F の厨房の壁の面（カウンター A〜D はこの手前）
STAIR_WX = (10.04, 10.2)     # 奥の階段の西の壁（厨房側）
STAIR_EX = (12.8, 12.96)     # 東の壁（洗い場側）
DISH_GAP = (14.0, 20.0)      # 返却口の奥の開口
DOOR_H = 2.4

FLOOR1 = D.OUTLINE[3:] + (D.OUTLINE[0], (D.NW_X[1], D.NW_Y))
NW_POLY = (D.OUTLINE[0], D.OUTLINE[1], D.OUTLINE[2], D.OUTLINE[3], (D.NW_X[1], D.NW_Y))
SOFFIT_CHAIN = D.OUTLINE[3:11] + (
    (4.6, D.OUTLINE[10][1] + (4.6 - D.OUTLINE[10][0]) * (D.OUTLINE[11][1] - D.OUTLINE[10][1])
     / (D.OUTLINE[11][0] - D.OUTLINE[10][0])),)
GLASS2_CHAIN = D.GLASS2 + (D.OUTLINE[10], D.OUTLINE[11])
KITCHEN_RECT = (D.NW_X[1], D.KITCHEN_Y, STAIR_EX[1], D.Y_BACK)


def _edge(i):
    return D.OUTLINE[i], D.OUTLINE[(i + 1) % len(D.OUTLINE)]


def _normal(a, b):
    dx, dy = b[0] - a[0], b[1] - a[1]
    L = math.hypot(dx, dy)
    return (-dy / L, dx / L)


def cap(mb, a, b, z, off0, off1):
    """a -> b の線から左手へ off0〜off1 の帯に、高さ z + CAP_UP の下向きの蓋を貼る。"""
    n = _normal(a, b)
    pts = [(a[0] + n[0] * off0, a[1] + n[1] * off0), (b[0] + n[0] * off0, b[1] + n[1] * off0),
           (b[0] + n[0] * off1, b[1] + n[1] * off1), (a[0] + n[0] * off1, a[1] + n[1] * off1)]
    zc = z + CAP_UP
    mb.add_face([(p[0], p[1], zc) for p in reversed(pts)], CONCRETE)


def mullions(mb, a, b, z0, z1, mat, seg, gaps=(), w=0.08):
    """a -> b の外面線に沿った縦の桟（辺の端から seg おき。開口の中は抜く）。"""
    L = math.hypot(b[0] - a[0], b[1] - a[1])
    d = ((b[0] - a[0]) / L, (b[1] - a[1]) / L)
    n = (-d[1], d[0])
    k = seg
    while k < L - 0.2:
        if not any(s - 0.05 < k < e + 0.05 for s, e in gaps):
            pts = []
            for s, off in ((k - w * 0.5, 0.0), (k + w * 0.5, 0.0), (k + w * 0.5, 0.24),
                           (k - w * 0.5, 0.24)):
                pts.append((a[0] + d[0] * s + n[0] * off, a[1] + d[1] * s + n[1] * off))
            mb.add_prism(pts, z0, z1, mat)
        k += seg


def wall(c, line, z0, z1, mat=CONCRETE, glass=None, header=0.0, gaps=(), gap_top=None,
         top=True, sash=SASH1, seg=1.5):
    """外周壁の 1 帯。glass なら床からのガラスと濃い色の桟。top=True なら上端に蓋。"""
    a, b = line
    shell.outer_wall(c.wall, a, b, z0, z1, wall=mat, glass=glass, sill=0.0, header=header,
                     seg=1e9, gaps=gaps, gap_top=gap_top)
    if glass:
        mullions(c.wall, a, b, z0, z1 - header, sash, seg, gaps=gaps)
    if top:
        cap(c.wall, a, b, z1, 0.0, shell.WALL)


def part(c, a, b, z0, z1, mat=CONCRETE, thick=KITCHEN_T):
    """間仕切り（線の中心に厚み）と天面の蓋。"""
    shell.partition(c.wall, a, b, z0, z1, mat, thick=thick)
    cap(c.wall, a, b, z1, -thick * 0.5, thick * 0.5)


def door_part(c, a, b, z0, z1, gap, mat=CONCRETE, thick=KITCHEN_T, head=DOOR_H):
    """軸に沿った間仕切りに、a からの距離 gap=(s, e) の扉口（高さ head）を開ける。"""
    L = math.hypot(b[0] - a[0], b[1] - a[1])
    u = ((b[0] - a[0]) / L, (b[1] - a[1]) / L)

    def at(s):
        return (a[0] + u[0] * s, a[1] + u[1] * s)

    part(c, a, at(gap[0]), z0, z1, mat, thick)
    part(c, at(gap[1]), b, z0, z1, mat, thick)
    part(c, at(gap[0]), at(gap[1]), z0 + head, z1, mat, thick)


def _liner(mb, hole, z0, z1, mat):
    """穴の周りの見付（穴の内側を向く）。"""
    hx0, hy0, hx1, hy1 = hole
    ring = [(hx0, hy0), (hx0, hy1), (hx1, hy1), (hx1, hy0)]
    for k in range(4):
        kit.vplate(mb, ring[k], ring[(k + 1) % 4], z0, z1, mat)


# --------------------------------------------------------------------------- #
#  外周
# --------------------------------------------------------------------------- #
def _outer(c):
    for i in D.E_NW + (D.E_WC, D.E_EAST, D.E_BACK):
        wall(c, _edge(i), D.Z1, D.C2)
    # 西北西の曲面: 1F は床から天井のガラス、2F はテラスの低い立ち上がりとメッシュの柵
    for i in D.E_TERRACE:
        wall(c, _edge(i), D.Z1, D.Z2, glass=GLASS, header=D.Z2 - D.C1, top=False)
        _parapet(c, _edge(i))
    a, b = _edge(D.E_SW)
    wall(c, (a, b), D.Z1, D.Z2, glass=GLASS, header=D.Z2 - D.C1, top=False)
    _parapet(c, (a, D.G1))
    wall(c, (D.G1, b), D.Z2, D.C2, mat=SASH2, glass=GLASS, header=0.2, sash=SASH2)
    # 入口の辺: 1F は風除室の開口、2F は床から天井のガラス
    front = _edge(D.E_FRONT)
    wall(c, front, D.Z1, D.Z2, glass=GLASS, header=D.Z2 - D.C1, gaps=D.DOOR_EDGE_GAPS,
         gap_top=3.0, top=False)
    wall(c, front, D.Z2, D.C2, mat=SASH2, glass=GLASS, header=0.2, sash=SASH2)
    x0, _y0, x1, _y1 = D.VEST
    shell.glass_entrance(c.wall, x0, x1, D.Z1, 3.0, D.ENTRANCE_Y, frame=SASH1, glass=GLASS)
    c.door_gap = (x0, x1)
    # 段の面: 1F は研究棟側のロビーまで続くガラス（開かない）、2F は打放し
    lobby = _edge(D.E_LOBBY)
    wall(c, lobby, D.Z1, D.Z2, glass=GLASS, header=D.Z2 - D.C1, top=False)
    wall(c, lobby, D.Z2, D.C2)


def _parapet(c, line):
    """テラスの外の低い立ち上がりと、その上のメッシュの柵（両面）。"""
    a, b = line
    zt = D.Z2 + 0.35
    shell.outer_wall(c.wall, a, b, D.Z2, zt, wall=CONCRETE, sill=0.0, header=0.0)
    cap(c.wall, a, b, zt, 0.0, shell.WALL)
    n = _normal(a, b)
    o = shell.WALL * 0.5
    pa, pb = (a[0] + n[0] * o, a[1] + n[1] * o), (b[0] + n[0] * o, b[1] + n[1] * o)
    kit.vplate(c.wall, pa, pb, zt, D.Z2 + D.PARAPET, "research2_mesh_fence")
    kit.vplate(c.wall, pa, pb, zt, D.Z2 + D.PARAPET, "research2_mesh_fence", flip=True)
    kit.tube(c.wall, (pa[0], pa[1], D.Z2 + D.PARAPET), (pb[0], pb[1], D.Z2 + D.PARAPET),
             0.03, STEEL, seg=4)


def _glass2(c):
    """2F の引いたガラスの線（テラスとの境）と、線の上の鉄骨の柱。"""
    for a, b in zip(D.GLASS2, D.GLASS2[1:]):
        wall(c, (a, b), D.Z2, D.C2, mat=SASH2, glass=GLASS, header=0.2, sash=SASH2)
    chain = GLASS2_CHAIN
    for t in D.STEEL_COLS:
        p, u = P.along(chain, t)
        n = (-u[1], u[0])
        cx, cy = p[0] + n[0] * 0.43, p[1] + n[1] * 0.43
        h = 0.125
        pts = [(cx + (u[0] * sx + n[0] * sy) * h, cy + (u[1] * sx + n[1] * sy) * h)
               for sx, sy in ((-1, -1), (1, -1), (1, 1), (-1, 1))]
        c.wall.add_prism(pts, D.Z2, D.C2, STEEL)
    # テラスの床（入れない。外周の閉じの線の内側だけ）
    P.fill(c.floor, D.TERRACE, D.Z2, "research2_panel_grey", xmin=D.CLIP_X, ymin=D.CLIP_Y)


# --------------------------------------------------------------------------- #
#  室内の壁
# --------------------------------------------------------------------------- #
def _nw_block(c):
    """北西の塊（厨房の裏。入れない）の南と東の壁。"""
    corner = (D.NW_X[1], D.NW_Y)
    wall(c, (corner, D.OUTLINE[3]), D.Z1, D.C2)
    wall(c, (D.OUTLINE[0], corner), D.Z1, D.C2)
    P.fill(c.wall, NW_POLY, D.C2, CONCRETE, down=True)


def _kitchen(c):
    """1F の配膳口・2F のカウンターの奥の厨房の壁と、浅いくぼみ。"""
    t = KITCHEN_T * 0.5
    x_w = D.NW_X[1] + shell.WALL * 0.5
    # 1F: 面 y = KITCHEN_Y。配膳口は腰の上から帯の下まで開ける
    px0, px1, pz0, pz1, py = POCKET1
    y1 = D.KITCHEN_Y + t
    part(c, (x_w, y1), (px0, y1), D.Z1, D.C1)
    part(c, (px1, y1), (STAIR_WX[1], y1), D.Z1, D.C1)
    part(c, (px0, y1), (px1, y1), D.Z1, pz0)
    part(c, (px0, y1), (px1, y1), pz1, D.C1)
    part(c, (px0 - t, D.KITCHEN_Y), (px0 - t, py + KITCHEN_T), D.Z1, D.C1)
    part(c, (px0, py + t), (STAIR_WX[0], py + t), D.Z1, D.C1, mat="research2_stainless")
    # 2F: 面 y = KITCHEN2_Y。カウンター A〜D の上を開け、奥にオープンキッチン
    qx0, qx1, qz0, qz1, qy = POCKET2
    y2 = KITCHEN2_Y + t
    part(c, (x_w, y2), (qx0, y2), D.Z2, D.C2)
    part(c, (qx1, y2), (STAIR_WX[1], y2), D.Z2, D.C2)
    part(c, (qx0, y2), (qx1, y2), D.Z2, D.Z2 + qz0)
    part(c, (qx0, y2), (qx1, y2), D.Z2 + qz1, D.C2)
    for x in (qx0 - t, qx1 + t):
        part(c, (x, KITCHEN2_Y), (x, qy + KITCHEN_T), D.Z2, D.C2)
    part(c, (qx0, qy + t), (qx1, qy + t), D.Z2, D.C2, mat="research2_stainless")
    # 奥の階段の両脇の壁と北の壁、2F の洗い場の上の壁
    for x0, x1 in (STAIR_WX, STAIR_EX):
        xm = (x0 + x1) * 0.5
        part(c, (xm, D.KITCHEN_Y), (xm, D.INNER[14][1] + 0.1), D.Z1, D.C1)
        part(c, (xm, KITCHEN2_Y), (xm, D.INNER[14][1] + 0.1), D.Z2, D.C2)
    yl = D.BACK_LANDING[3] + t
    part(c, (STAIR_WX[1], yl), (STAIR_EX[0], yl), D.Z1, D.C2)
    part(c, (STAIR_EX[1], y2), (D.PART_X, y2), D.Z2, D.C2)
    # 1F の洗い場（返却口の奥に開口）
    g0, g1 = DISH_GAP
    part(c, (STAIR_EX[1], y1), (g0, y1), D.Z1, D.C1)
    part(c, (g1, y1), (D.PART_X, y1), D.Z1, D.C1)
    part(c, (g0, y1), (g1, y1), DOOR_H, D.C1)


def _wc(c):
    """入口の右のトイレ（入れない）。西の面は打放しの壁 X 4.6。"""
    x0, _y0, x1, y1 = D.WC
    part(c, (4.75, 6.2), (4.75, y1 + 0.16), D.Z1, D.C1, thick=0.3)
    part(c, (4.6, y1 + 0.08), (x1 + 0.7, y1 + 0.08), D.Z1, D.C1)


def _research(c):
    """研究棟側との間仕切り（1F・2F とも扉口）、EV の箱、北北東の階段室のガラス。"""
    a, b = (D.PART_X, D.PART_Y[0]), (D.PART_X, D.PART_Y[1])
    gap = (D.PART_DOOR[0] - D.PART_Y[0], D.PART_DOOR[1] - D.PART_Y[0])
    door_part(c, a, b, D.Z1, D.C1, gap)
    door_part(c, a, b, D.Z2, D.C2, gap)
    ex0, ey0, ex1, ey1 = D.PART_X, D.EV[1], D.EV[2], D.EV_HALL[3]
    c.wall.add_prism([(ex0, ey0), (ex1, ey0), (ex1, ey1), (ex0, ey1)], D.Z1, D.C2,
                     "research2_lobby_panel")
    y = D.R_STAIR[1]
    xa, xb = D.EV[2], D.INNER[13][0]
    c.wall.add_prism([(xa, y - 0.02), (xb, y - 0.02), (xb, y + 0.02), (xa, y + 0.02)],
                     D.Z1, D.C2, GLASS)
    for z0, z1 in ((D.Z1, D.C1), (D.Z2, D.C2)):
        mullions(c.wall, (xa, y - 0.12), (xb, y - 0.12), z0, z1, SASH1, 1.3)
        kit.box(c.wall, xa, y - 0.06, z1 - 0.12, xb, y + 0.06, z1, SASH1)


# --------------------------------------------------------------------------- #
#  床・天井
# --------------------------------------------------------------------------- #
def _floors(c):
    holes = (D.FRONT_HOLE, D.BACK_HOLE, D.R_STAIR_HOLE)
    P.fill(c.floor, FLOOR1, D.Z1, "research2_floor_1f_wood", xmin=D.CLIP_X, ymin=D.CLIP_Y)
    # 尖りの先（外周の閉じの線の外）は床を張らず、柱状の面でふさぐ
    for tip in (_tip_x(), _tip_y()):
        c.wall.add_prism(tip, D.Z1, D.C1, "research2_soffit")
        c.wall.add_ngon_flat(tip, D.C1 + CAP_UP, "research2_soffit", flip=True)
    P.fill(c.floor, D.FLOOR2, D.Z2, "research2_floor_2f_oak", holes=holes)
    _liner(c.wall, D.FRONT_HOLE, D.C1, D.Z2, CONCRETE)
    _liner(c.wall, D.BACK_HOLE, D.C1, D.Z2, CONCRETE)
    _liner(c.wall, D.R_STAIR_HOLE, D.C1, D.Z2, "research2_brick_core")


def _tip_x():
    """西の尖り（P4）の内面のうち x < CLIP_X の三角形。"""
    a, p, b = D.INNER[3], D.INNER[4], D.INNER[5]
    return (_cross_x(p, b, D.CLIP_X), _cross_x(a, p, D.CLIP_X), p)[::-1]


def _tip_y():
    """南の尖り（P8）の内面のうち y < CLIP_Y の三角形。"""
    a, p, b = D.INNER[7], D.INNER[8], D.INNER[9]
    return (_cross_y(a, p, D.CLIP_Y), p, _cross_y(p, b, D.CLIP_Y))


def _cross_x(a, b, x):
    t = (x - a[0]) / (b[0] - a[0])
    return (x, a[1] + (b[1] - a[1]) * t)


def _cross_y(a, b, y):
    t = (y - a[1]) / (b[1] - a[1])
    return (a[0] + (b[0] - a[0]) * t, y)


def _soffit(c):
    """南南西・西北西のガラスに沿った下がり天井の帯（下面に暖色の間接照明）。"""
    w = c.wall
    o0, o1 = shell.WALL, shell.WALL + D.SOFFIT_W
    l0 = P.chain_offset(SOFFIT_CHAIN, o0)
    l1 = P.chain_offset(SOFFIT_CHAIN, o1)
    lg = P.chain_offset(SOFFIT_CHAIN, o1 - 0.12)
    z = D.SOFFIT_Z
    for i in range(len(SOFFIT_CHAIN) - 1):
        quad = [l0[i], l0[i + 1], l1[i + 1], l1[i]]
        w.add_face([(p[0], p[1], z) for p in reversed(quad)], "research2_soffit")
        glow = [lg[i], lg[i + 1], l1[i + 1], l1[i]]
        w.add_face([(p[0], p[1], z - 0.005) for p in reversed(glow)], "research2_glow_warm")
        kit.vplate(w, l1[i + 1], l1[i], z, D.C1, "research2_soffit")
    for k, (x, y) in enumerate(lg[1:-1]):
        c.light(x, y, z - 0.3, 90.0, 1.6)


def _ceilings(c):
    w = c.wall
    holes = (D.FRONT_HOLE, D.BACK_HOLE, D.R_STAIR_HOLE)
    P.fill(w, FLOOR1, D.C1, "research2_ceiling_white", down=True, holes=holes,
           xmin=D.CLIP_X, ymin=D.CLIP_Y)
    _soffit(c)
    wx0, wy0, wx1, wy1 = D.WOOD_CEIL
    P.fill(w, D.FLOOR2, D.C2, "research2_ceiling_2f_white", down=True, holes=(D.WOOD_CEIL,))
    kit.plate(w, wx0, wy0, wx1, wy1, D.C_WOOD, "research2_ceiling_wood", flip=True)
    _liner(w, D.WOOD_CEIL, D.C2, D.C_WOOD, "research2_ceiling_wood")
    for x in D.WOOD_LED_X:
        kit.plate(w, x - 0.04, wy0 + 0.6, x + 0.04, wy1 - 0.6, D.C_WOOD - 0.01,
                  "research2_led_linear", flip=True)
        for y in (wy0 + 3.0, wy1 - 3.0):
            c.light(x, y, D.C_WOOD - 0.4, 110.0, 1.6)


def _ground(c):
    """窓の外の地面（レンガ敷きの広場）。外接矩形を 0.8 m 縮めた板と、外へ下がる縁。"""
    x0, y0, x1, y1 = D.X0 + 0.8, D.Y_FACE + 0.8, D.X1 - 0.8, D.Y_BACK - 0.8
    z = -0.02
    kit.plate(c.floor, x0, y0, x1, y1, z, "research2_ground_brick")
    X0, Y0, X1, Y1, zb = D.X0, D.Y_FACE, D.X1, D.Y_BACK, -1.0
    mat = "research2_ground_brick"
    c.floor.add_face([(X0, Y0, zb), (X1, Y0, zb), (x1, y0, z), (x0, y0, z)], mat)
    c.floor.add_face([(X1, Y0, zb), (X1, Y1, zb), (x1, y1, z), (x1, y0, z)], mat)
    c.floor.add_face([(X1, Y1, zb), (X0, Y1, zb), (x0, y1, z), (x1, y1, z)], mat)
    c.floor.add_face([(X0, Y1, zb), (X0, Y0, zb), (x0, y0, z), (x0, y1, z)], mat)


# --------------------------------------------------------------------------- #
#  照明
# --------------------------------------------------------------------------- #
def _keep_out(z_ceil, hangs):
    """吊り看板の真上には照明を置かない。"""
    return tuple((x - 1.6, y - 1.6, x + 1.6, y + 1.6) for x, y, zc in hangs
                 if abs(zc - z_ceil) < 1e-6)


def _in_any(p, rects, pad=0.0):
    return any(r[0] - pad < p[0] < r[2] + pad and r[1] - pad < p[1] < r[3] + pad for r in rects)


def _grid(poly, z, step, keep, avoid, glass, gap):
    """多角形の中の step 格子の点のうち、avoid の矩形の外で glass の線から gap 以上離れたもの。"""
    xs = [p[0] for p in poly]
    ys = [p[1] for p in poly]
    out = []
    y = min(ys) + step * 0.5
    while y < max(ys):
        x = min(xs) + step * 0.5
        while x < max(xs):
            p = (x, y)
            if (P.inside(p, poly) and keep(p) and not _in_any(p, avoid, 0.4)
                    and all(P.seg_dist(p, a, b) > gap for a, b in zip(glass, glass[1:]))):
                out.append(p)
            x += step
        y += step
    return out


def _lights(c, hangs):
    w = c.wall

    def panels(pts, z):
        for x, y in pts:
            kit.plate(w, x - 0.15, y - 0.15, x + 0.15, y + 0.15, z - 0.01, "light_panel",
                      flip=True)

    holes = (D.FRONT_HOLE, D.BACK_HOLE, D.R_STAIR_HOLE)
    avoid1 = holes + (KITCHEN_RECT,) + _keep_out(D.C1, hangs)
    pts1 = _grid(FLOOR1, D.C1, 3.0, lambda p: p[0] > D.CLIP_X + 1.0, avoid1,
                 SOFFIT_CHAIN, D.SOFFIT_W + shell.WALL + 0.5)
    pts1 += [(x, D.KITCHEN_Y + 0.9) for x in range(-24, 10, 3)]
    panels(pts1, D.C1)
    c.lights_from(pts1, D.C1, energy=120.0, step=3)
    kitchen2 = (D.NW_X[1], KITCHEN2_Y, STAIR_EX[1], D.Y_BACK)
    avoid2 = holes + (kitchen2, D.WOOD_CEIL) + _keep_out(D.C2, hangs)
    pts2 = _grid(D.FLOOR2, D.C2, 3.6, lambda p: True, avoid2, GLASS2_CHAIN, 1.2)
    pts2 += [(x, POCKET2[4] - 0.8) for x in range(-18, 4, 3)]
    panels(pts2, D.C2)
    c.lights_from(pts2, D.C2, energy=200.0, step=2)


def build(c, hangs=()):
    """躯体一式。hangs は吊り看板の吊り元 (x, y, z_ceil) の列（その真上の照明を抜く）。"""
    _outer(c)
    _glass2(c)
    _nw_block(c)
    _kitchen(c)
    _wc(c)
    _research(c)
    _floors(c)
    _ceilings(c)
    _ground(c)
    _lights(c, hangs)
