"""実験棟 (lab1 / lab2): 廊下 + 実験室が並ぶ。実験台・ドラフトチャンバー・ボンベ・流し・薬品庫。"""

import math

from . import common, furniture as F, kit, shell

CEIL = 3.20
Z_TOP = 4.10
COR_D = 2.60         # 廊下の奥行き


def build(c):
    s = c.spec
    ix0, iy0, ix1, iy1 = s.inner()
    cor_y0, cor_y1 = iy0 + 1.2, iy0 + 1.2 + COR_D

    common.envelope(c, CEIL, "floor_resin_grey", door_w=3.2, z_top=Z_TOP,
                    sill=1.05, header=0.40, seg=2.8, ceil=True,
                    ceil_mat="ceiling_grid", grid=1.5)

    common.entry_kit(c, CEIL, door_w=3.2)
    pts = shell.ceiling_lights(c.wall, ix0 + 1.0, iy0 + 0.6, ix1 - 1.0,
                               iy1 - 0.6, CEIL, sx=4.0, sy=3.6,
                               mat="light_strip", w=0.24, l=1.5)
    c.lights_from(pts, CEIL, energy=112.0, step=2)

    # 廊下と実験室を分ける間仕切り（各室のドア位置に開口）
    rooms = _room_spans(ix0, ix1)
    gaps = []
    for (rx0, rx1, _kind) in rooms:
        d = (rx0 + rx1) * 0.5 - ix0
        gaps.append((d - 0.6, d + 0.6))
    shell.partition(c.wall, (ix0, cor_y1), (ix1, cor_y1), 0.0, CEIL,
                    "wall_white", thick=0.16, gaps=gaps, glass_top=True,
                    glass_z=2.10)

    cor = c.furn("corridor")
    common.corridor_run(c, cor, ix0 + 2.0, ix1 - 2.0, cor_y0 + 0.1, CEIL,
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
        shell.partition(c.wall, (rx1, cor_y1), (rx1, iy1), 0.0, CEIL,
                        "wall_white", thick=0.14)
        if kind == "lab":
            pois = set()
            if i == lab_idx[0]:
                pois.add("lab")
            if i == lab_idx[-1]:
                pois.add("lab_b")
            _lab_room(c, rx0, cor_y1, rx1, iy1, i, pois)
        else:
            _prep_room(c, rx0, cor_y1, rx1, iy1, i, i == prep_idx[-1])

    # 廊下のロッカーと洗い場
    F.locker_bank(cor, ix0 + 2.0, ix0 + 8.0, cor_y0 + 0.2, ang=0.0, h=1.80)
    shell.trash_bins(cor, ix1 - 3.0, cor_y0 + 0.6, ang=0.0, n=3)
    shell.vending(cor, ix1 - 1.4, cor_y0 + 0.5, ang=0.0)
    common.window_planters(c, cor, ix0 + 10.0, ix1 - 6.0, iy0 + 0.6, n=3)

    c.cam("", (ix0 + 1.0, cor_y0 + 1.32, 1.62), (ix1 - 5.0, cor_y0 + 1.42, 1.42),
          lens=24.0)
    rx0, rx1, _k = rooms[0]
    c.cam("room", (rx1 - 1.3, cor_y1 + 0.9, 2.15), (rx0 + 1.8, iy1 - 2.4, 0.95),
          lens=20.0)
    c.note("廊下 + 実験室 %d 室（実験台・ドラフト・ボンベ・薬品庫）" % len(rooms))


# --------------------------------------------------------------------------- #
def _room_spans(ix0, ix1):
    """幅を見て実験室と準備室に割り付ける。"""
    width = ix1 - ix0
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
                F.stool(mb, sx, by + sgn * 1.15, ang=0.0, mat="chair_grey",
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
        F.bookshelf(mb, x1 - 0.55, y0 + 4.4 + k * 1.05, ang=-math.pi * 0.5,
                    w=1.0, h=1.75, shelves=4, rng=rng, body="metal_gray",
                    books=False)
    F.lab_sink(mb, x1 - 0.85, y0 + 7.2, ang=-math.pi * 0.5, w=1.4)
    F.whiteboard(mb, x0 + 0.65, y0 + 5.6, 1.05, ang=math.pi * 0.5, w=2.2,
                 h=1.1)

    # 前方: ホワイトボードと教員机
    F.whiteboard(mb, cx, y0 + 0.16, 1.05, ang=0.0, w=3.0, h=1.2)
    F.desk(mb, x0 + 1.6, y0 + 1.5, ang=0.0, w=1.4, d=0.7)
    F.chair_min(mb, F.T(x0 + 1.6, y0 + 2.2, 0.0, math.pi), mat="chair_blue")
    F.pc_tower(mb, x0 + 2.4, y0 + 1.5, ang=0.0)
    F.monitor(mb, x0 + 1.6, y0 + 1.7, 0.72, ang=math.pi)

    shell.fire_extinguisher(mb, x0 + 0.5, y0 + 0.8)
    if "lab" in pois:
        c.poi("lab", cx, y0 + 2.0, 0.0)
        c.npc(cx - 1.8, y0 + 4.2, 0.0)
        c.npc(cx + 2.0, y0 + 7.0, 0.0)
    if "lab_b" in pois:
        # lab と同室になったときは奥側（2 列目の実験台の脇）に離して置く
        by = y0 + 2.0 if "lab" not in pois else min(y0 + 7.2, y1 - 2.0)
        c.poi("lab_b", cx, by, 0.0)
        c.npc(cx, y0 + 5.2, 0.0)


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
        c.poi("prep", (x0 + x1) * 0.5, y0 + 1.6, 0.0)
