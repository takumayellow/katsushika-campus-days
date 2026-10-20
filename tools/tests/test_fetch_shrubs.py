"""tools/fetch_shrubs.py: Kenney の OBJ の読み方と、リポジトリに置いた変換済み JSON の整合 (#78)。

ネットワークには出ない。OBJ は文字列で与え、整合はリポジトリの data/shrubs/kenney_shrubs.json で確かめる。
"""

import json
import os
import sys

import pytest

TOOLS = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
if TOOLS not in sys.path:
    sys.path.insert(0, TOOLS)

import fetch_shrubs as fs  # noqa: E402

# zip から読んだ三角形の数。Kenney の更新で変わったら気付けるよう固定しておく
TRIANGLES = {
    "plant_bush": 32,
    "plant_bushDetailed": 104,
    "plant_bushLarge": 60,
    "plant_bushLargeTriangle": 39,
    "plant_bushSmall": 16,
    "plant_bushTriangle": 30,
    "grass": 132,
    "grass_large": 224,
    "grass_leafs": 36,
    "grass_leafsLarge": 144,
}


@pytest.fixture(scope="module")
def committed():
    with open(fs.OUTPUT, encoding="utf-8") as fp:
        return json.load(fp)


class TestParseObj:
    def test_reads_vertices_and_triangles(self):
        text = "# comment\nmtllib x.mtl\ng a\nv 0 0 0\nv 1 0 0\nv 0 1 0\nusemtl m\nf 1 2 3\n"
        vertices, triangles = fs.parse_obj(text)
        assert vertices == [[0.0, 0.0, 0.0], [1.0, 0.0, 0.0], [0.0, 1.0, 0.0]]
        assert triangles == [[0, 1, 2]]

    def test_quad_becomes_a_fan_of_two_triangles(self):
        text = "v 0 0 0\nv 1 0 0\nv 1 1 0\nv 0 1 0\nf 1 2 3 4\n"
        _, triangles = fs.parse_obj(text)
        assert triangles == [[0, 1, 2], [0, 2, 3]]

    def test_slash_forms_use_only_the_vertex_index(self):
        text = "v 0 0 0\nv 1 0 0\nv 0 1 0\nvt 0 0\nvn 0 0 1\nf 1/1/1 2/1/1 3//1\n"
        _, triangles = fs.parse_obj(text)
        assert triangles == [[0, 1, 2]]

    def test_negative_index_counts_from_the_end(self):
        text = "v 0 0 0\nv 1 0 0\nv 0 1 0\nf -3 -2 -1\n"
        _, triangles = fs.parse_obj(text)
        assert triangles == [[0, 1, 2]]

    def test_index_out_of_range_is_rejected(self):
        with pytest.raises(ValueError):
            fs.parse_obj("v 0 0 0\nv 1 0 0\nv 0 1 0\nf 1 2 4\n")

    def test_missing_geometry_is_rejected(self):
        with pytest.raises(ValueError):
            fs.parse_obj("g only_a_name\n")

    def test_short_face_is_rejected(self):
        with pytest.raises(ValueError):
            fs.parse_obj("v 0 0 0\nv 1 0 0\nf 1 2\n")


class TestCommittedData:
    def test_every_wanted_model_is_there(self, committed):
        assert sorted(committed["models"]) == sorted(TRIANGLES)

    def test_groups(self, committed):
        for name, model in committed["models"].items():
            assert model["group"] == ("bush" if name in fs.BUSHES else "grass"), name

    def test_triangle_counts(self, committed):
        counts = {name: len(model["triangles"]) for name, model in committed["models"].items()}
        assert counts == TRIANGLES

    def test_indices_are_in_range_and_not_degenerate(self, committed):
        for name, model in committed["models"].items():
            n = len(model["vertices"])
            for a, b, c in model["triangles"]:
                assert 0 <= a < n and 0 <= b < n and 0 <= c < n, name
                assert len({a, b, c}) == 3, name

    def test_models_are_y_up_and_stand_on_the_ground(self, committed):
        """Kenney は Y が上で、足元が y=0。Blender の Z 上へ回す向きと接地は、この前提に立つ。"""
        assert committed["axes"] == "Y_up"
        for name, model in committed["models"].items():
            ys = [v[1] for v in model["vertices"]]
            assert min(ys) == pytest.approx(0.0, abs=1e-6), name
            assert 0.1 < max(ys) < 0.5, name

    def test_license_and_source_are_recorded(self, committed):
        assert committed["source"]["license"] == "CC0 1.0"
        assert committed["source"]["license_url"].startswith("https://creativecommons.org/publicdomain/zero/1.0")
        assert committed["source"]["page"] == "https://kenney.nl/assets/nature-kit"
        assert committed["zip"]["sha256"] == fs.ZIP_SHA256

    def test_file_is_what_the_script_writes(self, committed):
        """書式が render() の出力と一致する（手で触って壊した JSON を弾く）。"""
        models = {name: {"group": m["group"], "vertices": m["vertices"], "triangles": m["triangles"]}
                  for name, m in committed["models"].items()}
        with open(fs.OUTPUT, encoding="utf-8", newline="") as fp:
            assert fp.read() == fs.render(models)
