"""走りの脚の運び (#232)。

人のふつうのランニングを脚長で縮めた脚。歩幅を取ってピッチを落とし（小走りに見せない）、
脚は高く上げない:

- 接地: 膝の屈曲は 20 度前後でほぼ伸び、すねは鉛直から 5〜8 度以内。足は体の少し前に降りる
- 立脚中期: 膝は 35〜45 度まで沈む（人は 40〜45 度）
- 遊脚: 蹴り出した脚はすぐ畳み（膝 65〜85 度）、腿は前で 40〜50 度まで上げる（人は 40〜50 度）。
  後ろへは 36 度まで引き、足を地面から高く離さない
- ピッチ: 背の高いキャラは毎分 260 歩以下（5.4 m/s のときは 360 歩で小走りに見えた）

関節の角度は gait_fk で組み立てた位置から測る。位相 0 が左足の接地。
2 頭身のキャラは歩幅に対して脚が短く接地がごく短いので、接地の形だけを見る。
歩きの脚は変えない。
"""

import pytest

import gait_fk
from kcd_chara import anim, params

TALL = [cid for cid in params.ALL_IDS if not params.resolve(cid).get("chibi")]
CHIBI = [cid for cid in params.ALL_IDS if params.resolve(cid).get("chibi")]
CYCLE = [i / 48 for i in range(48)]


def _cycle(name, kind="Run"):
    return [(ph, gait_fk.leg(gait_fk.joints(name, kind, ph), "Left")) for ph in CYCLE]


@pytest.mark.parametrize("name", params.ALL_IDS)
def test_run_lands_with_a_nearly_straight_knee(name):
    g = gait_fk.gait(name, "Run")
    c = gait_fk.leg(gait_fk.joints(name, "Run", 0.0), "Left")
    assert 12.0 < c["knee"] < 25.0, (name, c["knee"])
    # すねはほぼ鉛直（足が膝より前へ突き出さない）
    assert -8.0 < c["shin"] < 8.0, (name, c["shin"])
    # 足は腰の真下より少し前に降りる（脚長の 15〜30 %）
    assert 0.10 < c["foot_ahead"] / g.leg < 0.30, (name, c["foot_ahead"] / g.leg)


@pytest.mark.parametrize("name", TALL)
def test_run_stance_knee_sinks_to_about_forty(name):
    g = gait_fk.gait(name, "Run")
    knee = max(L["knee"] for ph, L in _cycle(name) if ph < g.duty)
    assert 35.0 < knee < 45.0, (name, knee)


@pytest.mark.parametrize("name", TALL)
def test_run_cadence_is_not_a_scurry(name):
    """1 周期（2 歩）のフレーム数から毎分の歩数を出す。260 歩を超えると小走りに見える。"""
    g = gait_fk.gait(name, "Run")
    steps_per_min = 2 * 60.0 * anim.FPS / g.nframes
    assert 200.0 < steps_per_min < 265.0, (name, steps_per_min)
    # 1 歩は脚長の 1.3 倍以上（人は 1.6 倍前後）
    assert anim.GAIT_TARGET["Run"]["v"] * 60.0 / steps_per_min / g.leg > 1.3, name


@pytest.mark.parametrize("name", TALL)
def test_run_swing_leg_folds_lightly(name):
    g = gait_fk.gait(name, "Run")
    rows = _cycle(name)
    swing = [L for ph, L in rows if ph >= g.duty]
    assert 65.0 < max(L["knee"] for L in swing) < 85.0, name
    # 蹴り出した脚を後ろへ伸ばしたまま浮かせない
    folded = [L["knee"] for ph, L in rows if g.duty + 0.08 < ph < g.duty + 0.12]
    assert folded and min(folded) > 22.0, (name, folded)


@pytest.mark.parametrize("name", TALL)
def test_run_thigh_does_not_thrust_forward(name):
    """腿は前で 40〜50 度まで。55 度を超えると脚を振り上げて見える (#232)。"""
    hip = max(L["hip"] for ph, L in _cycle(name))
    assert 40.0 < hip < 50.0, (name, hip)


@pytest.mark.parametrize("name", TALL)
def test_run_back_kick_is_small(name):
    """蹴り出した脚は後ろへ 36 度より引かず、足首は地面から脚長の 19 % より上がらない。"""
    rows = _cycle(name)
    assert min(L["hip"] for ph, L in rows) > -36.0, name
    z = [gait_fk.joints(name, "Run", ph)["LeftFoot"][2] for ph, L in rows]
    assert (max(z) - min(z)) / gait_fk.gait(name, "Run").leg < 0.19, name


@pytest.mark.parametrize("name", CHIBI)
def test_chibi_run_legs_stay_clear_of_the_body(name):
    """2 頭身のキャラは脚が短く、上げすぎると腿や足が体にめり込む。

    膝は常に股関節の脚長の 35 % 以上下にあり、足首は 60 % 以上下にある（足が腰へ蹴り上がらない）。
    左右の膝・足首は体の中心線を越えない（脚が交差して胴や反対の脚にめり込まない）。
    """
    leg = gait_fk.gait(name, "Run").leg
    for ph in CYCLE:
        j = gait_fk.joints(name, "Run", ph)
        for side, sgn in (("Left", 1.0), ("Right", -1.0)):
            hip, knee, ank = j[side + "UpperLeg"], j[side + "LowerLeg"], j[side + "Foot"]
            assert (hip[2] - knee[2]) / leg > 0.35, (name, side, ph)
            assert (hip[2] - ank[2]) / leg > 0.60, (name, side, ph)
            assert knee[0] * sgn > 0.0 and ank[0] * sgn > 0.0, (name, side, ph)
    assert max(L["hip"] for ph, L in _cycle(name)) < 36.0 and min(L["hip"] for ph, L in _cycle(name)) > -36.0


@pytest.mark.parametrize("name", TALL)
def test_run_leg_is_not_locked_straight_before_landing(name):
    """接地の直前に脚を伸ばし切って前へ投げ出さない。"""
    late = [L["knee"] for ph, L in _cycle(name) if ph > 0.80]
    assert min(late) > 15.0, (name, min(late))
    # 接地より前へ足を出しすぎて引き戻さない
    g = gait_fk.gait(name, "Run")
    ahead = max(L["foot_ahead"] for ph, L in _cycle(name) if ph > 0.80) / g.leg
    assert ahead < 0.35, (name, ahead)


def test_walk_target_is_unchanged():
    assert anim.GAIT_TARGET["Walk"] == dict(v=2.6, duty=0.45, reach=0.95, nf=(10, 22))


@pytest.mark.parametrize("name, ph, knee, hip", [
    ("mirai", 0.0, 18.25, 31.46), ("mirai", 0.25, 27.31, 6.27),
    ("mirai", 0.5, 11.52, -26.59), ("mirai", 0.75, 73.92, 40.65),
    ("botchan", 0.0, 21.46, 28.90), ("botchan", 0.5, 90.54, 8.42),
    ("madonna", 0.0, 20.62, 25.16), ("madonna", 0.5, 80.30, 12.35),
])
def test_walk_legs_are_unchanged(name, ph, knee, hip):
    """走りの脚の値は歩きに及ばない（値は歩きの脚の実測）。"""
    L = gait_fk.leg(gait_fk.joints(name, "Walk", ph), "Left")
    assert L["knee"] == pytest.approx(knee, abs=0.01)
    assert L["hip"] == pytest.approx(hip, abs=0.01)
