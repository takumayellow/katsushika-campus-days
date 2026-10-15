"""共創棟 3F: 吹き抜けに面したガラス張りの教室 2 室（歩けない。1F・2F から見上げて見える所だけ作る）。

3F の吹き抜けの側がガラスの教室の列であることは写真で確定（spec §1-5）。室の奥行きと 2 室の境は推定。
吹き抜けの奥行きでは、1F から見上げても室の床と机は縁の立ち上がりに隠れるので、天井・照明・壁だけを置く。
座標は建物のローカル。
"""

from . import kit, shell
from . import plan_kyoso_dims as D

WALL = "wall_white"
GLASS = "glass_clear"
FRAME = "metal_dark"
MULLION = 1.8           # ガラスの面の方立ての間隔の目安（幅を等分する）
GLASS_Y = 0.115         # 吹き抜けの縁の線から見たガラスの面の Y（2F の縁のガラスと同じ線）


def shell_3f(c, mb):
    """床・天井・奥と両脇の壁・2 室の境の壁・柱・吹き抜けに面したガラスの面・照明。"""
    x0, y0, x1, y1 = D.ROOMS3
    shell.floor(mb, x0, y0, x1, y1, D.Z3, "kyoso_floor_carpet_grey", thickness=0.0)
    shell.ceiling(mb, x0, y0, x1, y1, D.Z_CEIL3, "ceiling_white")
    kit.box(mb, x0, y0 - 0.3, D.Z3, x1, y0, D.Z_CEIL3, WALL)
    for xa, xb in ((x0 - 0.3, x0), (x1, x1 + 0.3)):
        kit.box(mb, xa, y0 - 0.3, D.Z_SLAB3, xb, y1, D.Z_TOP, WALL)
    sx = D.ROOMS3_SPLIT
    kit.box(mb, sx - 0.06, y0, D.Z3, sx + 0.06, y1, D.Z_CEIL3, WALL)
    h = D.COLUMN_SIZE * 0.5
    for x, y in D.COLUMNS3:
        kit.box(mb, x - h, y - h, D.Z3, x + h, y + h, D.Z_CEIL3, "kyoso_column_pale")
    _glass_face(mb, x0, x1, y1 + GLASS_Y)
    for y in (y0 + 2.0, y0 + 4.2, y1 - 1.2):
        for a, b in ((x0, sx), (sx, x1)):
            shell.light_strip(mb, a + 0.8, y, b - 0.8, y, D.Z_CEIL3, "light_strip", w=0.2)


def _glass_face(mb, x0, x1, y):
    """縁の立ち上がりの上から天井までのガラスと、方立て・上の枠。"""
    z0, z1 = D.RIM3_TOP, D.Z_CEIL3
    kit.box(mb, x0, y - 0.015, z0, x1, y + 0.015, z1, GLASS)
    kit.box(mb, x0, y - 0.04, z1 - 0.08, x1, y + 0.04, z1, FRAME)
    n = int(round((x1 - x0) / MULLION))
    for k in range(1, n):
        x = x0 + (x1 - x0) * k / n
        kit.box_nb(mb, x - 0.025, y - 0.04, z0, x + 0.025, y + 0.04, z1, FRAME)
