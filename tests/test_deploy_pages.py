"""tools/deploy_pages.py のテスト (#75)。python -m pytest tests で回す。

Unity と gh は呼ばない。unity_build / upload_release / dispatch_workflow / git_state は差し替える。
git_state だけは一時ディレクトリに本物の git リポジトリを作って確かめる。
"""

from __future__ import annotations

import json
import subprocess
import sys
import zipfile
from pathlib import Path

import pytest

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / "tools"))

import deploy_pages as dp  # noqa: E402

TEMPLATE = ROOT / "unity" / "KatsushikaCampusDays" / "Assets" / "WebGLTemplates" / "KCD" / "index.html"
SHA = "0123456789abcdef0123456789abcdef01234567"
HASHES = {
    "loader": "3f1c5b0d2a9e8f7c6b5a4d3e2f1a0b9c.loader.js",
    "data": "a1b2c3d4e5f60718293a4b5c6d7e8f90.data.unityweb",
    "framework": "0f1e2d3c4b5a69788796a5b4c3d2e1f0.framework.js.unityweb",
    "code": "9e8d7c6b5a4f3e2d1c0b9a8f7e6d5c4b.wasm.unityweb",
}
# テクスチャを ASTC で圧縮したビルドの data。ほかのファイルは DXT のビルドと同じ名前になる (#187)。
ASTC_DATA = "5a5a5a5a5a5a5a5a5a5a5a5a5a5a5a5a.data.unityweb"


# 段階読み込み（PROGRESSIVE_ASSET_LOADING）のときに data の配列へ入るファイル。
# ローダーは配列の URL を buildUrl を付けずにそのまま取りに行くので, index.html からの相対パスになる。
PRIMARY = ["Build/Data/level0.data.unityweb", "Build/Data/globalgamemanagers.data.unityweb"]
SECONDARY = ["Build/Data/level1.data.unityweb"]


def built_index(marker: bool = True, data: str = "url", astc: str | None = "slot") -> str:
    """Unity がテンプレートを展開した後の index.html に近い形。

    data は data の参照の形: "url" は dataUrl, "arrays" は primaryDataUrls / secondaryDataUrls,
    "none" はどちらも無い（テンプレートが崩れた場合）。
    astc は ASTC の data の参照: "slot" は名前を書き込む前の差し込み口, None は行が無い,
    それ以外は書き込んだ data の名前。
    """
    head_marker = f"    {dp.STAMP_MARKER}\n" if marker else ""
    data_lines = {
        "url": f"        dataUrl: buildUrl + \"/{HASHES['data']}\",\n",
        "arrays": (f"        primaryDataUrls: {json.dumps(PRIMARY)},\n"
                   f"        secondaryDataUrls: {json.dumps(SECONDARY)},\n"),
        "none": "",
    }[data]
    if astc is None:
        astc_line = ""
    elif astc == "slot":
        astc_line = f"      {dp.ASTC_SLOT}\n"
    else:
        astc_line = f"      var astcDataUrl = buildUrl + \"/{astc}\";\n"
    return (
        "<!DOCTYPE html>\n<html lang=\"ja\">\n  <head>\n    <meta charset=\"utf-8\">\n"
        f"{head_marker}"
        "  </head>\n  <body>\n    <script>\n"
        "      var buildUrl = \"Build\";\n"
        f"      var loaderUrl = buildUrl + \"/{HASHES['loader']}\";\n"
        f"{astc_line}"
        "      var config = {\n"
        "        arguments: [],\n"
        f"{data_lines}"
        f"        frameworkUrl: buildUrl + \"/{HASHES['framework']}\",\n"
        f"        codeUrl: buildUrl + \"/{HASHES['code']}\",\n"
        "        streamingAssetsUrl: \"StreamingAssets\",\n"
        "      };\n    </script>\n  </body>\n</html>\n"
    )


def make_build(tmp_path: Path, html: str | None = None, stale: bool = True) -> Path:
    build = tmp_path / "WebGL"
    (build / "Build").mkdir(parents=True)
    (build / "TemplateData").mkdir()
    (build / "index.html").write_text(html if html is not None else built_index(), encoding="utf-8")
    (build / "TemplateData" / "style.css").write_text("body{}", encoding="utf-8")
    for name in HASHES.values():
        (build / "Build" / name).write_bytes(b"x" * 16)
    for rel in PRIMARY + SECONDARY:
        (build / rel).parent.mkdir(parents=True, exist_ok=True)
        (build / rel).write_bytes(b"d" * 16)
    if stale:
        # 前のビルドが残したハッシュ名のファイル
        (build / "Build" / "ffffffffffffffffffffffffffffffff.data.unityweb").write_bytes(b"old")
        (build / "Build" / "Data" / "level9.data.unityweb").write_bytes(b"old")
    return build


def make_astc_build(tmp_path: Path, html: str | None = None) -> Path:
    """BuildPlayer.BuildWebGL が WebGL-ASTC に出すビルド。data の名前だけが DXT のビルドと違う。"""
    build = tmp_path / ("WebGL" + dp.ASTC_DIR_SUFFIX)
    (build / "Build").mkdir(parents=True)
    default = built_index().replace(HASHES["data"], ASTC_DATA)
    (build / "index.html").write_text(html if html is not None else default, encoding="utf-8")
    for name in [HASHES["loader"], HASHES["framework"], HASHES["code"]]:
        (build / "Build" / name).write_bytes(b"x" * 16)
    (build / "Build" / ASTC_DATA).write_bytes(b"a" * 24)
    return build


# --- テンプレート ---------------------------------------------------------

def test_template_has_stamp_marker_once():
    html = TEMPLATE.read_text(encoding="utf-8")
    assert html.count(dp.STAMP_MARKER) == 1
    assert html.index(dp.STAMP_MARKER) < html.index("</head>")


def test_template_references_build_files_through_placeholders():
    # テンプレートの参照の形が変わると zip の絞り込みが何も拾わなくなる。
    # Unity が置き換えるファイル名の差し込み口にハッシュ名を入れて読めることを見る。
    html = TEMPLATE.read_text(encoding="utf-8")
    for placeholder, key in (("LOADER_FILENAME", "loader"), ("DATA_FILENAME", "data"),
                             ("FRAMEWORK_FILENAME", "framework"), ("CODE_FILENAME", "code")):
        assert "{{{ " + placeholder + " }}}" in html
        html = html.replace("{{{ " + placeholder + " }}}", HASHES[key])
    assert set(HASHES.values()) <= set(dp.referenced_build_files(html))
    assert dp.missing_loader_parts(html) == []


def test_template_has_astc_slot_once():
    assert TEMPLATE.read_text(encoding="utf-8").count(dp.ASTC_SLOT) == 1


def test_template_progressive_data_arrays_are_read():
    # 段階読み込みを有効にすると, data は buildUrl + "/..." ではなく JSON の配列で入る。
    # その差し込み口に値を入れたとき, 配列の中のファイルも参照として読めることを見る。
    html = TEMPLATE.read_text(encoding="utf-8")
    for placeholder, urls in (("PRIMARY_DATA_FILES", PRIMARY), ("SECONDARY_DATA_FILES", SECONDARY)):
        slot = "{{{ JSON.stringify(" + placeholder + ") }}}"
        assert slot in html
        html = html.replace(slot, json.dumps(urls))
    assert set(PRIMARY + SECONDARY) <= set(dp.referenced_paths(html))


def test_project_settings_name_files_as_hashes():
    settings = ROOT / "unity" / "KatsushikaCampusDays" / "ProjectSettings" / "ProjectSettings.asset"
    assert "  webGLNameFilesAsHashes: 1\n" in settings.read_text(encoding="utf-8")


# --- 参照の読み取り -------------------------------------------------------

def test_referenced_build_files_reads_hashed_names():
    assert dp.referenced_build_files(built_index()) == list(HASHES.values())


def test_referenced_build_files_reads_background_without_css_tail():
    html = "canvas.style.background = \"url('\" + buildUrl + \"/bg.png') center / cover\";"
    assert dp.referenced_build_files(html) == ["bg.png"]


def test_referenced_build_files_ignores_other_urls():
    html = 'streamingAssetsUrl: "StreamingAssets", href="TemplateData/style.css"'
    assert dp.referenced_build_files(html) == []


def test_referenced_paths_read_progressive_data_arrays():
    paths = dp.referenced_paths(built_index(data="arrays"))
    assert paths == [f"Build/{HASHES[k]}" for k in ("loader", "framework", "code")] + PRIMARY + SECONDARY


def test_referenced_paths_strip_leading_dot_slash():
    html = 'primaryDataUrls: ["./Build/Data/a.data"], secondaryDataUrls: [],'
    assert dp.referenced_paths(html) == ["Build/Data/a.data"]


def test_referenced_paths_reject_broken_array():
    with pytest.raises(ValueError):
        dp.referenced_paths('primaryDataUrls: ["Build/a.data",],')


@pytest.mark.parametrize("url", ["/Build/a.data", "https://example.invalid/a.data", "Build/../a.data", ""])
def test_referenced_paths_reject_urls_outside_site(url):
    with pytest.raises(ValueError):
        dp.referenced_paths(f"primaryDataUrls: {json.dumps([url])},")


def test_missing_loader_parts_none_for_both_data_forms():
    assert dp.missing_loader_parts(built_index(data="url")) == []
    assert dp.missing_loader_parts(built_index(data="arrays")) == []


def test_missing_loader_parts_reports_data_when_absent():
    assert dp.missing_loader_parts(built_index(data="none")) == ["data"]
    empty_arrays = built_index(data="none").replace(
        "arguments: [],", "arguments: [], primaryDataUrls: [], secondaryDataUrls: [],")
    assert dp.missing_loader_parts(empty_arrays) == ["data"]


def test_missing_loader_parts_reports_code_and_framework():
    html = built_index().replace("codeUrl:", "wasmUrl:").replace("frameworkUrl:", "jsUrl:")
    assert dp.missing_loader_parts(html) == ["frameworkUrl", "codeUrl"]


# --- ビルド元の記録 -------------------------------------------------------

def test_stamp_replaces_marker_and_reads_back():
    html = dp.stamp_html(built_index(), SHA, "clean", "2026-09-24T01:02:03Z")
    assert dp.STAMP_MARKER not in html
    stamp = dp.read_stamp(html)
    assert stamp == dp.BuildStamp(commit=SHA, tree="clean", built="2026-09-24T01:02:03Z")
    assert stamp.clean


def test_stamp_dirty_is_not_clean():
    stamp = dp.read_stamp(dp.stamp_html(built_index(), SHA, "dirty", "t"))
    assert stamp is not None and stamp.known and not stamp.clean


def test_restamp_replaces_previous_stamp():
    once = dp.stamp_html(built_index(), SHA, "dirty", "t1")
    twice = dp.stamp_html(once, "f" * 40, "clean", "t2")
    assert twice.count('name="kcd-build"') == 1
    assert dp.read_stamp(twice) == dp.BuildStamp(commit="f" * 40, tree="clean", built="t2")


def test_stamp_without_marker_goes_before_head_end():
    html = dp.stamp_html(built_index(marker=False), SHA, "clean", "t")
    assert html.index('name="kcd-build"') < html.index("</head>")
    assert dp.read_stamp(html).commit == SHA


def test_stamp_without_head_raises():
    with pytest.raises(ValueError):
        dp.stamp_html("<p>no head</p>", SHA, "clean", "t")


@pytest.mark.parametrize("newline", ["\n", "\r\n"])
def test_stamp_file_keeps_line_endings(tmp_path, newline):
    index = tmp_path / "index.html"
    index.write_bytes(built_index().replace("\n", newline).encode("utf-8"))
    dp.stamp_file(index, dp.GitState(SHA), "t")
    data = index.read_bytes().decode("utf-8")
    assert dp.read_stamp(data).commit == SHA
    lines = data.split("\n")[:-1]
    assert all(line.endswith("\r") == (newline == "\r\n") for line in lines)


def test_unstamped_build_reads_none():
    assert dp.read_stamp(built_index()) is None


def test_deploy_refusal():
    assert dp.deploy_refusal(dp.BuildStamp(SHA, "clean", "t")) is None
    assert dp.deploy_refusal(dp.BuildStamp(SHA, "dirty", "t")) is not None
    assert dp.deploy_refusal(dp.BuildStamp("unknown", "unknown", "t")) is not None
    assert dp.deploy_refusal(None) is not None


def test_release_notes_link_commit():
    notes = dp.release_notes(dp.BuildStamp(SHA, "clean", "2026-09-24T01:02:03Z"))
    assert f"{dp.REPO_URL}/commit/{SHA}" in notes
    assert "作業ツリーはきれい" in notes
    assert "未コミット" in dp.release_notes(dp.BuildStamp(SHA, "dirty", "t"))
    assert "記録されていない" in dp.release_notes(None)


# --- zip ------------------------------------------------------------------

def test_zip_keeps_only_referenced_build_files(tmp_path):
    build = make_build(tmp_path)
    out = tmp_path / "webgl.zip"
    assert dp.make_zip(build, out) == 0
    with zipfile.ZipFile(out) as zf:
        names = set(zf.namelist())
    expected = {"index.html", "TemplateData/style.css"} | {f"Build/{n}" for n in HASHES.values()}
    assert names == expected


def test_zip_keeps_progressive_data_files(tmp_path):
    build = make_build(tmp_path, html=built_index(data="arrays"))
    out = tmp_path / "webgl.zip"
    assert dp.make_zip(build, out) == 0
    with zipfile.ZipFile(out) as zf:
        names = set(zf.namelist())
    expected = ({"index.html", "TemplateData/style.css"} | set(PRIMARY) | set(SECONDARY)
                | {f"Build/{HASHES[k]}" for k in ("loader", "framework", "code")})
    assert names == expected


def test_zip_fails_when_progressive_data_file_missing(tmp_path):
    build = make_build(tmp_path, html=built_index(data="arrays"), stale=False)
    (build / SECONDARY[0]).unlink()
    assert dp.make_zip(build, tmp_path / "webgl.zip") == 1
    assert not (tmp_path / "webgl.zip").exists()


def test_zip_fails_when_index_does_not_reference_data(tmp_path):
    # data の参照を読めないまま Build/ を絞ると, data の無い zip が「成功」として配信される。
    build = make_build(tmp_path, html=built_index(data="none"))
    assert dp.make_zip(build, tmp_path / "webgl.zip") == 1
    assert not (tmp_path / "webgl.zip").exists()


def test_deploy_stops_when_index_does_not_reference_data(fake, tmp_path):
    make_build(tmp_path, html=dp.stamp_html(built_index(data="none"), SHA, "clean", "t"))
    assert dp.main(fake.argv) == 1
    assert fake.uploads == [] and fake.dispatched == []


def test_zip_fails_when_referenced_file_missing(tmp_path):
    build = make_build(tmp_path, stale=False)
    (build / "Build" / HASHES["code"]).unlink()
    assert dp.make_zip(build, tmp_path / "webgl.zip") == 1
    assert not (tmp_path / "webgl.zip").exists()


def test_zip_fails_when_index_has_no_build_refs(tmp_path):
    build = make_build(tmp_path, html="<html><head></head><body></body></html>")
    assert dp.make_zip(build, tmp_path / "webgl.zip") == 1


def test_zip_fails_without_index(tmp_path):
    assert dp.make_zip(tmp_path, tmp_path / "webgl.zip") == 1


# --- git の状態 -----------------------------------------------------------

def git(repo: Path, *args: str) -> str:
    hooks = repo.parent / "no-hooks"
    hooks.mkdir(exist_ok=True)
    return subprocess.run(
        ["git", "-C", str(repo), "-c", "user.name=kcd-test", "-c", "user.email=kcd-test@example.invalid",
         "-c", "commit.gpgsign=false", "-c", f"core.hooksPath={hooks}", *args],
        capture_output=True, text=True, check=True).stdout.strip()


@pytest.fixture
def repo(tmp_path):
    repo = tmp_path / "repo"
    (repo / "unity" / "KatsushikaCampusDays" / "Assets").mkdir(parents=True)
    (repo / "tools").mkdir()
    (repo / "unity" / "KatsushikaCampusDays" / "Assets" / "a.cs").write_text("class A {}\n", encoding="utf-8")
    (repo / "tools" / "t.py").write_text("x = 1\n", encoding="utf-8")
    git(repo, "init", "-q")
    git(repo, "add", "unity/KatsushikaCampusDays/Assets/a.cs", "tools/t.py")
    git(repo, "commit", "-q", "-m", "init")
    return repo


def test_git_state_clean(repo):
    state = dp.git_state(repo)
    assert state.commit == git(repo, "rev-parse", "HEAD")
    assert len(state.commit) == 40
    assert not state.dirty and state.tree == "clean"


def test_git_state_modified_file_is_dirty(repo):
    (repo / "unity" / "KatsushikaCampusDays" / "Assets" / "a.cs").write_text("class B {}\n", encoding="utf-8")
    state = dp.git_state(repo)
    assert state.dirty and state.tree == "dirty"
    assert state.dirty_paths == ("unity/KatsushikaCampusDays/Assets/a.cs",)


def test_git_state_untracked_file_is_dirty(repo):
    (repo / "unity" / "KatsushikaCampusDays" / "Assets" / "new").mkdir()
    (repo / "unity" / "KatsushikaCampusDays" / "Assets" / "new" / "b.cs").write_text("//\n", encoding="utf-8")
    assert dp.git_state(repo).dirty_paths == ("unity/KatsushikaCampusDays/Assets/new/b.cs",)


def test_git_state_ignores_changes_outside_unity_project(repo):
    (repo / "tools" / "t.py").write_text("x = 2\n", encoding="utf-8")
    (repo / "README.md").write_text("new\n", encoding="utf-8")
    assert not dp.git_state(repo).dirty


# --- main -----------------------------------------------------------------

class Calls:
    def __init__(self):
        self.unity = 0
        self.uploads = []
        self.dispatched = []
        self.page = []  # 遊び方のページとして zip に足すファイル（サイトの根からのパス, 中身）


@pytest.fixture
def fake(monkeypatch, tmp_path):
    calls = Calls()
    build = tmp_path / "WebGL"

    def fake_unity_build(unity, build_dir):
        calls.unity += 1
        make_build(build_dir.parent, stale=True)
        make_astc_build(build_dir.parent)

    monkeypatch.setattr(dp, "unity_build", fake_unity_build)
    monkeypatch.setattr(dp, "upload_release", lambda zip_path, tag, notes: calls.uploads.append(notes))
    monkeypatch.setattr(dp, "dispatch_workflow", lambda tag: calls.dispatched.append(tag))
    monkeypatch.setattr(dp, "utc_now", lambda: "2026-09-24T00:00:00Z")
    monkeypatch.setattr(dp.howto_page, "site_files", lambda: calls.page)
    calls.build = build
    calls.argv = ["deploy_pages.py", "--build-dir", str(build), "--zip", str(tmp_path / "dist" / "webgl.zip")]
    return calls


def test_build_stops_on_dirty_tree(monkeypatch, fake):
    monkeypatch.setattr(dp, "git_state", lambda: dp.GitState(SHA, ("unity/KatsushikaCampusDays/Assets/a.cs",)))
    assert dp.main(fake.argv + ["--build"]) == 1
    assert fake.unity == 0
    assert fake.uploads == []


def test_build_clean_stamps_and_deploys(monkeypatch, fake):
    monkeypatch.setattr(dp, "git_state", lambda: dp.GitState(SHA))
    assert dp.main(fake.argv + ["--build"]) == 0
    assert fake.unity == 1
    stamp = dp.read_stamp((fake.build / "index.html").read_text(encoding="utf-8"))
    assert stamp == dp.BuildStamp(SHA, "clean", "2026-09-24T00:00:00Z")
    assert len(fake.uploads) == 1 and SHA in fake.uploads[0]
    assert fake.dispatched == [dp.TAG]


def test_build_attaches_astc_data_to_zip(monkeypatch, fake, tmp_path):
    monkeypatch.setattr(dp, "git_state", lambda: dp.GitState(SHA))
    assert dp.main(fake.argv + ["--build", "--no-deploy"]) == 0
    html = (fake.build / "index.html").read_text(encoding="utf-8")
    assert dp.astc_data_paths(html) == [f"Build/{ASTC_DATA}"]
    with zipfile.ZipFile(tmp_path / "dist" / "webgl.zip") as zf:
        assert zf.read(f"Build/{ASTC_DATA}") == b"a" * 24


def test_build_stops_without_astc_build(monkeypatch, fake, tmp_path, capsys):
    monkeypatch.setattr(dp, "git_state", lambda: dp.GitState(SHA))
    monkeypatch.setattr(dp, "unity_build", lambda unity, build_dir: make_build(build_dir.parent))
    assert dp.main(fake.argv + ["--build"]) == 1
    assert fake.uploads == []
    assert "ASTC の data を組み込めない" in capsys.readouterr().err


def test_build_allow_dirty_stamps_dirty_and_deploys(monkeypatch, fake):
    monkeypatch.setattr(dp, "git_state", lambda: dp.GitState(SHA, ("unity/KatsushikaCampusDays/x",)))
    assert dp.main(fake.argv + ["--build", "--allow-dirty"]) == 0
    stamp = dp.read_stamp((fake.build / "index.html").read_text(encoding="utf-8"))
    assert stamp.tree == "dirty"
    assert "未コミット" in fake.uploads[0]


def test_deploy_refuses_unstamped_build(fake, tmp_path):
    make_build(tmp_path)
    assert dp.main(fake.argv) == 1
    assert fake.uploads == [] and fake.dispatched == []


def test_deploy_refuses_dirty_stamped_build(fake, tmp_path):
    make_build(tmp_path, html=dp.stamp_html(built_index(), SHA, "dirty", "t"))
    assert dp.main(fake.argv) == 1
    assert fake.uploads == []


def test_deploy_existing_clean_build(fake, tmp_path):
    make_build(tmp_path, html=dp.stamp_html(built_index(), SHA, "clean", "t"))
    assert dp.main(fake.argv) == 0
    assert fake.unity == 0 and len(fake.uploads) == 1


def test_no_deploy_makes_zip_even_without_stamp(fake, tmp_path):
    make_build(tmp_path)
    assert dp.main(fake.argv + ["--no-deploy"]) == 0
    assert (tmp_path / "dist" / "webgl.zip").is_file()
    assert fake.uploads == []


# --- ASTC の data (#187) --------------------------------------------------

def test_code_names_read_loader_framework_and_code():
    assert dp.code_names(built_index()) == {
        "loaderUrl": [HASHES["loader"]],
        "frameworkUrl": [HASHES["framework"]],
        "codeUrl": [HASHES["code"]],
    }


def test_astc_data_paths_empty_until_attached():
    assert dp.astc_data_paths(built_index()) == []
    assert dp.astc_data_paths(built_index(astc=ASTC_DATA)) == [f"Build/{ASTC_DATA}"]


def test_astc_build_dir_is_sibling():
    assert dp.astc_build_dir(Path("build") / "WebGL") == Path("build") / "WebGL-ASTC"


def test_attach_copies_data_and_writes_name(tmp_path):
    build = make_build(tmp_path, stale=False)
    make_astc_build(tmp_path)
    assert dp.attach_astc_data(build, tmp_path / "WebGL-ASTC") == ASTC_DATA
    assert (build / "Build" / ASTC_DATA).read_bytes() == b"a" * 24
    html = (build / "index.html").read_text(encoding="utf-8")
    assert dp.ASTC_SLOT not in html
    assert dp.astc_data_paths(html) == [f"Build/{ASTC_DATA}"]
    assert dp.referenced_build_files(html).count(ASTC_DATA) == 1


@pytest.mark.parametrize("newline", ["\n", "\r\n"])
def test_attach_keeps_line_endings(tmp_path, newline):
    build = make_build(tmp_path, stale=False)
    (build / "index.html").write_bytes(built_index().replace("\n", newline).encode("utf-8"))
    make_astc_build(tmp_path)
    dp.attach_astc_data(build, tmp_path / "WebGL-ASTC")
    raw = (build / "index.html").read_bytes().decode("utf-8")
    assert raw.count("\r\n") == (raw.count("\n") if newline == "\r\n" else 0)


@pytest.mark.parametrize("part", ["loader", "framework", "code"])
def test_attach_rejects_different_code(tmp_path, part):
    build = make_build(tmp_path, stale=False)
    html = built_index().replace(HASHES["data"], ASTC_DATA).replace(HASHES[part], "0" * 32 + ".other")
    make_astc_build(tmp_path, html=html)
    with pytest.raises(ValueError, match="ローダー・framework・wasm が違う"):
        dp.attach_astc_data(build, tmp_path / "WebGL-ASTC")
    assert not (build / "Build" / ASTC_DATA).exists()


@pytest.mark.parametrize("astc", [None, ASTC_DATA])
def test_attach_rejects_index_without_slot(tmp_path, astc):
    build = make_build(tmp_path, html=built_index(astc=astc), stale=False)
    make_astc_build(tmp_path)
    with pytest.raises(ValueError, match="差し込み口"):
        dp.attach_astc_data(build, tmp_path / "WebGL-ASTC")


def test_attach_rejects_missing_astc_build(tmp_path):
    build = make_build(tmp_path, stale=False)
    with pytest.raises(FileNotFoundError):
        dp.attach_astc_data(build, tmp_path / "WebGL-ASTC")


def test_attach_rejects_missing_astc_data_file(tmp_path):
    build = make_build(tmp_path, stale=False)
    astc = make_astc_build(tmp_path)
    (astc / "Build" / ASTC_DATA).unlink()
    with pytest.raises(FileNotFoundError, match="ASTC のビルドの data が無い"):
        dp.attach_astc_data(build, astc)


def test_attach_rejects_astc_build_with_data_arrays(tmp_path):
    build = make_build(tmp_path, stale=False)
    make_astc_build(tmp_path, html=built_index(data="arrays"))
    with pytest.raises(ValueError, match="data の名前を 1 つに決められない"):
        dp.attach_astc_data(build, tmp_path / "WebGL-ASTC")


@pytest.mark.parametrize("name", [
    "../x.data.unityweb",
    "a/../../x.data.unityweb",
    "..\\..\\x.data.unityweb",
    "x</script><svg/onload=alert(1)>.data.unityweb",
    ".data.unityweb",
])
def test_attach_rejects_unsafe_data_name(tmp_path, name):
    build = make_build(tmp_path, stale=False)
    before = (build / "index.html").read_bytes()
    make_astc_build(tmp_path, html=built_index().replace(HASHES["data"], name))
    with pytest.raises(ValueError, match="使えない文字"):
        dp.attach_astc_data(build, tmp_path / "WebGL-ASTC")
    assert (build / "index.html").read_bytes() == before


# --- 配信サイズ (#70) -----------------------------------------------------

# 2026-09-24 に Release web-latest の webgl.zip を展開して測った値。
MEASURED_TOTAL = 66_468_279   # 配信する 6 ファイルの合計 = 63.39 MiB
MEASURED_DATA = 55_476_397    # そのうち data = 52.91 MiB
MEASURED_BUILD_DIR = 64_500_000  # #54 で build/WebGL をまるごと測った 64.5 MB


def clean_build(tmp_path: Path, data: str = "url") -> Path:
    return make_build(tmp_path, html=dp.stamp_html(built_index(data=data), SHA, "clean", "t"))


def test_measured_build_is_within_limits():
    size = dp.PayloadSize(total=MEASURED_TOTAL, data=MEASURED_DATA, files=6)
    assert dp.size_refusal(size) is None
    assert dp.size_refusal(dp.PayloadSize(total=MEASURED_BUILD_DIR, data=MEASURED_DATA, files=6)) is None


def test_limits_keep_headroom_but_stay_tight():
    # 実測より上に幅を持たせる。ただし 5 割を超えて緩めると, ふくらみに気付けない。
    assert 1.1 * MEASURED_TOTAL <= dp.MAX_TOTAL_BYTES <= 1.5 * MEASURED_TOTAL
    assert 1.1 * MEASURED_DATA <= dp.MAX_DATA_BYTES <= 1.5 * MEASURED_DATA
    # data は合計より先に止まる（data の幅の方が狭い）
    assert dp.MAX_DATA_BYTES / MEASURED_DATA < dp.MAX_TOTAL_BYTES / MEASURED_TOTAL


def test_size_at_limit_passes():
    size = dp.PayloadSize(total=dp.MAX_TOTAL_BYTES, data=dp.MAX_DATA_BYTES, files=6)
    assert dp.size_refusal(size) is None


def test_total_one_byte_over_limit_stops():
    reason = dp.size_refusal(dp.PayloadSize(total=dp.MAX_TOTAL_BYTES + 1, data=MEASURED_DATA, files=6))
    assert reason is not None
    assert "合計" in reason and "data " not in reason and "--allow-oversize" in reason


def test_data_one_byte_over_limit_stops():
    reason = dp.size_refusal(dp.PayloadSize(total=MEASURED_TOTAL, data=dp.MAX_DATA_BYTES + 1, files=6))
    assert reason is not None
    assert "data " in reason and "合計" not in reason


def test_size_refusal_reports_both_limits():
    size = dp.PayloadSize(total=dp.MAX_TOTAL_BYTES + 1, data=dp.MAX_DATA_BYTES + 1, files=6)
    reason = dp.size_refusal(size)
    assert "合計" in reason and "data " in reason


def test_size_refusal_takes_explicit_limits():
    size = dp.PayloadSize(total=100, data=40, files=3)
    assert dp.size_refusal(size, max_total=100, max_data=40) is None
    assert "data " in dp.size_refusal(size, max_total=100, max_data=39)
    assert "合計" in dp.size_refusal(size, max_total=99, max_data=40)


def test_data_paths_read_both_forms():
    assert dp.data_paths(built_index()) == [f"Build/{HASHES['data']}"]
    assert dp.data_paths(built_index(data="arrays")) == PRIMARY + SECONDARY
    assert dp.data_paths(built_index(data="none")) == []


def test_payload_size_counts_only_zipped_files(tmp_path):
    build = make_build(tmp_path)  # 前のビルドの残りも置く
    (build / "Build" / HASHES["data"]).write_bytes(b"x" * 1000)
    out = tmp_path / "webgl.zip"
    assert dp.make_zip(build, out) == 0
    others = (build / "index.html").stat().st_size + (build / "TemplateData" / "style.css").stat().st_size
    size = dp.payload_size(out)
    # zip に入らない前のビルドの残りは数えない
    total = others + 1000 + 16 * 3
    assert size == dp.PayloadSize(total=total, data=1000, files=6, stored=total)


@pytest.mark.parametrize("dxt, astc", [(1000, 600), (600, 1000)])
def test_payload_size_counts_one_data_per_device(tmp_path, dxt, astc):
    build = make_build(tmp_path, html=built_index(astc=ASTC_DATA))
    (build / "Build" / HASHES["data"]).write_bytes(b"x" * dxt)
    (build / "Build" / ASTC_DATA).write_bytes(b"a" * astc)
    out = tmp_path / "webgl.zip"
    assert dp.make_zip(build, out) == 0
    others = (build / "index.html").stat().st_size + (build / "TemplateData" / "style.css").stat().st_size
    size = dp.payload_size(out)
    # 端末は DXT か ASTC の一方しか読まないので, 大きい方だけを数える
    assert size == dp.PayloadSize(total=others + 16 * 3 + max(dxt, astc), data=max(dxt, astc), files=7,
                                  stored=others + 16 * 3 + dxt + astc, astc_data=astc)


def test_deploy_stops_when_astc_data_over_limit(monkeypatch, fake, tmp_path):
    build = make_build(tmp_path, html=dp.stamp_html(built_index(astc=ASTC_DATA), SHA, "clean", "t"))
    (build / "Build" / ASTC_DATA).write_bytes(b"a" * 17)
    monkeypatch.setattr(dp, "MAX_DATA_BYTES", 16)  # DXT の data は 16 バイトで収まる
    assert dp.main(fake.argv) == 1
    assert fake.uploads == []


def test_payload_size_counts_progressive_data_arrays(tmp_path):
    build = make_build(tmp_path, html=built_index(data="arrays"))
    out = tmp_path / "webgl.zip"
    assert dp.make_zip(build, out) == 0
    size = dp.payload_size(out)
    assert size.data == 16 * len(PRIMARY + SECONDARY)
    assert size.files == 2 + 3 + len(PRIMARY + SECONDARY)


def test_deploy_stops_when_data_over_limit(monkeypatch, fake, tmp_path, capsys):
    clean_build(tmp_path)
    monkeypatch.setattr(dp, "MAX_DATA_BYTES", 15)  # テストの data は 16 バイト
    assert dp.main(fake.argv) == 1
    assert fake.uploads == [] and fake.dispatched == []
    assert "data " in capsys.readouterr().err
    # 何が大きいか調べられるよう, zip は残す
    assert (tmp_path / "dist" / "webgl.zip").is_file()


def test_deploy_stops_when_total_over_limit(monkeypatch, fake, tmp_path, capsys):
    clean_build(tmp_path)
    monkeypatch.setattr(dp, "MAX_TOTAL_BYTES", 100)
    assert dp.main(fake.argv) == 1
    assert fake.uploads == [] and fake.dispatched == []
    assert "合計" in capsys.readouterr().err


def test_deploy_stops_when_progressive_data_over_limit(monkeypatch, fake, tmp_path):
    clean_build(tmp_path, data="arrays")
    monkeypatch.setattr(dp, "MAX_DATA_BYTES", 16 * len(PRIMARY + SECONDARY) - 1)
    assert dp.main(fake.argv) == 1
    assert fake.uploads == []


def test_deploy_passes_at_exact_limit(monkeypatch, fake, tmp_path):
    clean_build(tmp_path)
    monkeypatch.setattr(dp, "MAX_DATA_BYTES", 16)
    assert dp.main(fake.argv) == 0
    assert len(fake.uploads) == 1 and fake.dispatched == [dp.TAG]


def test_allow_oversize_deploys_with_warning(monkeypatch, fake, tmp_path, capsys):
    clean_build(tmp_path)
    monkeypatch.setattr(dp, "MAX_DATA_BYTES", 15)
    assert dp.main(fake.argv + ["--allow-oversize"]) == 0
    assert len(fake.uploads) == 1
    assert "--allow-oversize なので配信する" in capsys.readouterr().err


def test_allow_dirty_does_not_let_oversize_through(monkeypatch, fake, tmp_path):
    make_build(tmp_path, html=dp.stamp_html(built_index(), SHA, "dirty", "t"))
    monkeypatch.setattr(dp, "MAX_DATA_BYTES", 15)
    assert dp.main(fake.argv + ["--allow-dirty"]) == 1
    assert fake.uploads == []


def test_allow_oversize_does_not_let_dirty_through(monkeypatch, fake, tmp_path):
    make_build(tmp_path, html=dp.stamp_html(built_index(), SHA, "dirty", "t"))
    monkeypatch.setattr(dp, "MAX_DATA_BYTES", 15)
    assert dp.main(fake.argv + ["--allow-oversize"]) == 1
    assert fake.uploads == []


def test_deploy_reports_every_refusal(monkeypatch, fake, tmp_path, capsys):
    make_build(tmp_path)  # ビルド元の記録なし
    monkeypatch.setattr(dp, "MAX_DATA_BYTES", 15)
    assert dp.main(fake.argv) == 1
    err = capsys.readouterr().err
    assert "ビルド元のコミットの記録が無い" in err and "data " in err


def test_no_deploy_warns_about_oversize_and_keeps_zip(monkeypatch, fake, tmp_path, capsys):
    clean_build(tmp_path)
    monkeypatch.setattr(dp, "MAX_DATA_BYTES", 15)
    assert dp.main(fake.argv + ["--no-deploy"]) == 0
    assert (tmp_path / "dist" / "webgl.zip").is_file()
    assert fake.uploads == []
    assert "配信するなら止まる" in capsys.readouterr().err


# --- 遊び方のページ (#18) -------------------------------------------------

# zip に足すファイルの例。サイトの根からのパスと中身。
PAGE = [
    ("howto/index.html", b"<!DOCTYPE html>\n<title>t</title>\n"),
    ("howto/img/a.png", b"\x89PNG-a"),
    ("howto/img/b.jpg", b"\xff\xd8-b"),
]
PAGE_BYTES = sum(len(data) for _, data in PAGE)
BUILD_FILES = 2 + len(HASHES)  # index.html, TemplateData/style.css と Build/ の 4 つ


def test_zip_adds_extra_files_without_compression(tmp_path):
    build = make_build(tmp_path)
    out = tmp_path / "webgl.zip"
    assert dp.make_zip(build, out, extras=PAGE) == 0
    with zipfile.ZipFile(out) as zf:
        names = zf.namelist()
        assert all(info.compress_type == zipfile.ZIP_STORED for info in zf.infolist())
        assert zf.testzip() is None
        for name, data in PAGE:
            assert zf.read(name) == data
    expected = {"index.html", "TemplateData/style.css"} | {f"Build/{n}" for n in HASHES.values()}
    assert set(names) == expected | {name for name, _ in PAGE}
    assert len(names) == len(set(names))


def test_zip_extra_files_are_world_readable_regular_files(tmp_path):
    # pages.yml の upload-pages-artifact は, 誰でも読めないファイルがあると警告して権限を直す。
    build = make_build(tmp_path)
    out = tmp_path / "webgl.zip"
    assert dp.make_zip(build, out, extras=PAGE) == 0
    with zipfile.ZipFile(out) as zf:
        for name, _ in PAGE:
            info = zf.getinfo(name)
            assert info.create_system == 3
            assert info.external_attr >> 16 == 0o100644, name


def test_zip_extra_files_unpack_into_directories(tmp_path):
    # pages.yml は unzip -q webgl.zip -d site で展開する。howto/index.html と howto/img/ ができる。
    build = make_build(tmp_path)
    out = tmp_path / "webgl.zip"
    assert dp.make_zip(build, out, extras=PAGE) == 0
    site = tmp_path / "site"
    with zipfile.ZipFile(out) as zf:
        zf.extractall(site)
    assert (site / "index.html").is_file()
    for name, data in PAGE:
        assert (site / name).read_bytes() == data


def test_zip_without_extras_is_unchanged(tmp_path, capsys):
    build = make_build(tmp_path)
    out = tmp_path / "webgl.zip"
    assert dp.make_zip(build, out) == 0
    with zipfile.ZipFile(out) as zf:
        assert len(zf.namelist()) == BUILD_FILES
    assert f" : {BUILD_FILES} files," in capsys.readouterr().out


def test_zip_counts_extra_files_in_its_report(tmp_path, capsys):
    build = make_build(tmp_path)
    assert dp.make_zip(build, tmp_path / "webgl.zip", extras=PAGE) == 0
    assert f" : {BUILD_FILES + len(PAGE)} files," in capsys.readouterr().out


@pytest.mark.parametrize("extras, message", [
    ([(f"Build/{HASHES['code']}", b"x")], "同じ名前"),                # ビルドのファイルを上書きしない
    ([("index.html", b"x")], "同じ名前"),
    ([("howto/a.html", b"1"), ("howto/a.html", b"2")], "同じ名前"),   # 足すファイルどうし
    ([("../x.html", b"x")], "根の外"),
    ([("howto/../../x.html", b"x")], "根の外"),
    ([("/x.html", b"x")], "根の外"),
    ([("howto\\x.html", b"x")], "根の外"),
])
def test_zip_rejects_extras_that_clash_or_leave_the_site(tmp_path, capsys, extras, message):
    build = make_build(tmp_path)
    out = tmp_path / "webgl.zip"
    assert dp.make_zip(build, out, extras=extras) == 1
    assert not out.exists()  # 半端な zip を残さない
    assert message in capsys.readouterr().err


def test_extras_problem_accepts_the_page_and_names_every_offender():
    assert dp.extras_problem(["index.html"], PAGE) is None
    assert dp.extras_problem([], []) is None
    reason = dp.extras_problem([], [("../a", b""), ("/b", b""), ("c", b"")])
    assert "../a" in reason and "/b" in reason and "c" not in reason.replace("根の外", "")
    reason = dp.extras_problem(["a", "b"], [("a", b""), ("c", b""), ("c", b""), ("d", b"")])
    assert "a" in reason and "c" in reason and "d" not in reason


def test_payload_size_does_not_count_the_howto_page(tmp_path):
    build = make_build(tmp_path)
    (build / "Build" / HASHES["data"]).write_bytes(b"x" * 1000)
    plain, with_page = tmp_path / "plain.zip", tmp_path / "with-page.zip"
    assert dp.make_zip(build, plain) == 0
    assert dp.make_zip(build, with_page, extras=PAGE) == 0
    base, size = dp.payload_size(plain), dp.payload_size(with_page)
    assert base.page == 0 and size.page == PAGE_BYTES
    # 1 台がゲームを読む量は変わらない。zip の中身の合計とファイル数は増える。
    assert (size.total, size.data) == (base.total, base.data)
    assert size.stored == base.stored + PAGE_BYTES
    assert size.files == base.files + len(PAGE)


@pytest.mark.parametrize("dxt, astc", [(1000, 600), (600, 1000)])
def test_payload_size_page_and_astc_data_are_counted_apart(tmp_path, dxt, astc):
    build = make_build(tmp_path, html=built_index(astc=ASTC_DATA))
    (build / "Build" / HASHES["data"]).write_bytes(b"x" * dxt)
    (build / "Build" / ASTC_DATA).write_bytes(b"a" * astc)
    plain, with_page = tmp_path / "plain.zip", tmp_path / "with-page.zip"
    assert dp.make_zip(build, plain) == 0
    assert dp.make_zip(build, with_page, extras=PAGE) == 0
    base, size = dp.payload_size(plain), dp.payload_size(with_page)
    assert (size.total, size.data, size.astc_data) == (base.total, base.data, base.astc_data)
    assert size.page == PAGE_BYTES and size.stored == base.stored + PAGE_BYTES


def test_main_puts_the_page_in_the_zip_and_reports_its_size(fake, tmp_path, capsys):
    make_build(tmp_path)
    fake.page.extend(PAGE)
    assert dp.main(fake.argv + ["--no-deploy"]) == 0
    with zipfile.ZipFile(tmp_path / "dist" / "webgl.zip") as zf:
        assert zf.read("howto/index.html") == PAGE[0][1]
        assert zf.read("howto/img/a.png") == PAGE[1][1]
    out = capsys.readouterr().out
    assert "遊び方のページ（howto/）" in out and "合計と data には数えない" in out


def test_main_without_a_page_prints_no_page_line(fake, tmp_path, capsys):
    make_build(tmp_path)
    assert dp.main(fake.argv + ["--no-deploy"]) == 0
    assert "遊び方のページ" not in capsys.readouterr().out


def test_main_deploys_the_zip_with_the_page(fake, tmp_path):
    clean_build(tmp_path)
    fake.page.extend(PAGE)
    assert dp.main(fake.argv) == 0
    assert len(fake.uploads) == 1 and fake.dispatched == [dp.TAG]
    with zipfile.ZipFile(tmp_path / "dist" / "webgl.zip") as zf:
        assert "howto/index.html" in zf.namelist()


def test_howto_page_does_not_count_toward_the_size_limits(monkeypatch, fake, tmp_path):
    clean_build(tmp_path)
    assert dp.main(fake.argv + ["--no-deploy"]) == 0
    size = dp.payload_size(tmp_path / "dist" / "webgl.zip")
    # 上限をゲームの大きさちょうどにしても, ページを足して配信できる
    monkeypatch.setattr(dp, "MAX_TOTAL_BYTES", size.total)
    monkeypatch.setattr(dp, "MAX_DATA_BYTES", size.data)
    fake.page.extend(PAGE)
    assert dp.main(fake.argv) == 0
    assert len(fake.uploads) == 1
    assert dp.payload_size(tmp_path / "dist" / "webgl.zip").total == size.total


@pytest.mark.parametrize("limit", ["MAX_TOTAL_BYTES", "MAX_DATA_BYTES"])
def test_oversize_game_still_stops_with_a_page(monkeypatch, fake, tmp_path, limit):
    clean_build(tmp_path)
    fake.page.extend(PAGE)
    monkeypatch.setattr(dp, limit, 15)
    assert dp.main(fake.argv) == 1
    assert fake.uploads == [] and fake.dispatched == []


@pytest.mark.parametrize("error", [ValueError("13 行目: 画像は使えない"), FileNotFoundError("talk.jpg")])
def test_main_stops_before_building_when_the_page_cannot_be_made(monkeypatch, fake, tmp_path, capsys, error):
    def broken():
        raise error

    monkeypatch.setattr(dp.howto_page, "site_files", broken)
    monkeypatch.setattr(dp, "git_state", lambda: dp.GitState(SHA))
    assert dp.main(fake.argv + ["--build"]) == 1
    assert fake.unity == 0  # 時間のかかる Unity のビルドを始めない
    assert fake.uploads == [] and fake.dispatched == []
    assert not (tmp_path / "dist" / "webgl.zip").exists()
    err = capsys.readouterr().err
    assert "遊び方のページを作れない" in err and str(error) in err


def test_real_howto_page_goes_into_the_zip(tmp_path):
    build = make_build(tmp_path)
    out = tmp_path / "webgl.zip"
    page = dp.howto_page.site_files()  # 本物の docs/HOW_TO_PLAY.md と docs/screenshots
    assert dp.make_zip(build, out, extras=page) == 0
    with zipfile.ZipFile(out) as zf:
        names = set(zf.namelist())
        html = zf.read("howto/index.html").decode("utf-8")
    assert {name for name, _ in page} <= names
    assert html.startswith("<!DOCTYPE html>")
    assert dp.payload_size(out).page == sum(len(data) for _, data in page)
