"""走りの脚の運び (#232)。

ランニングの脚の目安（README_characters.md の「歩き・走り」に出典つきで載せている）:

- 接地: 膝の屈曲は 20 度前後でほぼ伸び、すねは鉛直から 5〜8 度以内。足は体の少し前に降りる
- 立脚中期: 膝は 40〜45 度まで沈む
- 遊脚: 蹴り出した脚はすぐ畳み（膝 90 度以上）、腿は前で 50〜55 度まで上がる。
  前へ振った脚は伸ばし切らずに降ろす

関節の角度は gait_fk で組み立てた位置から測る。位相 0 が左足の接地。
2 頭身のキャラは歩幅に対して脚が短く接地がごく短いので、接地の形だけを見る。
歩きの脚は変えない。
"""

import pytest

import gait_fk
from kcd_chara import anim, params

TALL = [cid for cid in params.ALL_IDS if not params.resolve(cid).get("chibi")]
CYCLE = [i / 48 for i in range(48)]


def _cycle(name, kind="Run"):
    return [(ph, gait_fk.leg(gait_fk.joints(name, kind, ph), "Left")) for ph in CYCLE]


@pytest.mark.parametrize("name", params.ALL_IDS)
def test_run_lands_with_a_nearly_straight_knee(name):
    g = gait_fk.gait(name, "Run")
    c = gait_fk.leg(gait_fk.joints(name, "Run", 0.0), "Left")
    assert 12.0 < c["knee"] < 25.0, (name, c["knee"])
    # すねはほぼ鉛直（足が膝より前へ突き出さない）
    assert -5.0 < c["shin"] < 8.0, (name, c["shin"])
    # 足は腰の真下より少し前に降りる（脚長の 15〜30 %）
    assert 0.15 < c["foot_ahead"] / g.leg < 0.30, (name, c["foot_ahead"] / g.leg)


@pytest.mark.parametrize("name", TALL)
def test_run_stance_knee_sinks_to_mid_forties(name):
    g = gait_fk.gait(name, "Run")
    knee = max(L["knee"] for ph, L in _cycle(name) if ph < g.duty)
    assert 38.0 < knee < 50.0, (name, knee)


@pytest.mark.parametrize("name", TALL)
def test_run_swing_leg_folds_and_lifts_the_knee(name):
    g = gait_fk.gait(name, "Run")
    rows = _cycle(name)
    swing = [L for ph, L in rows if ph >= g.duty]
    assert max(L["knee"] for L in swing) > 85.0, name
    # 蹴り出した脚を後ろへ伸ばしたまま浮かせない
    folded = [L["knee"] for ph, L in rows if g.duty + 0.08 < ph < g.duty + 0.12]
    assert folded and min(folded) > 30.0, (name, folded)


@pytest.mark.parametrize("name", params.ALL_IDS)
def test_run_thigh_does_not_thrust_forward(name):
    """腿は前で 55 度まで。それ以上は膝を前へ突き出して走るように見える。"""
    hip = max(L["hip"] for ph, L in _cycle(name))
    assert hip < 55.0, (name, hip)


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
    ("botchan", 0.0, 21.37, 28.86), ("botchan", 0.5, 90.81, 9.59),
    ("madonna", 0.0, 20.47, 25.13), ("madonna", 0.5, 79.70, 12.40),
])
def test_walk_legs_are_unchanged(name, ph, knee, hip):
    """走りの脚の値は歩きに及ばない（値は歩きの脚の実測）。"""
    L = gait_fk.leg(gait_fk.joints(name, "Walk", ph), "Left")
    assert L["knee"] == pytest.approx(knee, abs=0.01)
    assert L["hip"] == pytest.approx(hip, abs=0.01)
