"""葛飾コミュニティハウス（学生寮）— 外観。屋内は kcd_route/dorm_interior.py。

裏エンド「寮でぐーたら」(#41) の舞台。キャンパスの外、水戸街道ぞいの実在の寮で、
運営は **共立メンテナンス（学生会館ドーミー）**。東京理科大学の直営ではないので、
見た目にも文言にも大学のブランド（tus_green など）は使わない。

座標
----
外観は **ワールド XY**（x = 東[m], y = 北[m]、build_campus / build_route と同じ）。
footprint・高さ・階数・玄関はすべて ``data/osm/route.json`` の ``dormitory`` から読む。
外壁の割り付け（角の階段室の幅・1 階の素材の切り替え位置・屋上テラスの位置）は、実物の
写真と航空写真から辺ローカルの座標で測った値で、下の定数にまとめてある。

外観の構成（下から）
--------------------
* 1 階: 基壇。玄関面は素材を切り替えた壁、東北東面は階段室の脇の茶色い付属棟と暗い灰色の壁。
* 2〜5 階: 玄関面（南南東）と東北東面は奥行き BALC_D のバルコニー。2 階は濃いタイルの
  腰壁、3〜5 階は型板ガラスの手すり、各階の床スラブの白い小口が横しまになる。
  裏側（西南西・北北西）は白い壁に居室ごとの縦長の窓。北北西面の奥の階段・エレベーターの
  区間と屋上の塔屋は、屋内（dorm_interior.py）の間取りにそろえる。
* 角 F: 屋上より少し高い、小口タイル張りの階段室。
* 屋上: 陸屋根。玄関面の上はウッドデッキのテラス（ガラス手すり・塔屋・ベンチ・植栽）。

使い方
------
    from kcd_route import dorm

    # route.fbx に外観を混ぜる（build_route.py から）
    shell, trim, info = dorm.build_exterior(route["dormitory"])   # info は階高・住戸数・扉など
    shell.to_object(); trim.to_object()
    doors = [dorm.door_frame(route["dormitory"])]   # door_/entrance_ Empty 用
"""

import math
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))

from kcd_lib import entrances, facade, geom          # noqa: E402
from kcd_lib.mesh import MeshBuilder                 # noqa: E402

# --------------------------------------------------------------------------- #
#  契約（Unity / build_route と共有する名前）
# --------------------------------------------------------------------------- #
ID = "dorm"
DISPLAY = "葛飾コミュニティハウス"
OPERATOR = "共立メンテナンス（学生会館ドーミー）"
NOT_UNIVERSITY = "大学直営ではない（ゲーム内で『大学の寮』と断定しない）"

SHELL_OBJ = "bld_dorm"        # footprint ぴったりの躯体（検証はこれを測る）
TRIM_OBJ = "bld_dorm_trim"    # 手すり・デッキ・ルーバー・玄関・銘板などの細部（footprint の検査の外）

# 外観の寸法。実物の写真と航空写真から測った値で、t は辺に沿う距離、u は辺から建物の
# 内側への奥行き（どちらも玄関面なら西南西の端の角 E から、東北東面なら角 F から測る）。
CORE_IN = 0.45      # 閉じた躯体（1 階の基壇・2 階以上の本体の背面）を footprint からどれだけ内側に置くか
GF_MAX = 4.00       # 1 階の階高の上限
BALC_D = 1.50       # バルコニーの奥行き。footprint（屋上スラブの外形）の内側に取る
SLAB_T = 0.20       # バルコニーの床スラブの厚み。白い小口が各階の横しまになる
RAIL_H = 1.10       # 3〜5 階の型板ガラスの手すりの高さ（床から）
BAND_LO = 0.40      # 2 階のタイル張りの腰壁: 床から下へ
BAND_HI = 1.20      # 同: 床から上へ
UNIT_W = 2.80       # 住戸の幅（バルコニーの隔て板の間隔）
ROOF_T = 0.30       # 屋上スラブの厚み。5 階のバルコニーの屋根を兼ねる
PARAPET_H = 0.60
PARAPET_T = 0.25    # パラペットの厚み
CORE_S = 2.40       # 角（玄関面と東北東面の出会う角 F）の階段室: 玄関面に沿う幅
CORE_U = 5.00       # 同: 東北東面に沿う幅
CORE_TOP = 1.10     # 階段室の屋上からの立ち上がり（屋上のガラス手すりの天端とそろう）
TERRACE_T0 = 4.60   # 屋上テラス（玄関面の上）の西端の t。ここに背の高いメッシュフェンス
TERRACE_U = 13.50   # 同: 北端の u。ここに縦格子の手すり

# 玄関面 1 階の割り付け（E からの t）。茶色いパネル → 小窓のある凹んだ壁 → 割肌の石 →
# 黒いタイル → 玄関 → 目隠しルーバー → 階段室。玄関の扉は route.json の entrance.point
# （玄関面の中点）に置く。実物の扉はもう少し東寄りにある。
FRONT_BROWN = 0.90
FRONT_RECESS = 2.60
FRONT_STONE = 4.60

# 玄関の足元の石張り（エプロン）の奥行き。entrances.STANDARD は 7.0 m だが、この玄関は
# 目の前が前面道路（osm 58360717, tertiary, 幅 9.0 m）で、扉から車道の縁まで 4.00 m
# （エプロンの外側の角 s=+3.4 では 3.07 m）しかない。7.0 m のままだと車道へ最大 3.72 m
# 乗り上げ、天端 0.12 m の段差が車道を横切る。実測した「車道に触れない上限」は 3.069 m。
# キャンパス 9 棟は entrances.STANDARD を共有しているので、そちらは触らず寮だけ上書きする。
DOOR_APRON_D = 2.80

# 東北東面 1 階: 階段室の脇の茶色い付属棟（写真で本体の前へ張り出した別の箱）。その先は
# 暗い灰色の基壇。付属棟の長さは写真の遠近から読めないので推定。
ANNEX_L = 6.00
ANNEX_D = 1.50

# 屋内（kcd_route/dorm_interior.py）の間取りに外壁をそろえる位置。建物ローカル
# （原点 = 玄関の点、x = 玄関から奥を見て右、y = 奥）で持ち、_t_of で辺の t に直す。
STAIR_X = (4.60, 6.80)      # 奥の階段（鉄扉 x 5.60）
EV_X = (-2.90, 2.70)        # エレベーター 2 基とその機械
EV_Y = 38.00                # 屋上の塔屋の手前の端（奥の辺からおよそ 4 m）
BATH_Y = (27.00, 34.40)     # 1 階の大浴場（西）
KITCHEN_Y = (24.60, 28.80)  # 1 階の厨房（東）

# 裏の 2〜5 階の居室の窓（1 室に 1 つ、縦長）
ROOM_WIN = dict(wall="dorm_white", glass="glass_dark",
                seg=3.0, sill=0.90, header=0.55, inset=0.20, mullion=1.70)


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


def _pt(fr, t, d):
    """辺ローカル (t = 辺に沿う距離, d = 外向きの出。内側は負) を XY にする。"""
    p, e, n, _ = fr
    return (p[0] + e[0] * t + n[0] * d, p[1] + e[1] * t + n[1] * d)


def _box(mb, fr, t0, t1, d0, d1, z0, z1, mat, top=None, bottom=None):
    """辺ローカルの直方体（閉じた add_prism）。top / bottom を省くと側面と同じ材質で塞ぐ。"""
    poly = [_pt(fr, t0, d0), _pt(fr, t1, d0), _pt(fr, t1, d1), _pt(fr, t0, d1)]
    mb.add_prism(poly, z0, z1, mat, top or mat, bottom or mat)


def _box_open(mb, fr, t0, t1, d0, d1, z0, z1, mat, open_end):
    """_box から t0 側（open_end="t0"）か t1 側の端の面を抜いたもの。

    その端が隣の辺の外壁と同じ平面に乗るとき、面を 2 枚重ねるとちらつく（z-fighting）ので、
    隣の辺の外壁に塞がせる。"""
    def v(t, d, z):
        x, y = _pt(fr, t, d)
        return (x, y, z)
    mb.add_quad(v(t0, d1, z0), v(t1, d1, z0), v(t1, d1, z1), v(t0, d1, z1), mat)   # 外
    mb.add_quad(v(t1, d0, z0), v(t0, d0, z0), v(t0, d0, z1), v(t1, d0, z1), mat)   # 内
    mb.add_quad(v(t0, d1, z1), v(t1, d1, z1), v(t1, d0, z1), v(t0, d0, z1), mat)   # 上
    mb.add_quad(v(t0, d0, z0), v(t1, d0, z0), v(t1, d1, z0), v(t0, d1, z0), mat)   # 下
    if open_end != "t1":
        mb.add_quad(v(t1, d1, z0), v(t1, d0, z0), v(t1, d0, z1), v(t1, d1, z1), mat)
    if open_end != "t0":
        mb.add_quad(v(t0, d0, z0), v(t0, d1, z0), v(t0, d1, z1), v(t0, d0, z1), mat)


def _parapet_chain(mb, outer, inner, z, h, mat):
    """facade.add_parapet と同じ断面の笠木壁を、折れ線 outer / inner に沿って立てる。

    ループを一周せず途中で終わるときに使う。終点の端は塞ぐ（始点は階段室に突き当てる）。"""
    zt = z + h
    for k in range(len(outer) - 1):
        a, b = outer[k], outer[k + 1]
        ai, bi = inner[k], inner[k + 1]
        mb.add_quad((a[0], a[1], z), (b[0], b[1], z), (b[0], b[1], zt), (a[0], a[1], zt), mat)
        if geom.length(geom.sub(bi, ai)) < 1e-3:       # 直角の角の留めで内側が点になる
            mb.add_face([(a[0], a[1], zt), (b[0], b[1], zt), (bi[0], bi[1], zt)], mat)
            continue
        mb.add_quad((a[0], a[1], zt), (b[0], b[1], zt), (bi[0], bi[1], zt), (ai[0], ai[1], zt), mat)
        mb.add_quad((ai[0], ai[1], zt), (bi[0], bi[1], zt), (bi[0], bi[1], z), (ai[0], ai[1], z), mat)
    p, pi = outer[-1], inner[-1]
    mb.add_quad((p[0], p[1], z), (pi[0], pi[1], z), (pi[0], pi[1], zt), (p[0], p[1], zt), mat)


def _pane(mb, fr, t0, t1, d, z0, z1, mat):
    """辺ローカルの d に外向きの板を 1 枚貼る。閉じた立体のすぐ手前に置く窓・扉用。"""
    a, b = _pt(fr, t0, d), _pt(fr, t1, d)
    mb.add_quad((a[0], a[1], z0), (b[0], b[1], z0),
                (b[0], b[1], z1), (a[0], a[1], z1), mat)


def _inset(loop, offs):
    """辺 i を offs[i] だけ内側へ平行移動し、隣り合う線の交点でポリゴンを作り直す。"""
    n = len(loop)
    lines = []
    for i in range(n):
        p, e, nrm, _ = _edge_frame(loop, i)
        lines.append((geom.add(p, geom.mul(nrm, -offs[i])), e))
    out = []
    for i in range(n):
        (p1, e1), (p2, e2) = lines[i - 1], lines[i]
        den = e1[0] * e2[1] - e1[1] * e2[0]
        if abs(den) < 1e-9:
            out.append(p2)
            continue
        w = geom.sub(p2, p1)
        s = (w[0] * e2[1] - w[1] * e2[0]) / den
        out.append(geom.add(p1, geom.mul(e1, s)))
    return out


def _run(mb, loop, i, t0, t1, z0, floor_h, f0, f1, **kw):
    """辺 i の t0..t1 の区間だけに facade.add_facade の窓割りを貼る。"""
    fr = _edge_frame(loop, i)
    rect = [_pt(fr, t0, 0.0), _pt(fr, t1, 0.0), _pt(fr, t1, -1.0), _pt(fr, t0, -1.0)]
    facade.add_facade(mb, rect, z0, floor_h, f0, f1, edges=[0], **kw)


def _t_of(dorm, fr, x, y):
    """建物ローカル (x, y)（屋内と同じ座標）が辺 fr のどの t に当たるか。"""
    origin, n, _ = entrance_of(dorm)
    p = (origin[0] - n[1] * x - n[0] * y, origin[1] + n[0] * x - n[1] * y)
    return geom.dot(geom.sub(p, fr[0]), fr[1])


def _span(dorm, fr, x0, y0, x1, y1):
    """建物ローカルの 2 点を辺 fr の t に直し、小さい順に返す。"""
    return tuple(sorted((_t_of(dorm, fr, x0, y0), _t_of(dorm, fr, x1, y1))))


def _downpipe(mb, fr, t, z1):
    """外壁に沿って下りる竪樋（上下の抜けた角パイプ。背面は壁に付くので張らない）。"""
    def v(tt, d, z):
        x, y = _pt(fr, tt, d)
        return (x, y, z)
    t0, t1, d0, d1 = t - 0.06, t + 0.06, 0.03, 0.15
    mb.add_quad(v(t0, d1, 0.0), v(t1, d1, 0.0), v(t1, d1, z1), v(t0, d1, z1), "metal_grey")
    mb.add_quad(v(t1, d1, 0.0), v(t1, d0, 0.0), v(t1, d0, z1), v(t1, d1, z1), "metal_grey")
    mb.add_quad(v(t0, d0, 0.0), v(t0, d1, 0.0), v(t0, d1, z1), v(t0, d0, z1), "metal_grey")


def _side_ground(shell, trim, fs, dorm, top1):
    """東北東面の 1 階: 階段室の脇の茶色い付属棟、その先の暗い灰色の基壇と厨房の勝手口。"""
    a1 = CORE_U + ANNEX_L
    # 付属棟。階段室に当たる端は抜く
    _box_open(shell, fs, CORE_U, a1, -ANNEX_D, 0.0, 0.0, top1, "panel_brown", "t0")
    # その先は基壇（footprint から CORE_IN 内側）に暗い灰色の石調を貼る。角 A の柱は
    # 裏の辺の 1 階の外壁と同じ平面なので、その端を抜く
    _pane(trim, fs, a1, fs[3] - CORE_IN, -CORE_IN + 0.01, 0.0, top1, "stone_dark")
    _box_open(shell, fs, fs[3] - CORE_IN, fs[3], -CORE_IN, 0.0, 0.0, top1, "stone_dark", "t1")
    # 厨房の勝手口（鉄扉）と換気ガラリ
    k0, k1 = _span(dorm, fs, 8.0, KITCHEN_Y[0], 8.0, KITCHEN_Y[1])
    _pane(trim, fs, k0 + 0.60, k0 + 1.50, -CORE_IN + 0.02, 0.0, 2.10, "metal_charcoal")
    _pane(trim, fs, k1 - 1.60, k1 - 0.60, -CORE_IN + 0.02, 1.60, 2.40, "metal_grey")
    # 付属棟の前の自販機（銘柄・ロゴは入れない）
    _box(trim, fs, CORE_U + 1.00, CORE_U + 2.00, 0.10, 0.85, 0.0, 1.83, "vending_blue",
         "metal_white")


def _rear_walls(shell, trim, loop, dorm, rear, side, front, gf, up, lv, h):
    """裏の 4 辺（西南西・北北西）の 1 階と 2 階以上、竪樋、北北西端の屋上の塔屋。

    * 2 階以上は居室ごとに縦長の窓 1 つ（ROOM_WIN）。
    * 北北西面は屋内の奥の階段に当たる区間を縦長のスリット窓、エレベーターの区間を窓の無い
      壁、残りを廊下の突き当たりの窓にする。
    * 西南西面の 1 階の大浴場に当たる区間は腰の高い型板ガラスの高窓。
    """
    n = len(loop)
    nnw = (side + 1) % n                                    # 角 A から始まる北北西面
    for i in rear:
        fr = _edge_frame(loop, i)
        L = fr[3]
        # 1 階
        cuts = [0.0, L]
        if i != nnw:
            b0, b1 = _span(dorm, fr, -8.0, BATH_Y[0], -8.0, BATH_Y[1])
            if b1 - b0 > 1.0 and b1 > 0.5 and b0 < L - 0.5:
                cuts = [0.0, max(0.0, b0), min(L, b1), L]
        for k in range(len(cuts) - 1):
            bath = len(cuts) == 4 and k == 1
            _run(shell, loop, i, cuts[k], cuts[k + 1], 0.0, gf, 0, 1,
                 wall="dorm_white", glass="glass_frosted" if bath else "glass_dark",
                 seg=3.2, sill=1.80 if bath else 1.20, header=1.20, inset=0.20,
                 mullion=2.00)
        # 2 階以上
        t0 = BALC_D if i == nnw else 0.0                    # 東北東面のバルコニーの妻
        t1 = L - BALC_D if (i + 1) % n == front else L      # 玄関面のバルコニーの妻
        if i != nnw:
            _run(shell, loop, i, t0, t1, gf, up, 0, lv - 1, **ROOM_WIN)
            continue
        c = sum(_span(dorm, fr, STAIR_X[0], 40.0, STAIR_X[1], 40.0)) * 0.5
        e1 = _span(dorm, fr, EV_X[0], 40.0, EV_X[1], 40.0)[1]
        _pane(shell, fr, t0, c - 0.60, 0.0, gf, h, "dorm_white")
        _run(shell, loop, i, c - 0.60, c + 0.60, gf, up, 0, lv - 1, wall="dorm_white",
             glass="glass_dark", seg=9.0, sill=0.30, header=0.30, inset=0.20, mullion=0.60)
        _pane(shell, fr, c + 0.60, e1, 0.0, gf, h, "dorm_white")
        _run(shell, loop, i, e1, t1, gf, up, 0, lv - 1, **ROOM_WIN)
        # 屋上の塔屋（エレベーターの機械と階段の出口）。奥の辺から EV_Y まで
        p = geom.sub(_pt(_edge_frame(loop, side), _t_of(dorm, _edge_frame(loop, side),
                                                         8.0, EV_Y), 0.0), fr[0])
        _box(trim, fr, t0 - 0.10, e1, geom.dot(p, fr[2]), -0.60, h, h + 2.90,
             "dorm_white", "concrete_light")
    # 竪樋: 北北西面の西の角と、西南西面の南の端
    _downpipe(trim, _edge_frame(loop, (nnw + 1) % n), 0.40, h + PARAPET_H - 0.10)
    last = [i for i in rear if (i + 1) % n == front][0]
    fr = _edge_frame(loop, last)
    _downpipe(trim, fr, fr[3] - BALC_D - 0.40, h + PARAPET_H - 0.10)


def _balconies(mb, fr, t0, t1, z_floors):
    """辺ローカル t0..t1 に各階のバルコニーを並べる。戻り値は 1 階ぶんの住戸数。

    床スラブ・腰壁（2 階）/ 型板ガラスの手すり（3 階以上）・隔て板・奥の掃き出し窓。
    """
    units = max(1, int(round((t1 - t0) / UNIT_W)))
    w = (t1 - t0) / units
    for k, z in enumerate(z_floors):
        _box(mb, fr, t0, t1, -BALC_D, 0.0, z - SLAB_T, z, "dorm_white")
        if k == 0:
            _box(mb, fr, t0, t1, -0.10, 0.04, z - BAND_LO, z + BAND_HI, "tile_charcoal")
        else:
            _box(mb, fr, t0, t1, -0.10, -0.04, z, z + RAIL_H - 0.06, "glass_frosted")
            _box(mb, fr, t0, t1, -0.12, -0.02, z + RAIL_H - 0.06, z + RAIL_H, "metal_white")
        for j in range(1, units):
            ts = t0 + w * j
            _box(mb, fr, ts - 0.03, ts + 0.03, -BALC_D, -0.12, z, z + 1.95, "dorm_white")
        for j in range(units):
            _pane(mb, fr, t0 + w * j + 0.35, t0 + w * (j + 1) - 0.35, -BALC_D + 0.01,
                  z + 0.05, z + 2.05, "glass_dark")
    return units


def _front_ground(shell, trim, ff, t_door, dr, core_t, top):
    """玄関面の 1 階。E から順に素材を切り替える（FRONT_* の割り付け）。"""
    s0, s1 = t_door - dr["WO"], t_door + dr["WO"]
    _box_open(shell, ff, 0.0, FRONT_BROWN, -0.30, 0.0, 0.0, top, "panel_brown", "t0")
    for a, b, mat in ((FRONT_RECESS, FRONT_STONE, "stone_dark"),
                      (FRONT_STONE, s0, "plastic_black"),
                      (s1, s1 + 0.20, "concrete_grey")):
        if b - a > 0.01:
            _box(shell, ff, a, b, -0.30, 0.0, 0.0, top, mat)
    # 凹んだ壁の小窓（壁は基壇の側面そのもの）
    _box(trim, ff, FRONT_BROWN + 0.35, FRONT_RECESS - 0.35, -CORE_IN, -CORE_IN + 0.04,
         1.10, 2.30, "metal_charcoal")
    _pane(trim, ff, FRONT_BROWN + 0.42, FRONT_RECESS - 0.42, -CORE_IN + 0.045,
          1.17, 2.23, "glass_dark")
    # 玄関の風除室の上（庇から 2 階の床まで）
    _box(shell, ff, s0, s1, -0.30, 0.0, dr["HT"], top, "concrete_grey")
    # 目隠しルーバーと、それに絡むつる植物
    r0, r1 = s1 + 0.20, core_t
    _pane(trim, ff, r0, r1, -CORE_IN + 0.02, 0.0, top, "metal_charcoal")
    _box(trim, ff, r0, r1, -0.14, -0.02, 0.0, 0.15, "metal_charcoal")
    _box(trim, ff, r0, r1, -0.14, -0.02, top - 0.15, top, "metal_charcoal")
    n_slat = int((r1 - r0) / 0.20)
    for j in range(n_slat):
        ts = r0 + 0.10 + 0.20 * j
        _box(trim, ff, ts - 0.025, ts + 0.025, -0.12, -0.04, 0.15, top - 0.15, "metal_charcoal")
    for a, b, z0, z1 in ((r0 + 0.3, r0 + 1.1, 0.4, 2.6), (r1 - 1.3, r1 - 0.4, 1.0, 3.2)):
        _box(trim, ff, a, b, -0.03, 0.05, z0, z1, "leaf_light", "leaf_top")
    return s0, s1


def _street_planters(trim, ff, t_door):
    """玄関の左右、歩道側の植え込み（黒いプランターに低木と赤い花）。"""
    for a, b in ((t_door - 7.6, t_door - 3.6), (t_door + 3.6, t_door + 5.6)):
        a = max(0.05, a)
        if b - a < 0.8:
            continue
        _box(trim, ff, a, b, 0.05, 0.85, 0.0, 0.55, "plastic_black")
        _box(trim, ff, a + 0.10, b - 0.10, 0.15, 0.75, 0.55, 0.95, "leaf_light", "leaf_top")
        k = 0
        t = a + 0.35
        while t < b - 0.35:
            _box(trim, ff, t - 0.12, t + 0.12, 0.30 + 0.15 * (k % 2), 0.54 + 0.15 * (k % 2),
                 0.95, 1.07, "flower_red")
            t += 0.70
            k += 1


def _roof_terrace(trim, ff, core_t, h):
    """屋上テラス（玄関面の上）と、その奥の塔屋・空調の室外機。位置は航空写真から。"""
    t0, u1 = TERRACE_T0, TERRACE_U
    _box(trim, ff, t0, core_t, -u1, -0.25, h, h + 0.10, "deck_wood")
    # 玄関面の縁: 低い立ち上がり + ガラス手すり
    _box(trim, ff, 0.25, core_t, -0.25, 0.0, h, h + 0.30, "dorm_white")
    _box(trim, ff, 0.25, core_t, -0.15, -0.11, h + 0.30, h + 1.10, "glass_clear")
    _box(trim, ff, 0.25, core_t, -0.17, -0.09, h + 1.10, h + 1.18, "metal_white")
    n_post = max(2, int(round((core_t - 0.25) / 1.8)) + 1)
    for j in range(n_post):
        ts = 0.25 + (core_t - 0.25 - 0.06) * j / (n_post - 1)
        _box(trim, ff, ts, ts + 0.06, -0.17, -0.09, h + 0.30, h + 1.10, "metal_white")
    # 西端: 背の高いメッシュフェンス（柱と横桟）
    for j in range(7):
        u = 0.30 + (u1 - 0.36) * j / 6
        _box(trim, ff, t0 - 0.03, t0 + 0.03, -u - 0.06, -u, h, h + 2.40, "metal_grey")
    for z in (h + 0.10, h + 1.25, h + 2.34):
        _box(trim, ff, t0 - 0.02, t0 + 0.02, -u1, -0.30, z, z + 0.06, "metal_grey")
    # 北端: 縦格子の手すり
    for z in (h + 0.12, h + 1.04):
        _box(trim, ff, t0, core_t, -u1 - 0.03, -u1 + 0.03, z, z + 0.06, "metal_grey")
    t = t0 + 0.25
    while t < core_t - 0.1:
        _box(trim, ff, t - 0.015, t + 0.015, -u1 - 0.015, -u1 + 0.015, h + 0.18, h + 1.04,
             "metal_grey")
        t += 0.50
    # 塔屋（階段の出口）: 灰色の縁石の上の白い箱、扉と小庇はテラス側
    _box(trim, ff, 8.85, 11.15, -11.76, -7.76, h, h + 0.30, "concrete_grey")
    _box(trim, ff, 9.00, 11.00, -11.51, -8.01, h + 0.30, h + 2.90, "dorm_white",
         "concrete_light")
    _pane(trim, ff, 9.55, 10.45, -8.00, h + 0.32, h + 2.40, "metal_grey")
    _box(trim, ff, 9.35, 10.65, -8.01, -7.41, h + 2.50, h + 2.60, "metal_white")
    # 奥の小さな塔屋と空調の室外機
    _box(trim, ff, 12.75, 14.05, -21.45, -17.95, h, h + 2.00, "dorm_white", "concrete_light")
    for tc, uc in ((6.98, 18.80), (9.53, 18.48), (7.26, 15.27), (9.68, 15.76)):
        _box(trim, ff, tc - 0.50, tc + 0.50, -uc - 0.35, -uc + 0.35, h, h + 0.90, "metal_grey")
    # 植栽とベンチ
    for a, b, d0, d1 in ((5.20, 7.40, -1.05, -0.35), (11.40, 13.60, -1.05, -0.35),
                         (t0 + 0.15, t0 + 0.85, -8.00, -3.00)):
        _box(trim, ff, a, b, d0, d1, h + 0.10, h + 0.55, "plastic_black")
        _box(trim, ff, a + 0.10, b - 0.10, d0 + 0.10, d1 - 0.10, h + 0.55, h + 0.95,
             "leaf_light", "leaf_top")
    for a, b, d0, d1 in ((7.60, 9.40, -3.45, -3.00), (5.60, 6.05, -7.60, -5.80)):
        _box(trim, ff, a, b, d0, d1, h + 0.10, h + 0.45, "deck_wood")


def _nameplate(mb, dr):
    """銘板「葛飾コミュニティハウス」。道路際の自立サイン（台座 + 白い板 + 濃色の帯）。

    大学名も大学色（tus_green）も使わない。運営は共立メンテナンスであって大学ではない。
    """
    def box(s0, s1, d0, d1, z0, z1, mat, top=None, bottom=None):
        poly = [entrances.local(dr, s0, d0), entrances.local(dr, s1, d0),
                entrances.local(dr, s1, d1), entrances.local(dr, s0, d1)]
        mb.add_prism(poly, z0, z1, mat, top, bottom)

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
        "metal_charcoal", "metal_charcoal", "metal_charcoal")


def build_exterior(dorm, shell=None, trim=None):
    """外観を組む。戻り値は (躯体の MeshBuilder, 付属物の MeshBuilder, 寸法の辞書)。

    * 躯体（shell）は footprint の外へ 1 mm も出ない。検証はこれを測る。
    * 基壇・本体・階段室・屋上スラブ・バルコニーは **閉じた立体**（add_prism）で作る。
      板 1 枚の面は PhysX が片面でしか受け止めず、室内から外へ素通りになる（#45）。
      窓ガラスなどの板は、必ず閉じた立体のすぐ手前に貼る。
    """
    shell = shell or MeshBuilder(SHELL_OBJ)
    trim = trim or MeshBuilder(TRIM_OBJ)

    loop = loop_of(dorm)
    n = len(loop)
    h = float(dorm["height"])
    gf, up, lv = floor_heights(dorm)
    top1 = gf - SLAB_T                       # 1 階の壁の天端 = 2 階のバルコニーの床下

    front = int(dorm["entrance"]["edge_index"]) % n   # 玄関面（南南東）
    side = (front + 1) % n                            # 東北東面（いちばん長い辺）
    rear = [i for i in range(n) if i not in (front, side)]
    ff, fs = _edge_frame(loop, front), _edge_frame(loop, side)
    core_t = ff[3] - CORE_S                  # 玄関面で階段室が始まる t
    dr = door_frame(dorm)
    t_door = geom.dot(geom.sub(dr["origin"], ff[0]), ff[1])

    # --- 閉じた躯体: 1 階の基壇、2 階以上の本体（玄関面・東北東面はバルコニーの奥まで下げる）
    shell.add_prism(geom.offset_polygon(loop, -CORE_IN), -0.60, top1,
                    "concrete_grey", "concrete_grey", "concrete_dark")
    offs = [BALC_D if i in (front, side) else CORE_IN for i in range(n)]
    shell.add_prism(_inset(loop, offs), top1, h - ROOF_T,
                    "dorm_cream", "dorm_white", "dorm_white")

    # --- 角 F の階段室（footprint の 2 辺にぴったり。屋上より CORE_TOP 高い） ---
    corner = loop[side]
    a = _pt(ff, core_t, 0.0)
    b = _pt(fs, CORE_U, 0.0)
    inner = geom.add(a, geom.sub(b, corner))
    shell.add_prism([a, corner, b, inner], -0.60, h + CORE_TOP,
                    "tile_mauve", "concrete_dark", "concrete_dark")

    # --- 1 階 ---
    _front_ground(shell, trim, ff, t_door, dr, core_t, top1)
    _side_ground(shell, trim, fs, dorm, top1)

    # --- 裏の 4 辺（1 階・2 階以上・竪樋・北北西端の塔屋） ---
    z_floors = [gf + up * k for k in range(lv - 1)]
    _rear_walls(shell, trim, loop, dorm, rear, side, front, gf, up, lv, h)
    # バルコニーの妻壁（両端。footprint の角 E・A にかかる）。幅は裏の本体の下げ CORE_IN と
    # そろえ、本体との間にすき間を作らない。1 階の天端〜2 階の床は裏の辺の 1 階の外壁と
    # 同じ平面に乗るので、その端を抜く
    _box_open(shell, ff, 0.0, CORE_IN, -BALC_D, 0.0, top1, gf, "dorm_white", "t0")
    _box(shell, ff, 0.0, CORE_IN, -BALC_D, 0.0, gf, h - ROOF_T, "dorm_white")
    _box_open(shell, fs, fs[3] - CORE_IN, fs[3], -BALC_D, 0.0, top1, gf, "dorm_white", "t1")
    _box(shell, fs, fs[3] - CORE_IN, fs[3], -BALC_D, 0.0, gf, h - ROOF_T, "dorm_white")
    units = _balconies(trim, ff, CORE_IN, core_t, z_floors)
    units += _balconies(trim, fs, CORE_U, fs[3] - CORE_IN, z_floors)

    # --- 屋上: スラブ（5 階のバルコニーの屋根を兼ねる）+ パラペット + テラス ---
    shell.add_prism(geom.offset_polygon(loop, -0.02), h - ROOF_T, h,
                    "dorm_white", "concrete_light", "dorm_white")
    # パラペットは東北東面の階段室の脇から裏の 4 辺を回り、玄関面の角 E の先 0.25 m で止める
    # （階段室の面と重ねない）
    ring = geom.offset_polygon(loop, -PARAPET_T)
    corners = [(side + 1 + k) % n for k in range(len(rear) + 1)]
    _parapet_chain(shell,
                   [_pt(fs, CORE_U, 0.0)] + [loop[i] for i in corners] + [_pt(ff, PARAPET_T, 0.0)],
                   [_pt(fs, CORE_U, -PARAPET_T)] + [ring[i] for i in corners]
                   + [_pt(ff, PARAPET_T, -PARAPET_T)],
                   h, PARAPET_H, "dorm_white")
    _roof_terrace(trim, ff, core_t, h)

    # --- 玄関（キャンパスの他の棟と同じ「入れる扉」。紺の風除室 + ガラス両開き + 庇） ---
    entrances.build_one(trim, dr)
    _nameplate(trim, dr)
    _street_planters(trim, ff, t_door)

    return shell, trim, dict(levels=lv, gf=gf, up=up, height=h,
                             balcony_edge=[front, side], balcony_floors=len(z_floors),
                             balcony_units=units, door=dr)
