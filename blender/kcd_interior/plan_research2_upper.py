"""第2研究棟の上の階（3F〜6F）: 中廊下、EV ホール、閉じた研究室・事務室・会議室の扉。

ロビーの EV から各階へ行く（Unity の ElevatorPanel）。扉の奥の部屋・WC・階段は作らない。
寸法は plan_research2_dims の UPPER_*（host の座標）。ロビーのローカルへは plan_research2_frame で写す。
床・躯体は階ごと・X の区画ごとに別の物（Ctx.part）にして、1 つの物に届く点光源を 4 つまでに抑える。

Empty:
    ev_research2_<階>      EV を降りた所（床の高さ）。<階> は 3F〜6F（1F はロビーが置く）
    ev_research2_<階>_f    降りた所から 1 m 正面（降りたときに向く向き）
    lamp_research2_<階>_<n> 廊下の天井の点光源（Unity は今いる階の分だけ点ける）
"""

import math

from . import furn_research2 as FR
from . import kit, shell, signs
from . import plan_research2_dims as D
from . import plan_research2_frame as M

FLOOR = "floor_tile_grey"
WALL = "wall_white"
CEIL = "research2_ceiling_white"
PILASTER = "research2_wall_concrete"
DOOR = "research2_door_steel"
FRAME = "metal_dark"
NAME_PLATE = "research2_panel_grey"
BLUE = "sign_plate_blue"

DOOR_W, DOOR_H = 0.9, 2.1
WIDE_ROOM = 6.0          # これより広い部屋は扉を両端に 2 つ
DOOR_IN = 1.2            # 広い部屋の扉の中心を仕切りから離す距離
PIL_W, PIL_D = 0.3, 0.08
LAMP_DZ = 2.6            # 床から点光源まで
PANEL_STEP = 3.0         # 天井の照明パネルの間隔（廊下の X）
PANEL_W, PANEL_L = 0.6, 1.2
EV_OUT = 1.4             # EV の扉の面から降りた所まで
FACE_N = 0.0             # 正面 +Y
FACE_S = math.pi         # 正面 -Y
FACE_W = math.pi * 0.5   # 正面 -X
FACE_E = -math.pi * 0.5  # 正面 +X

W = D.UPPER_WALL
CX0, CY0, CX1, CY1 = D.UPPER_CORR
HX0, HY0, HX1, HY1 = D.UPPER_EV_HALL
OX0, OX1 = CX0 - W, CX1 + W            # 躯体の外面の X
OY0, OY1 = CY0 - W, HY1 + W            # 躯体の外面の Y（南の壁・EV ホールの北の壁）
EDGES = (OX0,) + D.UPPER_SPLIT_X + (OX1,)


def ev_point():
    """EV を降りた所 (x, y)（host の座標）。"""
    return HX0 + EV_OUT, (D.EV_Y[0] + D.EV_Y[1]) * 0.5


def zone():
    """閉じの検査（closure）に渡す上の階の外周（ロビーのローカル）。北西を欠いた L 字。"""
    z_lo = D.UPPER_FLOORS[0][1]
    z_hi = D.UPPER_FLOORS[-1][1]
    x0, y0, x1, y1 = M.rect(OX0, OY0, OX1, OY1)
    notch = M.rect(OX0, CY1 + W, HX0 - W, OY1)
    return {"x0": round(x0, 3), "x1": round(x1, 3), "y_face": round(y0, 3),
            "y_back": round(y1, 3),
            "notch": [round(v, 3) for v in notch],
            "z_min": round(z_lo - 0.5, 3), "z_max": round(z_hi + 0.5, 3)}


class _Floor:
    """1 つの階の区画ごとの MeshBuilder と、host の座標で描く箱。"""

    def __init__(self, c, name, z):
        self.c, self.name, self.z = c, name, z
        key = name.lower()
        self.floors = [c.part("floor", "%s_%d" % (key, k)) for k in range(len(EDGES) - 1)]
        self.walls = [c.part("wall", "%s_%d" % (key, k)) for k in range(len(EDGES) - 1)]

    def seg(self, x):
        for k in range(len(EDGES) - 1):
            if x < EDGES[k + 1]:
                return k
        return len(EDGES) - 2

    def wall_at(self, x):
        return self.walls[self.seg(x)]

    def box(self, x0, y0, z0, x1, y1, z1, mat, nb=False):
        """host の座標の箱。z は床からの高さ。区画の境をまたぐ箱は区画ごとに切る。"""
        k0, k1 = self.seg(min(x0, x1) + 1e-6), self.seg(max(x0, x1) - 1e-6)
        fn = kit.box_nb if nb else kit.box
        for k in range(k0, k1 + 1):
            a = max(min(x0, x1), EDGES[k]) if k > k0 else min(x0, x1)
            b = min(max(x0, x1), EDGES[k + 1]) if k < k1 else max(x0, x1)
            lx0, ly0, lx1, ly1 = M.rect(a, min(y0, y1), b, max(y0, y1))
            fn(self.walls[k], lx0, ly0, self.z + z0, lx1, ly1, self.z + z1, mat)

    def plate(self, mb, x0, y0, x1, y1, dz, mat, **kw):
        """host の矩形の水平な板（高さは階の床から dz）。"""
        kit.plate(mb, *M.rect(x0, y0, x1, y1), self.z + dz, mat, **kw)

    def t(self, x, y, dz, ang):
        return kit.T(*M.pt(x, y), self.z + dz, M.ang(ang))


def _slab_and_ceiling(f):
    """床（廊下 + EV ホール）と天井、天井の照明パネル。区画ごとに切る。"""
    h = D.UPPER_CEIL_H
    for k in range(len(EDGES) - 1):
        a, b = EDGES[k], EDGES[k + 1]
        shell.floor(f.floors[k], *M.rect(a, OY0, b, CY1 + W), f.z, FLOOR,
                    thickness=D.SLAB, side_mat=CEIL)
        shell.ceiling(f.walls[k], *M.rect(max(a, CX0), CY0, min(b, CX1), CY1), f.z + h, CEIL)
    # EV ホール（廊下の北。区画は東の端）
    k = len(EDGES) - 2
    shell.floor(f.floors[k], *M.rect(HX0 - W, CY1 + W, OX1, OY1), f.z, FLOOR,
                thickness=D.SLAB, side_mat=CEIL)
    shell.ceiling(f.walls[k], *M.rect(HX0, CY1, HX1, HY1), f.z + h, CEIL)
    ym = (CY0 + CY1) * 0.5
    x = CX0 + PANEL_STEP * 0.5
    while x < CX1 - PANEL_L * 0.5:
        f.plate(f.wall_at(x), x - PANEL_L * 0.5, ym - PANEL_W * 0.5,
                x + PANEL_L * 0.5, ym + PANEL_W * 0.5, h - 0.005, "light_panel", flip=True)
        x += PANEL_STEP
    hx_m, hy_m = (HX0 + HX1) * 0.5, (HY0 + HY1) * 0.5
    f.plate(f.wall_at(hx_m), hx_m - PANEL_W * 0.5, hy_m - PANEL_L * 0.5,
            hx_m + PANEL_W * 0.5, hy_m + PANEL_L * 0.5, h - 0.005, "light_panel", flip=True)


def _shell_walls(f):
    """外周の壁（廊下の南北・西の端・東の端、EV ホールの西・北）。"""
    h = D.UPPER_CEIL_H
    f.box(OX0, OY0, 0.0, OX1, CY0, h, WALL)                 # 南
    f.box(OX0, CY1, 0.0, HX0 - W, CY1 + W, h, WALL)          # 北（EV ホールの西の壁まで）
    f.box(OX0, CY0, 0.0, CX0, CY1, h, WALL)                  # 西の端
    f.box(CX1, CY0, 0.0, OX1, OY1, h, WALL)                  # 東の端（EV ホールの東まで）
    f.box(HX0 - W, CY1, 0.0, HX0, OY1, h, WALL)              # EV ホールの西（EV の扉の壁）
    f.box(HX0, HY1, 0.0, CX1, OY1, h, WALL)                  # EV ホールの北（階段室の扉）


def _door(f, x, y, ang, plate=True):
    """壁の面 (x, y) に付いた閉じた鋼の扉。ang は扉の正面（廊下側）。"""
    mb = f.wall_at(x)
    t = f.t(x, y, 0.0, ang)
    hw = DOOR_W * 0.5
    # 枠は壁に少し埋め、扉の板は枠の面から浮かせる（面が重なると閉じの検査のレイが裏の面を拾う）
    t.box(mb, -hw - 0.06, -0.02, 0.0, hw + 0.06, 0.03, DOOR_H + 0.06, FRAME)
    t.box_nb(mb, -hw, 0.035, 0.0, hw, 0.065, DOOR_H, DOOR)
    t.box(mb, hw - 0.20, 0.065, 0.95, hw - 0.12, 0.10, 1.15, "stainless")
    if plate:
        # 部屋の名札（名前は書かない。研究室名・教員名は出さない）
        t.box(mb, hw + 0.18, -0.01, 1.45, hw + 0.48, 0.02, 1.60, NAME_PLATE)


def _rooms(f, xs, y, ang, skip=()):
    """仕切りの X の列 xs で区切った部屋ごとに扉を付ける。"""
    for a, b in zip(xs, xs[1:]):
        if (a, b) in skip or b - a < 2.0:
            continue
        if b - a < WIDE_ROOM:
            _door(f, (a + b) * 0.5, y, ang)
        else:
            _door(f, a + DOOR_IN, y, ang)
            _door(f, b - DOOR_IN, y, ang)


def _pilasters(f, xs, y, side):
    """仕切りの位置の柱型（廊下へ PIL_D 出る）。side は廊下の向き（+1 = +Y）。"""
    for x in xs:
        f.box(x - PIL_W * 0.5, y, 0.0, x + PIL_W * 0.5, y + PIL_D * side, D.UPPER_CEIL_H,
              PILASTER, nb=True)


def _partitions(f):
    north = (CX0,) + D.NORTH_PARTS + (HX0,)
    south = (CX0,) + D.south_parts(f.name) + (CX1,)
    # 北の列: 西の端は階段室と WC（WC の扉だけ付ける）、東の端は EV の昇降路の脇
    _rooms(f, north, CY1, FACE_S, skip=((north[0], north[1]), (north[-2], north[-1])))
    _rooms(f, south, CY0, FACE_N)
    _pilasters(f, D.NORTH_PARTS, CY1, -1)
    _pilasters(f, D.south_parts(f.name), CY0, +1)
    _door(f, D.UPPER_WC_X, CY1, FACE_S, plate=False)
    _door(f, CX0, D.UPPER_WEST_STAIR_Y, FACE_E, plate=False)
    _door(f, D.UPPER_STAIR_DOOR_X, HY1, FACE_S, plate=False)


def _equipment(f):
    """EV の扉、階の看板、避難誘導灯、消火器、WC の看板。"""
    c = f.c
    mb = f.wall_at(HX1)
    for y in D.EV_Y:
        FR.ev_doors(mb, *M.pt(HX0, y), f.z, M.ang(FACE_E))
    ja, en = D.UPPER_USE[f.name]
    ym = (D.EV_Y[0] + D.EV_Y[1]) * 0.5
    signs.wall(c, mb, *M.pt(HX1, ym), f.z + 1.7, M.ang(FACE_W), ja=ja, en=en, w=1.4, h=0.5,
               plate=BLUE, frame="metal_white", out=0.04, anchor=False)
    wc = f.wall_at(D.UPPER_WC_X)
    signs.wall(c, wc, *M.pt(D.UPPER_WC_X + 0.95, CY1), f.z + 1.7, M.ang(FACE_S), ja="トイレ",
               en="Toilet", w=0.6, h=0.3, plate=BLUE, frame="metal_white", out=0.03,
               anchor=False)
    top = D.UPPER_CEIL_H - 0.26
    shell.exit_sign(f.wall_at(CX0), *M.pt(CX0 + 0.3, D.UPPER_WEST_STAIR_Y), f.z + top,
                    M.ang(FACE_E))
    shell.exit_sign(mb, *M.pt(D.UPPER_STAIR_DOOR_X, HY1 - 0.3), f.z + top, M.ang(FACE_S))
    shell.fire_extinguisher(f.wall_at(0.0), *M.pt(0.0, CY0 + 0.2), f.z, M.ang(FACE_N))


def _empties(f):
    c = f.c
    ex, ey = ev_point()
    c._put("ev_%s_%s" % (c.spec.id, f.name), *M.pt(ex, ey), f.z)
    c._put("ev_%s_%s_f" % (c.spec.id, f.name), *M.pt(ex + 1.0, ey), f.z)
    ym = (CY0 + CY1) * 0.5
    pts = [(x, ym) for x in D.UPPER_LAMP_X] + [((HX0 + HX1) * 0.5, (HY0 + HY1) * 0.5)]
    for n, (x, y) in enumerate(pts, 1):
        c._put("lamp_%s_%s_%d" % (c.spec.id, f.name, n), *M.pt(x, y), f.z + LAMP_DZ)
    c.lights_from([M.pt(x, y) for x, y in pts], f.z + D.UPPER_CEIL_H)


def build(c):
    """3F〜6F を作る。寸法は host の座標で、ロビーのローカルへは plan_research2_frame で写す。"""
    for name, z in D.UPPER_FLOORS:
        f = _Floor(c, name, z)
        _slab_and_ceiling(f)
        _shell_walls(f)
        _partitions(f)
        _equipment(f)
        _empties(f)
    c.closure_zones.append(zone())
    z4 = D.UPPER_FLOORS[1][1]
    ym = (CY0 + CY1) * 0.5
    c.cam("4f", (*M.pt(HX0 + 1.0, ym), z4 + 1.65), (*M.pt(-10.0, ym), z4 + 1.6), 18.0)
