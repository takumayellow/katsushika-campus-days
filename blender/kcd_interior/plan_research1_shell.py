"""第1研究棟の躯体: 外周壁・床・廊下の間仕切り・室の扉・天井・廊下の照明。

外周は FOOT の各辺に壁を立てる（厚みは a -> b の左手＝室内側）。閉じた室は中を作らず、
廊下に面した扉（白い扉板・視認窓・脇の紺のパネル・赤いコンセント）だけを付ける。
入れる室（荒井研究室の実験室・居室、産学試作開発室）だけ間仕切りに開口をあけて引き戸を開けておく。
"""

import math

from . import kit, shell
from . import plan_research1_dims as D
from .kit import T

WHITE = "research1_wall_white"
CONC = "research1_concrete"
GLASS = "research1_glass"
BLACK = "research1_frame_black"
NAVY = "research1_navy_panel"
CAP_UP = 0.01        # 壁の天面に貼る蓋の高さ
DOOR_W = 1.1
HALF_PART = shell.PART * 0.5
PI = math.pi
HALF_PI = math.pi * 0.5


def _normal(a, b):
    dx, dy = b[0] - a[0], b[1] - a[1]
    L = math.hypot(dx, dy)
    return (-dy / L, dx / L)


def cap(mb, a, b, z, off0, off1, mat=CONC):
    """a -> b の線から左手へ off0〜off1 の帯に、高さ z + CAP_UP の下向きの蓋を貼る。"""
    n = _normal(a, b)
    pts = [(a[0] + n[0] * off0, a[1] + n[1] * off0), (b[0] + n[0] * off0, b[1] + n[1] * off0),
           (b[0] + n[0] * off1, b[1] + n[1] * off1), (a[0] + n[0] * off1, a[1] + n[1] * off1)]
    zc = z + CAP_UP
    mb.add_face([(p[0], p[1], zc) for p in reversed(pts)], mat)


def wall(c, line, z0, z1, mat=WHITE, glass=None, sill=0.0, header=0.0, gaps=(),
         gap_top=None, top=True, seg=3.0):
    """外周壁の 1 帯。top=True なら上端に蓋を貼る。"""
    a, b = line
    shell.outer_wall(c.wall, a, b, z0, z1, wall=mat, glass=glass, sill=sill, header=header,
                     seg=seg, gaps=gaps, gap_top=gap_top)
    if top:
        cap(c.wall, a, b, z1, 0.0, shell.WALL)


def part(c, a, b, z0=D.Z1, z1=D.Z_WALL, mat=WHITE, gaps=()):
    """間仕切り（線の中心に厚み）と天面の蓋。"""
    shell.partition(c.wall, a, b, z0, z1, mat, thick=shell.PART, gaps=gaps)
    cap(c.wall, a, b, z1, -HALF_PART, HALF_PART)


def _gap(a, x_or_y, w=DOOR_W):
    """a からの距離で、中心 x_or_y（線に沿った座標）の扉の開口。"""
    return (x_or_y - w * 0.5 - a, x_or_y + w * 0.5 - a)


# ---- 外周 ----
def _curtain(c, a, b):
    """吹き抜けの南のカーテンウォール（黒い框・Z 0〜9）と風除室の外側の自動扉。"""
    x0 = a[0]
    gap = (D.DOOR[0] - x0, D.DOOR[1] - x0)
    shell.outer_wall(c.wall, a, b, D.Z1, D.Z_SKY, wall=BLACK, glass=GLASS, sill=0.0,
                     header=0.0, seg=1e9, gaps=[gap], gap_top=3.2)
    cap(c.wall, a, b, D.Z_SKY, 0.0, shell.WALL)
    y = a[1] + 0.06
    n = 10
    for k in range(n + 1):
        x = a[0] + (b[0] - a[0]) * k / n
        if D.DOOR[0] - 0.1 < x < D.DOOR[1] + 0.1:
            continue
        kit.box(c.wall, x - 0.05, y, D.Z1, x + 0.05, y + 0.22, D.Z_SKY, BLACK)
    for z in (3.2, 6.1):
        kit.box(c.wall, a[0], y, z - 0.05, b[0], y + 0.22, z + 0.05, BLACK)
    shell.glass_entrance(c.wall, D.DOOR[0], D.DOOR[1], D.Z1, 3.2, D.Y_DOOR + 0.15,
                         frame=BLACK, glass=GLASS, seg=1.6, door_h=2.4)
    c.door_gap = D.DOOR


def _outer(c):
    n = len(D.FOOT)
    for i in range(n):
        a, b = D.FOOT[i], D.FOOT[(i + 1) % n]
        if i == D.ATRIUM_EDGE:
            _curtain(c, a, b)
        elif i in D.WINDOW_EDGES:
            wall(c, (a, b), D.Z1, D.Z_WALL, glass=GLASS, sill=0.9, header=1.5, seg=3.6)
        else:
            wall(c, (a, b), D.Z1, D.Z_WALL)


# ---- 床 ----
def _floors(c):
    fl = c.floor
    for x0, x1, y0, y1 in D.SLABS:
        shell.floor(fl, x0, y0, x1, y1, D.Z1, "research1_floor_corridor", side_mat=CONC)
    navy = "research1_floor_navy"
    y_in = D.Y_DOOR + shell.WALL
    kit.plate(fl, -D.X_A, y_in, D.X_A, D.Y_NC1, 0.01, navy)
    kit.plate(fl, D.W_HALL[0], D.Y_SC1, -D.X_A, D.Y_NC0, 0.01, navy)
    kit.plate(fl, D.X_A, D.Y_SC1, D.E_HALL[2], D.Y_NC0, 0.01, navy)
    # 入口ホールの細長いタイル（X -4〜+4 の帯。濃淡を交互に）
    w = 0.5
    for k in range(16):
        x = -4.0 + w * k
        mat = "research1_tile_dark" if k % 2 else "research1_tile_light"
        kit.plate(fl, x, y_in, x + w, 22.0, 0.02, mat)
    # 室の床
    vinyl, carpet = "research1_vinyl_lightgrey", "research1_carpet_grey"
    kit.plate(fl, D.LAB_A[0], D.Y_SC1, D.LAB_A[1], D.Y_NC0, 0.01, vinyl)
    kit.plate(fl, D.LAB_B[0], D.Y_S, D.LAB_B[1], D.Y_SC0, 0.01, vinyl)
    kit.plate(fl, D.PROF[0], D.Y_SW, D.PROF[1], D.Y_SC0, 0.01, carpet)
    kit.plate(fl, -D.X_A, D.Y_NC1, D.X_A, D.Y_N, 0.01, carpet)
    # 廊下の壁際の青い帯（南の廊下は南側、北の廊下は北側）
    strip = "research1_floor_strip"
    yb = D.Y_SC0 + HALF_PART
    yn = D.Y_NC1 - HALF_PART
    for x0, x1 in ((D.X_W_IN, D.LOUNGE[0]), (D.REST[1], D.X_E_IN)):
        kit.plate(fl, x0, yb, x1, yb + 0.25, 0.015, strip)
    for x0, x1 in ((D.X_NW_IN, -D.X_A), (D.X_A, D.X_NE_IN)):
        kit.plate(fl, x0, yn - 0.25, x1, yn, 0.015, strip)


# ---- 間仕切り ----
def _corridors(c):
    sc0, sc1, nc0, nc1 = D.Y_SC0, D.Y_SC1, D.Y_NC0, D.Y_NC1
    # 南の廊下の南側（白い壁）。居室と産学試作開発室の引き戸
    a = D.X_W_IN
    part(c, (a, sc0), (D.LOUNGE[0], sc0), gaps=[_gap(a, -30.05)])
    a = D.REST[1]
    part(c, (a, sc0), (D.X_E_IN, sc0), gaps=[_gap(a, 51.75)])
    # 南の廊下の北側（打放し）。実験室の引き戸。EV ホールの前は開ける
    a = D.X_W_IN
    part(c, (a, sc1), (D.W_SHAFTS[0][2], sc1), mat=CONC, gaps=[_gap(a, -28.85)])
    part(c, (D.X_A, sc1), (D.E_SHAFT[2] + 0.2, sc1), mat=CONC)
    part(c, (D.E_HALL[2], sc1), (D.X_E_IN, sc1), mat=CONC)
    # 北の廊下（南側は打放し、北側は白い壁）
    part(c, (D.X_WEND, nc0), (-D.X_A, nc0), mat=CONC)
    part(c, (D.X_A, nc0), (D.X_NE_IN, nc0), mat=CONC)
    part(c, (D.X_NW_IN, nc1), (-D.X_A, nc1))
    part(c, (D.X_A, nc1), (D.X_NE_IN, nc1))
    # 端の EV・階段の区画との境
    part(c, (D.X_WEND, sc1), (D.X_WEND, 31.24), mat=CONC)
    part(c, (D.X_EEND, sc1), (D.X_EEND, 29.95), mat=CONC)


def _rooms(c):
    """入れる室の側壁と、EV ホール・事務室の側壁。"""
    for x in D.PROF:
        part(c, (x, D.Y_SW), (x, D.Y_SC0))
    for x in D.LAB_B:
        part(c, (x, D.Y_S), (x, D.Y_SC0))
    for x in D.LAB_A:
        part(c, (x, D.Y_SC1), (x, D.Y_NC0))
    part(c, (D.W_HALL[0], D.Y_SC1), (D.W_HALL[0], D.Y_NC0), mat=NAVY)
    part(c, (D.E_HALL[2], D.Y_SC1), (D.E_HALL[2], D.Y_NC0), mat=NAVY)
    for x in (-D.X_A, D.X_A):
        part(c, (x, D.Y_NC1), (x, D.Y_N))


# ---- 室の扉 ----
def room_door(mb, x, y, ang, outlet=True, z=0.0):
    """閉じた室の扉。白い扉板・視認窓（不透明）・脇の紺のパネル・赤いコンセント。

    (x, y) は廊下側の壁の面、ang は廊下を向く向き（kit.T の正面 +Y）、z は床の高さ。
    """
    t = T(x, y, z, ang)
    t.box(mb, -0.55, 0.0, 0.0, 0.55, 0.05, 2.15, "research1_sus_hairline")
    t.box(mb, -0.48, 0.05, 0.02, 0.48, 0.08, 2.10, "research1_door_white")
    t.box(mb, -0.12, 0.08, 1.15, 0.12, 0.09, 1.75, BLACK)
    t.box(mb, 0.70, 0.0, 0.0, 1.20, 0.03, 2.15, NAVY)
    if outlet:
        t.box(mb, -0.95, 0.0, 0.30, -0.80, 0.05, 0.42, "research1_red_outlet")


def ev_door(mb, x, y, ang, z=0.0):
    """閉じた EV の扉（クリーム白の 2 枚・ステンレスの枠・呼びボタン）。z は床の高さ。"""
    t = T(x, y, z, ang)
    t.box(mb, -0.75, 0.0, 0.0, 0.75, 0.06, 2.45, "research1_sus_hairline")
    t.box(mb, -0.55, 0.06, 0.0, -0.01, 0.09, 2.15, "research1_ev_door")
    t.box(mb, 0.01, 0.06, 0.0, 0.55, 0.09, 2.15, "research1_ev_door")
    t.box(mb, 0.85, 0.0, 1.0, 0.97, 0.05, 1.3, "research1_sus_hairline")


# 閉じた室の扉（x, 廊下の面の y, 向き）。spec §1-3 の室の並び
_SC0F = D.Y_SC0 + HALF_PART
_SC1F = D.Y_SC1 - HALF_PART
_NC0F = D.Y_NC0 + HALF_PART
_NC1F = D.Y_NC1 - HALF_PART
DOORS = (
    [(x, _SC0F, 0.0) for x in (-60.0, -50.0, -40.0)]
    + [(15.8 + 3.85 * (k + 0.5), _SC0F, 0.0) for k in range(8)]
    + [(x, _SC0F, 0.0) for x in (58.0, 61.4, 64.8)]
    + [(x, _SC1F, PI) for x in (-59.6, -55.0, -52.6, -50.2, -39.6)]
    + [(x, _SC1F, PI) for x in (19.4, 25.0, 27.4, 29.8, 41.4)]
    + [(x, _NC0F, 0.0) for x in (-60.0, -40.0, 20.0, 40.0)]
    + [(x, _NC1F, PI) for x in (-50.0, -30.0, 35.0)]
)
# 閉じた EV の扉（西端・東コア 2・東端）
EV_DOORS = (
    (-67.0, _SC1F, PI), (59.85, _SC1F, PI), (62.15, _SC1F, PI),
    (D.X_E_IN, 18.3, HALF_PI),
)
# 廊下の突き当たりの階段の扉（西端）
END_DOORS = ((D.X_W_IN, 18.3, -HALF_PI),)


def _doors(c):
    mb = c.wall
    for x, y, ang in (*DOORS, *END_DOORS):
        room_door(mb, x, y, ang)
    for x, y, ang in EV_DOORS:
        ev_door(mb, x, y, ang)
    # 入れる室の引き戸（開けておく）
    for x, y, ang in ((-30.05, D.Y_SC0, 0.0), (51.75, D.Y_SC0, 0.0), (-28.85, D.Y_SC1, PI)):
        shell.door(mb, x, y, ang, w=DOOR_W, h=2.1, leaf="research1_door_white",
                   frame="research1_sus_hairline")


# ---- 天井と照明 ----
CORRIDOR_CEILS = (
    (D.X_W_IN, D.Y_SC0, -D.X_A, D.Y_SC1), (D.X_A, D.Y_SC0, D.X_E_IN, D.Y_SC1),
    (D.X_NW_IN, D.Y_NC0, -D.X_A, D.Y_NC1), (D.X_A, D.Y_NC0, D.X_NE_IN, D.Y_NC1),
    (D.LOUNGE[0], D.Y_SW, -15.0, D.Y_SC0), (-15.0, 11.6, -8.23, D.Y_SC0),
    (8.16, 11.62, D.REST[1], D.Y_SC0),
)


def _ceilings(c):
    w = c.wall
    for x0, y0, x1, y1 in CORRIDOR_CEILS:
        shell.ceiling(w, x0, y0, x1, y1, D.C_COR, "research1_ceiling")
    shell.ceiling(w, D.PROF[0], D.Y_SW, D.PROF[1], D.Y_SC0, D.C_ROOM, "research1_ceiling")
    shell.ceiling(w, -D.X_A, D.Y_NC1, D.X_A, D.Y_N, D.C_OFFICE, "research1_ceiling")
    slab = "research1_slab_exposed"
    shell.ceiling(w, D.LAB_A[0], D.Y_SC1, D.LAB_A[1], D.Y_NC0, D.C_SLAB, slab)
    shell.ceiling(w, D.LAB_B[0], D.Y_S, D.LAB_B[1], D.Y_SC0, D.C_SLAB, slab)


def _lights(c):
    """廊下は中心から南へ 0.4 m のスリット照明 1 本。室は直管の列。"""
    w = c.wall
    lamp = "research1_light_panel"
    for x0, y0, x1, y1 in CORRIDOR_CEILS[:4]:
        y = (y0 + y1) * 0.5 - 0.4
        shell.light_strip(w, x0 + 0.3, y, x1 - 0.3, y, D.C_COR, mat=lamp, w=0.12, drop=0.03)
        n = max(1, int((x1 - x0) / 9.0))
        c.lights_from([(x0 + (x1 - x0) * (k + 0.5) / n, y) for k in range(n)], D.C_COR,
                      energy=90.0)
    rooms = ((D.PROF[0], D.Y_SW, D.PROF[1], D.Y_SC0, D.C_ROOM),
             (D.LAB_A[0], D.Y_SC1, D.LAB_A[1], D.Y_NC0, D.C_SLAB),
             (D.LAB_B[0], D.Y_S, D.LAB_B[1], D.Y_SC0, D.C_SLAB),
             (-D.X_A, D.Y_NC1, D.X_A, D.Y_N, D.C_OFFICE))
    for x0, y0, x1, y1, z in rooms:
        pts = shell.ceiling_lights(w, x0, y0, x1, y1, z, sx=3.0, sy=2.6, w=1.25, l=0.16,
                                   mat=lamp, drop=0.05 if z < 4.0 else 0.6)
        c.lights_from(pts, z - 0.1, energy=120.0, step=3)
    lounge = shell.ceiling_lights(w, D.LOUNGE[0], D.Y_SW, -15.0, D.Y_SC0, D.C_COR, sx=3.6,
                                  sy=3.0, w=0.3, l=0.3, mat=lamp, drop=0.02)
    c.lights_from(lounge, D.C_COR, energy=110.0, step=2)


def build(c):
    _outer(c)
    _floors(c)
    _corridors(c)
    _rooms(c)
    _doors(c)
    _ceilings(c)
    _lights(c)
