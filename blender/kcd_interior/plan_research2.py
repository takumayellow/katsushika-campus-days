"""第2研究棟（研究棟側のロビー）: ガラスの入口、石の床、パネルの壁、EV 2 基、ガラス越しの階段。

食堂（1〜2F）は plan_cafeteria。3F〜6F（事務室・研究室・会議室の中廊下）は plan_research2_upper が
同じ屋内に作り、EV の前の ev_research2_<階> から行く（Unity の ElevatorPanel）。
座標はロビーのローカル（入口の中心が x=0、+Y が奥）。入口は東面にある。host の座標（食堂の入口を
x=0 とした第2研究棟の局所座標。plan_research2_dims）からは plan_research2_frame で写す
（x = host の Y - 入口の Y、y = 東の面の X + 4.0 - host の X）。以下の寸法は host の座標で書く。
階段は EV ホールとガラスで隔てた見た目だけのもの（上れない）。南の半分を東へ上り、東の端の
踊り場で折り返して、北の半分を西へ上る（host の向き）。
"""

import math

from . import common, furn_research2 as FR, kit, shell, signs
from . import plan_research2_dims as D
from . import plan_research2_frame as M
from . import plan_research2_upper as upper
from . import spec as ispec

PANEL = "research2_lobby_panel"
STONE = "research2_lobby_stone"
BRICK = "research2_brick_core"
CONCRETE = "research2_wall_concrete"
GLASS = "research2_glass"
CEILING = "research2_ceiling_white"
STEEL = "research2_steel_dark"
BLUE = "sign_plate_blue"
CHARCOAL = "research2_wall_charcoal"

DOOR_W = 3.2
C1, C2, Z2 = D.C1, D.C2, D.Z2
SPINE = 0.16                        # 2 本の段の間の煉瓦の壁の厚み
LAND_W = 1.3                        # 東の踊り場の奥行き（X）
TOP_W = 1.1                         # 2F の着き場の奥行き（X）
STEPS = 14
FACE_W = M.ang(math.pi * 0.5)       # 正面 host の -X
FACE_E = M.ang(-math.pi * 0.5)      # 正面 host の +X

# ロビーの間取り（host の座標）
EV = (23.8, 30.4, 26.4, 37.1)                       # EV 2 基の箱（扉は +X を向く）
HALL_Y1 = 37.3                                      # EV ホールと階段室を分けるガラスの線
STAIR = (23.3, 37.3, 31.1, 41.05)                   # 階段室（煉瓦のコア）
HX_IN0 = ispec.LOBBY_X0 + 0.3                       # 食堂との間仕切りの東の内面（host の X）
HX_IN1 = ispec.LOBBY_X1 - 0.3                       # 東の壁の内面（入口のある壁）
HY_IN0 = ispec.LOBBY_Y0 + 0.3                       # 南のガラスの内面（host の Y）


def _box(mb, x0, y0, z0, x1, y1, z1, mat, nb=False, **kw):
    """host の座標の箱（z は床から）。"""
    lx0, ly0, lx1, ly1 = M.rect(x0, y0, x1, y1)
    (kit.box_nb if nb else kit.box)(mb, lx0, ly0, z0, lx1, ly1, z1, mat, **kw)


def _ring(mb, hole, z0, z1, mat):
    x0, y0, x1, y1 = hole
    ring = [(x0, y0), (x0, y1), (x1, y1), (x1, y0)]
    for k in range(4):
        kit.vplate(mb, ring[k], ring[(k + 1) % 4], z0, z1, mat)


def _shell(c):
    s = c.spec
    # 入口の壁（東）・北・間仕切りは壁、南だけガラス（外観のガラスの面）
    common.envelope(c, C1, STONE, door_w=DOOR_W, glass=GLASS, sill=0.0, header=C2 - C1,
                    seg=1.5, solid_edges=(0, 1, 2), wall=CONCRETE, z_top=C2, ceil=False,
                    floor_thick=0.22)
    ix0, iy0, ix1, iy1 = s.inner()
    # 天井は EV ホールまで。階段室は C2 まで抜け、上は蓋
    xg = M.pt(0.0, HALL_Y1)[0]
    shell.ceiling(c.wall, ix0, iy0, xg, iy1, C1, CEILING, grid=0.0)
    shell.ceiling(c.wall, xg, iy0, ix1, iy1, C2, CEILING, grid=0.0)
    return ix0, iy0, ix1, iy1


def _walls(c, mb):
    """間仕切り（食堂側）の内面のパネルと、EV の箱、EV ホールと階段室を隔てるガラス。"""
    _box(mb, HX_IN0, HY_IN0, 0.0, HX_IN0 + 0.03, 41.05, C1 - 0.3, PANEL, nb=True)
    ex0, ey0, ex1, ey1 = EV
    _box(mb, ex0, ey0, 0.0, ex1, ey1, C1, CONCRETE)
    for y in D.EV_Y:
        FR.ev_doors(mb, *M.pt(ex1, y), 0.0, FACE_E)
    gy = HALL_Y1
    _box(mb, HX_IN0, gy - 0.03, 0.1, HX_IN1, gy + 0.03, C2, GLASS)
    _box(mb, HX_IN0, gy - 0.05, 0.0, HX_IN1, gy + 0.05, 0.1, STEEL)
    _box(mb, HX_IN0, gy - 0.05, C1 - 0.12, HX_IN1, gy + 0.05, C1, STEEL)


def _stair(c, mb):
    x0, y0, x1, y1 = STAIR
    ym = (y0 + y1) * 0.5
    xa, xb = x0 + TOP_W, x1 - LAND_W
    zm = Z2 * 0.5
    FR.stair(mb, *M.rect(xa, y0 + 0.1, xb, ym - SPINE * 0.5), 0.0, zm, STEPS, M.STAIR_UP["+x"],
             side=None)
    _box(mb, xb, y0 + 0.1, zm - 0.22, x1, y1, zm, FR.TREAD, bottom=FR.SOFFIT)
    FR.stair(mb, *M.rect(xa, ym + SPINE * 0.5, xb, y1), zm, Z2, STEPS, M.STAIR_UP["-x"],
             side=None)
    _box(mb, x0, ym + SPINE * 0.5, Z2 - D.SLAB, xa, y1, Z2, FR.TREAD, bottom=FR.SOFFIT)
    _box(mb, xa, ym - SPINE * 0.5, 0.0, xb, ym + SPINE * 0.5, Z2 + 1.0, BRICK)
    FR.glass_rail(mb, (M.pt(x0, ym), M.pt(xa, ym)), Z2, h=1.0)


def _signs(c, mb):
    signs.wall(c, mb, *M.pt(HX_IN1, 28.0), 1.6, FACE_W,
               ja="6F 会議室\n4F・5F 研究室\n3F 事務室\n1F・2F 食堂",
               en="6F Meeting / 4-5F Labs\n3F Office / 1-2F Cafeteria",
               w=1.4, h=1.6, plate=BLUE, frame="metal_white", out=0.04)
    signs.wall(c, mb, *M.pt(HX_IN0 + 0.12, 21.0), 2.3, FACE_E, ja="第2研究棟",
               en="Research Labs Building No.2", w=1.6, h=0.5, plate=CHARCOAL,
               frame="metal_white", out=0.03)


def build(c):
    s = c.spec
    ix0, iy0, ix1, iy1 = _shell(c)
    common.entry_kit(c, C1, door_w=DOOR_W, spawn_depth=1.6)
    mb = c.furn("lobby")
    _walls(c, mb)
    _stair(c, mb)
    _signs(c, mb)
    c.poi("lobby", 0.0, s.y_face + 6.0, 0.0)
    c.npc(*M.pt(27.5, 30.0), 0.0)
    c.light(0.0, s.y_face + 6.0, 3.0, 220.0, 1.6)
    c.light(*M.pt(29.0, 32.0), 3.0, 220.0, 1.6)
    ev_mid = M.pt(26.4, sum(D.EV_Y) * 0.5)
    c.cam("", (0.0, s.y_face + 1.4, 1.65), (ev_mid[0], ev_mid[1], 1.6), 18.0)
    ex, ey = upper.ev_point()
    c._put("ev_%s_1F" % s.id, *M.pt(ex, ey), 0.0)
    c._put("ev_%s_1F_f" % s.id, *M.pt(ex + 1.0, ey), 0.0)
    upper.build(c)
    cx, cy = (ix0 + ix1) * 0.5, (iy0 + iy1) * 0.5
    c.cam("top", (cx, cy, 40.0), (cx, cy + 0.5, 0.0), 24.0)
    c.note("研究棟側のロビー（石の床・パネルの壁・EV 2 基・ガラス越しの階段）と、EV で行く 3F〜6F の中廊下")
