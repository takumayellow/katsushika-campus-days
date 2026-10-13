"""講義棟 2F〜7F の西の階段（西ロビーの西の張り出し。spec §1-5・§1-12）。

外形の西の張り出し（D.WEST_STAIR）を階段室にし、各階の西ロビーから壁の口で入れるようにする。
折り返し階段で、西ロビーから南の半分を西へ上り（12 段）、西の端の踊り場で折り返して北の半分を
東へ上り（12 段）、上の階の西ロビーへ出る。2 本の段の間は壁で仕切る。
2F は上りだけ（1F の外は屋外なので下りの段は作らず、北の半分は 2F の床のまま）、7F は下りだけ
（南の半分は口を壁でふさぐ）。部品は西ロビーの wall_lecture_corr<階>_0（各階の段・踊り場・壁）。
"""

from . import kit, shell
from . import plan_lecture_dims as D
from . import plan_lecture_rooms_dims as RD
from . import plan_lecture_shell as SH

X0, Y0, X1, Y1 = D.WEST_STAIR
IX0, IY0, IY1 = X0 + shell.WALL, Y0 + shell.WALL, Y1 - shell.WALL   # 階段室の内法（X1 は西ロビーの外壁の外面）
YM = (IY0 + IY1) * 0.5                 # 2 本の段の境
SPINE = shell.PART                     # 段の間の壁の厚み
RISERS = 12                            # 1 本の段の蹴上げの数（踊り場・上の階の床への段を含む）
RUN = 0.25                             # 踏面
XL = X1 - (RISERS - 1) * RUN           # 踊り場の東の縁（-31.35）
LANDING_T = 0.20                       # 踊り場の厚み
RAIL_H = 0.85                          # 手すりの高さ（段鼻から）

WALL = "lecture_concrete"
TREAD = "lecture_stone_light"
RAIL = "metal_dark"


class _Walls:
    """SH.wall / SH.part に渡す c の代わり（c.wall の代わりに西ロビーの部品へ積む）。"""

    def __init__(self, mb):
        self.wall = mb


def rise(floor):
    return (RD.next_z(floor) - RD.FLOOR_Z[floor]) / (2 * RISERS)


def halves(floor):
    """floor の西ロビーから入れる段の半分 (y0, y1) の並び。7F は北（下りる段）だけ。"""
    south, north = (IY0, YM - SPINE * 0.5), (YM + SPINE * 0.5, IY1)
    return (north,) if floor == RD.TOP_FLOOR else (south, north)


def wall_gaps(floor):
    """西ロビーの外壁（SH.WEST と同じ北 → 南の線、北端 RD.Y_CN）に開ける口（線に沿う距離）。"""
    ys = halves(floor)
    return ((RD.Y_CN - ys[-1][1], RD.Y_CN - ys[0][0]),)


def _flight(mb, y0, y1, z0, h, sgn):
    """X1 から西へ（sgn = -1）上る、または XL から東へ（sgn = +1）上る 11 段。h は蹴上げ。"""
    for k in range(1, RISERS):
        if sgn < 0:
            xa, xb = X1 - k * RUN, X1 - (k - 1) * RUN
        else:
            xa, xb = XL + (k - 1) * RUN, XL + k * RUN
        zt = z0 + k * h
        mb.add_prism([(xa, y0), (xb, y0), (xb, y1), (xa, y1)], zt - h, zt, WALL, TREAD, None)
    ya = y0 + 0.06 if y0 > YM else y1 - 0.06
    xa, xb = (X1, XL) if sgn < 0 else (XL, X1)
    za, zb = (z0, z0 + (RISERS - 1) * h) if sgn < 0 else (z0 + h, z0 + RISERS * h)
    kit.tube(mb, (xa, ya, za + RAIL_H), (xb, ya, zb + RAIL_H), 0.025, RAIL)


def _walls(mb, floor):
    """三方の外壁（2F はスラブの下から）と段の間の壁。7F の上端には蓋。"""
    z0, z1 = RD.FLOOR_Z[floor], RD.next_z(floor)
    top = floor == RD.TOP_FLOOR
    zb = z0 - D.SLAB if floor == RD.FLOORS[0] else z0
    w = _Walls(mb)
    for line in (((X0, Y1), (X0, Y0)), ((X0, Y0), (X1, Y0)), ((X1, Y1), (X0, Y1))):
        SH.wall(w, line, zb, z1, top=top)
    SH.part(w, (XL, YM), (RD.X_IN_W, YM), z0, z1, mat=WALL, top=top)


def build_floor(mb, floor):
    """floor の階の階段室: 西ロビーとの間の床、上る段と踊り場（7F 以外）、壁、2F の床と 7F の天井。"""
    z0 = RD.FLOOR_Z[floor]
    h = rise(floor)
    for y0, y1 in halves(floor):
        kit.box(mb, X1, y0, z0 - D.SLAB, RD.X_IN_W, y1, z0, WALL, top=TREAD)
    if floor == RD.FLOORS[0]:
        kit.box(mb, IX0, IY0, z0 - D.SLAB, X1, IY1, z0, WALL, top=TREAD)
    if floor != RD.TOP_FLOOR:
        zm = z0 + RISERS * h
        _flight(mb, IY0, YM - SPINE * 0.5, z0, h, -1)
        kit.box(mb, IX0, IY0, zm - LANDING_T, XL, IY1, zm, WALL, top=TREAD)
        _flight(mb, YM + SPINE * 0.5, IY1, zm, h, +1)
    else:
        shell.ceiling(mb, IX0, IY0, X1, IY1, RD.next_z(floor), "lecture_ceiling_upper")
    _walls(mb, floor)
