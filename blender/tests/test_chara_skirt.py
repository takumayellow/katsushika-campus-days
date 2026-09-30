"""セーラー服のプリーツスカートと着物の袴 (#47)。

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

# 殻の厚みは MaterialLibrary.CharacterOutlineWidth × カメラまでの距離（5 m で頭打ち）
OUTLINE_WIDTH = 0.005
HULL_AT_3M = OUTLINE_WIDTH * 3.0

SKIRT = [cid for cid in params.ALL_IDS
         if params.CHARACTERS[cid]["outfit"].startswith("seifuku")]
HAKAMA = [cid for cid in params.ALL_IDS
          if params.CHARACTERS[cid]["outfit"].startswith("kimono")]
PLEATS = [(cid, "skirt") for cid in SKIRT] + [(cid, "hakama") for cid in HAKAMA]


@functools.cache
def _build(name):
    p = params.resolve(name)
    mb = M.MeshBuilder()
    a, head, fs, uv_box, _ = body.build_base(mb, p)
    hair.build_hair(mb, p, head, a, fs, uv_box)
    cloth.build_outfit(mb, p, a)
    return mb


def _corner_normals(mb, part):
    """part の面の角ごとの法線を、頂点ごとに集める。

    なめらかな面の角は頂点の周りのなめらかな面の法線を平均した 1 本、平らな面の角は
    その面の法線になる（FBX の書き出しと同じ分かれ方。平均の重みは Blender と違う）。
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


def test_kimono_wears_a_hakama():
    assert {"botchan", "madonna"} <= set(HAKAMA)


@pytest.mark.parametrize("name,part", PLEATS)
def test_pleat_corners_share_one_normal(name, part):
    """頂点ごとに角の法線が 1 本にそろい、輪郭の殻が折り目で裂けない。"""
    corners = _corner_normals(_build(name), part)
    # スカートはヒダ 26 本 × 4 列の格子が 10 段、袴は 10〜16 本 × 4 列が 17 段ある
    assert len(corners) > 500, (name, len(corners))
    spread = max(np.linalg.norm(np.array(ns) - ns[0], axis=1).max()
                 for ns in corners.values())
    # 3 m で殻が裂ける幅。面ごとの法線では裾の折り返しで 29〜30 mm 開く
    assert spread * HULL_AT_3M < 1e-4, (name, part, spread * HULL_AT_3M)
