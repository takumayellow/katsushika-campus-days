"""共創棟 2F のモール側の教室 C201〜C204（床 FL 5.4）。

モール側の外壁（窓の帯）から 2F の廊下の壁まで、東から西へ 4 室を並べる。各室は廊下の側に引き戸が 2 つ、
東の壁に緑の黒板と教卓、西を向いた長机と椅子の列。室の数と並び・教室の番号は spec §1-4、
机の数と並べ方は推定。

1 つの物に届く光源は 4 つまでなので（Unity の per-object light の上限）、室ごとに床・壁・家具を分ける:

    floor_kyoso_2f_c20N     床
    wall_kyoso_2f_c20N      天井・窓の帯・廊下の壁と扉・室の境の壁
    furn_kyoso_NN_c20N      黒板・教卓・机・椅子
    lamp_kyoso_2F_<n>       室の天井の点光源（2 つずつ）

家具は z=0 で組んで最後に Z2 へ持ち上げる。座標は建物のローカル。
"""

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


def _board(mb, xa, yc):
    """東の壁の緑の黒板と枠・チョークの受け（z=0 で組む）。"""
    t = kit.T(xa, yc, 0.0, D.FACE_WEST)       # ローカルの +Y が室の側（西）
    hw = BOARD_W * 0.5
    z0, z1 = BOARD_Z
    f = 0.06
    t.box_nb(mb, -hw - f, 0.0, z0 - f, hw + f, 0.03, z1 + f, "metal_gray")
    t.vplate(mb, hw, 0.031, -hw, 0.031, z0, z1, "board_green")
    t.box_nb(mb, -hw, 0.03, z0 - f, hw, 0.12, z0 - f + 0.04, "metal_gray")


def _furniture(c, key, xa):
    mb = c.furn(key)
    base = len(mb.verts)
    yc = (DESK_Y[0] + DESK_Y[-1]) * 0.5
    _board(mb, xa, yc)
    F.podium(mb, xa + 1.4, yc, D.FACE_WEST)
    for dx in ROW_DX:
        x = xa + dx
        for y in DESK_Y:
            F.long_desk(mb, x, y, D.FACE_EAST, w=DESK_W, d=DESK_D, top=DESK_TOP)
            for sy in (-CHAIR_DY, CHAIR_DY):
                F.chair_canteen(mb, kit.T(x + CHAIR_DX, y + sy, 0.0, D.FACE_EAST),
                                mat="chair_grey")
    kit.lift(mb, base, D.Z2)


def lamp_points(xa, xb):
    """室の天井の点光源の位置（室の中央の X、窓の側と廊下の側）。"""
    xc = (xa + xb) * 0.5
    return [(xc, 7.0), (xc, 11.5)]


def build(c):
    """4 室を組む。戻り値は室の点光源の位置（Z を含まない）。"""
    pts = []
    rs = rooms()
    for i, (name, xa, xb) in enumerate(rs):
        key = "2f_" + name.lower()
        _floor(c, key, xa, xb)
        _shell(c, key, xa, xb, first=i == 0, last=i == len(rs) - 1)
        _furniture(c, name.lower(), xa)
        pts.extend(lamp_points(xa, xb))
    return pts
