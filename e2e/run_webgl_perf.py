#!/usr/bin/env python3
"""ブラウザ版の画質の段（Low / Medium / High）ごとの fps・1% low・描画コール・三角形 (#70)。

    python e2e/run_webgl_perf.py --serve build/WebGL                 # ローカルのビルド, 60 fps の上限あり
    python e2e/run_webgl_perf.py --serve build/WebGL --uncapped      # 上限なし
    python e2e/run_webgl_perf.py --url https://takumayellow.github.io/katsushika-campus-days/ --tiers high
    python e2e/run_webgl_perf.py --serve build/WebGL --cpu-throttle 4   # CPU を 4 倍遅くして測る
    python e2e/run_webgl_perf.py --serve build/WebGL --phone portrait --cpu-throttle 4 --throttle-load
                                                         # スマホの縦画面。読み込みから CPU を 4 倍遅くする

Edge を headless（実 GPU）で開き, ビューポートは 1280×900。音は出さない。マウスは取らない（クリックしない）。
--phone ならスマホの画面（縦 390×844 / 横 844×390, DPR 3, タッチ, Android の UA）で開き, 通信量の案内の
「読み込む」とタイトル・選択はタップで, 歩きは左下のスティックを指で倒して進める。保存が無いときの段は
スマホでは Low。読み込みの時間はローダーを取りに行ってから数える（案内を叩くまでの時間を含めない）。
CPU を遅くする命令はページのメインスレッドに掛かり, data を展開する Worker には掛からない。

1. 段ごとに新しいプロファイルで開き, タイトルの設定画面で段を変えてからキャンパスに入り,
   立ち（stand）と歩き（walk）をそれぞれ --seconds 秒記録する。--phone の Low は保存が無いときの段なので,
   設定画面を開かずに入る（WASM のメモリの拡張が 1 回少ない）。
2. 最後に 1 つのセッションでポーズし（時間が止まる）, 同じ絵のまま段だけを順に変えて --frozen-seconds 秒ずつ
   記録する。これを --frozen-rounds 回くり返す。0 回なら測らない。

結果は --out（既定 build/e2e/perf-<日時>/）に perf.json と, 段ごとの画像で残す。
終了コード: 0 = 測れた / 1 = 途中で止まった / 2 = 引数の誤り
"""

from __future__ import annotations

import argparse
import dataclasses
import sys
import time
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]


def parse_args(argv: list[str]) -> argparse.Namespace:
    from kcd_e2e.browser import BROWSERS, default_browser
    from kcd_e2e.perf import PHONES, TIERS

    parser = argparse.ArgumentParser(description=__doc__,
                                     formatter_class=argparse.RawDescriptionHelpFormatter)
    where = parser.add_mutually_exclusive_group(required=True)
    where.add_argument("--url", help="開く URL")
    where.add_argument("--serve", metavar="DIR", help="この WebGL ビルドを 127.0.0.1 で配信して開く")
    parser.add_argument("--tiers", default=",".join(TIERS), help="測る段（カンマ区切り）")
    parser.add_argument("--uncapped", action="store_true", help="60 fps の上限を外して測る")
    parser.add_argument("--cpu-throttle", type=float, default=1.0,
                        help="読み込みのあと CPU をこの倍率だけ遅くして測る（1 = 掛けない）")
    parser.add_argument("--throttle-load", action="store_true",
                        help="--cpu-throttle を開く前から掛け, 読み込みの時間にも効かせる")
    parser.add_argument("--phone", choices=tuple(PHONES), help="スマホの画面とタッチで測る（縦か横）")
    parser.add_argument("--seconds", type=float, default=10.0, help="立ちと歩きをそれぞれ記録する秒数")
    parser.add_argument("--frozen-rounds", type=int, default=2, help="ポーズ中の段の比べをくり返す回数")
    parser.add_argument("--frozen-seconds", type=float, default=3.0, help="ポーズ中に 1 つの段を記録する秒数")
    parser.add_argument("--browser", choices=BROWSERS, default=default_browser())
    parser.add_argument("--headed", action="store_true", help="ウィンドウを出して動かす")
    parser.add_argument("--out", help="結果の置き場所（既定 build/e2e/perf-<日時>/）")
    args = parser.parse_args(argv)
    tiers = tuple(t.strip() for t in args.tiers.split(",") if t.strip())
    unknown = [t for t in tiers if t not in TIERS]
    if not tiers or unknown:
        parser.error(f"--tiers は {TIERS} から選ぶ: {args.tiers}")
    if args.cpu_throttle < 1:
        parser.error("--cpu-throttle は 1 以上")
    if args.throttle_load and args.cpu_throttle == 1:
        parser.error("--throttle-load は --cpu-throttle（1 より大きい倍率）と組み合わせる")
    if args.frozen_rounds < 0 or args.seconds <= 0 or args.frozen_seconds <= 0:
        parser.error("--frozen-rounds は 0 以上, --seconds と --frozen-seconds は正の数")
    args.tiers = tiers
    return args


def format_table(result) -> str:
    lines = ["| 段 | 場面 | fps | 1% low | 1 フレームの処理（平均 / p99） | 描画コール（平均 / 最大） | 三角形（平均） |",
             "|---|---|---|---|---|---|---|"]
    rows = [(session.tier, scene, s) for session in result.sessions for scene, s in session.scenes.items()]
    rows += [(tier, f"frozen {round_index}", s) for round_index, tier, s in result.frozen]
    for tier, scene, s in rows:
        lines.append(f"| {tier} | {scene} | {s['fps']:.1f} | {s['one_percent_low']:.1f} | "
                     f"{s['work_ms']:.2f} / {s['work_p99_ms']:.2f} ms | "
                     f"{s['draws']:.0f} / {s['draws_max']} | {s['triangles'] / 1000:.0f}k |")
    return "\n".join(lines)


def main(argv: list[str]) -> int:
    sys.path.insert(0, str(Path(__file__).resolve().parent))
    args = parse_args(argv[1:])

    from kcd_e2e.browser import launch_config
    from kcd_e2e.perf import PHONES, PerfOptions, run_perf, uncapped_args
    from kcd_e2e.server import serve_build
    from playwright.sync_api import TimeoutError as PlaywrightTimeoutError

    base = launch_config(args.browser, headless=not args.headed, gl="gpu")
    launch = dataclasses.replace(base, args=base.args + uncapped_args(args.uncapped))
    stamp = time.strftime("%Y%m%d-%H%M%S")
    out_dir = Path(args.out) if args.out else ROOT / "build" / "e2e" / f"perf-{stamp}"

    def run(url: str) -> int:
        options = PerfOptions(url=url, out_dir=out_dir, launch=launch, tiers=args.tiers,
                              measure_s=args.seconds, frozen_rounds=args.frozen_rounds,
                              frozen_s=args.frozen_seconds, cpu_throttle=args.cpu_throttle,
                              throttle_load=args.throttle_load,
                              phone=PHONES[args.phone] if args.phone else None)
        try:
            result = run_perf(options)
        except (TimeoutError, PlaywrightTimeoutError) as error:
            print(error, file=sys.stderr)
            return 1
        print(format_table(result))
        for session in result.sessions:
            heap = session.heap_campus / 2**20 if session.heap_campus else float("nan")
            print(f"{session.tier}: canvas {session.canvas} / heap {heap:.1f} MiB")
            title = session.heap_title / 2**20 if session.heap_title else float("nan")
            ready = (session.load or {}).get("ready_after_request")
            ready_s = ready / 1000 if ready is not None else float("nan")
            print(f"{session.tier}: タイトルの heap {title:.1f} MiB / ローダーを取りに行ってから起動まで {ready_s:.1f} 秒")
        if result.sessions:
            print(f"起動時: {result.sessions[0].startup_log}")
        print(f"結果: {out_dir}")
        return 0

    if args.url:
        return run(args.url)
    try:
        with serve_build(Path(args.serve)) as url:
            return run(url)
    except FileNotFoundError as error:
        print(error, file=sys.stderr)
        return 2


if __name__ == "__main__":
    sys.exit(main(sys.argv))
