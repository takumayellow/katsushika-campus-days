"""後ろ髪が後頭部を覆うこと (#47)。

兜の後ろの垂れの毛先は、頭の中心の真後ろより首の方へ寄せてある。後頭部の
張り出しを避けずに毛先へ向けて寄せると、垂れが頭蓋の内側をくぐり、後ろから
見ると髪の縁の下に後頭部の肌が顔のような楕円で出る (inari・kaname・sora)。
垂れは頭の面から縁の厚みより内へ寄せない。

兜の無い prof も、地髪の殻の縁が後頭部の赤道の少し下で止まると、縁の下に
同じ楕円が出る。後頭部の縁は襟足まで下ろす。
"""

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
    skull = np.array([first_hit(c, u, chara["head_tri"]) for u in d / r[:, None]])
    inside = np.isfinite(skull) & (r < skull)
    assert not inside.any(), (chara["name"], int(inside.sum()),
                              float((r - skull)[inside].min()))


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
