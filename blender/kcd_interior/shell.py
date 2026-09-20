"""躯体シェル: 床・外周壁・間仕切り・天井・階段・手すり・照明・サイン。

すべて建物ローカル座標（+Y が入口から奥へ、+Z が上）で組む。
外周壁は「外面の線」を与え、壁厚ぶんだけ内側（進行方向の左手）へ立てる。
"""

import math

from . import kit
from .kit import T

WALL = 0.30          # 外周壁厚
PART = 0.16          # 間仕切り厚
DOOR_W = 1.10        # 片開きドアの幅
DOOR_H = 2.10


# --------------------------------------------------------------------------- #
#  床・天井
# --------------------------------------------------------------------------- #
def floor(mb, x0, y0, x1, y1, z, mat, thickness=0.22, side_mat=None):
    """床スラブ。上面 + 見付（厚み）を持つ。"""
    kit.plate(mb, x0, y0, x1, y1, z, mat)
    if thickness > 0:
        poly = [(x0, y0), (x1, y0), (x1, y1), (x0, y1)]
        mb.add_prism(poly, z - thickness, z, side_mat or "concrete_light",
                     None, side_mat or "concrete_light")


def ceiling(mb, x0, y0, x1, y1, z, mat="ceiling_white", grid=0.0):
    """下向きの天井面。grid>0 なら格子（システム天井）の目地を薄く入れる。"""
    kit.plate(mb, x0, y0, x1, y1, z, mat, flip=True)
    if grid > 0:
        eps = 0.012
        x = x0 + grid
        while x < x1 - 1e-6:
            kit.plate(mb, x - 0.02, y0, x + 0.02, y1, z - eps, "ceiling_grid",
                      flip=True)
            x += grid
        y = y0 + grid
        while y < y1 - 1e-6:
            kit.plate(mb, x0, y - 0.02, x1, y + 0.02, z - eps, "ceiling_grid",
                      flip=True)
            y += grid


def ceiling_lights(mb, x0, y0, x1, y1, z, sx=6.0, sy=6.0, w=1.20, l=0.60,
                   mat="light_panel", drop=0.03, limit=400):
    """天井照明パネルを格子状に並べる。戻り値は中心座標のリスト。"""
    out = []
    nx = max(1, int((x1 - x0) / sx))
    ny = max(1, int((y1 - y0) / sy))
    for i in range(nx):
        cx = x0 + (x1 - x0) * (i + 0.5) / nx
        for j in range(ny):
            cy = y0 + (y1 - y0) * (j + 0.5) / ny
            kit.box(mb, cx - w * 0.5, cy - l * 0.5, z - drop,
                    cx + w * 0.5, cy + l * 0.5, z, mat)
            out.append((cx, cy))
            if len(out) >= limit:
                return out
    return out


def light_strip(mb, x0, y0, x1, y1, z, mat="light_strip", w=0.22, drop=0.06):
    """ライン照明（廊下・什器上）。x か y のどちらかに長い帯。"""
    if abs(x1 - x0) >= abs(y1 - y0):
        cy = (y0 + y1) * 0.5
        kit.box(mb, x0, cy - w * 0.5, z - drop, x1, cy + w * 0.5, z, mat)
    else:
        cx = (x0 + x1) * 0.5
        kit.box(mb, cx - w * 0.5, y0, z - drop, cx + w * 0.5, y1, z, mat)


# --------------------------------------------------------------------------- #
#  壁
# --------------------------------------------------------------------------- #
def _runs(length, gaps):
    """[0, length] から gaps を除いた実体部分の区間リスト。"""
    segs = []
    cur = 0.0
    for s, e in sorted(gaps):
        s = max(0.0, min(length, s))
        e = max(0.0, min(length, e))
        if e <= cur:
            continue
        if s > cur:
            segs.append((cur, s))
        cur = max(cur, e)
    if cur < length:
        segs.append((cur, length))
    return [(s, e) for s, e in segs if e - s > 1e-4]


def outer_wall(mb, a, b, z0, z1, wall="wall_white", glass=None, thick=WALL,
               sill=0.95, header=0.45, seg=3.0, gaps=(), mullion=0.10,
               inner_mat=None):
    """a -> b の外面線に沿って壁を立てる（厚みは進行方向の左手＝室内側）。

    glass を与えると腰壁 + 連続窓 + 垂れ壁の構成になる。
    """
    dx, dy = b[0] - a[0], b[1] - a[1]
    L = math.hypot(dx, dy)
    if L < 1e-6:
        return
    d = (dx / L, dy / L)
    n = (-d[1], d[0])

    def pt(s, off):
        return (a[0] + d[0] * s + n[0] * off, a[1] + d[1] * s + n[1] * off)

    def slab(s, e, za, zb, mat):
        if e - s < 1e-4 or zb - za < 1e-4:
            return
        mb.add_prism([pt(s, 0.0), pt(e, 0.0), pt(e, thick), pt(s, thick)],
                     za, zb, mat, mat, mat)

    imat = inner_mat or wall
    for s, e in _runs(L, gaps):
        if glass is None:
            slab(s, e, z0, z1, wall)
            continue
        gz0 = z0 + sill
        gz1 = z1 - header
        slab(s, e, z0, min(gz0, z1), wall)
        if gz1 > gz0:
            slab(s, e, gz1, z1, imat)
            # ガラス面（外面から少し内側）
            mb.add_quad((pt(s, thick * 0.4)[0], pt(s, thick * 0.4)[1], gz0),
                        (pt(e, thick * 0.4)[0], pt(e, thick * 0.4)[1], gz0),
                        (pt(e, thick * 0.4)[0], pt(e, thick * 0.4)[1], gz1),
                        (pt(s, thick * 0.4)[0], pt(s, thick * 0.4)[1], gz1),
                        glass)
            # 方立
            k = s + seg
            while k < e - 1e-3:
                mb.add_prism([pt(k - mullion * 0.5, 0.0), pt(k + mullion * 0.5, 0.0),
                              pt(k + mullion * 0.5, thick * 0.8),
                              pt(k - mullion * 0.5, thick * 0.8)],
                             gz0, gz1, "metal_white", "metal_white", "metal_white")
                k += seg


def perimeter(mb, spec, z0, z1, wall="wall_white", glass="glass_clear",
              sill=0.95, header=0.45, seg=3.0, door_gap=None, extra_gaps=None,
              thick=WALL, solid_edges=()):
    """エンベロープ 4 辺の外周壁。

    door_gap: 入口辺（y_face 側）の開口 (x_start, x_end)。
    solid_edges: 窓なしにする辺のインデックス (0=入口, 1=右, 2=奥, 3=左)。
    """
    x0, x1 = spec.x0, spec.x1
    ya, yb = spec.y_face, spec.y_back
    edges = [((x0, ya), (x1, ya)), ((x1, ya), (x1, yb)),
             ((x1, yb), (x0, yb)), ((x0, yb), (x0, ya))]
    gapsets = [[], [], [], []]
    if door_gap:
        gapsets[0].append((door_gap[0] - x0, door_gap[1] - x0))
    for idx, gs in (extra_gaps or {}).items():
        gapsets[idx].extend(gs)
    for i, (a, b) in enumerate(edges):
        outer_wall(mb, a, b, z0, z1, wall=wall,
                   glass=None if i in solid_edges else glass,
                   thick=thick, sill=sill, header=header, seg=seg,
                   gaps=gapsets[i])


def partition(mb, a, b, z0, z1, mat="wall_white", thick=PART, gaps=(),
              glass_top=None, glass_z=None):
    """間仕切り壁（線の両側に thick/2 ずつ）。glass_top を与えると上部がガラス。"""
    dx, dy = b[0] - a[0], b[1] - a[1]
    L = math.hypot(dx, dy)
    if L < 1e-6:
        return
    d = (dx / L, dy / L)
    n = (-d[1], d[0])
    h = thick * 0.5

    def pt(s, off):
        return (a[0] + d[0] * s + n[0] * off, a[1] + d[1] * s + n[1] * off)

    gz = glass_z if glass_z is not None else z0 + 1.05
    for s, e in _runs(L, gaps):
        top = z1 if glass_top is None else gz
        mb.add_prism([pt(s, -h), pt(e, -h), pt(e, h), pt(s, h)], z0, top,
                     mat, mat, mat)
        if glass_top is not None and z1 > gz:
            mb.add_prism([pt(s, -h * 0.25), pt(e, -h * 0.25),
                          pt(e, h * 0.25), pt(s, h * 0.25)], gz, z1,
                         glass_top, glass_top, glass_top)


def door(mb, x, y, ang=0.0, w=DOOR_W, h=DOOR_H, leaf="desk_wood",
         frame="metal_white", glass=None):
    """開口部の建具。中心 (x, y)、面の法線は +Y 方向を ang 回転した向き。"""
    t = T(x, y, 0.0, ang)
    hw = w * 0.5
    t.box(mb, -hw - 0.07, -0.05, 0.0, -hw, 0.05, h + 0.07, frame)
    t.box(mb, hw, -0.05, 0.0, hw + 0.07, 0.05, h + 0.07, frame)
    t.box(mb, -hw - 0.07, -0.05, h, hw + 0.07, 0.05, h + 0.07, frame)
    if glass:
        t.box(mb, -hw + 0.04, -0.02, 0.05, hw - 0.04, 0.02, h - 0.05, glass)
        t.box(mb, -hw + 0.04, -0.03, 0.05, -hw + 0.10, 0.03, h - 0.05, frame)
        t.box(mb, hw - 0.10, -0.03, 0.05, hw - 0.04, 0.03, h - 0.05, frame)
    else:
        t.box(mb, -hw + 0.02, -0.025, 0.02, hw - 0.02, 0.025, h - 0.02, leaf)
        t.box(mb, hw - 0.22, 0.03, 1.02, hw - 0.14, 0.07, 1.10, "stainless")


def glass_entrance(mb, x0, x1, z0, z1, y, frame="metal_white",
                   glass="glass_clear", seg=1.6, door_h=2.4):
    """自動ドアのガラススクリーン（入口辺に貼る）。"""
    kit.box(mb, x0, y - 0.05, z0, x1, y + 0.05, z1, glass)
    kit.box(mb, x0, y - 0.09, z1 - 0.18, x1, y + 0.09, z1, frame)
    kit.box(mb, x0, y - 0.09, z0, x1, y + 0.09, z0 + 0.10, frame)
    k = x0
    while k <= x1 + 1e-6:
        kit.box(mb, k - 0.05, y - 0.09, z0, k + 0.05, y + 0.09, z1, frame)
        k += seg
    # 中央 2 枚の引き戸（框を濃く）
    cx = (x0 + x1) * 0.5
    for sgn in (-1, 1):
        a = cx + sgn * 0.08
        b = cx + sgn * 1.30
        lo, hi = min(a, b), max(a, b)
        kit.box(mb, lo, y - 0.11, z0, hi, y + 0.11, z0 + 0.12, "metal_gray")
        kit.box(mb, lo, y - 0.11, door_h - 0.12, hi, y + 0.11, door_h, "metal_gray")
        kit.box(mb, lo, y - 0.11, z0, lo + 0.07, y + 0.11, door_h, "metal_gray")
        kit.box(mb, hi - 0.07, y - 0.11, z0, hi, y + 0.11, door_h, "metal_gray")


# --------------------------------------------------------------------------- #
#  柱・階段・手すり
# --------------------------------------------------------------------------- #
def column(mb, x, y, z0, z1, size=0.62, mat="concrete_light", round_=False,
           base=True):
    if round_:
        kit.cyl(mb, x, y, z0, z1, size * 0.5, mat, seg=12, cap_top=False)
    else:
        kit.box_c(mb, x, y, z0, size, size, z1, mat)
    if base:
        kit.box_c(mb, x, y, z0, size + 0.14, size + 0.14, z0 + 0.10, "concrete_grey")


def column_grid(mb, x0, y0, x1, y1, z0, z1, sx=8.0, sy=8.0, size=0.62,
                mat="concrete_light", skip=None, round_=False):
    out = []
    nx = max(1, int(round((x1 - x0) / sx)))
    ny = max(1, int(round((y1 - y0) / sy)))
    for i in range(1, nx):
        cx = x0 + (x1 - x0) * i / nx
        for j in range(1, ny):
            cy = y0 + (y1 - y0) * j / ny
            if skip and skip(cx, cy):
                continue
            column(mb, cx, cy, z0, z1, size, mat, round_)
            out.append((cx, cy))
    return out


def stair_flight(mb, cx, y0, y1, z0, z1, width=2.6, steps=None,
                 tread_mat="floor_tile_grey", riser_mat="wall_white",
                 rail=True, rail_mat="metal_white"):
    """まっすぐな直階段（+Y 方向に上る）。踏面・蹴上を実ジオメトリで作る。"""
    n = steps or max(2, int(round((z1 - z0) / 0.175)))
    dy = (y1 - y0) / n
    dz = (z1 - z0) / n
    hw = width * 0.5
    for i in range(n):
        y = y0 + dy * i
        z = z0 + dz * i
        kit.box(mb, cx - hw, y, z, cx + hw, y + dy, z + dz * 0.32, tread_mat)
        kit.box(mb, cx - hw, y + dy * 0.86, z, cx + hw, y + dy, z + dz, riser_mat)
    # 側桁
    for sgn in (-1, 1):
        x = cx + sgn * hw
        mb.add_quad((x, y0, z0 - 0.30), (x, y1, z1 - 0.30),
                    (x, y1, z1), (x, y0, z0), riser_mat)
    if rail:
        for sgn in (-1, 1):
            x = cx + sgn * (hw - 0.05)
            kit.tube(mb, (x, y0, z0 + 0.95), (x, y1, z1 + 0.95), 0.035,
                     rail_mat, seg=6)
            for k in range(0, n + 1, max(2, n // 6)):
                y = y0 + dy * k
                z = z0 + dz * k
                kit.tube(mb, (x, y, z), (x, y, z + 0.95), 0.022, rail_mat, seg=4)


def landing(mb, x0, y0, x1, y1, z, mat="floor_tile_grey"):
    kit.box(mb, x0, y0, z - 0.22, x1, y1, z, mat, top=mat)


def railing(mb, pts, z, h=1.05, mat="metal_white", glass=None, post=1.6):
    """折れ線に沿った手すり。glass を与えるとガラス手すり。"""
    for i in range(len(pts) - 1):
        a, b = pts[i], pts[i + 1]
        L = math.hypot(b[0] - a[0], b[1] - a[1])
        if L < 1e-6:
            continue
        kit.tube(mb, (a[0], a[1], z + h), (b[0], b[1], z + h), 0.035, mat, seg=6)
        if glass:
            mb.add_quad((a[0], a[1], z + 0.05), (b[0], b[1], z + 0.05),
                        (b[0], b[1], z + h - 0.06), (a[0], a[1], z + h - 0.06),
                        glass)
        n = max(1, int(L / post))
        for k in range(n + 1):
            t = k / n
            x = a[0] + (b[0] - a[0]) * t
            y = a[1] + (b[1] - a[1]) * t
            kit.box_c(mb, x, y, z, 0.06, 0.06, z + h, mat)
        if not glass:
            kit.tube(mb, (a[0], a[1], z + h * 0.45), (b[0], b[1], z + h * 0.45),
                     0.020, mat, seg=4)


# --------------------------------------------------------------------------- #
#  設備・サイン
# --------------------------------------------------------------------------- #
def elevator_bank(mb, x0, y, count=3, ang=0.0, pitch=2.6, w=1.10, h=2.30,
                  surround="stainless"):
    """エレベーターの扉列。y は戸当たり面、ang=0 で扉が -Y を向く。"""
    centers = []
    for i in range(count):
        cx = x0 + pitch * i
        t = T(cx, y, 0.0, ang)
        t.box(mb, -w - 0.16, -0.06, 0.0, w + 0.16, 0.06, h + 0.30, surround)
        t.box(mb, -w, -0.10, 0.0, -0.015, 0.02, h, "metal_gray")
        t.box(mb, 0.015, -0.10, 0.0, w, 0.02, h, "metal_gray")
        t.box(mb, -0.35, -0.10, h + 0.06, 0.35, -0.02, h + 0.24, "screen_blue")
        t.box(mb, w + 0.20, -0.08, 1.00, w + 0.32, -0.01, 1.35, "plastic_white")
        centers.append((cx, y))
    return centers


def exit_sign(mb, x, y, z, ang=0.0, w=0.62, h=0.26):
    """避難誘導灯（緑・発光）。"""
    t = T(x, y, z, ang)
    t.box(mb, -w * 0.5, -0.05, -h, w * 0.5, 0.05, 0.0, "sign_exit_green")
    t.box(mb, -0.05, -0.03, 0.0, 0.05, 0.03, 0.26, "metal_gray")


def wall_sign(mb, x, y, z, ang=0.0, w=1.6, h=0.5, mat="sign_plate_blue",
              frame="metal_white", out=0.06):
    """案内サイン板（文字は Unity の TMP）。"""
    t = T(x, y, z, ang)
    t.box(mb, -w * 0.5 - 0.04, 0.0, -h * 0.5 - 0.04, w * 0.5 + 0.04, out * 0.5,
          h * 0.5 + 0.04, frame)
    t.box(mb, -w * 0.5, out * 0.5, -h * 0.5, w * 0.5, out, h * 0.5, mat)


def hanging_sign(mb, x, y, z, ang=0.0, w=1.8, h=0.42, mat="sign_plate_blue",
                 drop=0.5):
    t = T(x, y, z, ang)
    for sgn in (-1, 1):
        t.box(mb, sgn * w * 0.35 - 0.02, -0.02, 0.0, sgn * w * 0.35 + 0.02,
              0.02, drop, "metal_gray")
    t.box(mb, -w * 0.5, -0.04, -drop - h, w * 0.5, 0.04, -drop, mat)


def notice_board(mb, x, y, z, ang=0.0, w=2.4, h=1.3, sheets=9, rng=None):
    """掲示板（コルク + 貼り紙）。"""
    t = T(x, y, z, ang)
    t.box(mb, -w * 0.5, 0.0, 0.0, w * 0.5, 0.07, h, "desk_wood")
    t.box(mb, -w * 0.5 + 0.07, 0.07, 0.07, w * 0.5 - 0.07, 0.09, h - 0.07,
          "fabric_beige")
    cols = max(1, sheets // 2)
    for i in range(sheets):
        cx = -w * 0.5 + 0.22 + (w - 0.55) * (i % cols) / max(1, cols - 1 or 1)
        cy = h * (0.30 if i < cols else 0.66)
        jx = 0.0 if rng is None else rng.uniform(-0.03, 0.03)
        t.box(mb, cx - 0.14 + jx, 0.09, cy - 0.20, cx + 0.14 + jx, 0.10,
              cy + 0.20, "paper_white")


def fire_extinguisher(mb, x, y, z=0.0, ang=0.0):
    t = T(x, y, z, ang)
    t.cyl(mb, 0.0, 0.0, 0.10, 0.62, 0.075, "fire_red", seg=8)
    t.cyl(mb, 0.0, 0.0, 0.62, 0.72, 0.030, "metal_gray", seg=6)
    t.box(mb, -0.10, -0.10, 0.0, 0.10, 0.10, 0.10, "fire_red")


def trash_bins(mb, x, y, ang=0.0, n=3, mats=("plastic_white", "fm_blue",
                                             "chair_green")):
    t = T(x, y, 0.0, ang)
    for i in range(n):
        cx = (i - (n - 1) * 0.5) * 0.46
        t.box_c(mb, cx, 0.0, 0.0, 0.42, 0.40, 0.88, mats[i % len(mats)])
        t.box_c(mb, cx, 0.0, 0.88, 0.46, 0.44, 0.94, "plastic_black")
        t.box_c(mb, cx, 0.0, 0.94, 0.26, 0.26, 0.96, "plastic_black")


def vending(mb, x, y, ang=0.0, mat="fm_blue"):
    t = T(x, y, 0.0, ang)
    t.box(mb, -0.55, -0.40, 0.0, 0.55, 0.40, 1.83, mat)
    t.box(mb, -0.50, 0.40, 0.55, 0.12, 0.42, 1.68, "screen_blue")
    t.box(mb, 0.16, 0.40, 0.90, 0.48, 0.42, 1.30, "plastic_black")
    t.box(mb, -0.50, 0.40, 0.18, 0.50, 0.42, 0.40, "plastic_black")


def planter(mb, x, y, r=0.42, h=0.46, leaf_h=1.5, mat="plant_pot"):
    kit.cyl(mb, x, y, 0.0, h, r, mat, seg=10)
    kit.cyl(mb, x, y, h - 0.04, h, r - 0.05, "soil_dark", seg=10)
    kit.cyl(mb, x, y, h, h + leaf_h * 0.45, 0.045, "trunk", seg=6)
    kit.blob(mb, x, y, h + leaf_h * 0.72, r * 1.5, r * 1.5, leaf_h * 0.36,
             "plant_green", seg=6, rings=3)


def clock(mb, x, y, z, ang=0.0, r=0.28):
    """壁掛け時計。面は +Y 方向（ang で回転）を向く。"""
    t = T(x, y, z, ang)
    t.box(mb, -r, 0.0, -r, r, 0.05, r, "plastic_white")
    t.box(mb, -0.020, 0.05, -0.02, 0.020, 0.06, r * 0.72, "plastic_black")
    t.box(mb, -0.020, 0.05, -0.02, r * 0.55, 0.06, 0.02, "plastic_black")
