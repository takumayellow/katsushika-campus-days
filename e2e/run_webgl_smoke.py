#!/usr/bin/env python3
"""ブラウザ版（Unity WebGL）の E2E スモーク (#69)。

    pip install -r e2e/requirements.txt            # Playwright と Pillow（未導入なら）
    python -m playwright install chromium           # Edge を使うなら不要

    python e2e/run_webgl_smoke.py                   # 公開版 (GitHub Pages) を Edge の headless + 実 GPU で
    python e2e/run_webgl_smoke.py --serve build/WebGL   # ローカルのビルドを Pages と同じ条件で配信して
    python e2e/run_webgl_smoke.py --url http://localhost:8000/ --headed
    python e2e/run_webgl_smoke.py --browser chromium --gl software --until title   # CI と同じ条件

URL は --url、--serve、環境変数 KCD_E2E_URL の順に決まり、どれも無ければ公開版。
結果は --out（既定 build/e2e/<対象>-<日時>/）に metrics.json、console.log、スクリーンショットで残す。

終了コード: 0 = すべての確認が通った / 1 = どれかが落ちた / 2 = 引数や環境の誤り
"""

from __future__ import annotations

import argparse
import os
import sys
import time
from pathlib import Path
from urllib.parse import urlsplit

ROOT = Path(__file__).resolve().parents[1]
PUBLIC_URL = "https://takumayellow.github.io/katsushika-campus-days/"


def parse_args(argv: list[str]) -> argparse.Namespace:
    from kcd_e2e.browser import BROWSERS, GL_MODES, default_browser
    from kcd_e2e.smoke import STAGES

    parser = argparse.ArgumentParser(description=__doc__,
                                     formatter_class=argparse.RawDescriptionHelpFormatter)
    where = parser.add_mutually_exclusive_group()
    where.add_argument("--url", help=f"開く URL（既定 {PUBLIC_URL}）")
    where.add_argument("--serve", metavar="DIR", help="この WebGL ビルドを 127.0.0.1 で配信して開く")
    parser.add_argument("--serve-content-encoding", action="store_true",
                        help="--serve で .gz / .br に Content-Encoding を付ける（既定は Pages と同じく付けない）")
    parser.add_argument("--browser", choices=BROWSERS, default=default_browser())
    parser.add_argument("--headed", action="store_true", help="ウィンドウを出して動かす")
    parser.add_argument("--gl", choices=GL_MODES, default="gpu",
                        help="gpu = 実 GPU を必須にする / software = SwiftShader / any = 確かめない")
    parser.add_argument("--until", choices=STAGES, default="campus", help="どこまで進めるか")
    parser.add_argument("--load-timeout", type=float, default=180.0,
                        help="Unity が立ち上がるまでの上限（秒）")
    parser.add_argument("--scene-timeout", type=float, default=60.0,
                        help="キャンパスに入るまでの上限（秒）")
    parser.add_argument("--strict-warnings", action="store_true",
                        help="許可リストに無いコンソールの警告も失敗にする")
    parser.add_argument("--out", help="結果の置き場所（既定 build/e2e/<対象>-<日時>/）")
    return parser.parse_args(argv)


def resolve_url(url: str | None, serve: str | None, env: dict) -> tuple[str | None, str]:
    """(開く URL, 対象の名前)。--serve のときの URL は配信を始めてから決まるので None。"""
    if serve:
        return None, "local"
    chosen = url or env.get("KCD_E2E_URL") or PUBLIC_URL
    parts = urlsplit(chosen)
    if parts.scheme not in ("http", "https") or not parts.netloc:
        raise ValueError(f"http(s) の URL を渡す: {chosen}")
    if not parts.path.endswith("/") and not parts.path.endswith(".html"):
        chosen += "/"
    return chosen, "public" if chosen.rstrip("/") == PUBLIC_URL.rstrip("/") else "url"


def main(argv: list[str]) -> int:
    sys.path.insert(0, str(Path(__file__).resolve().parent))
    args = parse_args(argv[1:])

    from kcd_e2e.browser import launch_config
    from kcd_e2e.server import serve_build
    from kcd_e2e.smoke import SmokeOptions, run_smoke

    try:
        url, target = resolve_url(args.url, args.serve, os.environ)
    except ValueError as error:
        print(error, file=sys.stderr)
        return 2

    stamp = time.strftime("%Y%m%d-%H%M%S")
    out_dir = Path(args.out) if args.out else ROOT / "build" / "e2e" / f"{target}-{stamp}"
    launch = launch_config(args.browser, headless=not args.headed, gl=args.gl)

    def run(page_url: str) -> int:
        options = SmokeOptions(url=page_url, target=target, out_dir=out_dir, launch=launch,
                               until=args.until, load_timeout_s=args.load_timeout,
                               scene_timeout_s=args.scene_timeout,
                               strict_warnings=args.strict_warnings)
        report = run_smoke(options)
        print(report.summary())
        print(f"結果: {out_dir}")
        return 0 if report.ok else 1

    if url is not None:
        return run(url)
    try:
        with serve_build(Path(args.serve), content_encoding=args.serve_content_encoding) as base:
            return run(base)
    except FileNotFoundError as error:
        print(error, file=sys.stderr)
        return 2


if __name__ == "__main__":
    sys.exit(main(sys.argv))
