"""眼鏡の縁を置く面（inari の赤い縁、prof の銀縁） (#47)。

縁はレンズの輪を 1 本の管で張り、既定では顔の丸みに沿わせて外側ほど奥へ回す。inari は
レンズが縦に長いので、回すと横から縦長の輪に見え、輪の奥の側が目と頬の上に重なる。
inari の縁は目より手前のほぼ平らな面に置くので、左右の縁の芯の奥行きの幅が管の太さより
小さいこと（横からは 1 本の線に見える）と、目の部品がどれも縁の管の裏より奥にあることを
確かめる。prof の銀縁は顔の丸みに沿わせたままにする。
"""

import functools

import numpy as np

from kcd_chara import body, hair, params
from kcd_chara import mesh as M

EYE_PARTS = ("white", "iris", "lash_up", "lash_lo", "crease")


@functools.lru_cache(maxsize=None)
def _built(cid):
    """cid の頭と髪を組み、眼鏡の部品で張った管を呼び出し順で返す。"""
    tubes = []
    orig = M.MeshBuilder.add_tube

    def add_tube(self, path, radii, mat, **kw):
        if self._stack and self._stack[-1] == "glasses":
            tubes.append(dict(path=np.asarray(path, dtype=float), r=float(np.max(radii))))
        return orig(self, path, radii, mat, **kw)

    M.MeshBuilder.add_tube = add_tube
    try:
        p = params.resolve(cid)
        mb = M.MeshBuilder()
        a, head, fs, uv_box, _ = body.build_base(mb, p)
        hair.build_hair(mb, p, head, a, fs, uv_box)
    finally:
        M.MeshBuilder.add_tube = orig
    return dict(mb=mb, V=np.asarray(mb.verts, dtype=float), tubes=tubes)


def _rims(m):
    """左右の縁。芯線が閉じた輪になっている管。"""
    rims = [t for t in m["tubes"] if np.allclose(t["path"][0], t["path"][-1])]
    assert len(rims) == 2
    return rims


def _y_along_x(pts, x):
    """pts（x の順に並べ替える）の y を、x で線形に補間する。"""
    o = np.argsort(pts[:, 0])
    return np.interp(x, pts[o, 0], pts[o, 1])


def test_inari_rim_reads_as_a_line_from_the_side():
    """inari の左右の縁の芯の奥行きの幅は、管の直径より小さい。"""
    for rim in _rims(_built("inari")):
        assert np.ptp(rim["path"][:, 1]) < 2.0 * rim["r"]


def test_inari_eyes_sit_behind_the_rim():
    """inari の目の部品（白目・虹彩・まつ毛・二重線）は、どの頂点も同じ側の縁の管の裏より奥にある。

    管の裏は、芯の y に半径を足した位置。芯の y は、上の縁と下の縁をそれぞれ x で補間した
    奥のほうを使う。
    """
    m = _built("inari")
    mb, V = m["mb"], m["V"]
    for rim in _rims(m):
        core = rim["path"][:-1]
        side = "l" if core[:, 0].mean() > 0.0 else "r"  # x が正の側が eye_l
        names = [f"eye_{side}_{s}" for s in EYE_PARTS]
        assert all(len(mb.part_indices(n)) for n in names)
        P = V[mb.part_indices(*names)]
        upper = core[:, 2] >= core[:, 2].mean()
        back = np.maximum(_y_along_x(core[upper], P[:, 0]),
                          _y_along_x(core[~upper], P[:, 0])) + rim["r"]
        assert (P[:, 1] - back).min() > 0.0


def test_prof_rim_follows_the_face():
    """prof の縁は外側ほど奥へ回る。芯の奥行きの幅は管の直径の 3 倍を超える。"""
    for rim in _rims(_built("prof")):
        assert np.ptp(rim["path"][:, 1]) > 3.0 * 2.0 * rim["r"]
