"""パーカーに隠れたブラウスのウェイト (#156)。

sora はブラウスの上にパーカーを羽織る。ブラウスとパーカーの胴には別々に
自動ウェイトが付き、脇の下ではパーカーの方が上腕に強く付く。腕を下ろすと
パーカーだけが内へ寄り、ブラウスが外へ出て、脇の下に白い斑が見えていた。
ブラウスの頂点から法線の向きへ光線を撃ち、当たったパーカーのウェイトを写す。

ここでは Blender の自動ウェイトの代わりに、脇ほど上腕に付く 1 次式のウェイトを
パーカーに付け、腕を肩のまわりに回す線形ブレンドスキニングで腕を下ろす。
"""

import functools

import numpy as np
import pytest

from kcd_chara import body, cloth, hair, params, rig
from kcd_chara import mesh as M
from test_chara_under_cloth import _FakeObj

HOODIE_IDS = [cid for cid in params.ALL_IDS
              if params.CHARACTERS[cid]["outfit"] == "seifuku_hoodie"]
OUTER = rig.HOODIE_COVER
# 腕を下ろす角度（度）。Idle で上腕は A ポーズからこのくらい体へ寄る。
ARM_DOWN = 20.0


@functools.cache
def _build(name):
    p = params.resolve(name)
    mb = M.MeshBuilder()
    a, head, fs, uv_box, _ = body.build_base(mb, p)
    hair.build_hair(mb, p, head, a, fs, uv_box)
    cloth.build_outfit(mb, p, a)
    return p, a, mb


def _arm_share(pts, a):
    """脇ほど上腕に付く割合。体の中心から肩までを 0 → 1 に振る。"""
    x0 = a.shoulder[0] * 0.5
    return np.clip((np.abs(pts[:, 0]) - x0) / (a.shoulder[0] - x0), 0.0, 1.0)


def _rigged(name):
    """パーカー・袖は上腕と胸に分け、ブラウスと残りは胸だけに付けた代役。"""
    p, a, mb = _build(name)
    pts = np.array(mb.verts)
    outer = set(mb.part_indices(*OUTER).tolist())
    s = _arm_share(pts, a)
    weights = []
    for i in range(len(pts)):
        if i in outer and s[i] > 0.0:
            arm = "ArmL" if pts[i, 0] > 0 else "ArmR"
            weights.append({"Chest": 1.0 - s[i], arm: s[i]})
        else:
            weights.append({"Chest": 1.0})
    return p, a, mb, pts, _FakeObj(weights, ["Chest", "ArmL", "ArmR"])


def _lower_arms(pts, obj, a):
    """上腕のボーンを肩のまわりに体の方へ回し、ウェイトで混ぜる（線形ブレンド）。"""
    out = pts.copy()
    for arm, sgn in (("ArmL", 1.0), ("ArmR", -1.0)):
        th = np.radians(ARM_DOWN) * sgn
        c, s = np.cos(th), np.sin(th)
        rot = np.array([[c, 0.0, s], [0.0, 1.0, 0.0], [-s, 0.0, c]])
        pivot = np.array([a.shoulder[0] * sgn, 0.0, a.shoulder[2]])
        w = np.array([obj.w[i].get(arm, 0.0) for i in range(len(pts))])
        moved = (pts - pivot) @ rot.T + pivot
        out += (moved - pts) * w[:, None]
    return out


def _uncovered(mb, posed, p, a):
    """脇の下のブラウスの頂点のうち、法線の向きの光線がパーカーにも袖にも
    当たらず、内向きの光線がパーカーに当たる（＝パーカーの外へ出た）もの。"""
    bl = mb.part_indices("blouse")
    side = bl[(np.abs(posed[bl, 0]) > a.shoulder[0] * 0.5)
              & (posed[bl, 2] < a.shoulder[2])
              & (posed[bl, 2] > p["z"]["waist"])]
    n = M.vertex_normals(posed, M.faces_within(mb.faces, bl))
    tris = M.triangles(mb.faces, mb.part_indices(*OUTER))
    reach = p["height"] * 0.05
    out = M.first_hits(posed[side], n[side], posed, tris, reach)
    inn = M.first_hits(posed[side], -n[side], posed, tris, reach)
    return side, side[(out[:, 0] < 0) & (inn[:, 0] >= 0)]


@pytest.mark.parametrize("name", HOODIE_IDS)
def test_blouse_pokes_out_when_only_the_hoodie_follows_the_arm(name):
    """写さないと、腕を下ろしたとき脇の下のブラウスがパーカーの外へ出る。

    test_blouse_stays_under_the_hoodie_with_arms_down が、この代役のウェイトと
    腕の回し方で差を見分けられることの確かめ。"""
    p, a, mb, pts, obj = _rigged(name)
    side, poked = _uncovered(mb, _lower_arms(pts, obj, a), p, a)
    assert len(side) > 20
    assert len(poked) > 0


@pytest.mark.parametrize("name", HOODIE_IDS)
def test_blouse_stays_under_the_hoodie_with_arms_down(name):
    """写すと、腕を下ろしても脇の下のブラウスは全部パーカーか袖の内側に残る。"""
    p, a, mb, pts, obj = _rigged(name)
    rig._blouse_under_hoodie(obj, mb, pts, p)
    _side, poked = _uncovered(mb, _lower_arms(pts, obj, a), p, a)
    assert len(poked) == 0


@pytest.mark.parametrize("name", HOODIE_IDS)
def test_armpit_ray_hits_the_hoodie_first(name):
    """腕を下ろした姿勢で、脇の下へ横から撃った光線は、ブラウスより先に
    パーカーか袖に当たる。"""
    p, a, mb, pts, obj = _rigged(name)
    rig._blouse_under_hoodie(obj, mb, pts, p)
    posed = _lower_arms(pts, obj, a)
    outer = set(mb.part_indices(*OUTER).tolist())
    tris = M.triangles(mb.faces, mb.part_indices("blouse", *OUTER))
    zs = np.linspace(p["z"]["waist"], p["z"]["bust"], 6)
    hits = 0
    for sgn in (1.0, -1.0):
        for z in zs:
            for y in np.linspace(-0.04, 0.04, 5):
                origin = np.array([sgn * a.shoulder[0] * 2.0, y, z])
                (j, *_r), = M.first_hits(origin, [-sgn, 0.0, 0.0], posed, tris, 1.0)
                if j < 0:
                    continue
                hits += 1
                assert int(tris[int(j)][0]) in outer, (sgn, z, y)
    assert hits > 40


def test_nothing_changes_without_a_hoodie():
    """パーカーの無い衣装では、ブラウスのウェイトに触らない。"""
    name = next(cid for cid in params.ALL_IDS
                if params.CHARACTERS[cid]["outfit"] == "seifuku")
    p, _a, mb = _build(name)
    pts = np.array(mb.verts)
    weights = [{"Chest": 0.5, "ArmL": 0.5} for _ in range(len(pts))]
    obj = _FakeObj([dict(w) for w in weights], ["Chest", "ArmL"])
    rig._blouse_under_hoodie(obj, mb, pts, p)
    assert obj.w == weights


@pytest.mark.parametrize("name", HOODIE_IDS)
def test_blouse_takes_the_hoodie_weights(name):
    """ブラウスには光線が当たったパーカーの点のウェイトが付き、V の開きから
    覗く胸当てのように当たらない頂点は元のまま。パーカーの頂点は触らない。"""
    p, a, mb, pts, obj = _rigged(name)
    before = [dict(w) for w in obj.w]
    rig._blouse_under_hoodie(obj, mb, pts, p)
    bl = mb.part_indices("blouse")
    n = M.vertex_normals(pts, M.faces_within(mb.faces, bl))
    tris = M.triangles(mb.faces, mb.part_indices(*OUTER))
    hits = M.first_hits(pts[bl], n[bl], pts, tris, p["height"] * 0.02)
    s = _arm_share(pts, a)
    moved = 0
    for i, (j, u, v, _t) in zip(bl.tolist(), hits):
        if j < 0:
            assert obj.w[i] == before[i]
            continue
        moved += 1
        ta, tb, tc = (int(x) for x in tris[int(j)])
        share = s[ta] * (1.0 - u - v) + s[tb] * u + s[tc] * v
        arm = sum(w for k, w in obj.w[i].items() if k != "Chest")
        assert abs(arm - share) < 1e-9
    assert moved > len(bl) // 2
    for i in mb.part_indices(*OUTER).tolist():
        assert obj.w[i] == before[i]
