"""mirai の前髪とヘアピンの位置 (#47)。

Unity の輪郭線は法線の向きへ押し出した殻なので、輪郭線を持たない兜のドームより外へ
前髪の房が出ると、頭頂に輪や角のような線が出る。ヘアピンは髪の下に埋もれると見えず、
髪から浮くと殻の線がピンの下に回り込む。頭の中心から頂点へ光線を飛ばし、その先で
一番遠くに当たる髪の面との差で、どれだけ外へ出ているかを測る。
"""

import numpy as np
import pytest

from kcd_chara import body, hair, params
from kcd_chara import mesh as M


@pytest.fixture(scope="module")
def mirai():
    p = params.resolve("mirai")
    mb = M.MeshBuilder()
    a, head, fs, uv_box, _ = body.build_base(mb, p)
    hair.build_hair(mb, p, head, a, fs, uv_box)
    return p, mb, head, np.asarray(mb.verts, dtype=float)


def _tris(mb, V, names):
    keep = set(mb.part_indices(*names).tolist())
    tris = [(f[0], f[k], f[k + 1]) for f in mb.faces if f[0] in keep
            for k in range(1, len(f) - 1)]
    return V[np.array(tris)]


def _far_hit(tris, origin, dirs):
    """origin から dirs の向きへ飛ばした光線が tris に当たる一番遠い距離。当たらなければ nan。"""
    a, b, c = tris[:, 0], tris[:, 1], tris[:, 2]
    e1, e2 = b - a, c - a
    s = origin - a
    q = np.cross(s, e1)
    out = []
    for d in dirs:
        h = np.cross(d, e2)
        det = (e1 * h).sum(axis=1)
        ok = np.abs(det) > 1e-12
        inv = np.where(ok, 1.0 / np.where(ok, det, 1.0), 0.0)
        u = (s * h).sum(axis=1) * inv
        v = (q @ d) * inv
        t = (e2 * q).sum(axis=1) * inv
        hit = ok & (u >= 0.0) & (v >= 0.0) & (u + v <= 1.0) & (t > 0.0)
        out.append(t[hit].max() if hit.any() else np.nan)
    return np.array(out)


def _excess(head, pts, tris):
    """pts の各点が、同じ向きで一番外にある tris の面より何 m 外にあるか。"""
    x = pts - head.center
    r = np.linalg.norm(x, axis=1)
    return r - _far_hit(tris, head.center, x / r[:, None])


def test_mirai_bangs_stay_under_the_helmet_dome(mirai):
    _, mb, head, V = mirai
    front = V[mb.part_indices("hair_front")]
    ex = _excess(head, front, _tris(mb, V, ["hair_back"]))
    x = front - head.center
    polar = np.arccos(x[:, 2] / np.linalg.norm(x, axis=1))
    covered = ~np.isnan(ex) & (polar < 0.60)
    assert covered.sum() > 100
    assert ex[covered].max() < 0.002


def test_mirai_hairpin_shows_just_above_the_hair(mirai):
    p, mb, head, V = mirai
    names = [n for n in mb.parts if n.startswith("hair") and n != "hair_acc"]
    ex = _excess(head, V[mb.part_indices("hair_acc")], _tris(mb, V, names))
    assert not np.isnan(ex).any()
    assert 0.0005 < ex.max() < p["head_w"] * 0.03  # 上面だけが髪から出る
    assert ex.min() < 0.0  # 下は髪に埋まっていて、浮いた隙間が無い
