"""建物の入口（風除室 + ガラスの両開き扉 + 庇 + 足元の石張り）。

入口は「どこから入れるのか分からない」(#39) を解くための目印なので、どの建物でも同じ形・同じ色
（紺の枠 + 白い扉枠 + 光る庇裏）にそろえる。形と Unity の入口トリガーを同じ計画 (plan) から
作るので、見た目の扉とトリガーの位置が食い違わない。

扉ごとのローカル座標:
  s = 壁に沿う向き（t = n を +90 度回した向き）、d = 壁から外へ（n）、z = 上。
  原点は扉の中心線と外壁の交点（地面の高さ）。

Empty（build_campus.py が書き出す）:
  door_<id>     扉の外面の中心（d = D）
  entrance_<id> 扉の前の床（d = D + 0.8）。Unity はここにトリガーを置き、
                door → entrance の向きを「外」とみなす。
"""

import math

from . import geom

# 標準の入口（幅 3.2 m・高さ 2.6 m の両開き、奥行 2 m の風除室）
STANDARD = dict(
    W=1.6,        # 扉の開口の半幅
    H=2.6,        # 扉の高さ
    D=2.0,        # 風除室の張り出し
    WO=2.4,       # 風除室の外形の半幅
    HT=3.3,       # 風除室の天端（= 庇の下端）
    CAN_S=3.0,    # 庇の半幅
    CAN_D=3.8,    # 庇の出
    CAN_T=0.30,   # 庇の厚み（鼻先の紺の帯）
    APRON_S=3.4,  # 足元の石張りの半幅
    APRON_D=7.0,  # 足元の石張りの長さ
)

# 温室は小さな片開き程度の入口にする（棟の高さ 3.4 m・軒 2.0 m）
SMALL = dict(STANDARD, W=0.9, H=2.1, D=1.2, WO=1.5, HT=2.5,
             CAN_S=1.9, CAN_D=2.2, CAN_T=0.20, APRON_S=2.2, APRON_D=4.0)

APRON_Z = 0.12      # 石張りの天端（モール 0.085・歩道 0.055 より上。z-fight 回避）
BURY = 0.25         # 風除室と庇を壁の中へ埋める深さ（斜めの壁でも隙間を出さない）
KEEP_CLEAR = 8.0    # 扉の前に木・ベンチ・照明柱を置かない長さ（風除室の先から）
KEEP_HALF = 3.6     # その半幅
SIGN_D = 2.5        # 立て看板を風除室の先からどれだけ出すか
SIGN_S = 2.4        # 立て看板を風除室の側壁の外からどれだけ横へずらすか

# (建物 id, 探索の起点 (u, v), 建物へ向かう向き (du, dv), 扉を付ける面, 看板の側, 看板の高さ, 寸法)
#   面: "loop" = footprint の外壁、"inner1" = 図書館の 1F（外周から 2.2 m 引っ込んだガラス面）、
#       "bbox" = 温室（uv の外接矩形で建てている）
#   看板の側: +1 なら +s 側、-1 なら -s 側（木や照明柱とぶつからない側を選んである）
DOORS = [
    # 第1研究棟: 南面の切り欠き（u 111..127, 奥行 5 m）の奥の壁。ピロティの柱の間から入る
    ("research1", (119.0, -90.0), (0.0, 1.0), "loop", +1, 3.4, STANDARD),
    # 共創棟: モール側（+v）。スターバックスの色板（u 93..103）の西隣
    ("kyoso", (89.9, -28.0), (0.0, -1.0), "loop", +1, 3.4, STANDARD),
    # 講義棟: 南面（モール側）。曲面のガラスホール（u < 110）から離す
    ("lecture", (120.6, -22.0), (0.0, 1.0), "loop", -1, 3.4, STANDARD),
    # 第2研究棟: 南面の直線部（u 6..23）の中央。食堂の前
    ("research2", (14.5, -22.0), (0.0, 1.0), "loop", +1, 3.4, STANDARD),
    # 図書館: 東面。モールの軸（v = -24）の突き当たり
    ("library", (-50.0, -24.0), (-1.0, 0.0), "inner1", +1, 3.4, STANDARD),
    # 体育館: 南面の中央
    ("gym", (-2.1, 28.0), (0.0, 1.0), "loop", +1, 3.4, STANDARD),
    # 第1実験棟: 南面
    ("lab1", (56.2, 36.0), (0.0, 1.0), "loop", +1, 3.0, STANDARD),
    # 第2実験棟: 東面（南面は第1実験棟と 1.5 m しか離れていない）
    ("lab2", (80.0, 74.0), (-1.0, 0.0), "loop", +1, 3.0, STANDARD),
    # 温室: 西の妻面（歩道側）
    ("greenhouse", (80.0, 66.0), (1.0, 0.0), "bbox", +1, 2.4, SMALL),
]


# --------------------------------------------------------------------------- #
#  配置
# --------------------------------------------------------------------------- #
def _ray_hit(origin, direction, loop):
    """origin から direction へ伸ばした半直線が loop の辺に最初に当たる点と、その辺の外向き法線。"""
    loop = geom.ensure_ccw(geom.dedup(loop))
    best = None
    n = len(loop)
    for i in range(n):
        a, b = loop[i], loop[(i + 1) % n]
        e = geom.sub(b, a)
        den = geom.cross(direction, e)
        if abs(den) < 1e-9:
            continue
        ao = geom.sub(a, origin)
        t = geom.cross(ao, e) / den
        k = geom.cross(ao, direction) / den
        if t <= 0.0 or k < 0.0 or k > 1.0:
            continue
        nrm = geom.outward_normal(a, b)
        if geom.dot(nrm, direction) >= 0.0:
            continue            # 裏から当たった（内側の辺）
        if best is None or t < best[0]:
            best = (t, geom.add(origin, geom.mul(direction, t)), nrm)
    return best


def local(dr, s, d):
    """扉のローカル (s, d) → ワールド (x, y)。"""
    o, n, t = dr["origin"], dr["n"], dr["t"]
    return (o[0] + t[0] * s + n[0] * d, o[1] + t[1] * s + n[1] * d)


def _wall_loop(kind, loop, frame):
    if kind == "inner1":
        return geom.offset_polygon(loop, -2.2)
    if kind == "bbox":
        u0, v0, u1, v1 = frame.uv_bbox(loop)
        return frame.rect(u0, v0, u1, v1)
    return loop


def plan(data, frame, ctx):
    """扉の位置と向きを決め、ctx に入口・看板・扉を書き込む。戻り値は扉の辞書のリスト。"""
    loops = {}
    for b in data["buildings"]:
        if b.get("on_campus"):
            loops[b["id"]] = geom.ensure_ccw(geom.dedup(b["footprint"]))

    doors = []
    for bid, probe_uv, into_uv, kind, sign_side, sign_z, spec in DOORS:
        loop = loops.get(bid)
        if loop is None:
            print("[entrance] %s: campus.json に建物が無いので飛ばします" % bid)
            continue
        wall = _wall_loop(kind, loop, frame)
        origin = frame.xy(*probe_uv)
        direction = geom.normalize(frame.xy(*into_uv))
        hit = _ray_hit(origin, direction, wall)
        if hit is None:
            raise RuntimeError("[entrance] %s: 外壁が見つかりません (probe %s)" % (bid, probe_uv))
        _t, p, nrm = hit
        dr = dict(spec)
        dr.update(id=bid, origin=p, n=nrm, t=(-nrm[1], nrm[0]),
                  yaw=math.atan2(nrm[1], nrm[0]), sign_side=sign_side, sign_z=sign_z)
        doors.append(dr)

    ctx["door_frames"] = {dr["id"]: dr for dr in doors}
    ctx["door"] = [(dr["id"], local(dr, 0.0, dr["D"]), APRON_Z) for dr in doors]
    ctx["entrance"] = [(dr["id"], local(dr, 0.0, dr["D"] + 0.8), APRON_Z) for dr in doors]
    # 看板は通り道の脇（風除室の先 2.5 m・側壁の外 2.4 m）に、板の正面を外（近づく人の方）へ向ける
    ctx["sign"] = [(dr["id"], local(dr, dr["sign_side"] * (dr["WO"] + SIGN_S), dr["D"] + SIGN_D),
                    dr["sign_z"], dr["yaw"]) for dr in doors]
    return doors


def corridor(dr):
    """扉の前の通り道（木・ベンチ・照明柱を置かない範囲）の XY ポリゴン。"""
    hw = max(KEEP_HALF, dr["APRON_S"] + 0.2)
    d1 = dr["D"] + KEEP_CLEAR
    return [local(dr, -hw, 0.0), local(dr, hw, 0.0), local(dr, hw, d1), local(dr, -hw, d1)]


# --------------------------------------------------------------------------- #
#  形
# --------------------------------------------------------------------------- #
def _box(mb, dr, s0, s1, d0, d1, z0, z1, mat, top=None, bottom=None):
    poly = [local(dr, s0, d0), local(dr, s1, d0), local(dr, s1, d1), local(dr, s0, d1)]
    mb.add_prism(poly, z0, z1, mat, top, bottom)


def _wall_quad(mb, dr, s0, s1, d, z0, z1, mat):
    """d の位置に外（+n）を向く縦の板を 1 枚。"""
    a = local(dr, s0, d)
    b = local(dr, s1, d)
    mb.add_quad((a[0], a[1], z0), (b[0], b[1], z0), (b[0], b[1], z1), (a[0], a[1], z1), mat)


def _ceiling(mb, dr, s0, s1, d0, d1, z, mat):
    """下を向く水平の板（天井・庇裏の照明）。"""
    poly = [local(dr, s0, d0), local(dr, s1, d0), local(dr, s1, d1), local(dr, s0, d1)]
    mb.add_ngon_flat(poly, z, mat, flip=True)


def build_one(mb, dr):
    W, H, D, WO, HT = dr["W"], dr["H"], dr["D"], dr["WO"], dr["HT"]
    Z = APRON_Z
    wi = WO - 0.30          # 風除室の側壁の内面
    df = D - 0.25           # 扉の面（枠の見込み 0.25 m だけ引っ込める）

    # 足元: 石張り（明るい石・縁は濃い石）+ 扉の前の黒いマット
    _box(mb, dr, -dr["APRON_S"], dr["APRON_S"], -BURY, dr["APRON_D"], 0.0, Z,
         "stone_dark", top="stone_light")
    _box(mb, dr, -(W + 0.15), W + 0.15, D + 0.15, D + 1.75, Z, Z + 0.015,
         "plastic_black", top="plastic_black")

    # 風除室: 紺の側壁 + 扉まわりの額縁（袖壁と垂れ壁）
    for sg in (-1.0, 1.0):
        _box(mb, dr, sg * wi, sg * WO, -BURY, D, 0.0, HT, "wall_accent_navy")
        _box(mb, dr, sg * W, sg * wi, df, D, 0.0, H, "wall_accent_navy")
    _box(mb, dr, -wi, wi, df, D, H, HT, "wall_accent_navy", bottom="wall_accent_navy")

    # 風除室の中: 黒い奥壁と光る天井（ガラス越しに「中がある」ことが見える）
    _wall_quad(mb, dr, -wi, wi, 0.10, Z, HT, "plastic_black")
    _ceiling(mb, dr, -wi, wi, 0.10, df, H + 0.02, "light_panel")

    # 両開きのガラス扉: 白い枠・中央の召し合わせ・縦の取っ手
    _box(mb, dr, -W, W, df - 0.05, df - 0.01, Z, H, "glass_clear", top="glass_clear")
    fz0, fz1 = Z, H
    for sg in (-1.0, 1.0):
        _box(mb, dr, sg * (W - 0.10), sg * W, df - 0.07, df + 0.02, fz0, fz1, "metal_white")
        _box(mb, dr, sg * 0.20, sg * 0.26, df + 0.02, df + 0.07, 0.85, 1.75, "metal_grey")
    _box(mb, dr, -0.06, 0.06, df - 0.07, df + 0.02, fz0, fz1, "metal_white")
    _box(mb, dr, -W, W, df - 0.07, df + 0.02, fz1 - 0.12, fz1, "metal_white")
    _box(mb, dr, -W, W, df - 0.07, df + 0.02, fz0, fz0 + 0.20, "metal_white")

    # 庇: 白い板に紺の鼻先。庇裏の手前側に照明の帯。CANOPY=False の扉は壁の帯が庇を兼ねる
    if not dr.get("CANOPY", True):
        return
    _box(mb, dr, -dr["CAN_S"], dr["CAN_S"], -BURY, dr["CAN_D"], HT, HT + dr["CAN_T"],
         "wall_accent_navy", top="metal_white", bottom="metal_white")
    _ceiling(mb, dr, -(W + 0.4), W + 0.4, D + 0.25, dr["CAN_D"] - 0.35, HT - 0.005,
             "light_panel")


def build(mb, doors):
    for dr in doors:
        build_one(mb, dr)
    return len(doors)
