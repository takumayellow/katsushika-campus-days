"""共創棟 2F のモール側の教室 C201〜C204（床 FL 5.4）。

モール側の外壁（窓の帯）から 2F の廊下の壁まで、東から西へ 4 室を並べる。各室は廊下の側に引き戸が 2 つ、
東の壁に緑の黒板と教卓、西を向いた長机と椅子の列。室の数と並び・教室の番号は spec §1-4、
机の数と並べ方は推定。

1 つの物に届く光源は 4 つまでなので（Unity の per-object light の上限）、室ごとに床・壁・家具を分ける:

    floor_kyoso_2f_c20N     床
    wall_kyoso_2f_c20N      天井・窓の帯・廊下の壁と扉・室の境の壁
    furn_kyoso_NN_c20N      黒板・教卓・机・椅子
    furn_kyoso_NN_corr_c20N 室の前の廊下のベンチ・ごみ箱・消火器（廊下の光を受ける）
    lamp_kyoso_2F_<n>       室の天井の点光源（2 つずつ）

家具は z=0 で組んで最後に Z2 へ持ち上げる。座標は建物のローカル。
"""

import random

from . import furniture as F, kit, shell
from . import plan_kyoso_dims as D

WALL = "wall_white"
GLASS = "glass_clear"
FLOOR = "kyoso_floor_carpet_grey"
FRAME = "metal_white"
WOOD = "desk_wood"
DESK_TOP = "desk_white"     # 白い長机（photo_index の教室の写真）

DOOR_W = 1.10
DOOR_H = 2.10
DOOR_IN = 1.2           # 室の端の壁から扉の中心まで
SILL = 1.3              # 窓の帯の腰壁（2F のスラブの下端から。床から 0.9 m）
HEADER = 0.6            # 窓の帯の垂れ壁
BOARD_W = 3.6
BOARD_Z = (0.9, 2.1)    # 黒板の下端と上端（床から）
DESK_W, DESK_D = 1.8, 0.45
ROW_DX = (2.4, 3.55, 4.7, 5.85)     # 東の壁から机の列までの距離
DESK_Y = (6.1, 8.6, 11.1)           # 机の中心の Y
CHAIR_DX = 0.5                       # 机の中心から椅子の中心まで（西へ）
CHAIR_DY = 0.45                      # 机の中心から 2 脚の椅子の中心まで
END_UNDER = 0.02        # 両端の壁の天端を天井の面から下げる量
CORRIDOR_FACE = D.ROOMS2_Y[1] + shell.PART * 0.5      # 廊下の壁の廊下の側の面の Y
FRAME_W = 0.07          # 扉の枠の見付け（shell.door）
SKIRT_H = 0.08          # 廊下の壁の足元の巾木の高さ
SKIRT_T = 0.012         # 巾木の厚み
SKIRT = "plastic_black"
BOARD2_W, BOARD2_H, BOARD2_Z = 2.4, 1.1, 1.0    # 扉の間の掲示板（幅・高さ・下端の床からの高さ）
CORRIDOR_SEED = 425     # 掲示の紙の揺れ（ctx の乱数を使うと他の部品の配置が変わる）
CLOCK_W, CLOCK_Z = 0.32, 2.3    # 黒板の上の時計（幅と下端の床からの高さ）
SCREEN_CASE = "metal_white"      # 黒板の上の巻き上げスクリーンの箱（写真が無いので推定）
SCREEN_DROP = (0.30, 0.18)       # スクリーンの箱の下端と上端（天井から下へ）
SCREEN_D = 0.14                  # スクリーンの箱の奥行き（壁から）
PROJ_DX = 3.0                    # 黒板の面からプロジェクターの中心まで（机の 2 列目の上）
PROJ_DROP = (0.50, 0.36)         # プロジェクターの本体の下端と上端（天井から下へ）
PROJ_LENS = "plastic_black"       # プロジェクターのレンズ
SHEET_DY = 0.45         # 黒板の端から時間割の紙の中心まで
SHEET_W, SHEET_Z = 0.30, (1.3, 1.72)    # 時間割の紙の幅と、下端・上端の床からの高さ
SHEET_MARGIN = 0.05      # 時間割の紙の下に敷くコルクの板の、紙からのはみ出し（白い壁と見分けるため）


def rooms():
    """(番号, 東の X, 西の X) を東から順に。"""
    xs = D.ROOMS2_X
    return [(name, xs[i], xs[i + 1]) for i, name in enumerate(D.ROOMS2_NAMES)]


def door_xs(xa, xb):
    """室の廊下の側の 2 つの扉の中心の X（前 = 黒板の側、後ろ）。"""
    return (xa + DOOR_IN, xb - DOOR_IN)


def _floor(c, key, xa, xb):
    mb = c.part("floor", key)
    y0, _ = D.ROOMS2_Y
    shell.floor(mb, xa, y0, xb, D.SLAB_A[1], D.Z2, FLOOR, thickness=D.Z2 - D.Z_SLAB,
                side_mat=WALL)


def _shell(c, key, xa, xb, first, last):
    """天井・照明・窓の帯・廊下の壁と扉・室の境（または端）の壁。"""
    mb = c.part("wall", key)
    y0, yw = D.ROOMS2_Y
    yf = c.spec.y_face
    # 両端の室は天井を端の壁の上まで延ばし、壁の天端を立てる床にしない（外周の穴の検査）
    ca = xa - D.ROOMS2_END if first else xa
    cb = xb + D.ROOMS2_END if last else xb
    shell.ceiling(mb, ca, y0, cb, yw, D.Z_CEIL2, "ceiling_white", grid=1.8)
    for y in (7.0, 11.4):
        shell.light_strip(mb, xa + 0.8, y, xb - 0.8, y, D.Z_CEIL2, "light_strip", w=0.2)
    # モール側の窓の帯（外面の線 -> 進行方向の左手が室内）
    shell.outer_wall(mb, (xa, yf), (xb, yf), D.Z_SLAB, D.Z_CEIL2, wall=WALL, glass=GLASS,
                     sill=SILL, header=HEADER, seg=1.8)
    # 廊下の側の壁と引き戸。口は上まで開くので、扉の上をふさぐ
    gaps = [(x - xa - DOOR_W * 0.5, x - xa + DOOR_W * 0.5) for x in door_xs(xa, xb)]
    shell.partition(mb, (xa, yw), (xb, yw), D.Z2, D.Z_CEIL2, WALL, gaps=gaps)
    h = shell.PART * 0.5
    base = len(mb.verts)
    for x in door_xs(xa, xb):
        kit.box(mb, x - DOOR_W * 0.5, yw - h, DOOR_H, x + DOOR_W * 0.5, yw + h,
                D.Z_CEIL2 - D.Z2, WALL)
        shell.door(mb, x, yw, 0.0, w=DOOR_W, h=DOOR_H, leaf=WOOD, frame=FRAME)
    kit.lift(mb, base, D.Z2)
    # 東の端の壁・室の境の壁・西の端の壁
    # 両端の壁はモール側の外面まで延ばし、外殻の窓の帯の端との隅をふさぐ
    zt = D.Z_CEIL2 - END_UNDER
    if first:
        kit.box(mb, xa - D.ROOMS2_END, yf, D.Z_SLAB, xa, D.SLAB_A[1], zt, WALL)
    if last:
        kit.box(mb, xb, yf, D.Z_SLAB, xb + D.ROOMS2_END, D.SLAB_A[1], zt, WALL)
    else:
        shell.partition(mb, (xb, y0), (xb, yw), D.Z2, D.Z_CEIL2, WALL)
    return mb


def skirt_runs(xa, xb):
    """廊下の壁の巾木の (西端, 東端) の X。扉の口と枠を抜く。"""
    cuts = sorted((x - DOOR_W * 0.5 - FRAME_W, x + DOOR_W * 0.5 + FRAME_W) for x in door_xs(xa, xb))
    runs, x0 = [], xa
    for a, b in cuts:
        runs.append((x0, a))
        x0 = b
    runs.append((x0, xb))
    return runs


def _side(ang):
    """廊下の側の向き ang（FACE_PARK = +Y、FACE_MALL = -Y）を Y の符号にする。"""
    return 1.0 if ang == D.FACE_PARK else -1.0


def _wall_t(x, y, z, ang):
    """壁の面 (x, y) に付く小物の座標。ローカルの +Y が廊下の側。"""
    return kit.T(x, y, z, ang)


def wall_notice_board(mb, t, rng, sheets=4):
    """コルクの掲示板と貼り紙。壁に付く面と裏は作らない（屋内の三角形の予算）。"""
    hw, h = BOARD2_W * 0.5, BOARD2_H
    t.box_open(mb, -hw, 0.0, 0.0, hw, 0.05, h, WOOD, skip=("-y",))
    t.vplate(mb, hw - 0.06, 0.051, -hw + 0.06, 0.051, 0.06, h - 0.06, "fabric_beige")
    for i in range(sheets):
        cx = -hw + 0.35 + (BOARD2_W - 0.7) * i / max(1, sheets - 1) + rng.uniform(-0.04, 0.04)
        cz = h * 0.5 + rng.uniform(-0.12, 0.12)
        t.vplate(mb, cx + 0.15, 0.053, cx - 0.15, 0.053, cz - 0.21, cz + 0.21, "paper_white")


def corridor_wall(mb, xa, xb, z, rng, y=CORRIDOR_FACE, ang=D.FACE_PARK):
    """廊下の側の壁の面 y の巾木と、2 つの扉の間の掲示板（室の壁の部品 mb に入れる。2F・3F で共有）。"""
    xc = (xa + xb) * 0.5
    s = _side(ang)
    t = _wall_t(xc, y, 0.0, ang)
    base = len(mb.verts)
    for a, b in skirt_runs(xa, xb):
        la, lb = sorted((s * (a - xc), s * (b - xc)))
        t.vplate(mb, lb, SKIRT_T, la, SKIRT_T, 0.0, SKIRT_H, SKIRT)
    wall_notice_board(mb, _wall_t(xc, y, BOARD2_Z, ang), rng)
    kit.lift(mb, base, z)


def wall_extinguisher(mb, t):
    """壁際の床の消火器と、上の赤い表示の板。"""
    t.box_open(mb, -0.08, 0.0, 0.0, 0.08, 0.16, 0.62, "fire_red", skip=("-y",))
    t.vplate(mb, 0.12, 0.003, -0.12, 0.003, 1.55, 1.75, "fire_red")


def wall_trash_bins(mb, t, n=3, mats=("plastic_white", "fm_blue", "chair_green")):
    """壁に寄せた分別のごみ箱。上に黒い投入口の帯。"""
    for i in range(n):
        cx = (i - (n - 1) * 0.5) * 0.46
        t.box_open(mb, cx - 0.21, 0.0, 0.0, cx + 0.21, 0.42, 0.90, mats[i % len(mats)], skip=("-y",))
        t.vplate(mb, cx + 0.15, 0.421, cx - 0.15, 0.421, 0.78, 0.86, "plastic_black")


def corridor_props(mb, kind, xa, xb, z, y=CORRIDOR_FACE, ang=D.FACE_PARK):
    """室の前の廊下の小物。掲示板の下にベンチ（kind="bench"）かごみ箱（"bins"）、前の扉の脇に消火器。"""
    s = _side(ang)
    base = len(mb.verts)
    xc = (xa + xb) * 0.5
    if kind == "bench":
        F.bench(mb, xc, y + s * 0.3, ang, w=1.8, mat="kyoso_wood_oak")
    elif kind == "bins":
        wall_trash_bins(mb, _wall_t(xc, y, 0.0, ang))
    wall_extinguisher(mb, _wall_t(xa + 0.4, y, 0.0, ang))
    kit.lift(mb, base, z)


CORRIDOR_KINDS = (None, "bench", "bins", "bench")    # C201〜C204 の前の廊下の小物


def east_face(xa, first):
    """室の東の壁の室内の側の面の X。端の壁は xa で終わり、室の境の壁は xa を中心に厚みを持つ。"""
    return xa if first else xa + shell.PART * 0.5


def board(mb, xf, yc):
    """東の壁の面 xf に付く緑の黒板と枠・チョークの受け（z=0 で組む）。"""
    t = kit.T(xf, yc, 0.0, D.FACE_WEST)       # ローカルの +Y が室の側（西）
    hw = BOARD_W * 0.5
    z0, z1 = BOARD_Z
    f = 0.06
    t.box_nb(mb, -hw - f, 0.0, z0 - f, hw + f, 0.03, z1 + f, "metal_gray")
    t.vplate(mb, hw, 0.031, -hw, 0.031, z0, z1, "board_green")
    t.box_nb(mb, -hw, 0.03, z0 - f, hw, 0.12, z0 - f + 0.04, "metal_gray")
    board_extras(mb, t)


def board_extras(mb, t):
    """黒板の上の壁の時計と、黒板の脇の時間割の紙。壁に付く面は作らない（屋内の三角形の予算）。"""
    h = CLOCK_W * 0.5
    z0, z1 = CLOCK_Z, CLOCK_Z + CLOCK_W
    zc = (z0 + z1) * 0.5
    t.box_open(mb, -h, 0.0, z0, h, 0.04, z1, "plastic_black", skip=("-y",))
    t.vplate(mb, h - 0.025, 0.041, -h + 0.025, 0.041, z0 + 0.025, z1 - 0.025, "plastic_white")
    t.vplate(mb, 0.008, 0.043, -0.008, 0.043, zc, zc + 0.11, "plastic_black")     # 長針
    t.vplate(mb, 0.0, 0.044, -0.08, 0.044, zc - 0.006, zc + 0.006, "plastic_black")  # 短針
    x = -BOARD_W * 0.5 - SHEET_DY
    w, m = SHEET_W * 0.5, SHEET_MARGIN
    t.vplate(mb, x + w + m, 0.003, x - w - m, 0.003, SHEET_Z[0] - m, SHEET_Z[1] + m, "fabric_beige")
    t.vplate(mb, x + w, 0.006, x - w, 0.006, SHEET_Z[0], SHEET_Z[1], "paper_white")


def screen_case(mb, xf, yc, ceil):
    """東の壁の面 xf の黒板の上、天井の下に付く巻き上げスクリーンの箱（z=0 で組む）。"""
    t = kit.T(xf, yc, 0.0, D.FACE_WEST)
    hw = BOARD_W * 0.5 + 0.1
    t.box(mb, -hw, 0.0, ceil - SCREEN_DROP[0], hw, SCREEN_D, ceil - SCREEN_DROP[1], SCREEN_CASE)


def ceiling_projector(mb, x, yc, ceil):
    """天井から竿で吊った、東の黒板を向くプロジェクター（z=0 で組む）。ローカルの +Y が東。"""
    t = kit.T(x, yc, 0.0, D.FACE_EAST)
    z0, z1 = ceil - PROJ_DROP[0], ceil - PROJ_DROP[1]
    t.box(mb, -0.2, -0.15, z0, 0.2, 0.15, z1, "plastic_white")
    t.vplate(mb, 0.05, 0.151, -0.05, 0.151, z0 + 0.02, z0 + 0.12, PROJ_LENS)  # 本体の東の面のすぐ前
    t.box_open(mb, -0.025, -0.025, z1, 0.025, 0.025, ceil, "metal_gray", skip=("top",))


def classroom_furniture(mb, xa, first, desk_ys, z, ceil):
    """東の壁の黒板と教卓、西を向いた長机と椅子、天井のプロジェクターとスクリーンの箱を組んで
    高さ z へ持ち上げる（2F・3F・4F の教室で共有）。ceil は床から天井まで。"""
    base = len(mb.verts)
    yc = (desk_ys[0] + desk_ys[-1]) * 0.5
    xf = east_face(xa, first)
    board(mb, xf, yc)
    screen_case(mb, xf, yc, ceil)
    ceiling_projector(mb, xf + PROJ_DX, yc, ceil)
    F.podium(mb, xa + 1.4, yc, D.FACE_WEST)
    for dx in ROW_DX:
        x = xa + dx
        for y in desk_ys:
            F.long_desk(mb, x, y, D.FACE_EAST, w=DESK_W, d=DESK_D, top=DESK_TOP)
            for sy in (-CHAIR_DY, CHAIR_DY):
                F.chair_canteen(mb, kit.T(x + CHAIR_DX, y + sy, 0.0, D.FACE_EAST),
                                mat="chair_grey")
    kit.lift(mb, base, z)


def lamp_points(xa, xb):
    """室の天井の点光源の位置（室の中央の X、窓の側と廊下の側）。"""
    xc = (xa + xb) * 0.5
    return [(xc, 7.0), (xc, 11.5)]


def build(c):
    """4 室を組む。戻り値は室の点光源の位置（Z を含まない）。"""
    pts = []
    rng = random.Random(CORRIDOR_SEED)
    rs = rooms()
    for i, (name, xa, xb) in enumerate(rs):
        key = "2f_" + name.lower()
        _floor(c, key, xa, xb)
        wall = _shell(c, key, xa, xb, first=i == 0, last=i == len(rs) - 1)
        corridor_wall(wall, xa, xb, D.Z2, rng)
        classroom_furniture(c.furn(name.lower()), xa, i == 0, DESK_Y, D.Z2,
                            D.Z_CEIL2 - D.Z2)
        corridor_props(c.furn("corr_" + name.lower()), CORRIDOR_KINDS[i], xa, xb, D.Z2)
        pts.extend(lamp_points(xa, xb))
    return pts
