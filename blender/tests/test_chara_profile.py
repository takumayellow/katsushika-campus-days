"""横顔で目と眉が顔の輪郭から前へ出ないこと (#47)。

顔パーツは頭の表面から -y へ浮かせて貼る。浮かせたぶんとドームのふくらみは、真横から見ると
そのまま輪郭の外へ出る。高さごとに頭の最前面と目・眉の最前面を比べ、出っ張りの最大値を測る。
"""

import functools

import numpy as np
import pytest

from kcd_chara import body, params
from kcd_chara import mesh as M


@functools.cache
def _stick_out(name):
    """頭の奥行きに対する、目と眉が頭の最前面より前へ出る量の最大値。"""
    p = params.resolve(name)
    mb = M.MeshBuilder()
    _, head, _, _, _ = body.build_base(mb, p)
    verts = np.asarray(mb.verts)

    az, el = np.meshgrid(np.linspace(0.0, 2 * np.pi, 480, endpoint=False),
                         np.linspace(0.01, np.pi - 0.01, 480))
    surf = head.surface(az.ravel(), el.ravel())
    parts = [k for k in mb.parts if k.startswith(("eye_", "brow_"))]
    feat = np.concatenate([verts[mb.part_indices(k)] for k in parts])

    step = p["head_h"] * 0.01
    worst = -np.inf
    for z in np.arange(feat[:, 2].min(), feat[:, 2].max() + step, step):
        near = feat[np.abs(feat[:, 2] - z) < step]
        rim = surf[np.abs(surf[:, 2] - z) < step]
        if len(near) and len(rim):
            worst = max(worst, rim[:, 1].min() - near[:, 1].min())
    return worst / p["head_d"]


@pytest.mark.parametrize("name", ["mirai", "botchan", "madonna"])
def test_eyes_stay_inside_profile(name):
    assert params.resolve(name).get("outward_faces")
    assert _stick_out(name) < 0.015
