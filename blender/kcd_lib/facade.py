"""ファサード生成。

窓は「外側の面（スパンドレル・方立）＋ 奥に引っ込んだガラス面 ＋ 見込みの 4 面」で作る。
bmesh の inset/extrude を回さずに頂点を直接置くので、11 階 × 周長 400 m でも一瞬で終わる。
"""

import math

from . import geom


def _P(p, e, n, t, z, d):
    """辺上のローカル座標 (t=辺に沿う距離, z=高さ, d=法線方向オフセット) を 3D 点にする。"""
    return (p[0] + e[0] * t + n[0] * d,
            p[1] + e[1] * t + n[1] * d,
            z)


def add_facade(mb, loop, z0, floor_h, f0, f1, *,
               wall="concrete_grey", glass="glass_dark",
               seg=3.0, sill=1.10, header=0.40, inset=0.40, mullion=0.35,
               edges=None, side_reveals=True):
    """loop（反時計回り）の外周に、階 f0..f1-1 の水平連続窓を貼る。"""
    loop = geom.ensure_ccw(geom.dedup(loop))
    n_edges = len(loop)
    targets = range(n_edges) if edges is None else [i for i in edges if 0 <= i < n_edges]
    for i in targets:
        p = loop[i]
        q = loop[(i + 1) % n_edges]
        d = geom.sub(q, p)
        L = geom.length(d)
        if L < 0.35:
            continue
        e = geom.mul(d, 1.0 / L)
        nrm = (e[1], -e[0])
        ns = max(1, int(round(L / seg)))
        w = L / ns
        mw = min(mullion, w * 0.5)
        for f in range(f0, f1):
            zb = z0 + f * floor_h
            zt = zb + floor_h
            zw0 = zb + sill
            zw1 = zt - header
            if zw1 - zw0 < 0.25:
                mb.add_quad(_P(p, e, nrm, 0, zb, 0), _P(p, e, nrm, L, zb, 0),
                            _P(p, e, nrm, L, zt, 0), _P(p, e, nrm, 0, zt, 0), wall)
                continue
            # 腰壁と垂れ壁（辺の全長で 1 枚）
            mb.add_quad(_P(p, e, nrm, 0, zb, 0), _P(p, e, nrm, L, zb, 0),
                        _P(p, e, nrm, L, zw0, 0), _P(p, e, nrm, 0, zw0, 0), wall)
            mb.add_quad(_P(p, e, nrm, 0, zw1, 0), _P(p, e, nrm, L, zw1, 0),
                        _P(p, e, nrm, L, zt, 0), _P(p, e, nrm, 0, zt, 0), wall)
            # 方立（縦桟）
            for k in range(ns + 1):
                t = k * w
                t0 = max(0.0, t - mw * 0.5)
                t1 = min(L, t + mw * 0.5)
                if t1 - t0 < 1e-3:
                    continue
                mb.add_quad(_P(p, e, nrm, t0, zw0, 0), _P(p, e, nrm, t1, zw0, 0),
                            _P(p, e, nrm, t1, zw1, 0), _P(p, e, nrm, t0, zw1, 0), wall)
            # 開口（ガラス + 見込み）
            for k in range(ns):
                t0 = k * w + mw * 0.5
                t1 = (k + 1) * w - mw * 0.5
                if t1 - t0 < 0.05:
                    continue
                mb.add_quad(_P(p, e, nrm, t0, zw0, -inset), _P(p, e, nrm, t1, zw0, -inset),
                            _P(p, e, nrm, t1, zw1, -inset), _P(p, e, nrm, t0, zw1, -inset), glass)
                mb.add_quad(_P(p, e, nrm, t0, zw0, 0), _P(p, e, nrm, t1, zw0, 0),
                            _P(p, e, nrm, t1, zw0, -inset), _P(p, e, nrm, t0, zw0, -inset), wall)
                mb.add_quad(_P(p, e, nrm, t0, zw1, -inset), _P(p, e, nrm, t1, zw1, -inset),
                            _P(p, e, nrm, t1, zw1, 0), _P(p, e, nrm, t0, zw1, 0), wall)
                if side_reveals:
                    mb.add_quad(_P(p, e, nrm, t0, zw0, 0), _P(p, e, nrm, t0, zw0, -inset),
                                _P(p, e, nrm, t0, zw1, -inset), _P(p, e, nrm, t0, zw1, 0), wall)
                    mb.add_quad(_P(p, e, nrm, t1, zw0, -inset), _P(p, e, nrm, t1, zw0, 0),
                                _P(p, e, nrm, t1, zw1, 0), _P(p, e, nrm, t1, zw1, -inset), wall)


def add_curtain_wall(mb, loop, z0, floor_h, f0, f1, *,
                     frame="metal_white", glass="glass_clear",
                     seg=2.2, edges=None, inset=0.14):
    """全面ガラスのカーテンウォール（図書館・講義棟の階段ホール・共創棟 1F）。"""
    add_facade(mb, loop, z0, floor_h, f0, f1,
               wall=frame, glass=glass, seg=seg, sill=0.22, header=0.22,
               inset=inset, mullion=0.18, edges=edges, side_reveals=False)


def add_solid(mb, loop, z0, z1, mat, edges=None):
    loop = geom.ensure_ccw(geom.dedup(loop))
    n = len(loop)
    targets = range(n) if edges is None else edges
    for i in targets:
        a = loop[i % n]
        b = loop[(i + 1) % n]
        mb.add_quad((a[0], a[1], z0), (b[0], b[1], z0),
                    (b[0], b[1], z1), (a[0], a[1], z1), mat)


def add_parapet(mb, loop, z, h=1.10, t=0.40, mat="concrete_light", edges=None):
    loop = geom.ensure_ccw(geom.dedup(loop))
    inner = geom.offset_polygon(loop, -t)
    n = len(loop)
    targets = range(n) if edges is None else [i for i in edges if 0 <= i < n]
    for i in targets:
        a, b = loop[i], loop[(i + 1) % n]
        ai, bi = inner[i], inner[(i + 1) % n]
        mb.add_quad((a[0], a[1], z), (b[0], b[1], z),
                    (b[0], b[1], z + h), (a[0], a[1], z + h), mat)
        mb.add_quad((a[0], a[1], z + h), (b[0], b[1], z + h),
                    (bi[0], bi[1], z + h), (ai[0], ai[1], z + h), mat)
        mb.add_quad((ai[0], ai[1], z + h), (bi[0], bi[1], z + h),
                    (bi[0], bi[1], z), (ai[0], ai[1], z), mat)


def add_louvers(mb, loop, z0, floor_h, f0, f1, *, mat="louver_white",
                depth=0.55, thick=0.12, edges=None, per_floor=2):
    """水平ルーバー（共創棟）。各階に per_floor 枚の薄い庇を回す。"""
    loop = geom.ensure_ccw(geom.dedup(loop))
    n = len(loop)
    targets = range(n) if edges is None else edges
    for i in targets:
        a = loop[i % n]
        b = loop[(i + 1) % n]
        d = geom.sub(b, a)
        L = geom.length(d)
        if L < 0.5:
            continue
        e = geom.mul(d, 1.0 / L)
        nrm = (e[1], -e[0])
        ao = geom.add(a, geom.mul(nrm, depth))
        bo = geom.add(b, geom.mul(nrm, depth))
        for f in range(f0, f1):
            for k in range(per_floor):
                z = z0 + f * floor_h + floor_h * (k + 0.5) / per_floor
                mb.add_quad((a[0], a[1], z), (b[0], b[1], z),
                            (bo[0], bo[1], z), (ao[0], ao[1], z), mat)
                mb.add_quad((ao[0], ao[1], z - thick), (bo[0], bo[1], z - thick),
                            (b[0], b[1], z - thick), (a[0], a[1], z - thick), mat)
                mb.add_quad((ao[0], ao[1], z - thick), (ao[0], ao[1], z),
                            (bo[0], bo[1], z), (bo[0], bo[1], z - thick), mat)


def add_colonnade(mb, line_a, line_b, z0, z1, *, spacing=6.0, radius=0.45,
                  mat="concrete_light", slab=True, slab_depth=0.0, slab_mat="concrete_light"):
    """a→b の直線上に等間隔の丸柱を立てる。slab=True で上に庇スラブを載せる。"""
    L = geom.length(geom.sub(line_b, line_a))
    if L < 1.0:
        return
    n = max(2, int(round(L / spacing)) + 1)
    for i in range(n):
        p = geom.lerp(line_a, line_b, i / (n - 1))
        mb.add_cylinder(p[0], p[1], z0, z1, radius, mat, seg=10, cap_top=False)
    if slab and slab_depth > 0:
        d = geom.normalize(geom.sub(line_b, line_a))
        nrm = (d[1], -d[0])
        a0 = geom.add(line_a, geom.mul(nrm, radius + 0.6))
        b0 = geom.add(line_b, geom.mul(nrm, radius + 0.6))
        a1 = geom.add(line_a, geom.mul(nrm, -slab_depth))
        b1 = geom.add(line_b, geom.mul(nrm, -slab_depth))
        mb.add_slab([a1, b1, b0, a0], z1, z1 + 0.45, slab_mat)


def add_glass_tower(mb, base_uv_rect, z0, z1, frame_h, *, glass="glass_clear",
                    frame="metal_grey", canopy=1.6):
    """ガラスのエレベータ塔（薄い庇つき）。base_uv_rect は XY ポリゴン。"""
    add_curtain_wall(mb, base_uv_rect, z0, frame_h,
                     0, max(1, int(round((z1 - z0) / frame_h))),
                     frame=frame, glass=glass, seg=2.4, inset=0.10)
    top = geom.offset_polygon(base_uv_rect, canopy)
    mb.add_slab(top, z1, z1 + 0.45, frame)
