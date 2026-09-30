"""セーラー服のプリーツスカート (#47)。

Unity の輪郭線（KCD_Toon.shader の Outline パス）は、FBX に書き出した角ごとの法線の向きへ
頂点を押し出した殻の裏面で描く。ヒダの面を 1 枚ずつ平らに塗ると、折り目の頂点が面ごとに
別の法線を持ち、殻が折り目で裂ける。裂け目からは本体の面が覗き、ヒダに沿った破線と
裾の下のギザギザになる。
"""

import functools

import numpy as np
import pytest

from kcd_chara import body, cloth, hair, params
from kcd_chara import mesh as M

# 3 m 離れて見たときの殻の厚み（MaterialLibrary.CharacterOutlineWidth 0.005 × 距離）
HULL_AT_3M = 0.005 * 3.0


@functools.cache
def _build(name):
    p = params.resolve(name)
    mb = M.MeshBuilder()
    a, head, fs, uv_box, _ = body.build_base(mb, p)
    hair.build_hair(mb, p, head, a, fs, uv_box)
    cloth.build_outfit(mb, p, a)
    return mb


SKIRT = [cid for cid in params.ALL_IDS if "skirt" in _build(cid).parts]


def _corner_normals(mb, part):
    """part の面の角ごとの法線を、頂点ごとに集める。

    FBX の書き出しと同じく、なめらかな面の角は頂点の周りのなめらかな面の法線の平均、
    平らな面の角はその面の法線になる。
    """
    V = np.array(mb.verts)
    keep = set(mb.part_indices(part).tolist())
    face_n = {}
    smooth_sum = {}
    for fi, f in enumerate(mb.faces):
        if not all(i in keep for i in f):
            continue
        q = V[list(f)]
        # 多角形の法線（長さは面積の 2 倍）
        n = sum(np.cross(q[k], q[(k + 1) % len(f)]) for k in range(len(f)))
        if np.linalg.norm(n) < 1e-14:
            continue
        face_n[fi] = n
        if mb.face_smooth[fi]:
            for i in f:
                smooth_sum[i] = smooth_sum.get(i, 0.0) + n
    corners = {}
    for fi, n in face_n.items():
        for i in mb.faces[fi]:
            c = smooth_sum[i] if mb.face_smooth[fi] else n
            corners.setdefault(i, []).append(c / np.linalg.norm(c))
    return corners


def test_seifuku_wears_a_skirt():
    assert {"mirai", "inari", "kaname", "sora"} <= set(SKIRT)


@pytest.mark.parametrize("name", SKIRT)
def test_skirt_outline_hull_stays_closed(name):
    """同じ頂点の角を殻の厚みだけ押し出した先が、0.1 mm 以内に集まる。"""
    corners = _corner_normals(_build(name), "skirt")
    assert len(corners) > 500
    tear = max(np.linalg.norm(np.array(ns) - ns[0], axis=1).max()
               for ns in corners.values())
    assert tear * HULL_AT_3M < 1e-4, (name, tear * HULL_AT_3M)
