"""共創棟 1F: スターバックス風カフェ + コンビニ、2F: ラウンジ。q_coffee の舞台。"""

import math

from . import common, furniture as F, kit, shell

CEIL = 3.90          # 1F 天井
SLAB = 4.30          # 2F 床スラブ上面
CEIL2 = 7.90         # 2F 天井
Z_TOP = 8.60
CAFE_CEIL = 7.60     # カフェ上の吹き抜け天井


def build(c):
    s = c.spec
    ix0, iy0, ix1, iy1 = s.inner()

    # 入口は建物中央でなく東寄り（local x=0）。カフェは右、コンビニは左。
    common.envelope(c, CEIL, "floor_tile_grey", door_w=5.6, z_top=Z_TOP,
                    sill=0.35, header=0.55, seg=3.0, ceil=False)
    common.entry_kit(c, CEIL, door_w=5.6)

    # 建物は 130 m と長いが、内部を作り込むのは入口から西 38 m まで。
    # その先は防火区画の壁で閉じる（Unity 側でも進入させない）。
    END_X = max(ix0, -38.0)
    if END_X > ix0 + 0.5:
        shell.partition(c.wall, (END_X, iy0), (END_X, iy1), 0.0, Z_TOP,
                        "wall_grey", thick=0.24)
        shell.exit_sign(c.wall, END_X + 0.18, iy0 + 1.2, 2.40,
                        ang=-math.pi * 0.5)

    lobby_x0, lobby_x1 = -9.0, 8.0
    cafe_x0, cafe_x1 = lobby_x1, min(ix1, 31.0)
    cvs_x0, cvs_x1 = max(END_X + 0.2, -34.0), -10.0

    _lobby(c, lobby_x0, iy0, lobby_x1, iy1)
    _cafe(c, cafe_x0, iy0, cafe_x1, iy1)
    _cvs(c, cvs_x0, iy0, cvs_x1, iy1)
    _second_floor(c, END_X + 0.2, iy0, cafe_x0 - 1.0, iy1)

    c.cam("", (10.5, iy0 + 1.7, 1.62), (26.0, iy1 - 2.2, 1.25), lens=18.0)
    c.note("1F=カフェ（吹き抜け）+ コンビニ、2F=ラウンジ。q_coffee の舞台")


# --------------------------------------------------------------------------- #
def _lobby(c, x0, y0, x1, y1):
    mb = c.furn("lobby")
    w = c.wall
    shell.ceiling(w, x0, y0, x1, y1, CEIL, "ceiling_white", grid=1.8)
    pts = shell.ceiling_lights(w, x0 + 1.0, y0 + 1.0, x1 - 1.0, y1 - 1.0, CEIL,
                               sx=3.2, sy=3.4)
    c.lights_from(pts, CEIL, energy=170.0)

    shell.column(w, x0 + 1.4, y0 + 5.2, 0.0, CEIL, size=0.55)
    shell.column(w, x1 - 1.4, y0 + 5.2, 0.0, CEIL, size=0.55)

    # 館内案内サイン + ベンチ
    common.sign_board(c, mb, x0 + 0.4, y1 - 0.30, 2.05, ang=math.pi, w=2.4, h=0.9)
    F.bench(mb, x0 + 3.0, y0 + 1.5, ang=0.0, w=2.2)
    F.bench(mb, x0 + 5.8, y0 + 1.5, ang=0.0, w=2.2)
    shell.planter(mb, x1 - 1.0, y0 + 1.4, r=0.46, h=0.50, leaf_h=1.7)
    shell.planter(mb, x0 + 0.9, y0 + 1.4, r=0.46, h=0.50, leaf_h=1.7)
    shell.clock(w, 0.0, y1 - 0.24, 2.85, ang=math.pi, r=0.26)

    # 階段（2F ラウンジへ）
    sx = x0 + 3.2
    shell.stair_flight(w, sx, y1 - 7.4, y1 - 0.6, 0.0, SLAB, width=1.9,
                       tread_mat="floor_tile_grey", rail=True)
    shell.railing(w, [(sx + 1.15, y1 - 7.4), (sx + 1.15, y1 - 0.6)], SLAB,
                  h=1.05, mat="metal_white")
    c.poi("stair", sx, y1 - 4.0, 0.0)


def _cafe(c, x0, y0, x1, y1):
    """スターバックス風カフェ。吹き抜けにしてペンダントを吊る。"""
    mb = c.furn("cafe")
    w = c.wall
    rng = c.rng

    kit.plate(c.floor, x0, y0, x1, y1, 0.015, "floor_wood")
    shell.partition(w, (x0, y0), (x0, y1), 0.0, CAFE_CEIL, "wall_wood",
                    thick=0.14, gaps=[(1.4, 5.2)], glass_top=True, glass_z=2.30)
    shell.ceiling(w, x0, y0, x1, y1, CAFE_CEIL, "ceiling_dark", grid=0.0)

    # 壁の一面をサインカラーに
    kit.vplate(w, (x1 - 0.14, y1 - 0.16), (x0 + 0.14, y1 - 0.16), 0.0,
               CAFE_CEIL, "sb_green")

    # カウンター（奥壁ぞい）
    cy = y1 - 2.3
    F.counter(mb, x0 + 2.6, cy, x0 + 10.6, cy + 0.80, h=1.05)
    F.register(mb, x0 + 3.6, cy - 0.05)
    F.register(mb, x0 + 5.4, cy - 0.05)
    F.espresso_machine(mb, x0 + 8.2, cy + 0.42, ang=math.pi)
    F.showcase(mb, x0 + 11.4, cy + 0.25, ang=math.pi, w=2.2, d=0.7, rng=rng)
    # バックバー
    kit.box(mb, x0 + 2.4, y1 - 0.30, 0.0, x0 + 13.8, y1 - 0.16, 2.30,
            "wall_wood")
    for k in range(4):
        kit.box(mb, x0 + 2.6, y1 - 0.42, 1.05 + k * 0.38, x0 + 13.6,
                y1 - 0.18, 1.09 + k * 0.38, "sb_wood")
        F.book_row(mb, F.T(0.0, y1 - 0.30, 0.0, 0.0), x0 + 2.8, x0 + 13.4,
                   1.09 + k * 0.38, height=0.22, depth=0.20, rng=rng,
                   clump=0.34)
    c.poi("starbucks", x0 + 6.0, cy - 1.5, 0.0)
    c.npc(x0 + 4.0, cy + 0.55, 0.0)
    c.npc(x0 + 7.4, cy + 0.55, 0.0)
    common.sign_board(c, mb, x0 + 6.0, cy + 0.30, 2.55, ang=0.0, w=3.0, h=0.62)

    # 客席（丸テーブル + 長机のコミュナルテーブル）
    for i in range(7):
        tx = x0 + 3.0 + i * 2.5
        if tx > x1 - 1.4:
            break
        F.round_table(mb, tx, y0 + 2.0, r=0.44, h=0.73)
        for k in range(2):
            a = math.pi * 0.5 + k * math.pi
            F.chair_min(mb, F.T(tx + 0.92 * math.cos(a), y0 + 2.0 + 0.92 * math.sin(a),
                                0.0, a + math.pi), mat="chair_green")
            c.seats += 1
    F.long_desk(mb, x0 + 9.5, y0 + 4.6, ang=0.0, w=5.0, d=0.85, h=0.74,
                top="sb_wood")
    for k in range(6):
        F.stool(mb, x0 + 7.4 + k * 0.85, y0 + 3.9, ang=0.0)
        c.seats += 1
    for k in range(6):
        F.stool(mb, x0 + 7.4 + k * 0.85, y0 + 5.3, ang=math.pi)
        c.seats += 1
    F.sofa(mb, x1 - 2.4, y0 + 2.6, ang=-math.pi * 0.5, w=2.2)
    F.round_table(mb, x1 - 3.7, y0 + 2.6, r=0.38, h=0.42, top="sb_wood")
    F.lounge_chair(mb, x1 - 5.0, y0 + 2.6, ang=math.pi * 0.5)
    c.seats += 4

    # ペンダント照明
    for i in range(9):
        px = x0 + 2.2 + i * 2.4
        if px > x1 - 1.0:
            break
        F.pendant(mb, px, y0 + 2.0, CAFE_CEIL, drop=4.5, r=0.26, mat="metal_dark")
        if i % 2 == 0:
            c.light(px, y0 + 2.4, CAFE_CEIL - 4.8, 130.0, 1.1)
    pts = shell.ceiling_lights(w, x0 + 2.0, y1 - 4.4, x1 - 1.0, y1 - 1.2,
                               CAFE_CEIL, sx=2.8, sy=3.0, mat="light_strip")
    c.lights_from(pts, CAFE_CEIL, energy=200.0)
    c.light(x0 + 6.0, y0 + 3.0, 2.6, 400.0, 1.6)


def _cvs(c, x0, y0, x1, y1):
    """コンビニ（ファミリーマート風）。"""
    mb = c.furn("cvs")
    w = c.wall
    rng = c.rng

    kit.plate(c.floor, x0, y0, x1, y1, 0.015, "floor_tile_white")
    shell.partition(w, (x1, y0), (x1, y1), 0.0, CEIL, "wall_white",
                    thick=0.14, gaps=[(2.0, 6.0)], glass_top=True, glass_z=2.20)
    shell.ceiling(w, x0, y0, x1, y1, CEIL, "ceiling_grid", grid=1.5)
    pts = shell.ceiling_lights(w, x0 + 1.0, y0 + 1.0, x1 - 1.0, y1 - 1.0, CEIL,
                               sx=2.6, sy=3.0, mat="light_strip")
    c.lights_from(pts, CEIL, energy=200.0, step=2)

    # サインバンド（店名色）
    kit.vplate(w, (x1 - 0.16, y1 - 0.18), (x0 + 0.16, y1 - 0.18), 2.35, 3.05,
               "fm_green")
    kit.vplate(w, (x1 - 0.16, y1 - 0.20), (x0 + 0.16, y1 - 0.20), 1.85, 2.35,
               "fm_blue")

    # レジカウンター（入口寄り）
    F.counter(mb, x1 - 5.4, y0 + 1.0, x1 - 1.2, y0 + 1.75, h=1.02,
              body="counter_wood", top="counter_stone")
    F.register(mb, x1 - 4.4, y0 + 1.30)
    F.register(mb, x1 - 2.6, y0 + 1.30)
    c.poi("store", x1 - 3.4, y0 + 2.9, 0.0)
    c.npc(x1 - 3.4, y0 + 1.95, 0.0)
    common.sign_board(c, mb, x1 - 3.4, y0 + 0.95, 2.35, ang=math.pi,
                      w=2.6, h=0.55)

    # ゴンドラ（棚）列
    rows = 5
    for r in range(rows):
        gy = y0 + 2.6 + r * 1.55
        if gy > y1 - 2.6:
            break
        F.gondola(mb, (x0 + x1) * 0.5 - 2.0, gy, ang=0.0,
                  length=min(15.0, (x1 - x0) - 8.0), h=1.45, shelves=4,
                  rng=rng, clump=0.42)

    # 冷蔵ケース（奥壁）
    F.fridge_case(mb, x0 + 8.0, y1 - 0.9, ang=math.pi, length=13.0, h=2.00,
                  rng=rng, clump=0.36)
    # ドリンク以外のケースと備品
    F.fridge_case(mb, x0 + 2.4, y0 + 1.1, ang=0.0, length=3.6, h=1.30,
                  rng=rng, clump=0.34)
    shell.trash_bins(mb, x1 - 7.6, y0 + 1.2, ang=0.0, n=3)
    c.npc(x0 + 6.0, y0 + 5.2, 0.0)
    c.npc(x0 + 12.0, y0 + 3.6, 0.0)


def _second_floor(c, x0, y0, x1, y1):
    """2F ラウンジ。カフェ側は吹き抜けなのでスラブを張らない。"""
    mb = c.furn("lounge")
    w = c.wall
    shell.floor(w, x0, y0, x1, y1, SLAB, "floor_carpet_blue", thickness=0.40,
                side_mat="concrete_light")
    shell.railing(w, [(x1, y0 + 0.2), (x1, y1 - 0.2)], SLAB, h=1.10,
                  mat="metal_white", glass="glass_partition", post=2.0)
    shell.ceiling(w, x0, y0, x1, y1, CEIL2, "ceiling_white", grid=1.8)
    pts = shell.ceiling_lights(w, x0 + 2.0, y0 + 1.0, x1 - 2.0, y1 - 1.0, CEIL2,
                               sx=4.0, sy=3.4)
    c.lights_from(pts, CEIL2, energy=180.0, step=2)

    # 窓側にカウンター席、内側にソファ島と 4 人テーブル
    # （家具は z=0 前提で組み、最後に SLAB まで持ち上げる）
    base2f = len(mb.verts)
    n_isl = int((x1 - x0) / 9.0)
    for i in range(n_isl):
        cx = x0 + 5.0 + i * 9.0
        if cx > x1 - 4.0:
            break
        F.long_desk(mb, cx, y1 - 1.1, ang=0.0, w=4.4, d=0.58, h=0.74,
                    top="desk_white")
        for k in range(5):
            F.stool(mb, cx - 1.7 + k * 0.85, y1 - 1.9, ang=0.0)
            c.seats += 1
            F.desk_lamp(mb, cx - 1.5 + k * 1.0, y1 - 1.15, ang=math.pi)
        F.sofa(mb, cx - 2.6, y0 + 2.4, ang=0.0, w=2.2)
        F.sofa(mb, cx - 2.6, y0 + 4.6, ang=math.pi, w=2.2)
        F.table(mb, cx - 2.6, y0 + 3.5, ang=0.0, w=1.4, d=0.6, h=0.42,
                top="sb_wood")
        F.table(mb, cx + 2.0, y0 + 3.4, ang=0.0, w=1.5, d=0.85, h=0.72)
        for sgn in (-1, 1):
            for k in range(2):
                F.chair_min(mb, F.T(cx + 2.0 + (k - 0.5) * 0.75,
                                    y0 + 3.4 + sgn * 0.78, 0.0,
                                    0.0 if sgn > 0 else math.pi),
                            mat="chair_blue")
                c.seats += 1
        c.seats += 6
        shell.planter(mb, cx + 4.0, y0 + 1.2, r=0.42, h=0.46, leaf_h=1.6)
        if i == 0:
            c.poi("lounge", cx, y0 + 3.5, SLAB)
            c.npc(cx - 1.2, y0 + 5.6, SLAB)
            c.npc(cx + 3.2, y0 + 2.0, SLAB)
    kit.lift(mb, base2f, SLAB)
    shell.exit_sign(w, x1 - 0.6, y0 + 0.6, SLAB + 2.60)
    common.sign_board(c, mb, x0 + 2.0, y1 - 0.24, SLAB + 2.10, ang=math.pi,
                      w=2.0, h=0.55)
