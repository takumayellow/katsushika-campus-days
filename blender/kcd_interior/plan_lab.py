"""実験棟 (lab1 / lab2): 廊下 + 実験室が並ぶ。実験台・ドラフトチャンバー・ボンベ・流し・薬品庫。

入口が長辺にある棟（lab1）は、入口の面に沿って廊下を通し、その奥に部屋を並べる。
入口が短辺にある棟（lab2 は東の妻面）は、入口の内側を玄関ホールにし、廊下をローカル
-X の壁（lab2 では南、第1実験棟の側）に沿って奥へ通して、部屋を +X（北の窓側）に並べる。
"""

import math

from . import common, furniture as F, kit, shell

CEIL = 3.20
Z_TOP = 4.10
COR_D = 2.60         # 廊下の奥行き
HALL_D = 3.60        # 入口が短辺にある棟の玄関ホールの奥行き
PREP_W = 7.60        # 同じく、準備室の幅（残りを実験室にする）
LAB_W = 10.5         # 同じく、実験室 1 室の最小幅


def build(c):
    s = c.spec
    ix0, iy0, ix1, iy1 = s.inner()

    common.envelope(c, CEIL, "floor_resin_grey", door_w=3.2, z_top=Z_TOP,
                    sill=1.05, header=0.40, seg=2.8, ceil=True,
                    ceil_mat="ceiling_grid", grid=1.5)

    common.entry_kit(c, CEIL, door_w=3.2)
    pts = shell.ceiling_lights(c.wall, ix0 + 1.0, iy0 + 0.6, ix1 - 1.0,
                               iy1 - 0.6, CEIL, sx=4.0, sy=3.6,
                               mat="light_strip", w=0.24, l=1.5)
    c.lights_from(pts, CEIL, energy=112.0, step=2)

    if s.depth <= s.width:
        _wing(c, ix0, ix1, iy0, iy1)
        return

    hall_y1 = iy0 + HALL_D
    _hall(c, ix0, ix1, iy0, hall_y1)
    # 廊下と部屋の並びを、-90° 回した座標で組む（ローカル (x, y) -> (y, -x)）。
    # 回した座標の x はホール側の端 -hall_y1 から奥の壁 -iy1 まで、y は外周の -X 側
    # ix0（廊下の窓）から +X 側 ix1（部屋の奥の窓）まで。
    with common.turned(c, 0.0, 0.0, -math.pi * 0.5):
        _wing(c, -iy1, -hall_y1, ix0, ix1, prep_w=PREP_W)


def _wing(c, ax0, ax1, wy0, wy1, prep_w=None):
    """廊下と部屋の並び。廊下は x に沿って ax0..ax1、y = wy0 の窓ぎわから奥行き
    1.2 + COR_D、部屋はその先 wy1 まで。prep_w は _room_spans へ渡す。"""
    cor_y0, cor_y1 = wy0 + 1.2, wy0 + 1.2 + COR_D

    # 廊下と実験室を分ける間仕切り（各室のドア位置に開口）
    rooms = _room_spans(ax0, ax1, prep_w)
    gaps = []
    for (rx0, rx1, _kind) in rooms:
        d = (rx0 + rx1) * 0.5 - ax0
        gaps.append((d - 0.6, d + 0.6))
    shell.partition(c.wall, (ax0, cor_y1), (ax1, cor_y1), 0.0, CEIL,
                    "wall_white", thick=0.16, gaps=gaps, glass_top=True,
                    glass_z=2.10)

    cor = c.furn("corridor")
    common.corridor_run(c, cor, ax0 + 2.0, ax1 - 2.0, cor_y0 + 0.1, CEIL,
                        pitch=10.0)
    for (rx0, rx1, kind) in rooms:
        dx = (rx0 + rx1) * 0.5
        shell.door(c.wall, dx, cor_y1, ang=0.0, w=1.10, h=2.10,
                   leaf="desk_white", glass="glass_partition")
        shell.wall_sign(cor, dx + 0.95, cor_y1 - 0.10, 2.05, ang=math.pi,
                        w=0.70, h=0.28)
        c.sign(dx + 0.95, cor_y1 - 0.10, 2.05)
        shell.exit_sign(c.wall, dx, cor_y1 - 0.06, 2.85, ang=math.pi)
        # ドアの脇の壁に掲示板（廊下側を向く）
        if rx0 + 1.2 < dx - 1.0:
            shell.notice_board(cor, rx0 + 1.2, cor_y1 - 0.10, 1.58,
                               ang=math.pi, w=1.8, h=1.05, sheets=8,
                               rng=c.rng)

    # 各室。POI は部屋数に依存させない: 最初の実験室に lab、最後の実験室に
    # lab_b（実験室が 1 室しか無ければ同じ室の奥）、最後の準備室に prep。
    lab_idx = [i for i, r in enumerate(rooms) if r[2] == "lab"]
    prep_idx = [i for i, r in enumerate(rooms) if r[2] == "prep"]
    for i, (rx0, rx1, kind) in enumerate(rooms):
        shell.partition(c.wall, (rx1, cor_y1), (rx1, wy1), 0.0, CEIL,
                        "wall_white", thick=0.14)
        if kind == "lab":
            pois = set()
            if i == lab_idx[0]:
                pois.add("lab")
            if i == lab_idx[-1]:
                pois.add("lab_b")
            _lab_room(c, rx0, cor_y1, rx1, wy1, i, pois)
        else:
            _prep_room(c, rx0, cor_y1, rx1, wy1, i, i == prep_idx[-1])

    # 廊下のロッカーと洗い場
    F.locker_bank(cor, ax0 + 2.0, ax0 + 8.0, cor_y0 + 0.2, ang=0.0, h=1.80)
    shell.trash_bins(cor, ax1 - 3.0, cor_y0 + 0.6, ang=0.0, n=3)
    shell.vending(cor, ax1 - 1.4, cor_y0 + 0.5, ang=0.0)
    # 鉢は 3 つまで、廊下が短ければ 3 m に 1 つ
    span = (ax1 - 6.0) - (ax0 + 10.0)
    common.window_planters(c, cor, ax0 + 10.0, ax1 - 6.0, wy0 + 0.6,
                           n=min(3, max(1, int(span / 3.0))))

    c.cam("", (ax0 + 1.0, cor_y0 + 1.32, 1.62), (ax1 - 5.0, cor_y0 + 1.42, 1.42),
          lens=24.0)
    rx0, rx1, _k = rooms[0]
    c.cam("room", (rx1 - 1.3, cor_y1 + 0.9, 2.15), (rx0 + 1.8, wy1 - 2.4, 0.95),
          lens=20.0)
    c.note("廊下 + 実験室 %d 室（実験台・ドラフト・ボンベ・薬品庫）" % len(rooms))


def _hall(c, ix0, ix1, iy0, iy1):
    """入口が短辺にある棟の玄関ホール（ix0..ix1 x iy0..iy1）。

    奥の壁（iy1 + 0.1 の準備室の側壁）は _wing が立てる。-X 側は廊下へ抜ける。
    入口の右手（+X）に掲示板・ベンチ・鉢を置き、入口から廊下への通り道は空ける。
    """
    mb = c.furn("hall")
    wall_y = iy1 + 0.1 - 0.07     # 準備室の側壁（厚さ 0.14）のホール側の面
    bx = (1.6 + ix1) * 0.5        # 入口（幅 3.2）の右脇から +X の壁までの中央
    shell.notice_board(mb, bx, wall_y - 0.02, 1.58, ang=math.pi, w=2.4,
                       h=1.05, sheets=10, rng=c.rng)
    F.bench(mb, bx, wall_y - 0.45, ang=math.pi, w=1.8)
    c.seats += 1
    shell.planter(mb, ix1 - 0.7, wall_y - 0.7, r=0.40, h=0.44, leaf_h=1.4)
    shell.planter(mb, ix1 - 0.7, iy0 + 0.8, r=0.40, h=0.44, leaf_h=1.4)
    c.note("玄関ホール（入口が短辺）から廊下を奥へ")


# --------------------------------------------------------------------------- #
def _room_spans(ix0, ix1, prep_w=None):
    """幅を見て実験室と準備室に割り付ける。

    prep_w を渡すと、最後の準備室をその幅にし、残りを LAB_W 以上の実験室で等分する。
    """
    width = ix1 - ix0
    if prep_w is not None:
        lab_w = width - prep_w
        n = max(1, int(lab_w / LAB_W))
        edges = [ix0 + lab_w * i / n for i in range(n + 1)] + [ix1]
        kinds = ["lab"] * n + ["prep"]
        return [(edges[i] + 0.1, edges[i + 1] - 0.1, kinds[i])
                for i in range(n + 1)]
    n = max(2, int(width / 9.5))
    out = []
    for i in range(n):
        rx0 = ix0 + width * i / n
        rx1 = ix0 + width * (i + 1) / n
        # 最後の室は必ず準備室（prep の POI を常に置けるようにする）。
        # 途中は 4 室ごとに準備室を挟む。実験室は少なくとも 1 室残る。
        kind = "prep" if (i == n - 1 or i % 4 == 3) else "lab"
        out.append((rx0 + 0.1, rx1 - 0.1, kind))
    return out


def _lab_room(c, x0, y0, x1, y1, idx, pois=frozenset()):
    """pois: この室に置く POI 名の集合（"lab" / "lab_b"）。"""
    mb = c.furn("lab%02d" % (idx + 1))
    rng = c.rng
    cx = (x0 + x1) * 0.5
    kit.plate(c.floor, x0, y0, x1, y1, 0.014, "floor_resin_grey")

    # 島型の実験台を 2 列
    bw = min(7.4, (x1 - x0) - 2.0)
    for k in range(2):
        by = y0 + 3.0 + k * 4.2
        if by > y1 - 2.2:
            break
        F.lab_bench(mb, cx - 0.4, by, ang=0.0, w=bw, d=1.50, h=0.90,
                    shelf=True, rng=rng)
        for sgn in (-1, 1):
            for i in range(5):
                sx = cx - 0.4 - bw * 0.5 + 1.0 + i * (bw - 2.0) / 4.0
                # 実験台の向こう側（+Y 側）の丸椅子は台の方（-Y）へ向ける。
                # 両側とも ang=0 で、北側 15 脚が台に背を向けていた（#42）
                F.stool(mb, sx, by + sgn * 1.15,
                        ang=0.0 if sgn < 0 else math.pi, mat="chair_grey",
                        h=0.66)
                c.seats += 1

    # 壁ぎわ: ドラフトチャンバー・薬品庫・流し
    F.fume_hood(mb, x0 + 1.4, y1 - 0.8, ang=math.pi, w=1.6, d=0.9)
    F.fume_hood(mb, x0 + 3.3, y1 - 0.8, ang=math.pi, w=1.6, d=0.9)
    F.chem_cabinet(mb, x1 - 1.6, y1 - 0.8, ang=math.pi, w=1.2, h=1.85, rng=rng)
    F.chem_cabinet(mb, x1 - 3.0, y1 - 0.8, ang=math.pi, w=1.2, h=1.85, rng=rng)
    F.lab_sink(mb, cx + 1.6, y1 - 0.8, ang=math.pi, w=1.6)
    F.gas_cylinders(mb, x1 - 0.9, y0 + 1.4, ang=math.pi * 0.5, n=3)
    # 側面の壁ぎわ: 器具棚と予備の流し
    for k in range(2):
        F.bookshelf(mb, x1 - 0.55, y0 + 4.4 + k * 1.05, ang=math.pi * 0.5,
                    w=1.0, h=1.75, shelves=4, rng=rng, body="metal_gray",
                    books=False)
    F.lab_sink(mb, x1 - 0.85, y0 + 7.2, ang=-math.pi * 0.5, w=1.4)
    F.whiteboard(mb, x0 + 0.65, y0 + 5.6, 1.05, ang=-math.pi * 0.5, w=2.2,
                 h=1.1)

    # 前方: ホワイトボードと教員机。ボードは廊下からのドア（部屋の中央 ±0.6 m）
    # の真正面に幅 3.0 m・高さ 0.45〜1.65 m で立っていて、実験室に入れなかった
    # ので、ドアの東どなりへずらす（#42）
    F.whiteboard(mb, min(cx + 2.2, x1 - 1.8), y0 + 0.16, 1.05, ang=0.0,
                 w=3.0, h=1.2)
    F.desk(mb, x0 + 1.6, y0 + 1.1, ang=0.0, w=1.4, d=0.7)
    F.chair_min(mb, F.T(x0 + 1.6, y0 + 1.8, 0.0, math.pi), mat="chair_blue")
    F.pc_tower(mb, x0 + 2.4, y0 + 1.1, ang=0.0)
    F.monitor(mb, x0 + 1.6, y0 + 1.3, 0.72, ang=math.pi)

    shell.fire_extinguisher(mb, x0 + 0.5, y0 + 0.8)
    # 実験台の島は y0+3.0 と y0+7.2、丸椅子はその ±1.15 にあるので、POI と NPC は
    # 島と島の間（y0+5.1）と 2 島目の奥の通路に置く。以前は実験台や丸椅子の中で、
    # 体のまわりの空きが 0.02〜0.22 m しか無く、どこからも近づけなかった（#42）
    aisle = y0 + 5.1
    back = min(y0 + 9.2, y1 - 1.9)
    if "lab" in pois:
        c.poi("lab", cx, aisle, 0.0)
        c.npc(cx - 2.6, aisle, 0.0)
        c.npc(cx + 2.6, aisle, 0.0)
    if "lab_b" in pois:
        # lab と同室になったときは奥側（2 列目の実験台の奥）に離して置く
        c.poi("lab_b", cx, aisle if "lab" not in pois else back, 0.0)
        c.npc(cx - 1.4, back, 0.0)


def _prep_room(c, x0, y0, x1, y1, idx, place_poi=False):
    """準備室（薬品・ガラス器具の保管）。place_poi なら prep の POI を置く。"""
    mb = c.furn("prep%02d" % (idx + 1))
    rng = c.rng
    kit.plate(c.floor, x0, y0, x1, y1, 0.014, "floor_tile_grey")
    n = int((x1 - x0 - 1.6) / 1.35)
    for i in range(n):
        F.chem_cabinet(mb, x0 + 1.0 + i * 1.35, y1 - 0.8, ang=math.pi,
                       w=1.2, h=1.85, rng=rng)
    F.lab_bench(mb, (x0 + x1) * 0.5, y0 + 2.4, ang=0.0,
                w=min(5.0, x1 - x0 - 2.0), d=1.0, h=0.90, shelf=False, rng=rng)
    F.gas_cylinders(mb, x0 + 0.9, y0 + 1.4, ang=0.0, n=4)
    F.lab_sink(mb, x1 - 1.4, y0 + 1.2, ang=0.0, w=1.2)
    for i in range(3):
        F.bookshelf(mb, x0 + 1.0 + i * 1.0, y0 + 0.6, ang=0.0, w=0.9, h=1.90,
                    shelves=5, rng=rng)
    if place_poi:
        # 実験台（y0+1.9 から）と入口の間。y0+1.6 だと台まで 0.27 m だった（#42）
        c.poi("prep", (x0 + x1) * 0.5, y0 + 1.35, 0.0)
