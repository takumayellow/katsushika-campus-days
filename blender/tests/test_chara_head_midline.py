"""頭の正中が左右にずれないこと (#47)。

頭の変形は頬を横へ張り出させる。張り出す向きを sign(x) で決めると、正中の
列 (x = cos(pi/2) = 6e-17 など、丸めで 0 にならない) が左右の片方へ跳び、
鼻筋の列と後頭部の正中の列が頬の高さで 3〜7 mm ずれていた。
"""

import math

import numpy as np
import pytest

from kcd_chara import body, params
from kcd_chara import mesh as M


@pytest.fixture(scope="module", params=params.ALL_IDS)
def built(request):
    p = params.resolve(request.param)
    mb = M.MeshBuilder()
    _, head, _, _, _ = body.build_base(mb, p)
    return dict(name=request.param, mb=mb, head=head)


@pytest.mark.parametrize("part", ["head", "nose", "mouth"])
def test_head_is_mirror_symmetric(built, part):
    """頭 (耳を含む)・鼻・口の頂点を x で裏返すと、どれも元の頂点のどれかに重なる。

    口は頭の前面に沿わせて置くので、正中の列がずれると口も 0.7〜1.4 mm ゆがんでいた。
    """
    V = np.asarray(built["mb"].verts, dtype=float)[built["mb"].part_indices(part)]
    if not len(V):
        pytest.skip(f"{part} の無いキャラ")
    W = V * (-1.0, 1.0, 1.0)
    gap = np.concatenate([np.sqrt(((w[:, None, :] - V[None, :, :]) ** 2).sum(axis=2).min(axis=1))
                          for w in np.array_split(W, max(1, len(W) // 256))])
    assert gap.max() < 1e-6, (built["name"], part, float(gap.max()))


def test_head_surface_is_continuous_across_the_midline(built):
    """正中の両側のすぐ隣の点は、正面でも後ろでも同じ所にある。"""
    head = built["head"]
    el = np.linspace(0.05, math.pi - 0.05, 60)
    for mid in (0.5 * math.pi, 1.5 * math.pi):
        a = head.surface(np.full_like(el, mid - 1e-6), el)
        b = head.surface(np.full_like(el, mid + 1e-6), el)
        assert np.linalg.norm(a - b, axis=1).max() < 1e-5, (built["name"], mid)


@pytest.mark.parametrize("square", [0.0, 1.0])
def test_cheeks_bulge_sideways(square):
    """頬の高さの輪郭は、頭頂側の同じ高さの輪郭より 3% ほど横へ張る。

    張り出しを消すと 0.2〜0.3% しか違わない。正中をなめらかにしても、頬の
    張り出しそのものは残す。
    """
    a = np.linspace(0.0, 2 * math.pi, 720, endpoint=False)
    width = {}
    for z in (-0.26, 0.26):
        r = math.sqrt(1.0 - z * z)
        d = np.stack([np.cos(a) * r, np.sin(a) * r, np.full_like(a, z)], axis=1)
        width[z] = float(body._head_deform(d, square)[:, 0].max())
    assert width[-0.26] / width[0.26] > 1.02, width
