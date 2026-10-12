"""図書館棟 3F: ホワイエ・ガラス床の通路・浮いた階段・西翼・ホールの正面の外殻。

ホールの正面（Y 18.5）の壁そのものは plan_library_hall が作る。ここではその前面に
アルミ鋳物の帯（10 段、上に行くほど手前へ倒れる）と帯の継ぎ目の光の線を貼り、
光庭（天井 Z_WELL）の天窓の帯、ホールの前室・4F ロビーへ上がる浮いた階段を作る。
"""

import math

from . import kit
from . import furn_library as FL
from . import plan_library_dims as D

WOOD = FL.WOOD
WF = FL.WF
ALCAST = "library_shell_alcast"
JOINT = "library_shell_joint"
PART = "library_partition_white"
GREY = "library_info_grey"
FACE_W = math.pi * 0.5       # 正面 -X

BAND_H = 1.1
BAND_N = 10
BAND_Y = (18.35, 18.5)       # 帯の下端・上端の Y（上端が壁の面）
# 帯を切る区間 (x0, x1, 最初の段, 最後の段)。前室の入口と 2 階席の入口の上下の平らな壁を出す
BAND_CUTS = ((-5.0, 5.0, 2, 5), (12.3, 15.7, 6, 9))


# ---- ホールの正面のアルミ鋳物の帯 ----
def _band_spans(k):
    x0, x1 = D.HALL[0], D.OMURA_X
    gaps = sorted((a, b) for a, b, k0, k1 in BAND_CUTS if k0 <= k <= k1)
    out, cur = [], x0
    for a, b in gaps:
        out.append((cur, a))
        cur = b
    out.append((cur, x1))
    return out


def _band(mb, xa, xb, z0, z1):
    """-Y を向いて上が奥へ倒れた帯 1 枚と、両端の三角の蓋。"""
    ya, yb = BAND_Y
    mb.add_face([(xa, ya, z0), (xb, ya, z0), (xb, yb, z1), (xa, yb, z1)], ALCAST)
    mb.add_face([(xa, yb, z1), (xa, yb, z0), (xa, ya, z0)], ALCAST)
    mb.add_face([(xb, yb, z1), (xb, ya, z0), (xb, yb, z0)], ALCAST)


def _shell_face(mb):
    ya, yb = BAND_Y
    for k in range(BAND_N):
        z0 = D.Z3 + BAND_H * k
        z1 = z0 + BAND_H
        for xa, xb in _band_spans(k):
            _band(mb, xa, xb, z0, z1)
            # 帯の上の段差を埋める光の線（下を向く）
            if k < BAND_N - 1:
                kit.plate(mb, xa, ya, xb, yb, z1, JOINT, flip=True)
    # 西翼から見えるホールの西の外殻（X -19.5 の面）と継ぎ目の線
    x = D.HALL[0] - 0.01
    kit.vplate(mb, (x, D.WEST_WING[3]), (x, D.HALL[1]), D.Z3, D.C3, ALCAST)
    for k in range(1, 4):
        z = D.Z3 + BAND_H * k
        kit.vplate(mb, (x - 0.005, D.WEST_WING[3]), (x - 0.005, D.HALL[1]), z, z + 0.03, JOINT)


def _skylight(c, mb):
    """光庭の天井の、ホールの正面に沿った天窓の帯。"""
    x0, y0, x1, _ = D.WELL
    kit.plate(mb, x0, y0, x1, y0 + 0.8, D.Z_WELL - 0.01, "library_skylight", flip=True)
    for x in (-14.0, -4.0, 6.0, 14.0):
        c.light(x, 16.0, D.Z_WELL - 0.6, 350.0, 1.6)


# ---- ガラス床の通路 ----
def _walkway(mb):
    x0, y0, x1, y1 = D.WALKWAY
    z = D.Z3
    kit.box(mb, x0, y0, z - 0.05, x1, y1, z, "library_glass_floor")
    steel = "library_steel_underside"
    for ya, yb in ((y0, y0 + 0.12), (y1 - 0.12, y1)):
        kit.box_nb(mb, x0, ya, z - 0.3, x1, yb, z - 0.05, steel)
    n = int(round((x1 - x0) / 2.0))
    for i in range(n + 1):
        x = min(max(x0 + (x1 - x0) * i / n, x0 + 0.05), x1 - 0.05)
        kit.box_nb(mb, x - 0.05, y0 + 0.12, z - 0.22, x + 0.05, y1 - 0.12, z - 0.05, steel)
    for y in (y0 + 0.05, y1 - 0.05):
        FL.glass_rail(mb, [(x0, y), (x1, y)], z, frame="stainless", cap="stainless")


# ---- 浮いた階段（ホワイエ → 前室の踊り場 → 4F ロビー） ----
def _treads(mb, x0, y0, x1, y1, z0, z1, n):
    run, rise = (x1 - x0) / n, (z1 - z0) / n
    for i in range(n):
        top = z0 + rise * (i + 1)
        kit.box(mb, x0 + run * i, y0, top - 0.05, x0 + run * (i + 1), y1, top, WOOD)
    yc = (y0 + y1) * 0.5
    kit.tube(mb, (x0, yc, z0 - 0.05), (x1, yc, z1 - 0.12), 0.07, "library_steel_underside")


def _back_wall(mb, x0, x1, z0, z1):
    """段の奥（Y 17.8〜18.5）を埋める白い壁。上端は段鼻の線 + 1.0。"""
    y = (17.8 + D.HALL[1]) * 0.5
    t = D.HALL[1] - 17.8
    zb = D.Z3 - 0.22
    kit.thick_quad(mb, (x0, y, zb), (x1, y, zb), (x1, y, z1 + 1.0), (x0, y, z0 + 1.0), t, PART)


def _float_stair(c, mb):
    r1, land, r2, up = D.FLOAT["run1"], D.FLOAT["landing"], D.FLOAT["run2"], D.FLOAT["upper"]
    for x0, y0, x1, y1, z0, z1, n in (r1, r2):
        _treads(mb, x0, y0, x1, y1, z0, z1, n)
        _back_wall(mb, x0, x1, z0, z1)
        FL.stair_rails(mb, x0, y0, x1, y1, z0, z1, "+x", sides=(0,))
    # 1 本目の下は低いので塞ぐ（手前の斜めの板と、踊り場側の端）
    x0, y0, x1, y1, z0, z1, n = r1
    zt = z1 - 0.05
    kit.thick_quad(mb, (x0, y0 + 0.03, z0), (x1, y0 + 0.03, z0), (x1, y0 + 0.03, zt - 0.2),
                   (x0, y0 + 0.03, z0 + (z1 - z0) / n - 0.05), 0.06, PART)
    kit.box(mb, x1 - 0.05, y0, z0, x1, y1, land[4] - 0.2, PART)
    # 踊り場（前室の床へ続く）と 4F の踊り場
    for x0, y0, x1, y1, z in (land, up):
        kit.box(mb, x0, y0, z - 0.2, x1, y1, z, PART, top=WOOD)
    # 段の奥の側は段の奥の壁（踊り場から 1.0 立つ）が手すりを兼ねる
    lx0, ly0, lx1, _ly1, lz = land
    FL.glass_rail(mb, [(lx0, ly0 + 0.04), (lx1, ly0 + 0.04)], lz)
    ux0, uy0, ux1, _uy1, uz = up
    FL.glass_rail(mb, [(ux0, uy0 + 0.04), (ux1 - 0.04, uy0 + 0.04), (ux1 - 0.04, D.HALL[1])], uz)
    c.light(0.0, 17.2, lz + 2.6, 220.0, 1.2)
    c.light(14.0, 17.4, uz + 2.4, 220.0, 1.2)


# ---- ホワイエの什器 ----
def _info(mb):
    """総合案内の灰色の台（西の端に看板の袖板）と、ホワイトボードのスタンド。"""
    z = D.Z3
    kit.box(mb, -6.7, 9.95, z, -5.3, 10.45, z + 1.05, GREY, top="library_counter_black")
    kit.box(mb, -6.85, 9.55, z, -6.7, 10.85, z + 1.5, GREY)
    t = kit.T(-4.4, 10.3, z, math.pi)
    for sx in (-0.55, 0.55):
        t.box_nb(mb, sx - 0.02, -0.02, 0.0, sx + 0.02, 0.02, 1.85, WF)
        t.box_nb(mb, sx - 0.2, -0.02, 0.0, sx + 0.2, 0.02, 0.03, WF)
    t.box(mb, -0.6, -0.02, 0.85, 0.6, 0.02, 1.8, "board_white")


def _omura_doors(mb):
    """大村ホールへの両開きの木の扉（閉じている）。"""
    for y in (9.45, 10.55):
        FL.door(mb, D.OMURA_X, y, D.Z3, FACE_W, leaf=WOOD, open_=0.0)


def _west_wing(mb):
    """西翼の丸柱と、柱を囲むベンチ。ホワイエの西のベンチ（座面 16）。"""
    z = D.Z3
    for cx, cy in ((-37.5, 34.0), (-22.5, 34.0)):
        kit.cyl(mb, cx, cy, z, D.C3, 0.4, "library_concrete", seg=12)
        for ang in (0.0, math.pi * 0.5, math.pi, -math.pi * 0.5):
            t = kit.T(cx, cy, z, ang)
            t.box(mb, -0.65, 0.45, 0.0, 0.65, 0.95, D.SEAT_H, WOOD)
    kit.box(mb, -20.8, 11.78, z, -19.2, 12.22, z + D.SEAT_H, WOOD)


def build(c, mb):
    """mb は furn_library_12_foyer。"""
    _shell_face(mb)
    _skylight(c, mb)
    _walkway(mb)
    _float_stair(c, mb)
    _info(mb)
    _omura_doors(mb)
    _west_wing(mb)
