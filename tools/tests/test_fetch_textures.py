"""tools/fetch_textures.py: 面のテクスチャの加工と、焼いた画像・.meta の整合 (#78)。

ネットワークには出ない。ambientCG の API と zip は偽物に差し替え、加工は合成画像で、
整合はリポジトリに置いた jpg / .meta / MaterialLibrary.cs / CREDITS.md で確かめる。
"""

import io
import json
import math
import os
import random
import re
import sys
import uuid
import zipfile

import pytest

pytest.importorskip("PIL")

from PIL import Image, ImageChops, ImageStat  # noqa: E402

TOOLS = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
if TOOLS not in sys.path:
    sys.path.insert(0, TOOLS)

import fetch_textures as ft  # noqa: E402


def _noise(size, seed=7):
    """一様乱数の灰色画像。どこで切っても同じ性質なので、そのままタイルとして扱える。"""
    rng = random.Random(seed)
    width, height = size
    return Image.frombytes("L", size, bytes(rng.randrange(256) for _ in range(width * height)))


def _periodic(side, period):
    """縦横とも side で 1 周する正弦波。端どうしが滑らかにつながる。"""
    return Image.frombytes(
        "L",
        (side, side),
        bytes(
            round(128 + 60 * math.sin(2 * math.pi * x / period) * math.cos(2 * math.pi * y / period))
            for y in range(side)
            for x in range(side)
        ),
    )


def _roll(image, dx, dy):
    return ImageChops.offset(image, dx, dy)


def _max_diff(a, b):
    """画素ごとの差の最大。RGB ならチャンネルをまたいだ最大。"""
    extrema = ImageChops.difference(a, b).getextrema()
    bands = extrema if isinstance(extrema[0], tuple) else (extrema,)
    return max(high for _, high in bands)


# ---- 縮小と正方形化 ----

def test_resize_tileable_commutes_with_wrap():
    """ずらしてから縮めても、縮めてからずらしても同じ = 端も内側と同じに縮められている。"""
    tile = _noise((1024, 1024))
    shifted_first = ft.resize_tileable(_roll(tile, 300, 100), 512)
    shifted_after = _roll(ft.resize_tileable(tile, 512), 150, 50)
    assert _max_diff(shifted_first, shifted_after) == 0

    # 端を折り返さない素の縮小ではこの性質が崩れる（テストが実装を見分けられることの確認）
    naive_first = _roll(tile, 300, 100).resize((512, 512), Image.LANCZOS)
    naive_after = _roll(tile.resize((512, 512), Image.LANCZOS), 150, 50)
    assert _max_diff(naive_first, naive_after) > 10


def test_resize_tileable_size():
    assert ft.resize_tileable(_noise((256, 256)), 64).size == (64, 64)


def test_square_tile_stacks_landscape_vertically():
    wide = _noise((256, 128))
    square = ft.square_tile(wide)
    assert square.size == (256, 256)
    assert _max_diff(square.crop((0, 0, 256, 128)), wide) == 0
    assert _max_diff(square.crop((0, 128, 256, 256)), wide) == 0


def test_square_tile_stacks_portrait_horizontally():
    tall = _noise((64, 192))
    square = ft.square_tile(tall)
    assert square.size == (192, 192)
    for i in range(3):
        assert _max_diff(square.crop((i * 64, 0, i * 64 + 64, 192)), tall) == 0


def test_square_tile_rejects_non_integer_ratio():
    with pytest.raises(ValueError):
        ft.square_tile(_noise((300, 200)))


def test_resize_tileable_keeps_rectangular_tile_seamless():
    """Concrete034 のような 2:1 の素材も、正方形に並べてから縮めるので巻いても段差が出ない。"""
    wide = _periodic(256, 128).crop((0, 0, 256, 128))
    assert ft.seam_ratio(ft.resize_tileable(wide, 128)) <= ft.MAX_SEAM


# ---- 継ぎ目の検出 ----

def test_seam_ratio_passes_periodic_image():
    assert ft.seam_ratio(_periodic(128, 64)) <= ft.MAX_SEAM


def test_seam_ratio_flags_gradient():
    gradient = Image.frombytes("L", (128, 128), bytes(x * 2 for _ in range(128) for x in range(128)))
    assert ft.seam_ratio(gradient) > ft.MAX_SEAM


# ---- 色 ----

@pytest.mark.parametrize("target", ["3C3C3C", "6FA84A", "8E3B2F", "D8D6D0"])
def test_retint_hits_target_mean(target):
    image = Image.merge("RGB", [_noise((128, 128), seed) for seed in (1, 2, 3)])
    mean = ft.linear_mean(ft.retint(image, target))
    got = [round(ft.to_srgb(c) * 255) for c in mean]
    want = [int(target[i : i + 2], 16) for i in (0, 2, 4)]
    assert max(abs(g - w) for g, w in zip(got, want)) <= 1


def test_srgb_round_trip():
    for byte in range(256):
        assert round(ft.to_srgb(ft.LINEAR_OF_BYTE[byte]) * 255) == byte


def test_shape_keeps_channel_mean():
    """明暗を詰める基準は画像自身の平均なので、詰めても平均は動かない。"""
    image = Image.merge("RGB", [_noise((64, 64), seed) for seed in (4, 5, 6)])
    before = ImageStat.Stat(image).mean
    after = ImageStat.Stat(ft.shape(image, 1.0, 0.5)).mean
    assert max(abs(a - b) for a, b in zip(before, after)) < 1.0


# ---- 凹凸の焼き込み ----

def test_bake_detail_only_darkens():
    color = Image.new("RGB", (64, 64), (200, 180, 160))
    baked = ft.bake_detail(color, {"Displacement": _noise((128, 128))}, 64, 0.5)
    assert ImageChops.subtract(baked, color).getbbox() is None  # 明るくなった画素が無い
    assert ImageChops.subtract(color, baked).getbbox() is not None  # 暗くなった画素はある


def test_bake_detail_without_effect():
    color = Image.new("RGB", (64, 64), (200, 180, 160))
    assert ft.bake_detail(color, {}, 64, 0.5) is color
    assert ft.bake_detail(color, {"Displacement": _noise((128, 128))}, 64, 0) is color


def _detail_maps(side=256):
    """AO と Displacement の代わり。どちらも巻いてつながる。"""
    return {
        "AmbientOcclusion": _periodic(side, side // 4),
        "Displacement": ImageChops.add(_periodic(side, side), _noise((side, side), seed=11), scale=2),
    }


@pytest.mark.parametrize("highpass", [0, 8])
def test_bake_detail_with_both_maps_only_darkens_and_keeps_seam(highpass):
    """実際の素材は AO と Displacement の両方を使い、Concrete034 は highpass も掛ける。"""
    color = Image.new("RGB", (128, 128), (200, 180, 160))
    baked = ft.bake_detail(color, _detail_maps(), 128, 0.5, highpass)
    assert ImageChops.subtract(baked, color).getbbox() is None
    assert ImageChops.subtract(color, baked).getbbox() is not None
    assert ft.seam_ratio(baked) <= ft.MAX_SEAM


def test_bake_detail_blends_both_maps():
    """片方だけのときとは違う結果になる = 2 枚目も効いている。"""
    color = Image.new("RGB", (128, 128), (200, 180, 160))
    maps = _detail_maps()
    both = ft.bake_detail(color, maps, 128, 0.5)
    only_ao = ft.bake_detail(color, {"AmbientOcclusion": maps["AmbientOcclusion"]}, 128, 0.5)
    assert _max_diff(both, only_ao) > 10


def test_pipeline_meets_report_criteria(tmp_path, monkeypatch):
    """main が流す順（縮小 → 彩度と明暗 → 凹凸 → 色合わせ → jpg）で、report の基準を満たす。

    --check はリポジトリの jpg を測るだけなので、作る側の関数はここで通しておく。
    """
    side = 256
    color = Image.merge("RGB", [ImageChops.add(_periodic(side, side // 2), _noise((side, side), s), scale=2) for s in (1, 2, 3)])
    monkeypatch.setattr(ft, "download_maps", lambda asset, variant, wanted: {"Color": color, **_detail_maps(side)})
    surface = {"asset": "Bricks101", "size_px": 128, "saturation": 0.4, "contrast": 0.7, "depth": 0.5, "highpass_px": 8}

    shaped = ft.bake_surface(surface, "1K-JPG")
    assert shaped.size == (128, 128)

    path = tmp_path / "brick_red.jpg"
    assert ft.finish_material("brick_red", path, "8E3B2F", shaped, 92, in_unity=False)
    assert path.exists() and not ft.meta_path(path).exists()


def test_finish_material_check_mode_flags_what_is_missing(tmp_path, monkeypatch, capsys):
    """--check は書かないので、色・画像・.meta のどれかが欠けていれば測る前に NG にする。"""
    monkeypatch.setattr(ft, "ROOT", tmp_path)
    path = tmp_path / "grass.jpg"
    assert not ft.finish_material("grass", path, None, None, 92, in_unity=False)
    assert not ft.finish_material("grass", path, "6FA84A", None, 92, in_unity=False)
    Image.new("RGB", (8, 8)).save(path)
    assert not ft.finish_material("grass", path, "6FA84A", None, 92, in_unity=True)

    out = capsys.readouterr().out
    assert "CampusColors に無い" in out and "まだ焼かれていない" in out and ".meta が無い" in out


def test_highpass_removes_large_blotches_and_keeps_seam():
    """1 周の大きなムラ + 細かい粒から、大きなムラだけが抜ける。"""
    side = 256
    blotch = _periodic(side, side)
    grain = _noise((side, side), seed=9).point(lambda v: 128 + (v - 128) // 8)
    image = ImageChops.add(blotch, grain, scale=1, offset=-128)

    def blotch_share(im):
        # 16 px 四方の平均どうしのばらつき ÷ 全体のばらつき。大きなムラが多いほど 1 に近い
        blocks = im.resize((side // 16, side // 16), Image.BOX)
        return ImageStat.Stat(blocks).stddev[0] / ImageStat.Stat(im).stddev[0]

    filtered = ft.highpass_tileable(image, 8)
    assert blotch_share(image) > 0.9
    assert blotch_share(filtered) < 0.3
    assert ft.seam_ratio(filtered) <= ft.MAX_SEAM


# ---- 取得（ネットワークに出る前に弾くもの） ----

@pytest.mark.parametrize(
    "url",
    [
        "http://ambientcg.com/get?file=Grass004_1K-JPG.zip",
        "file:///etc/passwd",
        "https://ambientcg.com.example.net/get",
        "https://example.net/ambientcg.com",
    ],
)
def test_fetch_refuses_anything_but_https_ambientcg(url):
    with pytest.raises(SystemExit, match="ambientCG ではない"):
        ft.fetch(url, "test")


@pytest.mark.parametrize("asset, variant", [("../Grass004", "1K-JPG"), ("Grass004", "1K-JPG/../x"), ("Grass", "1K-JPG")])
def test_download_maps_refuses_odd_names(asset, variant):
    with pytest.raises(SystemExit, match="書き方"):
        ft.download_maps(asset, variant, ["Color"])


# ---- 取得（ambientCG の API と zip を偽物にして） ----

def _jpg(image):
    buffer = io.BytesIO()
    image.save(buffer, "JPEG")
    return buffer.getvalue()


def _zip(members):
    buffer = io.BytesIO()
    with zipfile.ZipFile(buffer, "w") as zipped:
        for name, data in members.items():
            zipped.writestr(name, data)
    return buffer.getvalue()


ZIP_LINK = "https://ambientcg.com/get?file=Grass004_1K-JPG.zip"


@pytest.fixture
def fake_ambientcg(tmp_path, monkeypatch):
    """ambientCG の返事を、実物と同じ形の JSON と zip に差し替える。取りに行った URL を返す。"""
    monkeypatch.setattr(ft, "CACHE", tmp_path / "cache")
    archive = _zip(
        {
            # 実物の zip には Color / Displacement / NormalDX / NormalGL / Roughness と .usdc などが入っている
            "Grass004_1K-JPG_Color.jpg": _jpg(Image.new("RGB", (32, 32), (90, 140, 60))),
            "Grass004_1K-JPG_Displacement.jpg": _jpg(_noise((32, 32)).convert("RGB")),
            "Grass004_1K-JPG_NormalGL.jpg": _jpg(_noise((32, 32), seed=3).convert("RGB")),
            "Grass004.png": b"",
        }
    )
    downloads = [
        {"attribute": "2K-JPG", "downloadLink": "https://ambientcg.com/get?file=Grass004_2K-JPG.zip"},
        {"attribute": "1K-JPG", "downloadLink": ZIP_LINK},
    ]
    api = {"foundAssets": [{"downloadFolders": {"default": {"downloadFiletypeCategories": {"zip": {"downloads": downloads}}}}}]}
    fetched = []

    def fetch(url, what):
        fetched.append(url)
        return json.dumps(api).encode() if "/api/" in url else archive

    monkeypatch.setattr(ft, "fetch", fetch)
    return fetched


def test_download_maps_takes_only_wanted_maps_and_caches(fake_ambientcg):
    maps = ft.download_maps("Grass004", "1K-JPG", ["Color", *ft.DETAIL_MAPS])
    # AmbientOcclusion は zip に無いので黙って落とし、NormalGL は頼んでいないので取らない
    assert sorted(maps) == ["Color", "Displacement"]
    assert maps["Color"].size == (32, 32)
    assert "id=Grass004" in fake_ambientcg[0] and fake_ambientcg[1:] == [ZIP_LINK]

    # 2 回目はキャッシュの zip を読むだけで、取りに行かない
    assert sorted(ft.download_maps("Grass004", "1K-JPG", ["Color"])) == ["Color"]
    assert len(fake_ambientcg) == 2


def test_download_maps_needs_the_variant(fake_ambientcg):
    with pytest.raises(SystemExit, match="4K-JPG が無い"):
        ft.download_maps("Grass004", "4K-JPG", ["Color"])


def test_download_maps_needs_color(fake_ambientcg):
    ft.CACHE.mkdir(parents=True)
    (ft.CACHE / "Grass004_1K-JPG.zip").write_bytes(_zip({"Grass004_1K-JPG_Displacement.jpg": _jpg(_noise((32, 32)))}))
    with pytest.raises(SystemExit, match="Color が無い.*Displacement"):
        ft.download_maps("Grass004", "1K-JPG", ["Color"])
    assert not fake_ambientcg


def test_download_maps_refuses_huge_members(fake_ambientcg, monkeypatch):
    monkeypatch.setattr(ft, "MAX_BYTES", 16)
    with pytest.raises(SystemExit, match="を超えている"):
        ft.download_maps("Grass004", "1K-JPG", ["Color"])


# ---- manifest と MaterialLibrary.cs ----

@pytest.fixture(scope="module")
def manifest():
    return json.loads(ft.MANIFEST.read_text(encoding="utf-8"))


def test_manifest_surfaces_are_well_formed(manifest):
    seen = set()
    for surface in manifest["surfaces"]:
        assert re.fullmatch(r"[A-Za-z]+\d+", surface["asset"]), surface
        assert surface["tile_cm"] > 0
        assert surface["size_px"] in (128, 256, 512, 1024)
        for key in ("saturation", "contrast", "depth"):
            assert 0 <= surface[key] <= 1, (surface["asset"], key)
        assert surface.get("highpass_px", 0) >= 0
        assert surface["materials"], surface["asset"]
        for material in surface["materials"]:
            assert material not in seen, f"{material} が 2 つの素材に割り当てられている"
            seen.add(material)


def test_manifest_materials_exist_in_campus_colors(manifest):
    colors = ft.load_campus_colors()
    missing = [m for s in manifest["surfaces"] for m in s["materials"] if m not in colors]
    assert not missing, f"MaterialLibrary.cs の CampusColors に無い: {missing}"


def test_every_asset_is_credited(manifest):
    """CC0 なので表記は義務ではないが、どの素材を使ったかは CREDITS.md から辿れるようにしておく。"""
    credits = (ft.ROOT / "docs" / "CREDITS.md").read_text(encoding="utf-8")
    missing = [s["asset"] for s in manifest["surfaces"] if s["asset"] not in credits]
    assert not missing, f"docs/CREDITS.md に載っていない ambientCG の素材: {missing}"


# ---- .meta ----

@pytest.fixture
def unity_project(tmp_path, monkeypatch):
    monkeypatch.setattr(ft, "UNITY_PROJECT", tmp_path)
    (tmp_path / "Assets" / "Textures").mkdir(parents=True)
    return tmp_path


def test_meta_guid_depends_only_on_relative_path(unity_project):
    path = unity_project / "Assets" / "Textures" / "a.jpg"
    guid = ft.meta_guid(path)
    assert re.fullmatch(r"[0-9a-f]{32}", guid)
    assert guid == uuid.uuid5(uuid.NAMESPACE_URL, ft.META_GUID_SEED + "Assets/Textures/a.jpg").hex
    assert guid != ft.meta_guid(unity_project / "Assets" / "Textures" / "b.jpg")


def test_ensure_meta_writes_once(unity_project):
    path = unity_project / "Assets" / "Textures" / "a.jpg"
    path.write_bytes(b"")
    assert ft.ensure_meta(path) is True
    meta = ft.meta_path(path)
    assert meta.read_text(encoding="utf-8") == f"fileFormatVersion: 2\nguid: {ft.meta_guid(path)}\n"

    # Unity が取り込み設定を書き足したあとの .meta は上書きしない
    meta.write_text("fileFormatVersion: 2\nguid: 0123\nTextureImporter:\n", encoding="utf-8")
    assert ft.ensure_meta(path) is False
    assert meta.read_text(encoding="utf-8").endswith("TextureImporter:\n")


def test_ensure_meta_folder(unity_project):
    folder = unity_project / "Assets" / "Textures"
    assert ft.ensure_meta(folder, folder=True) is True
    lines = ft.meta_path(folder).read_text(encoding="utf-8").splitlines()
    assert lines[:3] == ["fileFormatVersion: 2", f"guid: {ft.meta_guid(folder)}", "folderAsset: yes"]
    assert "DefaultImporter:" in lines


# ---- リポジトリに置いた成果物 ----

def _committed_textures(manifest):
    folder = ft.ROOT / manifest["output"]["folder"]
    suffix = manifest["output"]["format"]
    return [folder / f"{m}.{suffix}" for s in manifest["surfaces"] for m in s["materials"]]


def _meta_guid_line(path):
    match = re.search(r"^guid: ([0-9a-f]{32})$", ft.meta_path(path).read_text(encoding="utf-8"), re.M)
    assert match, ft.meta_path(path)
    return match.group(1)


def test_committed_metas_use_path_guids(manifest):
    """Unity が .meta を書き直しても GUID は残る。パスから決まる値のままか見る。"""
    paths = _committed_textures(manifest)
    for path in [paths[0].parent, *paths]:
        assert _meta_guid_line(path) == ft.meta_guid(path), path


def test_committed_meta_guids_are_unique_in_project(manifest):
    paths = _committed_textures(manifest)
    paths.append(paths[0].parent)
    ours = {ft.meta_guid(p) for p in paths}
    own_metas = {ft.meta_path(p) for p in paths}
    others = {}
    for meta in (ft.UNITY_PROJECT / "Assets").rglob("*.meta"):
        if meta in own_metas:
            continue
        match = re.search(r"^guid: ([0-9a-f]{32})$", meta.read_text(encoding="utf-8", errors="replace"), re.M)
        if match:
            others[match.group(1)] = meta
    clashes = {g: others[g] for g in ours if g in others}
    assert not clashes


def test_check_passes_on_committed_textures(capsys):
    """リポジトリの jpg が平均色・模様の濃さ・継ぎ目の基準をすべて満たしている。"""
    assert ft.main(["--check"]) == 0, capsys.readouterr().out
