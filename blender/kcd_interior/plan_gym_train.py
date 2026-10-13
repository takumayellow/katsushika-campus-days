"""2F のトレーニングルーム（マシン・トレッドミル・パワーラック・柱・空調）。"""

from . import kit, shell
from . import plan_gym_dims as D

Z0 = D.Z2


def _machine(mb, x, y, face):
    """ウェイトマシン 1 台。face=+1 で +Y を向く。"""
    kit.box(mb, x - 0.45, y - 0.7, Z0, x + 0.45, y + 0.7, Z0 + 0.08, "gym_rubber_black")
    kit.box(mb, x - 0.2, y - 0.6 * face - 0.1, Z0 + 0.08, x + 0.2, y - 0.6 * face + 0.1, Z0 + 1.7,
            "gym_machine_silver")
    kit.box(mb, x - 0.2, y - 0.5 * face - 0.2, Z0 + 0.12, x + 0.2, y - 0.5 * face + 0.2, Z0 + 1.2,
            "metal_dark")
    kit.box(mb, x - 0.22, y + 0.1 * face - 0.22, Z0 + 0.4, x + 0.22, y + 0.1 * face + 0.22,
            Z0 + 0.48, "plastic_black")
    kit.box(mb, x - 0.2, y - 0.1 * face - 0.1, Z0 + 0.48, x + 0.2, y - 0.1 * face + 0.1,
            Z0 + 1.0, "plastic_black")


def _treadmill(mb, x, y):
    """東の壁ぎわのトレッドミル（正面が -X）。"""
    kit.box(mb, x - 0.85, y - 0.4, Z0, x + 0.85, y + 0.4, Z0 + 0.22, "gym_machine_silver")
    kit.box(mb, x - 0.8, y - 0.3, Z0 + 0.22, x + 0.8, y + 0.3, Z0 + 0.26, "gym_rubber_black")
    for s in (-1, 1):
        kit.box(mb, x - 0.85 - 0.0, y + s * 0.37 - 0.02, Z0 + 0.22, x - 0.7, y + s * 0.37 + 0.02,
                Z0 + 1.2, "metal_dark")
    kit.box(mb, x - 0.9, y - 0.35, Z0 + 1.1, x - 0.7, y + 0.35, Z0 + 1.3, "plastic_black")


def equipment(c):
    mb = c.furn("train")
    for x in (3.4, 5.4, 7.4):
        _machine(mb, x, 12.9, 1)
        _machine(mb, x, 28.6, -1)
    for y in (22.9, 23.9, 24.9, 25.9):
        _treadmill(mb, 7.6, y)
    for y in (12.9, 19.0):
        kit.box(mb, 7.3, y - 0.3, Z0, 7.9, y + 0.7, Z0 + 0.35, "gym_machine_silver")
        kit.box(mb, 7.5, y - 0.2, Z0 + 0.35, 7.7, y - 0.1, Z0 + 1.6, "gym_machine_silver")
    for x, y in ((2.7, 24.5), (3.5, 24.5), (2.7, 26.5), (3.5, 26.5)):
        kit.box(mb, x - 0.03, y - 0.03, Z0, x + 0.03, y + 0.03, Z0 + 2.3, "gym_rail_metal")
    kit.tube(mb, (2.7, 24.5, Z0 + 1.4), (2.7, 26.5, Z0 + 1.4), 0.025, "gym_rail_metal", seg=4)
    kit.tube(mb, (3.5, 24.5, Z0 + 1.4), (3.5, 26.5, Z0 + 1.4), 0.025, "gym_rail_metal", seg=4)
    # 空調と照明
    for x, y in ((4.0, 16.0), (7.0, 16.0), (4.0, 24.0), (7.0, 24.0)):
        kit.box(mb, x - 0.45, y - 0.45, D.C_2F - 0.2, x + 0.45, y + 0.45, D.C_2F, "ceiling_white")
        kit.box(mb, x - 0.3, y - 0.3, D.C_2F - 0.22, x + 0.3, y + 0.3, D.C_2F - 0.2, "gym_vent_dark")
    pts = shell.ceiling_lights(c.wall, 2.6, 12.3, 8.4, 29.3, D.C_2F - 0.01, sx=1.9, sy=4.2,
                               w=0.15, l=2.2, mat="gym_light_linear", limit=12)
    c.lights_from(pts, D.C_2F, energy=250.0, step=2, radius=3.0)
    # 床のマット
    kit.plate(c.floor, 2.3, 11.9, 8.5, 29.6, Z0 + 0.012, "gym_mat_dark")


def columns(c):
    """観覧席の後ろの丸柱（屋根を受ける）。"""
    mb = c.wall
    for y in D.COLS_Y:
        kit.cyl(mb, D.COL_X, y, D.Z2, D.C_HALL, 0.2, "gym_column_concrete", seg=10)


def build(c):
    equipment(c)
    columns(c)
