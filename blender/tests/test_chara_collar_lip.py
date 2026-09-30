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
NAVY = "cloth_skirt_navy"
# 襟の面の法線からのずれの上限。直す前は内縁も折り返しも 87〜96° ずれていた（直した後は 3〜12°）
MAX_DEG = 20.0


@functools.cache
def _build(name):
    p = params.resolve(name)
    mb = M.MeshBuilder()
    a, _, _, _, _ = body.build_base(mb, p)
    cloth.build_outfit(mb, p, a)
    return mb


def _front_grids(mb):
    """前の襟の格子ごとに (先頭の頂点番号, 1 行の頂点数, 行数) を返す。

    襟は背面フラップと左右の前の襟を別々の part ブロックで張る。どのブロックも先頭は
    add_grid の格子で、四角形 (a, b, c, d) の d - a が 1 行の頂点数になる。
    前の襟は胸の側（-Y）にある。
    """
    V = np.array(mb.verts)
    out = []
    for start, end in mb.parts["collar"]:
        quads = [f for f, m in zip(mb.faces, mb.face_mat)
                 if m == NAVY and start <= f[0] < end and len(f) == 4]
        if not quads:
            continue
        a, _, _, d = quads[0]
        n = d - a
        rows = len(quads) // (n - 1) + 1
        if V[start:start + n * rows, 1].mean() < 0:
            out.append((start, n, rows))
    return out


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
    mb = _build(name)
    grids = _front_grids(mb)
    assert len(grids) == 2, f"{name}: 左右の前の襟が見つからない"
    custom = mb.normal_array()
    for start, n, rows in grids:
        vn = _vertex_normals(mb, start, start + n * rows)
        corner = np.where(np.linalg.norm(custom[start:start + n * rows], axis=1,
                                         keepdims=True) > 0,
                          custom[start:start + n * rows], vn)
        sheet = vn[(rows - 3) * n:(rows - 2) * n]
        for row, label in ((rows - 2, "内縁"), (rows - 1, "折り返し")):
            worst = _angle_deg(corner[row * n:(row + 1) * n], sheet).max()
            assert worst < MAX_DEG, f"{name}: {label}の法線が襟の面から {worst:.0f}° ずれている"


def test_collar_lip_folds_into_body():
    """上のテストが空振りしていない。折り返しは内縁から身頃の内側へ 1 cm ほど折れている。"""
    mb = _build("mirai")
    V = np.array(mb.verts)
    h = params.resolve("mirai")["height"]
    for start, n, rows in _front_grids(mb):
        edge = V[start + (rows - 2) * n:start + (rows - 1) * n]
        lip = V[start + (rows - 1) * n:start + rows * n]
        depth = np.linalg.norm(lip - edge, axis=1) / h
        assert np.allclose(depth, 0.010, atol=1e-3)
