"""箱と帯の襟の面が外を向く (#47)。

KCD_Toon の輪郭線は、角の法線の向きへ押し出した殻の裏面で描く。面が内を向くと、殻が
物の中へ縮み、表の面の際に黒い筋が出る。キャラの材質は両面描きなので、面の向きが
逆でも物そのものは見えてしまい、筋が出るまで気づけない。
"""

import numpy as np
import pytest

from kcd_chara import body, cloth, params
from kcd_chara import mesh as M


def _newell(V, f):
    q = V[list(f)]
    return sum(np.cross(q[k], q[(k + 1) % len(f)]) for k in range(len(f)))


@pytest.mark.parametrize("rot_z", [0.0, 0.7])
def test_add_box_faces_point_outward(rot_z):
    """MeshBuilder.add_box の 6 面の法線が箱の外を向く（靴の踵・袴の結び・下駄）。"""
    mb = M.MeshBuilder()
    c = np.array([0.1, -0.2, 0.3])
    mb.add_box(tuple(c), (0.04, 0.02, 0.06), "m", rot_z=rot_z)
    V = np.array(mb.verts)
    assert len(mb.faces) == 6
    for f in mb.faces:
        assert _newell(V, f) @ (V[list(f)].mean(axis=0) - c) > 0, f


def test_band_collar_faces_point_outward():
    """prof の白衣とブラウスの帯の襟（cloth._collar）の法線が、胴の軸から外を向く。"""
    p = params.resolve("prof")
    mb = M.MeshBuilder()
    a, *_ = body.build_base(mb, p)
    cloth.build_outfit(mb, p, a)
    V = np.array(mb.verts)
    blocks = mb.parts["collar"]
    faces = [f for f in mb.faces if any(s <= f[0] < e for s, e in blocks)]
    # 帯の襟は 1 周 26 分割（cloth._collar の seg）。ブラウスと白衣の 2 本ぶん以上
    assert len(faces) >= 2 * 26
    for f in faces:
        c = V[list(f)].mean(axis=0)
        assert _newell(V, f) @ np.array([c[0], c[1], 0.0]) > 0, f
