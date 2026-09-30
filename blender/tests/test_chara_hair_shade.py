"""兜の髪の陰の法線 (#47)。

兜の殻は房の束を出すために方位へ畝 (ridge_amp) を付け、毛先を同じ周期で
ギザギザ (jag) に切ってある。その面の法線で陰を付けると、畝ごとに法線が左右へ
振れて陰の境目を行き来し、後ろ髪にぼやけた濃い斑が並ぶ。陰の法線は畝も
ギザギザも無い同じ殻から取るので、書き出す法線の方位成分に畝の周期がほとんど
残らないことを、兜を持つキャラごとに確かめる。面の向きが内向きのキャラ
(outward_faces が無い) も同じ経路を通る。
"""

import numpy as np
import pytest

from kcd_chara import body, hair, params
from kcd_chara import mesh as M

HELMET_IDS = ["mirai", "madonna", "inari", "kaname", "sora"]


def _build(name):
    """name の髪を組み、兜の外殻のグリッドと build_helmet の引数を返す。"""
    grids = {}
    helmets = []
    orig_grid, orig_helmet = M.MeshBuilder.add_grid, hair.build_helmet

    def add_grid(self, rings, mat, **kw):
        v0 = len(self.verts)
        out = orig_grid(self, rings, mat, **kw)
        part = self._stack[-1] if self._stack else ""
        grids.setdefault(part, []).append(dict(v0=v0, rows=len(rings), cols=len(rings[0])))
        return out

    def build_helmet(*args, **kw):
        helmets.append(kw)
        return orig_helmet(*args, **kw)

    M.MeshBuilder.add_grid = add_grid
    hair.build_helmet = build_helmet
    try:
        p = params.resolve(name)
        mb = M.MeshBuilder()
        a, head, fs, uv_box, _ = body.build_base(mb, p)
        hair.build_hair(mb, p, head, a, fs, uv_box)
    finally:
        M.MeshBuilder.add_grid = orig_grid
        hair.build_helmet = orig_helmet
    assert len(helmets) == 1
    return dict(mb=mb, head=head, grid=grids["hair_back"][0], kw=helmets[0])


@pytest.fixture(scope="module", params=HELMET_IDS)
def helmet(request):
    return _build(request.param)


def test_hair_back_normals_are_all_set_and_finite(helmet):
    mb = helmet["mb"]
    n = mb.normal_array()[mb.part_indices("hair_back")]
    assert np.isfinite(n).all()
    assert np.all(np.linalg.norm(n, axis=1) > 0.0)


def test_helmet_shade_does_not_follow_the_ridges(helmet):
    """書き出す法線の方位成分に、畝の周期の振れがほとんど残らない。

    面から求めた法線では振幅が 0.21〜0.32 (mirai 0.32)。頭頂の近くは畝が無く、
    毛先の 2 行は縁の帯なので、その間の行で測る。
    """
    g, mb = helmet["grid"], helmet["mb"]
    rows, cols = g["rows"], g["cols"]
    idx = np.arange(g["v0"], g["v0"] + rows * cols)
    P = np.asarray(mb.verts, dtype=float)[idx].reshape(rows, cols, 3)[3:rows - 2]
    n = mb.normal_array()[idx].reshape(rows, cols, 3)[3:rows - 2]
    c = helmet["head"].center
    az = np.stack([c[1] - P[..., 1], P[..., 0] - c[0], np.zeros(P.shape[:2])], axis=-1)
    az /= np.linalg.norm(az, axis=-1, keepdims=True)
    L = np.linalg.norm(n, axis=-1)
    assert np.all(L > 0.0)
    t = (n / L[..., None] * az).sum(axis=-1)
    ridges = helmet["kw"].get("ridges", 14)
    amp = np.abs(np.fft.rfft(t, axis=1))[:, ridges] * 2 / cols
    assert amp.max() < 0.15
