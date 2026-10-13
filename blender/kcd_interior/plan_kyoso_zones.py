"""共創棟 1F のうち店以外の区画: エントランスホール・スタジオ・ラーニングスクエア1（段床）・
曲線テーブルのホール・ラーニングスクエア2（吹き抜けの下）。"""

import math

from . import furn_kyoso as FK, furniture as F, kit, shell
from . import plan_kyoso_dims as D

WOOD = "kyoso_wood_oak"
BLACK = "kyoso_steel_black"
GREY = "kyoso_sofa_grey"
CHARCOAL = "kyoso_sofa_charcoal"
# 曲線テーブルのまわりの椅子は色を混ぜる
MIXED = ("chair_orange", "plastic_white", "chair_grey", "fabric_yellow", "chair_blue",
         "plastic_white", "chair_green")


def _chair(mb, x, y, ang, mat="plastic_white", frame="metal_gray", z=0.0):
    F.chair_min(mb, kit.T(x, y, z, ang), mat, frame)
    FK.seat_mark(mb, x, y, z, ang, hw=0.22, hd=0.22, inner=0.44)


def _dir_ang(dx, dy):
    """(dx, dy) の向きを正面にする角（kit.T と同じ）。"""
    return math.atan2(-dx, dy)


def _table4(mb, x, y, mat="plastic_white"):
    F.table(mb, x, y, 0.0, w=1.6, d=0.8, h=0.72, top=WOOD, leg=BLACK)
    for dx in (-0.4, 0.4):
        _chair(mb, x + dx, y - 0.72, 0.0, mat)
        _chair(mb, x + dx, y + 0.72, math.pi, mat)


def _sofa(mb, x, y, ang, w=2.4, z=0.0, mat=GREY):
    """箱型ソファを z の床に置き、座面を記録する。座面はソファと一緒に持ち上げる。"""
    base = len(mb.verts)
    FK.seat_mark(mb, x, y, 0.0, ang, hw=w * 0.5, hd=0.42, inner=w - 0.3)
    FK.modular_sofa(mb, x, y, ang, w=w, mat=mat)
    kit.lift(mb, base, z)


def _round_sofa(mb, x, y, z=0.0):
    FK.round_sofa(mb, x, y, z)
    for dx, dy in ((0, -1), (0, 1), (-1, 0), (1, 0)):
        FK.seat_mark(mb, x + 0.55 * dx, y + 0.55 * dy, z, _dir_ang(dx, dy),
                     hw=0.25, hd=0.25, inner=0.5)


# ---- エントランスホール -------------------------------------------------- #
def lobby(c, mb):
    """東の風除室（ガラスの箱）・ベンチ・自立の案内板。"""
    x0, y0, x1, y1 = D.VESTIBULE
    for a, b in (((x0, y0), (x0, y1)), ((x0, y1), (x1, y1)), ((x1, y1), (x1, y0))):
        FK.glass_wall(mb, a, b, 3.2)
    kit.box(mb, x0, y0, 3.2, x1, y1, 3.3, BLACK, top=BLACK)
    for bx in (-6.0, -10.0):
        F.bench(mb, bx, 13.6, math.pi, w=1.8, mat=WOOD)
    # 1F の案内板（自立）
    kit.box(mb, 2.0, 6.2, 0.05, 3.2, 6.32, 1.9, "kyoso_info_beige")
    for lx in (2.1, 3.1):
        kit.box_c(mb, lx, 6.26, 0.0, 0.05, 0.05, 0.1, BLACK)
    kit.box(mb, 2.0, 6.2, 1.9, 3.2, 6.32, 1.95, BLACK)


# ---- スタジオ ------------------------------------------------------------ #
def studio(c, mb):
    """扉の西のガラス張りの部屋。エントランスホール側と奥（ラーニングスクエア1 側）に扉。"""
    x0, y0, x1, y1 = D.STUDIO
    kit.plate(mb, x0, y0, x1, y1, 0.012, "kyoso_floor_carpet_grey")
    FK.glass_wall(mb, (x0, y0), (x0, y1), D.Z_CEIL, gaps=((9.3 - y0, 11.1 - y0),),
                  door_top=2.6)
    FK.glass_wall(mb, (x0, y1), (D.CORE_W[0], y1), D.Z_CEIL, gaps=((2.0, 3.6),),
                  door_top=2.6)
    for tx in (12.0, 24.0, 34.0):
        _table4(mb, tx, 9.0)
    for wx in (25.0, 35.0):
        F.whiteboard(mb, wx, D.CORE_W[1], 1.5, math.pi, w=2.4, h=1.2)


# ---- ラーニングスクエア1（四方へ下りる段床）------------------------------- #
def learning_square(c, mb):
    """段床の上に箱型ソファの島が並ぶ。西の縁にグレーのベンチ、手前の縁にソファの列。"""
    z = D.DECK_H
    FK.deck_all(mb, *D.LS1)
    for sx in (6.6, 10.6):
        for ya, yb in ((20.0, 22.4), (23.8, 26.0)):
            _sofa(mb, sx, ya, D.FACE_PARK, z=z)
            _sofa(mb, sx, yb, D.FACE_MALL, z=z, mat=CHARCOAL if sx < 8 else GREY)
            F.round_table(mb, sx, (ya + yb) * 0.5, r=0.32, h=0.45, top=BLACK, leg=BLACK,
                          z=z)
    for sx in (6.4, 9.2, 12.0):
        _sofa(mb, sx, 16.6, D.FACE_PARK, w=2.0, z=z)
    for y0, y1 in ((19.2, 22.6), (23.4, 27.0)):
        FK.box_bench(mb, 13.0, y0, 13.5, y1, z=z)
        FK.seat_mark(mb, 13.25, (y0 + y1) * 0.5, z, D.FACE_EAST, hw=(y1 - y0) * 0.5,
                     hd=0.25, inner=y1 - y0)
    FK.box_stool(mb, 13.25, 23.0, z)
    FK.pouf(mb, 8.6, 24.9, z, mat=CHARCOAL)
    FK.seat_mark(mb, 8.6, 24.9, z, D.FACE_EAST, hw=0.3, hd=0.3, inner=0.5)
    # 公園側へ延びる小さな段床（丸いソファ）
    FK.deck_all(mb, *D.LS1_EXT)
    _round_sofa(mb, 1.75, 30.35, z)


# ---- 曲線テーブルのホール -------------------------------------------------- #
def _arc_table(mb, cx, cy, r0, r1, a0, a1, n=10, h=0.72):
    """中心 (cx, cy) の円環の一部の形の白い天板と脚。角度は度で +X から反時計回り。"""
    for k in range(n):
        p = math.radians(a0 + (a1 - a0) * k / n)
        q = math.radians(a0 + (a1 - a0) * (k + 1) / n)
        pts = [(cx + r0 * math.cos(p), cy + r0 * math.sin(p)),
               (cx + r1 * math.cos(p), cy + r1 * math.sin(p)),
               (cx + r1 * math.cos(q), cy + r1 * math.sin(q)),
               (cx + r0 * math.cos(q), cy + r0 * math.sin(q))]
        mb.add_prism(pts, h - 0.04, h, "plastic_white", "plastic_white")
        if k % 2 == 0:
            rm = (r0 + r1) * 0.5
            kit.cyl(mb, cx + rm * math.cos(p), cy + rm * math.sin(p), 0.0, h - 0.04, 0.03,
                    "metal_gray", seg=4)
    rm = (r0 + r1) * 0.5
    q = math.radians(a1)
    kit.cyl(mb, cx + rm * math.cos(q), cy + rm * math.sin(q), 0.0, h - 0.04, 0.03,
            "metal_gray", seg=4)


def _ring_chairs(mb, cx, cy, r, a0, a1, n, inward, k0=0):
    for k in range(n):
        a = math.radians(a0 + (a1 - a0) * (k + 0.5) / n)
        ux, uy = math.cos(a), math.sin(a)
        ang = _dir_ang(-ux, -uy) if inward else _dir_ang(ux, uy)
        _chair(mb, cx + r * ux, cy + r * uy, ang, MIXED[(k + k0) % len(MIXED)])


def hall(c, mb):
    """人造石の床に白い曲線テーブル。外側と内側に色を混ぜた椅子、手前に机 2 台、奥に移動式の白板。"""
    x0, y0, x1, y1 = D.HALL
    FK.terrazzo(mb, x0, y0, x1, y1, c.rng)
    cx, cy = D.CURVE_C
    r = D.CURVE_R
    _arc_table(mb, cx, cy, r - 0.4, r + 0.4, 20.0, 160.0)
    _ring_chairs(mb, cx, cy, r + 0.85, 20.0, 160.0, 13, inward=True)
    _ring_chairs(mb, cx, cy, r - 0.85, 30.0, 150.0, 7, inward=False, k0=3)
    for tx in (-3.0, 2.6):
        _table4(mb, tx, 20.0)
    for wx in (-3.2, 0.0, 3.2):
        F.whiteboard(mb, wx, 26.6, 0.9, math.pi, w=1.4, h=1.0)
        for dx in (-0.66, 0.66):
            kit.box_c(mb, wx + dx, 26.6, 0.0, 0.04, 0.04, 1.95, "metal_gray")
        kit.box_c(mb, wx, 26.6, 0.0, 1.4, 0.5, 0.04, "metal_gray")


# ---- ラーニングスクエア2（吹き抜けの下）------------------------------------ #
def learning_square_2(c, mb):
    """公園側の窓沿いのカウンター席と、丸机とラウンジチェアの組。"""
    x0, y0, x1, y1 = D.LS2
    kit.plate(mb, x0, y0, x1, y1, 0.012, "kyoso_floor_wood_oak")
    F.counter(mb, -5.6, 38.8, 10.6, 39.4, h=0.95, body=WOOD, top=WOOD, toe=False)
    for k in range(16):
        sx = -5.0 + 1.0 * k
        F.stool(mb, sx, 38.3, D.FACE_PARK, mat=WOOD, h=0.7)
        FK.seat_mark(mb, sx, 38.3, 0.0, D.FACE_PARK, hw=0.2, hd=0.2, inner=0.4)
    for tx, ty in ((-3.5, 32.6), (-3.5, 35.6), (6.6, 35.6), (9.2, 33.0)):
        F.round_table(mb, tx, ty, r=0.4, top=WOOD)
        for dx in (-0.8, 0.8):
            F.lounge_chair(mb, tx + dx, ty, D.FACE_WEST if dx < 0 else D.FACE_EAST,
                           mat="kyoso_sofa_beige")
    for px, py in ((-5.6, 31.0), (10.6, 31.0)):
        shell.planter(mb, px, py)
