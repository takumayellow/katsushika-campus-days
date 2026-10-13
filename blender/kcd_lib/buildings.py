"""campus.json の style ごとの建物生成（DESIGN.md §3.1）。"""

import math
import random
import zlib

from . import facade, geom


def _uv_rect(frame, u0, v0, u1, v1):
    return frame.rect(u0, v0, u1, v1)


def _roof_boxes(mb, frame, uvbb, z, n=3, du=12.0, dv=14.0, h=3.6):
    u0, v0, u1, v1 = uvbb
    vc = (v0 + v1) * 0.5
    dv = min(dv, (v1 - v0) * 0.55)
    for i in range(n):
        t = (i + 0.5) / n
        uc = u0 + (u1 - u0) * t
        loop = _uv_rect(frame, uc - du * 0.5, vc - dv * 0.5, uc + du * 0.5, vc + dv * 0.5)
        mb.add_prism(loop, z, z + h, "concrete_grey", "roof_grey")
        facade.add_parapet(mb, loop, z + h, 0.35, 0.25, "metal_grey")


def _brick_core(mb, frame, uc, v_lo, v_hi, z0, z1, floors, fh, du=8.0):
    """レンガ色の階段コア。長手方向の位置 uc に、外へ 1.2 m 出っ張る塊を置く。"""
    loop = _uv_rect(frame, uc - du * 0.5, v_lo, uc + du * 0.5, v_hi)
    mb.add_slab(loop, z0, z1, "brick_red")
    facade.add_facade(mb, loop, z0, fh, 0, floors,
                      wall="brick_red", glass="glass_dark", seg=2.2,
                      sill=1.5, header=0.9, inset=0.28, mullion=0.5)
    return loop


# --------------------------------------------------------------------------- #
#  第1研究棟 — 11F・49.5 m。水平連続窓 + レンガコア + ピロティ + 屋上機械室
# --------------------------------------------------------------------------- #
def build_lab_tower(mb, b, frame, ctx):
    loop = geom.ensure_ccw(geom.dedup(b["footprint"]))
    h = b["height"]
    lv = b["levels"]
    fh = h / lv
    u0, v0, u1, v1 = frame.uv_bbox(loop)

    facade.add_facade(mb, loop, 0.0, fh, 1, lv,
                      wall="concrete_grey", glass="glass_dark",
                      seg=3.0, sill=1.15, header=0.45, inset=0.45, mullion=0.36)
    facade.add_facade(mb, loop, 0.0, fh, 0, 1,
                      wall="concrete_dark", glass="glass_dark",
                      seg=3.6, sill=0.9, header=0.55, inset=0.55, mullion=0.30)

    # レンガ色の階段コア（南面のみ。北面は共創棟が全長で接していて、出っ張ると
    # 共創棟の躯体に 2.7 m 食い込む）。中央のコアは入口の切り欠き（u 111..127）の口を
    # 塞がないよう東へずらす（#39）
    for t in (0.10, 0.58, 0.90):
        uc = u0 + (u1 - u0) * t
        _brick_core(mb, frame, uc, v0 - 1.4, v0 + 6.0, 0.0, h + 2.2, lv, fh)

    # ガラスのエレベータ塔（南面）
    for t in (0.30, 0.70):
        uc = u0 + (u1 - u0) * t
        rect = _uv_rect(frame, uc - 3.4, v0 - 3.2, uc + 3.4, v0 + 0.4)
        facade.add_glass_tower(mb, rect, 0.0, h + 4.5, fh)

    # 南面のピロティ（柱列 + 庇）
    pa = frame.xy(u0 + 10, v0 - 3.4)
    pb = frame.xy(u1 - 10, v0 - 3.4)
    facade.add_colonnade(mb, pa, pb, 0.0, fh - 0.55, spacing=7.0, radius=0.52,
                         mat="concrete_light", slab=True, slab_depth=4.0,
                         slab_mat="concrete_light")

    mb.add_ngon_flat(loop, h, "roof_grey")
    facade.add_parapet(mb, loop, h, 1.2, 0.45, "concrete_light")
    _roof_boxes(mb, frame, (u0, v0, u1, v1), h + 1.2, n=3, du=14.0, dv=16.0, h=3.8)
    # 入口と看板は kcd_lib.entrances が置く


# --------------------------------------------------------------------------- #
#  共創棟 — 11F・47 m。白ルーバー + ガラス。1F にスタバ / ファミマ
# --------------------------------------------------------------------------- #
def _hidden_edges(loop, self_id, ctx, push=0.4):
    """他棟に接して見えない辺を探す。辺上 3 点を push だけ外へ出し、全部が他棟の
    フットプリント内なら隠れているとみなす。戻り値 {辺番号: 隠している棟の高さ}。"""
    out = {}
    fps = ctx.get("footprints") or []
    n = len(loop)
    for i in range(n):
        a, b = loop[i], loop[(i + 1) % n]
        d = geom.sub(b, a)
        L = geom.length(d)
        if L < 1e-6:
            continue
        e = geom.mul(d, 1.0 / L)
        nrm = (e[1], -e[0])
        pts = [(a[0] + e[0] * L * t + nrm[0] * push, a[1] + e[1] * L * t + nrm[1] * push)
               for t in (0.25, 0.5, 0.75)]
        for fid, fp, fh in fps:
            if fid == self_id:
                continue
            if all(geom.point_in_poly(p, fp) for p in pts):
                out[i] = max(out.get(i, 0.0), fh)
    return out


def build_kyoso(mb, b, frame, ctx):
    # OSM 実測のフットプリントをそのまま使う（6 点・722 m²・奥行 5.6〜6.8 m）。
    loop = geom.ensure_ccw(geom.dedup(b["footprint"]))
    u0, v0, u1, v1 = frame.uv_bbox(loop)
    h = b["height"]
    lv = b["levels"]
    fh = h / lv
    n = len(loop)

    def _edge(i):
        return geom.sub(loop[(i + 1) % n], loop[i])

    # 第1研究棟に接する南辺などは壁を出さない（同一平面で z-fight するだけ）
    hidden = _hidden_edges(loop, b["id"], ctx)
    visible = [i for i in range(n) if i not in hidden]
    louver_edges = [i for i in visible if geom.length(_edge(i)) >= 5.0]

    facade.add_facade(mb, loop, 0.0, fh, 1, lv,
                      wall="louver_white", glass="glass_clear",
                      seg=2.6, sill=0.85, header=0.30, inset=0.32, mullion=0.22,
                      edges=visible)
    facade.add_louvers(mb, loop, 0.0, fh, 1, lv, mat="louver_white",
                       depth=0.6, thick=0.10, edges=louver_edges, per_floor=2)
    # 1F: ラーニングスクエア（全面ガラス）
    facade.add_curtain_wall(mb, loop, 0.0, fh, 0, 1, seg=3.0, inset=0.30, edges=visible,
                            ground_sill=0.9)
    # 隠れた辺は、相手の棟より高い部分だけ壁を出す
    for i, other_h in hidden.items():
        if other_h < h - 0.5:
            facade.add_solid(mb, loop, other_h, h, "louver_white", edges=[i])

    mb.add_ngon_flat(loop, h, "roof_grey")
    facade.add_parapet(mb, loop, h, 1.1, 0.4, "louver_white", edges=visible)
    _roof_boxes(mb, frame, (u0, v0, u1, v1), h + 1.1, n=2, du=10.0, dv=4.0, h=3.2)

    # 入口と看板は kcd_lib.entrances が置く
    ctx["kyoso_mall_v"] = v1
    ctx["kyoso_rect"] = loop


# --------------------------------------------------------------------------- #
#  講義棟 — 7F・30 m。南西角が曲面でガラスの大階段ホール
# --------------------------------------------------------------------------- #
def build_lecture(mb, b, frame, ctx):
    loop = geom.ensure_ccw(geom.dedup(b["footprint"]))
    # (-u, -v) 側の角＝南西角を探して円弧に置き換える
    best = min(range(len(loop)), key=lambda i: sum(frame.uv(loop[i])))
    ARC = 12
    loop = geom.fillet_vertex(loop, best, 18.0, ARC)
    arc_edges = list(range(best, best + ARC))
    other = [i for i in range(len(loop)) if i not in arc_edges]

    h = b["height"]
    lv = b["levels"]
    fh = h / lv
    u0, v0, u1, v1 = frame.uv_bbox(loop)

    facade.add_facade(mb, loop, 0.0, fh, 1, lv,
                      wall="concrete_grey", glass="glass_dark",
                      seg=3.0, sill=1.15, header=0.45, inset=0.45, mullion=0.36,
                      edges=other)
    facade.add_facade(mb, loop, 0.0, fh, 0, 1,
                      wall="concrete_dark", glass="glass_clear",
                      seg=3.6, sill=0.9, header=0.55, inset=0.5, mullion=0.28,
                      edges=other)
    # 曲面部＝ガラスの階段ホール（全層吹き抜け。1F は腰壁を立てて扉に見えないようにする）
    facade.add_curtain_wall(mb, loop, 0.0, fh, 0, lv, seg=2.4, edges=arc_edges, inset=0.18,
                            ground_sill=0.9)

    for t in (0.22, 0.78):
        uc = u0 + (u1 - u0) * t
        _brick_core(mb, frame, uc, v1 - 6.0, v1 + 1.3, 0.0, h + 2.0, lv, fh)

    mb.add_ngon_flat(loop, h, "roof_grey")
    facade.add_parapet(mb, loop, h, 1.15, 0.4, "concrete_light")
    _roof_boxes(mb, frame, (u0, v0, u1, v1), h + 1.15, n=2, du=12.0, dv=13.0, h=3.4)
    # 入口と看板は kcd_lib.entrances が置く


# --------------------------------------------------------------------------- #
#  第2研究棟 — 6F・25 m。1〜2F は食堂の大きなガラス面 + 西へ張り出す屋上緑化低層部
# --------------------------------------------------------------------------- #
def build_office(mb, b, frame, ctx):
    loop = geom.ensure_ccw(geom.dedup(b["footprint"]))
    h = b["height"]
    lv = b["levels"]
    fh = h / lv
    u0, v0, u1, v1 = frame.uv_bbox(loop)

    facade.add_facade(mb, loop, 0.0, fh, 2, lv,
                      wall="concrete_grey", glass="glass_dark",
                      seg=2.9, sill=1.10, header=0.45, inset=0.42, mullion=0.34)
    # 1〜2F の食堂のガラス面。1F は腰壁を高くして扉に見えないようにする（#39）
    for f, sill in ((0, 0.9), (1, 0.55)):
        facade.add_facade(mb, loop, 0.0, fh, f, f + 1,
                          wall="concrete_light", glass="glass_clear",
                          seg=3.4, sill=sill, header=0.40, inset=0.45, mullion=0.26)

    for t in (0.18, 0.80):
        uc = u0 + (u1 - u0) * t
        _brick_core(mb, frame, uc, v1 - 5.5, v1 + 1.2, 0.0, h + 1.8, lv, fh)

    # 西へ張り出す低層部（屋上緑化）
    wing = _uv_rect(frame, u0 - 19.0, v0 + 10.0, u0 + 1.0, v1 - 1.0)
    mb.add_slab(wing, 0.0, 7.2, "concrete_light")
    facade.add_curtain_wall(mb, wing, 0.0, 3.6, 0, 2, seg=3.0, inset=0.25, ground_sill=0.9)
    mb.add_ngon_flat(geom.offset_polygon(wing, -0.6), 7.35, "grass")
    facade.add_parapet(mb, wing, 7.2, 0.7, 0.35, "concrete_light")

    mb.add_ngon_flat(loop, h, "roof_grey")
    facade.add_parapet(mb, loop, h, 1.1, 0.4, "concrete_light")
    _roof_boxes(mb, frame, (u0, v0, u1, v1), h + 1.1, n=2, du=9.0, dv=10.0, h=3.0)
    # 入口と看板は kcd_lib.entrances が置く


# --------------------------------------------------------------------------- #
#  図書館 — 5F・22 m。深い軒の大屋根 + 細柱 + 八角ドーム + 全面ガラス
# --------------------------------------------------------------------------- #
def build_library(mb, b, frame, ctx):
    loop = geom.ensure_ccw(geom.dedup(b["footprint"]))
    h = b["height"]
    lv = b["levels"]
    fh = h / lv
    u0, v0, u1, v1 = frame.uv_bbox(loop)

    # 躯体：全面ガラスのカーテンウォール。1F はさらに深く引っ込ませてピロティ感を出す
    facade.add_curtain_wall(mb, loop, 0.0, fh, 1, lv, seg=2.6, inset=0.16)
    inner1 = geom.offset_polygon(loop, -2.2)
    facade.add_curtain_wall(mb, inner1, 0.0, fh, 0, 1, seg=3.0, inset=0.20,
                            glass="glass_dark", ground_sill=0.9)
    mb.add_ngon_flat(loop, fh, "concrete_light")  # ピロティの天井 / 2F 床

    # 深い軒の大屋根（7 m 跳ね出し・見付 2.2 m の厚い版）
    eave = geom.offset_polygon(loop, 7.0)
    mb.add_prism(eave, h, h + 2.2, "concrete_light", "roof_grey")
    mb.add_ngon_flat(eave, h, "concrete_grey", flip=True)   # 軒裏
    # 軒先の水切り（薄い陰の線）
    mb.add_prism(geom.offset_polygon(eave, -0.35), h - 0.30, h, "concrete_grey")
    # 軒を支える細柱（外周から少し内側に立てる）
    cen = geom.centroid(eave)
    n = len(eave)
    for i in range(n):
        a, bb = eave[i], eave[(i + 1) % n]
        L = geom.length(geom.sub(bb, a))
        steps = max(1, int(round(L / 9.0)))
        for k in range(steps):
            p = geom.lerp(geom.lerp(a, bb, (k + 0.5) / steps), cen, 0.035)
            mb.add_cylinder(p[0], p[1], 0.0, h, 0.30, "concrete_light",
                            seg=10, cap_top=False)

    # 屋上の八角ドーム（3・4F の 600 席大ホール）
    cx, cy = frame.xy(u0 + (u1 - u0) * 0.74, (v0 + v1) * 0.5 + 6.0)
    R = 18.5
    oct8 = [(cx + R * math.cos(math.pi / 8 + 2 * math.pi * i / 8),
             cy + R * math.sin(math.pi / 8 + 2 * math.pi * i / 8)) for i in range(8)]
    mb.add_prism(oct8, h + 2.2, h + 3.0, "concrete_light")
    # ドラムのハイサイドライト（大ホールへの採光）
    facade.add_curtain_wall(mb, oct8, h + 3.0, 3.2, 0, 1, seg=3.4, inset=0.22)
    RINGS = 6
    DOME_H = 10.0
    base_z = h + 6.2
    prev = [(cx + R * math.cos(math.pi / 8 + 2 * math.pi * i / 8),
             cy + R * math.sin(math.pi / 8 + 2 * math.pi * i / 8), base_z)
            for i in range(8)]
    for k in range(1, RINGS + 1):
        ang = (math.pi * 0.5) * k / RINGS
        r = R * math.cos(ang)
        z = base_z + DOME_H * math.sin(ang)
        cur = [(cx + r * math.cos(math.pi / 8 + 2 * math.pi * i / 8),
                cy + r * math.sin(math.pi / 8 + 2 * math.pi * i / 8), z) for i in range(8)]
        for i in range(8):
            j = (i + 1) % 8
            mb.add_quad(prev[i], prev[j], cur[j], cur[i], "metal_white")
        prev = cur

    # 入口と看板は kcd_lib.entrances が置く（東面・メインストリートの突き当たり）
    ctx["library_uv"] = (u0, v0, u1, v1)


# --------------------------------------------------------------------------- #
#  体育館 — アリーナの大壁と部室の棟を 1 棟にした 6 階建て（kcd_lib/gym_exterior.py）
# --------------------------------------------------------------------------- #
def build_gym(mb, b, frame, ctx):
    from . import gym_exterior
    gym_exterior.build(mb, b, frame, ctx)


# --------------------------------------------------------------------------- #
#  実験棟 — 格子窓 + レンガコア + 屋上排気ダクト（第2実験棟）。第1実験棟は build_lab1
# --------------------------------------------------------------------------- #
def build_lab_low(mb, b, frame, ctx):
    if b["id"] == "lab1":
        build_lab1(mb, b, frame, ctx)
        return
    loop = geom.ensure_ccw(geom.dedup(b["footprint"]))
    h = b["height"]
    lv = max(1, b["levels"] or 2)
    fh = h / lv
    u0, v0, u1, v1 = frame.uv_bbox(loop)

    facade.add_facade(mb, loop, 0.0, fh, 0, lv,
                      wall="concrete_grey", glass="glass_dark",
                      seg=2.6, sill=1.15, header=0.45, inset=0.40, mullion=0.34)
    _brick_core(mb, frame, u0 + (u1 - u0) * 0.12, v1 - 5.0, v1 + 1.2, 0.0, h + 1.6, lv, fh,
                du=6.5)
    mb.add_ngon_flat(loop, h, "roof_grey")
    facade.add_parapet(mb, loop, h, 0.9, 0.35, "concrete_light")
    for t in (0.35, 0.55, 0.75):
        p = frame.xy(u0 + (u1 - u0) * t, (v0 + v1) * 0.5)
        mb.add_cylinder(p[0], p[1], h + 0.9, h + 4.2, 0.85, "metal_grey", seg=10)
    # 入口と看板は kcd_lib.entrances が置く


# --------------------------------------------------------------------------- #
#  第1実験棟 — 4F・17 m。東西はレンガの壁、南北は白い縦フィンの格子窓。
#  東面に外階段の切り込み、西に 1 階建ての別棟（docs/ref/exterior/lab1.md）
# --------------------------------------------------------------------------- #
LAB1_ANNEX_JOIN = ((41.5, 51.5), (41.7, 46.2))   # 別棟と本体が接する 2 頂点（uv、北・南）
LAB1_JOIN_TOL = 1.0
LAB1_ANNEX_H = 4.6       # 別棟の屋根の高さ（1 階分）
LAB1_PARAPET = 1.0       # 本体のパラペットの高さ
LAB1_WIN_SEG = 2.4       # 南北の窓の割り付け
LAB1_FIN_PITCH = 1.2     # 南北の白い縦フィンの間隔
LAB1_FIN_D = 0.40        # フィンの出
LAB1_FIN_W = 0.16        # フィンの見付
LAB1_SLOT_T = 0.55       # 東面の階段の切り込みの位置（南端からの比）
LAB1_SLOT_W = 3.4        # 切り込みの幅
LAB1_SLOT_D = 1.6        # 切り込みの奥行
LAB1_LANDING_D = 1.5     # 階段の踊り場の張り出し
LAB1_CANOPY_D = 2.0      # 南面の 2 階の床の高さの庇の出
LAB1_CANOPY_GAP = 3.8    # 入口の庇と重ならないよう、扉の中心から庇を切る半幅


def _lab1_split(loop, frame):
    """外形を本体と西の別棟に分ける。戻り値 (本体, 別棟)。どちらも反時計回りで、
    本体の最後の辺と別棟の最後の辺が、両者の接する壁（同じ 2 頂点）になる。"""
    if geom.poly_area(loop) <= 0:
        raise ValueError("lab1: 外形が反時計回りでない（本体と別棟が入れ替わる）")

    def _find(target):
        best = min(range(len(loop)), key=lambda i: math.dist(frame.uv(loop[i]), target))
        if math.dist(frame.uv(loop[best]), target) > LAB1_JOIN_TOL:
            raise ValueError("lab1: 別棟の接点 %s が外形に見つからない" % (target,))
        return best

    jn, js = (_find(t) for t in LAB1_ANNEX_JOIN)
    n = len(loop)
    main = [loop[(js + k) % n] for k in range((jn - js) % n + 1)]
    annex = [loop[(jn + k) % n] for k in range((js - jn) % n + 1)]
    if len(annex) < 3 or len(main) < 4:
        raise ValueError("lab1: 外形を本体と別棟に分けられない")
    return main, annex


def _lab1_facing(frame, a, b):
    """辺 a→b の外向きの向き（"n" / "s" / "e" / "w"）。"""
    nrm = geom.outward_normal(a, b)
    nu, nv = geom.dot(nrm, frame.u), geom.dot(nrm, frame.v)
    if abs(nv) >= abs(nu):
        return "n" if nv > 0 else "s"
    return "e" if nu > 0 else "w"


def _lab1_edge(a, b):
    d = geom.sub(b, a)
    L = geom.length(d)
    e = geom.mul(d, 1.0 / L)
    return e, (e[1], -e[0]), L


def _lab1_wall(mb, a, b, z0, z1, mat):
    """a→b の右（外）を向く壁。"""
    mb.add_quad((a[0], a[1], z0), (b[0], b[1], z0), (b[0], b[1], z1), (a[0], a[1], z1), mat)


def _lab1_local_rect(p, e, n, t0, t1, d0, d1):
    """辺のローカル座標（t = 辺に沿う距離、d = 外への距離）の矩形を XY の 4 点にする。"""
    return [facade._P(p, e, n, t, 0.0, d)[:2] for t, d in ((t0, d0), (t1, d0), (t1, d1), (t0, d1))]


def _lab1_fins(mb, a, b, z0, z1):
    """格子窓の前に白い縦フィンを LAB1_FIN_PITCH ごとに立てる（辺の両端は角と干渉するので除く）。"""
    e, n, L = _lab1_edge(a, b)
    k = int(L / LAB1_FIN_PITCH)
    if k < 2:
        return
    step = L / k
    hw = LAB1_FIN_W * 0.5
    for i in range(1, k):
        t = i * step
        p0, p1, p2, p3 = _lab1_local_rect(a, e, n, t - hw, t + hw, 0.0, LAB1_FIN_D)
        # p0 → p3 → p2 → p1 の順に、外から見える 3 面（両脇と正面）を張る
        _lab1_wall(mb, p0, p3, z0, z1, "louver_white")
        _lab1_wall(mb, p3, p2, z0, z1, "louver_white")
        _lab1_wall(mb, p2, p1, z0, z1, "louver_white")


def _lab1_grid(mb, poly, i, fh, lv):
    """南北の面（poly の辺 i）: 白い床の帯 + 暗いガラス、2 階から上に白い縦フィン。"""
    kw = dict(wall="louver_white", glass="glass_dark", seg=LAB1_WIN_SEG, header=0.6,
              inset=0.35, mullion=0.22, edges=[i], side_reveals=False)
    # 1 階は腰壁を高くして扉に見えないようにする（#39）
    facade.add_facade(mb, poly, 0.0, fh, 0, 1, sill=0.9, **kw)
    facade.add_facade(mb, poly, 0.0, fh, 1, lv, sill=0.45, **kw)
    _lab1_fins(mb, poly[i], poly[(i + 1) % len(poly)], fh, fh * lv)


def _lab1_stair_slot(mb, a, b, fh, lv):
    """東面: レンガの 2 つの塊の間に外階段の切り込み。2 階から上を LAB1_SLOT_D 引っ込め、
    各階の床の高さに踊り場を張り出す。1 階は外形どおりの壁（当たり判定を変えない）。"""
    e, n, L = _lab1_edge(a, b)
    h = fh * lv
    t0 = L * LAB1_SLOT_T
    t1 = t0 + LAB1_SLOT_W
    dz = -LAB1_SLOT_D

    def pt(t, d):
        return facade._P(a, e, n, t, 0.0, d)[:2]

    def rect(r0, r1, d0, d1):
        return _lab1_local_rect(a, e, n, r0, r1, d0, d1)

    _lab1_wall(mb, pt(0.0, 0.0), pt(t0, 0.0), 0.0, h, "brick_red")
    _lab1_wall(mb, pt(t1, 0.0), pt(L, 0.0), 0.0, h, "brick_red")
    # 1 階: 切り込みの下は明るい壁に小窓
    _lab1_wall(mb, pt(t0, 0.0), pt(t1, 0.0), 0.0, fh, "concrete_light")
    _lab1_wall(mb, pt(t0 + 0.6, 0.02), pt(t1 - 0.6, 0.02), 1.2, 3.2, "glass_dark")
    # 切り込みの両脇（レンガの塊の小口）と奥の壁、上の天井
    _lab1_wall(mb, pt(t0, 0.0), pt(t0, dz), fh, h, "brick_red")
    _lab1_wall(mb, pt(t1, dz), pt(t1, 0.0), fh, h, "brick_red")
    _lab1_wall(mb, pt(t0, dz), pt(t1, dz), fh, h, "concrete_light")
    mb.add_ngon_flat(rect(t0, t1, dz, 0.0), h, "concrete_light", flip=True)
    for f in range(1, lv):
        z = f * fh
        # 階段室の窓（奥の壁）
        _lab1_wall(mb, pt(t0 + 0.5, dz + 0.02), pt(t1 - 0.5, dz + 0.02), z + 0.9, z + 3.4,
                   "glass_dark")
        # 踊り場の床版と、コンクリートの手すり壁（正面と両脇）
        mb.add_slab(rect(t0, t1, dz, LAB1_LANDING_D), z - 0.25, z, "concrete_light")
        rails = ((t0, t1, LAB1_LANDING_D - 0.2, LAB1_LANDING_D),
                 (t0, t0 + 0.2, 0.0, LAB1_LANDING_D - 0.2),
                 (t1 - 0.2, t1, 0.0, LAB1_LANDING_D - 0.2))
        for r0, r1, d0, d1 in rails:
            mb.add_prism(rect(r0, r1, d0, d1), z, z + 1.1, "concrete_light", "concrete_light")


def _lab1_door_t(a, b, frame, bid="lab1"):
    """入口（kcd_lib.entrances.DOORS の bid）の探索線が辺 a→b に当たる位置（辺に沿う距離）。"""
    from . import entrances
    for door, (pu, pv), (du, dv), *_rest in entrances.DOORS:
        if door != bid:
            continue
        o = frame.xy(pu, pv)
        d = frame.xy(du, dv)
        e, _n, L = _lab1_edge(a, b)
        den = geom.cross(d, e)
        if abs(den) < 1e-9:
            return None
        t = geom.cross(geom.sub(a, o), d) / den
        return t if 0.0 <= t <= L else None
    return None


def _lab1_canopy(mb, a, b, z, door_t):
    """南面の 2 階の床の高さの庇。入口の庇（kcd_lib.entrances）の上だけ切る。"""
    e, n, L = _lab1_edge(a, b)
    spans = [(0.0, L)]
    if door_t is not None:
        spans = [(0.0, door_t - LAB1_CANOPY_GAP), (door_t + LAB1_CANOPY_GAP, L)]
    for t0, t1 in spans:
        if t1 - t0 > 1.0:
            mb.add_slab(_lab1_local_rect(a, e, n, t0, t1, 0.0, LAB1_CANOPY_D), z - 0.3, z,
                        "concrete_light")


def _lab1_roof_units(mb, frame, uvbb, z):
    """屋上の設備（空中写真の配置）: 北の縁の長い箱、南寄りの白い長い箱と中くらいの箱、
    室外機の列（西と東）、北西の丸い機器。"""
    u0, v0, u1, v1 = uvbb
    boxes = [
        (u0 + 5.0, v1 - 3.8, u1 - 4.0, v1 - 1.4, 2.0, "metal_white"),
        (u0 + 12.0, v0 + 2.0, u0 + 23.0, v0 + 4.6, 2.4, "metal_white"),
        (u0 + 15.0, v0 + 6.0, u0 + 20.0, v0 + 8.6, 2.0, "metal_white"),
    ]
    for r in range(2):
        for c in range(5):
            uc = u0 + 4.0 + c * 1.7
            vc = v1 - 10.0 - r * 2.4
            boxes.append((uc - 0.6, vc - 0.5, uc + 0.6, vc + 0.5, 1.4, "metal_grey"))
    for r in range(3):
        for c in range(4):
            uc = u1 - 10.0 + c * 2.0
            vc = v0 + 3.5 + r * 3.0
            boxes.append((uc - 0.7, vc - 0.6, uc + 0.7, vc + 0.6, 1.6, "metal_grey"))
    for bu0, bv0, bu1, bv1, bh, mat in boxes:
        mb.add_prism(_uv_rect(frame, bu0, bv0, bu1, bv1), z, z + bh, mat, "metal_white")
    cx, cy = frame.xy(u0 + 5.5, v1 - 6.5)
    mb.add_cylinder(cx, cy, z, z + 1.4, 1.6, "metal_white", seg=14)


def build_lab1(mb, b, frame, ctx):
    loop = geom.ensure_ccw(geom.dedup(b["footprint"]))
    h = b["height"]
    lv = max(1, b["levels"] or 4)
    fh = h / lv
    main, annex = _lab1_split(loop, frame)

    # 本体。最後の辺は別棟と接する壁（別棟の屋根より上だけ見える）
    nm = len(main)
    def _longest(side):
        return max((i for i in range(nm - 1) if _lab1_facing(frame, main[i], main[i + 1]) == side),
                   key=lambda i: math.dist(main[i], main[i + 1]))

    long_east, long_south = _longest("e"), _longest("s")
    brick, grid = [], []
    for i in range(nm):
        a, c = main[i], main[(i + 1) % nm]
        side = _lab1_facing(frame, a, c)
        if i == nm - 1:
            _lab1_wall(mb, a, c, LAB1_ANNEX_H, h, "brick_red")
            brick.append(i)
        elif i == long_east:
            _lab1_stair_slot(mb, a, c, fh, lv)
            brick.append(i)
        elif side in ("e", "w"):
            _lab1_wall(mb, a, c, 0.0, h, "brick_red")
            brick.append(i)
        else:
            _lab1_grid(mb, main, i, fh, lv)
            grid.append(i)
            if i == long_south:
                _lab1_canopy(mb, a, c, fh, _lab1_door_t(a, c, frame))
    mb.add_ngon_flat(main, h, "roof_grey")
    facade.add_parapet(mb, main, h, LAB1_PARAPET, 0.35, "louver_white", edges=grid)
    facade.add_parapet(mb, main, h, LAB1_PARAPET, 0.35, "brick_red", edges=brick)
    _lab1_roof_units(mb, frame, frame.uv_bbox(main), h)

    # 西の別棟（1 階建て）。最後の辺は本体と接するので壁を出さない
    outer = list(range(len(annex) - 1))
    facade.add_facade(mb, annex, 0.0, LAB1_ANNEX_H - 0.35, 0, 1,
                      wall="concrete_grey", glass="glass_dark", seg=2.6,
                      sill=1.15, header=0.45, inset=0.40, mullion=0.34, edges=outer)
    facade.add_solid(mb, annex, LAB1_ANNEX_H - 0.35, LAB1_ANNEX_H, "concrete_grey", edges=outer)
    mb.add_ngon_flat(annex, LAB1_ANNEX_H, "roof_grey")
    facade.add_parapet(mb, annex, LAB1_ANNEX_H, 0.6, 0.3, "concrete_light", edges=outer)
    # 入口と看板は kcd_lib.entrances が置く（南面）


# --------------------------------------------------------------------------- #
#  温室 — 白フレームの陸屋根のガラス箱（L 字。北東の角が欠ける）
# --------------------------------------------------------------------------- #
GH_FASCIA = 0.25     # 屋根の縁の白い鼻隠しの高さ（上端が建物の高さ）
GH_BAR_U = 1.25      # 屋根ガラスの桟の間隔（u 方向）
GH_BAR_V = 1.8       # 屋根ガラスの桟の間隔（v 方向）
GH_BAR_W = 0.07      # 桟の幅


def _gh_roof_bars(mb, frame, rects, z):
    """屋根ガラスの上の白い桟。rects は uv の矩形 (u0, v0, u1, v1) の列。"""
    hw = GH_BAR_W * 0.5
    for u0, v0, u1, v1 in rects:
        n = max(1, int(round((u1 - u0) / GH_BAR_U)))
        for i in range(1, n):
            u = u0 + (u1 - u0) * i / n
            mb.add_prism(_uv_rect(frame, u - hw, v0, u + hw, v1), z, z + 0.06,
                         "metal_white", "metal_white")
        m = max(1, int(round((v1 - v0) / GH_BAR_V)))
        for j in range(1, m):
            v = v0 + (v1 - v0) * j / m
            mb.add_prism(_uv_rect(frame, u0, v - hw, u1, v + hw), z, z + 0.06,
                         "metal_white", "metal_white")


def build_greenhouse(mb, b, frame, ctx):
    loop = geom.ensure_ccw(geom.dedup(b["footprint"]))
    bbox = frame.uv_bbox(loop)
    notch = frame.uv_notch(loop)
    uvs = geom.l_rect_uv(bbox, notch)
    shape = [frame.xy(u, v) for u, v in uvs]
    top = b["height"]
    wall_h = top - GH_FASCIA
    facade.add_curtain_wall(mb, shape, 0.0, wall_h, 0, 1, seg=1.0, inset=0.08,
                            ground_sill=0.5)
    mb.add_ngon_flat(shape, wall_h, "glass_clear")
    u0, v0, u1, v1 = bbox
    if notch is None:
        rects = [bbox]
    else:
        # 欠けた角の v の帯と、残りの帯に分ける（どちらも矩形）
        nv = notch[1] if notch[3] >= v1 - 1e-6 else notch[3]
        lo, hi = (v0, nv), (nv, v1)
        cut = hi if notch[3] >= v1 - 1e-6 else lo
        full = lo if cut is hi else hi
        cu0, cu1 = (u0, notch[0]) if notch[2] >= u1 - 1e-6 else (notch[2], u1)
        rects = [(u0, full[0], u1, full[1]), (cu0, cut[0], cu1, cut[1])]
    _gh_roof_bars(mb, frame, rects, wall_h)
    facade.add_parapet(mb, shape, wall_h, GH_FASCIA, 0.12, "metal_white")
    # 入口は kcd_lib.entrances が置く（西面）


# --------------------------------------------------------------------------- #
#  学生寮 — 6F・簡易バルコニー
# --------------------------------------------------------------------------- #
def build_dormitory(mb, b, frame, ctx):
    loop = geom.ensure_ccw(geom.dedup(b["footprint"]))
    h = b["height"]
    lv = max(1, b["levels"] or 6)
    fh = h / lv
    facade.add_facade(mb, loop, 0.0, fh, 0, lv,
                      wall="concrete_light", glass="glass_dark",
                      seg=3.2, sill=1.0, header=0.5, inset=0.55, mullion=0.35)
    mb.add_ngon_flat(loop, h, "roof_grey")
    facade.add_parapet(mb, loop, h, 0.9, 0.3, "concrete_light")


# --------------------------------------------------------------------------- #
#  周辺の家・ビル — 押し出しのみ
# --------------------------------------------------------------------------- #
BG_FLOOR_H = 3.1
BG_WIN_W = 1.3
BG_WIN_H = 1.2
BG_WIN_SILL = 1.0
BG_MIN_AREA = 40.0
BG_MIN_H = 4.5


def _bg_window_slots(loop, h, spacing):
    """辺ごとの (辺番号, 窓数) と階数。窓を貼る価値のない小屋は空を返す。"""
    if len(loop) < 3 or abs(geom.poly_area(loop)) < BG_MIN_AREA or h < BG_MIN_H:
        return [], 0
    floors = max(1, int(h // BG_FLOOR_H))
    slots = []
    n = len(loop)
    for i in range(n):
        L = geom.length(geom.sub(loop[(i + 1) % n], loop[i]))
        if L >= 3.0:
            slots.append((i, max(1, int((L - 1.0) / spacing))))
    return slots, floors


def bg_window_count(b, spacing):
    loop = geom.ensure_ccw(geom.dedup(b["footprint"]))
    h = max(2.5, b.get("height") or 8.0)
    slots, floors = _bg_window_slots(loop, h, spacing)
    return sum(k for _, k in slots) * floors


def _bg_windows(mb, loop, h, spacing, glass="glass_dark"):
    """押し出し壁の外側 4 cm に暗いガラスの四角を並べる（1 窓 = 四角 1 枚 = 三角 2）。"""
    slots, floors = _bg_window_slots(loop, h, spacing)
    n = len(loop)
    count = 0
    for i, k in slots:
        a, b = loop[i], loop[(i + 1) % n]
        d = geom.sub(b, a)
        L = geom.length(d)
        e = geom.mul(d, 1.0 / L)
        nrm = (e[1] * 0.04, -e[0] * 0.04)
        for f in range(floors):
            zb = f * BG_FLOOR_H + BG_WIN_SILL
            zt = zb + BG_WIN_H
            if zt > h - 0.3:
                break
            for j in range(k):
                tc = L * 0.5 + (j - (k - 1) * 0.5) * spacing
                t0, t1 = tc - BG_WIN_W * 0.5, tc + BG_WIN_W * 0.5
                p0 = (a[0] + e[0] * t0 + nrm[0], a[1] + e[1] * t0 + nrm[1])
                p1 = (a[0] + e[0] * t1 + nrm[0], a[1] + e[1] * t1 + nrm[1])
                mb.add_quad((p0[0], p0[1], zb), (p1[0], p1[1], zb),
                            (p1[0], p1[1], zt), (p0[0], p0[1], zt), glass)
                count += 1
    return count


def build_background(mb, b, ctx):
    loop = geom.ensure_ccw(geom.dedup(b["footprint"]))
    if len(loop) < 3 or abs(geom.poly_area(loop)) < 4.0:
        return
    h = max(2.5, b.get("height") or 8.0)
    # hash() はプロセスごとに種が変わるので色が毎回変わる。crc32 で固定する
    idx = zlib.crc32(b["id"].encode("utf-8")) % 6
    mb.add_prism(loop, 0.0, h, "bg_wall_%d" % idx, "roof_grey")
    spacing = ctx.get("bg_window_spacing")
    if spacing:
        ctx["bg_window_quads"] = ctx.get("bg_window_quads", 0) + _bg_windows(mb, loop, h, spacing)


BUILDERS = {
    "lab_tower": build_lab_tower,
    "kyoso": build_kyoso,
    "lecture": build_lecture,
    "office": build_office,
    "library": build_library,
    "gym": build_gym,
    "lab_low": build_lab_low,
    "greenhouse": build_greenhouse,
    "dormitory": build_dormitory,
}
