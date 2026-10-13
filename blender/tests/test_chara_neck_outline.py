"""低い正面から見た首の付け根に、胴の輪郭線の殻が横線になって出ないこと (#155)。

Unity の輪郭線（KCD_Toon.shader の Outline パス）は、頂点を輪郭の向きへ押し出した殻の
裏面で描く。胴の上端（肩の上のふた）の縁は首の前へ数 mm はみ出しているので、そこに殻が
あると、カメラが肩より低い全身のショットで、殻のふたが首の付け根を横切る黒い線になる。
"""

import functools

import numpy as np
import pytest

from kcd_chara import body, params
from kcd_chara import mesh as M
from raycast import first_hit

NAMES = list(params.CHARACTERS)

# 殻の厚みは MaterialLibrary.CharacterOutlineWidth × カメラまでの距離（0.5〜5 m で頭打ち）
OUTLINE_WIDTH = 0.005


@functools.cache
def _build(name):
    p = params.resolve(name)
    mb = M.MeshBuilder()
    body.build_base(mb, p)
    return p, mb


def _triangles(mb, P, part):
    idx = set(mb.part_indices(part).tolist())
    tri = [(f[0], f[k], f[k + 1]) for f in mb.faces if f[0] in idx
           for k in range(1, len(f) - 1)]
    return P[np.array(tri)]


def _camera(mb):
    """CharacterPreview の全身の正面ショット: 高さは外接箱の中心、FOV 30°、水平に見る。

    距離は CharacterPreview.cs と同じく、背丈がちょうど収まる距離（縦の画角の半分 15°）を
    1.12 倍して上下に余白を取る。
    """
    V = np.asarray(mb.verts)
    front = np.sign(V[mb.part_indices("mouth"), 1].mean())
    lo, hi = V[:, 2].min(), V[:, 2].max()
    dist = (hi - lo) * 0.5 / np.tan(np.radians(15)) * 1.12
    return np.array([0.0, front * dist, (lo + hi) * 0.5])


def _hull(mb, C):
    """Outline パス（_OutlineScreenFlat = 1）で押し出した殻の頂点。

    カメラへ向かう成分は消し、カメラから離れる成分は 1/4 だけ残す。
    """
    V = np.asarray(mb.verts)
    n = mb.outline_normals()
    w = mb.outline_widths()
    d = V - C
    dist = np.linalg.norm(d, axis=1)
    view = d / dist[:, None]
    dot = np.einsum("ij,ij->i", n, view)
    n = n - (dot - 0.25 * np.maximum(dot, 0.0))[:, None] * view
    return V + n * (OUTLINE_WIDTH * w * np.clip(dist, 0.5, 5.0))[:, None]


def _back_facing(tri, C):
    n = np.cross(tri[:, 1] - tri[:, 0], tri[:, 2] - tri[:, 0])
    return tri[np.einsum("ij,ij->i", n, C - tri[:, 0]) < 0]


@pytest.mark.parametrize("name", NAMES)
def test_torso_hull_does_not_cross_neck_base(name):
    """首の前を狙った光線が、首より手前で胴の殻に当たらない。

    殻は胴の上端から上へ 2 cm 近く押し出されるので、上端の 1 cm 下から 4 cm 上までを見る。
    """
    p, mb = _build(name)
    V = np.asarray(mb.verts)
    C = _camera(mb)
    neck = _triangles(mb, V, "neck")
    near = np.concatenate([_triangles(mb, V, "torso"), _triangles(mb, V, "head")])
    hull = _back_facing(_triangles(mb, _hull(mb, C), "torso"), C)

    r = body.Anatomy(p).neck_r
    torso_top = V[mb.part_indices("torso"), 2].max()
    crossed = []
    for z in np.arange(torso_top - 0.01, torso_top + 0.04, 0.0005):
        for x in np.linspace(-0.8 * r, 0.8 * r, 9):
            d = np.array([x, 0.0, z]) - C
            d /= np.linalg.norm(d)
            t_neck = first_hit(C, d, neck)
            if not np.isfinite(t_neck) or first_hit(C, d, near) < t_neck:
                continue
            if first_hit(C, d, hull) < t_neck:
                crossed.append((round(x * 1000), round((z - torso_top) * 1000, 1)))
    assert not crossed, f"{name}: 胴の殻が首の前に出る (x mm, 胴の上端から z mm): {crossed[:8]}"


@pytest.mark.parametrize("name", NAMES)
def test_only_torso_top_loses_outline(name):
    """輪郭線を消すのは胴の上端のリングとふたの中心だけ。肩と首の輪郭は残す。

    重なった頂点は細いほうにそろうので、首の頂点が上端と重なると首の輪郭まで消える。
    """
    p, mb = _build(name)
    w = mb.outline_widths()
    top = body.torso_top_indices(mb)
    # 胴は 1 段 30 頂点のリングを重ね、上下のふたの中心を 1 つずつ足す
    assert len(top) == 31
    assert np.all(w[top] == body.TORSO_TOP_OUTLINE_WIDTH)
    rest = np.ones(len(w), dtype=bool)
    rest[top] = False
    # 鼻は別に細くしている (#47)
    rest[mb.part_indices("nose")] = False
    assert np.all(w[rest] == 1.0)
