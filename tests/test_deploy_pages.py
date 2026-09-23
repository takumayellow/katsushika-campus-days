"""tools/deploy_pages.py のテスト (#75)。python -m pytest tests で回す。

Unity と gh は呼ばない。unity_build / upload_release / dispatch_workflow / git_state は差し替える。
git_state だけは一時ディレクトリに本物の git リポジトリを作って確かめる。
"""

from __future__ import annotations

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


def built_index(marker: bool = True) -> str:
    """Unity がテンプレートを展開した後の index.html に近い形。"""
    head_marker = f"    {dp.STAMP_MARKER}\n" if marker else ""
    return (
        "<!DOCTYPE html>\n<html lang=\"ja\">\n  <head>\n    <meta charset=\"utf-8\">\n"
        f"{head_marker}"
        "  </head>\n  <body>\n    <script>\n"
        "      var buildUrl = \"Build\";\n"
        f"      var loaderUrl = buildUrl + \"/{HASHES['loader']}\";\n"
        "      var config = {\n"
        f"        dataUrl: buildUrl + \"/{HASHES['data']}\",\n"
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
    if stale:
        # 前のビルドが残したハッシュ名のファイル
        (build / "Build" / "ffffffffffffffffffffffffffffffff.data.unityweb").write_bytes(b"old")
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


@pytest.fixture
def fake(monkeypatch, tmp_path):
    calls = Calls()
    build = tmp_path / "WebGL"

    def fake_unity_build(unity, build_dir):
        calls.unity += 1
        make_build(build_dir.parent, stale=True)

    monkeypatch.setattr(dp, "unity_build", fake_unity_build)
    monkeypatch.setattr(dp, "upload_release", lambda zip_path, tag, notes: calls.uploads.append(notes))
    monkeypatch.setattr(dp, "dispatch_workflow", lambda tag: calls.dispatched.append(tag))
    monkeypatch.setattr(dp, "utc_now", lambda: "2026-09-24T00:00:00Z")
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
