"""kcd_chara.mesh の陰の法線 (#47)。

to_object は決めた頂点だけをカスタム法線にし、残りの角には Blender が面から求めた法線を
そのまま渡す。その振り分けは bpy の要らない merge_normals が受け持つ。
"""

import numpy as np

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
