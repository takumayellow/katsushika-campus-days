"""首の筒があごの下で折り返さず、上端が頭の中に埋まっていること (#47)。

首の筒は肩から頭の中までを数段のリングで張る。前側でリングの高さが上下に入れ替わると
面が折り返し、Unity の輪郭線の殻がその折り目に寄って、あごの下に首輪のような横線が出る。
筒の口（上端のリングとふた）が頭の外に出ると、黒い穴になって見える。
"""

import functools

import numpy as np
import pytest

from kcd_chara import body, params
from kcd_chara import mesh as M
from raycast import first_hit, hits

NAMES = list(params.CHARACTERS)


@functools.cache
def _build(name):
    p = params.resolve(name)
    mb = M.MeshBuilder()
    body.build_base(mb, p)
    return p, mb


def _faces(mb, part):
    idx = set(mb.part_indices(part).tolist())
    return [f for f in mb.faces if f[0] in idx]


def _triangles(mb, part):
    V = np.asarray(mb.verts)
    return V[np.array([(f[0], f[k], f[k + 1]) for f in _faces(mb, part)
                       for k in range(1, len(f) - 1)])]


def _top_opening(mb):
    """首の筒の上端のリングの頂点と、ふたの中心。"""
    V = np.asarray(mb.verts)
    caps = {}
    for f in _faces(mb, "neck"):
        if len(f) == 3:
            # ふたは中心の頂点から扇に張る
            caps.setdefault(f[0], set()).update(f[1:])
    center = max(caps, key=lambda c: V[c, 2])
    ring = sorted(caps[center])
    # 上のふたの扇は中心から張るので、リングの頂点が全部そろう
    assert len(ring) == 18, len(ring)
    return V[ring], V[center]


@pytest.mark.parametrize("name", NAMES)
def test_neck_rises_along_every_column(name):
    """筒の縦の辺はどれも上へ向かう。下がる所では面が折り返している。"""
    p, mb = _build(name)
    V = np.asarray(mb.verts)
    # add_grid の四角形は (下 i, 下 i+1, 上 i+1, 上 i) の順
    rise = [V[f[3], 2] - V[f[0], 2] for f in _faces(mb, "neck") if len(f) == 4]
    rise += [V[f[2], 2] - V[f[1], 2] for f in _faces(mb, "neck") if len(f) == 4]
    assert len(rise) > 0
    assert min(rise) > p["height"] * 0.005, f"{name}: {min(rise) * 1000:.1f} mm"


@pytest.mark.parametrize("name", NAMES)
def test_neck_opening_is_buried_in_head(name):
    """上端のリングとふたの中心が頭の中にあり、リングは頭の面から身長 × 0.005 以上内側。"""
    p, mb = _build(name)
    head = _triangles(mb, "head")
    ring, center = _top_opening(mb)
    up = np.array([1e-4, 2e-4, 1.0])
    up /= np.linalg.norm(up)
    for q in [*ring, center]:
        # 頭は閉じた面なので、上へ向けた光線が奇数回当たれば中にある
        assert len(hits(q, up, head)) % 2 == 1, f"{name}: {q} は頭の外"
    mid = ring.mean(axis=0)
    gap = []
    for q in ring:
        d = np.array([q[0] - mid[0], q[1] - mid[1], 0.0])
        gap.append(first_hit(q, d / np.linalg.norm(d), head))
    assert min(gap) > p["height"] * 0.005, f"{name}: {min(gap) * 1000:.1f} mm"
