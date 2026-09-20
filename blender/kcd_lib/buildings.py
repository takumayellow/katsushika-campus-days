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
                      seg=3.6, sill=0.55, header=0.55, inset=0.55, mullion=0.30)

    # レンガ色の階段コア（南面のみ。北面は共創棟が全長で接していて、出っ張ると
    # 共創棟の躯体に 2.7 m 食い込む）
    for t in (0.10, 0.50, 0.90):
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
    ctx["entrance"].append((b["id"], frame.xy((u0 + u1) * 0.5, v0 - 6.5), 0.0))
    ctx["sign"].append((b["id"], frame.xy((u0 + u1) * 0.5, v0 - 6.5), 3.4))


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
    facade.add_curtain_wall(mb, loop, 0.0, fh, 0, 1, seg=3.0, inset=0.30, edges=visible)
    # 隠れた辺は、相手の棟より高い部分だけ壁を出す
    for i, other_h in hidden.items():
        if other_h < h - 0.5:
            facade.add_solid(mb, loop, other_h, h, "louver_white", edges=[i])

    # 店舗サイン（色板。文字は Unity 側 TMP）。モール側（+v）の 1F 開口の上に帯状に貼る
    sb_u = u0 + (u1 - u0) * 0.30
    fm_u = u0 + (u1 - u0) * 0.58
    z0 = fh - 1.75
    z1 = fh - 0.15

    def _plate(uc, hw, za, zb, mat, out=0.34):
        a = frame.xy(uc - hw, v1 + out)
        bb = frame.xy(uc + hw, v1 + out)
        mb.add_quad((a[0], a[1], za), (bb[0], bb[1], za),
                    (bb[0], bb[1], zb), (a[0], a[1], zb), mat)

    _plate(sb_u, 4.6, z0, z1, "sign_starbucks_green")
    _plate(fm_u, 4.6, z0, z1, "sign_familymart_white")
    _plate(fm_u, 4.6, z0, z0 + 0.40, "sign_familymart_green", out=0.40)
    _plate(fm_u, 4.6, z1 - 0.40, z1, "sign_familymart_blue", out=0.40)

    mb.add_ngon_flat(loop, h, "roof_grey")
    facade.add_parapet(mb, loop, h, 1.1, 0.4, "louver_white", edges=visible)
    _roof_boxes(mb, frame, (u0, v0, u1, v1), h + 1.1, n=2, du=10.0, dv=4.0, h=3.2)

    ctx["entrance"].append((b["id"], frame.xy(sb_u - 8.0, v1 + 4.0), 0.0))
    ctx["sign"].append((b["id"], frame.xy(sb_u - 8.0, v1 + 4.0), 3.4))
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
                      seg=3.6, sill=0.45, header=0.55, inset=0.5, mullion=0.28,
                      edges=other)
    # 曲面部＝ガラスの階段ホール（全層吹き抜け）
    facade.add_curtain_wall(mb, loop, 0.0, fh, 0, lv, seg=2.4, edges=arc_edges, inset=0.18)

    for t in (0.22, 0.78):
        uc = u0 + (u1 - u0) * t
        _brick_core(mb, frame, uc, v1 - 6.0, v1 + 1.3, 0.0, h + 2.0, lv, fh)

    mb.add_ngon_flat(loop, h, "roof_grey")
    facade.add_parapet(mb, loop, h, 1.15, 0.4, "concrete_light")
    _roof_boxes(mb, frame, (u0, v0, u1, v1), h + 1.15, n=2, du=12.0, dv=13.0, h=3.4)

    ent = frame.xy(u0 + (u1 - u0) * 0.40, v0 - 4.0)
    ctx["entrance"].append((b["id"], ent, 0.0))
    ctx["sign"].append((b["id"], ent, 3.4))


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
    facade.add_facade(mb, loop, 0.0, fh, 0, 2,
                      wall="concrete_light", glass="glass_clear",
                      seg=3.4, sill=0.55, header=0.40, inset=0.45, mullion=0.26)

    for t in (0.18, 0.80):
        uc = u0 + (u1 - u0) * t
        _brick_core(mb, frame, uc, v1 - 5.5, v1 + 1.2, 0.0, h + 1.8, lv, fh)

    # 西へ張り出す低層部（屋上緑化）
    wing = _uv_rect(frame, u0 - 19.0, v0 + 10.0, u0 + 1.0, v1 - 1.0)
    mb.add_slab(wing, 0.0, 7.2, "concrete_light")
    facade.add_curtain_wall(mb, wing, 0.0, 3.6, 0, 2, seg=3.0, inset=0.25)
    mb.add_ngon_flat(geom.offset_polygon(wing, -0.6), 7.35, "grass")
    facade.add_parapet(mb, wing, 7.2, 0.7, 0.35, "concrete_light")

    mb.add_ngon_flat(loop, h, "roof_grey")
    facade.add_parapet(mb, loop, h, 1.1, 0.4, "concrete_light")
    _roof_boxes(mb, frame, (u0, v0, u1, v1), h + 1.1, n=2, du=9.0, dv=10.0, h=3.0)

    ent = frame.xy((u0 + u1) * 0.5, v0 - 4.0)
    ctx["entrance"].append((b["id"], ent, 0.0))
    ctx["sign"].append((b["id"], ent, 3.4))


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
                            glass="glass_dark")
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

    ctx["entrance"].append((b["id"], frame.xy(u1 + 4.0, -24.0), 0.0))
    ctx["sign"].append((b["id"], frame.xy(u1 + 4.0, -24.0), 3.4))
    ctx["library_uv"] = (u0, v0, u1, v1)


# --------------------------------------------------------------------------- #
#  体育館 — 無窓の大壁 + 上部ハイサイドライト + 北側の部室棟
# --------------------------------------------------------------------------- #
def build_gym(mb, b, frame, ctx):
    loop = geom.ensure_ccw(geom.dedup(b["footprint"]))
    h = b["height"]
    u0, v0, u1, v1 = frame.uv_bbox(loop)

    facade.add_solid(mb, loop, 0.0, h - 5.4, "concrete_light")
    facade.add_facade(mb, loop, h - 5.4, 2.6, 0, 2,
                      wall="concrete_light", glass="glass_dark",
                      seg=3.2, sill=0.45, header=0.35, inset=0.35, mullion=0.24)
    # 北側の部室棟（小窓が並ぶ低層ウィング）
    wing = _uv_rect(frame, u0 + 4.0, v1 - 1.0, u0 + 30.0, v1 + 9.0)
    mb.add_slab(wing, 0.0, 16.5, "concrete_grey")
    facade.add_facade(mb, wing, 0.0, 3.3, 0, 5,
                      wall="concrete_grey", glass="glass_dark",
                      seg=2.0, sill=1.25, header=0.55, inset=0.30, mullion=0.5)
    facade.add_parapet(mb, wing, 16.5, 0.8, 0.3, "concrete_light")

    mb.add_ngon_flat(loop, h, "roof_grey")
    facade.add_parapet(mb, loop, h, 0.9, 0.4, "concrete_light")
    ent = frame.xy(u0 + (u1 - u0) * 0.5, v0 - 3.5)
    ctx["entrance"].append((b["id"], ent, 0.0))
    ctx["sign"].append((b["id"], ent, 3.4))


# --------------------------------------------------------------------------- #
#  実験棟 — 格子窓 + レンガコア + 屋上排気ダクト
# --------------------------------------------------------------------------- #
def build_lab_low(mb, b, frame, ctx):
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
    ent = frame.xy((u0 + u1) * 0.5, v0 - 3.0)
    ctx["entrance"].append((b["id"], ent, 0.0))
    ctx["sign"].append((b["id"], ent, 3.0))


# --------------------------------------------------------------------------- #
#  温室 — 白フレームの切妻ガラス
# --------------------------------------------------------------------------- #
def build_greenhouse(mb, b, frame, ctx):
    loop = geom.ensure_ccw(geom.dedup(b["footprint"]))
    u0, v0, u1, v1 = frame.uv_bbox(loop)
    rect = _uv_rect(frame, u0, v0, u1, v1)
    wall_h = 2.0
    ridge = b["height"]
    facade.add_curtain_wall(mb, rect, 0.0, wall_h, 0, 1, seg=1.6, inset=0.08)
    vm = (v0 + v1) * 0.5
    a = frame.xy(u0, v0)
    bb = frame.xy(u1, v0)
    c = frame.xy(u1, v1)
    d = frame.xy(u0, v1)
    r0 = frame.xy(u0, vm)
    r1 = frame.xy(u1, vm)
    mb.add_quad((a[0], a[1], wall_h), (bb[0], bb[1], wall_h),
                (r1[0], r1[1], ridge), (r0[0], r0[1], ridge), "glass_clear")
    mb.add_quad((c[0], c[1], wall_h), (d[0], d[1], wall_h),
                (r0[0], r0[1], ridge), (r1[0], r1[1], ridge), "glass_clear")
    mb.add_face([(a[0], a[1], wall_h), (r0[0], r0[1], ridge), (d[0], d[1], wall_h)],
                "metal_white")
    mb.add_face([(bb[0], bb[1], wall_h), (c[0], c[1], wall_h), (r1[0], r1[1], ridge)],
                "metal_white")
    ctx["entrance"].append((b["id"], frame.xy((u0 + u1) * 0.5, v0 - 2.5), 0.0))


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
