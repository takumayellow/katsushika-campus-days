"""どの建物にも共通する躯体まわり（床・外周壁・入口・天井・サイン類）。"""

import math

from . import furniture as F
from . import kit, shell


def envelope(c, z_ceil, floor_mat="floor_tile_white", door_w=6.0,
             glass="glass_clear", sill=0.95, header=0.45, seg=3.0,
             solid_edges=(), wall="wall_white", z_top=None, ceil=True,
             ceil_mat="ceiling_white", grid=1.8, extra_gaps=None,
             floor_thick=0.30):
    """床スラブ + 外周壁 + 入口ガラススクリーン + 天井。

    戻り値は入口開口の (x0, x1)。
    """
    s = c.spec
    z1 = z_top if z_top is not None else z_ceil + 0.55
    ix0, iy0, ix1, iy1 = s.inner()
    # 床（躯体外形いっぱい。壁の下まで敷いて隙間を作らない）
    shell.floor(c.floor, s.x0, s.y_face, s.x1, s.y_back, 0.0, floor_mat,
                thickness=floor_thick, side_mat="concrete_grey")
    d0, d1 = -door_w * 0.5, door_w * 0.5
    # 入口の開口はガラススクリーンの高さまで。以前は全高が開口で、スクリーンの
    # 上が抜けたままだったので上階の床からそこへ出られた (#45)
    z_screen = min(3.2, z1 - 0.2)
    shell.perimeter(c.wall, s, 0.0, z1, wall=wall, glass=glass, sill=sill,
                    header=header, seg=seg, door_gap=(d0, d1),
                    door_top=z_screen, solid_edges=solid_edges,
                    extra_gaps=extra_gaps)
    shell.glass_entrance(c.wall, d0, d1, 0.0, z_screen,
                         s.y_face + shell.WALL * 0.5)
    if ceil:
        shell.ceiling(c.wall, ix0, iy0, ix1, iy1, z_ceil, ceil_mat, grid=grid)
    return (d0, d1)


def entry_kit(c, z_ceil, door_w=6.0, spawn_depth=1.5, bin_x=None):
    """入口まわりの定番（spawn / exit Empty・誘導灯・消火器・ゴミ箱・マット）。

    ゴミ箱は既定で扉の西どなりに置くが、そこに階段の上り口などが来る建物では
    bin_x（入口中心からのローカル x）でずらす。
    """
    s = c.spec
    y = s.y_face + shell.WALL
    c.spawn(0.0, y + spawn_depth, 0.0)
    c.exit(0.0, y + 0.25, 0.0)
    shell.exit_sign(c.wall, 0.0, y + 0.05, min(z_ceil - 0.05, 2.95), ang=math.pi)
    mb = c.furn("entry")
    kit.plate(mb, -door_w * 0.5, y, door_w * 0.5, y + 2.2, 0.012,
              "floor_carpet_grey")
    shell.fire_extinguisher(mb, door_w * 0.5 + 1.2, y + 0.45)
    bx = -door_w * 0.5 - 2.4 if bin_x is None else bin_x
    shell.trash_bins(mb, bx, y + 0.7, ang=math.pi, n=3)
    return mb


def sign_board(c, mb, x, y, z, ang=0.0, w=1.8, h=0.55):
    shell.wall_sign(mb, x, y, z, ang=ang, w=w, h=h)
    c.sign(x, y, z)


def corridor_run(c, mb, x0, x1, y, z_ceil, pitch=9.0, both=True):
    """廊下の定番設備を等間隔に置く（誘導灯・消火器・掲示板・ベンチ）。"""
    n = max(1, int((x1 - x0) / pitch))
    for i in range(n):
        cx = x0 + (x1 - x0) * (i + 0.5) / n
        if i % 2 == 0:
            shell.exit_sign(c.wall, cx, y, min(z_ceil - 0.05, 2.9))
        if i % 3 == 1:
            shell.fire_extinguisher(mb, cx, y - 0.9)
        if both and i % 3 == 2:
            F.bench(mb, cx, y - 1.0, ang=0.0, w=1.8)


def window_planters(c, mb, x0, x1, y, n=4):
    for i in range(n):
        cx = x0 + (x1 - x0) * (i + 0.5) / n
        shell.planter(mb, cx, y, r=0.40, h=0.44, leaf_h=1.4)
