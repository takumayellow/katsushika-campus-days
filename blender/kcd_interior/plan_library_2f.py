"""図書館棟 2F: 左ブロック・開架書架・右手前の閲覧席・キャレル・グループ学習室・黙考書院・大階段。

外周の壁・床・天井と北西の吹き抜けの手すりは plan_library_shell、吹き抜けに面した縁
（西の縁 X -9 を除く）の手すりは plan_library_gallery が作る。看板の板の裏に当たる面
（黙考書院の扉の上の壁、グループ学習室の数字の壁、案内のスタンド）はここで作る。
"""

import math

from . import kit
from . import furn_library as FL
from . import plan_library_dims as D

WOOD = FL.WOOD
DARK = FL.DARK
WF = FL.WF
GLASS = FL.GLASS
PART = "library_partition_white"
DESK_TOP = "library_desk_top_dark"
BLACK = "library_counter_black"
CRIMSON = "library_carpet_crimson"
SLAT = "library_wood_slat"
Z = D.Z2
FACE_N = 0.0                 # 正面 +Y
FACE_S = math.pi             # 正面 -Y
FACE_W = math.pi * 0.5       # 正面 -X
FACE_E = -math.pi * 0.5      # 正面 +X
NPCS_2F = tuple((x, y) for x, y, z in D.NPCS if abs(z - Z) < 1e-6)


def _seat_chair(mb, x, y, z, ang, mat=FL.CHAIR):
    """座面・背・1 本脚の軽い椅子（正面 +Y、34 tris）。キャレルと長机に並べる。"""
    t = kit.T(x, y, z, ang)
    t.box(mb, -0.22, -0.21, 0.42, 0.22, 0.21, 0.47, mat)
    t.box(mb, -0.22, -0.26, 0.47, 0.22, -0.21, 0.86, mat)
    t.box_nb(mb, -0.03, -0.03, 0.0, 0.03, 0.03, 0.42, WF)


def _near_npc(x, y, r=0.75):
    return any(math.hypot(x - nx, y - ny) < r for nx, ny in NPCS_2F)


def _range_x(mb, x0, x1, yc, h, d, rng, levels=5, frame=WOOD, seg=(0.6, 1.4), ladder=True):
    """X 方向に長い両面の書架 1 連の簡略形（本体の箱・端板・天板・両面の本・棚の線）。"""
    hd = d * 0.5
    yf, yb = yc + hd - 0.01, yc - hd + 0.01
    kit.box_nb(mb, x0, yc - hd + 0.02, Z, x1, yc + hd - 0.02, Z + h - 0.02, frame)
    for xa in (x0 - 0.03, x1):
        kit.box_nb(mb, xa, yc - hd, Z, xa + 0.03, yc + hd, Z + h, frame)
    kit.box_nb(mb, x0, yc - hd, Z + h - 0.03, x1, yc + hd, Z + h, frame)
    front = kit.T(0.0, 0.0, Z, 0.0)
    back = kit.T(0.0, 0.0, Z, math.pi)
    step = (h - 0.15) / levels
    for k in range(levels):
        a = 0.10 + step * k
        b = a + step - 0.03
        FL.books_face(mb, front, x0 + 0.02, x1 - 0.02, yf, a, b, rng, seg=seg)
        FL.books_face(mb, back, -x1 + 0.02, -x0 - 0.02, -yb, a, b, rng, seg=seg)
        kit.vplate(mb, (x1, yf + 0.012), (x0, yf + 0.012), Z + a - 0.025, Z + a, frame)
        kit.vplate(mb, (x0, yb - 0.012), (x1, yb - 0.012), Z + a - 0.025, Z + a, frame)
    if ladder:
        kit.tube(mb, (x0, yf + 0.07, Z + h - 0.25), (x1, yf + 0.07, Z + h - 0.25), 0.02,
                 WF, seg=4)


# ---- 2F 左ブロック ----
def _west_counter(mb):
    """吹き抜け際（X -9）の黒い天板の長いカウンター机・白い椅子・ガラスの手すり。"""
    ax0, ay0, _ax1, _ay1 = D.ATRIUM
    _, _, _, (_, n2y1) = D.TIERS[1]
    FL.glass_rail(mb, [(ax0 - 0.05, ay0 - 0.05), (ax0 - 0.05, n2y1 + 0.05)], Z)
    for ya, yb in ((13.0, 25.0), (25.0, 37.5), (37.5, 50.0)):
        FL.desk(mb, -9.62, ya, -9.14, yb, Z, top=BLACK)
    for k in range(23):
        _seat_chair(mb, -10.05, 14.0 + 1.6 * k, Z, FACE_E)


def _reference(mb, rng):
    """参考図書の書架 10 連（5 列 x 2）と、回廊の西の壁の書架（Y 40〜45 は空ける）。"""
    for yc in (16.0, 19.0, 22.0, 25.0, 28.0):
        for x0, x1 in ((-24.0, -18.25), (-17.25, -11.5)):
            _range_x(mb, x0, x1, yc, 1.8, 0.6, rng, levels=5, frame=DARK, ladder=False)
    for yc in (20.5, 25.0, 29.5, 34.0):
        t = kit.T(D.X_W, yc, Z, FACE_E)
        FL.wall_shelf(mb, t, 4.2, 2.1, 0.35, rng, levels=5, frame=DARK)


def _lounge(mb):
    """弧のソファと丸テーブル、紫の箱形の椅子。"""
    cx, cy = -18.5, 34.5
    FL.arc_sofa(mb, cx, cy, Z, 1.8, math.pi * 0.1, math.pi * 0.9, 6, mat="library_info_grey")
    FL.round_table(mb, cx, cy, Z, r=0.55)
    for x in (-22.0, -15.5):
        FL.box_sofa(mb, x, 43.8, Z, FACE_N, w=1.4)
        FL.box_sofa(mb, x, 46.6, Z, FACE_S, w=1.4)
        FL.round_table(mb, x, 45.2, Z, r=0.4, h=0.45)


def _recommend(mb, rng):
    """教養・教員おすすめの棚（両面 4 連）と OPAC。"""
    for xc in (-22.5, -17.5):
        for yc in (55.0, 58.2):
            for ang in (FACE_N, FACE_S):
                t = kit.T(xc, yc, Z, ang)
                FL.wall_shelf(mb, t, 3.0, 1.6, 0.35, rng, levels=4, frame=WOOD)
    FL.opac(mb, -16.0, 53.2, Z, FACE_S)


# ---- 開架書架・ペアワーク ----
def _stacks(mb, rng):
    x0, x1 = D.STACKS_X
    for k in range(D.STACK_N):
        _range_x(mb, x0, x1, D.STACK_Y0 + D.STACK_PITCH * k, 2.1, 0.6, rng)
    # 窓際の閲覧机（書架の間の通路の正面に椅子）
    for ya, yb in ((18.2, 27.3), (27.5, 36.6), (36.8, 45.9)):
        FL.desk(mb, 39.5, ya, 40.6, yb, Z, top=WOOD)
    for k in range(D.STACK_N - 1):
        _seat_chair(mb, 39.0, D.STACK_Y0 + D.STACK_PITCH * (k + 0.5), Z, FACE_E)


def _pair(mb):
    """話してよい席: 黒い曲線のソファと白い丸テーブル。"""
    for cx in (25.0, 30.5, 36.0):
        FL.arc_sofa(mb, cx, 50.0, Z, 1.3, math.pi * 0.15, math.pi * 0.85, 5, mat=BLACK)
        FL.round_table(mb, cx, 50.0, Z, r=0.5)


# ---- 右手前の閲覧席 ----
def _front_desks(mb):
    for yd in (8.0, 11.2):
        for xa in (21.0, 28.0, 35.0, 42.0):
            FL.desk(mb, xa, yd, xa + 6.0, yd + 0.8, Z, top=WOOD)
            for x in (xa + 1.5, xa + 4.5):
                _seat_chair(mb, x, yd - 0.55, Z, FACE_N)
            FL.desk_light(mb, xa + 3.0, yd + 0.6, Z + 0.72, FACE_S)


# ---- キャレル ----
def _carrels(mb):
    _x0, y0, _x1, y1 = D.CARRELS
    aisle_run = 4                    # 4 本目と 5 本目の間は通路（4 本目の東に椅子を置かない）
    for k in range(1, 10):
        xc = -12.4 + 3.25 * (k - 1)
        kit.box(mb, xc - 0.7, y0, Z + 0.70, xc + 0.7, y1, Z + 0.74, WOOD)
        for ya in (y0, y1 - 0.04):
            kit.box_nb(mb, xc - 0.66, ya, Z, xc + 0.66, ya + 0.04, Z + 0.70, WF)
        kit.box(mb, xc - 0.02, y0, Z + 0.74, xc + 0.02, y1, Z + 1.25, PART)
        for y in (55.5, 56.5, 57.5):
            kit.box(mb, xc - 0.7, y - 0.015, Z + 0.74, xc + 0.7, y + 0.015, Z + 1.15, PART)
        for side, ang in ((-1, FACE_E), (1, FACE_W)):
            if side > 0 and k == aisle_run:
                continue
            x = xc + side * 1.15
            for y in (55.0, 56.0, 57.0, 58.0):
                if not _near_npc(x, y):
                    _seat_chair(mb, x, y, Z, ang)
        FL.desk_light(mb, xc - 0.35, 56.0, Z + 0.74, FACE_E)
    # 窓際のカウンター
    for xa in (-12.0, -4.0, 4.0):
        FL.desk(mb, xa, 62.0, xa + 8.0, 62.6, Z, top=WOOD)
    for k in range(12):
        _seat_chair(mb, -11.0 + 2.0 * k, 61.45, Z, FACE_N)


# ---- グループ学習室・案内のスタンド ----
def _group(mb):
    gx0, gy0, gx1, gy1 = D.GROUP
    yw = gy0 + 0.03                  # 正面の壁の中心（南の面が Y 53.45）
    for i in range(5):
        x0 = gx0 + D.GROUP_W * i
        x1, xc = x0 + D.GROUP_W, x0 + D.GROUP_W * 0.5
        # 正面: 扉・濃い壁（数字の看板の裏）・ガラス
        holes = ((0.25, 1.25, Z, Z + 2.1), (3.0, D.GROUP_W - 0.08, Z + 0.05, Z + 2.9))
        FL.holed_wall(mb, (x0, yw), (x1, yw), Z, D.C2, "library_wall_darkgrey", holes=holes)
        kit.box(mb, x0 + 3.0, yw - 0.02, Z + 0.05, x1 - 0.08, yw + 0.02, Z + 2.9, GLASS)
        FL.door(mb, x0 + 0.75, yw, Z, FACE_N, w=1.0, h=2.1, glass=GLASS)
        # 西の仕切り（東端の室は外周のガラスまで）
        FL.holed_wall(mb, (x0, gy0 + 0.11), (x0, gy1), Z, D.C2, PART, thick=0.12)
        kit.box(mb, x1 - 0.10, 55.0, Z + 0.9, x1 - 0.06, 57.8, Z + 2.1, "library_table_white")
        # 丸テーブルと椅子（①は東の椅子の所に、ホワイトボードの前の NPC が立つ）
        FL.round_table(mb, xc, 57.3, Z, r=0.5)
        chairs = [(xc, 56.5, FACE_N), (xc, 58.1, FACE_S), (xc + 0.8, 57.3, FACE_W),
                  (xc - 0.8, 57.3, FACE_E)]
        for x, y, ang in chairs:
            if not _near_npc(x, y):
                FL.chair(mb, x, y, Z, ang)
    # 「グループ学習室」のスタンド（看板は -X の面）
    kit.box(mb, 20.5, 50.5, Z, 20.58, 52.5, Z + 1.9, "library_info_grey")


# ---- 黙考書院 ----
def _mokko(mb, rng):
    mx0, my0, mx1, my1 = D.MOKKO
    yw = D.MOKKO_FACE_Y + 0.08       # 南の壁の中心（南の面が看板の裏）
    xw = mx1 - 0.08                  # 東の壁の中心（東の面が X -25.4）
    door0, door1 = -38.3, -36.7

    def s(x):
        return x - mx0

    holes = ((s(door0), s(door1), Z, Z + 2.1),
             (s(-39.1), s(-38.4), Z, Z + 2.1), (s(-36.6), s(-35.8), Z, Z + 2.1))
    FL.holed_wall(mb, (mx0, yw), (mx1, yw), Z, D.C2, DARK, holes=holes)
    for xa, xb in ((-39.1, -38.4), (-36.6, -35.8)):
        kit.box(mb, xa, yw - 0.02, Z, xb, yw + 0.02, Z + 2.1, "library_glass_frit")
    # 両開きの扉は内側へ開けておく
    for xh, sgn in ((door0, 1), (door1, -1)):
        kit.box(mb, min(xh, xh + sgn * 0.05), yw + 0.08, Z, max(xh, xh + sgn * 0.05),
                yw + 0.88, Z + 2.08, DARK)
    FL.holed_wall(mb, (xw, yw + 0.08), (xw, my1), Z, D.C2, PART)
    # 上部の横スラットの木（東の壁の内側）
    for k in range(4):
        zs = Z + 2.2 + 0.18 * k
        kit.box(mb, xw - 0.14, yw + 0.1, zs, xw - 0.08, my1 - 0.02, zs + 0.08, SLAT)
    # 奥の全面ガラスの白いロールスクリーン（半分ほど下ろしてある）
    for xa, xb, zb in ((-41.2, -37.4, Z + 1.6), (-37.3, -33.5, Z + 1.9),
                       (-33.4, -29.6, Z + 1.5), (-29.5, -25.7, Z + 2.0)):
        kit.vplate(mb, (xa, my1 - 0.05), (xb, my1 - 0.05), zb, D.C2, "library_ceiling_white")
    # 窓を向いたスリッパーチェア 8 と小さな丸いサイドテーブル
    chairs = ((-39.5, 57.5), (-37.0, 57.5), (-34.6, 57.5), (-33.0, 56.0), (-31.0, 56.0),
              (-29.0, 56.0), (-27.0, 56.0), (-39.5, 54.6))
    for x, y in chairs:
        FL.slipper_chair(mb, x, y, Z, FACE_N)
    for x, y in ((-38.25, 57.7), (-32.0, 56.2), (-28.0, 56.2)):
        FL.round_table(mb, x, y, Z, r=0.22, h=0.5, top=DARK, leg=DARK)
    # 暗い色の丸い一本脚テーブル・緑の肘掛け椅子・白いソファ・デスクライト・低い書架
    FL.round_table(mb, -34.5, 50.5, Z, r=0.6, top=DARK, leg=DARK)
    FL.desk_light(mb, -34.5, 50.7, Z + 0.72, FACE_S)
    FL.slipper_chair(mb, -34.5, 49.6, Z, FACE_N)
    FL.box_sofa(mb, -39.6, 50.5, Z, FACE_E, w=0.9, d=0.85, mat="library_tabchair_green")
    FL.box_sofa(mb, -26.3, 51.0, Z, FACE_W, w=2.0, d=0.85, mat=FL.CHAIR)
    for xc in (-32.0, -28.5):
        t = kit.T(xc, yw + 0.08, Z, FACE_N)
        FL.wall_shelf(mb, t, 3.0, 1.0, 0.35, rng, levels=3, frame=DARK)


# ---- 大階段・EV ----
def _grand(mb):
    g = D.GRAND
    zm = g["z_mid"]
    r1, ld, r2 = g["run1"], g["landing"], g["run2"]
    FL.stair(mb, *r1, Z, zm, 15, "+y", tread=CRIMSON, riser=WOOD)
    FL.stair_rails(mb, *r1, Z, zm, "+y")
    kit.box(mb, *ld[:2], Z, *ld[2:], zm, WOOD, top=CRIMSON)
    FL.stair(mb, *r2, zm, D.Z3, 15, "-y", tread=CRIMSON, riser=WOOD)
    FL.stair_rails(mb, *r2, zm, D.Z3, "-y")
    kit.box(mb, r2[0], r2[1], Z, r2[2], r2[3], zm - 0.05, WOOD)       # 2 本目の下の塊
    # 踊り場の縁
    FL.glass_rail(mb, [(ld[0] + 0.05, ld[1]), (ld[0] + 0.05, ld[3])], zm)
    FL.glass_rail(mb, [(r1[2], ld[1] + 0.05), (r2[0], ld[1] + 0.05)], zm)
    # 背後の横ルーバーの壁（3F の穴の奥の縁を兼ねる）と右の淡い金色の腰壁
    hx0, hy0, hx1, hy1 = g["hole"]
    kit.box(mb, hx0, hy1, Z, hx1, hy1 + 0.16, D.C3, SLAT)
    for k in range(10):
        zl = Z + 0.6 + 0.85 * k
        kit.box(mb, hx0, hy1 - 0.05, zl, hx1, hy1, zl + 0.1, WOOD)
    kit.box(mb, hx1, g["top"][1], Z, hx1 + 0.16, hy0, D.C2, WOOD)
    kit.box(mb, hx1, hy0, Z, hx1 + 0.16, hy1, D.Z3 + 1.05, WOOD)
    # 3F の穴の縁の手すり（2 本目の上がり口は開ける）
    FL.glass_rail(mb, [(r2[0], hy0 - 0.05), (hx0 - 0.05, hy0 - 0.05), (hx0 - 0.05, hy1)],
                  D.Z3)


def _ev(mb):
    """EV 2 基（閉じた扉だけ。乗れない）。"""
    ex0, ey0, ex1, ey1 = D.EV2
    kit.box(mb, D.X_W, ey0 + 0.1, Z, ex1, ey1, D.C2, PART)
    for xc in (ex0 + 1.5, ex0 + 4.5):
        FL.door(mb, xc, ey1, Z, FACE_N, w=1.1, h=2.3, leaf="library_glass_dark",
                frame="stainless", open_=0.0)
        kit.box(mb, xc + 0.85, ey1, Z + 1.0, xc + 0.97, ey1 + 0.05, Z + 1.3, "stainless")


def build(c, M):
    """M は "west2f" "stacks" "frontdesk" "carrel" "group" "mokko" "stair" の MeshBuilder。"""
    rng = c.rng
    _west_counter(M["west2f"])
    _reference(M["west2f"], rng)
    _lounge(M["west2f"])
    _recommend(M["west2f"], rng)
    _stacks(M["stacks"], rng)
    _pair(M["stacks"])
    _front_desks(M["frontdesk"])
    _carrels(M["carrel"])
    _group(M["group"])
    _mokko(M["mokko"], rng)
    _grand(M["stair"])
    _ev(M["stair"])
