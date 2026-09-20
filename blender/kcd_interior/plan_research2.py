"""第2研究棟 1F: 1,400 席の食堂（1〜2F 吹き抜け）。q_lunch の舞台。"""

import math

from . import common, furniture as F, kit, shell

CEIL = 8.20          # 吹き抜け天井
Z_TOP = 8.90
GALLERY_Z = 4.20     # 2F 回廊の床
TARGET_SEATS = 1400


def build(c):
    s = c.spec
    ix0, iy0, ix1, iy1 = s.inner()

    common.envelope(c, CEIL, "floor_tile_white", door_w=8.0, z_top=Z_TOP,
                    sill=0.50, header=0.70, seg=3.2, ceil=False)
    common.entry_kit(c, 3.2, door_w=8.0)
    shell.ceiling(c.wall, ix0, iy0, ix1, iy1, CEIL, "ceiling_dark", grid=0.0)

    kit.plate(c.floor, ix0, iy0, ix1, iy1, 0.014, "floor_tile_white")

    # ---- 柱（吹き抜けを通す） ----
    for i in range(6):
        px = ix0 + 6.0 + i * 13.5
        for py in (iy0 + 10.0, iy0 + 26.0):
            shell.column(c.wall, px, py, 0.0, CEIL, size=0.72,
                         mat="concrete_light")

    # ---- 2F 回廊（吹き抜けの縁） ----
    gal = c.furn("gallery")
    gal_seats = []           # 回廊に置く席列 [(x0, x1, y)]
    for side, (gy0, gy1) in enumerate(((iy0, iy0 + 5.2), (iy1 - 5.2, iy1))):
        shell.floor(c.wall, ix0, gy0, ix1, gy1, GALLERY_Z, "floor_tile_grey",
                    thickness=0.50, side_mat="concrete_light")
        edge = gy1 if side == 0 else gy0
        shell.railing(c.wall, [(ix0, edge), (ix1, edge)], GALLERY_Z, h=1.10,
                      mat="metal_white", glass="glass_partition", post=2.4)
        pts = shell.ceiling_lights(c.wall, ix0 + 2.0, gy0 + 1.0, ix1 - 2.0,
                                   gy1 - 0.6, GALLERY_Z + 3.10, sx=6.0, sy=4.0)
        shell.ceiling(c.wall, ix0, gy0, ix1, gy1, GALLERY_Z + 3.10,
                      "ceiling_white", grid=1.8)
        c.lights_from(pts, GALLERY_Z + 3.1, energy=180.0, step=3)
        # 窓ぎわの丸テーブル
        ty = gy0 + 1.35 if side == 0 else gy1 - 1.35
        for i in range(6):
            px = ix0 + 7.0 + i * 12.0
            F.round_table(gal, px, ty, r=0.45, h=0.73, z=GALLERY_Z)
            for k in range(3):
                a = math.pi * 2 * k / 3
                F.chair_min(gal, F.T(px + 0.95 * math.cos(a),
                                     ty + 0.95 * math.sin(a),
                                     GALLERY_Z, a + math.pi),
                            mat="chair_orange")
                c.seats += 1
        # 吹き抜けを見下ろす 4 人掛けの列
        gal_seats.append((ix0 + 2.5, ix1 - 2.5,
                          gy1 - 2.6 if side == 0 else gy0 + 2.6))
    # 回廊へ上がる階段
    shell.stair_flight(c.wall, ix1 - 5.0, iy0 + 6.0, iy0 + 13.6, 0.0,
                       GALLERY_Z, width=2.4, tread_mat="floor_tile_grey")
    shell.railing(c.wall, [(ix1 - 3.6, iy0 + 5.2), (ix1 - 3.6, iy0 + 13.6)],
                  GALLERY_Z, h=1.05, mat="metal_white")

    # ---- 配膳カウンター（奥の壁ぎわ） ----
    srv = c.furn("serving")
    sy = iy1 - 6.6
    F.serving_line(srv, ix0 + 8.0, ix0 + 46.0, sy, depth=1.35, h=0.95)
    for i in range(6):
        px = ix0 + 10.0 + i * 7.0
        shell.hanging_sign(srv, px, sy - 1.9, 3.60, w=2.0, h=0.44, drop=0.4)
        c.sign(px, sy - 1.9, 2.80)
    c.poi("counter", ix0 + 27.0, sy - 2.4, 0.0)
    for i in range(5):
        c.npc(ix0 + 12.0 + i * 8.0, sy - 0.95, 0.0)
    # 厨房の壁（カウンター背後）
    shell.partition(c.wall, (ix0 + 6.0, sy + 1.4), (ix0 + 48.0, sy + 1.4),
                    0.0, 3.60, thick=0.22, mat="wall_grey")
    shell.ceiling(c.wall, ix0 + 6.0, sy + 1.4, ix0 + 48.0, iy1, 3.60,
                  "ceiling_white", grid=1.8)
    shell.ceiling_lights(c.wall, ix0 + 8.0, sy + 2.4, ix0 + 46.0, iy1 - 1.0,
                         3.60, sx=6.0, sy=4.0)

    # ---- 券売機・返却口 ----
    ops = c.furn("ops")
    for i in range(6):
        F.ticket_machine(ops, ix0 + 3.2 + i * 1.05, iy0 + 7.4, ang=math.pi)
    c.poi("ticket", ix0 + 6.0, iy0 + 6.0, 0.0)
    c.npc(ix0 + 6.4, iy0 + 6.2, 0.0)
    F.return_counter(ops, ix1 - 8.0, iy1 - 2.0, ang=math.pi, w=3.2)
    F.tray_rack(ops, ix1 - 11.0, iy1 - 2.2, ang=math.pi, w=1.0)
    F.tray_rack(ops, ix0 + 47.5, sy - 1.2, ang=0.0, w=1.0)
    c.poi("tray_return", ix1 - 8.0, iy1 - 3.4, 0.0)
    for i in range(3):
        shell.trash_bins(ops, ix1 - 14.0 + i * 1.6, iy1 - 2.2, ang=math.pi, n=3)
    shell.vending(ops, ix1 - 3.4, iy0 + 8.0, ang=-math.pi * 0.5)
    shell.vending(ops, ix1 - 3.4, iy0 + 9.6, ang=-math.pi * 0.5, mat="fm_green")

    # ---- 客席 ----
    left = _dining(c, ix0 + 2.4, iy0 + 8.6, ix1 - 2.4, sy - 3.0,
                   TARGET_SEATS)
    # 1F で足りない分は 2F 回廊の席列で埋める
    gal_n = 0
    for gx0, gx1, gy in gal_seats:
        if left <= 0:
            break
        before = left
        left = _gallery_seats(c, gal, gx0, gx1, gy, left)
        gal_n += before - left
    c.note("2F 回廊 %d 席" % gal_n)

    # ---- 窓際のカウンター席（残りの席数をここで満たす） ----
    left = max(0, TARGET_SEATS - c.seats)
    if left > 0:
        cnt = c.furn("window_counter")
        cw_x0, cw_x1 = ix0 + 6.0, ix1 - 6.0
        kit.box(cnt, cw_x0, iy1 - 1.55, 0.72, cw_x1, iy1 - 1.05, 0.78,
                "counter_wood")
        n = int((cw_x1 - cw_x0) / 0.70)
        placed = 0
        for i in range(n):
            if left <= 0:
                break
            sx = cw_x0 + 0.35 + i * 0.70
            if i % 12 == 11:
                continue
            kit.box(cnt, sx - 0.05, iy1 - 1.55, 0.0, sx + 0.05, iy1 - 1.50,
                    0.72, "metal_dark")
            F.stool(cnt, sx, iy1 - 1.95, ang=0.0, mat="sb_wood", h=0.74)
            c.seats += 1
            left -= 1
            placed += 1
        c.note("窓際カウンター席 %d" % placed)
    c.note("食堂 合計 %d 席" % c.seats)

    # ---- 屋上緑化に面した大ガラスの内側 ----
    grn = c.furn("green")
    common.window_planters(c, grn, ix0 + 4.0, ix1 - 4.0, iy1 - 0.9, n=10)

    # ---- 吹き抜けのペンダント照明 ----
    for i in range(10):
        px = ix0 + 5.0 + i * 8.2
        for py in (iy0 + 13.0, iy0 + 22.0, iy0 + 30.0):
            F.pendant(c.wall, px, py, CEIL, drop=3.10, r=0.34,
                      mat="metal_dark", bulb="light_panel")
            if i % 2 == 0:
                c.light(px, py, CEIL - 3.5, 700.0, 1.2)
    c.light(0.0, iy0 + 18.0, 6.0, 2200.0, 3.0)

    c.cam("", (ix0 + 8.0, iy0 + 7.0, 2.35), (ix0 + 30.0, iy1 - 8.0, 1.60),
          lens=17.0)
    c.cam("=interior_cafeteria", (ix1 - 12.0, iy0 + 11.0, 3.20),
          (ix0 + 20.0, iy1 - 8.0, 1.30), lens=19.0)
    c.note("1〜2F 吹き抜けの大食堂。配膳カウンター 38 m・券売機 6 台・返却口・2F 回廊")


def _dining(c, x0, y0, x1, y1, budget):
    """4 人掛け・6 人掛けのテーブルを整列させ、残り席数を返す。"""
    tbl = c.furn("tables")
    chs = c.furn("chairs")
    seats = 0
    aisle_every = 8          # 何列ごとに通路を空けるか
    row_pitch = 2.28
    rows = int((y1 - y0) / row_pitch)
    for r in range(rows):
        yy = y0 + 0.9 + r * row_pitch
        if yy + 1.4 > y1:
            break
        six = (r % 3 == 1)   # 3 列に 1 列を 6 人掛けにする
        tw = 2.55 if six else 1.65
        pitch = tw + 0.55
        n = int((x1 - x0) / pitch)
        for i in range(n):
            if i % aisle_every == aisle_every - 1:
                continue
            px = x0 + pitch * 0.5 + i * pitch
            if px + tw * 0.5 > x1:
                break
            F.table(tbl, px, yy, ang=0.0, w=tw, d=0.80, h=0.72,
                    top="desk_wood" if (r + i) % 2 else "desk_white")
            per = 3 if six else 2
            for sgn in (-1, 1):
                for k in range(per):
                    cxx = px + (k - (per - 1) * 0.5) * (tw / per)
                    F.chair_canteen(
                        chs, F.T(cxx, yy + sgn * 0.78, 0.0,
                                 0.0 if sgn > 0 else math.pi),
                        mat="chair_blue" if (r + k) % 3 else "chair_orange")
                    seats += 1
            if seats >= budget:
                break
        if seats >= budget:
            break
    c.seats += seats
    c.note("食堂 1F %d 席" % seats)
    c.poi("hall", (x0 + x1) * 0.5, (y0 + y1) * 0.5, 0.0)
    for i in range(6):
        c.npc(x0 + 6.0 + i * 11.0, y0 + 3.0 + (i % 3) * 5.0, 0.0)
    return max(0, budget - seats)


def _gallery_seats(c, mb, x0, x1, y, budget):
    """2F 回廊に 4 人掛けテーブルを 1 列置く。残り席数を返す。"""
    seats = 0
    tw, pitch = 1.65, 2.25
    n = int((x1 - x0) / pitch)
    for i in range(n):
        if i % 9 == 8:       # 階段まわりの通路
            continue
        px = x0 + pitch * 0.5 + i * pitch
        if px + tw * 0.5 > x1 or seats >= budget:
            break
        F.table(mb, px, y, ang=0.0, w=tw, d=0.80, h=0.72,
                top="desk_wood" if i % 2 else "desk_white", z=GALLERY_Z)
        for sgn in (-1, 1):
            for k in range(2):
                cxx = px + (k - 0.5) * (tw * 0.5)
                F.chair_canteen(mb, F.T(cxx, y + sgn * 0.78, GALLERY_Z,
                                        0.0 if sgn > 0 else math.pi),
                                mat="chair_blue" if (i + k) % 3
                                else "chair_orange")
                seats += 1
    c.seats += seats
    return max(0, budget - seats)
