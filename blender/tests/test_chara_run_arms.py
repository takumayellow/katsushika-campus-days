"""走りの腕の振り (#232)。

アニメの女の子の走りとして、上腕は前後 ±20〜30 度、肘は 80〜100 度でほぼ固定し、手は
バストより下・肩関節のほぼ真下を通る（横に開かず、体の中心線も越えない）。歩きの腕は #43 で見直し中なので変えない。

角度は胸から見たもの（胴のひねりと前傾は含めない）。ボーンの向きに腕下ろしを合成した
モーションの回転を当てて求める（test_chara_arms と同じ方法）。
"""

import math

import numpy as np
import pytest

from kcd_chara import anim, body, params

TALL = [cid for cid in params.ALL_IDS if not params.resolve(cid).get("chibi")]
CHIBI = [cid for cid in params.ALL_IDS if params.resolve(cid).get("chibi")]
PHASES = np.linspace(-1.0, 1.0, 17)


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


def _swing(kind, f):
    a = anim.GAIT_ARMS[kind]
    D = math.radians
    return anim.gait_arm_spec(f, D(a["arm"]), (D(a["elbow"][0]), D(a["elbow"][1])),
                              D(a["arm_in"]), D(a["abd"]))


def _arm(name, f, side):
    """走りの位相 f での (肩, 上腕, 前腕)。"""
    p = params.resolve(name)
    a = body.Anatomy(p)
    flip = np.array([1.0 if side == "Left" else -1.0, 1.0, 1.0])
    sh, el, wr = a.shoulder * flip, a.elbow * flip, a.wrist * flip
    spec = anim._arms_down(_swing("Run", f), body.arm_drop(p), body.elbow_open(p))
    Ru = _compose(spec[side + "UpperArm"])
    Rl = Ru @ _compose(spec[side + "LowerArm"])
    return sh, Ru @ (el - sh), Rl @ (wr - el)


def _front(v):
    """横から見て鉛直下向きから前（-y）へ何度出るか。"""
    return math.degrees(math.atan2(-v[1], -v[2]))


def _bend(upper, fore):
    return math.degrees(math.acos(np.dot(upper, fore) / np.linalg.norm(upper) / np.linalg.norm(fore)))


def test_walk_arms_are_unchanged():
    """歩きの腕は #43 で保留中なので、値も回転も元のまま。"""
    assert anim.GAIT_ARMS["Walk"] == dict(arm=26.0, elbow=(14.0, 24.0), arm_in=0.0,
                                          abd=4.0, thx=0.70)
    D = math.radians
    for f in PHASES:
        spec = _swing("Walk", f)
        for side, sgn in (("Left", 1.0), ("Right", -1.0)):
            fs = f * sgn
            assert spec[side + "UpperArm"] == [("Y", D(-4.0) * sgn), ("X", D(26.0) * fs)]
            assert spec[side + "LowerArm"] == [("X", -(D(14.0) + D(24.0) * (0.5 - 0.5 * fs)))]


@pytest.mark.parametrize("side", ["Left", "Right"])
@pytest.mark.parametrize("name", params.ALL_IDS)
def test_run_upper_arm_swings_within_30_degrees(name, side):
    fronts = [_front(_arm(name, f, side)[1]) for f in PHASES]
    assert -30.0 < min(fronts) and max(fronts) < 30.0, (name, side, min(fronts), max(fronts))
    # 前後とも 15 度以上は振る（棒立ちで走らない）
    assert min(fronts) < -15.0 and max(fronts) > 15.0, (name, side, fronts)


@pytest.mark.parametrize("side", ["Left", "Right"])
@pytest.mark.parametrize("name", params.ALL_IDS)
def test_run_elbow_stays_near_right_angle(name, side):
    bends = [_bend(*_arm(name, f, side)[1:]) for f in PHASES]
    assert 80.0 < min(bends) and max(bends) < 100.0, (name, side, min(bends), max(bends))
    assert max(bends) - min(bends) < 5.0, (name, side, bends)


@pytest.mark.parametrize("side", ["Left", "Right"])
@pytest.mark.parametrize("name", TALL)
def test_run_hand_stays_below_the_bust(name, side):
    bust = params.resolve(name)["z"]["bust"]
    for f in PHASES:
        sh, upper, fore = _arm(name, f, side)
        wrist = sh + upper + fore
        # 胸の前で手が跳ね上がらない。バストより 6 cm 以上下を通る
        assert wrist[2] < bust - 0.06, (name, side, f, wrist[2] - bust)


@pytest.mark.parametrize("side", ["Left", "Right"])
@pytest.mark.parametrize("name", CHIBI)
def test_chibi_run_hand_stays_near_the_bust(name, side):
    """2 頭身のキャラは胴が短く、肘を直角に曲げると手首がバストの高さに来る。3 cm までは許す。"""
    bust = params.resolve(name)["z"]["bust"]
    for f in PHASES:
        sh, upper, fore = _arm(name, f, side)
        assert (sh + upper + fore)[2] < bust + 0.03, (name, side, f)


@pytest.mark.parametrize("side", ["Left", "Right"])
@pytest.mark.parametrize("name", params.ALL_IDS)
def test_run_hand_stays_under_the_shoulder(name, side):
    """手首は体の中心線を越えず、肩関節から横へ 6 cm 以上は離れない（腕を横に開かない）。"""
    out = 1.0 if side == "Left" else -1.0
    for f in PHASES:
        sh, upper, fore = _arm(name, f, side)
        x = (sh + upper + fore)[0] * out
        assert x > 0.03, (name, side, f, x)
        assert x < sh[0] * out + 0.06, (name, side, f, x, sh[0] * out)
