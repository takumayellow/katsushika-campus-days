"""第1実験棟の外観 (#240)。

第1実験棟だけを実物の外観（東西のレンガ、南北の縦フィン、東の外階段、西の 1 階建ての別棟）に
作り直す。第2実験棟の見た目、入口の位置、当たり判定の輪郭は変えない。
"""

import pytest

from kcd_lib import buildings, entrances, geom
from kcd_lib.mesh import MeshBuilder

# 作り直す前（ac006cb）の第2実験棟の面の数と三角数
LAB2_FACES = 601
LAB2_TRIS = 1178
# 作り直す前の第1実験棟の三角数。屋内の窓の外の近景にも入るので、これより重くしない
LAB1_TRIS_BEFORE = 3476
# 2 階の床（17.0 m / 4 = 4.25 m）の庇と踊り場の下端より低い高さ
GROUND_Z = 3.9
TOL = 0.05
# 外周の壁を確かめる高さと、辺に沿った間隔（当たり判定は人の背丈より上まであればよい）
WALL_ZS = (0.3, 1.5, 2.7)
WALL_STEP = 0.5


def _building(campus_data, bid):
    return next(b for b in campus_data["buildings"] if b["id"] == bid)


@pytest.fixture(scope="module")
def built(campus_data, campus_frame):
    """build_campus と同じ ctx で lab1 と lab2 を組む。"""
    on_campus = []
    for b in campus_data["buildings"]:
        loop = geom.ensure_ccw(geom.dedup(b["footprint"]))
        if len(loop) >= 3 and (b.get("style") or "background") in buildings.BUILDERS and b.get("on_campus"):
            on_campus.append((b["id"], loop, float(b.get("height") or 10.0)))
    ctx = {"entrance": [], "sign": [], "footprints": on_campus, "bg_window_spacing": 2.4}
    ctx["uvbb"] = {fid: campus_frame.uv_bbox(fp) for fid, fp, _h in on_campus}
    out = {}
    for bid in ("lab1", "lab2"):
        b = _building(campus_data, bid)
        mb = MeshBuilder("bld_%s" % bid)
        buildings.BUILDERS[b["style"]](mb, b, campus_frame, ctx)
        out[bid] = mb
    return out


def _tris(mb):
    return sum(len(f) - 2 for f in mb.faces)


def _inside_or_on(p, poly):
    """点 p が多角形の内側か、辺から TOL 以内にある。"""
    return geom.point_in_poly(p, poly) or geom.dist_point_poly_edges(p, poly) <= TOL


def test_both_labs_keep_the_lab_low_style(campus_data):
    assert _building(campus_data, "lab1")["style"] == "lab_low"
    assert _building(campus_data, "lab2")["style"] == "lab_low"


def test_lab2_is_unchanged(built):
    mb = built["lab2"]
    assert (len(mb.faces), _tris(mb)) == (LAB2_FACES, LAB2_TRIS)
    assert "louver_white" not in mb.mat_names


def test_lab1_is_lighter_than_before(built):
    assert 0 < _tris(built["lab1"]) < LAB1_TRIS_BEFORE


def test_lab1_uses_brick_fins_and_roof_units(built):
    names = set(built["lab1"].mat_names)
    assert {"brick_red", "louver_white", "glass_dark", "roof_grey", "metal_grey"} <= names


def test_lab1_splits_into_main_and_annex(campus_data, campus_frame):
    loop = geom.ensure_ccw(geom.dedup(_building(campus_data, "lab1")["footprint"]))
    main, annex = buildings._lab1_split(loop, campus_frame)
    # 本体と別棟は接する壁の 2 頂点だけを共有し、合わせると元の外形になる
    assert main[0] == annex[-1] and main[-1] == annex[0]
    assert len(main) + len(annex) - 2 == len(loop)
    assert set(main) | set(annex) == set(loop)
    # 別棟は本体より小さい
    assert abs(geom.poly_area(annex)) < abs(geom.poly_area(main)) * 0.2


def test_lab1_split_rejects_a_changed_outline(campus_data, campus_frame):
    loop = geom.ensure_ccw(geom.dedup(_building(campus_data, "lab1")["footprint"]))
    moved = [(x + 30.0, y) for x, y in loop]
    with pytest.raises(ValueError):
        buildings._lab1_split(moved, campus_frame)


def test_lab1_ground_level_stays_on_the_outline(campus_data, built):
    """2 階の床より下の頂点は外形の内側か外形の上にある（当たり判定の輪郭を変えない）。"""
    loop = geom.ensure_ccw(geom.dedup(_building(campus_data, "lab1")["footprint"]))
    low = [v for v in built["lab1"].verts if v[2] < GROUND_Z]
    assert low
    outside = [v for v in low if not _inside_or_on((v[0], v[1]), loop)]
    assert outside == []


def _ray_hits_tri(o, d, a, b, c, t_max):
    """o から d 向きの線分（長さ t_max）が三角形 abc に当たるか（Möller–Trumbore）。"""
    e1 = [b[i] - a[i] for i in range(3)]
    e2 = [c[i] - a[i] for i in range(3)]
    pv = [d[1] * e2[2] - d[2] * e2[1], d[2] * e2[0] - d[0] * e2[2], d[0] * e2[1] - d[1] * e2[0]]
    det = sum(e1[i] * pv[i] for i in range(3))
    if abs(det) < 1e-9:
        return False
    tv = [o[i] - a[i] for i in range(3)]
    u = sum(tv[i] * pv[i] for i in range(3)) / det
    if u < -1e-6 or u > 1 + 1e-6:
        return False
    qv = [tv[1] * e1[2] - tv[2] * e1[1], tv[2] * e1[0] - tv[0] * e1[2], tv[0] * e1[1] - tv[1] * e1[0]]
    v = sum(d[i] * qv[i] for i in range(3)) / det
    if v < -1e-6 or u + v > 1 + 1e-6:
        return False
    t = sum(e2[i] * qv[i] for i in range(3)) / det
    return 0.0 <= t <= t_max


def test_lab1_ground_level_walls_close_the_outline(campus_data, built):
    """外形の各辺は、地面から 2.7 m まで面でふさがっている（当たり判定に穴が無い）。

    外から辺に垂直に、辺の 1 m 外から 1 m 内までの線分を引き、どれかの面に当たるかを見る。
    外形の辺だけを見るので、本体と別棟が接する壁は対象にならない。"""
    loop = geom.ensure_ccw(geom.dedup(_building(campus_data, "lab1")["footprint"]))
    mb = built["lab1"]
    tris = []
    for f in mb.faces:
        vs = [mb.verts[i] for i in f]
        if min(v[2] for v in vs) < max(WALL_ZS):
            tris.extend((vs[0], vs[k], vs[k + 1]) for k in range(1, len(vs) - 1))
    holes = []
    n = len(loop)
    for i in range(n):
        a, b = loop[i], loop[(i + 1) % n]
        e, nrm, L = buildings._lab1_edge(a, b)
        steps = max(1, int((L - 0.2) / WALL_STEP))
        for k in range(steps + 1):
            q = geom.add(a, geom.mul(e, 0.1 + (L - 0.2) * k / steps))
            for z in WALL_ZS:
                o = (q[0] + nrm[0], q[1] + nrm[1], z)
                d = (-nrm[0], -nrm[1], 0.0)
                near = [t for t in tris
                        if min(p[0] for p in t) - 1.1 <= q[0] <= max(p[0] for p in t) + 1.1
                        and min(p[1] for p in t) - 1.1 <= q[1] <= max(p[1] for p in t) + 1.1]
                if not any(_ray_hits_tri(o, d, *t, 2.0) for t in near):
                    holes.append((i, round(L, 2), round(geom.length(geom.sub(q, a)), 2), z))
    assert holes == []


def test_lab1_split_rejects_a_clockwise_outline(campus_data, campus_frame):
    loop = geom.ensure_ccw(geom.dedup(_building(campus_data, "lab1")["footprint"]))
    with pytest.raises(ValueError):
        buildings._lab1_split(list(reversed(loop)), campus_frame)


def test_lab1_door_is_unchanged_and_on_the_south_face(campus_data, campus_frame):
    doors = [d for d in entrances.DOORS if d[0] == "lab1"]
    assert doors == [("lab1", (56.2, 36.0), (0.0, 1.0), "loop", +1, 3.0, entrances.STANDARD)]
    loop = geom.ensure_ccw(geom.dedup(_building(campus_data, "lab1")["footprint"]))
    main, _annex = buildings._lab1_split(loop, campus_frame)
    hits = []
    for i in range(len(main) - 1):
        a, b = main[i], main[i + 1]
        t = buildings._lab1_door_t(a, b, campus_frame)
        if t is not None:
            hits.append(buildings._lab1_facing(campus_frame, a, b))
    # 探索線は南面に当たる（庇はそこを空ける）
    assert "s" in hits


def test_lab1_canopy_leaves_the_door_open(campus_data, campus_frame, built):
    """南面の庇は入口の庇（半幅 CAN_S）の上を空け、両脇には出る。"""
    b = _building(campus_data, "lab1")
    loop = geom.ensure_ccw(geom.dedup(b["footprint"]))
    main, _annex = buildings._lab1_split(loop, campus_frame)
    door = None
    for i in range(len(main) - 1):
        a, c = main[i], main[i + 1]
        t = buildings._lab1_door_t(a, c, campus_frame)
        if t is not None and buildings._lab1_facing(campus_frame, a, c) == "s":
            e, _n, _L = buildings._lab1_edge(a, c)
            door = geom.add(a, geom.mul(e, t))
    assert door is not None
    fh = b["height"] / b["levels"]
    # 2 階の床の高さで、壁からフィンの出より遠くに張り出した頂点 = 庇と踊り場
    out = [(v[0], v[1]) for v in built["lab1"].verts
           if abs(v[2] - fh) < 0.35 and not geom.point_in_poly((v[0], v[1]), loop)
           and geom.dist_point_poly_edges((v[0], v[1]), loop) > buildings.LAB1_FIN_D + 0.2]
    can_s = entrances.STANDARD["CAN_S"]
    near = [p for p in out if geom.length(geom.sub(p, door)) < can_s]
    assert near == []
    beside = [p for p in out if geom.length(geom.sub(p, door)) < can_s + 3.0]
    assert beside
