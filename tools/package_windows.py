"""Windows ビルドを配布用 zip にまとめる (#18)。

使い方:
    python tools/package_windows.py [--build build/Windows] [--out-dir dist] [--sha <commit>]

zip の名前は KatsushikaCampusDays_v<版>_<commit の先頭 7 桁>_win64.zip。
版は ProjectSettings.asset の bundleVersion、commit は git rev-parse HEAD（--sha で指定もできる）。
展開すると KatsushikaCampusDays/ の下に次が並ぶ:

    README.txt   遊び方（docs/HOW_TO_PLAY.md の先頭に版と commit を足したもの）
    CREDITS.txt  素材の出典と権利（docs/CREDITS.md）
    KatsushikaCampusDays.exe ほかビルド一式

README.txt と CREDITS.txt は Windows のメモ帳でそのまま読めるよう、BOM 付き UTF-8・改行 CRLF にする。
遊び方に「操作」「動作環境」「既知の問題」「クレジット」の節が揃っていなければ zip を作らない。
Unity が「同梱するな」と言っているフォルダは package_zip と同じ規則で除く。
"""

from __future__ import annotations

import argparse
import os
import re
import subprocess
import sys
import zipfile
from pathlib import Path

import package_zip

ROOT = Path(__file__).resolve().parents[1]
PROJECT_SETTINGS = ROOT / "unity" / "KatsushikaCampusDays" / "ProjectSettings" / "ProjectSettings.asset"
HOW_TO_PLAY = ROOT / "docs" / "HOW_TO_PLAY.md"
CREDITS = ROOT / "docs" / "CREDITS.md"

PRODUCT = "KatsushikaCampusDays"
EXE_NAME = f"{PRODUCT}.exe"
README_NAME = "README.txt"
CREDITS_NAME = "CREDITS.txt"
REPO_URL = "https://github.com/takumayellow/katsushika-campus-days"

# 遊び方の README に要る節。見出し（## ...）にこの語が入っていればよい。
REQUIRED_SECTIONS = ("操作", "動作環境", "既知の問題", "クレジット")

_VERSION_LINE = re.compile(r"^\s*bundleVersion:\s*(\S+)\s*$", re.MULTILINE)
_VERSION_TEXT = re.compile(r"^[0-9A-Za-z][0-9A-Za-z._-]*$")
_SHA_TEXT = re.compile(r"^[0-9a-f]{7,40}$")


def read_version(settings: Path) -> str:
    """ProjectSettings.asset の bundleVersion。ファイル名に入れるので英数字と . _ - だけを許す。"""
    match = _VERSION_LINE.search(settings.read_text(encoding="utf-8"))
    if match is None:
        raise ValueError(f"{settings} に bundleVersion が無い")
    version = match.group(1)
    if not _VERSION_TEXT.match(version):
        raise ValueError(f"bundleVersion がファイル名に使えない: {version!r}")
    return version


def normalize_sha(sha: str) -> str:
    """16 進 7〜40 桁の commit を小文字にそろえる。それ以外は ValueError。"""
    value = sha.strip().lower()
    if not _SHA_TEXT.match(value):
        raise ValueError(f"commit の SHA として読めない: {sha!r}")
    return value


def head_commit(root: Path = ROOT) -> str:
    """root の git の HEAD（40 桁）。git が無い・リポジトリでないときは ValueError。"""
    try:
        result = subprocess.run(
            ["git", "rev-parse", "HEAD"], cwd=root, capture_output=True, text=True, check=True)
    except (OSError, subprocess.CalledProcessError) as error:
        raise ValueError(f"git の HEAD を読めない（--sha で指定する）: {error}") from error
    return normalize_sha(result.stdout)


def zip_name(version: str, sha: str) -> str:
    return f"{PRODUCT}_v{version}_{normalize_sha(sha)[:7]}_win64.zip"


def missing_sections(markdown: str) -> list[str]:
    """REQUIRED_SECTIONS のうち、## 見出しに出てこないもの。"""
    headings = [line[3:] for line in markdown.splitlines() if line.startswith("## ")]
    return [name for name in REQUIRED_SECTIONS if not any(name in heading for heading in headings)]


def readme_text(how_to_play: str, version: str, sha: str) -> str:
    """zip に入れる README の本文。どのビルドの zip かを先頭に書く。"""
    commit = normalize_sha(sha)
    header = (
        f"葛飾キャンパスデイズ (Katsushika Campus Days) v{version}\n"
        f"commit {commit}\n"
        f"{REPO_URL}/tree/{commit}\n"
        "\n"
    )
    return header + how_to_play


def windows_text(text: str) -> bytes:
    """メモ帳で文字化けせず改行が揃うよう、BOM 付き UTF-8・CRLF にする。"""
    lines = text.replace("\r\n", "\n").replace("\r", "\n")
    return lines.replace("\n", "\r\n").encode("utf-8-sig")


def build_package(build_dir: Path, out: Path, how_to_play: Path, credits: Path, version: str, sha: str) -> int:
    """zip を作る。作れたら 0、作れなければ理由を stderr に出して 1（zip は残さない）。"""
    exe = build_dir / EXE_NAME
    if not exe.is_file():
        print(f"ビルドが見つからない: {exe}", file=sys.stderr)
        return 1

    for doc in (how_to_play, credits):
        if not doc.is_file():
            print(f"同梱する文書が無い: {doc}", file=sys.stderr)
            return 1

    guide = how_to_play.read_text(encoding="utf-8")
    missing = missing_sections(guide)
    if missing:
        print(f"{how_to_play} に節が足りない: " + ", ".join(missing), file=sys.stderr)
        return 1

    files = package_zip.collect_files(build_dir)
    reserved = {README_NAME.lower(), CREDITS_NAME.lower()}
    clashes = [p for p in files if p.relative_to(build_dir).as_posix().lower() in reserved]
    if clashes:
        print("ビルドに同梱文書と同じ名前のファイルがある: " + ", ".join(p.name for p in clashes), file=sys.stderr)
        return 1

    out.parent.mkdir(parents=True, exist_ok=True)
    partial = out.with_name(out.name + ".part")
    try:
        with zipfile.ZipFile(partial, "w", compression=zipfile.ZIP_DEFLATED, compresslevel=6) as zf:
            zf.writestr(f"{PRODUCT}/{README_NAME}", windows_text(readme_text(guide, version, sha)))
            zf.writestr(f"{PRODUCT}/{CREDITS_NAME}", windows_text(credits.read_text(encoding="utf-8")))
            for path in files:
                zf.write(path, f"{PRODUCT}/{path.relative_to(build_dir).as_posix()}")
        os.replace(partial, out)
    finally:
        if partial.exists():
            partial.unlink()

    size_mib = out.stat().st_size / (1024 * 1024)
    print(f"{out} : {len(files)} files + {README_NAME}, {CREDITS_NAME}, {size_mib:.1f} MiB")
    return 0


def main(argv: list[str]) -> int:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--build", default=str(ROOT / "build" / "Windows"), help="Unity の Windows ビルドのフォルダ")
    parser.add_argument("--out-dir", default=str(ROOT / "dist"), help="zip を置くフォルダ")
    parser.add_argument("--sha", help="zip の名前と README に書く commit（既定は git rev-parse HEAD）")
    parser.add_argument("--settings", default=str(PROJECT_SETTINGS), help="版を読む ProjectSettings.asset")
    parser.add_argument("--readme", default=str(HOW_TO_PLAY), help="README.txt にする遊び方")
    parser.add_argument("--credits", default=str(CREDITS), help="CREDITS.txt にするクレジット")
    args = parser.parse_args(argv[1:])

    try:
        version = read_version(Path(args.settings))
        sha = normalize_sha(args.sha) if args.sha else head_commit(ROOT)
    except (OSError, ValueError) as error:
        print(error, file=sys.stderr)
        return 1

    out = Path(args.out_dir) / zip_name(version, sha)
    return build_package(Path(args.build), out, Path(args.readme), Path(args.credits), version, sha)


if __name__ == "__main__":
    raise SystemExit(main(sys.argv))
