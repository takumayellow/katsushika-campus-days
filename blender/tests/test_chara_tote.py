"""右手に提げるトートバッグ (#47)。

持ち手 2 本は握った指の内側で 1 本の束になる。腕を下ろした待機の姿勢では
袋が握りの真下に下がり、スカートと脚から離れる。指を握るのはトートを持つ
右手だけで、ほかの手は開いたまま。
"""

import functools
import math

import numpy as np
import pytest

from kcd_chara import body, cloth, hair, params
from kcd_chara import mesh as M

TOTE = [cid for cid in params.ALL_IDS if body.holds_tote(params.resolve(cid))]


@functools.cache
def _build(name):
    p = params.resolve(name)
    mb = M.MeshBuilder()
    a, head, fs, uv_box, _ = body.build_base(mb, p)
    hair.build_hair(mb, p, head, a, fs, uv_box)
    cloth.build_outfit(mb, p, a)
    return p, a, mb


def _arm_down(p, a, pts):
    """右腕を肩を中心に arm_drop だけ下ろした位置（どのモーションも最初にこれを掛ける）。"""
    th = math.radians(p.get("arm_drop", body.ARM_DROP))
    c, s = math.cos(-th), math.sin(-th)
    rot = np.array([[c, 0.0, s], [0.0, 1.0, 0.0], [-s, 0.0, c]])
    sh = a.shoulder * np.array([-1.0, 1.0, 1.0])
    return (np.asarray(pts) - sh) @ rot.T + sh


def test_someone_holds_a_tote():
    assert TOTE


@pytest.mark.parametrize("name", TOTE)
def test_straps_gather_in_the_fist(name):
    p, a, mb = _build(name)
    hd = body.hand_frame(p, a, -1)
    grip, _ = body.grip_hook(p, a)
    roll = body.GRIP_ROLL * p["height"]
    bag = np.array(mb.verts)[mb.part_indices("bag")]
    d = bag - grip
    along = d @ hd.spread
    off = np.linalg.norm(d - np.outer(along, hd.spread), axis=1)
    fist = (np.abs(along) < hd.palm_w * 0.3) & (off < hd.palm_t)
    # 2 本とも手の幅の中ほどを通り、指の鉤の内側の束（半径 roll）に収まる
    assert fist.sum() > 40
    assert off[fist].max() <= roll * 1.05


@pytest.mark.parametrize("name", TOTE)
def test_tote_hangs_plumb_with_the_arm_down(name):
    p, a, mb = _build(name)
    h = p["height"]
    grip, _ = body.grip_hook(p, a)
    bag = _arm_down(p, a, np.array(mb.verts)[mb.part_indices("bag")])
    g = _arm_down(p, a, grip[None])[0]
    bottom = bag[bag[:, 2] < bag[:, 2].min() + h * 0.003].mean(axis=0) - g
    assert math.hypot(bottom[0], bottom[1]) < h * 0.003
    assert -bottom[2] > h * (cloth.TOTE_DROP + cloth.TOTE_H) * 0.95


@pytest.mark.parametrize("name", TOTE)
def test_tote_clears_the_skirt_and_leg(name):
    p, a, mb = _build(name)
    h = p["height"]
    V = np.array(mb.verts)
    bag = _arm_down(p, a, V[mb.part_indices("bag")])
    near = V[mb.part_indices("skirt", "leg_r", "socks_r")]
    checked = 0
    for z in np.arange(bag[:, 2].min(), bag[:, 2].max(), h * 0.01):
        b = bag[np.abs(bag[:, 2] - z) < h * 0.008]
        q = near[np.abs(near[:, 2] - z) < h * 0.008]
        if len(b) and len(q):
            checked += 1
            # 右は -x。袋の内側の面が体のいちばん外より外にある
            assert q[:, 0].min() - b[:, 0].max() > h * 0.004, (name, round(float(z), 3))
    assert checked > 5


def _reach(p, a, mb, sgn):
    """手首から指の向きへ、手のいちばん先までの距離。"""
    hd = body.hand_frame(p, a, sgn)
    side = "l" if sgn > 0 else "r"
    V = np.array(mb.verts)[mb.part_indices("hand_" + side)]
    return float(((V - hd.wr) @ hd.f).max())


@pytest.mark.parametrize("name", params.ALL_IDS)
def test_only_the_tote_hand_grips(name):
    p, a, mb = _build(name)
    h = p["height"]
    left, right = _reach(p, a, mb, 1), _reach(p, a, mb, -1)
    if body.holds_tote(p):
        # 指を手のひらへ巻き込むぶん、手の先が縮む
        assert right < left - h * 0.008, (right, left)
    else:
        assert abs(right - left) < h * 0.001, (right, left)
