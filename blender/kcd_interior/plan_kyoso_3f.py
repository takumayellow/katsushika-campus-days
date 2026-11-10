"""共創棟 3F: 吹き抜けに面したガラス張りの教室 2 室と、その奥の廊下（床 FL 9.6）。

3F の吹き抜けの側がガラスの教室の列であることは写真で確定（spec §1-5）。室の奥行き・2 室の境・廊下の幅、
モール側を閉じた扉の並ぶ壁にしたこと、机の数と並べ方は推定。東のコアの西の面にある階段室の扉
（1F・2F・3F。stair_doors）で上り下りする。

1 つの物に届く光源は 4 つまでなので（Unity の per-object light の上限）、室と廊下で床・壁・家具を分ける:

    floor_kyoso_3f_<室> / wall_kyoso_3f_<室> / furn_kyoso_NN_3f_<室>   室は east・west（東から）
    floor_kyoso_3f_corr / wall_kyoso_3f_corr                       廊下
    furn_kyoso_NN_3f_corr_<室>                                     室の前の廊下の小物（ベンチ・ごみ箱・消火器）
    st_kyoso_core_<階> / _f                                        階段室の扉の前と、そこから 1 m 正面

座標は建物のローカル。
"""

import random

from . import kit, shell
from . import plan_kyoso_2f_rooms as R2
from . import plan_kyoso_dims as D
from . import plan_kyoso_shell as SH

WALL = "wall_white"
GLASS = "glass_clear"
FRAME = "metal_dark"
FLOOR = "kyoso_floor_carpet_grey"
MULLION = 1.8           # ガラスの面の方立ての間隔の目安（幅を等分する）
GLASS_Y = 0.115         # 吹き抜けの縁の線から見たガラスの面の Y（2F の縁のガラスと同じ線）
WALL_T = 0.3            # 3F の外周の壁の厚み（閉じの検査の wall_thickness と同じ）
SPLIT_T = 0.12          # 2 室の境の壁の厚み

CORR = (D.CORE_E[2], D.SLAB_B[1], D.ROOMS3[2], D.ROOMS3[1] - WALL_T)   # 廊下の内側（東・モール側・西・室の側）
CORR_YC = (CORR[1] + CORR[3]) * 0.5
MALL_DOORS = (-3.4, 3.0, 9.2)           # モール側の壁の閉じた扉の X（入れない部屋）
STAIR_DOOR_Y = {"1F": 27.8, "2F": 27.8, "3F": CORR_YC}    # 階段室の扉の中心の Y（東のコアの西の面）
STAIR_OUT = 1.0         # 扉の面から着いた所まで（Unity の InteriorStairStage.StairOut）
FACE_OUT = 1.0          # 着いた所から、向きを示す Empty（_f）まで
SIGN_OUT = 0.3          # 避難誘導灯を壁の面から出す距離
SIGN_DROP = 0.26        # 天井から避難誘導灯の上端まで

CORRIDOR_KINDS = ("bins", "bench")      # 東・西の室の前の廊下の小物（R2.corridor_props）
DESK_Y = (23.4, 25.9)     # 机の中心の Y（窓の側の柱の前で止める。2 行）


def rooms():
    """(名前, 東の X, 西の X) を東から順に。"""
    x0, _, x1, _ = D.ROOMS3
    return [("east", x0, D.ROOMS3_SPLIT), ("west", D.ROOMS3_SPLIT, x1)]


def door_xs(xa, xb):
    """室の廊下の側の 2 つの引き戸の中心の X（前 = 黒板の側、後ろ）。"""
    return (xa + R2.DOOR_IN, xb - R2.DOOR_IN)


def east_face(xa, first):
    """室の東の壁の室内の側の面の X。東の室は外周の壁の面、西の室は境の壁の面。"""
    return xa if first else xa + SPLIT_T * 0.5


def _glass_face(mb, x0, x1, y):
    """縁の立ち上がりの上から天井までのガラスと、方立て・上の枠。"""
    z0, z1 = D.RIM3_TOP, D.Z_CEIL3
    kit.box(mb, x0, y - 0.015, z0, x1, y + 0.015, z1, GLASS)
    kit.box(mb, x0, y - 0.04, z1 - 0.08, x1, y + 0.04, z1, FRAME)
    n = max(1, int(round((x1 - x0) / MULLION)))
    for k in range(1, n):
        x = x0 + (x1 - x0) * k / n
        kit.box_nb(mb, x - 0.025, y - 0.04, z0, x + 0.025, y + 0.04, z1, FRAME)


def _room_shell(c, key, xa, xb, first, last):
    """天井・照明・吹き抜けに面したガラス・廊下の側の壁と引き戸・境と両脇の壁・柱。戻り値は室の壁の部品。"""
    mb = c.part("wall", "3f_" + key)
    _, y0, _, y1 = D.ROOMS3
    shell.ceiling(mb, xa, y0, xb, y1, D.Z_CEIL3, "ceiling_white")
    if first:
        kit.box(mb, xa - WALL_T, y0 - WALL_T, D.Z_SLAB3, xa, y1, D.Z_TOP, WALL)
    if last:
        kit.box(mb, xb, y0 - WALL_T, D.Z_SLAB3, xb + WALL_T, y1, D.Z_TOP, WALL)
    else:
        kit.box(mb, xb - SPLIT_T * 0.5, y0, D.Z3, xb + SPLIT_T * 0.5, y1, D.Z_CEIL3, WALL)
    # 廊下の側の壁と引き戸。口は上まで開くので、扉の上をふさぐ
    yw = y0 - WALL_T * 0.5
    gaps = [(x - xa - R2.DOOR_W * 0.5, x - xa + R2.DOOR_W * 0.5) for x in door_xs(xa, xb)]
    shell.partition(mb, (xa, yw), (xb, yw), D.Z3, D.Z_CEIL3, WALL, thick=WALL_T, gaps=gaps)
    base = len(mb.verts)
    for x in door_xs(xa, xb):
        kit.box(mb, x - R2.DOOR_W * 0.5, y0 - WALL_T, R2.DOOR_H, x + R2.DOOR_W * 0.5, y0,
                D.Z_CEIL3 - D.Z3, WALL)
        shell.door(mb, x, yw, 0.0, w=R2.DOOR_W, h=R2.DOOR_H, leaf=R2.WOOD, frame=R2.FRAME)
    kit.lift(mb, base, D.Z3)
    h = D.COLUMN_SIZE * 0.5
    for x, y in D.COLUMNS3:
        if xa < x < xb:
            kit.box(mb, x - h, y - h, D.Z3, x + h, y + h, D.Z_CEIL3, "kyoso_column_pale")
    _glass_face(mb, xa, xb, y1 + GLASS_Y)
    for y in (y0 + 2.0, y0 + 4.2, y1 - 1.2):
        shell.light_strip(mb, xa + 0.8, y, xb - 0.8, y, D.Z_CEIL3, "light_strip", w=0.2)
    return mb


def _corridor(c):
    """廊下の床・天井・照明、モール側の壁と閉じた扉、両端の壁。戻り値は廊下の壁の部品。"""
    cx0, cy0, cx1, cy1 = CORR
    shell.floor(c.part("floor", "3f_corr"), cx0, cy0, cx1, cy1, D.Z3, FLOOR, thickness=0.0)
    mb = c.part("wall", "3f_corr")
    shell.ceiling(mb, cx0, cy0, cx1, cy1, D.Z_CEIL3, "ceiling_white")
    shell.light_strip(mb, cx0 + 0.8, CORR_YC, cx1 - 0.8, CORR_YC, D.Z_CEIL3, "light_strip", w=0.2)
    kit.box(mb, cx0 - WALL_T, cy0 - WALL_T, D.Z3, cx1 + WALL_T, cy0, D.Z_CEIL3, WALL)       # モール側
    kit.box(mb, cx0 - WALL_T, cy0, D.Z3, cx0, cy1 + WALL_T, D.Z_CEIL3, WALL)           # 東の端
    kit.box(mb, cx1, cy0, D.Z3, cx1 + WALL_T, cy1, D.Z_CEIL3, WALL)               # 西の端
    # 東の室の東の壁より東の、室の側の壁
    kit.box(mb, cx0, cy1, D.Z3, D.ROOMS3[0] - WALL_T, cy1 + WALL_T, D.Z_CEIL3, WALL)
    base = len(mb.verts)
    for x in MALL_DOORS:
        SH.door(mb, x, cy0, D.FACE_PARK)
    kit.lift(mb, base, D.Z3)
    return mb


def corridor_lamps():
    """廊下の天井の点光源の位置（X, Y）。"""
    return tuple((x, CORR_YC) for x in MALL_DOORS)


def stair_doors(c, corr):
    """東のコアの西の面の階段室の閉じた扉と避難誘導灯、扉の前の Empty を 1F・2F・3F に置く。"""
    x = D.CORE_E[2]
    floors = (("1F", 0.0, D.Z_CEIL), ("2F", D.Z2, D.Z_CEIL2), ("3F", D.Z3, D.Z_CEIL3))
    for floor, z, ceil in floors:
        y = STAIR_DOOR_Y[floor]
        mb = corr if floor == "3F" else c.wall
        base = len(mb.verts)
        SH.door(mb, x, y, D.FACE_WEST)
        kit.lift(mb, base, z)
        shell.exit_sign(mb, x + SIGN_OUT, y, ceil - SIGN_DROP, D.FACE_WEST)
        name = "st_%s_core_%s" % (c.spec.id, floor)
        c._put(name, x + STAIR_OUT, y, z)
        c._put(name + "_f", x + STAIR_OUT + FACE_OUT, y, z)


def zone():
    """閉じの検査（closure）に渡す 3F の外周。東の室の東の壁より東の、室の側の角を欠いた L 字。"""
    cx0, cy0, cx1, _ = CORR
    y_back = D.ROOMS3[3] + WALL_T
    return {"x0": round(cx0 - WALL_T, 3), "x1": round(cx1 + WALL_T, 3), "y_face": round(cy0 - WALL_T, 3),
            "y_back": round(y_back, 3),
            "notch": [round(cx0 - WALL_T, 3), round(D.ROOMS3[1], 3), round(D.ROOMS3[0] - WALL_T, 3),
                      round(y_back, 3)],
            "z_min": round(D.Z3 - 0.5, 3), "z_max": round(D.Z3 + 0.5, 3)}


def build(c):
    """2 室と廊下、1F〜3F の階段室の扉を組む。戻り値は室の点光源の位置（Z を含まない）。"""
    pts = []
    rng = random.Random(R2.CORRIDOR_SEED + 1)
    rs = rooms()
    for i, (key, xa, xb) in enumerate(rs):
        shell.floor(c.part("floor", "3f_" + key), xa, CORR[3], xb, D.ROOMS3[3], D.Z3, FLOOR,
                    thickness=0.0)
        wall = _room_shell(c, key, xa, xb, first=i == 0, last=i == len(rs) - 1)
        R2.corridor_wall(wall, xa, xb, D.Z3, rng, y=CORR[3], ang=D.FACE_MALL)
        R2.classroom_furniture(c.furn("3f_" + key), xa, i == 0, DESK_Y, D.Z3)
        R2.corridor_props(c.furn("3f_corr_" + key), CORRIDOR_KINDS[i], xa, xb, D.Z3,
                          y=CORR[3], ang=D.FACE_MALL)
        pts.append(((xa + xb) * 0.5, (D.ROOMS3[1] + D.ROOMS3[3]) * 0.5))
    stair_doors(c, _corridor(c))
    c.closure_zones.append(zone())
    return pts
