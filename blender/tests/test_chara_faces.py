"""キャラの面の向き (#47)。

Unity の輪郭線は法線の向きへ押し出した殻の裏面なので、面が内向きの部位には外形の線が出ない。
add_grid で張った筒を 1 枚ずつ取り出し、隣り合うリング 2 本の重心から見て面の法線が外を向く
面積の割合を測る。
"""

import functools

import numpy as np
import pytest

from kcd_chara import body, cloth, hair, params
from kcd_chara import mesh as M


def _outward_share(rings, flip):
    rings = [np.asarray(r, dtype=float) for r in rings]
    n = len(rings[0])
    ok = total = 0.0
    for j in range(len(rings) - 1):
        c = 0.5 * (rings[j].mean(axis=0) + rings[j + 1].mean(axis=0))
        for i in range(n):
            i2 = (i + 1) % n
            q = [rings[j][i], rings[j][i2], rings[j + 1][i2], rings[j + 1][i]]
            if flip:
                q = q[::-1]
            nn = sum(np.cross(q[k], q[(k + 1) % 4]) for k in range(4))
            area = float(np.linalg.norm(nn))
            if area < 1e-14:
                continue
            total += area
            ok += area * (np.dot(nn, np.mean(q, axis=0) - c) > 0)
    return ok / total


@functools.cache
def _grids(name):
    """キャラを組み立て、部位ごとに add_grid の呼び出し順で外向きの割合を返す。"""
    grids = {}
    orig = M.MeshBuilder.add_grid

    def add_grid(self, rings, mat, **kw):
        if kw.get("close_u", True) and len(rings[0]) >= 3:
            part = self._stack[-1] if self._stack else ""
            grids.setdefault(part, []).append(
                _outward_share(rings, kw.get("flip", False)))
        return orig(self, rings, mat, **kw)

    M.MeshBuilder.add_grid = add_grid
    try:
        p = params.resolve(name)
        mb = M.MeshBuilder()
        a, head, fs, uv_box, _ = body.build_base(mb, p)
        hair.build_hair(mb, p, head, a, fs, uv_box)
        cloth.build_outfit(mb, p, a)
    finally:
        M.MeshBuilder.add_grid = orig
    return grids


OUTLINE_PARTS = (
    [("mirai", part) for part in ("head", "hand_l", "hand_r", "skirt")]
    + [("botchan", part) for part in ("head", "hand_l", "hand_r", "hakama",
                                      "hair_cap", "sleeve_l", "sleeve_r")]
    + [("madonna", part) for part in ("head", "hand_l", "hand_r", "hakama",
                                      "sleeve_l", "sleeve_r")]
)


@pytest.mark.parametrize("name,part", OUTLINE_PARTS)
def test_outline_parts_face_outward(name, part):
    assert min(_grids(name)[part]) > 0.95


@pytest.mark.parametrize("name", ["mirai", "madonna"])
def test_hair_shell_outer_out_inner_in(name):
    outer, inner = _grids(name)["hair_back"][:2]
    assert outer > 0.95  # 外殻は頭の外を向く
    assert inner < 0.05  # 内殻は頭の側を向く
