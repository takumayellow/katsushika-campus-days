"""教授の頭身と頭の大きさを男子学生に揃える (#172)。

組んだメッシュから測る。
- 頭身: 全高（髪の上端から足の裏）を、頭の高さ（髪の上端から顎）で割る。
- 頭幅 / 肩の外幅: 頭幅は、頭と髪のうち顎から頭頂までの 6 割より上にある頂点の横幅。
  肩の外幅は、肩の高さの 3 cm 下を中心に上下 3 cm にある胴・服・腕の横幅。
- 肩幅 / 頭幅（README の表と同じ測り方）: 肩幅は、肩の高さから 5 mm 以内にある髪以外の頂点の
  横幅。頭幅は頭のパーツの横幅。
"""

import functools

import numpy as np
import pytest

from kcd_chara import body, cloth, hair, params
from kcd_chara import mesh as M

PAIR = ("prof", "student_m")
SHOULDER_PARTS = ("torso", "shirt", "jacket", "blouse", "collar", "sleeve", "arm", "lapel", "shoulder")


@functools.cache
def _build(name):
    p = params.resolve(name)
    mb = M.MeshBuilder()
    a, head, fs, uv_box, _ = body.build_base(mb, p)
    hair.build_hair(mb, p, head, a, fs, uv_box)
    cloth.build_outfit(mb, p, a)
    return p, mb


def _verts(mb, names):
    idx = mb.part_indices(*names)
    return np.asarray(mb.verts, dtype=float)[idx] if len(idx) else np.zeros((0, 3))


@functools.cache
def _measure(name):
    p, mb = _build(name)
    V = np.asarray(mb.verts, dtype=float)
    skull = _verts(mb, ["head"])
    hair_v = _verts(mb, [n for n in mb.parts if n.startswith("hair")])
    chin = p["z"]["chin"]
    skull_top = skull[:, 2].max()
    top = max(skull_top, hair_v[:, 2].max()) if len(hair_v) else skull_top
    heads = (top - V[:, 2].min()) / (top - chin)

    upper = np.vstack([skull, hair_v])
    upper = upper[upper[:, 2] > chin + 0.6 * (skull_top - chin)]
    sh = p["z"]["shoulder"]
    trunk = _verts(mb, [n for n in mb.parts if n.startswith(SHOULDER_PARTS)])
    outer = np.ptp(trunk[np.abs(trunk[:, 2] - (sh - 0.03)) < 0.03][:, 0])

    no_hair = _verts(mb, [n for n in mb.parts if not n.startswith("hair")])
    shoulder = np.ptp(no_hair[np.abs(no_hair[:, 2] - sh) < 0.005][:, 0])
    return {"heads": heads, "head_to_outer": np.ptp(upper[:, 0]) / outer,
            "shoulder_to_head": shoulder / np.ptp(skull[:, 0])}


def test_both_are_built():
    assert set(PAIR) <= set(params.ALL_IDS)


@pytest.mark.parametrize("name", PAIR)
def test_heads_tall(name):
    assert 5.6 <= _measure(name)["heads"] <= 5.9


@pytest.mark.parametrize("name", PAIR)
def test_head_width_against_the_outer_shoulders(name):
    assert 0.60 <= _measure(name)["head_to_outer"] <= 0.70


@pytest.mark.parametrize("name", PAIR)
def test_shoulder_width_against_the_head_as_in_the_readme(name):
    assert 1.40 <= _measure(name)["shoulder_to_head"] <= 1.55


def test_prof_matches_the_male_student():
    prof, student = _measure("prof"), _measure("student_m")
    assert abs(prof["heads"] - student["heads"]) < 0.1
    assert abs(prof["head_to_outer"] - student["head_to_outer"]) < 0.04
    assert abs(prof["shoulder_to_head"] - student["shoulder_to_head"]) < 0.05
