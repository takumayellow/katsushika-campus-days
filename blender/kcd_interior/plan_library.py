"""図書館 1F: 開架書庫・閲覧席・カウンター・階段・2F 回廊・個人閲覧ブース。q_library の舞台。"""

import math

from . import common, furniture as F, kit, shell

CEIL = 3.60          # 一般部の天井
SLAB = 4.40          # 2F 床
CEIL2 = 8.00         # 2F 天井（＝吹き抜けの天井）
Z_TOP = 8.80


def build(c):
    s = c.spec
    ix0, iy0, ix1, iy1 = s.inner()

    common.envelope(c, CEIL, "floor_carpet_blue", door_w=6.0, z_top=Z_TOP,
                    sill=0.85, header=0.55, seg=3.4, ceil=False)
    common.entry_kit(c, CEIL, door_w=6.0)

    # ゾーニング（入口 → 手前がカウンター、左が書架、右が閲覧、奥がブース）
    ent_y1 = iy0 + 13.0
    stk_x1 = -5.0
    read_y1 = min(iy1 - 14.0, iy0 + 46.0)
    booth_y0 = read_y1 + 1.2

    _entrance_zone(c, ix0, iy0, ix1, ent_y1)
    aisle_x, aisle_y0, aisle_y1 = _stacks(c, ix0 + 1.5, ent_y1 + 1.0, stk_x1,
                                          iy1 - 2.0)
    _reading(c, stk_x1 + 1.0, ent_y1 + 1.0, ix1, read_y1)
    _booths(c, stk_x1 + 1.0, booth_y0, ix1, iy1)
    _gallery(c, ix0, ent_y1, ix1, iy1, stk_x1, read_y1)

    c.cam("", (aisle_x, aisle_y0 - 2.6, 1.72),
          (aisle_x + 0.2, aisle_y1 - 1.0, 1.35), lens=20.0)
    c.cam("hall", (16.0, iy0 + 3.0, 2.35), (-10.0, iy0 + 13.0, 1.20), lens=17.0)
    c.note("開架書架 + 閲覧席 + 2F 回廊の吹き抜け + 個人閲覧ブース")


# --------------------------------------------------------------------------- #
def _entrance_zone(c, x0, y0, x1, y1):
    mb = c.furn("counter")
    w = c.wall
    shell.ceiling(w, x0, y0, x1, y1, CEIL, "ceiling_white", grid=1.8)
    pts = shell.ceiling_lights(w, x0 + 2.0, y0 + 1.0, x1 - 2.0, y1 - 1.0, CEIL,
                               sx=5.0, sy=4.0)
    c.lights_from(pts, CEIL, energy=200.0, step=2)

    # 貸出返却カウンター
    F.reception(mb, -12.0, y0 + 7.6, ang=0.0, w=7.0, d=1.0, h=1.10)
    F.monitor(mb, -13.6, y0 + 7.4, 1.10, ang=math.pi)
    F.monitor(mb, -10.4, y0 + 7.4, 1.10, ang=math.pi)
    c.poi("counter", -12.0, y0 + 6.2, 0.0)
    c.npc(-13.2, y0 + 8.2, 0.0)
    c.npc(-10.6, y0 + 8.2, 0.0)
    common.sign_board(c, mb, -12.0, y0 + 8.4, 2.45, ang=math.pi, w=3.2, h=0.62)

    # ゲート（入退館）
    for i in range(3):
        gx = -1.4 + i * 1.4
        kit.box(mb, gx - 0.16, y0 + 2.6, 0.0, gx + 0.16, y0 + 3.5, 0.98,
                "counter_dark", top="metal_gray")

    # 検索端末の島
    for i in range(4):
        F.desk(mb, 8.0 + i * 1.6, y0 + 4.4, ang=math.pi, w=1.4, d=0.70)
        F.monitor(mb, 8.0 + i * 1.6, y0 + 4.6, 0.72, ang=0.0)
        F.keyboard(mb, 8.0 + i * 1.6, y0 + 4.15, 0.72, ang=0.0)
        F.chair_min(mb, F.T(8.0 + i * 1.6, y0 + 3.6, 0.0, 0.0), mat="chair_grey")
    c.poi("search", 10.0, y0 + 3.0, 0.0)

    # ロッカー・掲示板・返却ブックポスト
    F.locker_bank(mb, 13.0, 19.0, y0 + 1.0, ang=0.0, h=1.80)
    shell.notice_board(w, -20.0, y0 + 0.4, 1.50, ang=0.0, w=2.6, h=1.3,
                       rng=c.rng)
    shell.planter(mb, 4.0, y0 + 1.6, r=0.48, h=0.50, leaf_h=1.8)
    # 入口ホールのラウンジと新聞架
    F.round_table(mb, 4.2, y0 + 8.6, r=0.62, h=0.70)
    F.round_table(mb, 10.6, y0 + 9.4, r=0.62, h=0.70)
    for ax, ay in ((4.2, 8.6), (10.6, 9.4)):
        for k in range(4):
            a = math.pi * 0.5 * k
            F.lounge_chair(mb, ax + math.sin(a) * 1.02,
                           ay - math.cos(a) * 1.02, ang=a,
                           mat="chair_blue" if k % 2 == 0 else "chair_green")
            c.seats += 1
    shell.planter(mb, 7.4, y0 + 9.0, r=0.60, h=0.58, leaf_h=2.3)
    for i in range(3):
        F.bookshelf(mb, 1.2 + i * 1.1, y0 + 10.8, ang=0.0, w=1.0, h=1.35,
                    shelves=3, rng=c.rng)
    F.bench(mb, -5.0, y0 + 10.6, ang=math.pi, w=2.4, back=True)
    F.bench(mb, -8.0, y0 + 10.6, ang=math.pi, w=2.4, back=True)
    shell.planter(mb, -4.0, y0 + 1.6, r=0.48, h=0.50, leaf_h=1.8)

    # 新着雑誌の面陳棚
    for i in range(4):
        F.bookshelf(mb, x0 + 2.2 + i * 1.1, y0 + 2.0, ang=0.0, w=1.0, h=1.50,
                    shelves=4, rng=c.rng)

    # 階段（2F 回廊へ）
    shell.stair_flight(w, x1 - 4.0, y0 + 3.0, y0 + 11.6, 0.0, SLAB, width=2.2,
                       tread_mat="floor_tile_grey")
    shell.railing(w, [(x1 - 2.8, y0 + 3.0), (x1 - 2.8, y0 + 11.6)], SLAB,
                  h=1.05, mat="metal_white")


def _stacks(c, x0, y0, x1, y1):
    """開架書架。列は Y 方向に伸ばし、X 方向に等間隔で並べる。"""
    mb = c.furn("stacks")
    w = c.wall
    rng = c.rng
    shell.ceiling(w, x0 - 1.5, y0, x1 + 1.0, y1, CEIL, "ceiling_grid", grid=1.5)
    pts = shell.ceiling_lights(w, x0, y0 + 1.0, x1, y1 - 1.0, CEIL,
                               sx=3.0, sy=5.0, mat="light_strip", w=0.24, l=3.6)
    c.lights_from(pts, CEIL, energy=170.0, step=2)

    pitch = 2.35
    length = min(24.0, y1 - y0 - 3.0)
    n = int((x1 - x0) / pitch)
    ranges = 0
    for i in range(n):
        cx = x0 + 0.9 + i * pitch
        if cx > x1 - 0.6:
            break
        for cy in (y0 + 2.0 + length * 0.5, y0 + 4.0 + length * 1.5):
            if cy + length * 0.5 > y1 - 0.8:
                continue
            F.stack_range(mb, cx, cy, length, ang=math.pi * 0.5,
                          bays=int(length / 0.92), h=1.90, shelves=5, rng=rng,
                          clump=2.48)
            ranges += 1
        # 列端の見出しサイン
        shell.wall_sign(mb, cx, y0 + 2.0 - length * 0.0 - 0.1, 2.05,
                        ang=math.pi, w=0.72, h=0.30)
    c.note("開架書架 %d 連" % ranges)
    aisle_i = max(0, ranges // 4)
    aisle_x = x0 + 0.9 + aisle_i * pitch + pitch * 0.5
    c.poi("stacks", (x0 + x1) * 0.5, y0 + 6.0, 0.0)
    c.npc(x0 + 3.0, y0 + 8.0, 0.0)
    c.npc(x0 + 8.0, y0 + 16.0, 0.0)

    # 書架の間のスツール
    for i in range(3):
        F.stool(mb, x0 + 2.0 + i * 9.0, y0 + 1.2, ang=0.0, mat="chair_grey")
    return aisle_x, y0 + 2.0, y0 + 2.0 + length


def _reading(c, x0, y0, x1, y1):
    """閲覧席。1〜2F 吹き抜け + 長机とデスクランプ。"""
    mb = c.furn("reading")
    w = c.wall
    shell.ceiling(w, x0, y0, x1, y1, CEIL2, "ceiling_white", grid=0.0)
    kit.plate(c.floor, x0, y0, x1, y1, 0.016, "floor_carpet_blue")

    # 吹き抜けを支える柱
    for i in range(3):
        for k in range(3):
            shell.column(w, x0 + 6.0 + i * 13.0, y0 + 6.0 + k * 13.0, 0.0,
                         CEIL2, size=0.60, mat="concrete_light")

    pts = shell.ceiling_lights(w, x0 + 2.0, y0 + 2.0, x1 - 2.0, y1 - 2.0, CEIL2,
                               sx=5.0, sy=5.0, w=2.0, l=0.8)
    c.lights_from(pts, CEIL2, energy=420.0, step=1, radius=2.0)

    rows = 0
    for r in range(6):
        ty = y0 + 2.6 + r * 2.9
        if ty > y1 - 2.0:
            break
        for i in range(4):
            tx = x0 + 5.0 + i * 10.0
            if tx + 4.0 > x1 - 1.0:
                break
            F.long_desk(mb, tx, ty, ang=0.0, w=7.2, d=1.30, h=0.73,
                        top="desk_wood")
            # 中央の仕切り板とランプ
            kit.box(mb, tx - 3.5, ty - 0.03, 0.73, tx + 3.5, ty + 0.03, 1.12,
                    "glass_partition")
            for k in range(4):
                F.desk_lamp(mb, tx - 2.7 + k * 1.8, ty - 0.35, ang=0.0)
            for sgn in (-1, 1):
                for k in range(4):
                    F.chair_min(mb, F.T(tx - 2.7 + k * 1.8, ty + sgn * 0.95,
                                        0.0, 0.0 if sgn > 0 else math.pi),
                                mat="chair_blue")
                    c.seats += 1
        rows += 1
    c.note("閲覧長机 %d 列" % rows)
    c.poi("reading", (x0 + x1) * 0.5, (y0 + y1) * 0.5, 0.0)
    for i in range(4):
        c.npc(x0 + 4.0 + i * 9.0, y0 + 5.0 + (i % 3) * 8.0, 0.0)

    # 窓ぎわのソファ席
    for i in range(4):
        sy = y0 + 4.0 + i * 11.0
        if sy > y1 - 2.0:
            break
        F.sofa(mb, x1 - 1.6, sy, ang=-math.pi * 0.5, w=2.4)
        F.table(mb, x1 - 3.0, sy, ang=0.0, w=1.2, d=0.6, h=0.42, top="sb_wood")
        c.seats += 3


def _booths(c, x0, y0, x1, y1):
    """静かな個人閲覧ブース（q_library の目的地）。"""
    mb = c.furn("booths")
    w = c.wall
    shell.partition(w, (x0, y0), (x1, y0), 0.0, CEIL, "wall_white", thick=0.16,
                    gaps=[(2.0, 3.6), (18.0, 19.6)], glass_top=True,
                    glass_z=2.20)
    shell.ceiling(w, x0, y0, x1, y1, CEIL, "ceiling_white", grid=1.8)
    pts = shell.ceiling_lights(w, x0 + 2.0, y0 + 1.0, x1 - 2.0, y1 - 1.0, CEIL,
                               sx=4.0, sy=3.4)
    c.lights_from(pts, CEIL, energy=180.0, step=2)
    common.sign_board(c, mb, x0 + 2.8, y0 + 0.16, 2.45, ang=0.0, w=2.2, h=0.52)

    n = 0
    for r in range(6):
        by = y0 + 2.0 + r * 1.85
        if by > y1 - 1.2:
            break
        for i in range(11):
            bx = x0 + 1.4 + i * 1.32
            if bx > x1 - 1.0:
                break
            F.study_booth(mb, bx, by, ang=0.0 if r % 2 == 0 else math.pi)
            F.chair_min(mb, F.T(bx, by - 0.62 if r % 2 == 0 else by + 0.62,
                                0.0, 0.0 if r % 2 == 0 else math.pi),
                        mat="chair_grey")
            c.seats += 1
            n += 1
    c.note("個人閲覧ブース %d 席" % n)
    c.poi("desk", x0 + 3.0, y0 + 2.0, 0.0)
    c.npc(x0 + 6.0, y0 + 3.9, 0.0)
    shell.exit_sign(w, x0 + 2.8, y0 + 0.05, 2.90, ang=0.0)


def _gallery(c, x0, y0, x1, y1, stk_x1, read_y1):
    """2F 回廊。閲覧室の吹き抜けをぐるりと囲う。"""
    mb = c.furn("gallery")
    w = c.wall
    gx0, gx1 = stk_x1 + 1.0, x1
    # 書架側の上と、奥のブース側の上にスラブを張る
    shell.floor(w, x0, y0, stk_x1 + 1.0, y1, SLAB, "floor_carpet_grey",
                thickness=0.45, side_mat="concrete_light")
    shell.floor(w, gx0, read_y1, gx1, y1, SLAB, "floor_carpet_grey",
                thickness=0.45, side_mat="concrete_light")
    shell.railing(w, [(stk_x1 + 1.0, y0), (stk_x1 + 1.0, read_y1)], SLAB,
                  h=1.10, mat="metal_white", glass="glass_partition", post=2.4)
    shell.railing(w, [(stk_x1 + 1.0, read_y1), (gx1, read_y1)], SLAB, h=1.10,
                  mat="metal_white", glass="glass_partition", post=2.4)
    shell.ceiling(w, x0, y0, stk_x1 + 1.0, y1, SLAB + 3.20, "ceiling_white",
                  grid=1.8)
    pts = shell.ceiling_lights(w, x0 + 2.0, y0 + 2.0, stk_x1 - 1.0, y1 - 2.0,
                               SLAB + 3.20, sx=5.0, sy=6.0)
    c.lights_from(pts, SLAB + 3.2, energy=170.0, step=3)

    # 2F の書架と閲覧席（z=0 で組んでから SLAB へ持ち上げる）
    base2f = len(mb.verts)
    for i in range(5):
        cx = x0 + 2.4 + i * 2.4
        if cx > stk_x1 - 1.6:
            break
        F.stack_range(mb, cx, y0 + 12.0, 18.0, ang=math.pi * 0.5, bays=19,
                      h=1.90, shelves=5, rng=c.rng, clump=2.48)
    for i in range(4):
        tx = x0 + 3.0 + i * 5.2
        if tx > stk_x1 - 2.0:
            break
        F.long_desk(mb, tx, y1 - 4.0, ang=0.0, w=4.0, d=1.20, h=0.73)
        for sgn in (-1, 1):
            for k in range(3):
                F.chair_min(mb, F.T(tx - 1.3 + k * 1.3, y1 - 4.0 + sgn * 0.9,
                                    0.0, 0.0 if sgn > 0 else math.pi),
                            mat="chair_green")
                c.seats += 1
    kit.lift(mb, base2f, SLAB)
    c.poi("gallery", x0 + 6.0, y0 + 12.0, SLAB)
    c.npc(x0 + 5.0, y0 + 20.0, SLAB)
    shell.exit_sign(w, x1 - 3.0, y0 + 0.8, SLAB + 2.80)
