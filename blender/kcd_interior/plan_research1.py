"""第1研究棟 1F: エントランスロビー + 廊下 + 研究室 2 室 + 教授室。

上階は建てず、エレベーター扉と階数表示だけを置く（DESIGN.md §3.4）。
"""

import math

from . import common, furniture as F, kit, shell

CEIL = 3.40          # 天井高
Z_TOP = 4.50         # 1 層ぶんの躯体高さ
Y_COR0 = 24.0        # 廊下の手前側
Y_COR1 = 27.40       # 廊下の奥側
ROOMS = (            # (x0, x1, 用途)
    (-38.0, -18.0, "lab_a"),
    (-14.0, -2.0, "professor"),
    (2.0, 24.0, "lab_b"),
)


def build(c):
    s = c.spec
    ix0, iy0, ix1, iy1 = s.inner()
    rng = c.rng

    common.envelope(c, CEIL, "floor_tile_white", door_w=7.0, z_top=Z_TOP,
                    sill=1.00, header=0.55, seg=3.2, grid=1.8)
    common.entry_kit(c, CEIL, door_w=7.0)

    # ---- 床の塗り分け ----
    kit.plate(c.floor, -15.0, iy0, 15.0, Y_COR0, 0.014, "floor_tile_white")
    kit.plate(c.floor, ix0, Y_COR0, ix1, Y_COR1, 0.014, "floor_tile_grey")
    for x0, x1, _ in ROOMS:
        kit.plate(c.floor, x0, Y_COR1, x1, iy1, 0.016, "floor_resin_grey")

    # ---- 間仕切り ----
    # 廊下手前の壁（ロビー側）。ロビー正面は開放し、翼部には出入口を開ける
    gaps_front = [(-12.0 - ix0, 12.0 - ix0)]
    for k in range(-6, 7):
        cx = k * 11.0
        if -16.0 < cx < 16.0:
            continue
        gaps_front.append((cx - 0.60 - ix0, cx + 0.60 - ix0))
    shell.partition(c.wall, (ix0, Y_COR0), (ix1, Y_COR0), 0.0, CEIL,
                    thick=0.20, gaps=gaps_front, glass_top="glass_partition",
                    glass_z=2.35)

    # 廊下奥の壁（研究室側）: 各室にドア開口
    doors = []
    for x0, x1, _ in ROOMS:
        cx = (x0 + x1) * 0.5
        doors.append((cx - 0.62 - ix0, cx + 0.62 - ix0))
    for cx in (-58.0, -47.0, 36.0, 47.0, 58.0):
        doors.append((cx - 0.62 - ix0, cx + 0.62 - ix0))
    shell.partition(c.wall, (ix0, Y_COR1), (ix1, Y_COR1), 0.0, CEIL,
                    thick=0.20, gaps=doors, glass_top="glass_partition",
                    glass_z=2.35)
    for x0, x1, _ in ROOMS:
        cx = (x0 + x1) * 0.5
        shell.door(c.wall, cx, Y_COR1, ang=0.0, w=1.10, h=2.10,
                   glass="glass_partition")
    for cx in (-58.0, -47.0, 36.0, 47.0, 58.0):
        shell.door(c.wall, cx, Y_COR1, ang=0.0, w=1.10, h=2.10)

    # 研究室どうしの仕切りと、翼部の室割り
    for x0, x1, _ in ROOMS:
        for px in (x0, x1):
            shell.partition(c.wall, (px, Y_COR1), (px, iy1), 0.0, CEIL,
                            thick=0.18)
    for px in (-63.0, -52.0, -43.0, 30.0, 41.0, 52.0, 63.0):
        shell.partition(c.wall, (px, Y_COR1), (px, iy1), 0.0, CEIL, thick=0.18)
    for px in (-52.0, -34.0, 24.0, 42.0, 60.0):
        shell.partition(c.wall, (px, iy0), (px, Y_COR0), 0.0, CEIL, thick=0.18)

    # ---- 柱 ----
    for k in range(-6, 7):
        cx = k * 11.0
        if abs(cx) < 8.0:
            continue
        shell.column(c.wall, cx, iy0 + 5.4, 0.0, CEIL, size=0.70)
    for k in (-3, -1, 1, 3):
        shell.column(c.wall, k * 11.0, Y_COR1 + 7.5, 0.0, CEIL, size=0.70)

    # ---- エレベーターホール ----
    core = c.furn("core")
    shell.partition(c.wall, (-8.6, 20.0), (8.6, 20.0), 0.0, CEIL, thick=0.34,
                    mat="wall_grey")
    shell.partition(c.wall, (-8.6, 20.0), (-8.6, Y_COR0), 0.0, CEIL, thick=0.34,
                    mat="wall_grey")
    shell.partition(c.wall, (8.6, 20.0), (8.6, Y_COR0), 0.0, CEIL, thick=0.34,
                    mat="wall_grey")
    centers = shell.elevator_bank(core, -5.2, 19.82, count=3, pitch=5.2,
                                  w=0.62, h=2.30)
    for cx, cy in centers:
        shell.wall_sign(core, cx, cy - 0.12, 2.86, ang=math.pi, w=1.30, h=0.30,
                        mat="screen_blue")
    c.poi("elevator", 0.0, 17.6, 0.0)
    common.sign_board(c, core, 0.0, 19.6, 2.60, ang=math.pi, w=3.2, h=0.62)

    # ---- ロビー ----
    lob = c.furn("lobby")
    F.reception(lob, -9.5, 14.0, ang=0.0, w=5.4, d=1.05, h=1.12)
    F.chair(lob, -10.6, 15.2, ang=math.pi, mat="chair_grey")
    F.chair(lob, -8.4, 15.2, ang=math.pi, mat="chair_grey")
    F.monitor(lob, -10.8, 14.5, 1.12, ang=math.pi, w=0.50, h=0.30)
    c.poi("reception", -9.5, 12.4, 0.0)
    c.npc(-9.5, 15.3, 0.0)
    c.npc(-6.0, 11.2, 0.0)

    shell.notice_board(c.wall, -14.9, 11.0, 1.05, ang=-math.pi * 0.5, w=4.2,
                       h=1.45, sheets=12, rng=rng)
    c.poi("notice", -13.4, 11.0, 0.0)
    shell.clock(c.wall, 14.9, 13.5, 2.55, ang=math.pi * 0.5, r=0.30)

    for sy, ang in ((9.6, 0.0), (13.4, math.pi)):
        F.sofa(lob, 9.0, sy, ang=ang, w=2.2, d=0.88)
    F.table(lob, 9.0, 11.5, ang=0.0, w=1.3, d=0.7, h=0.42, top="desk_dark")
    F.sofa(lob, 13.6, 11.5, ang=math.pi * 0.5, w=2.2, d=0.88)
    c.poi("lounge", 11.0, 11.5, 0.0)
    c.npc(11.4, 9.0, 0.0)

    # エントランス正面のインフォメーション島（内壁面 iy0 = 6.8 より奥に置く）
    for i in range(2):
        px = -3.4 + i * 6.8
        F.round_table(lob, px, 10.2, r=0.46, h=1.05, top="sb_wood")
        for k in range(3):
            ang = math.pi * (0.35 + k * 0.62)
            F.stool(lob, px + math.sin(ang) * 1.05, 10.2 - math.cos(ang) * 1.05,
                    ang=ang, mat="chair_grey", h=0.76)
            c.seats += 1
    # 案内サインは spawn_research1 (0, 8.30) の 0.10 m 先にあり、入った瞬間に
    # 板の中に立っていた。頭の上（板の下端 1.96 m）へ上げる（#42）
    common.sign_board(c, lob, 0.0, 8.4, 2.55, ang=0.0, w=1.6, h=1.10)
    shell.planter(lob, -6.2, 8.2, r=0.50, h=0.54, leaf_h=2.2)
    shell.planter(lob, 6.2, 8.2, r=0.50, h=0.54, leaf_h=2.2)
    F.bench(lob, -3.4, 13.6, ang=0.0, w=2.2, back=True)
    F.bench(lob, 3.4, 13.6, ang=0.0, w=2.2, back=True)
    shell.trash_bins(lob, -13.4, 8.0, ang=-math.pi * 0.5, n=3)
    F.locker_bank(lob, -14.6, -10.0, 21.8, ang=math.pi, h=1.80)

    shell.planter(lob, -13.6, 19.0, r=0.46, h=0.50, leaf_h=1.8)
    shell.planter(lob, 13.6, 19.0, r=0.46, h=0.50, leaf_h=1.8)
    shell.vending(lob, 13.0, 21.6, ang=math.pi)
    shell.vending(lob, 11.6, 21.6, ang=math.pi, mat="fm_green")
    shell.hanging_sign(c.wall, 0.0, 9.6, CEIL, w=3.6, h=0.46, drop=0.35)
    c.sign(0.0, 9.6, CEIL - 0.8)

    # ---- 廊下 ----
    cor = c.furn("corridor")
    common.corridor_run(c, cor, ix0 + 6.0, ix1 - 6.0, Y_COR1 - 0.35, CEIL,
                        pitch=11.0, both=False)
    # ベンチは壁を背にして廊下側を向ける。corridor_run の自動配置だと廊下の
    # 真ん中に出て壁（1.35 m 先）を向き、ロビーの開口 x=-12..12 もふさぐ（#42）
    for bx in (-35.0, 35.0):
        F.bench(cor, bx, Y_COR0 + 0.36, ang=0.0, w=1.8, back=True)
    F.bench(cor, 0.0, Y_COR1 - 0.36, ang=math.pi, w=1.8, back=True)
    shell.light_strip(c.wall, ix0 + 2.0, (Y_COR0 + Y_COR1) * 0.5,
                      ix1 - 2.0, (Y_COR0 + Y_COR1) * 0.5, CEIL - 0.02, w=0.30)

    # ---- 研究室 ----
    for x0, x1, kind in ROOMS:
        if kind == "professor":
            _professor(c, x0, x1, iy1)
        else:
            _lab(c, x0, x1, iy1, kind)

    # ---- 照明 ----
    pts = shell.ceiling_lights(c.wall, -15.0, iy0 + 1.0, 15.0, Y_COR0, CEIL,
                               sx=4.6, sy=4.6)
    c.lights_from(pts, CEIL, energy=120.0, step=2)
    for x0, x1, _ in ROOMS:
        p = shell.ceiling_lights(c.wall, x0 + 1.0, Y_COR1 + 1.0, x1 - 1.0,
                                 iy1 - 1.0, CEIL, sx=3.6, sy=4.0)
        c.lights_from(p, CEIL, energy=150.0, step=2)

    c.cam("", (11.5, 8.0, 1.72), (-8.0, 16.5, 1.15), lens=17.0)
    c.note("1F ロビー（受付・ソファ・掲示板）+ EV3 基 + 廊下 142 m + 研究室2室 + 教授室")


def _lab(c, x0, x1, y1, kind):
    """研究室: 机の島 + PC + 書架 + ホワイトボード。"""
    mb = c.furn(kind)
    rng = c.rng
    cx = (x0 + x1) * 0.5
    y0 = Y_COR1 + 0.6
    # 机の島（2 列背中合わせ）を 2 組
    for row in range(2):
        yy = y0 + 2.2 + row * 5.6
        n = max(2, int((x1 - x0 - 3.0) / 1.55))
        for i in range(n):
            px = x0 + 1.5 + (x1 - x0 - 3.0) * (i + 0.5) / n
            for sgn in (-1, 1):
                F.desk(mb, px, yy + sgn * 0.36, ang=0.0 if sgn > 0 else math.pi,
                       w=1.45, d=0.70, h=0.72, drawers=(i % 2 == 0))
                F.monitor(mb, px, yy + sgn * 0.20, 0.72,
                          ang=math.pi if sgn > 0 else 0.0)
                F.keyboard(mb, px, yy + sgn * 0.56, 0.72,
                           ang=0.0 if sgn > 0 else math.pi)
                F.chair(mb, px, yy + sgn * 1.30,
                        ang=math.pi if sgn > 0 else 0.0, mat="chair_blue")
                if i % 3 == 0:
                    F.pc_tower(mb, px - sgn * 0.55, yy + sgn * 0.45)
    # 壁際の書架
    for i in range(int((x1 - x0 - 2.0) / 0.95)):
        px = x0 + 1.0 + 0.95 * i + 0.475
        F.bookshelf(mb, px, y1 - 0.45, ang=math.pi, w=0.92, h=1.95,
                    shelves=5, rng=rng)
    F.whiteboard(mb, x0 + 0.14, y0 + 4.5, 1.62, ang=-math.pi * 0.5, w=2.8, h=1.30)
    F.locker_bank(mb, x1 - 4.4, x1 - 0.4, y0 + 0.35, ang=math.pi, h=1.80)
    shell.planter(mb, x1 - 1.2, y1 - 2.2, r=0.36, h=0.42, leaf_h=1.3)
    c.poi(kind, cx, y0 + 4.0, 0.0)
    # NPC は机の島（椅子は yy ± 1.30）の外に置く。島の間と北側の通路（#42）
    c.npc(cx - 2.0, y0 + 5.0, 0.0)
    c.npc(cx + 2.4, y0 + 11.0, 0.0)
    common.sign_board(c, mb, cx + 1.1, Y_COR1 - 0.22, 2.20, ang=math.pi,
                      w=1.1, h=0.34)


def _professor(c, x0, x1, y1):
    """教授室（q_orientation の相手）。"""
    mb = c.furn("professor")
    rng = c.rng
    cx = (x0 + x1) * 0.5
    y0 = Y_COR1 + 0.6
    kit.plate(c.floor, x0 + 0.1, y0 - 0.5, x1 - 0.1, y1 - 0.1, 0.018,
              "floor_carpet_blue")
    F.desk(mb, cx + 1.4, y1 - 2.6, ang=0.0, w=1.85, d=0.85, h=0.74,
           top="desk_dark", drawers=True)
    F.monitor(mb, cx + 1.4, y1 - 2.95, 0.74, ang=math.pi, w=0.62, h=0.38)
    F.keyboard(mb, cx + 1.4, y1 - 2.30, 0.74, ang=math.pi)
    F.chair(mb, cx + 1.4, y1 - 1.55, ang=math.pi, mat="chair_grey")
    base_lamp = len(mb.verts)
    F.desk_lamp(mb, cx + 2.05, y1 - 2.85, ang=math.pi * 0.25)
    kit.lift(mb, base_lamp, 0.74)
    for i in range(4):
        F.bookshelf(mb, x0 + 1.0 + 0.95 * i, y1 - 0.45, ang=math.pi, w=0.92,
                    h=2.05, shelves=6, rng=rng)
    for i in range(3):
        F.bookshelf(mb, x0 + 0.42, y0 + 1.4 + 0.95 * i, ang=-math.pi * 0.5,
                    w=0.92, h=2.05, shelves=6, rng=rng)
    F.sofa(mb, cx + 0.6, y0 + 1.6, ang=0.0, w=1.9, d=0.82)
    F.table(mb, cx + 0.6, y0 + 3.0, ang=0.0, w=1.1, d=0.62, h=0.44,
            top="desk_dark")
    F.lounge_chair(mb, cx + 0.6, y0 + 4.3, ang=math.pi)
    F.whiteboard(mb, x1 - 0.16, y0 + 4.6, 1.62, ang=math.pi * 0.5, w=2.2,
                 h=1.20)
    shell.planter(mb, x1 - 1.0, y1 - 1.3, r=0.38, h=0.44, leaf_h=1.5)
    # 机の手前（来客側）。y1-1.6 は教授の椅子（y1-1.55）の中で空き 0.18 m だった（#42）
    c.poi("professor", cx + 1.4, y1 - 3.6, 0.0)
    c.npc(cx + 2.6, y1 - 1.0, 0.0)   # 椅子（y1-1.55）の中に湧いていた
    common.sign_board(c, mb, cx + 1.05, Y_COR1 - 0.22, 2.20, ang=math.pi,
                      w=1.1, h=0.34)
