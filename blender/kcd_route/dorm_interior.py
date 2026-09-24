"""葛飾コミュニティハウス（学生寮）の屋内（1F）。

外観は kcd_route/dorm.py。名前（ID / DISPLAY / 運営者）と玄関の位置はそちらから読む。

座標は **建物ローカル**（原点 = entrance_dorm の真下の床、+Y = 入口から奥、+X = 右、+Z = 上）。
kcd_interior の Ctx / InteriorSpec 契約にそのまま乗るので、Unity 側は既存 9 棟と同じ
``spawn_<id>`` / ``exit_<id>`` / ``npc_<id>_<n>`` / ``poi_<id>_<name>`` を読めばよい。

間取りは公開図面が無いので推定。設備の種類（大浴場・マッサージチェア・カフェラウンジ・
キッチンコーナー・管理人室・オートロック・IC キーのフロア制御）は公式ページの記載、
色と什器は館内写真から読んでいる。居室は 2F（女子）と 3–5F（男子）にあるので 1F には置かない。
専用の什器は dorm_interior_props.py。

使い方
------
    from kcd_route import dorm_interior

    sp = dorm_interior.make_spec(route["dormitory"])
    c = Ctx(sp); dorm_interior.build(c)
"""

import math
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))

from kcd_lib import geom                             # noqa: E402
from kcd_interior import common, kit                 # noqa: E402
from kcd_interior import furniture as F              # noqa: E402
from kcd_interior import shell as sh                 # noqa: E402
from kcd_interior.spec import InteriorSpec           # noqa: E402
from kcd_route import dorm_interior_props as P       # noqa: E402
from kcd_route.dorm import DISPLAY, ID, NOT_UNIVERSITY, OPERATOR, entrance_of  # noqa: E402

# 屋内の寸法
X0, X1 = -8.00, 8.00
Y_FACE, Y_BACK = 2.80, 40.80      # 2.80 = 玄関の風除室 D 2.0 + 0.8（entrances と同じ）
Z_CEIL = 2.70
Z_PART = 3.20                     # 間仕切りの天端（天井 2.70 より上まで立てる）
# 外周壁の天端。間仕切りの天端 + ジャンプ 1.10 m より高くしておく。ここが低いと
# 「天井裏の間仕切り天端に立って外へ出られる」判定が残る（kcd_interior/closure.py）
Z_TOP = 4.45
Z_WIN_TOP = 2.55                  # 窓の上端（天井より下。header = Z_TOP - これ）
Z_HEAD = 2.20                     # 開口の上の垂れ壁の下端
DOOR_W = 3.20                     # 入口の開口（= entrances.STANDARD の W * 2）
CORR_X = 1.60                     # 中廊下の壁の芯（廊下の有効幅 = 3.04 m）
Y_LOCK = 6.60                     # 風除室とホールの境（オートロックの内扉）
X_VEST = 3.20                     # 風除室の幅の半分
Y_CROSS = 12.60                   # 玄関ホールとラウンジ／食堂の境
# 外形の切り欠き: y 19.89 より奥は西側が x -4.68 までしかない（route.json の footprint）
X_NOTCH, Y_NOTCH = -4.68, 19.89
Y_NOTCH_WALL = Y_NOTCH - 0.15     # 切り欠きをふさぐ壁（厚み 0.30）の芯
X_NOTCH_WALL = X_NOTCH + 0.15
Y_BATH = 27.00                    # 湯上がり処と大浴場（入れない）の境
Y_KITCHEN = 24.60                 # 食堂と厨房（入れない）の境
Y_KCORNER = 28.80                 # 厨房とキッチンコーナーの境
Y_EV = 34.40                      # 中廊下とエレベーターホールの境


# --------------------------------------------------------------------------- #
#  屋内: InteriorSpec
# --------------------------------------------------------------------------- #
def make_spec(dorm):
    """屋内のローカル座標系。玄関の外向き法線の逆が +Y（= 奥）になるように組む。"""
    origin, n, _bearing = entrance_of(dorm)
    t = (-n[1], n[0])
    frame = geom.Frame(t)          # Frame.v = u を +90 度 = -n = 建物の中へ
    eu, ev = frame.uv(origin)
    uvbb = (eu + X0, ev + Y_FACE, eu + X1, ev + Y_BACK)
    return InteriorSpec(ID, DISPLAY, dorm.get("levels") or 5,
                        dorm.get("height") or 17.8, uvbb, (eu, ev), "+v", frame)


# --------------------------------------------------------------------------- #
#  屋内: 躯体
# --------------------------------------------------------------------------- #
def _wall(c, a, b, gaps=(), mat="wall_white", thick=sh.PART):
    """間仕切り + 開口の上の垂れ壁（上階の床から抜けられないように必ずふさぐ。#45）。"""
    sh.partition(c.wall, a, b, 0.0, Z_PART, mat=mat, thick=thick, gaps=gaps)
    L = math.hypot(b[0] - a[0], b[1] - a[1])
    d = ((b[0] - a[0]) / L, (b[1] - a[1]) / L)
    for s0, s1 in gaps:
        p0 = (a[0] + d[0] * s0, a[1] + d[1] * s0)
        p1 = (a[0] + d[0] * s1, a[1] + d[1] * s1)
        sh.partition(c.wall, p0, p1, Z_HEAD, Z_PART, mat=mat, thick=thick)


def _partitions(c):
    """部屋割り。開口の位置は各部屋の関数の docstring と README の平面図に合わせる。"""
    ix0, iy0, ix1, iy1 = c.spec.inner()
    # 切り欠き（西・奥）を躯体と同じ厚みの壁でふさぐ。中は床と天井だけの死に空間
    _wall(c, (ix0, Y_NOTCH_WALL), (-CORR_X, Y_NOTCH_WALL), thick=sh.WALL)
    _wall(c, (X_NOTCH_WALL, Y_NOTCH_WALL), (X_NOTCH_WALL, iy1), thick=sh.WALL)
    xw = X_NOTCH_WALL + sh.WALL * 0.5          # 切り欠きの壁の東の面（-4.38）

    # 風除室の袖壁（外の自動ドア = envelope のガラス、内の自動ドア = y 6.60）
    for sx in (-X_VEST, X_VEST):
        _wall(c, (sx, iy0), (sx, Y_LOCK))
    # 玄関ホール／ラウンジ・食堂の境（中廊下の入口だけ開ける）
    _wall(c, (ix0, Y_CROSS), (-CORR_X, Y_CROSS))
    _wall(c, (CORR_X, Y_CROSS), (ix1, Y_CROSS))
    # 中廊下の壁。西 = ラウンジ・湯上がり処、東 = 食堂 2 か所・キッチンコーナー
    _wall(c, (-CORR_X, Y_CROSS), (-CORR_X, Y_EV), gaps=[(4.40, 5.60), (10.30, 13.80)])
    _wall(c, (CORR_X, Y_CROSS), (CORR_X, Y_EV),
          gaps=[(0.80, 2.60), (7.80, 9.60), (17.60, 19.60)])
    # 湯上がり処／大浴場、食堂／厨房、厨房／キッチンコーナー
    _wall(c, (xw, Y_BATH), (-CORR_X, Y_BATH))
    sh.partition(c.wall, (CORR_X, Y_KITCHEN), (ix1, Y_KITCHEN), 0.0, Z_PART,
                 gaps=[(2.00, 5.00)])
    sh.partition(c.wall, (3.60, Y_KITCHEN), (6.60, Y_KITCHEN), 0.0, 0.95)  # 配膳口の腰壁
    sh.partition(c.wall, (3.60, Y_KITCHEN), (6.60, Y_KITCHEN), 1.85, Z_PART)
    _wall(c, (CORR_X, Y_KCORNER), (ix1, Y_KCORNER))
    # エレベーターホールの南の壁（中廊下の幅だけ開ける）
    _wall(c, (xw, Y_EV), (-CORR_X, Y_EV))
    _wall(c, (CORR_X, Y_EV), (ix1, Y_EV))
    # 中廊下 → ラウンジはガラスの引き戸（開けてある）
    sh.door(c.wall, -CORR_X, Y_CROSS + 5.00, ang=math.pi * 0.5, w=1.10, h=2.10,
            glass="glass_clear", open_=0.86)
    return xw


# --------------------------------------------------------------------------- #
#  屋内: 部屋
# --------------------------------------------------------------------------- #
def _vestibule(c, mb):
    """風除室（x ±3.2, y 3.1 – 6.6）。集合郵便受けと、オートロックの内扉・集合玄関機。"""
    _ix0, iy0, _ix1, _iy1 = c.spec.inner()
    # 内扉: 腰だけ金属、上はガラス。自動ドアは開いた状態（左右へ引き込み）
    sh.partition(c.wall, (-X_VEST, Y_LOCK), (X_VEST, Y_LOCK), 0.0, Z_PART,
                 mat="metal_white", gaps=[(2.10, 4.30)],
                 glass_top="glass_partition", glass_z=0.12)
    sh.partition(c.wall, (-1.10, Y_LOCK), (1.10, Y_LOCK), 2.30, Z_PART,
                 mat="metal_white")
    for sx in (-1, 1):
        x0, x1 = sx * 1.12, sx * 2.26
        kit.box(mb, x0, Y_LOCK + 0.09, 0.02, x1, Y_LOCK + 0.12, 2.28,
                "glass_partition")
        kit.box(mb, x0, Y_LOCK + 0.085, 0.02, x0 + sx * 0.05, Y_LOCK + 0.125,
                2.28, "metal_white")
    P.intercom_stand(mb, 1.70, Y_LOCK - 0.30, ang=math.pi)
    # 集合郵便受け（西の袖壁。宅配便は管理人室で受け取る = 公式の記載）
    F.locker_bank(mb, -2.90 - 1.30, -2.90 + 1.30, 4.75, ang=-math.pi * 0.5,
                  h=1.60, cols=8, mat="metal_gray")
    sh.ceiling_lights(c.wall, -X_VEST, iy0, X_VEST, Y_LOCK, Z_CEIL, sx=2.4, sy=3.0)
    sh.wall_sign(mb, 0.0, Y_LOCK - 0.09, 2.48, ang=math.pi, w=1.40, h=0.28)
    c.sign(0.0, Y_LOCK - 0.09, 2.48)


def _hall(c):
    """玄関ホール（y 6.6 – 12.6 と風除室の両脇）。管理人室のカウンターと寮長。"""
    s = c.spec
    ix0, iy0, ix1, _iy1 = s.inner()
    mb = c.furn("hall")

    # 管理人室（北東の隅）。受付窓は腰カウンターと垂れ壁でふさぐので入れない
    sh.partition(c.wall, (4.20, 8.00), (4.20, Y_CROSS), 0.0, Z_PART,
                 gaps=[(1.20, 3.60)])
    sh.partition(c.wall, (4.20, 8.00), (ix1, 8.00), 0.0, Z_PART)
    sh.partition(c.wall, (4.20, 9.20), (4.20, 11.60), 1.30, Z_PART)   # 受付窓の垂れ壁
    F.counter(c.wall, 4.02, 9.20, 4.38, 11.60, h=1.10)
    F.reception(mb, 5.40, 10.40, ang=math.pi * 0.5, w=3.4, d=0.9)
    F.bookshelf(mb, 7.35, 11.40, ang=math.pi * 0.5, w=1.40, h=1.85, rng=c.rng)
    F.chair(mb, 6.40, 10.40, ang=math.pi * 0.5)
    sh.wall_sign(mb, 4.10, 11.95, 2.30, ang=math.pi * 0.5, w=1.2, h=0.36)
    c.sign(4.10, 11.95, 2.30)

    _vestibule(c, mb)

    # 掲示板（西の壁）・腰かけ・観葉植物・時計
    sh.notice_board(mb, ix0 + 0.05, 5.20, 0.95, ang=-math.pi * 0.5,
                    w=2.20, h=1.20, sheets=8, rng=c.rng)
    sh.notice_board(mb, ix0 + 0.05, 9.00, 0.95, ang=-math.pi * 0.5,
                    w=2.20, h=1.20, sheets=6, rng=c.rng)
    F.bench(mb, -6.10, 7.40, ang=-math.pi * 0.5, w=1.80, back=True)
    F.bench(mb, -6.10, 10.60, ang=-math.pi * 0.5, w=1.80, back=True)
    sh.planter(mb, -2.60, 11.80, r=0.42, h=0.46, leaf_h=1.6)
    sh.planter(mb, 2.60, 11.80, r=0.42, h=0.46, leaf_h=1.6)
    sh.clock(c.wall, -3.40, Y_CROSS - 0.09, 2.35, ang=math.pi)
    sh.ceiling_lights(c.wall, ix0, Y_LOCK, ix1, Y_CROSS, Z_CEIL, sx=4.0, sy=3.0)
    sh.ceiling_lights(c.wall, ix0, iy0, -X_VEST, Y_LOCK, Z_CEIL, sx=4.0, sy=3.0)
    sh.ceiling_lights(c.wall, X_VEST, iy0, ix1, Y_LOCK, Z_CEIL, sx=4.0, sy=3.0)

    # 寮長。カウンターの手前に立つ（spawn から内扉越しにまっすぐ見える位置）
    c.npc(2.60, 9.60)          # npc_dorm_1（既存 9 棟と同じ連番の契約）
    c.poi("kanrinin", 2.60, 9.60)
    # Unity 側 DormStage.NpcEmpty = "npc_" + DormRoute.DialogueId = "npc_dorm_head"。
    # 連番の npc_dorm_1 とは別名なので、同じ場所に別名の Empty も出しておく。
    c._put("npc_%s_head" % c.spec.id, 2.60, 9.60)
    c.note("寮長 = npc_dorm_1 / poi_dorm_kanrinin（玄関ホール、管理人カウンターの手前）")
    return mb


def _lounge(c):
    """ラウンジ（西、x -7.7 – -1.7, y 12.7 – 19.6）。オレンジの壁紙・黄色のソファ・寮生 1 人。"""
    s = c.spec
    ix0, _iy0, _ix1, _iy1 = s.inner()
    mb = c.furn("lounge")
    x_in = -CORR_X - sh.PART * 0.5
    y_s = Y_CROSS + sh.PART * 0.5
    y_n = Y_NOTCH_WALL - sh.WALL * 0.5

    # 壁紙（南と北の壁。西の外壁は窓なので貼らない）とカーペット
    kit.box(mb, ix0, y_s, 0.0, x_in, y_s + 0.02, Z_CEIL, "wallpaper_orange")
    kit.box(mb, ix0, y_n - 0.02, 0.0, x_in, y_n, Z_CEIL, "wallpaper_orange")
    kit.box(mb, -6.90, 13.50, 0.0, -2.70, 18.90, 0.012, "floor_carpet_gold")

    F.sofa(mb, -5.00, 14.30, ang=0.0, w=2.10, mat="fabric_yellow",
           cushion="fabric_yellow")
    F.sofa(mb, -5.00, 18.10, ang=math.pi, w=2.10, mat="fabric_yellow",
           cushion="fabric_yellow")
    F.table(mb, -5.00, 16.20, ang=0.0, w=1.20, d=0.60, h=0.42,
            top="counter_wood", leg="metal_dark")
    F.lounge_chair(mb, -7.00, 16.20, ang=-math.pi * 0.5, mat="fabric_yellow")
    F.bookshelf(mb, ix0 + 0.30, 13.60, ang=-math.pi * 0.5, w=1.40, h=1.85,
                rng=c.rng)
    # テレビ（廊下側の壁に掛ける）
    kit.box(mb, x_in - 0.04, 14.00, 1.05, x_in, 15.60, 1.98, "plastic_black")
    kit.box(mb, x_in - 0.05, 14.08, 1.12, x_in - 0.04, 15.52, 1.91, "screen_blue")
    sh.planter(mb, -7.20, 18.90, r=0.40, h=0.44, leaf_h=1.5)
    sh.planter(mb, -2.30, 19.00, r=0.36, h=0.42, leaf_h=1.3)
    sh.planter(mb, -2.30, 13.30, r=0.36, h=0.42, leaf_h=1.3)
    sh.ceiling_lights(c.wall, ix0, Y_CROSS, x_in, y_n, Z_CEIL, sx=3.0, sy=3.4)
    sh.wall_sign(mb, -CORR_X + 0.09, 17.60, 2.45, ang=-math.pi * 0.5, w=1.0, h=0.28)
    c.sign(-CORR_X + 0.09, 17.60, 2.45)

    c.npc(-4.00, 16.40)
    c.poi("lounge", -3.40, 17.60)
    return mb


def _dining(c):
    """食堂 兼 カフェラウンジ（東、x 1.7 – 7.7, y 12.7 – 24.5）。朝夕 2 食つき（月–土）。

    写真: 明るい木の床、こげ茶のテーブル、白い座面の椅子、こげ茶の柱、横型ブラインド、
    配膳口、ドリンクの冷蔵ケース、電子レンジの並ぶカウンター、コピー機、窓際のカウンター席。
    """
    _ix0, _iy0, ix1, _iy1 = c.spec.inner()
    mb = c.furn("dining")
    x_in = CORR_X + sh.PART * 0.5
    y_s = Y_CROSS + sh.PART * 0.5
    y_n = Y_KITCHEN - sh.PART * 0.5

    kit.plate(c.floor, x_in, y_s, ix1, y_n, 0.006, "floor_wood_light")
    for j in range(5):
        y = 14.00 + j * 2.05
        for x in (3.05, 5.15):
            F.table(mb, x, y, ang=0.0, w=1.30, d=0.80, h=0.72, top="desk_dark",
                    leg="metal_dark")
            for dx in (-0.36, 0.36):
                P.cafe_chair(mb, x + dx, y - 0.62, ang=0.0)
                P.cafe_chair(mb, x + dx, y + 0.62, ang=math.pi)
    for y in (17.03, 21.13):
        sh.column(mb, 4.10, y, 0.0, Z_CEIL, size=0.36, mat=P.WOOD_DARK)

    # 窓際のカウンター席（東の窓）とブラインド
    F.counter(mb, ix1 - 0.50, 13.20, ix1, 19.60, h=1.00, body=P.WOOD_DARK,
              top="counter_dark")
    for k in range(6):
        P.stool(mb, ix1 - 0.85, 13.70 + k * 1.06)
    P.blinds(mb, ix1, y_s, y_n, 1.95, 2.55, side=-1)

    # 配膳口（厨房は入れない）と、レンジの並ぶカウンター・冷蔵ケース・コピー機
    P.serving_counter(mb, 3.60, 6.60, y_n - 0.52, y_n)
    kit.box(mb, 3.60, Y_KITCHEN + 0.10, 0.0, 6.60, Y_KITCHEN + 0.80, 0.90,
            "stainless")                              # 厨房側の作業台（配膳口から見える）
    F.counter(mb, x_in, y_n - 0.60, 3.40, y_n, h=0.90, body=P.WOOD_DARK,
              top="counter_dark")
    for x in (2.15, 2.85):
        P.microwave(mb, x, y_n - 0.30, 0.90, ang=math.pi)
    P.pot(mb, 3.20, y_n - 0.25, 0.90)
    F.fridge_case(mb, ix1 - 0.44, 21.90, ang=math.pi * 0.5, length=2.80, h=1.95,
                  rng=c.rng)
    P.copier(mb, ix1 - 0.40, y_n - 0.40, ang=math.pi * 0.5)

    sh.ceiling_lights(c.wall, x_in, Y_CROSS, ix1, y_n, Z_CEIL, sx=2.1, sy=2.05,
                      w=0.60, l=0.60)
    sh.wall_sign(mb, CORR_X - 0.09, 14.30, 2.45, ang=math.pi * 0.5, w=1.2, h=0.28)
    c.sign(CORR_X - 0.09, 14.30, 2.45)
    c.poi("shokudo", 4.10, 19.10)
    return mb


def _bath(c, xw):
    """大浴場の入口と湯上がり処（西、x -4.4 – -1.7, y 19.9 – 27.0）。

    浴室そのもの（y 27 – 34.4）は入れない。男湯・女湯の暖簾の奥はすりガラスの扉で閉める。
    """
    mb = c.furn("bath")
    x_in = -CORR_X - sh.PART * 0.5
    y_s = Y_NOTCH_WALL + sh.WALL * 0.5
    y_n = Y_BATH - sh.PART * 0.5

    kit.plate(c.floor, xw, y_s, x_in, y_n, 0.006, "floor_wood")
    for x, mat in ((-3.72, "curtain_blue"), (-2.32, "cushion_red")):
        sh.door(mb, x, y_n - 0.05, ang=math.pi, w=0.85, h=2.05, leaf="metal_white",
                glass="door_frosted", open_=0.0)
        P.noren(mb, x, y_n - 0.16, math.pi, mat)
        sh.wall_sign(mb, x, y_n - 0.01, 2.40, ang=math.pi, w=0.70, h=0.24,
                     mat=mat)
    for y in (20.80, 22.10):
        P.massage_chair(mb, xw + 0.62, y, ang=-math.pi * 0.5)
    F.bench(mb, xw + 0.30, 24.60, ang=-math.pi * 0.5, w=1.60, mat="counter_wood")
    sh.ceiling_lights(c.wall, xw, y_s, x_in, y_n, Z_CEIL, sx=2.4, sy=2.4)
    sh.wall_sign(mb, -CORR_X + 0.09, 24.65, 2.45, ang=-math.pi * 0.5, w=1.2, h=0.28)
    c.sign(-CORR_X + 0.09, 24.65, 2.45)
    c.poi("daiyokujo", -3.00, 25.40)
    return mb


def _kitchen_corner(c):
    """キッチンコーナー（東、x 1.7 – 7.7, y 28.9 – 34.3）。2 列が向かい合う（写真どおり）。"""
    _ix0, _iy0, ix1, _iy1 = c.spec.inner()
    mb = c.furn("kitchen")
    x_in = CORR_X + sh.PART * 0.5
    y_s = Y_KCORNER + sh.PART * 0.5
    y_n = Y_EV - sh.PART * 0.5

    kit.plate(c.floor, x_in, y_s, ix1, y_n, 0.006, "floor_wood_light")
    P.kitchen_run(mb, 2.60, ix1, y_s, +1, sinks=(3.70,), hobs=(5.00, 6.60))
    P.kitchen_run(mb, 2.60, ix1, y_n, -1, sinks=(6.40,), hobs=(3.60, 5.20))
    P.microwave(mb, 2.90, y_s + 0.33, 0.90, ang=0.0)
    P.pot(mb, 4.45, y_s + 0.30, 0.90)
    P.microwave(mb, ix1 - 0.40, y_n - 0.33, 0.90, ang=math.pi)
    F.table(mb, 5.10, 31.60, ang=0.0, w=1.40, d=0.75, h=0.72, top="desk_dark",
            leg="metal_dark")
    for dx in (-0.36, 0.36):
        P.cafe_chair(mb, 5.10 + dx, 31.60 - 0.60, ang=0.0)
        P.cafe_chair(mb, 5.10 + dx, 31.60 + 0.60, ang=math.pi)
    # 厨房の勝手口（入れない）
    sh.door(mb, x_in - 0.21, 26.70, ang=math.pi * 0.5, w=0.90, h=2.05,
            leaf="metal_gray", open_=0.0)
    sh.ceiling_lights(c.wall, x_in, y_s, ix1, y_n, Z_CEIL, sx=2.6, sy=2.6)
    sh.wall_sign(mb, CORR_X - 0.09, 31.20, 2.45, ang=math.pi * 0.5, w=1.2, h=0.28)
    c.sign(CORR_X - 0.09, 31.20, 2.45)
    c.poi("kitchen", 4.20, 31.60)
    return mb


def _corridor(c):
    """中廊下（x ±1.6, y 12.6 – 34.4）。居室は 1F に無いので扉は並べない。"""
    mb = c.furn("corridor")
    xw = -CORR_X + sh.PART * 0.5      # 西側の壁の「廊下側」の面（-1.52）
    xe = CORR_X - sh.PART * 0.5       # 東側の壁の「廊下側」の面（+1.52）
    for y in (16.00, 23.00, 30.00):
        sh.exit_sign(c.wall, 0.0, y, Z_CEIL - 0.06, ang=math.pi)
    sh.fire_extinguisher(mb, xe - 0.30, 18.20, ang=math.pi * 0.5)
    sh.fire_extinguisher(mb, xw + 0.30, 30.00, ang=-math.pi * 0.5)
    sh.light_strip(c.wall, -0.40, Y_CROSS, 0.40, Y_EV, Z_CEIL)
    return mb


def _elevator_hall(c, xw):
    """エレベーターホールと階段（奥、x -4.4 – 7.7, y 34.5 – 40.5）。上の階へは行けない。

    2F 以上は IC キーのフロア制御（公式の記載）。ゲートのバーと IC 読み取り部で止める。
    """
    _ix0, _iy0, ix1, iy1 = c.spec.inner()
    mb = c.furn("elevator")
    y_s = Y_EV + sh.PART * 0.5

    sh.elevator_bank(mb, -1.30, iy1 - 0.06, count=2, ang=0.0, pitch=2.60, w=1.05,
                     h=2.25)
    P.ic_reader(mb, 2.90, iy1, 1.05, ang=math.pi)
    for sx in (-2.60, 2.60):
        kit.cyl(mb, sx, iy1 - 2.20, 0.0, 0.92, 0.055, "stainless", seg=8)
    kit.box(mb, -2.64, iy1 - 2.24, 0.84, 2.64, iy1 - 2.16, 0.90, "stainless")
    kit.box(mb, 2.52, iy1 - 2.30, 0.92, 2.68, iy1 - 2.10, 1.10, "plastic_black")
    sh.hanging_sign(mb, 0.0, iy1 - 2.60, Z_CEIL, ang=math.pi, w=1.80, h=0.34,
                    drop=0.22)
    c.sign(0.0, iy1 - 2.60, Z_CEIL - 0.39)
    # 階段（鉄扉は閉めてある）
    sh.door(mb, 5.60, iy1 - 0.05, ang=math.pi, w=0.95, h=2.10, leaf="metal_gray",
            open_=0.0)
    sh.wall_sign(mb, 5.60, iy1 - 0.01, 2.42, ang=math.pi, w=0.80, h=0.26)
    c.sign(5.60, iy1 - 0.01, 2.42)
    # 西の腰かけと掲示板
    F.bench(mb, xw + 0.30, 37.40, ang=-math.pi * 0.5, w=1.80, back=True)
    sh.notice_board(mb, -3.00, y_s, 0.95, ang=0.0, w=2.00, h=1.10, sheets=5,
                    rng=c.rng)
    sh.ceiling_lights(c.wall, xw, y_s, ix1, iy1, Z_CEIL, sx=3.0, sy=3.0)
    c.poi("corridor_end", 0.0, iy1 - 3.40)
    return mb


def build(c):
    """kcd_interior のプランと同じ契約（registry.get(bid).build(c) と同形）。"""
    s = c.spec
    _ix0, _iy0, _ix1, iy1 = s.inner()

    # 躯体（床 + 外周壁 + 入口ガラススクリーン + 天井）
    common.envelope(c, Z_CEIL, floor_mat="floor_tile_grey", door_w=DOOR_W,
                    glass="glass_clear", sill=0.95,
                    header=Z_TOP - Z_WIN_TOP, seg=3.2,
                    wall="wall_white", z_top=Z_TOP, ceil=True,
                    ceil_mat="ceiling_white", grid=2.4, floor_thick=0.30)
    common.entry_kit(c, Z_CEIL, door_w=DOOR_W, spawn_depth=1.50, bin_x=5.60)
    xw = _partitions(c)

    # c.furn の順番は契約: entry → hall → lounge（furn_dorm_02_hall / 03_lounge）
    _hall(c)
    _lounge(c)
    _dining(c)
    _bath(c, xw)
    _kitchen_corner(c)
    _corridor(c)
    _elevator_hall(c, xw)

    # プレビュー用の明かり
    for x, y in ((0.0, 4.8), (-4.0, 9.5), (2.0, 9.5), (-4.6, 14.6), (-4.6, 18.0),
                 (3.4, 15.0), (5.6, 19.0), (3.4, 23.0), (4.6, 31.6), (-2.0, 37.4),
                 (3.4, 37.4)):
        c.light(x, y, Z_CEIL - 0.15, 190.0, 1.6)
    c.light(-3.0, 24.2, Z_CEIL - 0.15, 120.0, 1.2)       # 大浴場前は狭いので 1 灯
    for y in (16.0, 24.0, 32.0):
        c.light(0.0, y, Z_CEIL - 0.15, 90.0, 1.2)

    # プレビューのカメラ（"" と "hall" の名前は契約）
    c.cam("", (0.0, 3.60, 1.85), (0.60, 12.00, 1.20), 17.0)
    c.cam("hall", (-6.80, 7.00, 2.30), (4.20, 10.60, 1.20), 19.0)
    c.cam("lounge", (-2.10, 13.10, 2.20), (-6.40, 18.60, 0.90), 18.0)
    c.cam("dining", (2.05, 13.05, 2.25), (6.20, 23.80, 0.90), 18.0)
    c.cam("bath", (-1.95, 20.20, 2.10), (-3.10, 26.90, 1.30), 17.0)
    c.cam("kitchen", (2.05, 30.20, 2.20), (7.40, 32.40, 0.90), 17.0)
    c.cam("elevator", (-3.90, 35.00, 2.20), (3.60, 40.40, 1.20), 18.0)
    c.cam("corridor", (0.0, 13.40, 1.95), (0.0, iy1 - 0.20, 1.55), 24.0)

    c.note("運営は %s。%s" % (OPERATOR, NOT_UNIVERSITY))
    c.note("ミニマップはキャンパス外なので黒のまま（#41 の仕様）")
    c.note("1F の間取りは推定（公開図面なし）。設備の種類は公式の記載、色と什器は館内写真から")
    c.note("1F に居室は無い（居室は 2F 女子 23 室、3–5F 男子 77 室）ので扉も並べない")
    c.note("入れない所: 管理人室・大浴場（すりガラスの扉）・厨房・エレベーター・階段・"
           "西奥の切り欠き（外形の外）")
    return c


# 既存の kcd_interior プランと同じ呼び方ができるようにしておく
build_interior = build
