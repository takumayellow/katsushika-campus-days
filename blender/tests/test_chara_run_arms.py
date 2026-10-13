"""走りの腕の振り (#232)。

軽い小走り（ジョグ）の腕: 上腕は脇に下ろしたまま肩から前後に片振幅 10〜15 度だけ振り、
肘は 90 度前後で開閉は小さい（前で 10 度ほど深く、後ろで浅く曲がる）。胸の回旋もごく小さい。
手はバストより下、肩関節のほぼ真下を通る（横に開かず、体の中心線も越えない）。
2 頭身のキャラは胴が短く、同じ肘の角度では手が胸の高さに来るので、肘を開いて手を収める。
歩きの腕は #43 で見直し中なので変えない。

肘の曲がりと手の位置は胸から見たもの（胴のひねりと前傾は含めない）。ボーンの向きに
腕下ろしを合成したモーションの回転を当てて求める（test_chara_arms と同じ方法）。
上腕の振りと開きは画面で見える向き（胴の前傾と回旋を含む）で、gait_fk で組み立てた
関節の位置から測る。
"""

import math

import numpy as np
import pytest

import gait_fk
from kcd_chara import anim, body, params

TALL = [cid for cid in params.ALL_IDS if not params.resolve(cid).get("chibi")]
CHIBI = [cid for cid in params.ALL_IDS if params.resolve(cid).get("chibi")]
PHASES = np.linspace(-1.0, 1.0, 17)
CYCLE = [i / 24 for i in range(24)]


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


def _swing(kind, f, name=None):
    a = anim.GAIT_ARMS[kind]
    D = math.radians
    elbow = a["elbow"]
    if name is not None:    # キャラごとの上書き（params の gait）
        elbow = (params.resolve(name).get("gait") or {}).get(kind, {}).get("elbow", elbow)
    return anim.gait_arm_spec(f, D(a["arm"]), (D(elbow[0]), D(elbow[1])),
                              D(a["arm_in"]), D(a["abd"]), D(a["fwd"]))


def _arm(name, f, side):
    """腕を振る位相 f での (肩, 上腕, 前腕)。胸から見た向き。"""
    p = params.resolve(name)
    a = body.Anatomy(p)
    flip = np.array([1.0 if side == "Left" else -1.0, 1.0, 1.0])
    sh, el, wr = a.shoulder * flip, a.elbow * flip, a.wrist * flip
    spec = anim._arms_down(_swing("Run", f, name), body.arm_drop(p), body.elbow_open(p))
    Ru = _compose(spec[side + "UpperArm"])
    Rl = Ru @ _compose(spec[side + "LowerArm"])
    return sh, Ru @ (el - sh), Rl @ (wr - el)


def _bend(upper, fore):
    return math.degrees(math.acos(np.dot(upper, fore) / np.linalg.norm(upper) / np.linalg.norm(fore)))


def _world(name, side):
    """1 周期の画面上の腕の角度（gait_fk.arm）。"""
    return [gait_fk.arm(gait_fk.joints(name, "Run", ph), side) for ph in CYCLE]


def test_walk_arms_are_unchanged():
    """歩きの腕は #43 で保留中なので、値も回転も元のまま。"""
    assert anim.GAIT_ARMS["Walk"] == dict(arm=26.0, elbow=(14.0, 24.0), arm_in=0.0,
                                          abd=4.0, thx=0.70, fwd=0.0)
    D = math.radians
    for f in PHASES:
        spec = _swing("Walk", f)
        for side, sgn in (("Left", 1.0), ("Right", -1.0)):
            fs = f * sgn
            assert spec[side + "UpperArm"] == [("Y", D(-4.0) * sgn), ("X", D(26.0) * fs)]
            assert spec[side + "LowerArm"] == [("X", -(D(14.0) + D(24.0) * (0.5 - 0.5 * fs)))]


@pytest.mark.parametrize("side", ["Left", "Right"])
@pytest.mark.parametrize("name", params.ALL_IDS)
def test_run_upper_arm_swings_fore_and_aft_evenly(name, side):
    """画面上の上腕は前後とも 10〜16 度だけ振り、前傾した胴に引かれて後ろへ偏らない。"""
    sw = [a["swing"] for a in _world(name, side)]
    assert -16.0 < min(sw) < -10.0 and 10.0 < max(sw) < 16.0, (name, side, min(sw), max(sw))
    assert abs(max(sw) + min(sw)) < 6.0, (name, side, min(sw), max(sw))


@pytest.mark.parametrize("side", ["Left", "Right"])
@pytest.mark.parametrize("name", TALL)
def test_run_upper_arm_stays_down(name, side):
    """上腕は鉛直下向きから 20 度より開かない（肘を持ち上げたまま走らない）。"""
    lift = max(a["lift"] for a in _world(name, side))
    assert lift < 20.0, (name, side, lift)


@pytest.mark.parametrize("side", ["Left", "Right"])
@pytest.mark.parametrize("name", CHIBI)
def test_chibi_run_upper_arm_stays_down(name, side):
    """2 頭身のキャラは腕下ろしが浅い（まどんなは袂を見せるため 24 度開く）ので 36 度まで。"""
    lift = max(a["lift"] for a in _world(name, side))
    assert lift < 36.0, (name, side, lift)


@pytest.mark.parametrize("side", ["Left", "Right"])
@pytest.mark.parametrize("name", params.ALL_IDS)
def test_run_elbow_opens_and_closes_with_the_swing(name, side):
    bends = [_bend(*_arm(name, f, side)[1:]) for f in PHASES]
    low = 66.0 if name in CHIBI else 78.0     # 2 頭身は肘を開いて手を胸の下へ下げる
    assert low < min(bends) < 92.0 and 76.0 < max(bends) < 100.0, (name, side, min(bends), max(bends))
    # 開閉は小さい（10 度前後）。振りの間ずっと固まってもいない
    assert 6.0 < max(bends) - min(bends) < 16.0, (name, side, bends)
    # 前へ振ったとき（f=-1 は左腕が前）に深く、後ろへ振ったときに浅く曲がる
    front, back = (bends[0], bends[-1]) if side == "Left" else (bends[-1], bends[0])
    assert front - back > 6.0, (name, side, front, back)


@pytest.mark.parametrize("side", ["Left", "Right"])
@pytest.mark.parametrize("name", TALL)
def test_run_hand_stays_below_the_bust(name, side):
    """前へ振りきって肘が深く曲がっても、手首はバストより下を通る。"""
    bust = params.resolve(name)["z"]["bust"]
    for f in PHASES:
        sh, upper, fore = _arm(name, f, side)
        wrist = sh + upper + fore
        assert wrist[2] < bust, (name, side, f, wrist[2] - bust)


@pytest.mark.parametrize("side", ["Left", "Right"])
@pytest.mark.parametrize("name", CHIBI)
def test_chibi_run_hand_stays_below_the_chest(name, side):
    """2 頭身のキャラは胴が短い。肘を開いて、手首が胸（バスト）の高さを越えないようにする。"""
    bust = params.resolve(name)["z"]["bust"]
    for f in PHASES:
        sh, upper, fore = _arm(name, f, side)
        assert (sh + upper + fore)[2] < bust, (name, side, f, (sh + upper + fore)[2] - bust)


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
