"""tools/package_windows.py のテスト (#18)。python -m pytest tests で回す。

Unity は呼ばない。ビルドの代わりに一時ディレクトリへ exe と Data の形だけのファイルを置く。
"""

from __future__ import annotations

import shutil
import sys
import zipfile
from pathlib import Path

import pytest

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / "tools"))

import package_windows as pw  # noqa: E402

SHA = "0123456789abcdef0123456789abcdef01234567"
PREFIX = "KatsushikaCampusDays/"
GUIDE = (
    "# 遊び方\n\n## 起動\n\nexe を開く\n\n## 動作環境\n\nWindows 10\n\n## 操作\n\n| 移動 | WASD |\n\n"
    "## 既知の問題\n\nなし\n\n## クレジット\n\n地図データ © OpenStreetMap contributors (ODbL)\n"
)
CREDITS = "# クレジット\n\n© OpenStreetMap contributors\n"


def fake_build(root: Path) -> Path:
    files = {
        "KatsushikaCampusDays.exe": b"exe",
        "UnityPlayer.dll": b"dll",
        "KatsushikaCampusDays_Data/level0": b"scene",
        "KatsushikaCampusDays_Data/StreamingAssets/Licenses/NotoSansJP-OFL.txt": b"ofl",
        "KatsushikaCampusDays_BackUpThisFolder_ButDontShipItWithYourGame/il2cpp.cpp": b"x",
        "KatsushikaCampusDays_BurstDebugInformation_DoNotShip/lib.pdb": b"x",
    }
    for rel, data in files.items():
        path = root / rel
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_bytes(data)
    return root


@pytest.fixture
def docs(tmp_path: Path) -> tuple[Path, Path]:
    guide = tmp_path / "HOW_TO_PLAY.md"
    guide.write_text(GUIDE, encoding="utf-8")
    credits = tmp_path / "CREDITS.md"
    credits.write_text(CREDITS, encoding="utf-8")
    return guide, credits


def build(tmp_path: Path, docs: tuple[Path, Path], build_dir: Path | None = None) -> tuple[int, Path]:
    out = tmp_path / "dist" / pw.zip_name("1.0.0", SHA)
    rc = pw.build_package(build_dir or fake_build(tmp_path / "Windows"), out, docs[0], docs[1], "1.0.0", SHA)
    return rc, out


# ---- 名前と版 ----

def test_read_version(tmp_path):
    settings = tmp_path / "ProjectSettings.asset"
    settings.write_text("PlayerSettings:\n  productName: X\n  bundleVersion: 1.2.3\n  preloadedAssets: []\n",
                        encoding="utf-8")
    assert pw.read_version(settings) == "1.2.3"


@pytest.mark.parametrize("body", ["PlayerSettings:\n  productName: X\n", "  bundleVersion: 1.0/../x\n"])
def test_read_version_rejects_missing_or_unsafe(tmp_path, body):
    settings = tmp_path / "ProjectSettings.asset"
    settings.write_text(body, encoding="utf-8")
    with pytest.raises(ValueError):
        pw.read_version(settings)


def test_read_version_of_the_project():
    assert pw.read_version(pw.PROJECT_SETTINGS) == "1.0.0"


def test_normalize_sha():
    assert pw.normalize_sha(" ABCDEF1\n") == "abcdef1"
    assert pw.normalize_sha(SHA) == SHA
    for bad in ("", "abc123", "xyz1234", SHA + "0", "0123 456"):
        with pytest.raises(ValueError):
            pw.normalize_sha(bad)


def test_zip_name_has_version_and_short_sha():
    assert pw.zip_name("1.0.0", SHA) == "KatsushikaCampusDays_v1.0.0_0123456_win64.zip"
    assert pw.zip_name("1.0.0", SHA.upper()) == "KatsushikaCampusDays_v1.0.0_0123456_win64.zip"


@pytest.mark.skipif(shutil.which("git") is None, reason="git が無い")
def test_head_commit_reads_this_repository():
    sha = pw.head_commit(ROOT)
    assert len(sha) == 40 and pw.normalize_sha(sha) == sha


def test_head_commit_outside_a_repository(tmp_path, monkeypatch):
    monkeypatch.setenv("GIT_CEILING_DIRECTORIES", str(tmp_path.parent))
    with pytest.raises(ValueError):
        pw.head_commit(tmp_path)


# ---- 遊び方 ----

def test_missing_sections():
    assert pw.missing_sections(GUIDE) == []
    assert pw.missing_sections("# 遊び方\n\n## 操作\n\n操作の説明に動作環境と書いても見出しでなければ数えない\n") == [
        "動作環境", "既知の問題", "クレジット"]


def test_how_to_play_has_every_section_and_the_map_attribution():
    guide = pw.HOW_TO_PLAY.read_text(encoding="utf-8")
    assert pw.missing_sections(guide) == []
    assert "© OpenStreetMap contributors" in guide
    assert "https://www.openstreetmap.org/copyright" in guide
    assert "https://takumayellow.github.io/katsushika-campus-days/" in guide


def test_readme_text_names_the_build():
    text = pw.readme_text(GUIDE, "1.0.0", SHA.upper())
    first, second = text.splitlines()[:2]
    assert first.endswith("v1.0.0")
    assert second == f"commit {SHA}"
    assert f"/tree/{SHA}" in text
    assert text.endswith(GUIDE)


def test_windows_text_is_bom_utf8_with_crlf():
    data = pw.windows_text("a\nb\r\nc\rd")
    assert data.startswith(b"\xef\xbb\xbf")
    assert data[3:] == "a\r\nb\r\nc\r\nd".encode("utf-8")


# ---- zip ----

def test_build_package(tmp_path, docs, capsys):
    rc, out = build(tmp_path, docs)
    assert rc == 0
    assert out.name == "KatsushikaCampusDays_v1.0.0_0123456_win64.zip"
    assert not out.with_name(out.name + ".part").exists()

    with zipfile.ZipFile(out) as zf:
        names = zf.namelist()
        assert names[:2] == [PREFIX + "README.txt", PREFIX + "CREDITS.txt"]
        assert set(names[2:]) == {
            PREFIX + "KatsushikaCampusDays.exe",
            PREFIX + "UnityPlayer.dll",
            PREFIX + "KatsushikaCampusDays_Data/level0",
            PREFIX + "KatsushikaCampusDays_Data/StreamingAssets/Licenses/NotoSansJP-OFL.txt",
        }
        assert all(info.compress_type == zipfile.ZIP_DEFLATED for info in zf.infolist())

        readme = zf.read(PREFIX + "README.txt")
        assert readme.startswith(b"\xef\xbb\xbf")
        assert b"\n" not in readme.replace(b"\r\n", b"")
        text = readme.decode("utf-8-sig")
        assert f"commit {SHA}" in text
        assert "v1.0.0" in text
        assert "© OpenStreetMap contributors" in text

        credits = zf.read(PREFIX + "CREDITS.txt").decode("utf-8-sig")
        assert credits.replace("\r\n", "\n") == CREDITS

    assert "4 files" in capsys.readouterr().out


def test_build_package_without_exe(tmp_path, docs, capsys):
    build_dir = fake_build(tmp_path / "Windows")
    (build_dir / "KatsushikaCampusDays.exe").unlink()
    rc, out = build(tmp_path, docs, build_dir)
    assert rc == 1
    assert not out.exists()
    assert "ビルドが見つからない" in capsys.readouterr().err


def test_build_package_needs_every_section(tmp_path, docs, capsys):
    docs[0].write_text(GUIDE.replace("## 既知の問題", "## 困ったとき"), encoding="utf-8")
    rc, out = build(tmp_path, docs)
    assert rc == 1
    assert not out.exists()
    assert "既知の問題" in capsys.readouterr().err


def test_build_package_needs_credits(tmp_path, docs, capsys):
    docs[1].unlink()
    rc, out = build(tmp_path, docs)
    assert rc == 1
    assert not out.exists()
    assert "CREDITS.md" in capsys.readouterr().err


def test_build_package_refuses_a_clashing_readme(tmp_path, docs, capsys):
    build_dir = fake_build(tmp_path / "Windows")
    (build_dir / "readme.TXT").write_bytes(b"x")
    rc, out = build(tmp_path, docs, build_dir)
    assert rc == 1
    assert not out.exists()
    assert "readme.TXT" in capsys.readouterr().err


def test_main_names_the_zip(tmp_path, docs):
    build_dir = fake_build(tmp_path / "Windows")
    out_dir = tmp_path / "dist"
    rc = pw.main(["package_windows.py", "--build", str(build_dir), "--out-dir", str(out_dir), "--sha", SHA[:12],
                  "--readme", str(docs[0]), "--credits", str(docs[1])])
    assert rc == 0
    assert [p.name for p in out_dir.iterdir()] == ["KatsushikaCampusDays_v1.0.0_0123456_win64.zip"]


def test_main_rejects_a_bad_sha(tmp_path, docs, capsys):
    rc = pw.main(["package_windows.py", "--build", str(fake_build(tmp_path / "Windows")),
                  "--out-dir", str(tmp_path / "dist"), "--sha", "not-a-sha"])
    assert rc == 1
    assert not (tmp_path / "dist").exists()
    assert "SHA" in capsys.readouterr().err
