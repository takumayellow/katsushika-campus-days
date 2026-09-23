"""WebGL ビルドを GitHub Pages に載せる。

使い方:
    python tools/deploy_pages.py            # build/WebGL を zip → Release web-latest → Pages ワークフロー起動
    python tools/deploy_pages.py --build    # 先に Unity で WebGL ビルドしてから同じことをする
    python tools/deploy_pages.py --no-deploy   # zip を作るだけ
    python tools/deploy_pages.py --smoke    # 載せる前に build/WebGL をローカル配信して E2E スモーク (#69)

Unity のビルドは CI ではライセンスの都合で回せないので、ここでローカルにビルドし、
成果物 zip を Release（タグ web-latest、prerelease）に置き換えて置き、
.github/workflows/pages.yml を workflow_dispatch で起動する。ワークフロー側は zip を展開して
Pages に載せるだけなので、git のサイズ制限（1 ファイル 100 MB）に当たらない。
"""

from __future__ import annotations

import argparse
import os
import subprocess
import sys
import time
import zipfile
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
PROJECT = ROOT / "unity" / "KatsushikaCampusDays"
LOG_DIR = ROOT / "unity" / "logs"
DEFAULT_UNITY = Path(r"C:\Program Files\Unity\Hub\Editor\6000.6.2f1\Editor\Unity.exe")
TAG = "web-latest"
ASSET_NAME = "webgl.zip"
SITE_URL = "https://takumayellow.github.io/katsushika-campus-days/"
SMOKE = ROOT / "e2e" / "run_webgl_smoke.py"


def run(cmd: list[str], **kwargs) -> subprocess.CompletedProcess:
    print("$", " ".join(str(c) for c in cmd), flush=True)
    return subprocess.run(cmd, **{"check": True, **kwargs})


def unity_build(unity: Path, build_dir: Path) -> None:
    LOG_DIR.mkdir(parents=True, exist_ok=True)
    log = LOG_DIR / "webgl.log"
    run([
        str(unity), "-batchmode", "-nographics", "-quit",
        "-projectPath", str(PROJECT),
        # URP は作業中のプラットフォームの品質レベルで Shader を絞る。WebGL で起動しないと
        # PC 用の設定で絞られ, WebGL で建物が描かれなくなる。
        "-buildTarget", "WebGL",
        "-executeMethod", "KCD.Editor.BuildPlayer.BuildWebGL",
        "-buildOutput", str(build_dir),
        "-logFile", str(log),
    ])


def make_zip(build_dir: Path, out: Path) -> int:
    index = build_dir / "index.html"
    if not index.is_file():
        print(f"WebGL ビルドが見つからない: {index}", file=sys.stderr)
        return 1

    files = sorted(p for p in build_dir.rglob("*") if p.is_file())
    out.parent.mkdir(parents=True, exist_ok=True)
    # .gz / .wasm / .data は既に圧縮済みなので、zip 自体は無圧縮で速く作る。
    with zipfile.ZipFile(out, "w", compression=zipfile.ZIP_STORED) as zf:
        for path in files:
            zf.write(path, path.relative_to(build_dir).as_posix())

    size_mib = out.stat().st_size / (1024 * 1024)
    print(f"{out} : {len(files)} files, {size_mib:.1f} MiB")
    return 0


def release_exists(tag: str) -> bool:
    result = subprocess.run(["gh", "release", "view", tag, "--json", "tagName"],
                            capture_output=True, text=True)
    return result.returncode == 0


def upload_release(zip_path: Path, tag: str, stamp: str) -> None:
    notes = f"WebGL ビルド {stamp}。Pages 配信用の成果物なので、遊ぶには {SITE_URL} を開く。"
    if not release_exists(tag):
        run(["gh", "release", "create", tag, "--prerelease", "--title", "WebGL (latest)",
             "--notes", notes])
    else:
        run(["gh", "release", "edit", tag, "--notes", notes])
    run(["gh", "release", "upload", tag, f"{zip_path}#{ASSET_NAME}", "--clobber"])


def local_smoke(build_dir: Path) -> int:
    """載せる前に、同じビルドを Pages と同じ条件（Content-Encoding なし）で配信してスモークを回す。"""
    return run([sys.executable, str(SMOKE), "--serve", str(build_dir)], check=False).returncode


def dispatch_workflow(tag: str) -> None:
    run(["gh", "workflow", "run", "pages.yml", "-f", f"tag={tag}"])


def main(argv: list[str]) -> int:
    parser = argparse.ArgumentParser(description=__doc__,
                                     formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--build", action="store_true", help="先に Unity で WebGL ビルドする")
    parser.add_argument("--build-dir", default=str(ROOT / "build" / "WebGL"))
    parser.add_argument("--zip", default=str(ROOT / "dist" / ASSET_NAME))
    parser.add_argument("--unity", default=os.environ.get("UNITY_EXE", str(DEFAULT_UNITY)))
    parser.add_argument("--tag", default=TAG)
    parser.add_argument("--no-deploy", action="store_true", help="zip を作るだけ")
    parser.add_argument("--smoke", action="store_true",
                        help="載せる前にローカル配信で E2E スモークを回し、落ちたら載せない")
    args = parser.parse_args(argv[1:])

    build_dir = Path(args.build_dir)
    if args.build:
        unity_build(Path(args.unity), build_dir)

    if args.smoke and local_smoke(build_dir) != 0:
        print("E2E スモークが落ちたので載せない（結果は build/e2e/ の下）", file=sys.stderr)
        return 1

    zip_path = Path(args.zip)
    zip_path.parent.mkdir(parents=True, exist_ok=True)
    if zip_path.exists():
        zip_path.unlink()
    rc = make_zip(build_dir, zip_path)
    if rc != 0 or args.no_deploy:
        return rc

    stamp = time.strftime("%Y-%m-%d %H:%M")
    upload_release(zip_path, args.tag, stamp)
    dispatch_workflow(args.tag)
    print(f"Pages ワークフローを起動した。数分後に {SITE_URL} が更新される。")
    print("進捗: gh run list --workflow pages.yml")
    print("配信後の E2E は e2e-pages.yml が自動で走る（GPU なし）。実 GPU で確かめるなら:")
    print(f"    python {SMOKE.relative_to(ROOT).as_posix()}")
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
