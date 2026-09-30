"""kcd_chara.mesh の陰の法線 (#47)。

to_object は決めた頂点だけをカスタム法線にし、残りの角には Blender が面から求めた法線を
そのまま渡す。その振り分けは bpy の要らない merge_normals が受け持つ。

share_normals は、同じ位置に重なった別々の頂点へ 1 本にそろえた法線を決める。
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
