"""brick_bond: 素材の格子を測り、芋張り・1 枚ずつの色・暗い目地で組み直す (#109)。"""

from __future__ import annotations

import sys
from pathlib import Path

import numpy as np
import pytest

pytest.importorskip("PIL")
from PIL import Image  # noqa: E402

TOOLS = str(Path(__file__).resolve().parents[1])
if TOOLS not in sys.path:
    sys.path.insert(0, TOOLS)

import brick_bond as bb  # noqa: E402

COURSES, PER_COURSE, SIZE = 6, 3, 240

SPEC = {
    "source_grid": [COURSES, PER_COURSE],
    "grid": [8, 4],
    "joint": 0.1,
    "grain": 0.5,
    "palette": [{"hex": "#4E423C", "weight": 0.5}, {"hex": "#8A573F", "weight": 0.5}],
    "joint_ratio": 0.38,
}


def synthetic_source(offset: int = 7) -> tuple[Image.Image, Image.Image]:
    """馬張りの素材: 横目地 y = offset + k·40、縦目地は段ごとに半枚ずらす。目地は凹み（暗い）。"""
    height = np.full((SIZE, SIZE), 200.0)
    pitch_y, pitch_x = SIZE // COURSES, SIZE // PER_COURSE
    for course in range(COURSES):
        y = (offset + course * pitch_y) % SIZE
        height[[y % SIZE, (y + 1) % SIZE], :] = 40
        shift = offset + (pitch_x // 2 if course % 2 else 0)
        rows = [(y + 2 + i) % SIZE for i in range(pitch_y - 2)]
        for index in range(PER_COURSE):
            x = (shift + index * pitch_x) % SIZE
            height[np.ix_(rows, [x % SIZE, (x + 1) % SIZE])] = 40

    rng = np.random.default_rng(0)
    color = np.clip(height[..., None] * np.array([0.9, 0.6, 0.5]) + rng.normal(0, 8, (SIZE, SIZE, 3)), 0, 255)
    return Image.fromarray(color.astype(np.uint8)), Image.fromarray(height.astype(np.uint8))


def test_find_grid_locates_the_joints():
    _, height = synthetic_source(offset=7)
    grid = bb.find_grid(np.asarray(height, dtype=np.float64), COURSES, PER_COURSE)
    assert abs(grid["row_phase"] - 7.5) <= 1.0
    assert abs(grid["col_phases"][0] - 7.5) <= 1.0
    assert abs(grid["col_phases"][1] - (7.5 + SIZE / PER_COURSE / 2)) <= 1.0
    assert 1.0 <= grid["joint_y"] <= 4.0


def test_compose_is_deterministic_and_stack_bonded():
    color, height = synthetic_source()
    first = np.asarray(bb.compose(color, height, SPEC, 128, "Bricks092"), dtype=np.float64)
    second = np.asarray(bb.compose(color, height, SPEC, 128, "Bricks092"), dtype=np.float64)
    assert first.shape == (128, 128, 3)
    assert np.array_equal(first, second)

    # 芋張り: 縦目地（x = 0, 32, 64, 96 の上）は全段で暗い
    lum = first.mean(axis=2)
    brick = lum[:, 16].mean()
    for x in (0, 32, 64, 96):
        assert lum[:, x].mean() < brick * 0.8


def test_compose_tiles_without_a_seam():
    color, height = synthetic_source()
    lum = np.asarray(bb.compose(color, height, SPEC, 128, "Bricks092"), dtype=np.float64).mean(axis=2)
    # 端は目地の中心なので、左右・上下の端どうしはどちらも目地の色でそろう
    assert abs(lum[:, 0].mean() - lum[:, -1].mean()) < 6
    assert abs(lum[0].mean() - lum[-1].mean()) < 6


def test_deal_colors_keeps_the_weights_per_block():
    rng = np.random.default_rng(1)
    dealt = bb.deal_colors(rng, np.array([0.25, 0.75]), 8, 4, 2)
    for top in range(0, 8, 2):
        block = dealt[top : top + 2]
        assert (block == 0).sum() == 2
        assert (block == 1).sum() == 6


def test_joint_is_darker_than_the_bricks():
    color, height = synthetic_source()
    lum = np.asarray(bb.compose(color, height, SPEC, 128, "Bricks092"), dtype=np.float64).mean(axis=2)
    assert lum[0, :].mean() < lum[8, 5:27].mean()


def test_bad_palette_weights_are_rejected():
    color, height = synthetic_source()
    spec = {**SPEC, "palette": [{"hex": "#4E423C", "weight": 0}]}
    with pytest.raises(ValueError):
        bb.compose(color, height, spec, 64, "x")
