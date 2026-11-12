"""共創棟 4F: 3F の廊下と教室の真上に、廊下と教室 2 室（床は吹き抜けの頂部のスラブの上）。

4F に教室 C401〜C413 があることは確定（spec §1-5）。どの教室がどこにあるか、室の大きさ・机の並べ方は
写真が無いので 3F と同じにした（推定）。3F と違って吹き抜けに面していないので、室の奥は窓の帯のある外壁。
窓の外は吹き抜けの頂部の屋上（Z_TOP に上向きの面を張る）。東のコアの西の面の階段室の扉で 3F と行き来する。

部品の名前は 3F と同じ決まりで、階の名前だけ 4f にする（plan_kyoso_3f の Level）。
"""

import random

from . import kit, shell
from . import plan_kyoso_2f_rooms as R2
from . import plan_kyoso_3f as P3
from . import plan_kyoso_dims as D

L4 = P3.Level("4f", D.Z4, D.Z_CEIL4, D.Z_TOP, D.Z_CEIL4 + 0.2)   # 壁は天井裏 0.2 m まで
SILL = 0.9              # 窓の帯の腰壁（床から）
HEADER = 0.5            # 窓の帯の垂れ壁（天井から）
ROOF = "concrete_grey"  # 窓の外に見える吹き抜けの頂部の屋上
CORRIDOR_KINDS = ("bench", "bins")      # 東・西の室の前の廊下の小物（3F と入れ替えて見分ける）


def _window_face(mb, xa, xb):
    """室の奥の外壁と窓の帯。外面は ROOMS3 の奥の線から WALL_T 外。"""
    y = D.ROOMS3[3] + P3.WALL_T
    shell.outer_wall(mb, (xb, y), (xa, y), L4.z, L4.ceil, glass=P3.GLASS, thick=P3.WALL_T,
                     sill=SILL, header=HEADER, seg=P3.MULLION, mullion_mat=P3.FRAME)


def roof(mb):
    """窓の外に見える吹き抜けの頂部の屋上（上向きの面だけ）。"""
    x0, _, x1, _ = D.ATRIUM
    kit.plate(mb, x0 - P3.WALL_T, D.ROOMS3[3] + P3.WALL_T, x1 + P3.WALL_T, D.Y_PARK, D.Z_TOP, ROOF)


def lamps():
    """4F の 2 室の中央と廊下の点光源の位置（X, Y）。"""
    x0, y0, x1, y1 = D.ROOMS3
    yc = (y0 + y1) * 0.5
    return ((0.5 * (x0 + D.ROOMS3_SPLIT), yc), (0.5 * (D.ROOMS3_SPLIT + x1), yc)) + P3.corridor_lamps()


def build(c):
    """2 室と廊下、4F の階段室の扉、窓の外の屋上を組む。戻り値は室の点光源の位置（Z を含まない）。"""
    rng = random.Random(R2.CORRIDOR_SEED + 2)
    pts = P3.build_level(c, L4, _window_face, rng, CORRIDOR_KINDS)
    corr = P3.corridor(c, L4)
    P3.stair_door(c, corr, "4F", L4.z, L4.ceil)
    roof(corr)
    return pts
