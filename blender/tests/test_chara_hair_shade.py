"""兜の髪の陰の法線 (#47)。

兜の殻は房の束を出すために方位へ畝 (ridge_amp) を付け、毛先を同じ周期で
ギザギザ (jag) に切ってある。その面の法線で陰を付けると、畝ごとに法線が左右へ
振れて陰の境目を行き来し、後ろ髪にぼやけた濃い斑が並ぶ。陰の法線は畝も
ギザギザも無い同じ殻から取るので、書き出す法線の方位成分に畝の周期がほとんど
残らないことを、兜を持つキャラごとに確かめる。面の向きが内向きのキャラ
(outward_faces が無い) も同じ経路を通る。

陰の境目はプレビューの光で水平に後ろを向いた法線のあたりに来るので、横と
後ろの垂れの法線は頭頂から毛先へ単調に下へ倒れる楕円体の法線にする。
"""

import math

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
    return dict(p=p, mb=mb, head=head, grid=grids["hair_back"][0], grids=grids["hair_back"],
                kw=helmets[0])


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


def test_helmet_back_shade_tilts_down_steadily_to_the_tips(helmet):
    """後ろの垂れの陰の法線は、頭頂から毛先へ行ごとに下へ倒れ、途中で上へ戻らない。

    陰の境目を硬くすると、垂れ始めの縁で法線が折れて行ごとに上下する所で境目が
    出入りし、後頭部の陰の縁がでこぼこになる。畝とギザギザを消した殻の面から
    求めた法線では、1 行で上向きへ 0.15〜0.23 戻っていた。楕円体の法線では 1 行ごとに
    0.016 以上下がる。面が内向きの殻は
    法線も内向きに書き出すので、面の向きにそろえてから測る (外向きのまま
    書き出すと、上下が逆になって後頭部が陰で塗りつぶされる)。
    """
    g, mb = helmet["grid"], helmet["mb"]
    rows, cols = g["rows"], g["cols"]
    idx = np.arange(g["v0"], g["v0"] + rows * cols)
    n = mb.normal_array()[idx].reshape(rows, cols, 3)
    n = n / np.linalg.norm(n, axis=-1, keepdims=True)
    if not helmet["p"].get("outward_faces"):
        n = -n
    back = hair._angdist(np.linspace(0.0, 2 * math.pi, cols, endpoint=False)) / math.pi > 0.6
    rise = np.diff(n[3:, back, 2], axis=0)
    assert rise.max() < -0.005, float(rise.max())


def test_helmet_tip_band_shares_the_shell_tip_normals(helmet):
    """毛先の縁の帯の外側の行は、重なった外殻の毛先の行と同じ法線で書き出す。

    外殻の垂れは楕円体の法線へ寄せる。縁の帯を寄せる前の法線のままにすると、同じ
    位置の頂点で法線が分かれ、同じ法線で押し出す輪郭線の殻が毛先で裂ける。
    """
    shell, band = helmet["grids"][0], helmet["grids"][2]
    assert band["rows"] == 2 and band["cols"] == shell["cols"]
    mb = helmet["mb"]
    tip = np.arange(shell["cols"]) + shell["v0"] + (shell["rows"] - 1) * shell["cols"]
    edge = np.arange(band["cols"]) + band["v0"]
    V = np.asarray(mb.verts, dtype=float)
    np.testing.assert_allclose(V[edge], V[tip], atol=1e-9)
    N = mb.normal_array()
    np.testing.assert_allclose(N[edge], N[tip], atol=1e-9)
