"""kcd_lib.shrubs: 低木と草むらの形・色・置き場所 (#78)。

Blender なしで試せる範囲。取り込んだ Kenney のモデル（data/shrubs/kenney_shrubs.json）の三角形・高さ・足元、
面の色が CampusColors の折れ線の上にあること、コミット済みの shrubs.json が建物・通り道・扉・看板・木・
メインストリート・水盤・公園・グラウンドを避けていること。

扉と通り道は entrances.plan で、看板は扉から決まる位置で組み直す。build_campus.py が Blender の中で作る
sign_placed（看板を避けて動かした後の位置）、mall_beds、水盤と池の占有は組み直せないので、ここでは入れない。
build_campus.py --only-shrubs は実行のたびに、組み直した木が trees.json と一致することを確かめている。
"""

import json
import math
import os
import random

import pytest

import build_campus  # noqa: E402  (conftest が sys.path を通す)
from conftest import CAMPUS_JSON, REPO
from kcd_lib import entrances, shrubs, site
from kcd_lib import park as parklib
from kcd_lib import trees as treelib

CAMPUS_FOLDER = os.path.join(REPO, "unity", "KatsushikaCampusDays", "Assets", "Models", "Campus")
SHRUBS_JSON = os.path.join(CAMPUS_FOLDER, "shrubs.json")
TREES_JSON = os.path.join(CAMPUS_FOLDER, "trees.json")

# Kenney Nature Kit 2.1 の 10 点の三角形数（表と裏を別々に数えた値。tools/fetch_shrubs.py が固定した zip から）
TRIANGLES = {
    "grass": 132, "grass_large": 224, "grass_leafs": 36, "grass_leafsLarge": 144,
    "plant_bush": 32, "plant_bushDetailed": 104, "plant_bushLarge": 60,
    "plant_bushLargeTriangle": 39, "plant_bushSmall": 16, "plant_bushTriangle": 30,
}

# 実際に置く大きさ（原型の大きさ x 揺らぎ）の高さの範囲 [m]
HEIGHT = {"bush": (0.45, 1.40), "grass": (0.25, 0.70)}

# shrubs.fbx 全体の三角形の上限。WebGL の 1 フレームは 742k 三角形（docs/WEBGL_BUDGET.md 1.3）で、その 3.5% まで
TRIANGLE_BUDGET = 26000


def _read(path):
    with open(path, encoding="utf-8") as fp:
        return json.load(fp)


@pytest.fixture(scope="module")
def models():
    return shrubs.load()["models"]


@pytest.fixture(scope="module")
def committed():
    return _read(SHRUBS_JSON)


@pytest.fixture(scope="module")
def tree_items():
    """[(x, y, 種類, 大きさ, 向き)]。build_campus の trees と同じ並び。"""
    return [(t["x"], t["y"], t["species"], t["scale"], t["rot"]) for t in _read(TREES_JSON)["trees"]]


@pytest.fixture(scope="module")
def world():
    """campus.json を build_campus と同じ手順で読み、扉と看板を決めたもの。(data, frame, ctx)。"""
    data = parklib.apply_buildings(build_campus.load_data(CAMPUS_JSON), parklib.load())
    frame = build_campus.make_frame(data)
    ctx = {}
    entrances.plan(data, frame, ctx)
    ctx["sign_placed"] = list(ctx["sign"])
    return data, frame, ctx


class _OpenWater:
    """水盤と池の占有の代わり（Blender なしでは組み直せない）。どこも塞がない。"""

    @staticmethod
    def blocked(_x, _y):
        return False


# ---- Kenney のモデル ----

def test_prototype_triangle_counts_are_the_ones_in_the_pinned_kit(models):
    assert {name: len(m["triangles"]) for name, m in models.items()} == TRIANGLES
    assert sum(len(m["triangles"]) for m in models.values()) == 817


def test_every_triangle_indexes_existing_vertices(models):
    for name, m in models.items():
        n = len(m["vertices"])
        assert all(len(tri) == 3 and all(0 <= k < n for k in tri) for tri in m["triangles"]), name
        assert all(all(math.isfinite(c) for c in v) for v in m["vertices"]), name


@pytest.mark.parametrize("name", sorted(TRIANGLES))
def test_models_stand_on_the_ground_with_a_garden_height(models, name):
    m = models[name]
    verts = shrubs.to_z_up(m["vertices"], shrubs.base_scale(name, m["group"]))
    zs = [v[2] for v in verts]
    assert min(zs) == pytest.approx(0.0, abs=1e-6)             # 足元は地面
    lo, hi = HEIGHT[m["group"]]
    assert lo <= max(zs) * min(shrubs.JITTER) and max(zs) * max(shrubs.JITTER) <= hi
    assert max(math.hypot(v[0], v[1]) for v in verts) < 2.0     # 横に張り出しすぎない


def test_z_up_turns_kenney_y_up_without_mirroring():
    (p,) = shrubs.to_z_up([(1.0, 2.0, 3.0)], 2.0)
    assert p == (2.0, -6.0, 4.0)                                # (x, y, z) -> (x, -z, y)
    a, b, c = shrubs.to_z_up([(0.0, 0.0, 0.0), (1.0, 0.0, 0.0), (0.0, 1.0, 0.0)], 1.0)
    before = ((1.0, 0.0, 0.0), (0.0, 1.0, 0.0))
    after = (tuple(b[k] - a[k] for k in range(3)), tuple(c[k] - a[k] for k in range(3)))
    # 回転なので法線の向きは同じ回転を受ける。Y 上の (0, 0, 1) は Z 上の (0, -1, 0) へ
    n_before = shrubs._cross3(*before)
    n_after = shrubs._cross3(*after)
    assert n_after == (n_before[0], -n_before[2], n_before[1])


# ---- 色 ----

def _dist_to_polyline(color, ramp):
    best = float("inf")
    for a, b in zip(ramp, ramp[1:]):
        ab = [b[k] - a[k] for k in range(3)]
        ac = [color[k] - a[k] for k in range(3)]
        t = sum(ab[k] * ac[k] for k in range(3)) / sum(v * v for v in ab)
        t = min(1.0, max(0.0, t))
        d = math.dist(color, [a[k] + ab[k] * t for k in range(3)])
        best = min(best, d)
    return best


@pytest.mark.parametrize("name", sorted(TRIANGLES))
def test_model_colors_lie_on_the_campus_palette(models, name):
    """材質は `leaf` の 1 つで、面の色は木の葉（低木）か草（草むら）の CampusColors の折れ線の上にある。"""
    m = models[name]
    mb = shrubs.model_mesh("shrub_mesh_" + name, m, shrubs.base_scale(name, m["group"]))
    assert mb.mat_names == ["leaf"]
    assert len(mb.faces) == len(m["triangles"])
    assert mb.face_col is not None and len(mb.face_col) == len(mb.faces)
    ramp = treelib._RAMP if m["group"] == "bush" else shrubs._GRASS_RAMP
    assert max(_dist_to_polyline(c, ramp) for c in mb.face_col.values()) < 1e-6


def test_bush_never_reaches_the_brightest_leaf_and_brightens_upward():
    assert shrubs.BUSH_T_BASE + shrubs.BUSH_T_HEIGHT + shrubs.BUSH_T_UP <= shrubs.BUSH_T_MAX < 1.0
    assert shrubs.face_color("bush", 1.0, 1.0) != treelib.leaf_color(1.0)
    low, high = shrubs.face_color("bush", 0.1, 0.0), shrubs.face_color("bush", 0.9, 0.0)
    assert sum(low) < sum(high)
    side, top = shrubs.face_color("bush", 0.5, 0.0), shrubs.face_color("bush", 0.5, 1.0)
    assert sum(side) < sum(top)


def test_grass_runs_from_dark_grass_to_leaf_top():
    ramp = shrubs._GRASS_RAMP
    assert shrubs.face_color("grass", 0.0, 0.0) == pytest.approx(ramp[0])
    assert shrubs.face_color("grass", 0.5, 0.0) == pytest.approx(ramp[1])
    assert shrubs.face_color("grass", 1.0, 0.0) == pytest.approx(ramp[2])


# ---- コミット済みの shrubs.json ----

def test_committed_shrubs_json_is_consistent(committed, models):
    items = committed["shrubs"]
    assert committed["models"] == sorted(models)
    assert [s["i"] for s in items] == list(range(len(items)))
    assert all(s["model"] in models and s["kind"] == models[s["model"]]["group"] for s in items)
    lo, hi = shrubs.JITTER
    assert all(lo - 1e-3 <= s["scale"] <= hi + 1e-3 for s in items)
    assert all(0.0 <= s["rot"] <= 2.0 * math.pi + 1e-3 for s in items)


def test_committed_counts_and_triangles_stay_within_budget(committed, models):
    kinds = [s["kind"] for s in committed["shrubs"]]
    assert 0 < kinds.count("bush") <= shrubs.MAX_BUSHES
    assert 0 < kinds.count("grass") <= shrubs.MAX_GRASS
    triangles = sum(len(models[s["model"]]["triangles"]) for s in committed["shrubs"])
    assert triangles <= TRIANGLE_BUDGET


def test_committed_shrubs_keep_clear_of_buildings_paths_doors_signs_and_trees(committed, world, tree_items):
    data, frame, ctx = world
    keep = shrubs.Keepout(data, frame, ctx, _OpenWater, tree_items)
    bad = [(s["i"], s["model"], keep.reason(s["x"], s["y"], s["kind"])) for s in committed["shrubs"]]
    bad = [b for b in bad if b[2] is not None]
    assert not bad, bad[:10]


def test_committed_shrubs_of_a_kind_are_not_crowded(committed):
    for kind in ("bush", "grass"):
        pts = [(s["x"], s["y"]) for s in committed["shrubs"] if s["kind"] == kind]
        near = shrubs.CLEAR[kind]["crowd"] - 0.01          # json は 3 桁に丸めてある
        crowded = [(i, j) for i in range(len(pts)) for j in range(i + 1, len(pts))
                   if math.dist(pts[i], pts[j]) < near]
        assert not crowded, (kind, crowded[:10])


# ---- 置き場所を決める plan ----

def _plan(world, tree_items, models, seed=1):
    data, frame, ctx = world
    stats = {}
    items = shrubs.plan(data, frame, ctx, tree_items, _OpenWater, random.Random(seed + shrubs.SEED_OFFSET),
                        models=models, stats=stats)
    return items, stats


def test_plan_is_deterministic_and_within_the_caps(world, tree_items, models):
    first, stats = _plan(world, tree_items, models)
    second, _ = _plan(world, tree_items, models)
    assert first == second
    kinds = [models[it[2]]["group"] for it in first]
    assert kinds.count("bush") <= shrubs.MAX_BUSHES and kinds.count("grass") <= shrubs.MAX_GRASS
    assert stats["hosts"] > 0 and stats["wanted"][0] >= kinds.count("bush")


def test_plan_places_only_where_the_keepout_allows(world, tree_items, models):
    data, frame, ctx = world
    items, _ = _plan(world, tree_items, models)
    keep = shrubs.Keepout(data, frame, ctx, _OpenWater, tree_items)
    assert items
    assert all(keep.reason(x, y, models[name]["group"]) is None for x, y, name, _sc, _rot in items)


def test_triangle_total_and_sidecar_agree(models):
    items = [(1.0, 2.0, "plant_bush", 1.0, 0.0), (3.0, 4.0, "grass", 1.1, 1.0)]
    assert shrubs.triangle_total(items, models) == TRIANGLES["plant_bush"] + TRIANGLES["grass"]
    side = shrubs.sidecar(items, models)
    assert side["models"] == sorted(models)
    assert [(s["x"], s["y"], s["model"], s["kind"]) for s in side["shrubs"]] == [
        (1.0, 2.0, "plant_bush", "bush"), (3.0, 4.0, "grass", "grass")]


# ---- 占有の写し（水盤・池だけの占有を建物のあとの占有と分けるのに使う）----

def test_occupancy_copy_is_independent_of_the_original():
    occ = site.Occupancy()
    occ.stamp_disc(0.0, 0.0, 1.0)
    twin = occ.copy()
    assert twin.blocked(0.0, 0.0) and not twin.blocked(100.0, 100.0)
    occ.stamp_disc(100.0, 100.0, 1.0)
    assert occ.blocked(100.0, 100.0) and not twin.blocked(100.0, 100.0)
    twin.stamp_disc(-100.0, -100.0, 1.0)
    assert twin.blocked(-100.0, -100.0) and not occ.blocked(-100.0, -100.0)
