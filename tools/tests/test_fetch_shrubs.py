"""tools/fetch_shrubs.py: Kenney の OBJ の読み方と、リポジトリに置いた変換済み JSON の整合 (#78)。

ネットワークには出ない。OBJ は文字列で与え、整合はリポジトリの data/shrubs/kenney_shrubs.json で確かめる。
ダウンロードは urlopen を差し替えて確かめる。zip の読み方は、テストの中で作る小さな zip で確かめる。
"""

import hashlib
import json
import os
import sys
import zipfile

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

    @pytest.mark.parametrize("value", ["nan", "inf", "-inf", "1e999"])
    def test_non_finite_coordinate_is_rejected(self, value):
        with pytest.raises(ValueError):
            fs.parse_obj(f"v 0 {value} 0\nv 1 0 0\nv 0 1 0\nf 1 2 3\n")


class TestRender:
    def test_non_finite_number_is_not_written(self):
        model = {"group": "bush", "vertices": [[float("nan"), 0.0, 0.0]], "triangles": [[0, 0, 0]]}
        with pytest.raises(ValueError):
            fs.render({"x": model})


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


class _FakeResponse:
    def __init__(self, data):
        self._data = data

    def __enter__(self):
        return self

    def __exit__(self, *exc):
        return False

    def read(self, size=-1):
        return self._data if size < 0 else self._data[:size]


class TestFetchZip:
    @staticmethod
    def _serve(monkeypatch, data):
        """urlopen を、data を返すものに差し替える。呼ばれた URL を返す。"""
        urls = []

        def fake_urlopen(request, timeout=None):
            urls.append(request.full_url)
            return _FakeResponse(data)

        monkeypatch.setattr(fs.urllib.request, "urlopen", fake_urlopen)
        return urls

    @pytest.fixture
    def cache(self, tmp_path, monkeypatch):
        """既定のキャッシュの zip を tmp_path の下に移す。"""
        path = tmp_path / "cache" / "kit.zip"
        monkeypatch.setattr(fs, "CACHE_ZIP", path)
        return path

    def test_mismatching_download_leaves_no_file_behind(self, cache, monkeypatch):
        urls = self._serve(monkeypatch, b"not the zip")

        with pytest.raises(SystemExit):
            fs.fetch_zip(None)

        # キャッシュには照合の済んだ zip だけを置く。一時名にも残さない
        assert not cache.exists()
        assert not cache.with_name("kit.zip.part").exists()
        assert urls == [fs.ZIP_URL]

        with pytest.raises(SystemExit):
            fs.fetch_zip(None)
        assert urls == [fs.ZIP_URL, fs.ZIP_URL]  # 次の実行は取得をやり直す

    def test_matching_download_is_put_under_its_name(self, cache, monkeypatch):
        data = b"pretend this is the zip"
        monkeypatch.setattr(fs, "ZIP_SHA256", hashlib.sha256(data).hexdigest())
        self._serve(monkeypatch, data)

        assert fs.fetch_zip(None) == cache

        assert cache.read_bytes() == data
        assert not cache.with_name("kit.zip.part").exists()

    def test_cached_zip_is_not_downloaded_again(self, cache, monkeypatch):
        data = b"already here"
        monkeypatch.setattr(fs, "ZIP_SHA256", hashlib.sha256(data).hexdigest())
        urls = self._serve(monkeypatch, b"would be a different file")
        cache.parent.mkdir()
        cache.write_bytes(data)

        assert fs.fetch_zip(None) == cache

        assert urls == []

    def test_cached_zip_of_another_version_is_replaced(self, cache, monkeypatch):
        """ZIP_SHA256 を差し替えた後は、キャッシュに残った前の版を落とし直した zip で置き換える。"""
        data = b"the new version"
        monkeypatch.setattr(fs, "ZIP_SHA256", hashlib.sha256(data).hexdigest())
        urls = self._serve(monkeypatch, data)
        cache.parent.mkdir()
        cache.write_bytes(b"the old version")

        assert fs.fetch_zip(None) == cache

        assert cache.read_bytes() == data
        assert urls == [fs.ZIP_URL]

    def test_oversized_download_is_rejected(self, cache, monkeypatch):
        monkeypatch.setattr(fs, "MAX_ZIP_BYTES", 4)
        self._serve(monkeypatch, b"12345")

        with pytest.raises(SystemExit):
            fs.fetch_zip(None)

        assert not cache.exists()
        assert not cache.with_name("kit.zip.part").exists()

    def test_given_zip_is_used_without_downloading(self, tmp_path, monkeypatch):
        data = b"a local copy"
        monkeypatch.setattr(fs, "ZIP_SHA256", hashlib.sha256(data).hexdigest())
        urls = self._serve(monkeypatch, b"would be a different file")
        given = tmp_path / "local.zip"
        given.write_bytes(data)

        assert fs.fetch_zip(given) == given

        assert urls == []

    def test_missing_given_zip_is_not_downloaded(self, tmp_path, monkeypatch):
        urls = self._serve(monkeypatch, b"would be downloaded")
        given = tmp_path / "typo" / "kit.zip"

        with pytest.raises(SystemExit):
            fs.fetch_zip(given)

        assert urls == []
        assert not given.parent.exists()

    def test_given_zip_of_another_version_is_rejected_and_kept(self, tmp_path, monkeypatch):
        urls = self._serve(monkeypatch, b"would be downloaded")
        given = tmp_path / "local.zip"
        given.write_bytes(b"not the zip")

        with pytest.raises(SystemExit):
            fs.fetch_zip(given)

        assert urls == []
        assert given.read_bytes() == b"not the zip"


def _tiny_zip(path):
    """欲しいモデル 10 点を、三角形 1 枚の OBJ として入れた zip を作る。"""
    with zipfile.ZipFile(path, "w") as archive:
        for name in fs.BUSHES + fs.GRASSES:
            archive.writestr(f"{fs.OBJ_DIR}{name}.obj", "v 0 0 0\nv 1 0 0\nv 0 1 0\nf 1 2 3\n")
    return path


class TestReadModels:
    def test_reads_every_wanted_model(self, tmp_path):
        with zipfile.ZipFile(_tiny_zip(tmp_path / "kit.zip")) as archive:
            models = fs.read_models(archive)
        assert sorted(models) == sorted(fs.BUSHES + fs.GRASSES)
        assert models["grass"] == {"group": "grass", "vertices": [[0.0, 0.0, 0.0], [1.0, 0.0, 0.0], [0.0, 1.0, 0.0]],
                                   "triangles": [[0, 1, 2]]}

    def test_oversized_obj_is_not_read(self, tmp_path, monkeypatch):
        monkeypatch.setattr(fs, "MAX_OBJ_BYTES", 10)
        parsed = []
        monkeypatch.setattr(fs, "parse_obj", lambda text: parsed.append(text))

        with zipfile.ZipFile(_tiny_zip(tmp_path / "kit.zip")) as archive, pytest.raises(SystemExit):
            fs.read_models(archive)
        assert parsed == []

    def test_too_many_vertices_are_rejected(self, tmp_path, monkeypatch):
        monkeypatch.setattr(fs, "MAX_VERTICES", 2)

        with zipfile.ZipFile(_tiny_zip(tmp_path / "kit.zip")) as archive, pytest.raises(SystemExit):
            fs.read_models(archive)


class TestMain:
    @pytest.fixture
    def output(self, tmp_path, monkeypatch):
        """書き出し先の JSON を tmp_path の下に移す。"""
        path = tmp_path / "data" / "shrubs" / "kenney_shrubs.json"
        monkeypatch.setattr(fs, "ROOT", tmp_path)
        monkeypatch.setattr(fs, "OUTPUT", path)
        return path

    def test_writes_the_json_and_check_agrees(self, tmp_path, output, monkeypatch):
        archive = _tiny_zip(tmp_path / "kit.zip")
        monkeypatch.setattr(fs, "ZIP_SHA256", hashlib.sha256(archive.read_bytes()).hexdigest())

        assert fs.main(["--zip", str(archive)]) == 0

        with zipfile.ZipFile(archive) as opened:
            expected = fs.render(fs.read_models(opened))
        with open(output, encoding="utf-8", newline="") as fp:
            assert fp.read() == expected
        assert not output.with_name("kenney_shrubs.json.part").exists()
        assert fs.main(["--zip", str(archive), "--check"]) == 0

    def test_zip_is_not_opened_before_its_hash_matches(self, tmp_path, output, monkeypatch):
        archive = _tiny_zip(tmp_path / "kit.zip")  # ZIP_SHA256 は本物の値のまま（合わない）
        opened = []
        monkeypatch.setattr(fs.zipfile, "ZipFile", lambda *args, **kwargs: opened.append(args))

        with pytest.raises(SystemExit):
            fs.main(["--zip", str(archive)])

        assert opened == []
        assert not output.exists()
