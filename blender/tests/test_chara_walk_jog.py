"""歩き（1.3 m/s）と小走り（2.6 m/s）の脚と腕 (#43)。

プレイヤーの既定の移動 2.6 m/s は、脚長 0.72 m のみらいでフルード数 0.96 に当たり、人なら走る速さ。
これを歩きの形で動かすと毎分 206 歩・接地 45 % の競歩に見えたので、2.6 m/s は滞空のある
小走り（Jog）にし、歩きは NPC がぶらぶら歩く 1.3 m/s に置く。人の目安を脚長で縮めて当てる:

- 歩き: 接地は 1 周期の 60 % 前後（両脚支持がある）、上下動 3〜5 cm、遊脚の膝 60 度前後、
  腿は前 30〜40 度。蹴り出しで脚を後ろへ伸ばし切らない
- 小走り: 滞空がある（接地 50 % 未満）、立脚中期の膝 40 度前後、遊脚の膝 65〜85 度、
  腿は前 40〜50 度。前腕は水平より下を向く

関節の角度は gait_fk で組み立てた位置から測る。位相 0 が左足の接地。
"""

import numpy as np
import pytest

import gait_fk
from kcd_chara import anim, params

TALL = [cid for cid in params.ALL_IDS if not params.resolve(cid).get("chibi")]
CHIBI = [cid for cid in params.ALL_IDS if params.resolve(cid).get("chibi")]
CYCLE = [i / 48 for i in range(48)]


def _legs(name, kind):
    return [(ph, gait_fk.leg(gait_fk.joints(name, kind, ph), "Left")) for ph in CYCLE]


def _bob(name, kind):
    return float(np.ptp([gait_fk.pelvis_z(name, kind, ph) for ph in CYCLE]))


def _steps_per_min(name, kind):
    return 2 * 60.0 * anim.FPS / gait_fk.gait(name, kind).nframes


@pytest.mark.parametrize("kind", ["Walk", "Jog", "Run"])
@pytest.mark.parametrize("name", params.ALL_IDS)
def test_clip_moves_at_the_blend_tree_speed(name, kind):
    """クリップの前進速度はゲーム側の閾値と同じ（足が滑らない）。"""
    g = gait_fk.gait(name, kind)
    assert g.stride / g.cycle == pytest.approx(anim.GAIT_TARGET[kind]["v"])


def test_jog_sits_between_walk_and_run():
    v = {k: anim.GAIT_TARGET[k]["v"] for k in ("Walk", "Jog", "Run")}
    assert v["Walk"] < v["Jog"] < v["Run"]
    assert anim.ACTION_NAMES.index("Walk") < anim.ACTION_NAMES.index("Jog") < anim.ACTION_NAMES.index("Run")


# -- 歩き ---------------------------------------------------------------------

@pytest.mark.parametrize("name", TALL)
def test_walk_has_double_support(name):
    """接地は 1 周期の 55 % を超える（両脚が着いている時間がある。45 % では競歩に見えた）。"""
    assert gait_fk.gait(name, "Walk").duty > 0.55, name


@pytest.mark.parametrize("name", TALL)
def test_walk_cadence(name):
    """毎分 110〜145 歩（人は 105〜120 歩。脚長が短いぶん速くなる）。"""
    assert 110.0 < _steps_per_min(name, "Walk") < 145.0, (name, _steps_per_min(name, "Walk"))


@pytest.mark.parametrize("name", TALL)
def test_walk_swing_leg_is_not_lifted_high(name):
    """遊脚の膝は 55〜68 度、腿は前へ 42 度まで（脚を持ち上げて歩かない）。"""
    rows = _legs(name, "Walk")
    g = gait_fk.gait(name, "Walk")
    knee = max(L["knee"] for ph, L in rows if ph >= g.duty)
    assert 55.0 < knee < 68.0, (name, knee)
    assert max(L["hip"] for ph, L in rows) < 42.0, name


@pytest.mark.parametrize("name", TALL)
def test_walk_push_off_is_not_locked_back(name):
    """蹴り出しで膝を伸ばし切らず（20 度以上）、腿を後ろへ 20 度より引かない。"""
    g = gait_fk.gait(name, "Walk")
    L = gait_fk.leg(gait_fk.joints(name, "Walk", g.duty), "Left")
    assert L["knee"] > 20.0, (name, L["knee"])
    assert L["hip"] > -20.0, (name, L["hip"])


@pytest.mark.parametrize("name", TALL)
def test_walk_bob_is_small(name):
    assert _bob(name, "Walk") < 0.04, (name, _bob(name, "Walk"))


@pytest.mark.parametrize("name", CHIBI)
def test_chibi_walk_bob_is_small(name):
    """2 頭身のキャラは脚長に対して速いが、上下動は脚長の 20 % 未満にとどめる（蹴り出しで跳ねない）。"""
    leg = gait_fk.gait(name, "Walk").leg
    assert _bob(name, "Walk") / leg < 0.20, (name, _bob(name, "Walk"), leg)


# -- 小走り -------------------------------------------------------------------

@pytest.mark.parametrize("name", params.ALL_IDS)
def test_jog_has_a_flight_phase(name):
    assert gait_fk.gait(name, "Jog").duty < 0.5, name


@pytest.mark.parametrize("name", TALL)
def test_jog_cadence_is_between_walk_and_run(name):
    """毎分 170〜215 歩で、歩きと走りのあいだ。1 歩は脚長と同じくらい。"""
    spm = _steps_per_min(name, "Jog")
    assert 170.0 < spm < 215.0, (name, spm)
    assert _steps_per_min(name, "Walk") < spm <= _steps_per_min(name, "Run"), name
    step = anim.GAIT_TARGET["Jog"]["v"] * 60.0 / spm / gait_fk.gait(name, "Jog").leg
    assert 0.9 < step < 1.3, (name, step)


@pytest.mark.parametrize("name", TALL)
def test_jog_legs(name):
    """接地で膝はほぼ伸び、立脚中期に 38〜48 度沈む。遊脚は膝 65〜85 度、腿は前 40〜50 度。"""
    g = gait_fk.gait(name, "Jog")
    rows = _legs(name, "Jog")
    assert 12.0 < rows[0][1]["knee"] < 28.0, (name, rows[0][1]["knee"])
    stance = max(L["knee"] for ph, L in rows if ph < g.duty)
    assert 38.0 < stance < 48.0, (name, stance)
    swing = max(L["knee"] for ph, L in rows if ph >= g.duty)
    assert 65.0 < swing < 85.0, (name, swing)
    assert 40.0 < max(L["hip"] for ph, L in rows) < 50.0, name
    assert min(L["hip"] for ph, L in rows) > -34.0, name


@pytest.mark.parametrize("name", TALL)
def test_jog_bob_is_small(name):
    """上下動は 6.5 cm 未満（人の小走りは 6〜8 cm。脚長が短いぶん小さい）。"""
    assert _bob(name, "Jog") < 0.065, (name, _bob(name, "Jog"))


@pytest.mark.parametrize("side", ["Left", "Right"])
@pytest.mark.parametrize("name", params.ALL_IDS)
def test_jog_forearm_points_down(name, side):
    """画面上の前腕は 1 周期を通して水平より下を向く（はさみを構えたように突き出さない）。"""
    rise = [gait_fk.arm(gait_fk.joints(name, "Jog", ph), side)["fore_rise"] for ph in CYCLE[::2]]
    assert max(rise) < -2.0, (name, side, max(rise))


@pytest.mark.parametrize("name", CHIBI)
def test_chibi_jog_legs_stay_clear_of_the_body(name):
    """2 頭身のキャラの膝は股関節の脚長の 35 % 以上下、足首は 60 % 以上下にある。"""
    leg = gait_fk.gait(name, "Jog").leg
    for ph in CYCLE:
        j = gait_fk.joints(name, "Jog", ph)
        for side in ("Left", "Right"):
            hip, knee, ank = j[side + "UpperLeg"], j[side + "LowerLeg"], j[side + "Foot"]
            assert (hip[2] - knee[2]) / leg > 0.35, (name, side, ph)
            assert (hip[2] - ank[2]) / leg > 0.60, (name, side, ph)
