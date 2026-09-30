"""髪の殻の継ぎ目で、輪郭線の殻が裂けない (#47)。

髪は外殻・内殻・毛先の縁の帯を別々の add_grid で張るので、毛先では位置の同じ頂点が
番号違いで重なる。Unity の輪郭線（KCD_Toon.shader の Outline パス）は FBX の角ごとの法線の
向きへ押し出した殻で描くため、重なった頂点の法線がそろっていないと殻が毛先で裂け、
横髪の前の縁に沿った点線になる。頂点ごとに法線を見るテストでは、この裂け方は拾えない。
"""

import functools

import numpy as np
import pytest

from kcd_chara import body, hair, params
from kcd_chara import mesh as M

# 殻の厚みは MaterialLibrary.CharacterOutlineWidth × カメラまでの距離（5 m で頭打ち）
HULL_AT_3M = 0.005 * 3.0
PARTS = ("hair_back", "hair_front")


@functools.cache
def _build(name):
    p = params.resolve(name)
    mb = M.MeshBuilder()
    a, head, fs, uv_box, _ = body.build_base(mb, p)
    hair.build_hair(mb, p, head, a, fs, uv_box)
    return mb


def _corner_normals(mb, part):
    """part の頂点ごとに、FBX に書き出す角の法線を集める。

    カスタム法線を決めた頂点はその 1 本。決めていない頂点は、なめらかな面の角なら周りの
    なめらかな面の法線の和、平らな面の角ならその面の法線になる。
    """
    V = np.array(mb.verts)
    custom = mb.normal_array()
    keep = set(mb.part_indices(part).tolist())
    face_n = {}
    smooth_sum = {}
    for fi, f in enumerate(mb.faces):
        if not all(i in keep for i in f):
            continue
        q = V[list(f)]
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
            if np.linalg.norm(custom[i]) > 0:
                c = custom[i]
            else:
                c = smooth_sum[i] if mb.face_smooth[fi] else n
            corners.setdefault(i, []).append(c / np.linalg.norm(c))
    return corners


def _coincident(P, tol=1e-6):
    """位置が tol 以内で重なる頂点の組 (a, b)（a < b）。丸め方に左右されないよう総当たりで探す。"""
    pairs = []
    for s in range(0, len(P), 256):
        d = np.linalg.norm(P[s:s + 256, None] - P[None], axis=2)
        a, b = np.nonzero(d < tol)
        a = a + s
        keep = a < b
        pairs.extend(zip(a[keep].tolist(), b[keep].tolist()))
    return pairs


def _seams(name, part):
    """part の中で重なった頂点の組と、それぞれの角の法線。"""
    mb = _build(name)
    corners = _corner_normals(mb, part)
    idx = np.array(sorted(corners))
    if len(idx) == 0:
        return [], corners
    pairs = _coincident(np.array(mb.verts)[idx])
    return [(idx[a], idx[b]) for a, b in pairs], corners


@pytest.mark.parametrize("name,part", [(n, p) for n in params.ALL_IDS for p in PARTS])
def test_hair_seams_share_one_normal(name, part):
    """重なった頂点どうしで角の法線がそろい、3 m で輪郭の殻が裂けない。"""
    pairs, corners = _seams(name, part)
    if not corners:
        pytest.skip(f"{name} has no {part}")
    worst = 0.0
    for a, b in pairs:
        ns = np.array(corners[a] + corners[b])
        worst = max(worst, np.linalg.norm(ns[:, None] - ns[None], axis=2).max())
    # そろえる前は毛先で最大 30 mm 裂けていた
    assert worst * HULL_AT_3M < 1e-4, f"{name}/{part}: {worst * HULL_AT_3M * 1000:.1f} mm"


def test_mirai_hair_has_tip_seams():
    """上のテストが空振りしていない。みらいの後ろ髪は毛先で頂点が重なっている。"""
    pairs, _ = _seams("mirai", "hair_back")
    assert len(pairs) >= 100
