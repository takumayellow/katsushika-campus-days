"""講義棟 1F: 吹き抜けの大階段ホール + 600 席の大ホール + 中教室。"""

import math

from . import common, furniture as F, kit, shell

CEIL = 3.60          # ホール以外の天井高
HALL_CEIL = 11.20    # 大ホールの天井高
VOID_TOP = 13.60     # 階段ホールの吹き抜け頂部
Z_TOP = 14.20

HALL_X0, HALL_X1 = 2.0, 32.0     # 大ホールの X 範囲（幅 30 m）
HALL_Y1 = 33.0                   # 大ホールの奥（この先はホワイエ）
SEM_X0 = 33.0                    # 演習室ゾーンの手前
ATRIUM_X1 = -1.0                 # 階段ホールの右端
ROOM_Y0 = 31.0                   # 中教室の手前
SEAT_ROWS = 12                   # 客席の列数
SEAT_BLOCKS = (16, 18, 16)       # 1 列 50 席を 3 ブロックに分ける


def build(c):
    s = c.spec
    ix0, iy0, ix1, iy1 = s.inner()

    common.envelope(c, VOID_TOP, "floor_tile_white", door_w=7.2, z_top=Z_TOP,
                    sill=0.55, header=0.60, seg=3.0, ceil=False)
    common.entry_kit(c, 3.0, door_w=7.2)

    # ---- 床の塗り分け ----
    kit.plate(c.floor, ix0, iy0, ATRIUM_X1, iy1, 0.014, "floor_tile_white")
    kit.plate(c.floor, HALL_X0, iy0, HALL_X1, iy1, 0.016, "floor_carpet_red")
    kit.plate(c.floor, ix0, ROOM_Y0, ATRIUM_X1, iy1, 0.018, "floor_tile_grey")

    # ---- 大ホールの囲い ----
    shell.partition(c.wall, (HALL_X0 - 0.5, iy0), (HALL_X0 - 0.5, HALL_Y1), 0.0,
                    HALL_CEIL, thick=0.36, mat="wall_accent_navy",
                    gaps=[(6.0, 8.6), (24.0, 26.6)])
    shell.partition(c.wall, (HALL_X1 + 0.5, iy0), (HALL_X1 + 0.5, HALL_Y1), 0.0,
                    HALL_CEIL, thick=0.36, mat="wall_accent_navy",
                    gaps=[(6.0, 8.6), (24.0, 26.6)])
    shell.partition(c.wall, (HALL_X0 - 0.5, iy0 + 2.6), (HALL_X1 + 0.5, iy0 + 2.6),
                    0.0, HALL_CEIL, thick=0.36, mat="wall_accent_navy",
                    gaps=[(4.0, 6.6), (24.0, 26.6)])
    shell.partition(c.wall, (HALL_X0 - 0.5, HALL_Y1), (HALL_X1 + 0.5, HALL_Y1),
                    0.0, HALL_CEIL, thick=0.36, mat="wall_accent_navy")
    shell.ceiling(c.wall, HALL_X0, iy0 + 2.6, HALL_X1, HALL_Y1, HALL_CEIL,
                  "ceiling_dark", grid=0.0)
    for gx in (7.3, 25.3):
        shell.door(c.wall, HALL_X0 - 0.5, iy0 + gx, ang=math.pi * 0.5,
                   w=2.40, h=2.40, leaf="wall_accent_navy")
        shell.door(c.wall, HALL_X1 + 0.5, iy0 + gx, ang=-math.pi * 0.5,
                   w=2.40, h=2.40, leaf="wall_accent_navy")
    _hall(c, iy0 + 2.6, HALL_Y1)

    # ---- ホール北側のホワイエ ----
    _foyer(c, HALL_X0 - 0.5, HALL_Y1, HALL_X1 + 0.5, iy1)

    # ---- 東側の演習室 ----
    _seminar_rooms(c, SEM_X0, iy0, ix1, iy1)

    # ---- 階段ホール ----
    _atrium(c, ix0, iy0, ATRIUM_X1, ROOM_Y0)

    # ---- 中教室 ----
    _lecture_room(c, ix0, ROOM_Y0, ATRIUM_X1, iy1)

    c.cam("", (-24.0, 6.0, 1.80), (-9.0, 22.0, 4.60), lens=17.0)
    c.note("3 層吹き抜けの大階段ホール + 大ホール + ホワイエ + 演習室 3 室 + 中教室")


# --------------------------------------------------------------------------- #
def _hall(c, y0, y1):
    """600 席の大ホール。階段状の客席を +Y へ上げる。"""
    seats = c.furn("hall_seats")
    deck = c.furn("hall_tiers")
    stage_mb = c.furn("hall_stage")

    x0, x1 = HALL_X0 + 0.4, HALL_X1 - 0.4
    cx = (x0 + x1) * 0.5
    stage_y1 = y0 + 6.4

    # 演台まわり
    F.stage(stage_mb, x0 + 5.0, y0 + 0.6, x1 - 5.0, stage_y1 - 1.0,
            h=0.80, deck="floor_wood", skirt="wall_accent_navy")
    F.projection_screen(stage_mb, cx, y0 + 0.75, 8.40, ang=0.0, w=11.0, h=5.6)
    F.projector(stage_mb, cx, y0 + 17.0, HALL_CEIL - 0.10, ang=math.pi)
    # 舞台の上の什器は z=0 で組み、最後に舞台の高さ（0.80 m）まで持ち上げる
    base_st = len(stage_mb.verts)
    F.podium(stage_mb, cx - 7.0, y0 + 2.6, ang=0.0, w=1.15, d=0.64, h=1.14)
    F.table(stage_mb, cx + 6.0, y0 + 2.8, ang=0.0, w=2.4, d=0.8, h=0.74,
            top="desk_dark")
    for i in range(3):
        F.chair(stage_mb, cx + 5.0 + i * 1.0, y0 + 2.0, ang=0.0,
                mat="chair_grey")
    kit.lift(stage_mb, base_st, 0.80)
    # 舞台へ上がる 3 段。0.80 m の段差は CharacterController の stepOffset
    # 0.40 m を超えるので、以前は舞台の POI/NPC に近づけなかった（#42）
    for i in range(3):
        kit.box(stage_mb, cx - 1.8, stage_y1 - 1.0 + i / 3.0, 0.0,
                cx + 1.8, stage_y1 - 1.0 + (i + 1) / 3.0,
                0.80 * (3 - i) / 3.0, "wall_accent_navy", top="floor_wood")
    c.poi("hall_stage", cx, y0 + 3.4, 0.80)
    c.npc(cx - 7.0, y0 + 1.9, 0.80)          # 演台の後ろ（話す側）
    c.npc(cx + 6.0, y0 + 3.9, 0.80)

    # 階段客席: 1 列 50 席（16 / 18 / 16）を 12 列 = 600 席
    rows = SEAT_ROWS
    rise, run = 0.30, 1.05
    pitch = 0.50
    aisle = 1.70
    span = sum(SEAT_BLOCKS) * pitch + aisle * (len(SEAT_BLOCKS) - 1)
    blocks = []
    bx = (x0 + x1) * 0.5 - span * 0.5
    for n_seat in SEAT_BLOCKS:
        blocks.append((bx, n_seat))
        bx += n_seat * pitch + aisle
    total = 0
    for r in range(rows):
        yy = stage_y1 + run * r
        zz = rise * r
        # 段板はホールの壁から壁まで。以前は壁との間に幅 0.72 m の溝が残っていて、
        # 扉を開けて客席へ入れるようにしたら、そこへ最大 3.3 m 落ちた（#42）
        kit.box(deck, HALL_X0 - 0.4, yy, max(0.0, zz - 0.30), HALL_X1 + 0.4,
                yy + run, zz, "concrete_light", top="floor_carpet_red")
        for bx0, n_seat in blocks:
            for i in range(n_seat):
                px = bx0 + (i + 0.5) * pitch
                F.hall_seat(seats, F.T(px, yy + 0.62, zz, math.pi))
                total += 1
        # 段鼻の足元灯
        if r % 4 == 0:
            for bx0, _n in blocks:
                kit.box(deck, bx0 - 0.20, yy + 0.05, zz + 0.04, bx0 - 0.08,
                        yy + 0.30, zz + 0.14, "light_strip")
    c.seats += total
    c.note("大ホール %d 席（16/18/16 × %d 列）" % (total, rows))

    # 通路の手すり
    for bx0, n_seat in blocks[:-1]:
        px = bx0 + n_seat * pitch + aisle * 0.5
        pts = [(px, stage_y1 + 0.2), (px, stage_y1 + run * rows)]
        shell.railing(deck, pts, 0.0, h=0.95, mat="metal_white")

    # 最後列の背後: 音響・映写ブースと車椅子スペース
    back_y = stage_y1 + run * rows
    # ブースの床。最後列の段板（天端 rise * (rows - 1)）と面一にして段差を作らない。
    # 以前は床が無く、カウンターだけが rise * rows に浮いて椅子は z=0（客席の
    # 下）に沈み、hall_booth の POI もカウンターの箱の中にあった（#42）。
    bz = rise * (rows - 1)
    bt_y1 = min(back_y + 3.6, y1 - 0.3)
    # 最後列の背後の横通路。これが無いと通路の上端からブースへ回り込めない。
    # ブースの床は東へ 1.0 m 広げる（以前は 0.6 m しか空かず、半径 0.28 の
    # カプセルがカウンターの東を通れなかった）(#42)
    bt_x0, bt_x1 = x0 + 0.6, x0 + 7.6
    cross_y1 = back_y + 1.2
    kit.box(deck, HALL_X0 - 0.4, back_y, max(0.0, bz - 0.30), HALL_X1 + 0.4,
            cross_y1, bz, "concrete_light", top="floor_carpet_red")
    kit.box(deck, bt_x0, cross_y1, max(0.0, bz - 0.30), bt_x1, bt_y1, bz,
            "concrete_light", top="floor_carpet_red")
    # カウンターの奥行は 0.7 m（2.2 m の箱だと座る椅子ごと飲み込む）
    kit.box(deck, x0 + 1.0, back_y + 1.2, bz, x0 + 6.0,
            back_y + 1.9, bz + 1.05, "counter_dark", top="counter_stone")
    kit.box(deck, x0 + 1.0, back_y + 1.2, bz + 1.05, x0 + 6.0,
            back_y + 1.3, bz + 2.10, "glass_interior")
    base_bt = len(deck.verts)
    for i in range(2):
        F.chair(deck, x0 + 2.2 + i * 1.6, back_y + 2.6, ang=math.pi,
                mat="chair_grey")
    kit.lift(deck, base_bt, bz)
    F.monitor(deck, x0 + 3.0, back_y + 1.5, bz + 1.05, ang=math.pi)
    F.monitor(deck, x0 + 4.4, back_y + 1.5, bz + 1.05, ang=math.pi)
    c.poi("hall_booth", x0 + 5.4, back_y + 2.6, bz)   # 2 脚の椅子の東どなり

    # 横通路の北はホール後方の通路（z=0）で 3.3 m 下がる。縁とブースの三方に
    # 手すりを立てる。ブースの南は横通路と面一なので開けておく
    for pts in ([(HALL_X0 - 0.32, cross_y1), (bt_x0, cross_y1)],
                [(bt_x1, cross_y1), (HALL_X1 + 0.32, cross_y1)],
                [(bt_x0, cross_y1), (bt_x0, bt_y1), (bt_x1, bt_y1),
                 (bt_x1, cross_y1)]):
        shell.railing(deck, pts, bz, h=1.05, mat="metal_white")

    # 天井の照明・吊り物
    lz = HALL_CEIL - 0.05
    for i in range(6):
        px = x0 + (x1 - x0) * (i + 0.5) / 6
        for j in range(4):
            py = y0 + 3.0 + (y1 - y0 - 4.0) * (j + 0.5) / 4
            kit.cyl(deck, px, py, lz - 0.55, lz, 0.34, "metal_dark", seg=8)
            kit.cyl(deck, px, py, lz - 0.62, lz - 0.55, 0.30, "light_panel",
                    seg=8)
            if (i + j) % 2 == 0:
                c.light(px, py, lz - 0.80, 900.0, 1.0)
    c.light(cx, y0 + 3.0, 6.0, 1600.0, 2.0)
    # POI は席のまん中だと前列の背もたれまで 0.18 m しか空かない（カプセル半径
    # 0.28）ので、ブロック間の通路（幅 aisle・中央に手すり）の手すりと座席の
    # 中間、段板の中ほどに置く（#42）
    pa = blocks[1][0] - aisle * 0.5          # 通路の手すりの通り
    c.poi("hall_seats", pa - 0.44, stage_y1 + run * 7 + 0.5, rise * 7)
    c.cam("=interior_lecture_hall", (cx - 12.0, y1 - 5.0, 6.60), (cx + 1.0, y0 + 3.4, 2.20),
          lens=18.0)


# --------------------------------------------------------------------------- #
def _atrium(c, x0, y0, x1, y1):
    """曲面ガラスに面した大階段ホール（3 層吹き抜け）。"""
    mb = c.furn("atrium")
    w = c.wall
    rng = c.rng
    fh = 4.40

    # 大階段の位置。西の列 A（中心 cx）と東の列 B（中心 cx + 3.8）を幅 3.6 で並べ、
    # 踊り場は A の西端 sa から B の東端 sb まで渡す。
    #   手前の踊り場 ym0..ym1: 1 階ぶんの途中（z + 2.2）で折り返す
    #   奥の踊り場 yf0..yf1: 2F / 3F の床の高さ。y0 + 14.0 でスラブの縁につながる
    cx = x1 - 6.5
    sa, sb = cx - 1.8, cx + 5.6
    ym0, ym1 = y0 + 1.6, y0 + 4.2
    yf0, yf1 = y0 + 11.4, y0 + 14.0

    # 2F / 3F のスラブ（吹き抜けを残す）
    for lv in (1, 2):
        z = fh * lv
        sx0 = x0 + 0.0
        shell.floor(w, sx0, y0 + 14.0, x1, y1, z, "floor_tile_grey",
                    thickness=0.45, side_mat="concrete_light")
        # 吹き抜け側のガラス手すり。階段の踊り場がつながる sa..sb は空ける
        for a, b in ((sx0, sa), (sb, x1)):
            shell.railing(w, [(a, y0 + 14.0), (b, y0 + 14.0)], z, h=1.10,
                          mat="metal_white", glass="glass_partition", post=2.2)
        # 東の縁（下は 1F まで抜けている）と奥の縁（中教室の天井の上）も止める
        shell.railing(w, [(x1, y0 + 14.0), (x1, y1)], z, h=1.10,
                      mat="metal_white", glass="glass_partition", post=2.2)
        shell.partition(w, (sx0, y1), (x1, y1), z, z + 3.30, "wall_white",
                        thick=0.20)
        shell.ceiling(w, sx0, y0 + 14.2, x1, y1 - 0.2, z + 3.30,
                      "ceiling_white", grid=1.8)
        shell.ceiling_lights(w, sx0 + 2.0, y0 + 16.0, x1 - 2.0, y1 - 2.0,
                             z + 3.30, sx=5.0, sy=5.0)
        base_lv = len(mb.verts)
        for i in range(3):
            px = sx0 + 3.0 + i * 8.0
            F.bench(mb, px, y0 + 15.4, ang=0.0, w=2.0, back=True)
            F.table(mb, px, y0 + 16.8, ang=0.0, w=1.2, d=0.6, h=0.42,
                    top="desk_dark")
        shell.planter(mb, sx0 + 1.4, y0 + 15.2, r=0.44, h=0.48, leaf_h=1.7)
        shell.trash_bins(mb, x1 - 1.4, y0 + 15.6, ang=math.pi * 0.5, n=2)
        kit.lift(mb, base_lv, z)

    # 大階段（1F -> 2F -> 3F）。1 階ぶんを 2 本で上る折り返し階段:
    #   A: 奥 yf0 から手前 ym1 へ上る -> 手前の踊り場で折り返す
    #   B: 手前 ym1 から奥 yf0 へ上る -> 奥の踊り場 = 2F / 3F の床
    for lv in (0, 1):
        zb = fh * lv
        zm = zb + fh * 0.5
        zt = zb + fh
        shell.stair_flight(w, cx, yf0, ym1, zb, zm, width=3.6,
                           tread_mat="floor_tile_grey", riser_mat="wall_white")
        shell.landing(w, sa, ym0, sb, ym1, zm)
        shell.railing(w, [(sa, ym1), (sa, ym0), (sb, ym0), (sb, ym1)], zm,
                      h=1.10, mat="metal_white")
        shell.stair_flight(w, cx + 3.8, ym1, yf0, zm, zt, width=3.6,
                           tread_mat="floor_tile_grey", riser_mat="wall_white")
        shell.landing(w, sa, yf0, sb, yf1, zt)
        shell.railing(w, [(sb, yf0), (sb, yf1)], zt, h=1.10, mat="metal_white")
        if lv == 0:
            shell.railing(w, [(sa, yf0), (sa, yf1)], zt, h=1.10,
                          mat="metal_white")
        else:
            # 3F は上へ続く A が無いので、踊り場の手前の縁も A の上で止める
            shell.railing(w, [(sa, yf1), (sa, yf0), (cx + 2.0, yf0)], zt,
                          h=1.10, mat="metal_white")
    c.poi("grand_stair", cx, y0 + 12.6, 0.0)

    # 柱と吹き抜けの縦ライン（階段と踊り場にかかる柱は立てない）
    for i in range(4):
        px = x0 + 4.0 + i * 8.0
        if sa - 0.6 < px < sb + 0.6:
            continue
        shell.column(w, px, y0 + 11.0, 0.0, VOID_TOP, size=0.80,
                     mat="concrete_light", round_=True)
    shell.ceiling(w, x0, y0, x1, y0 + 14.0, VOID_TOP, "ceiling_white",
                  grid=0.0)
    for i in range(5):
        px = x0 + 2.0 + i * 6.6
        kit.box(w, px - 3.0, y0 + 1.0, VOID_TOP - 0.55, px + 3.0, y0 + 1.6,
                VOID_TOP - 0.45, "light_strip")
        c.light(px, y0 + 6.5, VOID_TOP - 1.4, 880.0, 2.0)

    # ベンチ・植栽・サイン
    for i in range(4):
        px = x0 + 3.0 + i * 6.4
        F.bench(mb, px, y0 + 2.6, ang=0.0, w=2.2, back=(i % 2 == 0))
    shell.planter(mb, x0 + 1.6, y0 + 9.0, r=0.55, h=0.55, leaf_h=2.4)
    shell.planter(mb, x0 + 1.6, y0 + 12.5, r=0.55, h=0.55, leaf_h=2.2)
    shell.notice_board(mb, x0 + 0.20, y0 + 6.0, 1.05, ang=math.pi * 0.5,
                       w=4.0, h=1.45, sheets=12, rng=rng)
    common.sign_board(c, mb, x1 - 0.4, y0 + 3.2, 2.35, ang=-math.pi * 0.5,
                      w=2.4, h=0.60)
    shell.hanging_sign(mb, (x0 + x1) * 0.5, y0 + 3.0, 4.20, w=3.2, h=0.46,
                       drop=0.4)
    c.sign((x0 + x1) * 0.5, y0 + 3.0, 3.40)
    shell.trash_bins(mb, x0 + 1.8, y0 + 4.4, ang=-math.pi * 0.5, n=3)
    shell.vending(mb, x0 + 1.0, y0 + 16.0, ang=-math.pi * 0.5)
    for j in range(2):
        ty = y0 + 5.6 + j * 4.4
        for i in range(2):
            tx = x0 + 5.0 + i * 7.2
            F.table(mb, tx, ty, ang=0.0, w=1.8, d=0.9, h=0.72)
            for sgn in (-1, 1):
                for k in range(2):
                    F.chair_min(mb, F.T(tx - 0.45 + k * 0.9,
                                        ty + sgn * 0.82, 0.0,
                                        math.pi if sgn > 0 else 0.0),
                                mat="chair_blue" if (i + j) % 2 == 0
                                else "chair_green")
                    c.seats += 1
        shell.planter(mb, x0 + 16.0, ty, r=0.46, h=0.50, leaf_h=2.0)
    F.locker_bank(mb, x0 + 2.8, x0 + 9.8, y0 + 13.4, ang=math.pi, h=1.80)
    shell.planter(mb, x1 - 2.2, y0 + 9.4, r=0.50, h=0.52, leaf_h=2.1)
    shell.planter(mb, x1 - 2.2, y0 + 12.6, r=0.50, h=0.52, leaf_h=1.9)
    # 背もたれ付きベンチ。柱（x0 + 12, y0 + 11）と重ならないよう柱の間に置き、南を向ける
    F.bench(mb, x0 + 16.0, y0 + 12.2, ang=math.pi, w=2.4, back=True)
    c.poi("atrium", (x0 + x1) * 0.5, y0 + 7.0, 0.0)
    c.npc(x0 + 5.0, y0 + 5.0, 0.0)
    c.npc(x0 + 11.0, y0 + 9.0, 0.0)
    c.light((x0 + x1) * 0.5, y0 + 4.0, 3.0, 400.0, 2.0)


# --------------------------------------------------------------------------- #
def _lecture_room(c, x0, y0, x1, y1):
    """中教室（長机 + 椅子 + ホワイトボード + プロジェクター）。"""
    mb = c.furn("lecture_room")
    shell.partition(c.wall, (x0, y0), (x1, y0), 0.0, CEIL, thick=0.22,
                    gaps=[(3.0, 5.4), (20.0, 22.4)])
    shell.door(c.wall, x0 + 4.2, y0, ang=0.0, w=1.2, h=2.10,
               glass="glass_partition")
    shell.door(c.wall, x0 + 21.2, y0, ang=0.0, w=1.2, h=2.10,
               glass="glass_partition")
    shell.ceiling(c.wall, x0, y0, x1, y1, CEIL, "ceiling_white", grid=1.8)

    cx = (x0 + x1) * 0.5
    F.whiteboard(mb, cx, y1 - 0.3, 1.75, ang=math.pi, w=6.0, h=1.45)
    F.projection_screen(mb, cx + 4.0, y1 - 0.45, CEIL - 0.12, ang=math.pi,
                        w=3.2, h=2.0)
    F.projector(mb, cx, y1 - 5.2, CEIL - 0.10, ang=0.0)
    F.podium(mb, cx - 4.6, y1 - 1.8, ang=math.pi, w=1.1, d=0.6, h=1.10)
    c.poi("lecture_room", cx, y1 - 2.6, 0.0)
    c.npc(cx - 4.6, y1 - 2.6, 0.0)

    rows = 6
    total = 0
    for r in range(rows):
        yy = y1 - 4.4 - r * 1.55
        if yy < y0 + 1.6:
            break
        for bx in (cx - 6.6, cx + 0.4):
            F.long_desk(mb, bx + 2.9, yy, ang=0.0, w=5.8, d=0.55, h=0.73)
            for i in range(5):
                px = bx + 0.6 + i * 1.15
                F.chair_min(mb, F.T(px, yy - 0.62, 0.0, 0.0),
                            mat="chair_blue")
                total += 1
    c.seats += total
    c.note("中教室 %d 席" % total)
    pts = shell.ceiling_lights(c.wall, x0 + 1.5, y0 + 1.5, x1 - 1.5, y1 - 1.5,
                               CEIL, sx=3.6, sy=3.6)
    c.lights_from(pts, CEIL, energy=200.0, step=2)
    shell.exit_sign(c.wall, x0 + 4.2, y0 + 0.2, CEIL - 0.1)
    common.sign_board(c, mb, x0 + 5.6, y0 + 0.16, 2.30, ang=0.0, w=1.4, h=0.4)


# --------------------------------------------------------------------------- #
def _foyer(c, x0, y0, x1, y1):
    """大ホール北側のホワイエ（開演前の待合）。"""
    mb = c.furn("foyer")
    w = c.wall
    kit.plate(c.floor, x0, y0, x1, y1, 0.015, "floor_carpet_blue")
    shell.ceiling(w, x0, y0, x1, y1, CEIL, "ceiling_white", grid=1.8)
    pts = shell.ceiling_lights(w, x0 + 2.0, y0 + 1.6, x1 - 2.0, y1 - 1.6, CEIL,
                               sx=3.8, sy=3.8)
    c.lights_from(pts, CEIL, energy=190.0, step=2)

    cx = (x0 + x1) * 0.5
    # クローク兼チケットカウンター
    F.counter(mb, x0 + 2.0, y0 + 2.0, x0 + 9.0, y0 + 2.9, h=1.05,
              body="counter_wood", top="counter_stone")
    F.register(mb, x0 + 3.4, y0 + 2.2)
    F.monitor(mb, x0 + 5.6, y0 + 2.3, 1.05, ang=0.0)
    c.poi("foyer_counter", x0 + 5.0, y0 + 3.8, 0.0)
    c.npc(x0 + 5.0, y0 + 1.6, 0.0)

    # 待合のソファとラウンジチェア
    for i in range(4):
        sy = y0 + 6.0 + i * 4.6
        if sy > y1 - 3.0:
            break
        F.sofa(mb, cx - 5.0, sy, ang=0.0, w=2.6)
        F.sofa(mb, cx + 5.0, sy, ang=math.pi, w=2.6)
        F.table(mb, cx, sy, ang=0.0, w=1.4, d=0.8, h=0.42, top="sb_wood")
        F.lounge_chair(mb, cx - 1.6, sy + 1.4, ang=math.atan2(-1.6, -1.4))
        F.lounge_chair(mb, cx + 1.6, sy - 1.4, ang=math.atan2(1.6, 1.4))
        c.seats += 8

    # 掲示・自販機・ごみ箱・植栽
    shell.notice_board(w, x0 + 0.24, y0 + 8.0, 1.50, ang=math.pi * 0.5,
                       w=5.0, h=1.5, sheets=14, rng=c.rng)
    shell.vending(mb, x1 - 0.9, y0 + 5.0, ang=-math.pi * 0.5)
    shell.vending(mb, x1 - 0.9, y0 + 6.6, ang=-math.pi * 0.5, mat="fm_green")
    shell.trash_bins(mb, x1 - 1.4, y0 + 9.0, ang=-math.pi * 0.5, n=3)
    shell.planter(mb, x0 + 1.4, y1 - 2.0, r=0.60, h=0.55, leaf_h=2.2)
    shell.planter(mb, x1 - 1.4, y1 - 2.0, r=0.60, h=0.55, leaf_h=2.2)
    shell.fire_extinguisher(mb, x0 + 0.7, y0 + 1.2)
    shell.exit_sign(w, cx, y1 - 0.24, 2.60, ang=math.pi)
    common.sign_board(c, mb, cx, y1 - 0.30, 2.05, ang=math.pi, w=2.6, h=0.62)
    shell.hanging_sign(mb, cx, y0 + 4.2, CEIL - 0.10, w=3.0, h=0.44, drop=0.36)
    c.sign(cx, y0 + 4.2, CEIL - 0.60)
    c.poi("foyer", cx, (y0 + y1) * 0.5, 0.0)
    c.npc(cx - 3.0, y0 + 7.0, 0.0)
    c.npc(cx + 2.4, y0 + 13.0, 0.0)


# --------------------------------------------------------------------------- #
def _seminar_rooms(c, x0, y0, x1, y1):
    """東側の演習室 3 室 + 廊下。"""
    mb = c.furn("seminar")
    w = c.wall
    cor_x1 = x0 + 2.8
    kit.plate(c.floor, x0, y0, x1, y1, 0.015, "floor_tile_grey")
    shell.ceiling(w, x0, y0, x1, y1, CEIL, "ceiling_white", grid=1.8)
    # 廊下の入口。以前は西側の壁に開口が無く、演習室 3 室と廊下の POI・NPC には
    # ホワイエ側からも外からも入れなかった（#42）
    gy = y1 - 9.6                        # ホワイエ（Y=33 より北）に面した位置
    shell.partition(w, (x0, y0), (x0, y1), 0.0, CEIL, "wall_white", thick=0.20,
                    gaps=[(gy - y0, gy - y0 + 2.6)])
    shell.door(w, x0, gy + 1.3, ang=math.pi * 0.5, w=2.40, h=2.40)

    n = 3
    depth = (y1 - y0) / n
    for i in range(n):
        ry0 = y0 + depth * i + 0.15
        ry1 = y0 + depth * (i + 1) - 0.15
        cy = (ry0 + ry1) * 0.5
        # 廊下との間仕切り（ドア開口つき）
        shell.partition(w, (cor_x1, ry0), (cor_x1, ry1), 0.0, CEIL,
                        "wall_white", thick=0.16,
                        gaps=[(depth * 0.5 - 0.7, depth * 0.5 + 0.7)],
                        glass_top=True, glass_z=2.10)
        shell.door(w, cor_x1, cy, ang=math.pi * 0.5, w=1.15, h=2.10,
                   leaf="desk_white", glass="glass_partition")
        shell.wall_sign(mb, cor_x1 - 0.12, cy + 1.05, 2.05, ang=-math.pi * 0.5,
                        w=0.70, h=0.28)
        c.sign(cor_x1 - 0.12, cy + 1.05, 2.05)
        shell.exit_sign(w, cor_x1 - 0.08, cy, 2.85, ang=-math.pi * 0.5)
        if i < n - 1:
            shell.partition(w, (cor_x1, ry1 + 0.15), (x1, ry1 + 0.15), 0.0,
                            CEIL, "wall_white", thick=0.16)
        _seminar_room(c, mb, cor_x1, ry0, x1, ry1, i)

    # 廊下の設え（廊下は Y 方向に伸びるので自前で等間隔に置く）
    steps = max(1, int((y1 - y0) / 9.0))
    for k in range(steps):
        cy = y0 + (y1 - y0) * (k + 0.5) / steps
        if k % 2 == 0:
            shell.exit_sign(w, x0 + 0.22, cy, CEIL - 0.30,
                            ang=math.pi * 0.5)
        if k % 3 == 1:
            shell.fire_extinguisher(mb, x0 + 0.55, cy)
        if k % 3 == 2:
            F.bench(mb, x0 + 0.9, cy, ang=-math.pi * 0.5, w=1.8)  # 廊下向き
    shell.trash_bins(mb, x0 + 1.0, y0 + 3.0, ang=math.pi * 0.5, n=2)
    shell.fire_extinguisher(mb, x0 + 0.6, y1 - 3.0)
    pts = shell.ceiling_lights(w, x0 + 1.2, y0 + 1.5, x1 - 1.2, y1 - 1.5, CEIL,
                               sx=3.6, sy=3.8)
    c.lights_from(pts, CEIL, energy=190.0, step=3)


def _seminar_room(c, mb, x0, y0, x1, y1, idx):
    """演習室 1 室（長机 + 椅子 + ホワイトボード）。"""
    cy = (y0 + y1) * 0.5
    F.whiteboard(mb, x1 - 0.26, cy, 1.75, ang=math.pi * 0.5, w=4.2, h=1.35)
    F.projection_screen(mb, x1 - 0.40, cy + 3.0, CEIL - 0.14, ang=-math.pi * 0.5,
                        w=2.6, h=1.7)
    F.projector(mb, x1 - 4.6, cy, CEIL - 0.10, ang=-math.pi * 0.5)
    F.podium(mb, x1 - 1.5, cy - 2.6, ang=math.pi * 0.5, w=1.0, d=0.6, h=1.10)

    total = 0
    for r in range(4):
        rx = x1 - 3.4 - r * 1.60
        if rx < x0 + 1.6:
            break
        for k in range(2):
            ty = cy - 4.2 + k * 8.0
            F.long_desk(mb, rx, ty, ang=-math.pi * 0.5, w=6.4, d=0.55, h=0.73)
            for i in range(5):
                py = ty - 2.6 + i * 1.30
                F.chair_min(mb, F.T(rx - 0.62, py, 0.0, -math.pi * 0.5),
                            mat="chair_blue")
                total += 1
    c.seats += total
    if idx == 0:
        c.poi("seminar", (x0 + x1) * 0.5, cy, 0.0)
        c.npc(x1 - 1.5, cy - 4.4, 0.0)      # 教壇の真南 0.8 m だと空きが 0.26 m（#42）
        c.npc((x0 + x1) * 0.5, cy + 2.0, 0.0)
