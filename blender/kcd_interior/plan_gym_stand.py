"""観覧席（壁 B の上）・ギャラリー（アリーナ上空の U 字）・2F との境のガラス帯。"""

import math

from . import furniture as F
from . import kit, shell
from . import plan_gym_dims as D
from . import plan_gym_shell as S

LANE_HALF = 0.6          # 階段レーンの幅（中心から片側）
FRONT = 0.45             # 階段レーンで下げる段の前縁の幅
STEP_DROP = 0.21         # 階段レーンの蹴上（段の高さの半分）
VENT_R = 0.25            # ギャラリーの鼻先の丸い吹出口の半径
VENT_STEP = 1.55         # 吹出口の間隔
VENT_Z = 4.3             # 吹出口の中心の高さ
VENT_SEG = 12
VENT_END = 0.6           # 辺の端から最初の吹出口の中心までの最小の距離


def _intervals():
    """観覧席を階段レーンで区切った (y0, y1, レーンか) のリスト。"""
    out, y = [], D.STAND_Y[0]
    for s in D.STAIRS_Y:
        out.append((y, s - LANE_HALF, False))
        out.append((s - LANE_HALF, s + LANE_HALF, True))
        y = s + LANE_HALF
    out.append((y, D.STAND_Y[1], False))
    return out


def tiers(c):
    mb = c.wall
    top_mat, side = "gym_stand_concrete", "gym_concrete_nose"
    kit.box(mb, -3.7, D.STAND_Y[0], 2.7, -3.0, D.STAND_Y[1], D.Z_FRONT, side, top="gym_floor_grey")
    for x0, x1, top in D.TIERS:
        for y0, y1, lane in _intervals():
            if lane:
                kit.box(mb, x0, y0, 2.7, x0 + FRONT, y1, top - STEP_DROP, side, top=top_mat)
                kit.box(mb, x0 + FRONT, y0, 2.7, x1, y1, top, side, top=top_mat)
            else:
                kit.box(mb, x0, y0, 2.7, x1, y1, top, side, top=top_mat)
    kit.box(mb, D.REAR_WALK[0], D.STAND_Y[0], 2.7, D.REAR_WALK[1], D.STAND_Y[1], D.Z2,
            side, top="gym_floor_grey")
    # 南北の端の通路（壁 B から 2F の床までつながる）
    for y0, y1 in D.END_WALKS:
        kit.box(mb, -3.7, y0, 2.7, 2.0, y1, D.Z2, side, top="gym_floor_grey")
    # 観覧席のベンチ
    mb2 = c.furn("stand")
    base = len(mb2.verts)
    for y in (15.0, 25.0, 30.0):
        F.bench(mb2, -1.6, y, math.pi / 2, 1.2, mat="gym_bench_wood", leg="gym_rail_metal")
    kit.lift(mb2, base, D.TIERS[1][2])


def _vents(mb, a, b, out):
    """鼻先の線 a -> b に沿って丸い吹出口を中央にそろえて並べる。out は鼻先の面の外向きの法線。"""
    L = math.hypot(b[0] - a[0], b[1] - a[1])
    u = ((b[0] - a[0]) / L, (b[1] - a[1]) / L)
    k = int((L - 2 * VENT_END) / VENT_STEP) + 1
    s0 = (L - (k - 1) * VENT_STEP) / 2
    for i in range(k):
        s = s0 + i * VENT_STEP
        cx = a[0] + u[0] * s + out[0] * 0.005
        cy = a[1] + u[1] * s + out[1] * 0.005
        ring = [(cx + VENT_R * math.cos(t) * u[0], cy + VENT_R * math.cos(t) * u[1],
                 VENT_Z + VENT_R * math.sin(t))
                for t in (2 * math.pi * j / VENT_SEG for j in range(VENT_SEG))]
        if u[1] * out[0] - u[0] * out[1] < 0:   # この並びの法線 (u1, -u0) が out と逆なら裏返す
            ring.reverse()
        mb.add_face(ring, "gym_vent_dark")


def gallery(c):
    mb = c.wall
    top, side = "gym_floor_grey", "gym_concrete_nose"
    z0, z1 = 3.8, D.Z2
    x_w = -27.48
    kit.box(mb, x_w, 4.7, z0, -4.0, 6.7, z1, side, top=top)
    kit.box(mb, x_w, 37.0, z0, -4.0, 38.85, z1, side, top=top)
    kit.box(mb, x_w, 6.7, z0, -25.4, 37.0, z1, side, top=top)
    # 手すり（U 字の内側）
    shell.railing(c.wall, [(-4.2, 6.7), (-25.4, 6.7), (-25.4, 37.0), (-4.2, 37.0)], D.Z2,
                  h=D.RAIL_H, mat="gym_rail_metal", glass="gym_rail_mesh", post=2.4)
    # 壁 B の上の手すり（観覧席の前の落下防止）
    shell.railing(c.wall, [(-3.85, D.STAND_Y[0]), (-3.85, D.STAND_Y[1])], D.Z_FRONT,
                  h=D.RAIL_TOP - D.Z_FRONT, mat="gym_rail_metal", glass="gym_rail_mesh", post=2.4)
    # 鼻先の丸い吹出口（南・西・北の 3 辺）
    for a, b, out in (((-25.4, 6.7), (-4.0, 6.7), (0.0, 1.0)),
                      ((-25.4, 6.7), (-25.4, 37.0), (1.0, 0.0)),
                      ((-25.4, 37.0), (-4.0, 37.0), (0.0, -1.0))):
        _vents(mb, a, b, out)


def band(c):
    """観覧席の後ろ（x 2.0〜2.3）の壁: ガラス帯・戸口 2 か所・南北の端の白壁。"""
    mb = c.wall
    z0, z1 = D.BAND_Z
    zl = z0 + D.DOOR_H
    x0, x1 = D.GLASS_BAND_X
    xm = (x0 + x1) * 0.5
    S.ywall(mb, x0, x1, 4.7, 6.9, z0, z1, S.WHITE)
    S.ywall(mb, x0, x1, 38.6, 47.6, z0, z1, S.WHITE)
    for g in D.BAND_DOORS:
        kit.box(mb, x0, g[0], zl, x1, g[1], z1, S.WHITE)
    segs = ((6.9, D.BAND_DOORS[0][0]), (D.BAND_DOORS[0][1], D.BAND_DOORS[1][0]))
    for ya, yb in segs:
        kit.vplate(mb, (xm, ya), (xm, yb), z0, z1, "glass_interior")
        kit.vplate(mb, (xm, ya), (xm, yb), z0, z1, "glass_interior", flip=True)
        n = max(1, int((yb - ya) / 1.5))
        for k in range(n + 1):
            y = ya + (yb - ya) * k / n
            kit.box(mb, x0, y - 0.04, z0, x1, y + 0.04, z1, "gym_mullion_black")
        kit.box(mb, x0, ya, z0, x1, yb, z0 + 0.08, "gym_mullion_black")
        kit.box(mb, x0, ya, z1 - 0.08, x1, yb, z1, "gym_mullion_black")


def build(c):
    tiers(c)
    gallery(c)
    band(c)
