"""右手に提げるトートバッグ (#47)。

持ち手 2 本は握った指の内側で 1 本の束になり、指はその束を手の甲の側から
指先の側まで包む。腕を下ろした待機の姿勢では袋が握りの真下に下がり、口が
開いたまま、スカートと脚から離れる。指を握るのはトートを持つ右手だけで、
ほかの手は開いたまま。
"""

import functools
import math

import numpy as np
import pytest

from kcd_chara import body, cloth, hair, params
from kcd_chara import mesh as M
from raycast import first_hit

TOTE = [cid for cid in params.ALL_IDS if body.holds_tote(params.resolve(cid))]


@functools.cache
def _build(name, arm_drop=None):
    p = params.resolve(name)
    if arm_drop is not None:
        p = {**p, "arm_drop": arm_drop}
    mb = M.MeshBuilder()
    a, head, fs, uv_box, _ = body.build_base(mb, p)
    hair.build_hair(mb, p, head, a, fs, uv_box)
    cloth.build_outfit(mb, p, a)
    return p, a, mb


def _triangles(mb, idx):
    V = np.array(mb.verts)
    keep = set(np.asarray(idx).tolist())
    tri = [(f[0], f[k], f[k + 1]) for f in mb.faces if all(i in keep for i in f)
           for k in range(1, len(f) - 1)]
    return V[np.array(tri)]


def _pieces(mb, part):
    """part の頂点を、面でつながった塊ごとに分ける。"""
    idx = mb.part_indices(part).tolist()
    root = {i: i for i in idx}

    def find(i):
        while root[i] != i:
            root[i] = root[root[i]]
            i = root[i]
        return i

    for f in mb.faces:
        if f[0] in root:
            for v in f[1:]:
                root[find(v)] = find(f[0])
    groups = {}
    for i in idx:
        groups.setdefault(find(i), []).append(i)
    return [np.array(g) for g in groups.values()]


def _arm_down(p, a, pts):
    """右腕を肩を中心に arm_drop だけ下ろした位置（どのモーションも最初にこれを掛ける）。"""
    th = math.radians(p.get("arm_drop", body.ARM_DROP))
    c, s = math.cos(-th), math.sin(-th)
    rot = np.array([[c, 0.0, s], [0.0, 1.0, 0.0], [-s, 0.0, c]])
    sh = a.shoulder * np.array([-1.0, 1.0, 1.0])
    return (np.asarray(pts) - sh) @ rot.T + sh


def _bag_pieces(p, a, mb):
    """トートの袋と、持ち手 2 本。袋は腕を下ろしたときにいちばん下へ来る塊。"""
    V = np.array(mb.verts)
    pieces = _pieces(mb, "bag")
    low = min(range(len(pieces)), key=lambda k: _arm_down(p, a, V[pieces[k]])[:, 2].min())
    return pieces[low], [g for k, g in enumerate(pieces) if k != low]


def test_someone_holds_a_tote():
    assert TOTE


@pytest.mark.parametrize("name", TOTE)
def test_both_straps_gather_in_the_fist(name):
    p, a, mb = _build(name)
    hd = body.hand_frame(p, a, -1)
    grip = body.grip_hook(p, a)
    roll = body.GRIP_ROLL * p["height"]
    _, straps = _bag_pieces(p, a, mb)
    assert len(straps) == 2
    for s in straps:
        d = np.array(mb.verts)[s] - grip
        along = d @ hd.spread
        off = np.linalg.norm(d - np.outer(along, hd.spread), axis=1)
        fist = (np.abs(along) < hd.palm_w * 0.3) & (off < hd.palm_t)
        # どちらの持ち手も手の幅の中ほどを通り、指の鉤の内側の束（半径 roll）に収まる
        assert fist.sum() > 20
        assert off[fist].max() <= roll * 1.05


@pytest.mark.parametrize("name", TOTE)
def test_fingers_wrap_the_straps(name):
    """束の芯から、指の並びの各位置で手の甲・指先の側へ光線を飛ばすと指に当たる。

    束が手の甲の外や指先の先へずれると、その向きの光線が手に当たらなくなる。
    """
    p, a, mb = _build(name)
    hd = body.hand_frame(p, a, -1)
    grip = body.grip_hook(p, a)
    hand = _triangles(mb, mb.part_indices("hand_r"))
    for base, _ in body._finger_bases(hd):
        o = grip + hd.spread * float((base - grip) @ hd.spread)
        # -30° は指先の巻き込み、0° は指の中の節、60〜90° は手の甲の側
        for ang in (-30.0, 0.0, 60.0, 90.0):
            t = math.radians(ang)
            d = hd.f * math.cos(t) + hd.n_hat * math.sin(t)
            assert first_hit(o, d, hand) < hd.finger_len * 0.4, (name, ang)


@pytest.mark.parametrize("arm_drop", [None, 18.0, 34.0])
@pytest.mark.parametrize("name", TOTE)
def test_tote_hangs_plumb_with_the_arm_down(name, arm_drop):
    p, a, mb = _build(name, arm_drop)
    h = p["height"]
    grip = body.grip_hook(p, a)
    bag = _arm_down(p, a, np.array(mb.verts)[mb.part_indices("bag")])
    g = _arm_down(p, a, grip[None])[0]
    bottom = bag[bag[:, 2] < bag[:, 2].min() + h * 0.003].mean(axis=0) - g
    assert math.hypot(bottom[0], bottom[1]) < h * 0.003
    assert -bottom[2] > h * (cloth.TOTE_DROP + cloth.TOTE_H) * 0.95


@pytest.mark.parametrize("name", TOTE)
def test_tote_mouth_is_open(name):
    """口の縁の内側から真下へ落とした光線が、内張りの底まで届く。"""
    p, a, mb = _build(name)
    h = p["height"]
    bag, _ = _bag_pieces(p, a, mb)
    V = np.array(mb.verts)
    rim = _arm_down(p, a, V[bag])
    tri = _arm_down(p, a, _triangles(mb, bag).reshape(-1, 3)).reshape(-1, 3, 3)
    top = rim[rim[:, 2] > rim[:, 2].max() - h * 0.002]
    c = top.mean(axis=0)
    half = (top.max(axis=0) - top.min(axis=0)) * 0.5
    for ux in (-0.5, 0.0, 0.5):
        for uy in (-0.5, 0.0, 0.5):
            o = c + np.array([half[0] * ux, half[1] * uy, h * 0.002])
            depth = first_hit(o, np.array([0.0, 0.0, -1.0]), tri)
            assert depth > h * cloth.TOTE_H * 0.5, (name, ux, uy, depth / h)


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


def test_mitten_hand_cannot_hold_a_tote():
    """指を曲げないミトンの手に持たせると、束が拳の前で宙に浮くので作らない。"""
    name = next(cid for cid in params.ALL_IDS if params.resolve(cid).get("chibi"))
    p = params.resolve(name)
    p = {**p, "accessories": tuple(p.get("accessories", ())) + ("tote",)}
    mb = M.MeshBuilder()
    a, *_ = body.build_base(mb, p)
    with pytest.raises(ValueError):
        cloth._tote(mb, p, a)
