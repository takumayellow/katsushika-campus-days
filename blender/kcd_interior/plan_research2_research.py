"""第2研究棟の研究棟側: ロビーのパネルの壁と石の床、EV 2 基の扉（1F・2F）、ガラスの奥の階段。

階段は EV ホールとガラスで隔てた見た目だけのもの（上れない）。南の半分を東へ上り、
東の端の踊り場で折り返して、北の半分を西へ上る。2F は西の端に着く。
"""

from . import furn_research2 as FR
from . import kit
from . import plan_research2_dims as D

PANEL = "research2_lobby_panel"
STONE = "research2_floor_1f_dark"
BRICK = "research2_brick_core"
SPINE = 0.16                 # 2 本の段の間の煉瓦の壁の厚み
LAND_W = 1.3                 # 東の踊り場の奥行き（X）
TOP_W = 1.1                  # 2F の着き場の奥行き（X）
STEPS = 14


def _lobby(mb):
    """間仕切りの東の面のパネル（扉口を除く）と、ロビー・EV ホールの石の床。"""
    x = D.PART_X + 0.09
    _x0, y0, _x1, y1 = D.LOBBY
    d0, d1 = D.PART_DOOR
    for a, b in ((y0, d0), (d1, y1)):
        kit.box_nb(mb, x, a, D.Z1, x + 0.03, b, D.C1 - 0.3, PANEL)
    kit.box_nb(mb, x, d0, 2.4, x + 0.03, d1, D.C1 - 0.3, PANEL)
    hx0, _hy0, hx1, hy1 = D.EV_HALL
    z = D.Z1 + 0.004
    kit.plate(mb, D.PART_X + 0.08, y0, hx1, D.EV[1], z, STONE)
    kit.plate(mb, hx0, D.EV[1], hx1, hy1, z, STONE)


def _ev(mb):
    for z in (D.Z1, D.Z2):
        for y in D.EV_Y:
            FR.ev_doors(mb, D.EV[2], y, z, D.FACE_E)


def stair_x(mb, x0, y0, x1, y1, z0, z1, n, up, tread=FR.TREAD, riser=FR.SOFFIT):
    """X 向きの直階段（up は '+x' '-x'）。範囲は踏面の外形。段裏に白い斜めの板。"""
    run = (x1 - x0) / n
    rise = (z1 - z0) / n
    sgn = 1 if up == "+x" else -1
    start = x0 if sgn > 0 else x1
    end = start + sgn * (x1 - x0)
    for i in range(n):
        p = start + sgn * run * i
        q = p + sgn * run
        zt = z0 + rise * (i + 1)
        kit.box(mb, min(p, q), y0, zt - 0.05, max(p, q), y1, zt, tread)
        kit.box_nb(mb, min(p, p + sgn * 0.03), y0, zt - rise, max(p, p + sgn * 0.03), y1,
                   zt - 0.05, riser)
    kit.thick_quad(mb, (start, y0, z0 - 0.3), (start, y1, z0 - 0.3),
                   (end, y1, z1 - 0.3), (end, y0, z1 - 0.3), 0.03, riser)


def _stair(mb):
    x0, y0, x1, y1 = D.R_STAIR
    ym = (y0 + y1) * 0.5
    xa, xb = x0 + TOP_W, x1 - LAND_W
    zm = (D.Z1 + D.Z2) * 0.5
    # 南の半分（東へ上る）・東の踊り場・北の半分（西へ上る）・2F の着き場
    stair_x(mb, xa, y0 + 0.1, xb, ym - SPINE * 0.5, D.Z1, zm, STEPS, "+x")
    kit.box(mb, xb, y0 + 0.1, zm - 0.22, x1, y1, zm, FR.TREAD, bottom=FR.SOFFIT)
    stair_x(mb, xa, ym + SPINE * 0.5, xb, y1, zm, D.Z2, STEPS, "-x")
    kit.box(mb, x0, ym + SPINE * 0.5, D.Z2 - D.SLAB, xa, y1, D.Z2, FR.TREAD, bottom=FR.SOFFIT)
    # 2 本の間の煉瓦の壁（手すりを兼ねる）と、2F の着き場の南の手すり
    kit.box(mb, xa, ym - SPINE * 0.5, D.Z1, xb, ym + SPINE * 0.5, D.Z2 + 1.0, BRICK)
    FR.glass_rail(mb, ((x0, ym), (xa, ym)), D.Z2, h=1.0)


def build(c, M):
    mb = M["research"]
    _lobby(mb)
    _ev(mb)
    _stair(mb)
