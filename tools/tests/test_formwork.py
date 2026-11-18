"""formwork: 打放しの型枠の目地と P コンの穴を、割付けの寸法から手続きで描く (#423)。"""

from __future__ import annotations

import json
import sys
from pathlib import Path

import numpy as np
import pytest

pytest.importorskip("PIL")

TOOLS = str(Path(__file__).resolve().parents[1])
if TOOLS not in sys.path:
    sys.path.insert(0, TOOLS)

import fetch_textures as ft  # noqa: E402
import formwork as fw  # noqa: E402

# 下のしきい値（目地の落ち込み -8、穴は背景の 0.8 倍未満 など）は、formwork.BASE_LINEAR = 0.4 と
# この SPEC の明るさの比（joint_ratio 0.55・pcon_ratio 0.35）で測った値に合わせてある。
SIZE, TILE_CM = 512, 182

SPEC = {
    "panel_mm": [1820, 910],
    "joint_mm": 3,
    "joint_ratio": 0.55,
    "pcon_mm": 30,
    "pcon_ratio": 0.35,
    "pcon_pitch_mm": 455,
    "mottle": [{"scale_mm": 250, "amount": 0.05}, {"scale_mm": 40, "amount": 0.025}],
    "panel_tone": 0.03,
    "grain": 0.02,
    "pores_per_m2": 120,
    "pore_mm": 2.5,
    "pore_ratio": 0.55,
}


@pytest.fixture(scope="module")
def gray():
    return np.asarray(fw.compose(SPEC, SIZE, TILE_CM, "test_wall").convert("L"), dtype=np.float64)


def at(fraction):
    """タイルの中の割合の位置を画素の番号へ。"""
    return int(round(fraction * SIZE - 0.5))


def test_same_name_gives_same_image():
    a = fw.compose(SPEC, 128, TILE_CM, "a")
    assert a.tobytes() == fw.compose(SPEC, 128, TILE_CM, "a").tobytes()
    assert a.tobytes() != fw.compose(SPEC, 128, TILE_CM, "b").tobytes()


def test_image_is_gray_and_sized():
    image = fw.compose(SPEC, 128, TILE_CM, "a")
    assert image.size == (128, 128) and image.mode == "RGB"
    r, g, b = (np.asarray(c) for c in image.split())
    assert (r == g).all() and (g == b).all()


def local_dip(profile, reach=6):
    """各位置の値が、reach px 離れた両隣の平均よりどれだけ暗いか（ムラの大きな傾きは打ち消える）。"""
    return profile - (np.roll(profile, reach) + np.roll(profile, -reach)) / 2


def test_joints_lie_on_the_panel_grid(gray):
    """横の目地はタイルの 1/4 と 3/4（910 mm おき）、縦の目地は 1/2（1820 mm おき）。その他に線は無い。"""
    rows = local_dip(gray.mean(axis=1))
    cols = local_dip(gray.mean(axis=0))
    joint_rows = np.flatnonzero(rows < -8)
    joint_cols = np.flatnonzero(cols < -8)
    assert joint_rows.size and all(min(abs(y - at(0.25)), abs(y - at(0.75))) <= 1 for y in joint_rows), joint_rows
    assert any(abs(y - at(0.25)) <= 1 for y in joint_rows) and any(abs(y - at(0.75)) <= 1 for y in joint_rows)
    assert joint_cols.size and all(abs(x - at(0.5)) <= 1 for x in joint_cols), joint_cols


def test_pcon_holes_two_rows_per_panel(gray):
    """穴は 455 mm 間隔の格子（タイルの 1/8・3/8・5/8・7/8）。パネル 1 枚（1820×910）に 2 段 × 4 列。"""
    centers = [at(k / 8) for k in (1, 3, 5, 7)]
    background = np.median(gray)
    for y in centers:
        for x in centers:
            assert gray[y, x] < background * 0.8, (y, x)

    # 穴と穴のあいだ（格子の半ピッチずれた所）は暗くない
    assert gray[at(1 / 8), at(2 / 8)] > background * 0.9


def test_hole_and_joint_sizes_are_to_scale(gray):
    """穴の直径は 30 mm、目地は 3 mm。暗い画素の幅を実寸に直して確かめる。"""
    mm_per_px = TILE_CM * 10 / SIZE
    background = np.median(gray)
    row = gray[at(1 / 8), at(1 / 8) - 20 : at(1 / 8) + 21]
    hole_px = np.count_nonzero(row < (background + gray[at(1 / 8), at(1 / 8)]) / 2)
    assert 25 <= hole_px * mm_per_px <= 36, hole_px * mm_per_px

    column = gray[at(0.125) - 10 : at(0.125) + 10, at(0.5) - 5 : at(0.5) + 6].mean(axis=0)
    joint_px = np.count_nonzero(column < background - 4)
    # 512 px では 1 px が約 3.6 mm。3 mm の目地は縁の滑らかさを含めて 1〜2 px に収まる
    assert 1 <= joint_px <= 2, joint_px * mm_per_px


def test_tiles_without_a_seam():
    image = ft.retint(fw.compose(SPEC, SIZE, TILE_CM, "test_wall"), "C2BDBA")
    assert ft.seam_ratio(image) <= ft.MAX_SEAM


@pytest.mark.parametrize("hex_text", ["C2BDBA", "8F8675"])
def test_retint_reaches_the_declared_color_with_visible_pattern(tmp_path, hex_text):
    path = tmp_path / "wall.jpg"
    ft.retint(fw.compose(SPEC, SIZE, TILE_CM, "test_wall"), hex_text).save(path, quality=90, subsampling=0)
    assert ft.report("wall", path, hex_text)


@pytest.mark.parametrize(
    "change",
    [{"panel_mm": [1000, 910]}, {"panel_mm": [1820, 700]}, {"pcon_pitch_mm": 400}],
)
def test_layout_must_divide_the_tile(change):
    with pytest.raises(ValueError):
        fw.compose({**SPEC, **change}, 64, TILE_CM, "a")


def test_manifest_formwork_entry_is_drawable():
    manifest = json.loads(ft.MANIFEST.read_text(encoding="utf-8"))
    entries = [s for s in manifest["surfaces"] if s.get("procedural") == "formwork"]
    assert entries
    for surface in entries:
        fw.check_layout(surface["formwork"], surface["tile_cm"] * 10)
        assert all(m.startswith("lecture_") and m.endswith("_wall") for m in surface["materials"])
