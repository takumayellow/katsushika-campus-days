"""Windows ビルドを配布用 zip にまとめる。

使い方:
    python tools/package_zip.py [--build build/Windows] [--out dist/KatsushikaCampusDays_win64.zip]

`*_BackUpThisFolder_ButDontShipItWithYourGame` と `*_BurstDebugInformation_DoNotShip` は
Unity が「同梱するな」と言っているフォルダなので除外する。zip の先頭に遊び方の README を入れる。
"""

from __future__ import annotations

import argparse
import sys
import zipfile
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
EXCLUDED_SUFFIXES = (
    "_BackUpThisFolder_ButDontShipItWithYourGame",
    "_BurstDebugInformation_DoNotShip",
)


def is_excluded(path: Path, build_dir: Path) -> bool:
    relative = path.relative_to(build_dir)
    return any(part.endswith(EXCLUDED_SUFFIXES) for part in relative.parts)


def collect_files(build_dir: Path) -> list[Path]:
    files = [p for p in build_dir.rglob("*") if p.is_file() and not is_excluded(p, build_dir)]
    return sorted(files)


def build_zip(build_dir: Path, out: Path, readme: Path) -> int:
    exe = build_dir / "KatsushikaCampusDays.exe"
    if not exe.is_file():
        print(f"ビルドが見つからない: {exe}", file=sys.stderr)
        return 1

    files = collect_files(build_dir)
    out.parent.mkdir(parents=True, exist_ok=True)
    prefix = "KatsushikaCampusDays"
    with zipfile.ZipFile(out, "w", compression=zipfile.ZIP_DEFLATED, compresslevel=6) as zf:
        if readme.is_file():
            zf.write(readme, f"{prefix}/README.md")
        for path in files:
            zf.write(path, f"{prefix}/{path.relative_to(build_dir).as_posix()}")

    size_mib = out.stat().st_size / (1024 * 1024)
    print(f"{out} : {len(files)} files, {size_mib:.1f} MiB")
    return 0


def main(argv: list[str]) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--build", default=str(ROOT / "build" / "Windows"))
    parser.add_argument("--out", default=str(ROOT / "dist" / "KatsushikaCampusDays_win64.zip"))
    parser.add_argument("--readme", default=str(ROOT / "docs" / "HOW_TO_PLAY.md"))
    args = parser.parse_args(argv[1:])
    return build_zip(Path(args.build), Path(args.out), Path(args.readme))


if __name__ == "__main__":
    raise SystemExit(main(sys.argv))
