"""体育館: アリーナ（バスケットコート・ゴール・ステージ・観覧席・屋根トラス）。q_gym の舞台。"""

import math

from . import common, furniture as F, kit, shell

CEIL = 11.60         # トラス下端より上のスラブ
TRUSS_Z = 10.40
Z_TOP = 12.40
COURT_W = 15.0       # コート幅（X）
COURT_L = 28.0       # コート長（Y）
# FIBA のゴール位置: バックボードの表面がエンドラインの 1.2 m 内側、リングの中心が
# 1.575 m 内側。移動式ゴールの支柱はエンドラインの外に置く。
HOOP_BOARD_IN = 1.20
HOOP_POST_OUT = 0.675


def build(c):
    s = c.spec
    ix0, iy0, ix1, iy1 = s.inner()
    cx = (ix0 + ix1) * 0.5

    # 高窓（腰高 4.2 m）。天井は張らずトラスを見せる。
    common.envelope(c, CEIL, "floor_wood", door_w=5.0, z_top=Z_TOP,
                    sill=4.20, header=1.20, seg=3.6, ceil=False,
                    wall="wall_white")
    common.entry_kit(c, 3.0, door_w=5.0)
    shell.ceiling(c.wall, ix0, iy0, ix1, iy1, CEIL, "ceiling_dark", grid=0.0)

    # 玄関ホール（下足エリア）と、アリーナとの段差
    hall_y1 = iy0 + 5.0
    # 玄関マット（entry_kit, z=0.012）より下に敷く。0.016 だとマットが埋もれた
    kit.plate(c.floor, ix0, iy0, ix1, hall_y1, 0.008, "floor_tile_grey")
    ent = c.furn("entry_hall")
    F.locker_bank(ent, cx - 9.0, cx - 2.0, iy0 + 0.6, ang=0.0, h=1.80)
    F.locker_bank(ent, cx + 2.0, cx + 9.0, iy0 + 0.6, ang=0.0, h=1.80)
    F.bench(ent, cx - 6.0, iy0 + 2.6, ang=0.0, w=2.4)
    F.bench(ent, cx + 6.0, iy0 + 2.6, ang=0.0, w=2.4)
    shell.notice_board(c.wall, ix0 + 3.0, iy0 + 0.4, 1.50, ang=0.0, w=2.4,
                       h=1.2, rng=c.rng)
    # 窓台（4.2 m）より下の南の壁に、コート側を向けて掛ける
    shell.clock(c.wall, cx + 5.2, iy0 + 0.01, 3.30, ang=0.0, r=0.34)
    common.sign_board(c, ent, cx, iy0 + 0.30, 2.45, ang=0.0, w=2.6, h=0.6)
    shell.vending(ent, ix1 - 1.6, iy0 + 2.4, ang=-math.pi * 0.5)
    shell.vending(ent, ix1 - 1.6, iy0 + 4.0, ang=-math.pi * 0.5, mat="fm_green")

    hoop_base = _court(c, cx, hall_y1)
    _stage(c, ix0, ix1, iy1, hoop_base)
    _bleachers(c, ix0, ix1, iy0, iy1, cx, hall_y1)
    _storage(c, ix0, ix1, iy0, iy1, cx, hall_y1)
    _truss(c, ix0, ix1, iy0, iy1)

    c.cam("", (cx - 11.0, hall_y1 + 2.0, 3.10), (cx + 3.0, iy1 - 12.0, 1.40),
          lens=21.0)
    c.note("アリーナ（28×15 m コート・ゴール 2 基・ステージ・観覧席・屋根トラス）")


# --------------------------------------------------------------------------- #
def _court(c, cx, hall_y1):
    """フロアのコートライン。実ジオメトリの薄板で引く。

    戻り値は北のゴールの台座が床を占める範囲 (x0, x1, y_north)。
    """
    mb = c.furn("court")
    # 南のエンドラインは玄関ホールから 2.0 m。北のエンドラインと舞台の前面の間
    # （体育館の内寸で決まり約 2.1 m）には、北のゴールの台座が入る。
    cy = hall_y1 + 2.0 + COURT_L * 0.5
    x0, x1 = cx - COURT_W * 0.5, cx + COURT_W * 0.5
    y0, y1 = cy - COURT_L * 0.5, cy + COURT_L * 0.5
    lw = 0.05
    Z = 0.020

    def band(a0, b0, a1, b1):
        kit.plate(mb, a0, b0, a1, b1, Z, "court_line")

    # アウトライン
    band(x0 - lw, y0 - lw, x1 + lw, y0 + lw)
    band(x0 - lw, y1 - lw, x1 + lw, y1 + lw)
    band(x0 - lw, y0, x0 + lw, y1)
    band(x1 - lw, y0, x1 + lw, y1)
    # センターライン + センターサークル
    band(x0, cy - lw, x1, cy + lw)
    _circle(mb, cx, cy, 1.80, lw, Z)
    # 制限区域・フリースローサークル・3 ポイントライン（FIBA）。
    # sgn=-1 が南のエンドライン y0、+1 が北の y1。コートの内側は -sgn 方向。
    t3 = math.acos(6.60 / 6.75)            # 弧と直線がつながる角度
    y3 = 1.575 + 6.75 * math.sin(t3)       # 直線部の長さ（2.99 m）
    for sgn in (-1, 1):
        by = y0 if sgn < 0 else y1
        ky = by - sgn * 5.80
        band(cx - 2.45 - lw, ky - lw, cx + 2.45 + lw, ky + lw)
        band(cx - 2.45 - lw, min(by, ky), cx - 2.45 + lw, max(by, ky))
        band(cx + 2.45 - lw, min(by, ky), cx + 2.45 + lw, max(by, ky))
        _circle(mb, cx, ky, 1.80, lw, Z)
        rim_y = by - sgn * 1.575
        inward = 0.0 if sgn < 0 else math.pi   # 弧をコートの内側へ振る
        _arc(mb, cx, rim_y, 6.75, lw, Z, a0=inward + t3, span=math.pi - 2 * t3)
        # ノーチャージ半円（半径 1.25 m）
        _arc(mb, cx, rim_y, 1.25 + lw * 2, lw, Z, a0=inward, span=math.pi,
             seg=10)
        for s2 in (-1, 1):
            band(cx + s2 * (6.60 - lw) - lw, min(by, by - sgn * y3),
                 cx + s2 * (6.60 - lw) + lw, max(by, by - sgn * y3))

    # ゴール 2 基（移動式。台座はエンドラインの外、腕でコートへ張り出す）
    arm = HOOP_POST_OUT + HOOP_BOARD_IN - 0.06
    for sgn in (-1, 1):
        by = y0 if sgn < 0 else y1
        F.basketball_hoop(mb, cx, by + sgn * HOOP_POST_OUT,
                          ang=0.0 if sgn < 0 else math.pi, arm=arm)
    hoop_base = (cx - 0.35, cx + 0.35,
                 y1 + HOOP_POST_OUT + F.hoop_base_back(arm))
    # バレーボールコート（18 x 9 m、青ライン）
    vw, vl = 9.0, 18.0
    vx0, vx1 = cx - vw * 0.5, cx + vw * 0.5
    vy0, vy1 = cy - vl * 0.5, cy + vl * 0.5

    def vband(a0, b0, a1, b1):
        kit.plate(mb, a0, b0, a1, b1, Z + 0.004, "court_line_blue")

    vband(vx0 - lw, vy0 - lw, vx1 + lw, vy0 + lw)
    vband(vx0 - lw, vy1 - lw, vx1 + lw, vy1 + lw)
    vband(vx0 - lw, vy0, vx0 + lw, vy1)
    vband(vx1 - lw, vy0, vx1 + lw, vy1)
    for sgn in (-1, 1):
        vband(vx0, cy + sgn * 3.0 - lw, vx1, cy + sgn * 3.0 + lw)
    # 支柱とネット
    for sgn in (-1, 1):
        kit.cyl(mb, cx + sgn * (vw * 0.5 + 0.6), cy, 0.0, 2.55, 0.055,
                "metal_gray", seg=6)
    _volley_net(mb, cx - vw * 0.5 - 0.5, cx + vw * 0.5 + 0.5, cy)

    # バドミントンコート 2 面
    for sgn in (-1, 1):
        bcx = cx + sgn * 4.4
        bx0, bx1 = bcx - 3.05, bcx + 3.05
        by0, by1 = cy - 6.7, cy + 6.7
        for (a0, b0, a1, b1) in (
                (bx0 - lw, by0 - lw, bx1 + lw, by0 + lw),
                (bx0 - lw, by1 - lw, bx1 + lw, by1 + lw),
                (bx0 - lw, by0, bx0 + lw, by1),
                (bx1 - lw, by0, bx1 + lw, by1),
                (bx0, by0 + 1.98 - lw, bx1, by0 + 1.98 + lw),
                (bx0, by1 - 1.98 - lw, bx1, by1 - 1.98 + lw)):
            kit.plate(mb, a0, b0, a1, b1, Z + 0.008, "court_line")

    # バレーのネット（y=cy・高さ 1.43〜2.43 m）の真下だと体のまわりの空きが
    # 0.02 m しか無いので、コートの中で 2.4 m 手前へずらす（#42）
    c.poi("court", cx, cy - 2.4, 0.0)
    c.npc(cx - 4.0, cy - 6.0, 0.0)
    c.npc(cx + 3.5, cy + 4.0, 0.0)
    c.npc(cx, cy - 11.0, 0.0)
    return hoop_base


def _volley_net(mb, nx0, nx1, y, z0=1.43, z1=2.43, pitch=0.25):
    """バレーのネット。上下の白帯と黒い網糸で組み、向こう側が透けて見えるようにする。

    1 枚の箱で張ると、コートの長手方向の視界が高さ 1 m の灰色の壁でふさがれた。
    """
    kit.box(mb, nx0, y - 0.012, z1 - 0.07, nx1, y + 0.012, z1, "net_white")
    kit.box(mb, nx0, y - 0.008, z0, nx1, y + 0.008, z0 + 0.05, "net_white")
    for x in (nx0, nx1):
        kit.box(mb, x - 0.025, y - 0.010, z0, x + 0.025, y + 0.010, z1,
                "net_white")
    za, zb = z0 + 0.05, z1 - 0.07
    n = max(2, int(round((nx1 - nx0) / pitch)))
    for i in range(1, n):
        x = nx0 + (nx1 - nx0) * i / n
        kit.tube(mb, (x, y, za), (x, y, zb), 0.007, "rubber_black", seg=3)
    m = max(1, int(round((zb - za) / pitch)))
    for k in range(1, m):
        z = za + (zb - za) * k / m
        kit.tube(mb, (nx0, y, z), (nx1, y, z), 0.007, "rubber_black", seg=3)


def _circle(mb, cx, cy, r, lw, z, seg=24):
    _arc(mb, cx, cy, r, lw, z, a0=0.0, span=math.pi * 2, seg=seg)


def _arc(mb, cx, cy, r, lw, z, a0=0.0, span=math.pi, seg=20):
    n = max(4, int(seg * span / math.pi))
    for i in range(n):
        t0 = a0 + span * i / n
        t1 = a0 + span * (i + 1) / n
        p0 = (cx + r * math.cos(t0), cy + r * math.sin(t0))
        p1 = (cx + r * math.cos(t1), cy + r * math.sin(t1))
        q0 = (cx + (r - lw * 2) * math.cos(t0), cy + (r - lw * 2) * math.sin(t0))
        q1 = (cx + (r - lw * 2) * math.cos(t1), cy + (r - lw * 2) * math.sin(t1))
        mb.add_quad((p0[0], p0[1], z), (p1[0], p1[1], z),
                    (q1[0], q1[1], z), (q0[0], q0[1], z), "court_line")


def _stage(c, x0, x1, y1, hoop_base):
    """奥のステージ（式典用）。hoop_base は北のゴールの台座 (x0, x1, y_north)。"""
    mb = c.furn("stage")
    sx0, sx1 = x0 + 8.0, x1 - 8.0
    sy0, sy1 = y1 - 7.0, y1 - 0.4
    # 前面の上り段は両端に。中央は北のゴールの台座が来る（コートは中央）
    steps, step_w, step_d = (sx0 + 2.6, sx1 - 2.6), 2.0, 0.9
    hx0, hx1, hy1 = hoop_base
    assert hy1 < sy0, "ゴールの台座が舞台に食い込む"
    for sx in steps:
        assert (hy1 <= sy0 - step_d or sx + step_w * 0.5 <= hx0
                or sx - step_w * 0.5 >= hx1), "上り段がゴールの台座に重なる"
    F.stage(mb, sx0, sy0, sx1, sy1, h=0.90, steps=steps, step_w=step_w)
    # 緞帳
    kit.box(c.wall, sx0 - 0.4, sy1 - 0.45, 0.90, sx1 + 0.4, sy1 - 0.30, 7.40,
            "curtain_blue")
    for i in range(14):
        px = sx0 + (sx1 - sx0) * i / 13.0
        kit.cyl(c.wall, px, sy1 - 0.52, 0.90, 7.20, 0.10, "curtain_blue", seg=5)
    # 校章代わりのパネル。緞帳のひだ（手前端 sy1-0.62）のすぐ前に吊り、客席を向ける
    mx = (sx0 + sx1) * 0.5
    py = sy1 - 0.70
    kit.box(c.wall, mx - 1.5, py, 4.30, mx + 1.5, py + 0.04, 6.10, "wall_wood")
    kit.vplate(c.wall, (mx - 1.3, py - 0.005), (mx + 1.3, py - 0.005),
               4.50, 5.90, "paper_white")
    c.wall.add_face([(mx + 0.55 * math.cos(math.pi * 2 * k / 12), py - 0.01,
                      5.20 + 0.55 * math.sin(math.pi * 2 * k / 12))
                     for k in range(12)], "wall_accent_navy")
    # 演台は客席（-Y）へ向け、舞台の高さ（0.90 m）まで持ち上げる
    base_pd = len(mb.verts)
    F.podium(mb, (sx0 + sx1) * 0.5 - 2.0, sy0 + 1.6, ang=math.pi)
    kit.lift(mb, base_pd, 0.90)
    for i in range(3):
        shell.column(c.wall, sx0 - 1.2, sy0 + 1.0 + i * 2.4, 0.0, CEIL,
                     size=0.50, mat="concrete_light")
        shell.column(c.wall, sx1 + 1.2, sy0 + 1.0 + i * 2.4, 0.0, CEIL,
                     size=0.50, mat="concrete_light")
    # ステージ袖の階段
    for sgn in (-1, 1):
        px = (sx0 - 1.0) if sgn < 0 else (sx1 + 1.0)
        for k in range(5):
            kit.box(mb, px - 0.9, sy0 - 0.2 + k * 0.28, 0.0,
                    px + 0.9, sy0 + 1.2, 0.18 * (k + 1), "floor_wood")
    c.poi("stage", (sx0 + sx1) * 0.5, sy0 + 2.6, 0.90)
    c.npc((sx0 + sx1) * 0.5 - 2.0, sy0 + 2.4, 0.90)


def _bleachers(c, x0, x1, y0, y1, cx, hall_y1):
    """片側の壁ぎわに観覧席、反対側に肋木。"""
    mb = c.furn("seats")
    by0 = hall_y1 + 3.0
    by1 = y1 - 8.0
    rows = 10
    run = 0.82
    # 蹴上は CharacterController の stepOffset（0.40 m）より低くする。
    # 0.42 だとコートから最前列にも、段から段にも上がれなかった（#42）。
    rise = 0.35
    # ang=+90°: ローカル (dx, dy) -> ワールド (-dy, dx)。
    # 最前列をコート側に置き、-X 側の壁へ向かってせり上がる（客はコート = +X を向く）。
    seats = F.bleachers(mb, by0, by1, -(x0 + 0.6 + rows * run), rows=rows, rise=rise,
                        run=run, ang=math.pi * 0.5,
                        seat_mat="chair_hall_red", seats_every=0.52)
    c.seats += seats
    c.note("観覧席 %d 席" % seats)
    top_x = x0 + 0.6            # 最上段の背（壁との隙間 0.6 m に落ちないよう）
    shell.railing(c.wall, [(top_x, by0), (top_x, by1)], rise * rows,
                  h=1.05, mat="metal_white")
    # 観覧席へ上がる階段。上り切りの z は段 r=4 の天端（rise * 5）と同じ高さ。
    # 上り切り（by0 - 0.4）から観覧席の最前列（by0）までは床が無かったので、
    # 段 r=5 と r=4 に跨る幅（run の左右 1 段ぶん）の踊り場でつなぐ。
    st_x = x0 + 0.6 + rows * run * 0.5
    st_z = rise * rows * 0.5
    shell.stair_flight(c.wall, st_x, by0 - 3.4, by0 - 0.4, 0.0, st_z, width=1.8,
                       tread_mat="concrete_light", rail=True)
    shell.landing(c.wall, st_x - run, by0 - 0.4, st_x + run, by0, st_z,
                  mat="concrete_light")
    # 南北の端の腰壁。蹴上を 0.35 m にして段を上れるようにしたぶん、段の上を
    # 南北に歩くと端からコートへ落ちられるようになった（最大 3.5 m）ので、
    # 段なりの壁でふさぐ。南側は踊り場から入る 2 段ぶんだけ開ける（#42）
    front_x = x0 + 0.6 + rows * run
    for r in range(rows):
        ex0 = front_x - (r + 1) * run
        ex1 = front_x - r * run
        ez = rise * (r + 1)
        for yy, sgn in ((by0, -1.0), (by1, 1.0)):
            if sgn < 0 and ex0 > st_x - run - 0.01 and ex1 < st_x + run + 0.01:
                continue
            ya, yb = min(yy, yy + sgn * 0.10), max(yy, yy + sgn * 0.10)
            kit.box(c.wall, ex0, ya, 0.0, ex1, yb, ez, "concrete_light")
            kit.box(c.wall, ex0, ya, ez, ex1, yb, ez + 1.00, "metal_white")
    # POI / NPC は段の踏面の中心へ。段の端だと次の段の蹴上に食い込む
    x_r2 = x0 + 0.6 + run * (rows - 2.5)      # 天端 rise * 3 の段
    x_r3 = x0 + 0.6 + run * (rows - 3.5)      # 天端 rise * 4 の段
    c.poi("bleachers", x_r2, (by0 + by1) * 0.5, rise * 3)
    c.npc(x_r2, by0 + 6.0, rise * 3)
    c.npc(x_r3, by0 + 12.0, rise * 4)
    # 撮影スポット ps_gym_catwalk の立ち位置。キャットウォーク (z=8.50) は
    # 上がる階段もはしごも無く BFS 到達 0 だったので、観覧席の最上段
    # （天端 rise*rows = 3.50 m・背は手すり）へ振り替える。+Y を向くと
    # アリーナを長手に見下ろせる (#42)
    c.poi("catwalk", x0 + 0.6 + run * 0.5, by0 + 2.0, rise * rows)

    # 反対側の壁に肋木と用具
    F.wall_bars(mb, -(by0 + 14.0), -(by0 + 2.0), x1 - 0.3, z0=0.4, z1=2.60,
                ang=-math.pi * 0.5)
    for i in range(4):
        kit.box(mb, x1 - 1.5, by0 + 18.0 + i * 1.3, 0.0, x1 - 0.4,
                by0 + 19.0 + i * 1.3, 1.10, "metal_gray", top="metal_gray")
    shell.fire_extinguisher(mb, x1 - 0.8, y0 + 6.0)
    shell.fire_extinguisher(mb, x0 + 0.8, y1 - 9.0)


def _truss(c, x0, x1, y0, y1):
    """屋根トラスと高天井の照明。"""
    w = c.wall
    n = 7
    for i in range(n):
        ty = y0 + (y1 - y0) * (i + 0.5) / n
        F.roof_truss(w, x0 + 0.3, x1 - 0.3, ty, TRUSS_Z, depth=1.5)
    # 縦つなぎ
    for k in range(5):
        tx = x0 + (x1 - x0) * (k + 0.5) / 5
        kit.tube(w, (tx, y0 + 1.0, TRUSS_Z - 1.2), (tx, y1 - 1.0, TRUSS_Z - 1.2),
                 0.07, "metal_gray", seg=5)
    # 高所照明
    pts = shell.ceiling_lights(w, x0 + 3.0, y0 + 3.0, x1 - 3.0, y1 - 3.0,
                               TRUSS_Z - 0.2, sx=6.0, sy=6.0, w=1.0, l=1.0,
                               mat="light_panel")
    c.lights_from(pts, TRUSS_Z - 0.4, energy=620.0, step=2, radius=3.0)


# --------------------------------------------------------------------------- #
def _storage(c, x0, x1, y0, y1, cx, hall_y1):
    """用具庫・体操器具・スコアボード。アリーナの密度を上げる。"""
    mb = c.furn("equipment")
    w = c.wall
    rng = c.rng

    # --- 用具庫（+X 側の奥、シャッターつき） ---
    sx0, sx1 = x1 - 7.6, x1 - 0.3
    sy0, sy1 = y1 - 16.0, y1 - 8.4
    shell.partition(w, (sx0, sy0), (sx0, sy1), 0.0, 4.20, "wall_grey",
                    thick=0.22, gaps=[(2.0, 5.2)])
    shell.partition(w, (sx0, sy0), (sx1, sy0), 0.0, 4.20, "wall_grey",
                    thick=0.22)
    shell.partition(w, (sx0, sy1), (sx1, sy1), 0.0, 4.20, "wall_grey",
                    thick=0.22)
    shell.ceiling(w, sx0, sy0, sx1, sy1, 4.20, "ceiling_dark", grid=0.0)
    # シャッター（半開き）
    kit.box(w, sx0 - 0.06, sy0 + 2.0, 2.40, sx0 + 0.06, sy0 + 5.2, 4.10,
            "metal_gray")
    shell.exit_sign(w, sx0 - 0.10, sy0 + 3.6, 3.60, ang=-math.pi * 0.5)
    common.sign_board(c, mb, sx0 - 0.16, sy0 + 6.4, 2.40, ang=-math.pi * 0.5,
                      w=1.4, h=0.40)
    # 棚とボールかご
    for i in range(3):
        F.bookshelf(mb, sx1 - 0.7, sy0 + 1.2 + i * 2.2, ang=math.pi * 0.5,
                    w=2.0, h=2.10, shelves=4, rng=rng, body="metal_gray",
                    depth=0.60, books=False)
    for i in range(6):
        bx = sx0 + 1.6 + (i % 2) * 2.4
        by = sy0 + 1.4 + (i // 2) * 2.0
        kit.box(mb, bx - 0.55, by - 0.45, 0.0, bx + 0.55, by + 0.45, 0.10,
                "metal_dark")
        for k in range(4):
            kit.box(mb, bx - 0.55, by - 0.45 + k * 0.28, 0.10,
                    bx + 0.55, by - 0.41 + k * 0.28, 0.95, "metal_gray")
        for k in range(6):
            kit.blob(mb, bx - 0.34 + (k % 3) * 0.34,
                     by - 0.22 + (k // 3) * 0.34, 0.24, 0.12, 0.12, 0.12,
                     "chair_orange", seg=6, rings=3)
    # ボールかご（x = sx0+1.05..2.15 と 3.45..4.55）と棚（sx1-1.0 から）の間の
    # 通路。sx0+3.6 だとかごまで 0.17 m しか空いていなかった（#42）
    c.poi("storage", sx0 + 5.4, sy0 + 3.6, 0.0)

    # --- 体操器具（コート脇に並べる） ---
    ey = hall_y1 + 1.6
    # 跳び箱 3 台（段ごとに少しずつ小さく）
    for j in range(3):
        bx = cx + 9.0 + j * 1.9
        for k in range(6):
            hw = 0.62 - k * 0.045
            kit.box(mb, bx - hw, ey - hw * 0.72, 0.16 * k,
                    bx + hw, ey + hw * 0.72, 0.16 * (k + 1),
                    "wood" if k < 5 else "fabric_beige")
    # 体操マット（積み重ね）
    for j in range(4):
        mx = cx + 9.0 + j * 1.5
        for k in range(3):
            kit.box(mb, mx - 0.70, ey + 2.4 - 0.45, 0.06 * k,
                    mx + 0.70, ey + 2.4 + 0.45, 0.06 * (k + 1),
                    "cushion_red" if (j + k) % 2 else "fabric_green")
    # 平均台 2 台
    for j in range(2):
        bx = cx + 9.4 + j * 1.6
        kit.box(mb, bx - 0.09, ey + 4.2 - 2.0, 1.15, bx + 0.09,
                ey + 4.2 + 2.0, 1.25, "wood")
        for sgn in (-1, 1):
            kit.box(mb, bx - 0.30, ey + 4.2 + sgn * 1.6, 0.0,
                    bx + 0.30, ey + 4.2 + sgn * 1.8, 1.15, "metal_gray")
    # 得点板（両妻面の高い位置）
    for sgn in (-1, 1):
        by = y0 + 1.0 if sgn < 0 else y1 - 1.0
        kit.box(w, cx - 2.4, by - 0.16, 6.20, cx + 2.4, by + 0.16, 8.00,
                "metal_dark")
        # 表示面はコート側へ（南は +Y、北は -Y）
        kit.vplate(w, (cx - 2.2, by - sgn * 0.20), (cx + 2.2, by - sgn * 0.20),
                   6.40, 7.80, "screen_blue", flip=sgn < 0)
    # 放送・審判席
    F.table(mb, cx - 10.0, hall_y1 + 2.4, ang=0.0, w=2.4, d=0.8, h=0.74,
            top="desk_dark")
    for i in range(2):
        F.chair(mb, cx - 10.6 + i * 1.2, hall_y1 + 3.2, ang=math.pi,
                mat="chair_grey")
    F.monitor(mb, cx - 10.0, hall_y1 + 2.3, 0.74, ang=math.pi)
    c.npc(cx - 10.0, hall_y1 + 3.4, 0.0)
    _extras(c, x0, x1, y0, y1, cx, hall_y1)
    c.note("用具庫 + 体操器具（跳び箱・マット・平均台）+ 得点板 + 肋木・キャットウォーク")


def _extras(c, x0, x1, y0, y1, cx, hall_y1):
    """肋木・折りたたみ椅子の山・卓球台・キャットウォーク。"""
    mb = c.furn("extras")
    w = c.wall

    # --- 肋木（-X 側の壁に 6 連） ---
    for j in range(6):
        by = hall_y1 + 4.0 + j * 3.0
        if by > y1 - 3.0:
            break
        for sgn in (-1, 1):
            kit.box(mb, x0 + 0.22, by + sgn * 0.45 - 0.05, 0.30,
                    x0 + 0.34, by + sgn * 0.45 + 0.05, 2.70, "wood")
        for k in range(13):
            rz = 0.35 + k * 0.185
            kit.cyl(mb, x0 + 0.28, by, rz - 0.018, rz + 0.018, 0.018, "wood",
                    seg=5)
        kit.cyl(mb, x0 + 0.28, by - 0.45, 0.30, 2.70, 0.019, "wood", seg=4)

    # --- 折りたたみ椅子の山（用具庫まわり） ---
    for j in range(12):
        sx = x1 - 10.4 + (j % 4) * 0.62
        sy = y1 - 6.4 + (j // 4) * 0.90
        for k in range(11):
            kit.box(mb, sx - 0.24, sy - 0.26 + k * 0.022, 0.42 + k * 0.035,
                    sx + 0.24, sy + 0.26 + k * 0.022, 0.50 + k * 0.035,
                    "chair_blue" if j % 2 == 0 else "chair_grey")
        kit.box(mb, sx - 0.22, sy - 0.24, 0.0, sx - 0.18, sy + 0.24, 0.45,
                "metal_gray")
        kit.box(mb, sx + 0.18, sy - 0.24, 0.0, sx + 0.22, sy + 0.24, 0.45,
                "metal_gray")

    # --- 卓球台 4 台（コート脇に展開） ---
    for j in range(4):
        tx = cx - 12.0 + (j % 2) * 2.4
        ty = hall_y1 + 6.0 + (j // 2) * 3.4
        kit.box(mb, tx - 0.76, ty - 1.37, 0.72, tx + 0.76, ty + 1.37, 0.76,
                "fm_green")
        kit.plate(mb, tx - 0.76, ty - 0.01, tx + 0.76, ty + 0.01, 0.77,
                  "line_white")
        kit.box(mb, tx - 0.80, ty - 0.008, 0.76, tx + 0.80, ty + 0.008, 0.91,
                "net_white")
        for sgn_x in (-1, 1):
            for sgn_y in (-1, 1):
                kit.box(mb, tx + sgn_x * 0.60 - 0.04, ty + sgn_y * 1.18 - 0.04,
                        0.0, tx + sgn_x * 0.60 + 0.04,
                        ty + sgn_y * 1.18 + 0.04, 0.72, "metal_dark")

    # --- キャットウォーク（長手 2 本 + 手すり） ---
    for sgn in (-1, 1):
        wx = cx + sgn * ((x1 - x0) * 0.5 - 3.2)
        kit.box(w, wx - 0.55, hall_y1 + 1.0, 8.40, wx + 0.55, y1 - 1.0, 8.50,
                "metal_gray")
        for rail in (0.55, -0.55):
            kit.tube(w, (wx + rail, hall_y1 + 1.0, 9.50),
                     (wx + rail, y1 - 1.0, 9.50), 0.035, "metal_gray", seg=5)
            n = max(2, int((y1 - hall_y1 - 2.0) / 2.2))
            for k in range(n + 1):
                py = hall_y1 + 1.0 + (y1 - hall_y1 - 2.0) * k / n
                kit.tube(w, (wx + rail, py, 8.50), (wx + rail, py, 9.50),
                         0.028, "metal_gray", seg=4)
    # --- 横断幕（南の妻面。北は舞台の緞帳が前に来るので掛けない） ---
    kit.box(w, cx - 7.0, y0 + 0.28, 4.60, cx + 7.0, y0 + 0.30, 5.60,
            "wall_accent_navy")
    for j in range(6):
        mx = x0 + 3.0 + j * 2.2
        kit.box(mb, mx - 0.95, y0 + 0.40, 0.0, mx + 0.95, y0 + 0.46, 1.90,
                "cushion_red" if j % 2 else "fabric_green")
    # poi_gym_catwalk は _bleachers() が観覧席の最上段に置く（ここのキャット
    # ウォークには上がる手段が無く、立てないため）
