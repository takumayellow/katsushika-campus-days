"""用具庫・北の通路（アリーナの北側）。"""

from . import kit, shell
from . import plan_gym_dims as D
from . import plan_gym_shell as S


def passage(c):
    """アリーナ北東の通路（壁 B の戸口へ抜ける）と、その奥の閉じた空間を囲う壁。"""
    mb = c.wall
    S.ywall(mb, -7.1, -6.8, 39.3, 47.6, 0.0, D.C_STORE + 0.3, S.WHITE)
    # 奥の壁は暗い色にする。アリーナの W_N の開口から覗くと、写真（P1）のように暗く見える。
    S.xwall(mb, -6.8, -3.7, 41.7, 42.0, 0.0, D.C_PASS, S.DARK)
    pts = [(-5.25, 40.5)]
    c.lights_from(pts, D.C_PASS, energy=120.0, radius=2.5)
    kit.box(mb, -6.78, 39.5, 1.2, -6.7, 40.5, 1.6, "paper_white")


def storage(c):
    mb = c.furn("storage")
    # 跳び箱（中央のものは天端 1.10 m）
    for (x, y, w, d, h) in ((-20.0, 44.0, 1.2, 0.8, 1.10), (-24.0, 44.5, 1.2, 0.8, 0.80),
                            (-15.0, 45.0, 1.2, 0.8, 1.00)):
        kit.box(mb, x - w / 2, y - d / 2, 0.0, x + w / 2, y + d / 2, h - 0.12, "gym_box_green")
        kit.box(mb, x - w / 2, y - d / 2, h - 0.12, x + w / 2, y + d / 2, h, "gym_vault_top")
    # マット
    kit.box(mb, -27.0, 40.0, 0.0, -25.0, 43.0, 0.6, "gym_mat_dark")
    kit.box(mb, -27.0, 43.4, 0.0, -25.0, 45.2, 0.45, "gym_mat_dark")
    # ボールカート
    for k in range(4):
        x = -13.0 + 1.1 * k
        kit.box(mb, x, 46.0, 0.12, x + 0.8, 46.8, 0.95, "gym_cart_blue")
        kit.box(mb, x + 0.05, 46.05, 0.0, x + 0.15, 46.15, 0.12, "gym_rubber_black")
        kit.box(mb, x + 0.65, 46.05, 0.0, x + 0.75, 46.15, 0.12, "gym_rubber_black")
    # 北壁の棚
    kit.box(mb, -27.0, 47.05, 0.0, -21.0, 47.5, 2.1, "gym_shoe_rack_wood")
    for z in (0.7, 1.4):
        kit.box(mb, -27.0, 46.7, z, -21.0, 47.05, z + 0.04, "gym_joint_metal")
    # 得点板・平均台
    kit.box(mb, -10.0, 47.1, 0.0, -8.4, 47.25, 1.0, "wall_white")
    kit.box(mb, -10.0, 46.8, 0.0, -8.4, 46.9, 0.9, "gym_rubber_black")
    kit.box(mb, -17.0, 40.6, 0.38, -12.0, 40.9, 0.48, "gym_vault_top")
    for x in (-16.5, -12.5):
        kit.box(mb, x, 40.65, 0.0, x + 0.1, 40.85, 0.38, "gym_rail_metal")
    pts = shell.ceiling_lights(mb, -26.0, 41.0, -9.0, 47.0, D.C_STORE - 0.02, sx=5.0, sy=3.0,
                               w=0.3, l=1.5, mat="gym_light_linear", limit=8)
    c.lights_from(pts, D.C_STORE, energy=200.0, step=2, radius=3.0)


def build(c):
    passage(c)
    storage(c)
