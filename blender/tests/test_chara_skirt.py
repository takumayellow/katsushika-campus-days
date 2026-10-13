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
# 面が外向きのキャラ。面が内向きだと殻は体の中へ押し込まれ、輪郭線がもともと出ない
OUTWARD = [(cid, part) for cid, part in PLEATS if params.CHARACTERS[cid].get("outward_faces")]
# 殻が一番厚くなるのは 5 m より遠く
HULL_MAX = OUTLINE_WIDTH * 5.0


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


def _part_faces(mb, part):
    keep = set(mb.part_indices(part).tolist())
    return [f for f in mb.faces if all(i in keep for i in f)]


def test_outward_pleats_are_checked():
    assert {("mirai", "skirt"), ("botchan", "hakama"), ("madonna", "hakama")} <= set(OUTWARD)


@pytest.mark.parametrize("name,part", OUTWARD)
def test_outline_hull_does_not_fold_over_pleats(name, part):
    """輪郭線の殻を一番厚くしても、ヒダの面が 1 枚も裏返らない。

    陰の法線のまま押すと、ヒダの壁（法線が横を向く）で殻が隣のヒダを越えて折れ返り、
    谷ごとに黒い破線が出る。mirai のスカートは 936 面のうち 176 面が裏返っていた。
    坊っちゃんの袴は、裾の V の切れ込みで裾が 1 段上の段を越え、面そのものが 10 枚折れていた。
    """
    mb = _build(name)
    V = np.array(mb.verts)
    hull = V + HULL_MAX * mb.outline_normals()
    flipped = []
    for f in _part_faces(mb, part):
        a, b = V[list(f)], hull[list(f)]
        na = np.cross(a, np.roll(a, -1, axis=0)).sum(axis=0)
        nb = np.cross(b, np.roll(b, -1, axis=0)).sum(axis=0)
        if np.dot(na, nb) <= 0:
            flipped.append(f)
    assert not flipped, (name, part, len(flipped), flipped[:3])


@pytest.mark.parametrize("name,part", PLEATS)
def test_outline_normals_ignore_the_pleats(name, part):
    """殻の向きが、同じ段の隣の頂点とヒダの列の間隔ほどしか変わらない。

    ヒダを平らにならした格子の法線なので、段を 1 周するあいだに向きがなめらかに回る。
    陰の法線は、ヒダの壁をまたぐたびに 57〜66° 折れる。
    """
    mb = _build(name)
    V = np.array(mb.verts)
    on = mb.outline_normals()
    faces = _part_faces(mb, part)
    top = V[faces[0][0], 2]
    columns = sum(1 for i in mb.part_indices(part) if abs(V[i, 2] - top) < 1e-9)
    worst = 0.0
    for f in faces:
        for a, b in zip(f, f[1:] + f[:1]):
            if abs(V[a, 2] - V[b, 2]) < 1e-9:
                worst = max(worst, np.degrees(np.arccos(np.clip(on[a] @ on[b], -1.0, 1.0))))
    # 列の間隔（360° / 列の数）の 1.5 倍まで。直した後は 1.29〜1.41 倍
    assert worst < 1.5 * 360.0 / columns, (name, part, worst, columns)
