"""後ろ髪が後頭部を覆うこと (#47)。

兜の後ろの垂れの毛先は、頭の中心の真後ろより首の方へ寄せてある。後頭部の
張り出しを避けずに毛先へ向けて寄せると、垂れが頭蓋の内側をくぐり、後ろから
見ると髪の縁の下に後頭部の肌が顔のような楕円で出る (inari・kaname・sora)。
垂れは頭の面から縁の厚みより内へ寄せない。

兜の無い prof も、地髪の殻の縁が後頭部の赤道の少し下で止まると、縁の下に
同じ楕円が出る。後頭部の縁は襟足まで下ろす。
"""

import math

import numpy as np
import pytest

from kcd_chara import body, hair, params
from kcd_chara import mesh as M
from raycast import first_hit
from test_chara_hair_shade import HELMET_IDS

HAIR_PARTS = ("hair_back", "hair_cap", "hair_side", "hair_front")


def _triangles(mb, *parts):
    V = np.array(mb.verts)
    keep = set(mb.part_indices(*parts).tolist())
    tri = [(f[0], f[k], f[k + 1]) for f in mb.faces if all(i in keep for i in f)
           for k in range(1, len(f) - 1)]
    return V[np.array(tri)]


def _hit(orig, d, tri):
    """first_hit と同じ。光線が三角形の辺をちょうど通ると丸めで両側の三角形を
    外すことがあるので、そのときは向きをわずかにずらして撃ち直す。正中 (x = 0) の
    面の中を進む光線は正中の辺の上を通るので、x も含めて 3 軸ともずらす。"""
    t = first_hit(orig, d, tri)
    return t if np.isfinite(t) else first_hit(orig, d + 1e-6, tri)


def _hair_dirs(seed, n=400):
    """頭の中心から、髪の掛かる上と後ろと横へ向かう乱数の向き。

    顔の正中の竜骨 (face_keel) は head.surface に含めず、耳は頭の網に埋めた
    別の球なので、正面と耳の向きは除く。
    """
    d = np.random.default_rng(seed).normal(size=(n, 3))
    d /= np.linalg.norm(d, axis=1, keepdims=True)
    return d[(d[:, 2] > -0.8) & (d[:, 1] > -0.5) & (np.abs(d[:, 0]) < math.cos(math.radians(35)))]


@pytest.fixture(scope="module", params=HELMET_IDS + ["prof"])
def chara(request):
    p = params.resolve(request.param)
    mb = M.MeshBuilder()
    a, head, fs, uv_box, _ = body.build_base(mb, p)
    hair.build_hair(mb, p, head, a, fs, uv_box)
    return dict(name=request.param, p=p, mb=mb, head=head,
                head_tri=_triangles(mb, "head"), hair_tri=_triangles(mb, *HAIR_PARTS))


def test_helmet_stays_outside_the_head(chara):
    """頭の中心から兜の頂点へ向かう光線が、頂点より先で頭の面を横切らない。"""
    if chara["name"] not in HELMET_IDS:
        pytest.skip("兜の無いキャラ（房の根元は地髪の殻の中に埋めてある）")
    mb, c = chara["mb"], chara["head"].center
    V = np.asarray(mb.verts, dtype=float)[mb.part_indices("hair_back")]
    d = V - c
    r = np.linalg.norm(d, axis=1)
    skull = np.array([_hit(c, u, chara["head_tri"]) for u in d / r[:, None]])
    assert np.isfinite(skull).all()
    inside = r < skull
    assert not inside.any(), (chara["name"], int(inside.sum()),
                              float((r - skull)[inside].min()))


def test_skull_radius_matches_the_head_mesh(chara):
    """_skull_radius は、頭の中心からの光線が頭の網に当たる距離と 1% 以内で合う。

    ニュートン法が収束しない向きや別の枝へ飛んだ向きがあると、垂れをそこだけ
    押し出し過ぎたり押し出さなかったりする。網の面の平らさによる差は 0.5% 前後。
    """
    head = chara["head"]
    d = _hair_dirs(len(chara["name"]))
    r = hair._skull_radius(head, d)
    t = np.array([_hit(head.center, u, chara["head_tri"]) for u in d])
    err = np.abs(r - t) / t
    assert err.max() < 0.01, (chara["name"], float(err.max()), d[np.argmax(err)].tolist())


def test_off_skull_pushes_only_inside_points_and_keeps_order(chara):
    """gap より外の点は動かず、内の点は面から gap の 0.64 倍より内に残らない。

    同じ向きの点は押し出したあとも頭の中心からの順が入れ替わらない（面が裏返らない）。
    """
    head = chara["head"]
    d = _hair_dirs(1, n=40)
    R = hair._skull_radius(head, d)
    gap = 0.01
    s = np.linspace(0.5, 1.5, 41)
    pts = head.center + (d[:, None, :] * (R[:, None] * s)[..., None]).reshape(-1, 3)
    out = hair._off_skull(head, pts, gap)
    r0 = np.linalg.norm(pts - head.center, axis=1).reshape(len(d), -1)
    r1 = np.linalg.norm(out - head.center, axis=1).reshape(len(d), -1)
    u1 = (out - head.center) / r1.reshape(-1, 1)
    np.testing.assert_allclose(u1, np.repeat(d, len(s), axis=0), atol=1e-9)
    outside = r0 >= R[:, None] + gap
    np.testing.assert_allclose(r1[outside], r0[outside])
    assert (r1 - R[:, None] >= 0.64 * gap - 1e-9).all()
    assert (np.diff(r1, axis=1) > -1e-12).all()


def test_back_of_head_is_covered_by_hair(chara):
    """真後ろから見て、うなじより上の後頭部に当たる光線は先に髪に当たる。

    毛先は顎より 0.03〜0.09hh 上で終わり、その下はうなじの肌が見えてよい。
    旧版は顎より 0.1hh 上 (prof は 0.35hh 上) から頭の中心の近くまで肌が見えていた。
    """
    head, p = chara["head"], chara["p"]
    hw, hh, chin = p["head_w"], p["head_h"], p["z"]["chin"]
    d = np.array([0.0, -1.0, 0.0])
    bare = []
    for x in np.linspace(-0.3 * hw, 0.3 * hw, 7):
        for z in np.linspace(chin + 0.15 * hh, head.center[2], 8):
            o = np.array([x, head.center[1] + 1.0, z])
            t_skin = first_hit(o, d, chara["head_tri"])
            if np.isfinite(t_skin) and not first_hit(o, d, chara["hair_tri"]) < t_skin:
                bare.append((round(float(x), 3), round(float(z), 3)))
    assert not bare, (chara["name"], bare)
