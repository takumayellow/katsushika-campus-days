"""README_characters.md の体型の表（頭幅・肩幅・腕の直径）を、組んだメッシュと突き合わせる (#47)。

表の値は組んだメッシュから測ったもので、params.py の BUILDS や衣装を変えると変わる。README と同じ
測り方で測り直し、表に書いた桁まで合うかを見る。
- 頭幅: 耳まで含めた head パーツの横幅。
- 肩幅: 肩の高さから 5 mm 以内にある、髪以外の頂点の横幅。
- 腕の直径: 左腕（arm_l）のうち肩と肘の中点にある輪の、腕の軸に垂直な面での最大の幅。
"""

import functools
import os

import numpy as np
import pytest

from kcd_chara import body, cloth, hair, params
from kcd_chara import mesh as M

README = os.path.join(os.path.dirname(os.path.dirname(os.path.abspath(__file__))), "README_characters.md")
HEADER = "| id | 頭幅 m | 肩幅 m | 肩幅 / 頭幅 | 腕の直径 m | 腕 / 頭幅 |"
COLUMNS = ("head", "shoulder", "shoulder_to_head", "arm", "arm_to_head")
#: README の「肩幅は全員が頭幅の 1.6 倍以下、腕の直径は全員 0.22 倍以下」
MAX_SHOULDER_TO_HEAD = 1.6
MAX_ARM_TO_HEAD = 0.22


@functools.cache
def _table():
    """表の行を id ごとに、書いてある文字列のまま返す（桁数を見るため）。"""
    with open(README, encoding="utf-8") as f:
        lines = f.read().splitlines()
    assert HEADER in lines, f"README_characters.md に体型の表の見出しが無い: {HEADER}"
    rows = {}
    for line in lines[lines.index(HEADER) + 2:]:
        if not line.startswith("|"):
            break
        cells = [c.strip() for c in line.strip("|").split("|")]
        rows[cells[0].strip("`")] = cells[1:]
    return rows


@functools.cache
def _build(name):
    p = params.resolve(name)
    mb = M.MeshBuilder()
    a, head, fs, uv_box, _ = body.build_base(mb, p)
    hair.build_hair(mb, p, head, a, fs, uv_box)
    cloth.build_outfit(mb, p, a)
    return p, a, mb


def _arm_width(arm, shoulder, elbow):
    """上腕のまん中の輪を腕の軸に垂直な面へ写し、180 方向の幅のうち最大のものを返す。"""
    length = np.linalg.norm(elbow - shoulder)
    axis = (elbow - shoulder) / length
    ring = arm[np.abs((arm - shoulder) @ axis / length - 0.5) < 0.05]
    assert len(ring) >= 8, f"上腕のまん中に輪が無い（{len(ring)} 頂点）"
    side = np.cross(axis, np.eye(3)[np.argmin(np.abs(axis))])
    side /= np.linalg.norm(side)
    up = np.cross(axis, side)
    ang = np.linspace(0.0, np.pi, 180, endpoint=False)
    dirs = np.outer(np.cos(ang), side) + np.outer(np.sin(ang), up)
    return float(np.ptp(ring @ dirs.T, axis=0).max())


@functools.cache
def _measure(name):
    p, a, mb = _build(name)
    V = np.asarray(mb.verts, dtype=float)
    head_w = float(np.ptp(V[mb.part_indices("head")][:, 0]))
    no_hair = V[mb.part_indices(*[n for n in mb.parts if not n.startswith("hair")])]
    band = no_hair[np.abs(no_hair[:, 2] - p["z"]["shoulder"]) < 0.005]
    shoulder = float(np.ptp(band[:, 0]))
    arm = _arm_width(V[mb.part_indices("arm_l")], a.shoulder, a.elbow)
    return {"head": head_w, "shoulder": shoulder, "shoulder_to_head": shoulder / head_w,
            "arm": arm, "arm_to_head": arm / head_w}


def test_the_table_lists_every_character():
    listed = set(_table())
    ids = set(params.ALL_IDS)
    assert listed == ids, f"表に無い id: {sorted(ids - listed)}、キャラに無い id: {sorted(listed - ids)}"


@pytest.mark.parametrize("name", params.ALL_IDS)
def test_the_row_matches_the_mesh(name):
    row = _table().get(name)
    assert row is not None, f"表に {name} の行が無い"
    assert len(row) == len(COLUMNS), f"{name} の行のセルが {len(row)} 個（{len(COLUMNS)} 個要る）"
    got = _measure(name)
    for column, text in zip(COLUMNS, row):
        digits = len(text.partition(".")[2])
        # 表は測った値を書いた桁で丸めたもの。丸めの境目で揺れないよう、半桁より少しだけ広く取る
        assert abs(float(text) - got[column]) <= 0.6 * 10.0 ** -digits, (
            f"{name} の {column}: 表は {text}、測ると {got[column]:.{digits + 1}f}")


@pytest.mark.parametrize("name", params.ALL_IDS)
def test_no_one_is_built_wide(name):
    got = _measure(name)
    assert got["shoulder_to_head"] <= MAX_SHOULDER_TO_HEAD, (
        f"{name} の肩幅は頭幅の {got['shoulder_to_head']:.3f} 倍（{MAX_SHOULDER_TO_HEAD} 倍以下にする）")
    assert got["arm_to_head"] <= MAX_ARM_TO_HEAD, (
        f"{name} の腕の直径は頭幅の {got['arm_to_head']:.3f} 倍（{MAX_ARM_TO_HEAD} 倍以下にする）")
