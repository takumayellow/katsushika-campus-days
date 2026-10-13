"""鼻の輪郭線を細くする (#47)。

鼻は幅数ミリの球で、顔と同じ太さ（1 m 先で 5 mm）の殻で囲むと、斜めから見たとき鼻より太い
黒い弧になる。鼻の頂点だけ、輪郭線の太さの倍率（MeshBuilder.outline_widths）を下げる。
"""

import numpy as np
import pytest

from kcd_chara import body, params
from kcd_chara import mesh as M


def _widths(cid):
    mb = M.MeshBuilder()
    body.build_base(mb, params.resolve(cid))
    return mb, mb.outline_widths()


@pytest.mark.parametrize("cid", ["mirai", "inari", "kaname", "sora", "prof", "student_m", "botchan"])
def test_nose_outline_is_thinner_than_the_face(cid):
    mb, w = _widths(cid)
    nose = mb.part_indices("nose")
    assert len(nose) > 0
    assert 0.0 < body.NOSE_OUTLINE_WIDTH < 1.0
    np.testing.assert_allclose(w[nose], body.NOSE_OUTLINE_WIDTH)
    # 鼻の周りの顔は細くしない（重なった頂点を細いほうにそろえても、顔までは及ばない）
    V = np.array(mb.verts)
    near = np.linalg.norm(V - V[nose].mean(axis=0), axis=1) < 0.03
    near[nose] = False
    assert near.sum() > 0
    assert np.all(w[near] == 1.0)


def test_characters_without_a_nose_keep_full_width():
    p = params.resolve("madonna")
    assert p.get("nose", True) is False
    mb, w = _widths("madonna")
    # 胴の上端は鼻と関係なく輪郭線を消している (#155)
    keep = np.ones(len(w), dtype=bool)
    keep[body.torso_top_indices(mb)] = False
    assert np.all(w[keep] == 1.0)
