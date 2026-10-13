"""exterior.coarse_trunk: 遠い木の幹の輪の頂点を 1 つおきに抜く（#110）。"""

from collections import Counter

import pytest
from kcd_interior import exterior
from kcd_lib import trees
from kcd_lib.mesh import _newell

SPECIES = dict(trees.TREE_SPECIES)
# 幹の帯の数（trees._add_trunk の輪の数 - 1）と輪の角の数
BANDS = {"keyaki": (4, 12), "round": (2, 10), "pine": (1, 10)}


def _proto(species):
    """原型を load と同じ形（頂点、[(頂点番号, マテリアル, 色)]）にする。"""
    mb = SPECIES[species]()
    col = mb.face_col or {}
    faces = [(f, mb.mat_names[m], col.get(i)) for i, (f, m) in enumerate(zip(mb.faces, mb.face_mat))]
    return mb.verts, faces


def _tris(faces, mat):
    return sum(len(f[0]) - 2 for f in faces if f[1] == mat)


def _edges(pts, faces):
    out = Counter()
    for idx, _mat, _col in faces:
        ks = [exterior._key(pts[i]) for i in idx]
        for a, b in zip(ks, ks[1:] + ks[:1]):
            out[frozenset((a, b))] += 1
    return out


@pytest.mark.parametrize("species", sorted(BANDS))
def test_coarse_trunk_halves_every_trunk_ring(species):
    pts, faces = _proto(species)
    bands, seg = BANDS[species]
    rings = [b for b in exterior._trunk_bands(pts, faces) if len(b) == seg]

    coarse = exterior.coarse_trunk(pts, faces)

    assert len(rings) == bands
    quads = [f for f in coarse if f[1] == "trunk" and f not in faces]
    assert len([q for q in quads if len(q[0]) == 4]) == bands * seg // 2
    assert _tris(coarse, "trunk") < _tris(faces, "trunk") - bands * seg // 2


@pytest.mark.parametrize("species", sorted(BANDS))
def test_coarse_trunk_keeps_the_leaves_and_branches(species):
    pts, faces = _proto(species)
    ring_faces = {fi for b in exterior._trunk_bands(pts, faces) for fi in b}
    tops = {exterior._key(pts[faces[fi][0][3]]) for fi in ring_faces}

    coarse = exterior.coarse_trunk(pts, faces)

    kept = [f for i, f in enumerate(faces)
            if i not in ring_faces and not (f[1] == "trunk" and
                                            {exterior._key(pts[j]) for j in f[0]} <= tops)]
    assert all(f in coarse for f in kept)
    assert [f for f in coarse if f[1] == "leaf"] == [f for f in faces if f[1] == "leaf"]


@pytest.mark.parametrize("species", sorted(BANDS))
def test_coarse_trunk_has_no_gaps(species):
    """作り直した面の辺は、根元の輪を除いて 2 枚の面で共有される（帯の継ぎ目と蓋に隙間が無い）。"""
    pts, faces = _proto(species)
    coarse = exterior.coarse_trunk(pts, faces)
    edges = _edges(pts, coarse)
    new = [f for f in coarse if f not in faces]

    open_edges = []
    for idx, _mat, _col in new:
        ks = [exterior._key(pts[i]) for i in idx]
        for a, b in zip(ks, ks[1:] + ks[:1]):
            if a[2] == b[2] == 0.0:
                continue   # 根元の輪は地面に埋まる
            if edges[frozenset((a, b))] != 2:
                open_edges.append((a, b))

    assert new and open_edges == []


@pytest.mark.parametrize("species", sorted(BANDS))
def test_coarse_trunk_caps_face_up(species):
    pts, faces = _proto(species)
    coarse = exterior.coarse_trunk(pts, faces)
    top = max(pts[f[0][3]][2] for f in coarse if f[1] == "trunk" and len(f[0]) == 4
              and f not in faces)

    caps = [f for f in coarse if f not in faces and all(abs(pts[i][2] - top) < 1e-5 for i in f[0])]

    assert caps
    assert all(_newell([pts[i] for i in f[0]])[2] > 0.0 for f in caps)


def test_coarse_trunk_leaves_a_mesh_without_rings_alone():
    pts = [(0.0, 0.0, 0.0), (1.0, 0.0, 0.0), (0.0, 1.0, 0.5)]
    faces = [((0, 1, 2), "leaf", (0.1, 0.5, 0.1))]

    assert exterior.coarse_trunk(pts, faces) == faces
