"""セーラー襟の内縁の折り返しで、輪郭線の殻が襟から離れない (#47)。

前の襟は身頃から少し浮かせて張り、内縁を身頃の内側へ折り返している（cloth._sailor_collar）。
折り返しの面は V の中心を向くので、面の向きのまま法線を決めると、Unity の輪郭線
（KCD_Toon.shader の Outline パス。角の法線の向きへ押し出した殻）が折り返しの分だけ
V の中心へずれる。斜めから見ると、奥の襟の内縁から 2 cm ほど離れた黒い線が首から
リボンまで走っていた。内縁と折り返しの角の法線が、襟の面の法線にそろっていることを確かめる。
"""

import functools

import numpy as np
import pytest

from kcd_chara import body, cloth, params
from kcd_chara import mesh as M

SAILOR = [n for n in params.ALL_IDS
          if params.resolve(n)["outfit"].startswith("seifuku")]
# 襟の面の法線からのずれの上限。直す前は内縁も折り返しも 87〜96° ずれていた（直した後は 3〜12°）
MAX_DEG = 20.0


@functools.cache
def _build(name):
    """キャラを組み、襟の部位で add_grid が張った開いた格子の (先頭の頂点, 段数, 列数) を返す。"""
    grids = []
    orig = M.MeshBuilder.add_grid

    def add_grid(self, rings, mat, **kw):
        v0 = len(self.verts)
        out = orig(self, rings, mat, **kw)
        # 襟の部位の add_tube（白線）も add_grid で筒を張るので、開いた格子だけ拾う
        if self._stack and self._stack[-1] == "collar" and not kw.get("close_u", True):
            grids.append((v0, len(rings), len(rings[0])))
        return out

    M.MeshBuilder.add_grid = add_grid
    try:
        p = params.resolve(name)
        mb = M.MeshBuilder()
        a, _, _, _, _ = body.build_base(mb, p)
        cloth.build_outfit(mb, p, a)
    finally:
        M.MeshBuilder.add_grid = orig
    return mb, grids


def _front_grids(name):
    """前の襟 2 枚の (先頭の頂点, 段数, 列数)。最後の段は身頃の縁の内側への折り返し。"""
    _, grids = _build(name)
    # 1 枚目は背面フラップ、続く 2 枚が前の襟
    assert len(grids) == 3, f"{name}: 襟の格子が {len(grids)} 枚"
    return grids[1:]


def _vertex_normals(mb, lo, hi):
    """lo..hi の頂点の、なめらかな面から求めた法線（Blender の角の法線と同じ向き）。"""
    V = np.array(mb.verts)
    out = np.zeros((hi - lo, 3))
    for f, smooth in zip(mb.faces, mb.face_smooth):
        if not smooth or not all(lo <= i < hi for i in f):
            continue
        q = V[list(f)]
        n = sum(np.cross(q[k], q[(k + 1) % len(f)]) for k in range(len(f)))
        for i in f:
            out[i - lo] += n
    return out / (np.linalg.norm(out, axis=1, keepdims=True) + 1e-15)


def _angle_deg(u, v):
    cos = np.sum(u * v, axis=1) / (np.linalg.norm(u, axis=1) * np.linalg.norm(v, axis=1))
    return np.degrees(np.arccos(np.clip(cos, -1.0, 1.0)))


@pytest.mark.parametrize("name", SAILOR)
def test_collar_lip_follows_collar_normal(name):
    """内縁と折り返しの角の法線が、1 行外側の襟の面の法線から MAX_DEG 以内。"""
    mb, _ = _build(name)
    custom = mb.normal_array()
    for start, rows, n in _front_grids(name):
        vn = _vertex_normals(mb, start, start + n * rows)
        corner = np.where(np.linalg.norm(custom[start:start + n * rows], axis=1,
                                         keepdims=True) > 0,
                          custom[start:start + n * rows], vn)
        sheet = vn[(rows - 3) * n:(rows - 2) * n]
        for row, label in ((rows - 2, "内縁"), (rows - 1, "折り返し")):
            worst = _angle_deg(corner[row * n:(row + 1) * n], sheet).max()
            assert worst < MAX_DEG, f"{name}: {label}の法線が襟の面から {worst:.0f}° ずれている"


@pytest.mark.parametrize("name", SAILOR)
def test_collar_lip_folds_into_body(name):
    """上のテストが空振りしていない。折り返しは内縁から身頃の内側へ 1 cm ほど折れている。"""
    mb, _ = _build(name)
    V = np.array(mb.verts)
    h = params.resolve(name)["height"]
    for start, rows, n in _front_grids(name):
        edge = V[start + (rows - 2) * n:start + (rows - 1) * n]
        lip = V[start + (rows - 1) * n:start + rows * n]
        depth = np.linalg.norm(lip - edge, axis=1) / h
        assert np.allclose(depth, 0.010, atol=1e-3)
