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
