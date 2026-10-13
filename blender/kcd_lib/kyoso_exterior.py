"""共創棟の外観。11F・45 m（2024 年竣工）。

上階はグレーの縦フィンの格子、妻面はレンガタイル、1F は公園側が列柱の奥のガラス、モール側が
ガラスとグレーの水平ルーバーの帯（docs/ref/interiors/kyoso/photo_index.md の外観写真）。
店の看板は出さない（#181）。外形は campus.json の手入力の矩形（約 72 × 35.5 m、
tools/osm_extract.py の MANUAL_BUILDINGS）。"""
from . import facade, geom
from .buildings import _roof_boxes

GROUND_H = 5.4      # 1F の階高（2F スラブの上面）。屋内の 1F 天井 4.6〜5.0 の上

# 長手 2 面の縦フィンの格子
FIN_SEG = 1.8       # 縦フィンの間隔（上階の窓割りと同じ）
FIN_DEPTH = 0.7     # フィンと各階の横桟の出
FIN_HALF_W = 0.12   # 縦フィンの半幅
BAR_H = 0.3         # 各階の横桟の高さ（上端が床の高さ）

# 公園側の 1F の軒下
ARCADE = 2.4             # ガラスを列柱の線から引っ込める奥行き
ARCADE_COL_END = 3.0     # 両端の丸柱を妻面の壁から離す距離
ARCADE_COL_INSET = 0.6   # 丸柱を列柱の線から内へ寄せる距離
ARCADE_COL_SPACING = 6.0
ARCADE_COL_R = 0.42

# 妻面（西端）のガラスの階段塔。(u, v) は建物の uv の外接矩形 (u0, v1) からの相対
TOWER_U = (-3.0, 0.2)        # 妻面から外へ 3 m 出す（建物の占有の余白の内側）
TOWER_V = (-13.0, -6.0)      # モール側に寄せる
TOWER_OVER = 2.0             # 屋上の軒より上に出す高さ
TOWER_GLASS_D = -0.10        # ガラスを外面から引っ込める奥行き
TOWER_SILL = 0.9             # 足元の腰壁（facade.add_curtain_wall の ground_sill と同じ）
TOWER_MULLION_SEG = 2.4      # 全高の方立ての間隔
TOWER_MULLION_HALF_W = 0.09
TOWER_MULLION_D = 0.02       # 方立てをガラスの外面より出す量
TOWER_BAND_H = 0.22          # 各階の横枠の高さ
TOWER_CAP_OUT = 0.8          # 塔の頂部の庇の出
TOWER_CAP_H = 0.45

# 屋上
EAVE_OUT = 1.4      # 軒の出
EAVE_H = 0.6        # 軒の厚み
PARAPET_H = 0.9
PARAPET_T = 0.3


def _sides(loop, frame):
    """辺を外向きの向きで分ける。戻り値 (モール側 +v, 公園側 -v, 妻面 ±u の辺番号のリスト)。"""
    mall = park = None
    gables = []
    n = len(loop)
    for i in range(n):
        nu, nv = frame.uv(geom.outward_normal(loop[i], loop[(i + 1) % n]))
        if abs(nv) >= abs(nu):
            if nv > 0:
                mall = i
            else:
                park = i
        else:
            gables.append(i)
    return mall, park, gables


def _fin_grid(mb, loop, i, z0, z1, floor_h, mat):
    """辺 i の前に縦フィンと各階の横桟を立てて格子にする。フィンは上階の窓割りの方立ての位置。

    フィンは正面と両脇の 3 面だけ（天端は軒、足元は最下段の横桟に隠れる）、横桟は正面・天端・
    下端・両端の 5 面。閉じた箱にしないのは、ほかの建物の屋内の近景（kcd_interior.exterior）に
    共創棟が写り込み、三角形の予算を食うため（#281）。"""
    a, b = loop[i], loop[(i + 1) % len(loop)]
    d = geom.sub(b, a)
    L = geom.length(d)
    e = geom.mul(d, 1.0 / L)
    n = (e[1], -e[0])
    D = FIN_DEPTH
    ns = max(1, int(round(L / FIN_SEG)))

    def P(t, z, dd):
        return facade._P(a, e, n, t, z, dd)

    for k in range(ns + 1):
        t = L * k / ns
        t0, t1 = max(0.0, t - FIN_HALF_W), min(L, t + FIN_HALF_W)
        mb.add_quad(P(t0, z0, D), P(t1, z0, D), P(t1, z1, D), P(t0, z1, D), mat)
        mb.add_quad(P(t0, z0, 0), P(t0, z0, D), P(t0, z1, D), P(t0, z1, 0), mat)
        mb.add_quad(P(t1, z0, D), P(t1, z0, 0), P(t1, z1, 0), P(t1, z1, D), mat)
    for f in range(int(round((z1 - z0) / floor_h)) + 1):
        z = min(z1, z0 + f * floor_h)
        za, zb = max(z0, z - BAR_H), max(z0 + BAR_H, z)
        mb.add_quad(P(0, za, D), P(L, za, D), P(L, zb, D), P(0, zb, D), mat)
        mb.add_quad(P(0, zb, D), P(L, zb, D), P(L, zb, 0), P(0, zb, 0), mat)
        mb.add_quad(P(0, za, 0), P(L, za, 0), P(L, za, D), P(0, za, D), mat)
        mb.add_quad(P(0, za, 0), P(0, za, D), P(0, zb, D), P(0, zb, 0), mat)
        mb.add_quad(P(L, za, D), P(L, za, 0), P(L, zb, 0), P(L, zb, D), mat)


def _stair_tower(mb, rect, z0, z1, floor_h, frame_mat, glass_mat):
    """妻面のガラスの階段塔。各面を 1 枚のガラスにして、その前に全高の方立てと各階の横枠を
    貼る（facade.add_glass_tower は窓ごとに見込みを作るので、ここでは使わない。#281）。"""
    rect = geom.ensure_ccw(geom.dedup(rect))
    floors = max(1, int(round((z1 - z0) / floor_h)))
    for i in range(len(rect)):
        a, b = rect[i], rect[(i + 1) % len(rect)]
        d = geom.sub(b, a)
        L = geom.length(d)
        e = geom.mul(d, 1.0 / L)
        n = (e[1], -e[0])

        def P(t, z, dd):
            return facade._P(a, e, n, t, z, dd)

        g = TOWER_GLASS_D
        mb.add_quad(P(0, z0, g), P(L, z0, g), P(L, z1, g), P(0, z1, g), glass_mat)
        # 足元の腰壁（入口以外の地上のガラスを扉に見せない）
        zs = z0 + TOWER_SILL
        mb.add_quad(P(0, z0, 0), P(L, z0, 0), P(L, zs, 0), P(0, zs, 0), frame_mat)
        ns = max(1, int(round(L / TOWER_MULLION_SEG)))
        md = TOWER_MULLION_D
        for k in range(ns + 1):
            t = L * k / ns
            t0, t1 = max(0.0, t - TOWER_MULLION_HALF_W), min(L, t + TOWER_MULLION_HALF_W)
            mb.add_quad(P(t0, z0, md), P(t1, z0, md), P(t1, z1, md), P(t0, z1, md), frame_mat)
        for f in range(1, floors):
            z = z0 + f * floor_h
            mb.add_quad(P(0, z - TOWER_BAND_H, 0), P(L, z - TOWER_BAND_H, 0), P(L, z, 0), P(0, z, 0),
                        frame_mat)
    mb.add_slab(geom.offset_polygon(rect, TOWER_CAP_OUT), z1, z1 + TOWER_CAP_H, frame_mat)


def _arcade(mb, loop, i, zt):
    """公園側の 1F: 列柱の線から ARCADE だけ引っ込めたガラスの面と、その上の軒裏・
    両端の壁・打放しの丸柱。軒下は歩いて入れる。戻り値は、辺 i をガラスの面へ下げたループ。"""
    n = len(loop)
    a, b = loop[i], loop[(i + 1) % n]
    nrm = geom.outward_normal(a, b)
    back = geom.mul(nrm, -ARCADE)
    ai, bi = geom.add(a, back), geom.add(b, back)
    inner = list(loop)
    inner[i], inner[(i + 1) % n] = ai, bi
    # 軒裏（下向き）と、軒下の両端の壁（軒下の側へ向く）
    mb.add_quad((a[0], a[1], zt), (ai[0], ai[1], zt), (bi[0], bi[1], zt), (b[0], b[1], zt),
                "concrete_light")
    mb.add_quad((a[0], a[1], 0.0), (ai[0], ai[1], 0.0), (ai[0], ai[1], zt), (a[0], a[1], zt),
                "concrete_dark")
    mb.add_quad((bi[0], bi[1], 0.0), (b[0], b[1], 0.0), (b[0], b[1], zt), (bi[0], bi[1], zt),
                "concrete_dark")
    # 打放しの丸柱
    e = geom.normalize(geom.sub(b, a))
    inset = geom.mul(nrm, -ARCADE_COL_INSET)
    pa = geom.add(geom.add(a, geom.mul(e, ARCADE_COL_END)), inset)
    pb = geom.add(geom.add(b, geom.mul(e, -ARCADE_COL_END)), inset)
    facade.add_colonnade(mb, pa, pb, 0.0, zt, spacing=ARCADE_COL_SPACING, radius=ARCADE_COL_R,
                         mat="concrete_light", slab=False)
    return inner


def build(mb, b, frame, ctx):
    loop = geom.ensure_ccw(geom.dedup(b["footprint"]))
    u0, v0, u1, v1 = frame.uv_bbox(loop)
    h = b["height"]
    lv = b["levels"]
    g1 = GROUND_H
    fu = (h - g1) / (lv - 1)
    mall, park, gables = _sides(loop, frame)
    longs = [i for i in (mall, park) if i is not None]

    # 上階: 長手の両面は濃いガラスの前にグレーの縦フィンの格子、妻面はレンガタイルに縦長の窓。
    # 長手の窓は各階 1 本の連続窓にし、窓割りはフィンに任せる（方立てはフィンの陰に隠れる）
    facade.add_facade(mb, loop, g1, fu, 0, lv - 1,
                      wall="concrete_light", glass="glass_dark",
                      seg=1000.0, sill=0.85, header=0.55, inset=0.25, mullion=0.20,
                      edges=longs, side_reveals=False)
    for i in longs:
        _fin_grid(mb, loop, i, g1, h, fu, "metal_grey")
    facade.add_facade(mb, loop, g1, fu, 0, lv - 1,
                      wall="brick_red", glass="glass_dark",
                      seg=4.4, sill=1.0, header=0.9, inset=0.30, mullion=2.6,
                      edges=gables, side_reveals=False)

    # 1F: 妻面は濃色の基壇、モール側は全面ガラス（濃色の方立て）の上にグレーの水平ルーバーの帯、
    # 公園側は列柱の奥のガラス
    facade.add_facade(mb, loop, 0.0, g1, 0, 1,
                      wall="concrete_dark", glass="glass_dark",
                      seg=4.4, sill=0.9, header=1.6, inset=0.40, mullion=1.4,
                      edges=gables)
    if mall is not None:
        facade.add_curtain_wall(mb, loop, 0.0, g1, 0, 1, frame="metal_charcoal",
                                glass="glass_clear", seg=3.0, inset=0.25, edges=[mall],
                                ground_sill=0.9)
        facade.add_louvers(mb, loop, g1 - 1.6, 1.6, 0, 1, mat="metal_grey",
                           depth=0.6, thick=0.10, edges=[mall], per_floor=3)
    if park is not None:
        inner = _arcade(mb, loop, park, g1)
        facade.add_curtain_wall(mb, inner, 0.0, g1, 0, 1, frame="metal_charcoal",
                                glass="glass_clear", seg=3.0, inset=0.25, edges=[park],
                                ground_sill=0.9)

    # 妻面（西端）のガラスの階段塔
    tower = frame.rect(u0 + TOWER_U[0], v1 + TOWER_V[0], u0 + TOWER_U[1], v1 + TOWER_V[1])
    _stair_tower(mb, tower, 0.0, h + TOWER_OVER, fu, "metal_charcoal", "glass_clear")

    # 屋上: 深い軒とパラペット、機械室
    eave = geom.offset_polygon(loop, EAVE_OUT)
    mb.add_slab(eave, h, h + EAVE_H, "concrete_light")
    facade.add_parapet(mb, eave, h + EAVE_H, PARAPET_H, PARAPET_T, "metal_grey")
    _roof_boxes(mb, frame, (u0, v0, u1, v1), h + EAVE_H, n=3, du=12.0, dv=14.0, h=3.4)

    # 入口と看板は kcd_lib.entrances が置く。モール側の面の v は自販機の置き場所の予備
    ctx["kyoso_mall_v"] = v1
