"""スカート・袴の前と後ろの中央を腿について行かせる骨 (#49)。

走ると前へ出た腿がスカートの前を、後ろへ蹴った腿が後ろを突き抜けていた。前と後ろの中央に
SkirtFront / SkirtBack を足し、裾の中央だけをそれに付ける（回す角度は anim.skirt_rotations と
Unity の SkirtFollow が決める。ここではウェイトの付き方だけを見る）。
"""

import functools

import numpy as np
import pytest

from kcd_chara import body, cloth, hair, params, rig
from kcd_chara import mesh as M
from test_chara_skirt import HAKAMA, SKIRT
from test_chara_under_cloth import _FakeObj

COVERS = [(cid, "skirt") for cid in SKIRT] + [(cid, "hakama") for cid in HAKAMA]


@functools.lru_cache(maxsize=None)
def _build(cid):
    p = params.resolve(cid)
    mb = M.MeshBuilder()
    a, head, fs, uv_box, _ = body.build_base(mb, p)
    hair.build_hair(mb, p, head, a, fs, uv_box)
    cloth.build_outfit(mb, p, a)
    return p, mb, a


@functools.lru_cache(maxsize=None)
def _weights(cid, part):
    """override_weights と同じ引数で布だけにウェイトを付け、(頂点の位置, ウェイト) を返す。"""
    p, mb, a = _build(cid)
    pts = np.array(mb.verts)
    idx = mb.part_indices(part)
    obj = _FakeObj([{"Hips": 1.0} for _ in range(len(pts))], ["Hips"])
    knee = part == "hakama" and "boots" not in p.get("accessories", ())
    rig._cloth_leg_weights(obj, mb, pts, a, part, knee=knee)
    return pts[idx], [obj.w[i] for i in idx.tolist()], a, knee


def test_every_skirt_and_hakama_is_covered():
    assert {"mirai", "inari", "kaname", "sora"} <= set(SKIRT)
    assert {"botchan", "madonna"} <= set(HAKAMA)


def test_skirt_bones_hang_from_the_hips_centre():
    assert dict(rig.SKIRT_HIERARCHY) == {"SkirtFront": "Hips", "SkirtBack": "Hips"}
    p = params.resolve("mirai")
    a = body.build_base(M.MeshBuilder(), p)[0]
    pos = rig.bone_positions(p, a)
    for name in rig.SKIRT_BONE_NAMES:
        head, tail = (np.asarray(v, dtype=float) for v in pos[name])
        assert abs(head[0]) < 1e-9 and abs(tail[0]) < 1e-9, name + " が左右の中央に無い"
        assert head[2] > tail[2], name + " が下を向いていない（腿と同じ向きでないと振り角が合わない）"


@pytest.mark.parametrize("cid,part", COVERS)
def test_weights_sum_to_one_with_at_most_four_bones(cid, part):
    _, ws, _, _ = _weights(cid, part)
    for w in ws:
        # 1e-4 以下のウェイトは書かないので、その分だけ 1 から欠ける（取り込みで正規化される）
        assert abs(sum(w.values()) - 1.0) < 5e-4
        assert len(w) <= 4


@pytest.mark.parametrize("cid,part", COVERS)
def test_front_bone_holds_the_front_hem_and_back_bone_the_back(cid, part):
    pts, ws, _, _ = _weights(cid, part)
    front = np.array([w.get("SkirtFront", 0.0) for w in ws])
    back = np.array([w.get("SkirtBack", 0.0) for w in ws])
    y = pts[:, 1]
    # Blender の前は -Y
    assert front.max() > 0.5, "前の中央の裾が SkirtFront に付いていない"
    assert back.max() > 0.5, "後ろの中央の裾が SkirtBack に付いていない"
    assert np.average(y, weights=front) < 0.0
    assert np.average(y, weights=back) > 0.0
    # 前に付けた分が後ろへ回り込んでいない（境は mix_front の幅でならすだけ）
    assert front[y > 0.0].sum() < 0.05 * front.sum()
    assert back[y < 0.0].sum() < 0.05 * back.sum()


@pytest.mark.parametrize("cid,part", COVERS)
def test_waist_stays_on_the_hips(cid, part):
    """腰まわり（t が mix_t - mix_band より上）は SkirtFront / SkirtBack に付けない。
    回る骨に付けると、走りで腰の線まで動く。"""
    pts, ws, _, _ = _weights(cid, part)
    cfg = rig.CLOTH_LEG[part]
    z = pts[:, 2]
    t = ((z.max() - z) / (z.max() - z.min())) ** cfg["hem_pow"]
    waist = t < cfg["mix_t"] - cfg["mix_band"]
    assert waist.any()
    for w, is_waist in zip(ws, waist):
        if is_waist:
            assert w.get("SkirtFront", 0.0) == 0.0 and w.get("SkirtBack", 0.0) == 0.0


@pytest.mark.parametrize("cid", HAKAMA)
def test_split_hakama_below_the_knee_follows_the_shins_not_the_skirt_bones(cid):
    pts, ws, a, knee = _weights(cid, "hakama")
    if not knee:
        pytest.skip("筒の行灯袴は膝で切り替えない")
    below = pts[:, 2] < float(a.knee[2]) - 0.01
    assert below.any()
    for w, is_below in zip(ws, below):
        if is_below:
            assert "SkirtFront" not in w and "SkirtBack" not in w


@pytest.mark.parametrize("part", ["jacket", "pants_seat"])
def test_other_leg_cloth_does_not_use_the_skirt_bones(part):
    for cid in params.ALL_IDS:
        _, mb, _ = _build(cid)
        if len(mb.part_indices(part)) == 0:
            continue
        _, ws, _, _ = _weights(cid, part)
        assert not any("SkirtFront" in w or "SkirtBack" in w for w in ws), cid
