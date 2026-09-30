"""布に隠れた胴の素肌のウェイト (#47)。

自動ウェイトは布とその下の肌で割合が揃わず、腕を下ろすと脇の肌がブラウスの外へ
出ていた。胴の頂点から法線の向きへ光線を撃ち、最初に当たった布の三角形の
ウェイトを当たった点で内分して写す。脚について動く布（スカート・袴など）に
当たった頂点は写さない。
"""

import functools

import numpy as np
import pytest

from kcd_chara import body, cloth, hair, params, rig
from kcd_chara import mesh as M
from raycast import first_hit


@functools.cache
def _build(name):
    p = params.resolve(name)
    mb = M.MeshBuilder()
    a, head, fs, uv_box, _ = body.build_base(mb, p)
    hair.build_hair(mb, p, head, a, fs, uv_box)
    cloth.build_outfit(mb, p, a)
    return p, mb


def _torso_normals(mb, pts):
    return M.vertex_normals(pts, M.faces_within(mb.faces, mb.part_indices("torso")))


def test_first_hits_agree_with_single_ray():
    """束で撃つ first_hits が、1 本ずつ撃つテスト側の実装と同じ t を返し、
    (u, v) から組み立てた点が光線の上に乗る。

    raycast.first_hit は別に書いた答え合わせ用なので、first_hits とまとめない。"""
    p, mb = _build("mirai")
    pts = np.array(mb.verts)
    skin = mb.part_indices("torso")[::7]
    n = _torso_normals(mb, pts)
    tris = M.triangles(mb.faces, mb.part_indices("blouse"))
    reach = p["height"] * 0.02
    hits = M.first_hits(pts[skin], n[skin], pts, tris, reach)
    assert (hits[:, 0] >= 0).sum() > 20
    for i, (j, u, v, t) in zip(skin.tolist(), hits):
        ref = first_hit(pts[i], n[i], pts[tris])
        if j < 0:
            assert not ref < reach
            continue
        assert abs(t - ref) < 1e-9
        a, b, c = pts[tris[int(j)]]
        assert np.allclose(a * (1 - u - v) + b * u + c * v, pts[i] + n[i] * t, atol=1e-9)


def test_cover_weights_blend_at_the_hit_point():
    """当たった点の重心座標で内分し、上位 4 本で正規化する。届かない頂点と、
    W に無い三角形に当たった頂点は None。"""
    pts = np.array([
        [0.0, 0.0, 0.01], [1.0, 0.0, 0.01], [0.0, 1.0, 0.01],   # 写す元の布
        [5.0, 0.0, 0.01], [6.0, 0.0, 0.01], [5.0, 1.0, 0.01],   # W に無い布
        [0.2, 0.3, 0.0], [5.2, 0.3, 0.0], [0.2, 0.3, -0.05],     # 肌
    ])
    tris = np.array([[0, 1, 2], [3, 4, 5]])
    normals = np.tile([0.0, 0.0, 1.0], (len(pts), 1))
    W = {0: {"A": 1.0},
         1: {"B": 0.6, "E": 0.4},
         2: {"C": 0.5, "D": 0.3, "E": 0.2}}
    out = rig._cover_weights(pts, [6, 7, 8], normals, tris, W, reach=0.02)
    assert out[1] is None and out[2] is None
    w = out[0]
    # 当たった点 (0.2, 0.3) の重心座標は a=0.5, b=0.2, c=0.3
    raw = {"A": 0.5, "B": 0.2 * 0.6, "C": 0.3 * 0.5, "D": 0.3 * 0.3,
           "E": 0.2 * 0.4 + 0.3 * 0.2}
    top = sorted(raw.items(), key=lambda kv: kv[1], reverse=True)[:4]
    tot = sum(v for _, v in top)
    assert set(w) == {k for k, _ in top} and "D" not in w
    for k, v in top:
        assert abs(w[k] - v / tot) < 1e-12


class _Group:
    def __init__(self, group, weight):
        self.group, self.weight = group, weight


class _Vertex:
    def __init__(self, obj, i):
        self._obj, self._i = obj, i

    @property
    def groups(self):
        return [_Group(self._obj.index[name], w) for name, w in self._obj.w[self._i].items()]


class _VertexGroup:
    def __init__(self, obj, name):
        self._obj, self.name, self.index = obj, name, obj.index[name]

    def remove(self, idx):
        for i in idx:
            self._obj.w[i].pop(self.name, None)

    def add(self, idx, val, mode):
        for i in idx:
            self._obj.w[i][self.name] = val


class _Groups(list):
    def __getitem__(self, key):
        if isinstance(key, str):
            return next(g for g in self if g.name == key)
        return super().__getitem__(key)


class _FakeObj:
    """rig が触る vertex_groups / data.vertices[i].groups だけを持つ代役。"""

    def __init__(self, weights, names):
        self.w = weights
        self.index = {n: k for k, n in enumerate(names)}
        self.vertex_groups = _Groups(_VertexGroup(self, n) for n in names)

        class _Data:
            pass

        self.data = _Data()
        self.data.vertices = [_Vertex(self, i) for i in range(len(weights))]


@pytest.mark.parametrize("name", params.ALL_IDS)
def test_torso_under_cloth_takes_the_cloth_weights(name):
    """布の下の胴は布と同じウェイトになり、脚の布の下と布の外の胴は元のまま。

    布には位置の 1 次式のウェイトを付ける。三角形の中で内分すると 1 次式の値に
    そのまま戻るので、写した値は「光線が当たった点での 1 次式」になり、肌の頂点
    からは光線の長さ（身長の 2%）以上離れない。
    """
    p, mb = _build(name)
    pts = np.array(mb.verts)
    skin = mb.part_indices("torso")
    under = set(mb.part_indices(*rig.UNDER_CLOTH).tolist())
    leg = set(mb.part_indices(*rig.LEG_CLOTH).tolist())
    fa = 1.0 + pts[:, 0] + 0.5 * pts[:, 2]
    fb = 2.0 - pts[:, 1] + 0.25 * pts[:, 2]
    weights = [({"ClothA": fa[i], "ClothB": fb[i]} if i in under
                else {"Leg": 1.0} if i in leg else {"Skin": 1.0})
               for i in range(len(pts))]
    obj = _FakeObj(weights, ["Skin", "Leg", "ClothA", "ClothB"])
    rig._hide_under_cloth(obj, mb, pts, p)

    reach = p["height"] * 0.02
    tris = M.triangles(mb.faces, mb.part_indices(*rig.UNDER_CLOTH, *rig.LEG_CLOTH))
    n = _torso_normals(mb, pts)
    hits = M.first_hits(pts[skin], n[skin], pts, tris, reach)
    moved = 0
    for i, (j, _u, _v, _t) in zip(skin.tolist(), hits):
        w = obj.w[i]
        if j < 0 or not all(int(c) in under for c in tris[int(j)]):
            assert w == {"Skin": 1.0}
            continue
        moved += 1
        assert set(w) == {"ClothA", "ClothB"}
        assert abs(sum(w.values()) - 1.0) < 1e-9
        # ClothA の割合は、肌の頂点での値から光線の長さぶんしか動かない
        share = fa[i] / (fa[i] + fb[i])
        assert abs(w["ClothA"] - share) < 3.0 * reach
    assert moved > len(skin) // 3
    # 布の頂点は触らない
    for i in under:
        assert obj.w[i] == {"ClothA": fa[i], "ClothB": fb[i]}


@pytest.mark.parametrize("name", params.ALL_IDS)
def test_torso_sides_are_under_the_top(name):
    """胸の高さで胴のいちばん外側（脇）の頂点は、全員、上着の布の下にある。

    腕を下ろすと脇の肌が上着の外へ出ていた (mirai の Idle で 4.2 mm) のはここ。
    """
    p, mb = _build(name)
    pts = np.array(mb.verts)
    skin = mb.part_indices("torso")
    z = pts[skin, 2]
    band = (z >= p["z"]["underbust"]) & (z <= p["z"]["bust"])
    ax = np.abs(pts[skin, 0])
    side = skin[band & (ax >= 0.85 * ax[band].max())]
    assert len(side) >= 10
    under = set(mb.part_indices(*rig.UNDER_CLOTH).tolist())
    W = {i: {"Cloth": 1.0} for i in under}
    tris = M.triangles(mb.faces, mb.part_indices(*rig.UNDER_CLOTH, *rig.LEG_CLOTH))
    out = rig._cover_weights(pts, side, _torso_normals(mb, pts), tris, W,
                             p["height"] * 0.02)
    assert all(w == {"Cloth": 1.0} for w in out)


@pytest.mark.parametrize("name", [cid for cid in params.ALL_IDS
                                  if params.CHARACTERS[cid]["outfit"].startswith("seifuku")])
def test_hips_under_the_skirt_keep_their_weights(name):
    """セーラー服の腰はスカートが最初に当たる。スカートは脚について動くので
    写さない（写すと胴の腰まで脚に振られる）。"""
    p, mb = _build(name)
    pts = np.array(mb.verts)
    skin = mb.part_indices("torso")
    tris = M.triangles(mb.faces, mb.part_indices(*rig.UNDER_CLOTH, *rig.LEG_CLOTH))
    hits = M.first_hits(pts[skin], _torso_normals(mb, pts)[skin], pts, tris,
                        p["height"] * 0.02)
    skirt = set(mb.part_indices("skirt").tolist())
    on_skirt = [i for i, (j, *_r) in zip(skin.tolist(), hits)
                if j >= 0 and int(tris[int(j)][0]) in skirt]
    assert len(on_skirt) > 50
    assert all(pts[i, 2] < p["z"]["waist"] for i in on_skirt)
