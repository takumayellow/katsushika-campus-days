"""待機の腕 (#47)。

上腕は体側に沿って下り、前腕は肘から外へ開いて（運搬角）手が腰から離れる。正面から
見て肩から手首が鉛直から 12〜13° 外へ開き、横から見て前腕が前へ出すぎない。頭身の
低いキャラは前腕を開かない。

角度は胸から見たもの（胴の揺れは含めない）。ボーンの向きにモーションの回転（ワールド軸の
回転を順に掛けたもの。前腕には上腕の回転も掛かる）を当てて求める。
"""

import math

import numpy as np
import pytest

from kcd_chara import anim, body, params

TALL = [cid for cid in params.ALL_IDS if not params.resolve(cid).get("chibi")]
CHIBI = [cid for cid in params.ALL_IDS if params.resolve(cid).get("chibi")]
TIMES = np.linspace(0.0, 1.0, 9)


def _rot(axis, ang):
    c, s = math.cos(ang), math.sin(ang)
    if axis == "X":
        return np.array([[1.0, 0.0, 0.0], [0.0, c, -s], [0.0, s, c]])
    if axis == "Y":
        return np.array([[c, 0.0, s], [0.0, 1.0, 0.0], [-s, 0.0, c]])
    return np.array([[c, -s, 0.0], [s, c, 0.0], [0.0, 0.0, 1.0]])


def _compose(ops):
    R = np.eye(3)
    for axis, ang in ops:
        R = _rot(axis, ang) @ R
    return R


def _spec(p, poser, t):
    return anim._posed(poser, body.arm_drop(p), body.elbow_open(p))(t)


def _arm(name, t, side="Left", poser=anim._idle):
    """(上腕, 前腕) のベクトル。"""
    p = params.resolve(name)
    a = body.Anatomy(p)
    flip = np.array([1.0 if side == "Left" else -1.0, 1.0, 1.0])
    sh, el, wr = a.shoulder * flip, a.elbow * flip, a.wrist * flip
    spec = _spec(p, poser, t)
    Ru = _compose(spec[side + "UpperArm"])
    Rl = Ru @ _compose(spec[side + "LowerArm"])
    return Ru @ (el - sh), Rl @ (wr - el)


def _side(v, side="Left"):
    """正面から見て鉛直から外へ何度開くか（左は +x、右は -x が外）。"""
    out = v[0] if side == "Left" else -v[0]
    return math.degrees(math.atan2(out, -v[2]))


def _front(v):
    """横から見て鉛直から前（-y）へ何度出るか。"""
    return math.degrees(math.atan2(-v[1], -v[2]))


@pytest.mark.parametrize("side", ["Left", "Right"])
@pytest.mark.parametrize("name", TALL)
def test_idle_upper_arm_runs_along_the_side(name, side):
    for t in TIMES:
        upper, _ = _arm(name, t, side)
        # 待機の呼吸で ±1.5° 揺れる。鉛直から 10° を超えると脇が空いて見える
        assert 3.0 < _side(upper, side) < 10.0, (name, side, t)


@pytest.mark.parametrize("side", ["Left", "Right"])
@pytest.mark.parametrize("name", TALL)
def test_idle_forearm_carries_outward(name, side):
    """前腕が上腕より外へ開き（運搬角）、肩から手首が鉛直から約 12.6° になる。"""
    for t in TIMES:
        upper, fore = _arm(name, t, side)
        carry = _side(fore, side) - _side(upper, side)
        assert 8.0 < carry < 16.0, (name, side, t, carry)
        assert 10.5 < _side(upper + fore, side) < 15.0, (name, side, t)


@pytest.mark.parametrize("side", ["Left", "Right"])
@pytest.mark.parametrize("name", TALL)
def test_idle_forearm_is_not_pushed_forward(name, side):
    for t in TIMES:
        upper, fore = _arm(name, t, side)
        assert abs(_front(upper)) < 8.0, (name, side, t)
        assert 0.0 < _front(fore) < 15.0, (name, side, t)


@pytest.mark.parametrize("name", CHIBI)
def test_chibi_forearm_stays_as_modeled(name):
    """頭身の低いキャラは前腕を開かず、待機の前腕の回転は元のモーションのまま。"""
    p = params.resolve(name)
    assert body.elbow_open(p) == 0.0
    for t in TIMES:
        spec = _spec(p, anim._idle, t)
        for side in ("Left", "Right"):
            assert spec[side + "LowerArm"] == anim._idle(t)[side + "LowerArm"]


@pytest.mark.parametrize("name", TALL)
def test_wave_cancels_the_carry_on_the_waving_arm(name):
    """振る腕は腕下ろしも前腕の開きも打ち消して、どのキャラでも同じ高さで手を振る。

    振らない右腕には開きが残る。
    """
    p = params.resolve(name)
    d, e = body.arm_drop(p), body.elbow_open(p)
    assert e > 0.0
    for t in (0.3, 0.5, 0.7):
        opened = anim._posed(lambda u: anim._wave(u, d, e), d, e)(t)
        bare = anim._posed(lambda u: anim._wave(u, 0.0, 0.0), 0.0, 0.0)(t)
        for bone in ("LeftUpperArm", "LeftLowerArm"):
            assert np.allclose(_compose(opened[bone]), _compose(bare[bone]), atol=1e-9), (bone, t)
        assert not np.allclose(_compose(opened["RightLowerArm"]),
                               _compose(bare["RightLowerArm"]), atol=1e-3)


def _wave_spec(p, t):
    d, e, lift = body.arm_drop(p), body.elbow_open(p), body.arm_angles(p)[0]
    return anim._posed(lambda u: anim._wave(u, d, e, lift), d, e)(t)


def _wave_points(name, t):
    """振る左腕の (上腕, 前腕, 指先)。指先は手首の回転も含めた位置。"""
    p = params.resolve(name)
    a = body.Anatomy(p)
    spec = _wave_spec(p, t)
    Ru = _compose(spec["LeftUpperArm"])
    Rl = Ru @ _compose(spec["LeftLowerArm"])
    Rh = Rl @ _compose(spec.get("LeftHand", []))
    upper, fore = Ru @ (a.elbow - a.shoulder), Rl @ (a.wrist - a.elbow)
    tip = a.shoulder + upper + fore + Rh @ (a.hand_tip - a.wrist)
    return upper, fore, tip


def _elev(v):
    return math.degrees(math.atan2(v[2], math.hypot(v[0], v[1])))


HOLD = [anim.WAVE_PHASES[0] + anim.WAVE_PHASES[1] * k / 8 for k in range(9)]


@pytest.mark.parametrize("name", params.ALL_IDS)
def test_wave_raises_the_hand_beside_the_head(name):
    """振っている間は上腕を肩の高さに上げ、前腕を立てて、手を顔の横に出す (#13)。

    A ポーズの腕の角度はキャラごとに違う（頭身の低いキャラは深い）ので、それぞれの角度から
    持ち上げて同じ形にする。
    """
    p = params.resolve(name)
    for t in HOLD:
        upper, fore, tip = _wave_points(name, t)
        assert abs(_elev(upper)) < 3.0, (name, t, _elev(upper))
        assert 55.0 < _elev(fore) < 90.0, (name, t, _elev(fore))
        assert tip[0] > 0.0, (name, t)  # 振る手は体の左側にあって、顔の前を横切らない
        if p.get("chibi"):
            assert tip[2] > p["z"]["chin"], (name, t)
        else:
            assert abs(tip[2] - p["z"]["top"]) < 0.08, (name, t, tip[2] - p["z"]["top"])


@pytest.mark.parametrize("name", params.ALL_IDS)
def test_wave_swings_the_hand_side_to_side(name):
    """手は左右に振れ、上下にはあまり動かない。身長の 5% 以上振る。"""
    p = params.resolve(name)
    tips = np.array([_wave_points(name, t)[2] for t in HOLD])
    span = tips.max(axis=0) - tips.min(axis=0)
    assert span[0] > 0.05 * p["height"], (name, span)
    assert span[0] > 3.0 * span[2], (name, span)


@pytest.mark.parametrize("name", params.ALL_IDS)
def test_wave_starts_and_ends_on_the_rest_pose(name):
    """出だしと終わりは両腕・胴・頭とも待機の形で、ほかのクリップから切り替わっても跳ねない。"""
    p = params.resolve(name)
    d, e = body.arm_drop(p), body.elbow_open(p)
    rest = anim._posed(lambda u: {}, d, e)(0.0)
    for t in (0.0, 1.0):
        spec = _wave_spec(p, t)
        for bone in ("LeftUpperArm", "LeftLowerArm", "LeftHand", "RightUpperArm", "RightLowerArm",
                     "Spine", "UpperChest", "Head"):
            assert np.allclose(_compose(spec.get(bone, [])), _compose(rest.get(bone, [])), atol=1e-9), (bone, t)
