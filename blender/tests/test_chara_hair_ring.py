"""髪の天使の輪の座標 (#47)。

髪の頂点に、高さと向き (u = 髪の頂上から測った高さ, v = 正面からの方位角の絶対値 / pi)
を 2 枚目の UV として書く。Unity の KCD/Toon が画素ごとにその座標から帯を塗る。
"""

import math

import numpy as np
import pytest

from kcd_chara import body, hair, params
from kcd_chara import mesh as M

CHARACTERS = ["mirai", "inari", "kaname", "sora", "prof", "botchan", "madonna"]


def _build(cid):
    p = params.resolve(cid)
    mb = M.MeshBuilder()
    a, head, fs, uv_box, _face_uvs = body.build_base(mb, p)
    hair.build_hair(mb, p, head, a, fs, uv_box)
    return mb, head


def _local(head, x, y, z):
    """頭の中心から、頭の半径を 1 とした座標で測った点。"""
    return head.center + np.array([x, y, z]) * head.scale


def _around(head, height, dist, azimuth_from_front):
    """高さ height、頭の軸から dist 離れた点。azimuth_from_front は正面 (-Y) から左 (+X) へ測る。"""
    az = hair.FRONT + azimuth_from_front
    return _local(head, dist * math.cos(az), dist * math.sin(az), height)


@pytest.fixture(scope="module")
def mirai():
    return _build("mirai")


def test_ring_coords_measure_from_the_top_and_the_front(mirai):
    _mb, head = mirai
    top = 1.2
    pts = [
        _around(head, top, 0.0, 0.0),  # 髪の頂上
        _around(head, 0.0, 1.0, 0.0),  # 頭の中心の高さ、正面
        _around(head, 0.0, 1.0, math.pi / 2),  # 同じ高さ、左の真横
        _around(head, 0.0, 1.0, math.pi),  # 同じ高さ、真後ろ
        _around(head, 0.6, 0.8, math.radians(60.0)),
        _around(head, -1.5, 0.5, 0.0),  # 頭の中心より下は 1 で止める
        _around(head, top + 0.1, 0.0, 0.0),  # 頂上より上は 0 で止める
    ]
    uv = hair.ring_coords(head, pts, top)
    np.testing.assert_allclose(uv[0, 0], 0.0, atol=1e-9)
    np.testing.assert_allclose(uv[1], [0.5, 0.0], atol=1e-9)
    np.testing.assert_allclose(uv[2], [0.5, 0.5], atol=1e-9)
    np.testing.assert_allclose(uv[3], [0.5, 1.0], atol=1e-9)
    np.testing.assert_allclose(uv[4], [0.25, 60.0 / 180.0], atol=1e-9)
    np.testing.assert_allclose(uv[5, 0], 1.0, atol=1e-9)
    np.testing.assert_allclose(uv[6, 0], 0.0, atol=1e-9)


def test_ring_coords_fold_left_and_right(mirai):
    """左右を絶対値で畳む。真後ろの左右どちらから近づいても v は 1 に向かい、継ぎ目で跳ばない。"""
    _mb, head = mirai
    left = [_around(head, 0.7, 0.8, a) for a in (0.3, 1.2, math.pi - 1e-4)]
    right = [_around(head, 0.7, 0.8, -a) for a in (0.3, 1.2, math.pi - 1e-4)]
    np.testing.assert_allclose(hair.ring_coords(head, left, 1.2), hair.ring_coords(head, right, 1.2), atol=1e-9)
    assert hair.ring_coords(head, left, 1.2)[-1, 1] > 0.999


def test_ring_coords_give_layers_at_the_same_height_the_same_u(mirai):
    """前髪の房が地の髪の上に重なっていても、同じ高さなら同じ u（帯の縁が層ごとにずれない）。"""
    _mb, head = mirai
    inner = _around(head, 0.8, 0.6, 0.2)
    outer = _around(head, 0.8, 0.9, 0.2)
    uv = hair.ring_coords(head, [inner, outer], 1.3)
    np.testing.assert_allclose(uv[0], uv[1], atol=1e-9)


def test_ring_coords_reject_a_top_below_the_head_centre(mirai):
    _mb, head = mirai
    with pytest.raises(ValueError):
        hair.ring_coords(head, [head.center], 0.0)


def test_ring_top_ignores_strands_away_from_the_head_axis(mirai):
    """横へ跳ねた房は頂上より高くても数えない。軸の近くに髪が無ければ全体の最高点を使う。"""
    _mb, head = mirai
    crown = _around(head, 1.1, 0.1, 0.0)
    side = _around(head, 1.4, 0.8, math.pi / 2)
    assert hair.ring_top(head, [crown, side]) == pytest.approx(1.1)
    assert hair.ring_top(head, [side]) == pytest.approx(1.4)


@pytest.mark.parametrize("cid", CHARACTERS)
def test_only_hair_vertices_get_ring_coords(cid):
    mb, head = _build(cid)
    uv = hair.ring_uvs(mb, head)
    assert uv.shape == (len(mb.verts), 2)
    hair_v = sorted({i for f, m in zip(mb.faces, mb.face_mat) if m == hair.RING_MATERIAL for i in f})
    assert hair_v
    other = np.ones(len(mb.verts), dtype=bool)
    other[hair_v] = False
    np.testing.assert_array_equal(uv[other], np.tile(hair.RING_OUTSIDE, (int(other.sum()), 1)))
    assert np.all((uv >= 0.0) & (uv <= 1.0))
    # 天使の輪を置く高さ（u = 0.1〜0.25。KCD_Toon の帯 0.13〜0.165 と歯の深さ 0.04 を包む）の髪が、前にも後ろにもある
    u, v = uv[hair_v, 0], uv[hair_v, 1]
    band = (u > 0.1) & (u < 0.25)
    assert np.any(band & (v < 0.2)) and np.any(band & (v > 0.8))
