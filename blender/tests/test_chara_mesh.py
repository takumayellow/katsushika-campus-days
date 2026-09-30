"""kcd_chara.mesh の陰の法線 (#47)。

to_object は決めた頂点だけをカスタム法線にし、残りの角には Blender が面から求めた法線を
そのまま渡す。その振り分けは bpy の要らない merge_normals が受け持つ。

share_normals は、同じ位置に重なった別々の頂点へ 1 本にそろえた法線を決める。
shade_as は、形はそのままで、陰の法線だけを別の位置に置いた同じ面から求める。
outline_normals は、輪郭線の殻を押し出す向きを陰の法線とは別に求める。
"""

import numpy as np
import pytest

from kcd_chara import mesh as M


def test_merge_normals_uses_auto_where_unset():
    mb = M.MeshBuilder()
    mb.add_verts([(0.0, 0.0, 0.0), (1.0, 0.0, 0.0), (0.0, 1.0, 0.0)])
    mb.set_normals(1, [(0.0, 0.0, 2.0)])
    loop_v = np.array([0, 1, 2, 1])
    auto = np.array([(1.0, 0.0, 0.0), (0.0, 1.0, 0.0), (0.0, 0.0, -1.0), (0.0, -1.0, 0.0)])
    out = M.merge_normals(mb.normal_array(), loop_v, auto)
    # 決めた頂点 1 の角は 2 つとも custom、決めていない頂点の角は角ごとの auto のまま
    np.testing.assert_allclose(out, [(1, 0, 0), (0, 0, 1), (0, 0, -1), (0, 0, 1)])


def test_zero_normal_stays_unset():
    mb = M.MeshBuilder()
    mb.add_verts([(0.0, 0.0, 0.0)])
    mb.set_normals(0, [(0.0, 0.0, 0.0)])
    assert not np.isnan(mb.normal_array()).any()
    np.testing.assert_allclose(M.merge_normals(mb.normal_array(), [0], [(1.0, 0.0, 0.0)]),
                               [(1, 0, 0)])


def _l_shape():
    """x = 1 の辺で 90° に折れた 2 枚の四角形を、別々の頂点で張る（床 +z、壁 +x）。

    番号 0 は v0 より前に置いた、辺の端と同じ位置の頂点。
    """
    mb = M.MeshBuilder()
    mb.add_verts([(1.0, 0.0, 0.0)])
    floor = mb.add_verts([(0.0, 0.0, 0.0), (1.0, 0.0, 0.0), (1.0, 1.0, 0.0), (0.0, 1.0, 0.0)])
    mb.add_face([floor + k for k in range(4)], "m", smooth=False)
    wall = mb.add_verts([(1.0, 0.0, 0.0), (1.0, 0.0, -1.0), (1.0, 1.0, -1.0), (1.0, 1.0, 0.0)])
    mb.add_face([wall + k for k in range(4)], "m", smooth=False)
    return mb, floor, wall


def test_share_normals_gives_overlapping_vertices_one_normal():
    mb, floor, wall = _l_shape()
    mb.share_normals(floor, 0)
    n = mb.normal_array()
    edge = np.array([1.0, 0.0, 1.0]) / np.sqrt(2.0)
    # 折れ目の 2 点は、床と壁のどちらの頂点も床と壁の法線の平均
    for i in (floor + 1, floor + 2, wall, wall + 3):
        np.testing.assert_allclose(n[i], edge)
    # 重ならない頂点と、v0 より前の頂点は決めない
    for i in (0, floor, floor + 3, wall + 1, wall + 2):
        np.testing.assert_allclose(n[i], 0.0)


def test_share_normals_leaves_cancelling_normals_unset():
    """表と裏の面が同じ位置で打ち消し合う点は、向きが定まらないので決めない。"""
    mb = M.MeshBuilder()
    quad = [(0.0, 0.0, 0.0), (1.0, 0.0, 0.0), (1.0, 1.0, 0.0), (0.0, 1.0, 0.0)]
    top = mb.add_verts(quad)
    mb.add_face([top + k for k in range(4)], "m")
    bottom = mb.add_verts(quad)
    mb.add_face([bottom + k for k in range(4)][::-1], "m")
    mb.share_normals(0, 0)
    np.testing.assert_allclose(mb.normal_array(), 0.0)


def test_share_normals_rejects_faces_before_v0():
    """f0 以降の面が v0 より前の頂点を使うと、番号をずらした先が別の頂点になるので止める。"""
    mb, floor, _ = _l_shape()
    mb.add_face([0, floor + 2, floor + 3], "m")
    with pytest.raises(ValueError):
        mb.share_normals(floor, 0)


def test_vertex_normals_by_angle_ignores_face_size():
    """角の角度で重み付けすると、大きな面に接した細い面の向きも同じだけ効く。"""
    verts = [(0.0, 0.0, 0.0), (10.0, 0.0, 0.0), (10.0, 10.0, 0.0), (0.0, 10.0, 0.0),
             (0.0, 0.0, -1.0)]
    faces = [(0, 1, 2, 3), (0, 4, 3)]  # 面積 100 の床 (+z) と面積 5 の壁 (+x)
    np.testing.assert_allclose(M.vertex_normals(verts, faces, by_angle=True)[0],
                               np.array([1.0, 0.0, 1.0]) / np.sqrt(2.0))
    assert M.vertex_normals(verts, faces)[0][2] > 0.99  # 面積の重みでは床の向きになる


def test_shade_as_takes_normals_from_the_given_positions():
    """陰の法線は at に置いた面から求め、頂点の位置は動かさない。"""
    mb = M.MeshBuilder()
    quad = [(0.0, 0.0, 0.0), (1.0, 0.0, 0.0), (1.0, 1.0, 0.0), (0.0, 1.0, 0.0)]
    v = mb.add_verts(quad)
    mb.add_face([v + k for k in range(4)], "m")
    mb.shade_as(v, 0, [(x, y, x) for x, y, _ in quad])  # z = x の斜面
    np.testing.assert_allclose(mb.normal_array(),
                               np.tile(np.array([-1.0, 0.0, 1.0]) / np.sqrt(2.0), (4, 1)))
    np.testing.assert_allclose(mb.verts, quad)


def test_shade_as_gives_overlapping_vertices_one_normal():
    """今の位置で重なった頂点は、at では離れていても、それぞれの法線の平均にそろう。"""
    mb, floor, wall = _l_shape()
    at = np.asarray(mb.verts[floor:], dtype=float)
    at[wall - floor:] += (0.5, 0.0, 0.0)  # 壁だけ平行に動かす（向きは +x のまま）
    mb.shade_as(floor, 0, at)
    n = mb.normal_array()
    n = n / np.maximum(np.linalg.norm(n, axis=1, keepdims=True), 1e-30)
    for i in (floor + 1, floor + 2, wall, wall + 3):
        np.testing.assert_allclose(n[i], np.array([1.0, 0.0, 1.0]) / np.sqrt(2.0))
    for i in (floor, floor + 3):
        np.testing.assert_allclose(n[i], (0.0, 0.0, 1.0))
    for i in (wall + 1, wall + 2):
        np.testing.assert_allclose(n[i], (1.0, 0.0, 0.0))
    np.testing.assert_allclose(n[0], 0.0)  # v0 より前の頂点は決めない


def test_shade_as_keeps_cancelling_normals_per_vertex():
    """表と裏の面が同じ位置で打ち消し合う点は、平均せずにそれぞれの面の向きのままにする。"""
    mb = M.MeshBuilder()
    quad = [(0.0, 0.0, 0.0), (1.0, 0.0, 0.0), (1.0, 1.0, 0.0), (0.0, 1.0, 0.0)]
    top = mb.add_verts(quad)
    mb.add_face([top + k for k in range(4)], "m")
    bottom = mb.add_verts(quad)
    mb.add_face([bottom + k for k in range(4)][::-1], "m")
    mb.shade_as(0, 0, mb.verts)
    n = mb.normal_array()
    np.testing.assert_allclose(n[top:top + 4], np.tile((0.0, 0.0, 1.0), (4, 1)))
    np.testing.assert_allclose(n[bottom:bottom + 4], np.tile((0.0, 0.0, -1.0), (4, 1)))


def test_shade_as_rejects_positions_of_another_length():
    mb, floor, _ = _l_shape()
    with pytest.raises(ValueError):
        mb.shade_as(floor, 0, np.zeros((3, 3)))


def test_outline_normals_pool_overlapping_vertices():
    """別々の面の同じ位置の頂点は、殻の向きを和の向きにそろえる（面の無い頂点は数えない）。"""
    mb, floor, wall = _l_shape()
    on = mb.outline_normals()
    edge = np.array([1.0, 0.0, 1.0]) / np.sqrt(2.0)
    for i in (0, floor + 1, floor + 2, wall, wall + 3):
        np.testing.assert_allclose(on[i], edge, atol=1e-12)
    np.testing.assert_allclose(on[floor], (0.0, 0.0, 1.0), atol=1e-12)
    np.testing.assert_allclose(on[wall + 1], (1.0, 0.0, 0.0), atol=1e-12)


def test_outline_normals_keep_cancelling_normals_per_vertex():
    """表と裏の面が重なる所は、それぞれの面の向きのまま。"""
    mb = M.MeshBuilder()
    quad = [(0.0, 0.0, 0.0), (1.0, 0.0, 0.0), (1.0, 1.0, 0.0), (0.0, 1.0, 0.0)]
    top = mb.add_verts(quad)
    mb.add_face([top + k for k in range(4)], "m")
    bottom = mb.add_verts(quad)
    mb.add_face([bottom + k for k in range(4)][::-1], "m")
    on = mb.outline_normals()
    np.testing.assert_allclose(on[top:top + 4], [(0.0, 0.0, 1.0)] * 4, atol=1e-12)
    np.testing.assert_allclose(on[bottom:bottom + 4], [(0.0, 0.0, -1.0)] * 4, atol=1e-12)


def _fold(half_deg):
    """x 軸の辺で折れた 2 枚の四角形を別々の頂点で張る。面の法線は z から ±half_deg 傾く。"""
    mb = M.MeshBuilder()
    t = np.radians(half_deg)
    starts = []
    for n in ((0.0, np.sin(t), np.cos(t)), (0.0, -np.sin(t), np.cos(t))):
        e = np.cross(n, (1.0, 0.0, 0.0))
        v = mb.add_verts([(0.0, 0.0, 0.0), (1.0, 0.0, 0.0), (1.0, 0.0, 0.0) + e, e])
        mb.add_face([v + k for k in range(4)], "m")
        starts.append(v)
    return mb, starts


def test_outline_normals_pool_up_to_120_degrees():
    """向きが 120° より開く組は和にそろえない（和の向きへ押すと殻が面に沿って横へずれる）。"""
    mb, (a, b) = _fold(55.0)
    on = mb.outline_normals()
    np.testing.assert_allclose(on[a], (0.0, 0.0, 1.0), atol=1e-12)
    np.testing.assert_allclose(on[b], (0.0, 0.0, 1.0), atol=1e-12)
    mb, (a, b) = _fold(65.0)
    on = mb.outline_normals()
    t = np.radians(65.0)
    np.testing.assert_allclose(on[a], (0.0, np.sin(t), np.cos(t)), atol=1e-12)
    np.testing.assert_allclose(on[b], (0.0, -np.sin(t), np.cos(t)), atol=1e-12)


def test_outline_normals_prefer_the_given_then_the_shading_normals():
    """決めた殻の向き、陰の法線、面から求めた法線の順に取る。"""
    mb, floor, wall = _l_shape()
    mb.set_normals(floor, [(0.0, 1.0, 1.0)] * 4)
    mb.set_outline_normals(wall + 1, [(0.0, 0.0, -3.0)])
    on = mb.outline_normals()
    np.testing.assert_allclose(on[floor], np.array([0.0, 1.0, 1.0]) / np.sqrt(2.0), atol=1e-12)
    np.testing.assert_allclose(on[wall + 1], (0.0, 0.0, -1.0), atol=1e-12)
    np.testing.assert_allclose(on[wall + 2], (1.0, 0.0, 0.0), atol=1e-12)
    np.testing.assert_allclose(np.linalg.norm(on, axis=1), 1.0)
