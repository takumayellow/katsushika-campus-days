"""家具・什器。すべて「自分の +Y が正面」でモデリングし、T が向きと位置を与える。

三角形数を抑えるため、大量配置するもの（講堂の座席・食堂の椅子・本の背表紙）は
底面を省いた箱（kit.box_nb）で作る。
"""

import math

from . import imats, kit
from .kit import T, face_ang  # noqa: F401  （プラン側が F.face_ang で使う）


# --------------------------------------------------------------------------- #
#  机・椅子
# --------------------------------------------------------------------------- #
def desk(mb, x, y, ang=0.0, w=1.40, d=0.70, h=0.72, top="desk_wood",
         leg="metal_gray", drawers=False):
    t = T(x, y, 0.0, ang)
    hw, hd = w * 0.5, d * 0.5
    t.box(mb, -hw, -hd, h - 0.045, hw, hd, h, top)
    for sx in (-1, 1):
        for sy in (-1, 1):
            t.box_nb(mb, sx * (hw - 0.10) - 0.03, sy * (hd - 0.08) - 0.03,
                     0.0, sx * (hw - 0.10) + 0.03, sy * (hd - 0.08) + 0.03,
                     h - 0.045, leg)
    if drawers:
        t.box(mb, hw - 0.44, -hd + 0.04, 0.05, hw - 0.04, hd - 0.04,
              h - 0.05, "desk_white")
        for k in range(3):
            z = 0.14 + k * 0.19
            t.box(mb, hw - 0.44, hd - 0.05, z, hw - 0.04, hd - 0.02,
                  z + 0.15, "plastic_white")


def long_desk(mb, x, y, ang=0.0, w=3.60, d=0.55, h=0.72, top="desk_wood",
              front="desk_white", two_sided=False):
    """講義室の長机（前板つき）。

    two_sided=True で前板を天板の中央に寄せる。両側に座る閲覧机で使う
    （+Y 側だけに幕板があると、そちら側に座る人の膝に当たる）。
    """
    t = T(x, y, 0.0, ang)
    hw, hd = w * 0.5, d * 0.5
    t.box(mb, -hw, -hd, h - 0.04, hw, hd, h, top)
    fy0, fy1 = (-0.03, 0.03) if two_sided else (hd - 0.05, hd)
    t.box(mb, -hw, fy0, 0.30, hw, fy1, h - 0.04, front)
    for sx in (-1, 1):
        t.box_nb(mb, sx * (hw - 0.14) - 0.04, -hd, 0.0,
                 sx * (hw - 0.14) + 0.04, hd, 0.30, "metal_gray")


def chair(mb, x, y, ang=0.0, mat="chair_blue", frame="metal_gray"):
    """脚つきの椅子（研究室・カフェ用。約 60 三角形）。"""
    t = T(x, y, 0.0, ang)
    t.box(mb, -0.23, -0.23, 0.42, 0.23, 0.23, 0.47, mat)
    t.box(mb, -0.22, -0.24, 0.47, 0.22, -0.18, 0.88, mat)
    for sx in (-1, 1):
        for sy in (-1, 1):
            t.box_nb(mb, sx * 0.19 - 0.022, sy * 0.19 - 0.022, 0.0,
                     sx * 0.19 + 0.022, sy * 0.19 + 0.022, 0.42, frame)


def chair_min(mb, t, mat="chair_blue", frame="metal_gray"):
    """大量配置用の最小椅子（36 三角形）。t は既に位置・向きを持つ T。"""
    t.box_nb(mb, -0.21, -0.21, 0.41, 0.21, 0.21, 0.455, mat)
    t.box_nb(mb, -0.20, -0.22, 0.455, 0.20, -0.17, 0.82, mat)
    t.box_nb(mb, -0.15, -0.15, 0.0, 0.15, 0.15, 0.41, frame)


def chair_canteen(mb, t, mat="chair_blue", frame="metal_gray"):
    """食堂・大教室用のさらに軽い椅子（28 三角形）。"""
    t.box_nb(mb, -0.20, -0.20, 0.42, 0.20, 0.20, 0.46, mat)
    t.box_nb(mb, -0.19, -0.21, 0.46, 0.19, -0.17, 0.80, mat)
    t.cyl(mb, 0.0, 0.0, 0.0, 0.42, 0.055, frame, seg=4, cap_top=False)


def stool(mb, x, y, ang=0.0, mat="sb_wood", h=0.72):
    t = T(x, y, 0.0, ang)
    t.cyl(mb, 0.0, 0.0, h - 0.05, h, 0.19, mat, seg=8)
    t.cyl(mb, 0.0, 0.0, 0.0, h - 0.05, 0.05, "metal_dark", seg=6)
    t.cyl(mb, 0.0, 0.0, 0.0, 0.03, 0.22, "metal_dark", seg=8)


def hall_seat(mb, t, mat="chair_hall_red", frame="metal_dark"):
    """講堂の折りたたみ座席（44 三角形）。"""
    t.box_nb(mb, -0.212, -0.22, 0.42, 0.212, 0.24, 0.47, mat)
    t.box_nb(mb, -0.212, -0.26, 0.47, 0.212, -0.19, 0.93, mat)
    t.box_nb(mb, -0.238, -0.26, 0.10, -0.190, 0.24, 0.47, frame)
    t.box_nb(mb, 0.190, -0.26, 0.10, 0.238, 0.24, 0.47, frame)


def table(mb, x, y, ang=0.0, w=1.60, d=0.80, h=0.72, top="desk_wood",
          leg="metal_gray", z=0.0):
    """食堂のテーブル（脚は 2 枚の T 字。低ポリ）。"""
    t = T(x, y, z, ang)
    hw, hd = w * 0.5, d * 0.5
    t.box(mb, -hw, -hd, h - 0.045, hw, hd, h, top)
    for sx in (-1, 1):
        cx = sx * (hw - 0.24)
        t.box_nb(mb, cx - 0.04, -0.05, 0.03, cx + 0.04, 0.05, h - 0.045, leg)
        t.box_nb(mb, cx - 0.06, -hd + 0.10, 0.0, cx + 0.06, hd - 0.10, 0.06, leg)


def round_table(mb, x, y, r=0.42, h=0.73, top="sb_wood", leg="metal_dark",
                z=0.0):
    kit.cyl(mb, x, y, z + h - 0.04, z + h, r, top, seg=10)
    kit.cyl(mb, x, y, z + 0.03, z + h - 0.04, 0.055, leg, seg=6)
    kit.cyl(mb, x, y, z, z + 0.035, r * 0.55, leg, seg=8)


SEAT_PITCH = 0.60    # 座る位置（アンカー）の最小間隔


def _seat(mb, t, vi, hw, d0, d1, inner, depth):
    """座れる家具を mb.seats に記録する（Ctx.flush_seats が seat_ Empty にする）。

    Unity の SeatFactory がこれを読んで「E で座る」操作と、上に乗り上げない
    ための見えない壁を付ける。t のローカルで幅 +-hw・奥行き d0..d1（+Y が正面）が
    家具の外形。inner は腰掛けられる幅、depth は座面の前端から腰を置く位置
    （床の高さのアンカー）までの距離。立つと前へ 0.55 m 出るので、depth は
    0.55 - カプセル半径 0.31 より小さくして立ち位置を家具の外に出す。
    vi はこの家具の最初の頂点番号（kit.lift で一緒に持ち上げるため）。
    """
    seats = getattr(mb, "seats", None)
    if seats is None:
        return
    n = max(1, int(inner / SEAT_PITCH + 1e-6))
    dm = (d0 + d1) * 0.5
    seats.append({
        "vi": vi,
        "c": t.p(0.0, dm),
        "f": t.p(0.0, d1),
        "s": t.p(hw, dm),
        "anchors": [t.p(inner * ((k + 0.5) / n - 0.5), d1 - depth)
                    for k in range(n)],
    })


def bench(mb, x, y, ang=0.0, w=1.80, mat="wood", leg="metal_gray", back=False):
    vi = len(mb.verts)
    t = T(x, y, 0.0, ang)
    hw = w * 0.5
    t.box(mb, -hw, -0.22, 0.40, hw, 0.22, 0.45, mat)
    for sx in (-1, 1):
        t.box_nb(mb, sx * (hw - 0.18) - 0.05, -0.20, 0.0,
                 sx * (hw - 0.18) + 0.05, 0.20, 0.40, leg)
    if back:
        t.box(mb, -hw, -0.24, 0.45, hw, -0.18, 0.86, mat)
    _seat(mb, t, vi, hw, -0.24 if back else -0.22, 0.22, w, 0.20)


def sofa(mb, x, y, ang=0.0, w=2.0, d=0.85, mat="fabric_beige",
         cushion="cushion_red"):
    vi = len(mb.verts)
    t = T(x, y, 0.0, ang)
    hw, hd = w * 0.5, d * 0.5
    t.box(mb, -hw, -hd, 0.0, hw, hd, 0.38, mat)
    t.box(mb, -hw + 0.14, -hd + 0.06, 0.38, hw - 0.14, hd - 0.02, 0.46, cushion)
    t.box(mb, -hw, -hd, 0.38, hw, -hd + 0.18, 0.82, mat)
    # 背クッション。座った腰（前端から 0.22 m）と背もたれの隙間を詰める
    t.box(mb, -hw + 0.14, -hd + 0.18, 0.46, hw - 0.14, -hd + 0.34, 0.74, cushion)
    for sx in (-1, 1):
        t.box(mb, sx * hw - sx * 0.14, -hd, 0.38, sx * hw, hd, 0.62, mat)
    _seat(mb, t, vi, hw, -hd, hd, w - 0.28, 0.22)


def lounge_chair(mb, x, y, ang=0.0, mat="fabric_green"):
    vi = len(mb.verts)
    t = T(x, y, 0.0, ang)
    t.box(mb, -0.34, -0.34, 0.0, 0.34, 0.34, 0.36, mat)
    t.box(mb, -0.30, -0.30, 0.36, 0.30, 0.30, 0.43, "cushion_red")
    t.box(mb, -0.34, -0.36, 0.36, 0.34, -0.20, 0.78, mat)
    _seat(mb, t, vi, 0.34, -0.36, 0.34, 0.60, 0.22)


# --------------------------------------------------------------------------- #
#  棚・本
# --------------------------------------------------------------------------- #
def book_row(mb, t, x0, x1, z, height=0.26, depth=0.22, rng=None, density=1.0,
             clump=0.26):
    """棚板 1 段ぶんの背表紙。

    1 冊ずつ箱にすると図書館だけで 10 万三角形を超えるので、同色の本
    数冊ぶんを 1 個の箱（房）にまとめる。房ごとに幅・高さ・奥行きを
    揺らすので、並びは単調にならない。
    """
    x = x0
    i = 0
    while x < x1 - 0.04:
        w = clump if rng is None else rng.uniform(clump * 0.55, clump * 1.45)
        if x + w > x1:
            w = x1 - x
            if w < 0.06:
                break
        if rng is not None and rng.random() > density:
            x += w + 0.03
            i += 1
            continue
        h = height * (0.86 if rng is None else rng.uniform(0.72, 1.0))
        back = 0.0 if rng is None else rng.uniform(0.0, 0.035)
        t.box_nb(mb, x, -depth * 0.5 + back, z, x + w, depth * 0.5, z + h,
                 imats.book(i * 3 + (0 if rng is None else rng.randrange(8))))
        x += w + 0.012
        i += 1


def bookshelf(mb, x, y, ang=0.0, w=0.90, h=1.90, shelves=5, rng=None,
              body="desk_wood", depth=0.30, books=True):
    """片面の書架 1 連。"""
    t = T(x, y, 0.0, ang)
    hw, hd = w * 0.5, depth * 0.5
    t.box(mb, -hw, -hd, 0.0, -hw + 0.03, hd, h, body)
    t.box(mb, hw - 0.03, -hd, 0.0, hw, hd, h, body)
    t.box(mb, -hw, -hd, 0.0, hw, -hd + 0.02, h, body)
    t.box(mb, -hw, -hd, 0.0, hw, hd, 0.09, body)
    for k in range(shelves):
        z = 0.09 + (h - 0.20) * k / shelves
        t.box(mb, -hw + 0.03, -hd, z, hw - 0.03, hd, z + 0.025, body)
        if books:
            book_row(mb, t, -hw + 0.06, hw - 0.06, z + 0.025,
                     height=(h - 0.20) / shelves - 0.07, depth=depth - 0.08,
                     rng=rng, density=0.92)
    t.box(mb, -hw, -hd, h - 0.03, hw, hd, h, body)


def stack_range(mb, x, y, length, ang=0.0, bays=6, h=1.90, shelves=5,
                rng=None, depth=0.56, clump=0.30):
    """図書館の両面書架 1 列。(x, y) が列の中心、長手は自身の X 方向。"""
    t = T(x, y, 0.0, ang)
    hl = length * 0.5
    hd = depth * 0.5
    t.box(mb, -hl, -0.02, 0.0, hl, 0.02, h, "metal_gray")          # 背板
    t.box(mb, -hl, -hd, 0.0, hl, hd, 0.11, "metal_dark")           # 幅木
    t.box(mb, -hl, -hd, h - 0.05, hl, hd, h, "metal_gray")         # 天板
    for k in range(bays + 1):
        px = -hl + length * k / bays
        t.box(mb, px - 0.025, -hd, 0.0, px + 0.025, hd, h, "metal_gray")
    bh = (h - 0.24) / shelves - 0.06
    for side in (-1, 1):
        off = side * (hd - 0.13)
        p = t.p2(0.0, off)
        shifted = T(p[0], p[1], 0.0, ang)
        for s in range(shelves):
            z = 0.11 + (h - 0.24) * s / shelves
            t.box(mb, -hl, side * 0.02, z, hl, side * hd, z + 0.022,
                  "metal_gray")
            book_row(mb, shifted, -hl + 0.10, hl - 0.10, z + 0.022,
                     height=bh, depth=0.22, rng=rng, density=0.90, clump=clump)


def locker_bank(mb, x0, x1, y, ang=0.0, h=1.80, cols=None, mat="metal_gray"):
    t = T((x0 + x1) * 0.5, y, 0.0, ang)
    w = x1 - x0
    hw = w * 0.5
    t.box(mb, -hw, -0.22, 0.0, hw, 0.22, h, mat)
    n = cols or max(1, int(w / 0.32))
    for i in range(n):
        cx = -hw + w * (i + 0.5) / n
        for k in range(3):
            z = 0.10 + k * (h - 0.20) / 3
            t.box(mb, cx - w / n * 0.42, 0.22, z, cx + w / n * 0.42, 0.235,
                  z + (h - 0.20) / 3 - 0.04, "plastic_white")


def gondola(mb, x, y, ang=0.0, length=3.0, h=1.50, shelves=4, rng=None,
            body="plastic_white", clump=0.12):
    """コンビニの両面ゴンドラ什器。商品は色付きの小箱。"""
    t = T(x, y, 0.0, ang)
    hl = length * 0.5
    t.box(mb, -hl, -0.02, 0.0, hl, 0.02, h, body)
    t.box(mb, -hl, -0.45, 0.0, hl, 0.45, 0.12, "metal_gray")
    for side in (-1, 1):
        for s in range(shelves):
            z = 0.12 + (h - 0.22) * s / shelves
            t.box(mb, -hl, side * 0.02, z, hl, side * 0.44, z + 0.03, body)
            gx = -hl + 0.06
            i = 0
            while gx < hl - 0.12:
                w = clump if rng is None else rng.uniform(clump * 0.8,
                                                           clump * 1.45)
                col = imats.book(i + (0 if rng is None else rng.randrange(8)))
                t.box_nb(mb, gx, side * 0.10, z + 0.03, gx + w,
                         side * 0.40, z + 0.03 + 0.20, col)
                gx += w + 0.02
                i += 1


def fridge_case(mb, x, y, ang=0.0, length=4.0, h=2.00, rng=None,
                clump=0.10):
    """コンビニのリーチイン冷蔵ケース（ガラス扉）。"""
    t = T(x, y, 0.0, ang)
    hl = length * 0.5
    t.box(mb, -hl, -0.42, 0.0, hl, 0.42, h, "metal_gray")
    t.box(mb, -hl + 0.05, -0.30, 0.10, hl - 0.05, 0.30, h - 0.22, "screen_blue")
    for s in range(4):
        z = 0.20 + s * (h - 0.55) / 4
        t.box(mb, -hl + 0.06, -0.28, z, hl - 0.06, 0.28, z + 0.03, "metal_gray")
        gx = -hl + 0.10
        i = 0
        while gx < hl - 0.16:
            w = clump if rng is None else rng.uniform(clump * 0.78,
                                                       clump * 1.4)
            t.box_nb(mb, gx, -0.10, z + 0.03, gx + w, 0.24, z + 0.26,
                     imats.book(i * 2))
            gx += w + 0.015
            i += 1
    nd = max(1, int(length / 0.9))
    for i in range(nd):
        px = -hl + length * (i + 0.5) / nd
        t.box(mb, px - length / nd * 0.46, 0.40, 0.10,
              px + length / nd * 0.46, 0.44, h - 0.22, "glass_interior")
        t.box(mb, px + length / nd * 0.40, 0.40, 0.10,
              px + length / nd * 0.46, 0.46, h - 0.22, "metal_white")
    t.box(mb, -hl, -0.44, h - 0.22, hl, 0.44, h, "fm_blue")


# --------------------------------------------------------------------------- #
#  カウンター・店舗什器
# --------------------------------------------------------------------------- #
def counter(mb, x0, y0, x1, y1, h=1.05, body="counter_wood", top="counter_stone",
            toe=True, front_mat=None):
    """直線カウンター（矩形で指定）。天板が 0.04 m 出る。"""
    kit.box(mb, x0, y0, 0.0 if not toe else 0.10, x1, y1, h - 0.04,
            front_mat or body)
    kit.box(mb, x0 - 0.04, y0 - 0.04, h - 0.04, x1 + 0.04, y1 + 0.04, h, top)
    if toe:
        kit.box(mb, x0 + 0.08, y0 + 0.08, 0.0, x1 - 0.08, y1 - 0.08, 0.10,
                "metal_dark")


def reception(mb, x, y, ang=0.0, w=4.2, d=0.9, h=1.10):
    """受付カウンター（低いカウンター + 立ち上がり）。"""
    t = T(x, y, 0.0, ang)
    hw, hd = w * 0.5, d * 0.5
    t.box(mb, -hw, -hd, 0.10, hw, hd, 0.74, "counter_wood")
    t.box(mb, -hw, -hd, 0.74, hw, hd - 0.28, h - 0.04, "counter_wood")
    t.box(mb, -hw - 0.05, -hd - 0.05, h - 0.04, hw + 0.05, hd - 0.23, h,
          "counter_stone")
    t.box(mb, -hw - 0.05, hd - 0.32, 0.70, hw + 0.05, hd + 0.06, 0.76,
          "counter_stone")
    t.box(mb, -hw + 0.08, -hd + 0.06, 0.0, hw - 0.08, hd - 0.06, 0.10,
          "metal_dark")


def register(mb, x, y, ang=0.0):
    t = T(x, y, 0.0, ang)
    t.box(mb, -0.19, -0.16, 0.0, 0.19, 0.16, 0.13, "plastic_black")
    t.box(mb, -0.17, -0.12, 0.13, 0.17, 0.10, 0.34, "plastic_white")
    t.box(mb, -0.16, -0.10, 0.34, 0.16, 0.04, 0.38, "screen_blue")


def espresso_machine(mb, x, y, ang=0.0):
    t = T(x, y, 0.0, ang)
    t.box(mb, -0.42, -0.26, 0.0, 0.42, 0.26, 0.48, "stainless")
    t.box(mb, -0.42, -0.26, 0.48, 0.42, 0.10, 0.62, "plastic_black")
    for sx in (-0.22, 0.22):
        t.cyl(mb, sx, 0.22, 0.10, 0.20, 0.035, "plastic_black", seg=6)
        t.box(mb, sx - 0.06, 0.16, 0.20, sx + 0.06, 0.28, 0.30, "stainless")
    t.cyl(mb, 0.0, -0.10, 0.62, 0.86, 0.10, "stainless", seg=8)


def showcase(mb, x, y, ang=0.0, w=1.8, d=0.7, h=1.15, rng=None):
    """ガラスのショーケース（ケーキ・サンドイッチ）。"""
    t = T(x, y, 0.0, ang)
    hw, hd = w * 0.5, d * 0.5
    t.box(mb, -hw, -hd, 0.0, hw, hd, 0.82, "counter_wood")
    t.box(mb, -hw, -hd, 0.82, hw, hd, 0.86, "stainless")
    t.box(mb, -hw, -hd, 0.86, hw, hd, h, "glass_interior")
    for s in range(2):
        z = 0.90 + s * 0.14
        t.box(mb, -hw + 0.05, -hd + 0.05, z, hw - 0.05, hd - 0.05, z + 0.02,
              "stainless")
        n = max(1, int((w - 0.2) / 0.22))
        for i in range(n):
            cx = -hw + 0.12 + (w - 0.24) * i / max(1, n - 1)
            col = "cake_pink" if (i + s) % 2 == 0 else "coffee_brown"
            t.box_nb(mb, cx - 0.08, -0.12, z + 0.02, cx + 0.08, 0.12,
                     z + 0.12, col)
    t.box(mb, -hw, -hd, h, hw, hd, h + 0.04, "stainless")


def serving_line(mb, x0, x1, y, depth=1.2, h=0.95, trays=True):
    """食堂の配膳カウンター（スチール天板 + トレーレール + 上部の照明）。"""
    kit.box(mb, x0, y - depth * 0.5, 0.08, x1, y + depth * 0.5, h, "stainless")
    kit.box(mb, x0, y - depth * 0.5 - 0.30, h - 0.08, x1, y - depth * 0.5,
            h - 0.02, "stainless")
    for k in range(int((x1 - x0) / 1.6) + 1):
        cx = x0 + 1.6 * k
        kit.box(mb, cx - 0.04, y - depth * 0.5 - 0.30, 0.0, cx + 0.04,
                y - depth * 0.5 - 0.22, h - 0.08, "stainless")
    if trays:
        kit.box(mb, x0 + 0.2, y - 0.18, h, x1 - 0.2, y + depth * 0.5 - 0.10,
                h + 0.12, "counter_stone")
    kit.box(mb, x0, y + depth * 0.5 - 0.10, h + 1.40, x1,
            y + depth * 0.5 + 0.02, h + 1.62, "metal_gray")
    kit.box(mb, x0 + 0.05, y + depth * 0.5 - 0.20, h + 1.32, x1 - 0.05,
            y + depth * 0.5 - 0.02, h + 1.40, "light_strip")


def tray_rack(mb, x, y, ang=0.0, w=0.9):
    t = T(x, y, 0.0, ang)
    hw = w * 0.5
    t.box(mb, -hw, -0.30, 0.10, hw, 0.30, 0.90, "stainless")
    for k in range(4):
        t.box(mb, -hw + 0.06, -0.26, 0.90 + k * 0.035, hw - 0.06, 0.26,
              0.90 + k * 0.035 + 0.025, "tray_beige")


def ticket_machine(mb, x, y, ang=0.0):
    """食券の券売機。"""
    t = T(x, y, 0.0, ang)
    t.box(mb, -0.38, -0.32, 0.0, 0.38, 0.32, 1.60, "metal_gray")
    t.box(mb, -0.34, 0.32, 0.95, 0.34, 0.345, 1.45, "screen_blue")
    t.box(mb, -0.30, 0.32, 0.62, 0.30, 0.345, 0.90, "plastic_black")
    t.box(mb, -0.12, 0.32, 0.40, 0.12, 0.345, 0.52, "plastic_white")
    t.box(mb, -0.38, -0.32, 1.60, 0.38, 0.32, 1.72, "fm_green")


def return_counter(mb, x, y, ang=0.0, w=2.4):
    """食器返却口。"""
    t = T(x, y, 0.0, ang)
    hw = w * 0.5
    t.box(mb, -hw, -0.45, 0.0, hw, 0.45, 0.92, "stainless")
    t.box(mb, -hw, -0.45, 0.92, hw, 0.45, 1.02, "counter_stone")
    t.box(mb, -hw + 0.1, -0.45, 1.02, hw - 0.1, 0.45, 1.70, "metal_gray")
    t.box(mb, -hw + 0.2, -0.30, 1.10, hw - 0.2, 0.30, 1.48, "plastic_black")


# --------------------------------------------------------------------------- #
#  OA・教室
# --------------------------------------------------------------------------- #
def monitor(mb, x, y, z, ang=0.0, w=0.56, h=0.34):
    t = T(x, y, z, ang)
    t.box(mb, -w * 0.5, -0.02, 0.12, w * 0.5, 0.02, 0.12 + h, "plastic_black")
    t.box(mb, -w * 0.5 + 0.02, -0.021, 0.14, w * 0.5 - 0.02, -0.018,
          0.10 + h, "screen_blue")
    t.box(mb, -0.05, -0.02, 0.0, 0.05, 0.06, 0.12, "plastic_black")
    t.box(mb, -0.13, -0.10, 0.0, 0.13, 0.10, 0.02, "plastic_black")


def keyboard(mb, x, y, z, ang=0.0):
    t = T(x, y, z, ang)
    t.box(mb, -0.19, -0.07, 0.0, 0.19, 0.07, 0.018, "plastic_black")


def pc_tower(mb, x, y, ang=0.0):
    t = T(x, y, 0.0, ang)
    t.box(mb, -0.10, -0.22, 0.0, 0.10, 0.22, 0.42, "plastic_black")
    t.box(mb, -0.08, 0.22, 0.28, 0.08, 0.225, 0.38, "screen_blue")


def whiteboard(mb, x, y, z, ang=0.0, w=2.4, h=1.2):
    t = T(x, y, z, ang)
    t.box(mb, -w * 0.5 - 0.04, 0.0, -h * 0.5 - 0.04, w * 0.5 + 0.04, 0.05,
          h * 0.5 + 0.04, "metal_white")
    t.box(mb, -w * 0.5, 0.05, -h * 0.5, w * 0.5, 0.07, h * 0.5, "board_white")
    t.box(mb, -w * 0.5, 0.05, -h * 0.5 - 0.10, w * 0.5, 0.14, -h * 0.5 - 0.04,
          "metal_white")


def projection_screen(mb, x, y, z_top, ang=0.0, w=5.0, h=3.0):
    t = T(x, y, z_top, ang)
    t.box(mb, -w * 0.5 - 0.08, -0.06, -0.14, w * 0.5 + 0.08, 0.06, 0.0,
          "metal_gray")
    t.box(mb, -w * 0.5, -0.02, -h, w * 0.5, 0.01, -0.14, "screen_white")


def projector(mb, x, y, z, ang=0.0):
    t = T(x, y, z, ang)
    t.box(mb, -0.22, -0.16, -0.14, 0.22, 0.16, 0.0, "plastic_white")
    t.cyl(mb, 0.0, 0.16, -0.10, 0.22, 0.05, "plastic_black", seg=6)
    for sx in (-0.16, 0.16):
        t.box(mb, sx - 0.02, -0.02, 0.0, sx + 0.02, 0.02, 0.22, "metal_gray")


def podium(mb, x, y, ang=0.0, w=1.1, d=0.62, h=1.12):
    t = T(x, y, 0.0, ang)
    hw, hd = w * 0.5, d * 0.5
    t.box(mb, -hw, -hd, 0.0, hw, hd, h - 0.05, "counter_wood")
    t.box(mb, -hw - 0.04, -hd - 0.04, h - 0.05, hw + 0.04, hd + 0.04, h,
          "desk_dark")
    t.box(mb, -0.22, -hd + 0.06, h, 0.22, hd - 0.20, h + 0.02, "paper_white")
    t.cyl(mb, 0.30, -0.10, h, h + 0.30, 0.012, "metal_gray", seg=5)
    t.box(mb, 0.28, -0.12, h + 0.30, 0.34, -0.06, h + 0.36, "plastic_black")


def desk_lamp(mb, x, y, ang=0.0, mat="metal_gray"):
    t = T(x, y, 0.0, ang)
    t.cyl(mb, 0.0, 0.0, 0.0, 0.02, 0.09, mat, seg=8)
    t.cyl(mb, 0.0, 0.0, 0.0, 0.38, 0.016, mat, seg=5)
    t.box(mb, -0.02, -0.02, 0.36, 0.02, 0.24, 0.40, mat)
    t.box(mb, -0.07, 0.16, 0.30, 0.07, 0.28, 0.38, mat)
    t.box(mb, -0.06, 0.17, 0.295, 0.06, 0.27, 0.305, "light_strip")


def study_booth(mb, x, y, ang=0.0, w=1.10, d=0.70, h=0.73, panel=1.28):
    """キャレル（個人自習ブース）。"""
    t = T(x, y, 0.0, ang)
    hw, hd = w * 0.5, d * 0.5
    t.box(mb, -hw, -hd, h - 0.04, hw, hd, h, "desk_wood")
    t.box(mb, -hw, -hd, 0.02, -hw + 0.04, hd, h - 0.04, "desk_white")
    t.box(mb, hw - 0.04, -hd, 0.02, hw, hd, h - 0.04, "desk_white")
    t.box(mb, -hw, -hd, h, hw, -hd + 0.04, panel, "desk_white")
    # 側板は天板から 0.45 m 以上高くする。0.31 m（panel-0.24＝1.04）だと
    # 天板 0.73 -> 側板 1.04 -> 前板 1.28 と伝って登れて、そこから床へ
    # 1.0〜1.3 m 落ちられた。stepOffset 0.40 で届かない高さにする (#45)
    side = max(panel - 0.10, h + 0.46)
    for sx in (-1, 1):
        t.box(mb, sx * hw - sx * 0.04, -hd, h, sx * hw, hd - 0.18,
              side, "desk_white")


# --------------------------------------------------------------------------- #
#  実験棟
# --------------------------------------------------------------------------- #
def lab_bench(mb, x, y, ang=0.0, w=3.6, d=1.5, h=0.90, shelf=True, rng=None):
    """島型の実験台（中央に試薬棚）。"""
    t = T(x, y, 0.0, ang)
    hw, hd = w * 0.5, d * 0.5
    t.box(mb, -hw, -hd, 0.10, hw, hd, h - 0.04, "desk_white")
    t.box(mb, -hw - 0.03, -hd - 0.03, h - 0.04, hw + 0.03, hd + 0.03, h,
          "counter_stone")
    t.box(mb, -hw + 0.1, -hd + 0.1, 0.0, hw - 0.1, hd - 0.1, 0.10, "metal_dark")
    n = max(1, int(w / 0.75))
    for i in range(n):
        cx = -hw + w * (i + 0.5) / n
        for sy in (-1, 1):
            t.box(mb, cx - 0.30, sy * hd, 0.30, cx + 0.30, sy * (hd + 0.015),
                  h - 0.14, "plastic_white")
    if shelf:
        t.box(mb, -hw, -0.05, h, hw, 0.05, h + 0.06, "metal_gray")
        for sx in (-1, 1):
            t.box(mb, sx * (hw - 0.1) - 0.03, -0.06, h, sx * (hw - 0.1) + 0.03,
                  0.06, h + 0.95, "metal_gray")
        for k in range(2):
            z = h + 0.34 + k * 0.40
            t.box(mb, -hw, -0.16, z, hw, 0.16, z + 0.025, "metal_gray")
            bx = -hw + 0.08
            i = 0
            while bx < hw - 0.10:
                bw = 0.07 if rng is None else rng.uniform(0.05, 0.10)
                t.box_nb(mb, bx, -0.07, z + 0.025, bx + bw, 0.07,
                         z + 0.025 + (0.16 if i % 3 else 0.24),
                         "glass_interior" if i % 3 else imats.book(i))
                bx += bw + 0.02
                i += 1


def fume_hood(mb, x, y, ang=0.0, w=1.6, d=0.9, h=2.35):
    t = T(x, y, 0.0, ang)
    hw, hd = w * 0.5, d * 0.5
    t.box(mb, -hw, -hd, 0.0, hw, hd, 0.88, "desk_white")
    t.box(mb, -hw, -hd, 0.88, hw, hd, 0.92, "counter_stone")
    t.box(mb, -hw, -hd, 0.92, -hw + 0.08, hd, h, "metal_gray")
    t.box(mb, hw - 0.08, -hd, 0.92, hw, hd, h, "metal_gray")
    t.box(mb, -hw, -hd, 0.92, hw, -hd + 0.08, h, "metal_gray")
    t.box(mb, -hw, hd - 0.05, 0.92, hw, hd, h - 0.55, "glass_interior")
    t.box(mb, -hw, hd - 0.07, h - 0.55, hw, hd, h - 0.45, "metal_gray")
    t.box(mb, -hw, -hd, h, hw, hd, h + 0.10, "metal_gray")
    t.cyl(mb, 0.0, 0.0, h + 0.10, h + 1.30, 0.13, "pipe_grey", seg=8)


def gas_cylinders(mb, x, y, ang=0.0, n=3):
    t = T(x, y, 0.0, ang)
    for i in range(n):
        cx = (i - (n - 1) * 0.5) * 0.34
        mat = "gas_green" if i % 2 == 0 else "gas_blue"
        t.cyl(mb, cx, 0.0, 0.0, 1.30, 0.115, mat, seg=8)
        t.cyl(mb, cx, 0.0, 1.30, 1.44, 0.055, "metal_gray", seg=6)
    t.box(mb, -n * 0.18, -0.02, 0.85, n * 0.18, 0.02, 0.92, "metal_gray")


def lab_sink(mb, x, y, ang=0.0, w=1.2):
    t = T(x, y, 0.0, ang)
    hw = w * 0.5
    t.box(mb, -hw, -0.32, 0.10, hw, 0.32, 0.86, "desk_white")
    t.box(mb, -hw, -0.34, 0.86, hw, 0.34, 0.90, "stainless")
    t.box(mb, -hw + 0.12, -0.24, 0.72, hw - 0.12, 0.24, 0.86, "stainless")
    t.cyl(mb, 0.0, -0.24, 0.90, 1.22, 0.022, "stainless", seg=6)
    t.box(mb, -0.02, -0.24, 1.18, 0.02, 0.02, 1.22, "stainless")


def chem_cabinet(mb, x, y, ang=0.0, w=1.2, h=1.85, rng=None):
    t = T(x, y, 0.0, ang)
    hw = w * 0.5
    t.box(mb, -hw, -0.22, 0.0, hw, 0.22, h, "metal_gray")
    for k in range(4):
        z = 0.14 + k * (h - 0.25) / 4
        t.box(mb, -hw + 0.04, -0.20, z, hw - 0.04, 0.20, z + 0.02, "metal_gray")
        bx = -hw + 0.08
        i = 0
        while bx < hw - 0.10:
            bw = 0.08 if rng is None else rng.uniform(0.06, 0.11)
            t.box_nb(mb, bx, -0.08, z + 0.02, bx + bw, 0.08, z + 0.24,
                     "glass_interior" if i % 2 else imats.book(i * 5))
            bx += bw + 0.02
            i += 1
    t.box(mb, -hw, 0.22, 0.10, -0.01, 0.235, h - 0.05, "glass_partition")
    t.box(mb, 0.01, 0.22, 0.10, hw, 0.235, h - 0.05, "glass_partition")


# --------------------------------------------------------------------------- #
#  体育館
# --------------------------------------------------------------------------- #
def hoop_base_back(arm):
    """ゴールの台座が支柱の中心から後ろへ伸びる長さ。"""
    return 0.55 if arm <= 1.05 else 0.55 + (arm - 1.05) * 0.8


def basketball_hoop(mb, x, y, ang=0.0, rim_z=3.05, arm=1.05):
    """バスケットゴール（支柱 + バックボード + リング + ネット）。

    arm は支柱の中心からバックボード裏面までの張り出し。バックボードの表面は
    arm + 0.06、リングの中心はその 0.375 m 先（FIBA の寸法）に来る。
    張り出しが長いときは台座を後ろへ伸ばし、腕の下に斜めの支えを入れる。
    """
    t = T(x, y, 0.0, ang)
    back = hoop_base_back(arm)
    t.box(mb, -0.35, -back, 0.0, 0.35, 0.55, 0.16, "metal_dark")
    t.box(mb, -0.11, -0.11, 0.16, 0.11, 0.11, rim_z + 0.90, "metal_gray")
    t.box(mb, -0.09, 0.11, rim_z + 0.55, 0.09, arm, rim_z + 0.72, "metal_gray")
    if arm > 1.05:
        kit.tube(mb, t.p(0.0, 0.11, rim_z - 0.45),
                 t.p(0.0, arm * 0.62, rim_z + 0.56), 0.035, "metal_gray",
                 seg=4)
    # 1.80 x 1.05 m の板。下端はリングの 0.15 m 下
    face = arm + 0.06
    t.box(mb, -0.90, arm, rim_z - 0.15, 0.90, face, rim_z + 0.90,
          "backboard_white")
    # 板の表のターゲット枠（外寸 0.59 x 0.45、線幅 0.05、下辺の上端がリングの高さ）
    fy = face + 0.004
    for u0, z0, u1, z1 in ((-0.295, rim_z - 0.05, 0.295, rim_z),
                           (-0.295, rim_z + 0.35, 0.295, rim_z + 0.40),
                           (-0.295, rim_z, -0.245, rim_z + 0.35),
                           (0.245, rim_z, 0.295, rim_z + 0.35)):
        mb.add_face([t.p(u0, fy, z0), t.p(u0, fy, z1), t.p(u1, fy, z1),
                     t.p(u1, fy, z0)], "court_line_blue")
    # リング（内径 0.45、板の表から 0.15 m 離す）。上から見て輪に見えるよう管でつなぐ
    rr = 0.235
    p = t.p2(0.0, face + 0.15 + 0.225)
    ring = [(p[0] + rr * math.cos(math.pi * 2 * k / 12),
             p[1] + rr * math.sin(math.pi * 2 * k / 12), rim_z)
            for k in range(12)]
    for k in range(12):
        kit.tube(mb, ring[k], ring[(k + 1) % 12], 0.010, "hoop_orange", seg=3)
    t.box(mb, -0.06, face, rim_z - 0.10, 0.06, face + 0.15, rim_z + 0.01,
          "hoop_orange")
    for k in range(8):
        a = math.pi * 2 * k / 8
        px = p[0] + rr * math.cos(a)
        py = p[1] + rr * math.sin(a)
        kit.tube(mb, (px, py, rim_z), (p[0] + 0.10 * math.cos(a),
                                       p[1] + 0.10 * math.sin(a), rim_z - 0.42),
                 0.012, "net_white", seg=3)


def bleachers(mb, x0, x1, y_front, rows=8, rise=0.42, run=0.78, ang=0.0,
              seat_mat="chair_blue", deck="concrete_light", seats_every=0.50):
    """階段状の観覧席。+Y 側へ上がる。戻り値は座席位置の数。"""
    count = 0
    t = T(0.0, 0.0, 0.0, ang)
    for r in range(rows):
        y = y_front + run * r
        z = rise * r
        t.box(mb, x0, y, z, x1, y + run, z + rise, deck)
        n = max(1, int((x1 - x0) / seats_every))
        for i in range(n):
            cx = x0 + (x1 - x0) * (i + 0.5) / n
            t.box_nb(mb, cx - seats_every * 0.42, y + 0.10, z + rise,
                     cx + seats_every * 0.42, y + 0.55, z + rise + 0.05,
                     seat_mat)
            count += 1
    return count


def stage(mb, x0, y0, x1, y1, h=0.90, deck="floor_wood", skirt="wall_accent_navy",
          steps=None, step_w=2.4):
    """舞台。steps は前面の上り段の中心 x の並び（省略時は中央に 1 か所）。"""
    kit.box(mb, x0, y0, 0.0, x1, y1, h - 0.06, skirt)
    kit.box(mb, x0 - 0.06, y0 - 0.06, h - 0.06, x1 + 0.06, y1 + 0.06, h, deck)
    # 前面の上り段
    hw = step_w * 0.5
    for sx in (steps if steps is not None else ((x0 + x1) * 0.5,)):
        kit.box(mb, sx - hw, y0 - 0.9, 0.0, sx + hw, y0 - 0.45, h * 0.5, deck)
        kit.box(mb, sx - hw, y0 - 0.45, 0.0, sx + hw, y0, h, deck)


def roof_truss(mb, x0, x1, y, z, depth=1.6, seg=None, mat="metal_gray"):
    """平行弦トラス 1 本（x 方向に架ける）。"""
    n = seg or max(4, int((x1 - x0) / 3.0))
    kit.tube(mb, (x0, y, z), (x1, y, z), 0.09, mat, seg=4)
    kit.tube(mb, (x0, y, z - depth), (x1, y, z - depth), 0.09, mat, seg=4)
    for i in range(n + 1):
        px = x0 + (x1 - x0) * i / n
        kit.tube(mb, (px, y, z), (px, y, z - depth), 0.05, mat, seg=4)
        if i < n:
            nx = x0 + (x1 - x0) * (i + 1) / n
            zz = (z, z - depth) if i % 2 == 0 else (z - depth, z)
            kit.tube(mb, (px, y, zz[0]), (nx, y, zz[1]), 0.045, mat, seg=4)


def wall_bars(mb, x0, x1, y, z0=0.4, z1=2.6, ang=0.0):
    """肋木（体育館の壁面）。"""
    t = T(0.0, 0.0, 0.0, ang)
    for sx in (x0, x1):
        t.box(mb, sx - 0.05, y - 0.08, z0 - 0.1, sx + 0.05, y + 0.08, z1 + 0.1,
              "wood")
    k = z0
    while k <= z1:
        p0 = t.p(x0, y, k)
        p1 = t.p(x1, y, k)
        kit.tube(mb, p0, p1, 0.022, "wood", seg=5)
        k += 0.17


# --------------------------------------------------------------------------- #
#  その他
# --------------------------------------------------------------------------- #
def pendant(mb, x, y, z_ceiling, drop=1.6, r=0.22, mat="metal_dark",
            bulb="light_strip"):
    kit.cyl(mb, x, y, z_ceiling - drop, z_ceiling, 0.012, mat, seg=4)
    kit.cyl(mb, x, y, z_ceiling - drop - 0.18, z_ceiling - drop, r, mat, seg=8)
    kit.cyl(mb, x, y, z_ceiling - drop - 0.20, z_ceiling - drop - 0.18, r * 0.8,
            bulb, seg=8)


def greenhouse_bench(mb, x, y, ang=0.0, w=4.0, d=1.1, h=0.78, rng=None):
    t = T(x, y, 0.0, ang)
    hw, hd = w * 0.5, d * 0.5
    t.box(mb, -hw, -hd, h - 0.05, hw, hd, h, "metal_gray")
    for sx in (-1, 1):
        for sy in (-1, 1):
            t.box_nb(mb, sx * (hw - 0.2) - 0.03, sy * (hd - 0.15) - 0.03, 0.0,
                     sx * (hw - 0.2) + 0.03, sy * (hd - 0.15) + 0.03,
                     h - 0.05, "metal_gray")
    n = max(1, int(w / 0.40))
    m = max(1, int(d / 0.40))
    for i in range(n):
        for j in range(m):
            cx = -hw + w * (i + 0.5) / n
            cy = -hd + d * (j + 0.5) / m
            if rng is not None and rng.random() < 0.18:
                continue
            rr = 0.13 if rng is None else rng.uniform(0.10, 0.16)
            p = t.p2(cx, cy)
            kit.cyl(mb, p[0], p[1], h, h + 0.17, rr, "plant_pot", seg=6)
            kit.blob(mb, p[0], p[1], h + 0.34, rr * 1.7, rr * 1.7, 0.18,
                     "plant_green", seg=5, rings=2)


def irrigation_pipe(mb, x0, x1, y, z, mat="pipe_grey", drops=6):
    kit.tube(mb, (x0, y, z), (x1, y, z), 0.045, mat, seg=6)
    for i in range(drops):
        px = x0 + (x1 - x0) * (i + 0.5) / drops
        kit.tube(mb, (px, y, z), (px, y, z - 0.45), 0.018, mat, seg=4)
        kit.box_c(mb, px, y, z - 0.52, 0.07, 0.07, z - 0.45, "metal_gray")
