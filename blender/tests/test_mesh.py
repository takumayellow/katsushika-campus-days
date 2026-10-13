"""kcd_lib.mesh の純 Python 部分（MeshBuilder の面の組み立てと split_by_grid）(#68)。"""

import math
import random

import pytest
import stubs
from mathutils.geometry import tessellate_polygon

from kcd_lib import geom
from kcd_lib import mesh as M
from kcd_lib.mesh import MeshBuilder


def _normal(mb, face):
    return M._newell([mb.verts[k] for k in face])


def _unit_z(n):
    length = math.sqrt(n[0] ** 2 + n[1] ** 2 + n[2] ** 2)
    return n[2] / length if length > 0 else 0.0


def _area_by_mat(mb):
    out = {}
    for face, mi in zip(mb.faces, mb.face_mat):
        name = mb.mat_names[mi]
        out[name] = out.get(name, 0.0) + M._area3([mb.verts[k] for k in face])
    return out


def _longest_edge(mb, horizontal_only=True):
    best = 0.0
    for face in mb.faces:
        poly = [mb.verts[k] for k in face]
        if horizontal_only and abs(_unit_z(M._newell(poly))) < 0.99:
            continue
        for i in range(len(poly)):
            a, b = poly[i], poly[(i + 1) % len(poly)]
            best = max(best, math.dist(a, b))
    return best


# 凹 6 角形（L 字）と凹 8 角形（コの字）。反時計回り
L_SHAPE = [(0.0, 0.0), (6.0, 0.0), (6.0, 2.0), (2.0, 2.0), (2.0, 6.0), (0.0, 6.0)]
U_SHAPE = [(0.0, 0.0), (9.0, 0.0), (9.0, 7.0), (6.0, 7.0), (6.0, 3.0), (3.0, 3.0), (3.0, 7.0), (0.0, 7.0)]


# ---- スタブの耳切り ----

@pytest.mark.parametrize("poly", [L_SHAPE, U_SHAPE])
@pytest.mark.parametrize("reverse", [False, True])
def test_stub_tessellate_covers_polygon_in_input_winding(poly, reverse):
    p = list(reversed(poly)) if reverse else list(poly)
    tris = tessellate_polygon([[(x, y, 0.0) for x, y in p]])
    assert len(tris) == len(p) - 2
    total = 0.0
    for tri in tris:
        a = geom.poly_area([p[i] for i in tri])
        assert (a < 0) == reverse  # 入力と同じ巻き方向
        total += abs(a)
        assert geom.point_in_poly(geom.centroid([p[i] for i in tri]), poly)
    assert total == pytest.approx(abs(geom.poly_area(p)))


def test_stub_tessellate_handles_collinear_vertices():
    # 辺の途中に頂点がある長方形（split_by_grid の切り口で出る形）
    p = [(0, 0, 0), (2, 0, 0), (4, 0, 0), (4, 2, 0), (2, 2, 0), (0, 2, 0)]
    tris = tessellate_polygon([p])
    total = sum(abs(geom.poly_area([p[i][:2] for i in t])) for t in tris)
    assert total == pytest.approx(8.0)


def test_stub_tessellate_rejects_holes():
    with pytest.raises(NotImplementedError):
        tessellate_polygon([[(0, 0, 0), (1, 0, 0), (0, 1, 0)], [(0, 0, 0), (1, 0, 0), (0, 1, 0)]])


def test_stubbed_blender_modules_fail_loudly():
    import bmesh
    import bpy
    with pytest.raises(AttributeError, match="stubbed"):
        bpy.data
    with pytest.raises(AttributeError, match="stubbed"):
        bmesh.new
    stubs.install()  # 2 回目も同じモジュールのまま
    import mathutils
    assert mathutils.geometry.tessellate_polygon is tessellate_polygon


# ---- add_ngon_flat ----

@pytest.mark.parametrize("poly", [L_SHAPE, U_SHAPE], ids=["L", "U"])
@pytest.mark.parametrize("reverse", [False, True], ids=["ccw", "cw"])
@pytest.mark.parametrize("flip", [False, True], ids=["up", "down"])
def test_add_ngon_flat_concave(poly, reverse, flip):
    p = list(reversed(poly)) if reverse else list(poly)
    mb = MeshBuilder("t")
    mb.add_ngon_flat(p, 1.5, "ground", flip=flip)
    assert len(mb.faces) == len(p) - 2
    want = -1.0 if flip else 1.0
    for face in mb.faces:
        assert _unit_z(_normal(mb, face)) == pytest.approx(want)
        assert all(mb.verts[k][2] == 1.5 for k in face)
    assert _area_by_mat(mb) == {"ground": pytest.approx(abs(geom.poly_area(poly)))}


@pytest.mark.parametrize("n", [3, 4])
@pytest.mark.parametrize("reverse", [False, True])
def test_add_ngon_flat_small_polygons_are_one_face(n, reverse):
    p = [(0.0, 0.0), (3.0, 0.0), (3.0, 2.0), (0.0, 2.0)][:n]
    if reverse:
        p.reverse()
    for flip in (False, True):
        mb = MeshBuilder("t")
        mb.add_ngon_flat(p + [p[0]], 0.0, "m", flip=flip)  # 閉じ重複は落とす
        assert len(mb.faces) == 1 and len(mb.faces[0]) == n
        assert _unit_z(_normal(mb, mb.faces[0])) == pytest.approx(-1.0 if flip else 1.0)


def test_add_ngon_flat_skips_degenerate():
    mb = MeshBuilder("t")
    mb.add_ngon_flat([(0, 0), (0, 0), (1, 0)], 0.0, "m")
    assert mb.faces == [] and mb.verts == []


def test_add_prism_faces_point_outward():
    mb = MeshBuilder("t")
    mb.add_prism(list(reversed(L_SHAPE)), 0.0, 3.0, "wall", "roof", "floor")
    for face, mi in zip(mb.faces, mb.face_mat):
        poly = [mb.verts[k] for k in face]
        n = M._newell(poly)
        name = mb.mat_names[mi]
        if name == "roof":
            assert _unit_z(n) == pytest.approx(1.0)
        elif name == "floor":
            assert _unit_z(n) == pytest.approx(-1.0)
        else:
            assert abs(n[2]) < 1e-9
            # 側面の中点から法線方向へ少し出た点は、平面図で多角形の外
            mx = sum(p[0] for p in poly) / 4
            my = sum(p[1] for p in poly) / 4
            ln = math.hypot(n[0], n[1])
            out = (mx + 0.01 * n[0] / ln, my + 0.01 * n[1] / ln)
            assert not geom.point_in_poly(out, L_SHAPE), (name, poly)
    areas = _area_by_mat(mb)
    assert areas["roof"] == pytest.approx(20.0)
    assert areas["floor"] == pytest.approx(20.0)
    assert areas["wall"] == pytest.approx(geom.perimeter(L_SHAPE) * 3.0)


def test_add_ribbon_top_faces_up():
    mb = MeshBuilder("t")
    mb.add_ribbon([(0, 0), (10, 0), (10, 10)], 2.0, 0.1, "road", thickness=0.05)
    tops = [f for f in mb.faces if all(mb.verts[k][2] == 0.1 for k in f)]
    assert len(tops) == 2
    for f in tops:
        assert _unit_z(_normal(mb, f)) == pytest.approx(1.0)


def test_mat_indices_are_stable():
    mb = MeshBuilder("t")
    assert mb.mat("a") == 0 and mb.mat("b") == 1 and mb.mat("a") == 0
    assert mb.mat_names == ["a", "b"]


# ---- split_by_grid ----

def _square_mesh(size, z=0.0, reverse=False):
    mb = MeshBuilder("t")
    p = [(-size, -size), (size, -size), (size, size), (-size, size)]
    if reverse:
        p.reverse()
    mb.add_face([(x, y, z) for x, y in p], "ground")
    return mb


def test_split_by_grid_big_square():
    # 外周の地面（±350 m）の形。30 m 格子で切ると、どの辺も 30 m 以下になる
    mb = _square_mesh(350.0)
    n_split, n_faces = mb.split_by_grid(30.0)
    assert n_split == 1
    assert n_faces == len(mb.faces)
    # -350..350 を 30 m で切ると、端の 20 m と 22 本の 30 m 幅で 24 列
    assert n_faces == 24 * 24
    assert _longest_edge(mb) <= 30.0 + 1e-9
    assert _area_by_mat(mb)["ground"] == pytest.approx(700.0 * 700.0)
    for face in mb.faces:
        assert _unit_z(_normal(mb, face)) == pytest.approx(1.0)


def test_split_by_grid_keeps_downward_faces_down():
    mb = _square_mesh(40.0, reverse=True)
    mb.split_by_grid(30.0)
    assert all(_unit_z(_normal(mb, f)) == pytest.approx(-1.0) for f in mb.faces)


def test_split_by_grid_leaves_faces_inside_one_cell():
    mb = MeshBuilder("t")
    mb.add_face([(1, 1, 0), (29, 1, 0), (29, 29, 0), (1, 29, 0)], "a")
    # 格子線の上に乗った辺（eps 以内）も切らない
    mb.add_face([(30.0005, 0, 0), (59.9995, 0, 0), (60, 30, 0), (30, 30, 0)], "b")
    before = [tuple(f) for f in mb.faces], list(mb.verts)
    assert mb.split_by_grid(30.0) == (0, 2)
    assert ([tuple(f) for f in mb.faces], list(mb.verts)) == before


@pytest.mark.parametrize("poly", [L_SHAPE, U_SHAPE], ids=["L", "U"])
@pytest.mark.parametrize("reverse", [False, True], ids=["ccw", "cw"])
def test_split_by_grid_concave_ngon(poly, reverse):
    # 凹多角形を 1 枚の面として入れ、細かい格子で切る（三角形に割ってから切る経路）
    scale = 10.0
    p = [(x * scale, y * scale, 0.0) for x, y in poly]
    if reverse:
        p.reverse()
    mb = MeshBuilder("t")
    mb.add_face(p, "m")
    n_split, n_faces = mb.split_by_grid(7.0)
    assert n_split == 1 and n_faces > 20
    assert _area_by_mat(mb)["m"] == pytest.approx(abs(geom.poly_area(poly)) * scale * scale)
    assert _longest_edge(mb) <= 7.0 * math.sqrt(2) + 1e-6
    want = -1.0 if reverse else 1.0
    for face in mb.faces:
        pts = [mb.verts[k] for k in face]
        assert _unit_z(M._newell(pts)) == pytest.approx(want)
        # 切れ端はすべて元の多角形の中
        c = (sum(q[0] for q in pts) / len(pts) / scale, sum(q[1] for q in pts) / len(pts) / scale)
        assert geom.point_in_poly(c, poly)


def test_split_by_grid_vertical_wall():
    # 壁は x 方向にだけ切られ、法線と面積は保たれる
    mb = MeshBuilder("t")
    mb.add_face([(0, 5, 0), (100, 5, 0), (100, 5, 12), (0, 5, 12)], "wall")
    n_before = M._newell([mb.verts[k] for k in mb.faces[0]])
    n_split, n_faces = mb.split_by_grid(30.0)
    assert (n_split, n_faces) == (1, 4)
    assert _area_by_mat(mb)["wall"] == pytest.approx(1200.0)
    for face in mb.faces:
        n = _normal(mb, face)
        assert n[1] * n_before[1] > 0 and abs(n[0]) < 1e-9 and abs(n[2]) < 1e-9


def test_split_by_grid_random_triangles_preserve_area_and_material():
    rng = random.Random(68)
    mb = MeshBuilder("t")
    for i in range(60):
        pts = [(rng.uniform(-120, 120), rng.uniform(-120, 120), 0.0) for _ in range(3)]
        if abs(geom.poly_area([q[:2] for q in pts])) < 1.0:
            continue
        mb.add_face(pts, "m%d" % (i % 3))
    before = _area_by_mat(mb)
    mb.split_by_grid(25.0)
    after = _area_by_mat(mb)
    assert after.keys() == before.keys()
    for k in before:
        assert after[k] == pytest.approx(before[k], rel=1e-9)
    assert _longest_edge(mb) <= 25.0 * math.sqrt(2) + 1e-6
    assert len(mb.face_mat) == len(mb.faces)
    for face in mb.faces:
        assert abs(_unit_z(_normal(mb, face))) == pytest.approx(1.0)


# ---- 下請け ----

def test_grid_range():
    assert M._grid_range(-350.0, 350.0, 30.0, 1e-3) == list(range(-11, 12))
    assert M._grid_range(0.0, 30.0, 30.0, 1e-3) == []  # 両端が線の上
    assert M._grid_range(0.0, 30.01, 30.0, 1e-3) == [1]
    assert M._grid_range(0.0, 30.0005, 30.0, 1e-3) == []
    assert M._grid_range(1.0, 29.0, 30.0, 1e-3) == []


def test_is_convex_and_area3():
    sq = [(0, 0, 0), (1, 0, 0), (1, 1, 0), (0, 1, 0)]
    assert M._is_convex(sq)
    assert M._is_convex(list(reversed(sq)))
    assert not M._is_convex([(x, y, 0.0) for x, y in L_SHAPE])
    assert M._area3(sq) == pytest.approx(1.0)
    assert M._area3([(0, 0, 0), (0, 3, 0), (0, 3, 4)]) == pytest.approx(6.0)


def test_clip_half_keeps_the_requested_side():
    sq = [(0.0, 0.0, 0.0), (10.0, 0.0, 0.0), (10.0, 10.0, 0.0), (0.0, 10.0, 0.0)]
    left = M._clip_half(sq, 0, 4.0, -1.0, 1e-3)
    right = M._clip_half(sq, 0, 4.0, 1.0, 1e-3)
    assert M._area3(left) == pytest.approx(40.0)
    assert M._area3(right) == pytest.approx(60.0)
    assert max(p[0] for p in left) == pytest.approx(4.0)
    assert min(p[0] for p in right) == pytest.approx(4.0)
    # 向きは元のまま
    assert _unit_z(M._newell(left)) == pytest.approx(1.0)
    assert _unit_z(M._newell(right)) == pytest.approx(1.0)
    # 線が多角形の外なら丸ごと残る / 丸ごと消える
    assert M._clip_half(sq, 1, -5.0, 1.0, 1e-3) == sq
    assert M._clip_half(sq, 1, -5.0, -1.0, 1e-3) == []


def test_triangulate_orients_triangles_like_the_polygon():
    poly = [(x, y, 0.0) for x, y in reversed(U_SHAPE)]
    tris = M._triangulate(poly)
    assert len(tris) == len(poly) - 2
    assert sum(M._area3(t) for t in tris) == pytest.approx(abs(geom.poly_area(U_SHAPE)))
    assert all(_unit_z(M._newell(t)) == pytest.approx(-1.0) for t in tris)
