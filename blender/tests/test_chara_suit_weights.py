"""背広の胸の側（jacket_yoke）と、その下に隠れたシャツのウェイト (#172)。

距離のウェイトのままだと、休めの形で腰の横に下りた前腕と手が背広の脇を引っ張り、
腕を下ろす Idle で脇が浮いて、下のシャツとズボンが背広の外へ出ていた。
胸の側の腕の骨は脇の下より上だけに効かせ、腰の継ぎ目では裾の側（Hips）に揃える。
隠れたシャツには、法線の向きの光線が当たった背広のウェイトを写す。
"""

import functools

import numpy as np
import pytest

from kcd_chara import body, cloth, hair, params, rig
from kcd_chara import mesh as M
from test_chara_under_cloth import _FakeObj

SUIT_IDS = [cid for cid in params.ALL_IDS if params.CHARACTERS[cid]["outfit"] == "suit"]
BONES = ["Hips", "Chest", "LeftUpperArm", "LeftLowerArm", "LeftHand",
         "RightUpperArm", "RightLowerArm", "RightHand"]


@functools.cache
def _build(name):
    p = params.resolve(name)
    mb = M.MeshBuilder()
    a, head, fs, uv_box, _ = body.build_base(mb, p)
    hair.build_hair(mb, p, head, a, fs, uv_box)
    cloth.build_outfit(mb, p, a)
    return p, a, mb


def _arm_weights(pts):
    """どの頂点も胸と、その側の手に半分ずつ付く代役（腰の横で手に引かれる距離のウェイト）。"""
    return [{"Chest": 0.5, "LeftHand" if x > 0 else "RightHand": 0.5} for x in pts[:, 0]]


def test_prof_wears_the_suit():
    assert "prof" in SUIT_IDS


@pytest.mark.parametrize("name", SUIT_IDS)
def test_yoke_keeps_the_arms_above_the_armpit_only(name):
    """脇の下より下では腕の骨が 0、肩の近くでは元の割合のまま。どこも合計 1 で 4 本まで。"""
    p, _a, mb = _build(name)
    pts = np.array(mb.verts)
    obj = _FakeObj(_arm_weights(pts), list(BONES))
    rig._suit_yoke_weights(obj, mb, pts, p)
    h, z_sh = p["height"], p["z"]["shoulder"]
    low = high = 0
    for i in mb.part_indices("jacket_yoke").tolist():
        w = obj.w[i]
        assert abs(sum(w.values()) - 1.0) < 1e-9 and len(w) <= 4
        arm = sum(v for k, v in w.items() if k in rig.ARM_CHAIN)
        if pts[i, 2] < z_sh - h * 0.10:
            low += 1
            assert arm == 0.0, pts[i]
        elif pts[i, 2] > z_sh - h * 0.05:
            high += 1
            assert abs(arm - 0.5) < 1e-9, pts[i]
    assert low > 100 and high > 20


@pytest.mark.parametrize("name", SUIT_IDS)
def test_yoke_meets_the_hem_side_on_the_hips_at_the_seam(name):
    """腰の継ぎ目では、胸の側も裾の側の上端も Hips 100%。"""
    p, a, mb = _build(name)
    pts = np.array(mb.verts)
    obj = _FakeObj(_arm_weights(pts), list(BONES))
    rig._suit_yoke_weights(obj, mb, pts, p)
    yoke = mb.part_indices("jacket_yoke")
    seam = yoke[np.abs(pts[yoke, 2] - p["z"]["waist"]) < 1e-6]
    assert len(seam) > 10
    for i in seam.tolist():
        assert obj.w[i] == {"Hips": 1.0}
    rig._cloth_leg_weights(obj, mb, pts, a, "jacket")
    hem_side = mb.part_indices("jacket")
    top = hem_side[np.abs(pts[hem_side, 2] - p["z"]["waist"]) < 1e-6]
    assert len(top) > 10
    for i in top.tolist():
        assert obj.w[i] == {"Hips": 1.0}


@pytest.mark.parametrize("name", SUIT_IDS)
def test_hidden_shirt_takes_the_suit_weights(name):
    """背広に隠れたシャツには当たった背広の点のウェイトが付き、V の開きから見える
    シャツは元のまま。背広の頂点は触らない。"""
    p, _a, mb = _build(name)
    pts = np.array(mb.verts)
    outer = set(mb.part_indices(*rig.SUIT_COVER).tolist())
    obj = _FakeObj([{"Hips": 1.0} if i in outer else {"Chest": 1.0} for i in range(len(pts))],
                   list(BONES))
    before = [dict(w) for w in obj.w]
    rig._shirt_under_suit(obj, mb, pts, p)
    shirt = mb.part_indices("shirt").tolist()
    taken = [i for i in shirt if obj.w[i] == {"Hips": 1.0}]
    kept = [i for i in shirt if obj.w[i] == {"Chest": 1.0}]
    assert len(taken) + len(kept) == len(shirt)
    assert len(taken) > len(shirt) // 2
    # 残るのは前の中央の V の開きの所
    assert len(kept) > 0 and np.all(pts[kept, 1] < 0.0)
    for i in outer:
        assert obj.w[i] == before[i]


def test_nothing_changes_without_a_suit():
    """背広の無い衣装では、シャツにも胸の側にも触らない。"""
    name = "student_m"
    assert params.CHARACTERS[name]["outfit"] != "suit"
    p, _a, mb = _build(name)
    assert len(mb.part_indices("shirt")) > 0
    pts = np.array(mb.verts)
    weights = _arm_weights(pts)
    obj = _FakeObj([dict(w) for w in weights], list(BONES))
    rig._suit_yoke_weights(obj, mb, pts, p)
    rig._shirt_under_suit(obj, mb, pts, p)
    assert obj.w == weights
