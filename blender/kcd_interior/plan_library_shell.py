"""図書館棟の躯体: 外周壁・階ごとの床・ギャラリーの段・天井・照明。

外周は直方体ではない（東のノッチ・奥の張り出し）。外周の閉じ（closure）は外接矩形の
内面線で測るので、壁の上端には下向きの蓋を 0.01 上に貼り、壁の天面に立てないようにする。
"""

import math

from . import kit, shell
from . import plan_library_dims as D

CONCRETE = "library_concrete"
GLASS = "glass_partition"
IVORY = "library_wall_ivory"
PART_MAT = "library_partition_white"
CAP_UP = 0.01        # 壁の天面に貼る蓋の高さ

# 外周壁の外面の線（厚みは a -> b の左手＝室内側）
FRONT = ((-41.6535, 4.0), (49.6607, 4.0))
RIGHT = ((49.66, 4.0), (49.66, 14.449))
NOTCH_OUT = ((49.66, 14.449), (41.3, 15.679))
EAST = ((41.3, 15.38), (41.3, 59.5))
BACK_E = ((41.3, 59.5), (15.5, 59.5))
BUMP_E = ((15.8, 59.2), (15.8, 63.3))
BUMP_N = ((15.8, 63.3), (-14.9, 63.3))
BUMP_W = ((-14.9, 63.3), (-14.9, 60.6))
BACK_W = ((-14.6, 60.9), (-41.65, 60.9))
WEST = ((-41.65, 60.9), (-41.65, 4.0))

ATRIUM_HOLES = ((-9.0, 6.2, 19.5, 15.0), (-9.0, 15.0, 15.5, 47.5))
SLAT = (-9.0, 6.2, 19.5, 13.0)          # 入口の上の木ルーバー天井
STEEL = (-9.0, 15.0, 15.5, 47.5)        # 吹き抜けの上の鉄骨の見える天井


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


def wall(c, line, z0, z1, mat=CONCRETE, glass=None, header=0.0, gaps=(), gap_top=None,
         top=True):
    """外周壁の 1 帯。top=True なら上端に蓋を貼る。"""
    a, b = line
    shell.outer_wall(c.wall, a, b, z0, z1, wall=mat, glass=glass, sill=0.0, header=header,
                     seg=3.0, gaps=gaps, gap_top=gap_top)
    if top:
        cap(c.wall, a, b, z1, 0.0, shell.WALL)


def part(c, a, b, z0, z1, mat=PART_MAT, thick=shell.PART, gaps=()):
    """間仕切り（線の中心に厚み）と天面の蓋。"""
    shell.partition(c.wall, a, b, z0, z1, mat, thick=thick, gaps=gaps)
    cap(c.wall, a, b, z1, -thick * 0.5, thick * 0.5)


def _outer(c):
    # 正面: 各階のガラスの帯（1F は風除室の開口、2F はガラスの上に天井裏の垂れ壁）
    door = (38.6535, 44.6535)
    wall(c, FRONT, D.Z1, D.Z2, glass=GLASS, header=0.5, gaps=[door], gap_top=3.2, top=False)
    wall(c, FRONT, D.Z2, D.Z3, glass=GLASS, header=2.0, top=False)
    wall(c, FRONT, D.Z3, 14.3, glass=GLASS, header=0.3)
    shell.glass_entrance(c.wall, -3.0, 3.0, 0.0, 3.2, 4.15, glass=GLASS)
    c.door_gap = (-3.0, 3.0)
    # 東: 1F は腰までのコンクリート、2F はガラス
    for line in (RIGHT, NOTCH_OUT, EAST):
        wall(c, line, D.Z1, D.Z2, top=False)
        wall(c, line, D.Z2, D.Z3, glass=GLASS, header=1.7)
    wall(c, BACK_E, D.Z1, D.Z3)
    # 奥の張り出し（2F は窓際カウンターの背のアイボリーの壁、上はホールの外殻）
    for line in (BUMP_E, BUMP_W):
        wall(c, line, D.Z1, 22.0 - CAP_UP)
    wall(c, BUMP_N, D.Z1, D.Z2, top=False)
    wall(c, BUMP_N, D.Z2, D.Z3, mat=IVORY, top=False)
    wall(c, BUMP_N, D.Z3, 22.0 - CAP_UP)
    # 奥の西: 2F（黙考書院・おすすめの棚）は床から天井までのガラス
    wall(c, BACK_W, D.Z1, D.Z2, top=False)
    wall(c, BACK_W, D.Z2, D.Z3, glass=GLASS, header=2.0)
    wall(c, WEST, D.Z1, 14.3)


def _partitions(c):
    zt = D.C1 + 0.28          # 2F スラブの下面まで
    # 1F の左右（外側は別施設）と奥
    part(c, (-D.X_P, D.Y_F), (-D.X_P, 60.0), D.Z1, zt)
    part(c, (D.X_P, D.Y_F), (D.X_P, 60.0), D.Z1, zt)
    part(c, (-D.X_P, 60.0), (D.X_P, 60.0), D.Z1, zt)
    # 北西の吹き抜けの下（理科大サイエンス道場の上の吹き抜け）
    part(c, (D.X_W, 13.0), (-D.X_P, 13.0), D.Z1, zt)
    # 3F: 大村ホールとの境（光庭の部分は Z_WELL まで）・西翼の奥
    x = D.OMURA_X + shell.PART * 0.5
    part(c, (x, D.Y_F), (x, 15.0), D.Z3, D.C3)
    part(c, (x, 15.0), (x, 18.5), D.Z3, D.Z_WELL)
    part(c, (D.X_W, 40.0), (-D.X_P, 40.0), D.Z3, D.C3)


def _floors(c):
    fl, side = c.floor, CONCRETE
    # 1F（オリーブの床にカーキの市松）と、北西の吹き抜けの下
    shell.floor(fl, -D.X_P, D.Y_F, D.X_P, 60.0, D.Z1, "library_carpet_olive", side_mat=side)
    shell.floor(fl, D.X_W, D.Y_F, -D.X_P, 13.0, D.Z1, "library_carpet_khaki", side_mat=side)
    t = 3.6
    for i in range(int(2 * D.X_P / t) + 1):
        for j in range(int((60.0 - D.Y_F) / t) + 1):
            if (i + j) % 2:
                continue
            x0, y0 = -D.X_P + i * t, D.Y_F + j * t
            x1, y1 = min(x0 + t, D.X_P), min(y0 + t, 60.0)
            if x1 - x0 > 0.05 and y1 - y0 > 0.05:
                kit.plate(fl, x0, y0, x1, y1, 0.004, "library_carpet_khaki")
    # 2F（吹き抜け・北西の吹き抜けを抜く。奥は張り出しの中まで、東はノッチの斜めの縁まで）
    carpet = "floor_carpet_grey"
    shell.floor(fl, D.X_W, D.Y_F, D.X_E2, 59.2, D.Z2, carpet, side_mat=side,
                holes=(D.NW_VOID,) + ATRIUM_HOLES)
    shell.floor(fl, D.X_W, 59.2, -14.6, 60.6, D.Z2, carpet, side_mat=side)
    shell.floor(fl, -14.6, 59.2, 15.5, 63.0, D.Z2, carpet, side_mat=side)
    (nx0, ny0), (nx1, ny1) = D.NOTCH
    shell.floor(fl, D.X_E2, D.Y_F, D.X_E, ny0, D.Z2, carpet, side_mat=side)
    fl.add_prism([(nx1, ny0), (nx0, ny0), (nx1, ny1)], D.Z2 - 0.22, D.Z2, side, carpet, side)
    # 3F（ホワイエ + 西翼。ガラス床の通路と大階段の穴を抜く）
    carpet = "library_carpet_crimson"
    shell.floor(fl, D.FOYER[0], D.FOYER[1], D.FOYER[2], D.FOYER[3], D.Z3, carpet,
                side_mat=side, holes=(D.WALKWAY,))
    shell.floor(fl, *D.WEST_WING[:4], D.Z3, carpet, side_mat=side, holes=(D.GRAND["hole"],))


def _tiers(c):
    """ブックギャラリーの段（L 字）と、2F の帯の下を埋める塊。"""
    fl = c.floor
    top, side = "library_carpet_brown", "library_wood_slat"
    for _name, z, (ex0, ex1), (ny0, ny1) in D.TIERS:
        kit.box(fl, ex0, D.TIER_Y0, 0.0, ex1, ny0, z, side, top=top)
        kit.box(fl, -9.0, ny0, 0.0, ex1, ny1, z, side, top=top)
    zt = D.C1 + 0.28
    fl.add_prism([(D.TOP_EAST[0], D.TIER_Y0), (D.TOP_EAST[1], D.TIER_Y0),
                  (D.TOP_EAST[1], D.TOP_NORTH[0]), (D.TOP_EAST[0], D.TOP_NORTH[0])],
                 0.0, zt, side)
    fl.add_prism([(-9.0, D.TOP_NORTH[0]), (D.X_P, D.TOP_NORTH[0]),
                  (D.X_P, D.TOP_NORTH[1]), (-9.0, D.TOP_NORTH[1])], 0.0, zt, side)


def _liner(mb, hole, z0, z1, mat=PART_MAT):
    """天井の穴の周りの見付（穴の内側を向く）。"""
    hx0, hy0, hx1, hy1 = hole
    ring = [(hx0, hy0), (hx0, hy1), (hx1, hy1), (hx1, hy0)]
    for k in range(4):
        kit.vplate(mb, ring[k], ring[(k + 1) % 4], z0, z1, mat)


def _ceilings(c):
    w = c.wall
    shell.ceiling(w, -D.X_P, D.Y_F, D.X_P, 60.0, D.C1, "library_ceiling_wood_dark",
                  holes=(D.ATRIUM,))
    grand = D.GRAND["hole"]
    shell.ceiling(w, D.X_W, D.Y_F, D.X_E2, 59.2, D.C2, "library_perforated_grey",
                  holes=(SLAT, STEEL, D.WALKWAY, grand))
    shell.ceiling(w, *SLAT, D.C2, "library_wood_slat")
    shell.ceiling(w, *STEEL, D.C2, "library_steel_underside")
    shell.ceiling(w, D.X_W, 59.2, -14.6, 60.6, D.C2, "library_perforated_grey")
    shell.ceiling(w, -14.6, 59.2, 15.5, 63.0, D.C2, "library_perforated_grey")
    (nx0, ny0), (nx1, ny1) = D.NOTCH
    shell.ceiling(w, D.X_E2, D.Y_F, D.X_E, ny0, D.C2, "library_perforated_grey")
    w.add_face([(nx1, ny1, D.C2), (nx0, ny0, D.C2), (nx1, ny0, D.C2)], "library_perforated_grey")
    _liner(w, grand, D.C2, D.Z3)
    _liner(w, D.WALKWAY, D.C2, D.Z3 - 0.22)
    # 3F ホワイエ・西翼（光庭の上は Z_WELL）
    white = "library_ceiling_white"
    shell.ceiling(w, D.X_W, D.Y_F, D.OMURA_X, D.WELL[1], D.C3, white)
    shell.ceiling(w, D.X_W, D.WELL[1], -D.X_P, D.WELL[3], D.C3, white)
    shell.ceiling(w, *D.WEST_WING[:4], D.C3, white)
    shell.ceiling(w, *D.WELL, D.Z_WELL, white)
    kit.vplate(w, (D.OMURA_X, D.WELL[1]), (-D.X_P, D.WELL[1]), D.C3, D.Z_WELL, IVORY)
    kit.vplate(w, (-D.X_P, D.WELL[1]), (-D.X_P, D.WELL[3]), D.C3, D.Z_WELL, IVORY)


def _nvoid_rail(c):
    """2F から北西の吹き抜けへ落ちないガラスの手すり。"""
    pts = [(D.X_W, 13.0), (-D.X_P, 13.0), (-D.X_P, D.Y_F)]
    shell.railing(c.wall, pts, D.Z2, h=1.15, mat="library_rail_white", glass=GLASS, post=3.0)


def _keep_out(z_ceil, hangs):
    """吊り看板の真上には照明を置かない（看板の吊り元が天井の面になるように）。"""
    return tuple((x - 1.6, y - 1.6, x + 1.6, y + 1.6) for x, y, zc in hangs
                 if abs(zc - z_ceil) < 1e-6)


def _lights(c, hangs):
    w = c.wall
    out1 = _keep_out(D.C1, hangs)
    # 1F: 細かいダウンライト
    pts = shell.ceiling_lights(w, -D.X_P, D.Y_F, D.X_P, 60.0, D.C1, sx=3.0, sy=3.0, w=0.3, l=0.3,
                               mat="light_panel", drop=0.02, holes=(D.ATRIUM,) + out1)
    c.lights_from(pts, D.C1, energy=120.0, step=3)
    out2 = _keep_out(D.C2, hangs)
    # 入口の上の木ルーバー: 丸いダウンライト
    pts = shell.ceiling_lights(w, *SLAT, D.C2, sx=2.6, sy=2.4, w=0.3, l=0.3,
                               mat="light_panel", drop=0.02, holes=out2)
    c.lights_from(pts, D.C2, energy=200.0, step=2)
    # 2F: 白い格子の照明（吹き抜け・穴・吊り看板を避ける）
    holes2 = (D.NW_VOID, SLAT, STEEL, D.WALKWAY, D.GRAND["hole"]) + out2
    pts = shell.ceiling_lights(w, D.X_W, D.Y_F, D.X_E2, 59.2, D.C2, sx=5.0, sy=5.0, w=1.2,
                               l=0.6, mat="light_panel", holes=holes2)
    pts += shell.ceiling_lights(w, -14.6, 59.2, 15.5, 63.0, D.C2, sx=5.0, sy=3.8, w=1.2, l=0.6,
                                mat="light_panel", holes=out2)
    c.lights_from(pts, D.C2, energy=260.0, step=2)
    # 吹き抜けの東の天窓の帯
    kit.plate(w, D.OMURA_X, D.TIER_Y0, D.X_P, D.TOP_NORTH[0], D.C2 - 0.01, "library_skylight",
              flip=True)
    for y in (20.0, 30.0, 40.0):
        c.light(17.9, y, D.C2 - 0.4, 400.0, 2.0)
    # 3F ホワイエ・西翼
    pts = shell.ceiling_lights(w, D.X_W, D.Y_F, D.OMURA_X, D.WELL[1], D.C3, sx=4.0, sy=3.6,
                               w=0.3, l=0.3, mat="light_panel", drop=0.02)
    pts += shell.ceiling_lights(w, *D.WEST_WING[:4], D.C3, sx=4.0, sy=4.0, w=0.3, l=0.3,
                                mat="light_panel", drop=0.02)
    c.lights_from(pts, D.C3, energy=200.0, step=3)


def build(c, hangs=()):
    """躯体一式。hangs は吊り看板の吊り元 (x, y, z_ceil) の列（その真上の照明を抜く）。"""
    _outer(c)
    _partitions(c)
    _floors(c)
    _tiers(c)
    _ceilings(c)
    _nvoid_rail(c)
    _lights(c, hangs)
