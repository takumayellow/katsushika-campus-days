"""肩先と首の付け根があごから十分に下がっていること (#162)。

肩があごのすぐ下で水平に張ると、首がほとんど見えず、肩をすくめているように見える。
あごからの下がりを頭の高さ H（あご → 頭蓋の天端）で測る。実物の人は肩先が約 0.4 H、
首の付け根の横が約 0.2 H 下がる。アニメの頭身ではこれを少し詰め、肩先を 0.34 H 前後に置く。
ちび（坊っちゃん・マドンナちゃん）は公式でも首がほぼ見えないので対象にしない。
"""

import pytest

from kcd_chara import body, params

TALL = [n for n in params.CHARACTERS if not params.CHARACTERS[n].get("chibi")]


def _drops(name):
    p = params.resolve(name)
    a = body.Anatomy(p)
    zz, _, _ = body.torso_profile(p, a)
    chin, hh = p["z"]["chin"], p["head_h"]
    # 胴の輪郭は z["shoulder"] でいちばん広い（肩先）
    tip = p["z"]["shoulder"]
    return (chin - tip) / hh, (chin - zz[-1]) / hh, p, a


def test_tall_characters_are_listed():
    assert {"mirai", "inari", "kaname", "sora", "prof", "student_m"} <= set(TALL)


@pytest.mark.parametrize("name", TALL)
def test_shoulder_tip_sits_well_below_chin(name):
    tip, _, _, _ = _drops(name)
    assert 0.30 < tip < 0.45, f"{name}: 肩先があごの {tip:.3f} H 下"


@pytest.mark.parametrize("name", TALL)
def test_neck_base_sits_below_chin(name):
    tip, base, _, _ = _drops(name)
    assert 0.15 < base < tip, f"{name}: 首の付け根があごの {base:.3f} H 下"


@pytest.mark.parametrize("name", TALL)
def test_arm_root_hangs_from_shoulder_tip(name):
    tip, _, p, a = _drops(name)
    root = (p["z"]["chin"] - a.shoulder[2]) / p["head_h"]
    # 腕の付け根は肩先のすぐ下（0.12 H は肩の丸みの厚みの目安）。肩だけ下げて腕が残ると、肩の上に腕が浮く
    assert 0.0 < root - tip < 0.12, f"{name}: 付け根 {root:.3f} H / 肩先 {tip:.3f} H"


@pytest.mark.parametrize("name", list(params.CHARACTERS))
def test_torso_profile_keys_stay_in_order(name):
    """肩を下げても胸より上にあること（キーの順が崩れると胴の輪郭が折り返す）。

    0.045 は body._torso_profile が肩の下に置くキー（shoulder - 0.045 h）の深さ。
    """
    p = params.resolve(name)
    z = p["z"]
    assert z["shoulder"] - p["height"] * 0.045 > z["bust"], name
