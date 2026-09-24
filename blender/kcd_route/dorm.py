"""葛飾コミュニティハウス（学生寮）— 外観と屋内。

裏エンド「寮でぐーたら」(#41) の舞台。キャンパスの外、水戸街道ぞいの実在の寮で、
運営は **共立メンテナンス（学生会館ドーミー）**。東京理科大学の直営ではないので、
見た目にも文言にも大学のブランド（tus_green など）は使わない。

座標
----
外観は **ワールド XY**（x = 東[m], y = 北[m]、build_campus / build_route と同じ）。
footprint・高さ・階数・玄関はすべて ``data/osm/route.json`` の ``dormitory`` から読む。
勝手な数字は 1 つも持たない。

屋内は **建物ローカル**（原点 = entrance_dorm の真下の床、+Y = 入口から奥、+X = 右、+Z = 上）。
kcd_interior の Ctx / InteriorSpec 契約にそのまま乗るので、Unity 側は既存 9 棟と同じ
``spawn_<id>`` / ``exit_<id>`` / ``npc_<id>_<n>`` / ``poi_<id>_<name>`` を読めばよい。

使い方
------
    from kcd_route import dorm

    # route.fbx に外観を混ぜる（build_route.py から）
    shell, trim = dorm.build_exterior(route["dormitory"])
    shell.to_object(); trim.to_object()
    doors = [dorm.door_frame(route["dormitory"])]   # door_/entrance_ Empty 用

    # 屋内 FBX（build_dorm.py から）
    sp = dorm.make_spec(route["dormitory"])
    c = Ctx(sp); dorm.build(c)
"""

import math
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))

from kcd_lib import entrances, facade, geom          # noqa: E402
from kcd_lib.mesh import MeshBuilder                 # noqa: E402
from kcd_interior import common, kit                 # noqa: E402
from kcd_interior import furniture as F              # noqa: E402
from kcd_interior import shell as sh                 # noqa: E402
from kcd_interior.spec import InteriorSpec           # noqa: E402

# --------------------------------------------------------------------------- #
#  契約（Unity / build_route と共有する名前）
# --------------------------------------------------------------------------- #
ID = "dorm"
DISPLAY = "葛飾コミュニティハウス"
OPERATOR = "共立メンテナンス（学生会館ドーミー）"
NOT_UNIVERSITY = "大学直営ではない（ゲーム内で『大学の寮』と断定しない）"

SHELL_OBJ = "bld_dorm"        # footprint ぴったりの躯体（検証はこれを測る）
TRIM_OBJ = "bld_dorm_trim"    # バルコニー・塔屋・玄関・銘板（footprint から出る部分）

# 外観の寸法
CORE_IN = 0.45      # 閉じた躯体コアを footprint からどれだけ内側に置くか
GF_MAX = 4.00       # 1 階の階高の上限
BAND_OUT = 0.12     # 各階の床見切り（水平ライン）の出
BAND_H = 0.14       # その厚み
BALC_D = 1.40       # バルコニーの出
BALC_RAIL = 1.06    # 手すりの高さ（床から）
PARAPET_H = 0.95
PENT_H = 2.80       # 屋上の塔屋（階段室）

# 玄関の足元の石張り（エプロン）の奥行き。entrances.STANDARD は 7.0 m だが、この玄関は
# 目の前が前面道路（osm 58360717, tertiary, 幅 9.0 m）で、扉から車道の縁まで 4.00 m
# （エプロンの外側の角 s=+3.4 では 3.07 m）しかない。7.0 m のままだと車道へ最大 3.72 m
# 乗り上げ、天端 0.12 m の段差が車道を横切る。実測した「車道に触れない上限」は 3.069 m。
# キャンパス 9 棟は entrances.STANDARD を共有しているので、そちらは触らず寮だけ上書きする。
DOOR_APRON_D = 2.80

# 屋内の寸法
X0, X1 = -8.00, 8.00
Y_FACE, Y_BACK = 2.80, 40.80      # 2.80 = 玄関の風除室 D 2.0 + 0.8（entrances と同じ）
Z_CEIL = 2.70
Z_PART = 3.20                     # 間仕切りの天端（天井 2.70 より上まで立てる）
# 外周壁の天端。間仕切りの天端 + ジャンプ 1.10 m より高くしておく。ここが低いと
# 「天井裏の間仕切り天端に立って外へ出られる」判定が残る（kcd_interior/closure.py）
Z_TOP = 4.45
Z_WIN_TOP = 2.55                  # 窓の上端（天井より下。header = Z_TOP - これ）
DOOR_W = 3.20                     # 入口の開口（= entrances.STANDARD の W * 2）
CORR_X = 1.60                     # 中廊下の壁の芯（廊下の有効幅 = 3.04 m）
Y_CROSS = 12.60                   # 玄関ホールとラウンジ／食堂の境
Y_NORTH = 23.00                   # ラウンジ／食堂と居室エリアの境


# --------------------------------------------------------------------------- #
#  route.json から読む
# --------------------------------------------------------------------------- #
def loop_of(dorm):
    """footprint を反時計回りの重複なしポリゴンにして返す。"""
    return geom.ensure_ccw(geom.dedup([tuple(p) for p in dorm["footprint"]]))


def entrance_of(dorm):
    """玄関の (原点 (x, y), 外向き単位ベクトル n, 方位角[deg])。

    数字は route.json のものをそのまま使う。実測の辺中点との差 5 mm は壁厚 0.30 m と
    entrances.BURY 0.25 m に埋もれるので、Unity 側の定数と桁までそろう方を取る。
    """
    ent = dorm["entrance"]
    bearing = float(ent["facing_bearing"])
    rad = math.radians(bearing)
    n = (math.sin(rad), math.cos(rad))       # 方位角は北から時計回り、y が北
    return (tuple(ent["point"]), n, bearing)


def floor_heights(dorm):
    """(1 階の階高, 2 階以上の階高, 階数)。合計は必ず dorm['height'] になる。"""
    h = float(dorm["height"])
    lv = max(1, int(dorm.get("levels") or 1))
    if lv == 1:
        return (h, h, 1)
    gf = min(GF_MAX, h / lv + 0.5)
    return (gf, (h - gf) / (lv - 1), lv)


def door_frame(dorm):
    """entrances.build_one / plan と同じ形の扉の辞書。"""
    origin, n, bearing = entrance_of(dorm)
    dr = dict(entrances.STANDARD)
    dr.update(id=ID, origin=origin, n=n, t=(-n[1], n[0]),
              yaw=math.atan2(n[1], n[0]), sign_side=-1, sign_z=3.4,
              bearing=bearing, APRON_D=DOOR_APRON_D)
    return dr


# --------------------------------------------------------------------------- #
#  外観
# --------------------------------------------------------------------------- #
def _edge_frame(loop, i):
    """辺 i の (始点, 単位方向, 外向き法線, 長さ)。facade.py と同じ向きの取り方。"""
    p = loop[i]
    q = loop[(i + 1) % len(loop)]
    d = geom.sub(q, p)
    L = geom.length(d)
    e = geom.mul(d, 1.0 / L)
    return p, e, (e[1], -e[0]), L


def _quad(p, e, n, t0, t1, d0, d1):
    """辺ローカル (t = 辺に沿う距離, d = 外向きの出) の矩形を XY ポリゴンにする。"""
    def P(t, d):
        return (p[0] + e[0] * t + n[0] * d, p[1] + e[1] * t + n[1] * d)
    return [P(t0, d0), P(t1, d0), P(t1, d1), P(t0, d1)]


def _balconies(mb, loop, ei, z_list):
    """辺 ei にバルコニー（閉じた床スラブ + 手すり + 袖壁 + 隔て板）を並べる。"""
    p, e, n, L = _edge_frame(loop, ei)
    t0, t1 = 2.0, L - 2.0
    if t1 - t0 < 4.0:
        return 0
    ndiv = max(2, int(round((t1 - t0) / 6.5)))
    made = 0
    for z in z_list:
        # 床スラブ
        mb.add_prism(_quad(p, e, n, t0, t1, 0.0, BALC_D), z, z + 0.14,
                     "concrete_light", "concrete_light", "concrete_light")
        # 手すり壁（外側）
        mb.add_prism(_quad(p, e, n, t0, t1, BALC_D - 0.10, BALC_D),
                     z + 0.14, z + BALC_RAIL,
                     "concrete_light", "concrete_light", "concrete_light")
        # 袖壁（両端）
        for ts in (t0, t1 - 0.12):
            mb.add_prism(_quad(p, e, n, ts, ts + 0.12, 0.0, BALC_D),
                         z + 0.14, z + BALC_RAIL,
                         "concrete_light", "concrete_light", "concrete_light")
        # 隔て板（住戸の境。ここが「集合住宅」に見える決め手）
        for k in range(1, ndiv):
            ts = t0 + (t1 - t0) * k / ndiv
            mb.add_prism(_quad(p, e, n, ts - 0.05, ts + 0.05, 0.0, BALC_D - 0.10),
                         z + 0.14, z + 1.90,
                         "metal_white", "metal_white", "metal_white")
        made += 1
    return made


def _nameplate(mb, dr):
    """銘板「葛飾コミュニティハウス」。壁付けの板 + 道路際の自立サイン。

    大学名も大学色（tus_green）も使わない。運営は共立メンテナンスであって大学ではない。
    """
    def box(s0, s1, d0, d1, z0, z1, mat, top=None, bottom=None):
        poly = [entrances.local(dr, s0, d0), entrances.local(dr, s1, d0),
                entrances.local(dr, s1, d1), entrances.local(dr, s0, d1)]
        mb.add_prism(poly, z0, z1, mat, top, bottom)

    # 壁付け（玄関の右手、庇の下）
    box(3.6, 6.4, 0.02, 0.10, 2.05, 2.65, "wall_accent_navy",
        "wall_accent_navy", "wall_accent_navy")
    box(3.7, 6.3, 0.10, 0.14, 2.12, 2.58, "sign_plate", "sign_plate", "sign_plate")

    # 自立サイン（歩道側）。低い台座 + 白い板 + 濃紺の帯
    s = -(dr["WO"] + entrances.SIGN_S)
    d = dr["D"] + entrances.SIGN_D
    box(s - 1.10, s + 1.10, d - 0.30, d + 0.30, 0.0, 0.42,
        "stone_dark", "stone_light", None)
    for sg in (-1.0, 1.0):
        box(s + sg * 0.95, s + sg * 1.05, d - 0.10, d + 0.10, 0.42, 2.30,
            "concrete_light", "concrete_light", "concrete_light")
    box(s - 1.05, s + 1.05, d - 0.07, d + 0.07, 1.00, 2.30,
        "sign_plate", "sign_plate", "sign_plate")
    box(s - 1.05, s + 1.05, d - 0.09, d + 0.09, 1.00, 1.32,
        "wall_accent_navy", "wall_accent_navy", "wall_accent_navy")


def build_exterior(dorm, shell=None, trim=None):
    """外観を組む。戻り値は (躯体の MeshBuilder, 付属物の MeshBuilder)。

    * 躯体（shell）は footprint の外へ 1 mm も出ない。検証はこれを測る。
    * 窓・壁・手すりは全部 **厚みのある閉じた立体**（add_prism）で作る。板 1 枚の面は
      PhysX が片面でしか受け止めず、室内から外へ素通りになる（#45）。
    """
    shell = shell or MeshBuilder(SHELL_OBJ)
    trim = trim or MeshBuilder(TRIM_OBJ)

    loop = loop_of(dorm)
    h = float(dorm["height"])
    gf, up, lv = floor_heights(dorm)

    # --- 閉じた躯体コア（上下の蓋つき。ここがあるので中は完全に塞がっている） ---
    core = geom.offset_polygon(loop, -CORE_IN)
    shell.add_prism(core, -0.60, h - 0.02, "concrete_light",
                    "roof_grey", "concrete_dark")

    # --- 1 階（腰高のガラス。ロビーとして明るく） ---
    facade.add_facade(shell, loop, 0.0, gf, 0, 1,
                      wall="concrete_grey", glass="glass_clear",
                      seg=3.2, sill=1.00, header=0.60, inset=0.22, mullion=0.30)
    # --- 2 階以上（住戸窓。腰 1.05 / 垂れ 0.95 の強い横しまで「5 階建て」に見せる） ---
    facade.add_facade(shell, loop, gf, up, 0, lv - 1,
                      wall="concrete_light", glass="glass_dark",
                      seg=3.2, sill=1.05, header=0.95, inset=0.30, mullion=0.35)

    # --- 屋上（陸屋根 + パラペット） ---
    shell.add_ngon_flat(loop, h, "roof_grey")
    facade.add_parapet(shell, loop, h, PARAPET_H, 0.30, "concrete_light")

    # --- 各階の床見切り（水平ライン）。閉じたスラブなので当たり判定も素直 ---
    band = geom.offset_polygon(loop, BAND_OUT)
    z_floors = [gf + up * k for k in range(lv - 1)]
    for z in z_floors:
        trim.add_prism(band, z - BAND_H, z, "concrete_grey",
                       "concrete_grey", "concrete_grey")

    # --- バルコニー（いちばん長い辺 = 東北東向きの 42 m 面） ---
    ei = geom.longest_edge(loop)
    n_balc = _balconies(trim, loop, ei, z_floors)

    # --- 屋上の塔屋（階段室）と高置水槽 ---
    cx, cy = geom.centroid(loop)
    pent = [(cx - 3.2, cy - 2.4), (cx + 3.2, cy - 2.4),
            (cx + 3.2, cy + 2.4), (cx - 3.2, cy + 2.4)]
    trim.add_prism(pent, h, h + PENT_H, "concrete_light", "roof_grey", None)
    tx, ty = cx + 6.0, cy - 6.0
    for sx in (-1.3, 1.3):
        for sy in (-1.0, 1.0):
            trim.add_prism([(tx + sx - 0.12, ty + sy - 0.12), (tx + sx + 0.12, ty + sy - 0.12),
                            (tx + sx + 0.12, ty + sy + 0.12), (tx + sx - 0.12, ty + sy + 0.12)],
                           h, h + 1.60, "metal_grey", None, None)
    trim.add_cylinder(tx, ty, h + 1.60, h + 3.10, 1.70, "metal_white",
                      seg=8, cap_top=True, cap_bottom=True)

    # --- 玄関（キャンパスの他の棟と同じ「入れる扉」。紺の風除室 + ガラス両開き + 庇） ---
    dr = door_frame(dorm)
    entrances.build_one(trim, dr)
    _nameplate(trim, dr)

    return shell, trim, dict(levels=lv, gf=gf, up=up, height=h,
                             balcony_edge=ei, balcony_floors=n_balc, door=dr)


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
#  屋内: プラン
# --------------------------------------------------------------------------- #
def _hall(c):
    """玄関ホール（y 3.1 – 12.6）。管理人室のカウンターと寮長。"""
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

    # メールボックス（玄関の西どなり）と掲示板
    F.locker_bank(mb, -6.80, -2.60, 3.34, ang=0.0, h=1.70, mat="metal_gray")
    sh.notice_board(mb, ix0 + 0.05, 6.20, 0.95, ang=-math.pi * 0.5,
                    w=2.20, h=1.20, sheets=8, rng=c.rng)
    sh.notice_board(mb, ix0 + 0.05, 9.00, 0.95, ang=-math.pi * 0.5,
                    w=2.20, h=1.20, sheets=6, rng=c.rng)

    # 腰かけ・観葉植物・自販機・時計
    F.bench(mb, -6.10, 7.20, ang=-math.pi * 0.5, w=1.80, back=True)
    F.bench(mb, -6.10, 9.40, ang=-math.pi * 0.5, w=1.80, back=True)
    sh.planter(mb, -2.60, 11.80, r=0.42, h=0.46, leaf_h=1.6)
    sh.planter(mb, 2.60, 11.80, r=0.42, h=0.46, leaf_h=1.6)
    sh.vending(mb, 7.10, 4.60, ang=math.pi * 0.5, mat="fm_blue")
    sh.vending(mb, 7.10, 5.90, ang=math.pi * 0.5, mat="fm_green")
    sh.clock(c.wall, -3.40, Y_CROSS - 0.02, 2.35, ang=math.pi)
    sh.ceiling_lights(c.wall, ix0, iy0, ix1, Y_CROSS, Z_CEIL, sx=4.0, sy=4.2)

    # 寮長。カウンターの手前に立つ（spawn からまっすぐ見える位置）
    c.npc(2.60, 9.60)          # npc_dorm_1（既存 9 棟と同じ連番の契約）
    c.poi("kanrinin", 2.60, 9.60)
    # Unity 側 DormStage.NpcEmpty = "npc_" + DormRoute.DialogueId = "npc_dorm_head"。
    # 連番の npc_dorm_1 とは別名なので、同じ場所に別名の Empty も出しておく。
    c._put("npc_%s_head" % c.spec.id, 2.60, 9.60)
    c.note("寮長 = npc_dorm_1 / poi_dorm_kanrinin（玄関ホール、管理人カウンターの手前）")
    return mb


def _lounge(c):
    """ラウンジ（西、y 12.6 – 23.0）。寮生が 1 人いる。"""
    s = c.spec
    ix0, _iy0, _ix1, _iy1 = s.inner()
    mb = c.furn("lounge")
    x_in = -CORR_X - sh.PART * 0.5

    F.sofa(mb, -5.40, 15.60, ang=0.0, w=2.10)
    F.sofa(mb, -5.40, 19.40, ang=math.pi, w=2.10)
    F.round_table(mb, -5.40, 17.50, r=0.62, h=0.44)
    F.lounge_chair(mb, -3.10, 17.50, ang=math.pi * 0.5)
    F.lounge_chair(mb, -7.00, 17.50, ang=-math.pi * 0.5)
    F.bookshelf(mb, ix0 + 0.30, 21.40, ang=-math.pi * 0.5, w=1.60, h=1.85,
                rng=c.rng)
    F.bookshelf(mb, ix0 + 0.30, 13.60, ang=-math.pi * 0.5, w=1.60, h=1.85,
                rng=c.rng)
    # テレビ（廊下側の壁に掛ける）
    kit.box(mb, x_in - 0.10, 20.10, 1.05, x_in - 0.06, 21.70, 1.98, "plastic_black")
    kit.box(mb, x_in - 0.14, 20.20, 1.12, x_in - 0.10, 21.60, 1.91, "screen_blue")
    sh.planter(mb, -7.20, 22.20, r=0.40, h=0.44, leaf_h=1.5)
    sh.ceiling_lights(c.wall, ix0, Y_CROSS, x_in, Y_NORTH, Z_CEIL, sx=3.4, sy=3.6)
    sh.wall_sign(mb, x_in - 0.02, 14.10, 2.35, ang=math.pi * 0.5, w=1.2, h=0.34)
    c.sign(x_in - 0.02, 14.10, 2.35)

    c.npc(-4.00, 16.40)
    c.poi("lounge", -5.40, 17.50)
    return mb


def _dining(c):
    """食堂（東、y 12.6 – 23.0）。ドーミーなので朝夕 2 食つき。"""
    s = c.spec
    _ix0, _iy0, ix1, _iy1 = s.inner()
    mb = c.furn("dining")
    x_in = CORR_X + sh.PART * 0.5

    for j in range(3):
        y = 14.40 + j * 2.60
        for k in range(2):
            x = 3.40 + k * 2.80
            F.table(mb, x, y, ang=0.0, w=1.50, d=0.85, h=0.72)
            F.chair_canteen(mb, kit.T(x - 0.45, y - 0.72, 0.0, 0.0))
            F.chair_canteen(mb, kit.T(x + 0.45, y - 0.72, 0.0, 0.0))
            F.chair_canteen(mb, kit.T(x - 0.45, y + 0.72, 0.0, math.pi))
            F.chair_canteen(mb, kit.T(x + 0.45, y + 0.72, 0.0, math.pi))

    # 配膳カウンターと厨房の仕切り（北端。厨房側へは入れない）
    sh.partition(c.wall, (x_in, 21.60), (ix1, 21.60), 0.0, Z_PART)
    F.serving_line(mb, x_in + 0.40, ix1 - 0.40, 20.85, depth=1.10, h=0.95,
                   trays=True)
    F.tray_rack(mb, x_in + 0.70, 19.80, ang=math.pi)
    sh.ceiling_lights(c.wall, x_in, Y_CROSS, ix1, Y_NORTH, Z_CEIL, sx=3.4, sy=3.6)
    sh.wall_sign(mb, x_in + 0.02, 14.10, 2.35, ang=-math.pi * 0.5, w=1.2, h=0.34)
    c.sign(x_in + 0.02, 14.10, 2.35)
    c.poi("shokudo", 5.00, 17.00)
    return mb


def _corridor(c):
    """中廊下（y 12.6 – 40.5）。奥は行き止まりで、居室には入れない。"""
    s = c.spec
    _ix0, _iy0, _ix1, iy1 = s.inner()
    mb = c.furn("corridor")
    xw = -CORR_X + sh.PART * 0.5      # 西側の壁の「廊下側」の面（-1.52）
    xe = CORR_X - sh.PART * 0.5       # 東側の壁の「廊下側」の面（+1.52）

    # 居室の扉（見た目だけ。壁に開口は開けないので中へは入れない）
    for j in range(5):
        y = 24.60 + j * 2.60
        sh.door(mb, xw + 0.05, y, ang=math.pi * 0.5, w=0.90, h=2.05,
                leaf="desk_wood", open_=0.0)
        sh.door(mb, xe - 0.05, y, ang=-math.pi * 0.5, w=0.90, h=2.05,
                leaf="desk_wood", open_=0.0)

    # 誘導灯・消火器・ライン照明
    for j in range(4):
        y = 16.00 + j * 7.00
        sh.exit_sign(c.wall, 0.0, y, Z_CEIL - 0.06, ang=math.pi)
    sh.fire_extinguisher(mb, xe - 0.30, 20.20, ang=math.pi * 0.5)
    sh.fire_extinguisher(mb, xw + 0.30, 33.00, ang=-math.pi * 0.5)
    sh.light_strip(c.wall, -0.40, Y_CROSS, 0.40, iy1, Z_CEIL)

    # 行き止まり: エレベーター + 立入禁止の掲示 + 進入止めのポール
    sh.elevator_bank(mb, 0.0, iy1 - 0.06, count=1, ang=0.0, w=1.05, h=2.25)
    sh.wall_sign(mb, xw + 0.02, iy1 - 1.90, 1.95, ang=-math.pi * 0.5,
                 w=1.10, h=0.40)
    c.sign(xw + 0.02, iy1 - 1.90, 1.95)
    sh.notice_board(mb, xe - 0.02, iy1 - 2.10, 0.95, ang=math.pi * 0.5,
                    w=1.60, h=1.10, sheets=5, rng=c.rng)
    for sx in (-1.30, 1.30):
        kit.cyl(mb, sx, iy1 - 2.20, 0.0, 0.92, 0.055, "stainless", seg=8)
    kit.box(mb, -1.34, iy1 - 2.24, 0.84, 1.34, iy1 - 2.16, 0.90, "stainless")
    c.poi("corridor_end", 0.0, iy1 - 3.20)
    return mb


def build(c):
    """kcd_interior のプランと同じ契約（registry.get(bid).build(c) と同形）。"""
    s = c.spec
    ix0, iy0, ix1, iy1 = s.inner()

    # 躯体（床 + 外周壁 + 入口ガラススクリーン + 天井）
    common.envelope(c, Z_CEIL, floor_mat="floor_tile_grey", door_w=DOOR_W,
                    glass="glass_clear", sill=0.95,
                    header=Z_TOP - Z_WIN_TOP, seg=3.2,
                    wall="wall_white", z_top=Z_TOP, ceil=True,
                    ceil_mat="ceiling_white", grid=2.4, floor_thick=0.30)
    common.entry_kit(c, Z_CEIL, door_w=DOOR_W, spawn_depth=1.50, bin_x=5.60)

    # 間仕切り: 玄関ホール／ラウンジ・食堂の境（中廊下の入口だけ開ける）
    sh.partition(c.wall, (ix0, Y_CROSS), (-CORR_X, Y_CROSS), 0.0, Z_PART)
    sh.partition(c.wall, (CORR_X, Y_CROSS), (ix1, Y_CROSS), 0.0, Z_PART)
    # 中廊下の壁。ラウンジと食堂の入口だけ開ける
    sh.partition(c.wall, (-CORR_X, Y_CROSS), (-CORR_X, iy1), 0.0, Z_PART,
                 gaps=[(4.40, 5.60)])
    sh.partition(c.wall, (CORR_X, Y_CROSS), (CORR_X, iy1), 0.0, Z_PART,
                 gaps=[(4.40, 5.60)])
    # 開口の上の垂れ壁（上階の床から抜けられないように必ずふさぐ。#45）
    sh.partition(c.wall, (-CORR_X, Y_CROSS + 4.40), (-CORR_X, Y_CROSS + 5.60),
                 2.20, Z_PART)
    sh.partition(c.wall, (CORR_X, Y_CROSS + 4.40), (CORR_X, Y_CROSS + 5.60),
                 2.20, Z_PART)
    # 建具は「面の法線 = +Y を ang 回転」。廊下の壁は Y 方向に走るので ang = ±pi/2。
    # 0 のままだと引き戸が廊下を横切って立つ（プレビューで発覚）
    sh.door(c.wall, -CORR_X, Y_CROSS + 5.00, ang=math.pi * 0.5, w=1.10, h=2.10,
            glass="glass_clear", open_=0.86)
    sh.door(c.wall, CORR_X, Y_CROSS + 5.00, ang=-math.pi * 0.5, w=1.10, h=2.10,
            glass="glass_clear", open_=0.86)
    # 居室エリアの仕切り（ここから奥は入れない）
    sh.partition(c.wall, (ix0, Y_NORTH), (-CORR_X, Y_NORTH), 0.0, Z_PART)
    sh.partition(c.wall, (CORR_X, Y_NORTH), (ix1, Y_NORTH), 0.0, Z_PART)

    _hall(c)
    _lounge(c)
    _dining(c)
    _corridor(c)

    # プレビュー用の明かり
    for y in (6.0, 10.5):
        c.light(-3.0, y, Z_CEIL - 0.15, 200.0, 1.6)
        c.light(3.0, y, Z_CEIL - 0.15, 200.0, 1.6)
    for y in (15.5, 20.0):
        c.light(-5.0, y, Z_CEIL - 0.15, 190.0, 1.6)
        c.light(5.0, y, Z_CEIL - 0.15, 190.0, 1.6)
    for y in (16.0, 24.0, 32.0, 39.0):
        c.light(0.0, y, Z_CEIL - 0.15, 150.0, 1.2)

    # プレビューのカメラ
    c.cam("", (0.0, 4.20, 2.25), (1.60, 12.00, 1.15), 17.0)
    c.cam("hall", (-6.60, 4.30, 2.30), (4.20, 10.60, 1.20), 19.0)
    c.cam("lounge", (-1.90, 13.60, 2.20), (-6.20, 20.00, 1.00), 19.0)
    c.cam("corridor", (0.0, 13.40, 1.95), (0.0, iy1 - 0.20, 1.55), 24.0)

    c.note("運営は %s。%s" % (OPERATOR, NOT_UNIVERSITY))
    c.note("ミニマップはキャンパス外なので黒のまま（#41 の仕様）")
    c.note("行き止まりの廊下: 居室の扉は見た目だけで開口を開けていない")
    return c


# 既存の kcd_interior プランと同じ呼び方ができるようにしておく
build_interior = build
