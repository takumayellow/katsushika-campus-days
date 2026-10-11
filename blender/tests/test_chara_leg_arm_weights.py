"""脚の肌から腕のウェイトを外す (#13)。

立ち姿では手が腿の横に下りているので、距離ウェイトだと腿の付け根に手や前腕が
乗る。腕を頭の横まで上げる Wave で、その頂点だけ手について行って腰から突き出ていた。
"""

import numpy as np

from kcd_chara import body, cloth, hair, params, rig
from kcd_chara import mesh as M
from test_chara_under_cloth import _FakeObj

NAMES = ["Hips", "LeftUpperLeg", "RightUpperLeg", "LeftHand", "LeftLowerArm",
         "RightLowerArm", "RightShoulder"]


class _FakeMB:
    def __init__(self, parts):
        self.parts = parts

    def part_indices(self, *names):
        return np.array(sorted({i for n in names for i in self.parts.get(n, ())}), dtype=int)


def _run(weights, parts):
    obj = _FakeObj([dict(w) for w in weights], NAMES)
    rig._legs_without_arms(obj, _FakeMB(parts))
    return obj.w


def test_arm_share_moves_to_the_leg_and_hips_left_on_the_vertex():
    w = _run([{"LeftHand": 0.5, "LeftUpperLeg": 0.3, "Hips": 0.2}], {"leg_l": [0]})
    assert set(w[0]) == {"LeftUpperLeg", "Hips"}
    assert abs(w[0]["LeftUpperLeg"] - 0.6) < 1e-12
    assert abs(w[0]["Hips"] - 0.4) < 1e-12


def test_vertex_only_on_the_arm_goes_to_its_own_thigh():
    w = _run([{"RightLowerArm": 0.7, "RightShoulder": 0.3}], {"leg_r": [0]})
    assert w[0] == {"RightUpperLeg": 1.0}


def test_other_parts_and_clean_leg_vertices_are_untouched():
    weights = [{"LeftHand": 0.4, "Hips": 0.6},      # 胴（腕を残す）
               {"LeftUpperLeg": 0.8, "Hips": 0.2}]  # 腕の乗っていない脚
    w = _run(weights, {"torso": [0], "leg_l": [1]})
    assert w == weights


def test_thigh_group_is_made_when_the_mesh_has_none_yet():
    obj = _FakeObj([{"LeftHand": 1.0}], ["LeftHand"])
    rig._legs_without_arms(obj, _FakeMB({"leg_l": [0]}))
    assert obj.w[0] == {"LeftUpperLeg": 1.0}


def test_tamoto_follows_only_its_own_upper_arm():
    weights = [{"Hips": 0.6, "Spine": 0.4},
               {"Hips": 0.5, "RightLowerArm": 0.5},
               {"LeftHand": 1.0}]                    # 袖口（袂ではない）
    obj = _FakeObj([dict(w) for w in weights], NAMES + ["Spine"])
    rig._tamoto_on_upper_arm(obj, _FakeMB({"tamoto_l": [0], "tamoto_r": [1]}))
    assert obj.w[0] == {"LeftUpperArm": 1.0}
    assert obj.w[1] == {"RightUpperArm": 1.0}
    assert obj.w[2] == weights[2]


def test_madonna_tamoto_hangs_from_each_sleeve():
    """袂の部位は袖の一部で、左右の袖の側にある。"""
    p = params.resolve("madonna")
    mb = M.MeshBuilder()
    a, head, fs, uv_box, _ = body.build_base(mb, p)
    hair.build_hair(mb, p, head, a, fs, uv_box)
    cloth.build_outfit(mb, p, a)
    pts = np.array(mb.verts)
    for sd, sgn in (("l", 1.0), ("r", -1.0)):
        tam = set(mb.part_indices(f"tamoto_{sd}").tolist())
        assert tam and tam <= set(mb.part_indices(f"sleeve_{sd}").tolist())
        assert np.all(pts[sorted(tam), 0] * sgn > 0.0)
