"""宣言した全高どおりにメッシュが仕上がる (#47)。

silhouette_pad を持つキャラは、髪と履物のはみ出しを素体の高さから差し引き、履物の底から髪の先までの
全高を宣言値（params の height）に合わせる。pad は髪型と履物の形から測った値なので、形を変えて測り直し
忘れると、全高がそのぶんずれる。組んだメッシュの高さの幅を宣言値と比べて、そのずれを捕まえる。
"""

import functools

import numpy as np
import pytest

from kcd_chara import body, cloth, hair, params
from kcd_chara import mesh as M

#: 宣言した全高と、組んだメッシュの全高の差の上限（m）。
TOLERANCE = 0.01

PADDED = tuple(n for n in params.ALL_IDS if params.CHARACTERS[n].get("silhouette_pad"))


@functools.cache
def _build(name):
    p = params.resolve(name)
    mb = M.MeshBuilder()
    a, head, fs, uv_box, _ = body.build_base(mb, p)
    hair.build_hair(mb, p, head, a, fs, uv_box)
    cloth.build_outfit(mb, p, a)
    return p, mb


def test_low_head_count_characters_declare_pad():
    # 低頭身の 2 人は pad を持つ。持たないと、下のテストは何も比べずに通る。
    assert {"botchan", "madonna"} <= set(PADDED), PADDED


@pytest.mark.parametrize("name", PADDED)
def test_declared_height_matches_mesh(name):
    p, mb = _build(name)
    span = float(np.ptp(np.asarray(mb.verts, dtype=float)[:, 2]))
    assert abs(span - p["silhouette_h"]) < TOLERANCE, (name, span, p["silhouette_h"])
