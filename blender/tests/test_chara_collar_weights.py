"""襟と首の付け根のウェイト (#47)。

襟を Neck / UpperChest だけで動かすと、肩について動く身頃が襟を突き抜け、斜めから
見ると首の横に白い塊が出ていた。襟の頂点には、いちばん近い下の布の点のウェイトを
写す。写すと、首の付け根まで効いていた上腕のウェイトが襟にも乗り、腕を下ろすと
襟が後ろ髪を突き抜けた。上腕の付け根より胴の側は、骨の向きの距離を遠く数える。
"""

import functools

import numpy as np
import pytest

from kcd_chara import body, cloth, hair, params, rig
from kcd_chara import mesh as M
from test_chara_under_cloth import _FakeObj


@functools.cache
def _build(name):
    p = params.resolve(name)
    mb = M.MeshBuilder()
    a, head, fs, uv_box, _ = body.build_base(mb, p)
    hair.build_hair(mb, p, head, a, fs, uv_box)
    cloth.build_outfit(mb, p, a)
    return p, mb, a


def _seg_ref(p, a, b):
    """点 p から線分 ab までの距離（1 点ずつ）。"""
    d = b - a
    dd = float(d @ d)
    t = 0.0 if dd == 0.0 else min(max(float((p - a) @ d) / dd, 0.0), 1.0)
    return float(np.linalg.norm(p - (a + t * d)))


def _tri_dist_ref(p, A, B, C):
    """点 p から各三角形 (A[k], B[k], C[k]) までの距離。

    平面へ下ろした足が三角形の中なら平面までの距離、外なら 3 辺までの距離の最小。
    closest_points（Ericson の領域分け）とは別の組み立てで答え合わせする。
    """
    def seg(a, b):
        d = b - a
        dd = np.einsum("ij,ij->i", d, d)
        t = np.clip(np.einsum("ij,ij->i", p - a, d) / np.where(dd > 0, dd, 1.0), 0.0, 1.0)
        return np.linalg.norm(p - (a + t[:, None] * d), axis=1)

    edge = np.minimum(np.minimum(seg(A, B), seg(B, C)), seg(C, A))
    n = np.cross(B - A, C - A)
    nn = np.einsum("ij,ij->i", n, n)
    ok = nn > 1e-24
    h = np.einsum("ij,ij->i", p - A, n) / np.where(ok, nn, 1.0)
    q = p - h[:, None] * n
    inside = ok.copy()
    for a, b in ((A, B), (B, C), (C, A)):
        inside &= np.einsum("ij,ij->i", np.cross(b - a, q - a), n) >= 0.0
    plane = np.abs(h) * np.sqrt(nn)
    return np.where(inside, plane, edge)


def test_closest_points_agree_with_reference():
    rng = np.random.default_rng(7)
    verts = rng.normal(size=(60, 3))
    tris = rng.integers(0, 60, size=(40, 3))
    tris = tris[(tris[:, 0] != tris[:, 1]) & (tris[:, 1] != tris[:, 2]) & (tris[:, 0] != tris[:, 2])]
    pts = rng.normal(scale=1.5, size=(150, 3))
    res = M.closest_points(pts, verts, tris, chunk=16)
    A, B, C = verts[tris[:, 0]], verts[tris[:, 1]], verts[tris[:, 2]]
    for p, (j, u, v, dist) in zip(pts, res):
        ref = _tri_dist_ref(p, A, B, C)
        assert abs(dist - ref.min()) < 1e-9
        j = int(j)
        assert abs(ref[j] - ref.min()) < 1e-9
        assert u >= -1e-12 and v >= -1e-12 and u + v <= 1.0 + 1e-12
        q = A[j] * (1.0 - u - v) + B[j] * u + C[j] * v
        assert abs(np.linalg.norm(p - q) - dist) < 1e-9


def test_closest_points_on_degenerate_triangles():
    """面積 0 の三角形（1 点に潰れた・一直線に並んだ）でも NaN を出さず、点や線分
    までの距離を返す。三角形が無ければ番号は -1。"""
    verts = np.array([[0.0, 0.0, 0.0], [0.0, 0.0, 0.0], [0.0, 0.0, 0.0],
                      [1.0, 0.0, 0.0], [2.0, 0.0, 0.0], [3.0, 0.0, 0.0]])
    pts = np.array([[0.0, 1.0, 0.0], [2.5, 0.0, 2.0], [-1.0, 0.0, 0.0]])
    for tri, a, b in (([0, 1, 2], verts[0], verts[0]), ([3, 4, 5], verts[3], verts[5])):
        res = M.closest_points(pts, verts, np.array([tri]))
        assert np.isfinite(res).all()
        for p, (_j, _u, _v, dist) in zip(pts, res):
            assert abs(dist - _seg_ref(p, a, b)) < 1e-9
    empty = M.closest_points(pts, verts, np.zeros((0, 3), dtype=int))
    assert (empty[:, 0] == -1).all()


def test_closest_points_pass_over_a_sliver_among_real_triangles():
    """2 頂点が重なった三角形は、点の足が残りの辺の途中に来ると重心座標が 0/0 に
    なる。NaN の距離を最小と取り違えず、ちゃんとした三角形のほうを選ぶ。"""
    verts = np.array([[5.0, 0.0, 0.0], [5.0, 2.0, 0.0],
                      [0.0, 0.0, 0.0], [1.0, 0.0, 0.0], [0.0, 1.0, 0.0]])
    tris = np.array([[0, 0, 1], [2, 3, 4]])
    (j, u, v, dist), = M.closest_points([[0.2, 0.2, 0.5]], verts, tris)
    assert int(j) == 1
    assert abs(dist - 0.5) < 1e-12
    assert abs(u - 0.2) < 1e-12 and abs(v - 0.2) < 1e-12


def test_collar_keeps_its_weights_where_the_cloth_has_none():
    """拾った布の点にウェイトが無ければ、襟は Neck / UpperChest のまま。"""
    _p, mb, _a = _build("mirai")
    pts = np.array(mb.verts)
    collar = set(mb.part_indices("collar").tolist())
    weights = [{"Neck": 0.35, "UpperChest": 0.65} if i in collar else {}
               for i in range(len(pts))]
    obj = _FakeObj([dict(w) for w in weights], ["Neck", "UpperChest"])
    rig._collar_on_cloth(obj, mb, pts)
    assert obj.w == weights


def test_seg_distance_scales_only_behind_the_head():
    """付け根より手前 (s < 0) の点だけ、骨の向きの成分が behind 倍になる。"""
    rng = np.random.default_rng(3)
    p0, p1 = np.array([0.1, 0.0, 1.2]), np.array([0.3, 0.02, 1.0])
    pts = rng.normal(loc=0.15, scale=0.2, size=(300, 3)) + np.array([0.0, 0.0, 1.1])
    d = p1 - p0
    s = (pts - p0) @ d / (d @ d)
    plain = rig._seg_distance(pts, p0, p1)
    assert np.allclose(plain, [_seg_ref(p, p0, p1) for p in pts], atol=1e-12)
    far = rig._seg_distance(pts, p0, p1, 1.5)
    assert np.array_equal(far[s >= 0], plain[s >= 0])
    along = s * np.linalg.norm(d)
    perp = np.sqrt(np.maximum(plain ** 2 - along ** 2, 0.0))
    back = s < 0
    assert back.sum() > 50
    assert np.allclose(far[back], np.hypot(1.5 * along[back], perp[back]), atol=1e-9)


class _DWGroup:
    def __init__(self, w, name):
        self._w, self.name = w, name

    def add(self, idx, val, mode):
        assert mode == "ADD"
        for i in idx:
            self._w[i][self.name] = self._w[i].get(self.name, 0.0) + val


class _DWGroups(dict):
    def __init__(self, w):
        super().__init__()
        self._w = w

    def new(self, name):
        self[name] = _DWGroup(self._w, name)
        return self[name]


class _DWVerts:
    def __init__(self, pts):
        self._pts = pts

    def __len__(self):
        return len(self._pts)

    def foreach_get(self, attr, out):
        assert attr == "co"
        out[:] = self._pts.ravel()


class _DWObj:
    """distance_weights が触る vertex_groups.get/new と data.vertices だけの代役。"""

    def __init__(self, pts):
        self.w = [{} for _ in range(len(pts))]
        self.vertex_groups = _DWGroups(self.w)

        class _Data:
            pass

        self.data = _Data()
        self.data.vertices = _DWVerts(pts)


def _distance_weights(pts, pos):
    obj = _DWObj(pts)
    rig.distance_weights(obj, None, pos)
    return obj.w


UPPER_ARMS = ("LeftUpperArm", "RightUpperArm")
# 付け根で打ち切るだけの距離で、首の付け根に上腕が乗っていたキャラ。
REACHED_NECK_BASE = ("mirai", "madonna", "inari", "kaname", "sora")


@pytest.mark.parametrize("name", params.ALL_IDS)
def test_upper_arm_does_not_reach_the_neck_base(name, monkeypatch):
    """首の付け根（肩の付け根より内側・上）には上腕のウェイトが乗らない。

    距離を付け根で打ち切るだけだと、セーラー服の 4 人でここに上腕が最大 0.27
    乗っていた。遠く数えるのは付け根より手前の点だけなので、上腕が上位に
    入っていない頂点と、入っていても付け根より先にある頂点は元のまま。
    """
    p, mb, a = _build(name)
    pts = np.array(mb.verts)
    pos = rig.bone_positions(p, a)
    new = _distance_weights(pts, pos)
    monkeypatch.setattr(rig, "BEHIND_HEAD", {})
    old = _distance_weights(pts, pos)

    root = pos["LeftUpperArm"][0]
    neck = pos["Neck"][1]
    base = np.nonzero((np.abs(pts[:, 0]) <= 0.5 * root[0])
                      & (pts[:, 2] >= root[2]) & (pts[:, 2] <= neck[2]))[0]
    assert len(base) > 50
    if name in REACHED_NECK_BASE:
        assert any(set(old[i]) & set(UPPER_ARMS) for i in base.tolist())
    for i in base.tolist():
        assert not set(new[i]) & set(UPPER_ARMS)

    behind = {}
    for n in UPPER_ARMS:
        p0, p1 = pos[n]
        d = p1 - p0
        behind[n] = (pts - p0) @ d / (d @ d) < 0
    changed = 0
    for i in range(len(pts)):
        if any(n in old[i] and behind[n][i] for n in UPPER_ARMS):
            changed += new[i] != old[i]
            continue
        assert new[i] == old[i]
    assert changed > 100


def _collar_ids():
    return [cid for cid in params.ALL_IDS
            if len(_build(cid)[1].part_indices(*rig.COLLAR_BASE))]


@pytest.mark.parametrize("name", _collar_ids())
def test_collar_takes_the_weights_of_the_nearest_cloth_point(name):
    """襟の頂点は、いちばん近い下の布（身頃と袖）の点のウェイトになる。

    布には位置の 1 次式のウェイト X = 1+x, Y = 1+y, Z = z, C = 3-x-y-z を付ける。
    和がいつも 5 なので、写したウェイトから写した元の点 q = (5X-1, 5Y-1, 5Z) が
    そのまま戻る。q は布の上にあり、襟の頂点から布までの最短距離だけ離れている。
    """
    p, mb, _a = _build(name)
    pts = np.array(mb.verts)
    collar = mb.part_indices("collar")
    base = mb.part_indices(*rig.COLLAR_BASE, "sleeve_l", "sleeve_r")
    in_base = set(base.tolist())
    in_collar = set(collar.tolist())
    field = {i: {"X": 1.0 + pts[i, 0], "Y": 1.0 + pts[i, 1], "Z": pts[i, 2],
                 "C": 3.0 - pts[i, 0] - pts[i, 1] - pts[i, 2]} for i in in_base}
    weights = [dict(field[i]) if i in in_base
               else {"Neck": 0.35, "UpperChest": 0.65} if i in in_collar
               else {"Skin": 1.0} for i in range(len(pts))]
    obj = _FakeObj(weights, ["Skin", "Neck", "UpperChest", "X", "Y", "Z", "C"])
    rig._collar_on_cloth(obj, mb, pts)

    tris = M.triangles(mb.faces, base)
    A, B, C = pts[tris[:, 0]], pts[tris[:, 1]], pts[tris[:, 2]]
    for i in collar.tolist():
        w = obj.w[i]
        assert set(w) == {"X", "Y", "Z", "C"}
        assert abs(sum(w.values()) - 1.0) < 1e-9
    for i in collar[::13].tolist():
        w = obj.w[i]
        q = np.array([5.0 * w["X"] - 1.0, 5.0 * w["Y"] - 1.0, 5.0 * w["Z"]])
        assert _tri_dist_ref(q, A, B, C).min() < 1e-9
        assert abs(np.linalg.norm(q - pts[i]) - _tri_dist_ref(pts[i], A, B, C).min()) < 1e-9
    for i in range(len(pts)):
        if i not in in_collar:
            assert obj.w[i] == (field[i] if i in in_base else {"Skin": 1.0})


@pytest.mark.parametrize("name", [cid for cid in params.ALL_IDS
                                  if params.CHARACTERS[cid]["outfit"].startswith("kimono")])
def test_kimono_collar_keeps_the_spine_weights(name):
    """着物の衿は着物ごと背骨で動くので、Neck / UpperChest のまま。"""
    p, mb, _a = _build(name)
    pts = np.array(mb.verts)
    collar = set(mb.part_indices("collar").tolist())
    assert collar
    weights = [{"Neck": 0.35, "UpperChest": 0.65} if i in collar else {"Skin": 1.0}
               for i in range(len(pts))]
    obj = _FakeObj([dict(w) for w in weights], ["Skin", "Neck", "UpperChest"])
    rig._collar_on_cloth(obj, mb, pts)
    assert obj.w == weights
