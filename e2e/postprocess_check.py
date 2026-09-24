"""Web 版で URP の後処理（Bloom・Vignette・LUT）が実際に描かれているかを判定する（#72）。

postprocess_probe.js を add_init_script で入れ、WebGL の描画をパスごとに数える。
判定は 2 本立て:

- 描画: UberPost・LutBuilder が 1 フレームに 1 回以上、Bloom の Upsample が 1 回以上描かれ、
  UberPost に渡る Vignette の強さ（_Vignette_Params2.z = intensity * 3）と
  Bloom の強さ（_Bloom_Params.x）が 0 より大きいこと。
  UberPost が走っているだけでは、Bloom が止まった変種や Vignette が 0 の場合と区別できない。
- console: "PostProcessing render passes will not execute" を含む warning / error は、
  WebGL2 では必ず出る EASU（FSR）の 1 行を除いて失敗。console.error と例外も失敗。
  Bloom・被写界深度・Panini のシェーダが使えないときの文言は LogType.Log（console.log）で出るので、
  Bloom の停止は console ではなく描画の数で捕まえる。

単体で本番を測る: python e2e/postprocess_check.py [--url URL] [--seconds 3]
"""

from __future__ import annotations

import argparse
import json
import sys
import time
from pathlib import Path
from typing import Any, Iterable, Mapping

PROBE_PATH = Path(__file__).with_name("postprocess_probe.js")
SITE_URL = "https://takumayellow.github.io/katsushika-campus-days/"

SHADER_OUTAGE_SUFFIX = "PostProcessing render passes will not execute"
# WebGL2 では graphicsShaderLevel が 45 未満なので、FSR の EASU は設定に関係なく使えない（#72 §2）。
ALLOWED_OUTAGES = (
    "Shader 'Hidden/Universal Render Pipeline/Edge Adaptive Spatial Upsampling' "
    "is not supported or has been stripped from the build (in 'Blit FSR Upscaling')",
)

MIN_RENDERED_FRAMES = 10
# パスごとの 1 フレームあたりの最小描画回数。本番（タイトル）の実測は 1 / 1 / 5。
MIN_DRAWS_PER_FRAME = {
    "uber": 0.9,
    "lutBuilder": 0.9,
    "bloomUpsample": 1.0,
}
PASS_LABELS = {
    "uber": "UberPost（_Vignette_Params2）",
    "lutBuilder": "LutBuilder（_HueSatCon）",
    "bloomUpsample": "Bloom Upsample（_SourceTexLowMip）",
}


def check_draws(result: Mapping[str, Any]) -> list[str]:
    """probe の stop() の結果から、描かれていない後処理を挙げる。"""
    frames = int(result.get("renderedFrames", 0))
    if frames < MIN_RENDERED_FRAMES:
        return [f"描画されたフレームが {frames} しかない（{MIN_RENDERED_FRAMES} 以上要る）"]

    failures = []
    draws = result.get("draws", {})
    for tag, minimum in MIN_DRAWS_PER_FRAME.items():
        per_frame = draws.get(tag, 0) / frames
        if per_frame < minimum:
            failures.append(
                f"{PASS_LABELS[tag]} が 1 フレームに {per_frame:.2f} 回（{minimum} 回以上要る）"
            )

    for index, uber in enumerate(result.get("uber", [])):
        failures.extend(_check_uber_values(index, uber))
    return failures


def _check_uber_values(index: int, uber: Mapping[str, Any]) -> list[str]:
    failures = []
    vignette = uber.get("vignetteParams2")
    if vignette is None:
        block = uber.get("vignetteBlockIndex")
        failures.append(
            f"UberPost[{index}] の _Vignette_Params2 の値を読めなかった（uniform block index {block}）"
        )
    elif not vignette[2] > 0:
        failures.append(f"UberPost[{index}] の Vignette の強さが 0（_Vignette_Params2.z = {vignette[2]}）")

    bloom = uber.get("bloomParams")
    if not uber.get("bloomVariant") or bloom is None:
        failures.append(f"UberPost[{index}] が Bloom を合成しない変種で描かれている")
    elif not bloom[0] > 0:
        failures.append(f"UberPost[{index}] の Bloom の強さが 0（_Bloom_Params.x = {bloom[0]}）")
    return failures


def check_console(messages: Iterable[Mapping[str, str]]) -> list[str]:
    """console と例外から、後処理のシェーダが使えない文言とエラーを挙げる。"""
    failures = []
    for message in messages:
        kind = message.get("type", "")
        text = message.get("text", "")
        if kind in ("error", "pageerror"):
            failures.append(f"console.{kind}: {text}")
        elif kind == "warning" and SHADER_OUTAGE_SUFFIX in text and not _is_allowed(text):
            failures.append(f"後処理のパスが止まる警告: {text}")
    return failures


def _is_allowed(text: str) -> bool:
    return any(allowed in text for allowed in ALLOWED_OUTAGES)


def evaluate(result: Mapping[str, Any], messages: Iterable[Mapping[str, str]]) -> list[str]:
    """描画と console の両方の失敗をまとめて返す。空なら合格。"""
    return check_draws(result) + check_console(messages)


def attach(page: Any) -> list[dict[str, str]]:
    """ページを開く前に呼ぶ。probe を入れ、console と例外をためるリストを返す。"""
    messages: list[dict[str, str]] = []
    page.on("console", lambda m: messages.append({"type": m.type, "text": m.text}))
    page.on("pageerror", lambda e: messages.append({"type": "pageerror", "text": str(e)}))
    page.add_init_script(path=str(PROBE_PATH))
    return messages


def wait_for_post_process(page: Any, timeout_s: float) -> None:
    """UberPost が描かれ始めるまで待つ（読み込みは 50 MB 強）。"""
    deadline = time.monotonic() + timeout_s
    while time.monotonic() < deadline:
        page.evaluate("() => window.__kcdPostProbe.start()")
        page.wait_for_timeout(500)
        result = page.evaluate("() => window.__kcdPostProbe.stop()")
        if result["draws"].get("uber", 0) > 0:
            return
    raise TimeoutError(f"{timeout_s:.0f} 秒待っても UberPost が描かれなかった")


def measure(page: Any, seconds: float) -> dict[str, Any]:
    """seconds 秒のあいだ描画を数えて、probe の結果を返す。"""
    page.evaluate("() => window.__kcdPostProbe.start()")
    page.wait_for_timeout(int(seconds * 1000))
    return page.evaluate("() => window.__kcdPostProbe.stop()")


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("--url", default=SITE_URL)
    parser.add_argument("--seconds", type=float, default=3.0)
    parser.add_argument("--timeout", type=float, default=180.0)
    parser.add_argument("--channel", default="msedge", help="空文字なら Playwright 同梱の Chromium")
    args = parser.parse_args(argv)
    if hasattr(sys.stdout, "reconfigure"):
        sys.stdout.reconfigure(encoding="utf-8")

    from playwright.sync_api import sync_playwright

    with sync_playwright() as playwright:
        browser = playwright.chromium.launch(channel=args.channel or None, headless=True)
        try:
            page = browser.new_page(viewport={"width": 1280, "height": 720})
            messages = attach(page)
            page.goto(args.url, wait_until="load")
            try:
                wait_for_post_process(page, args.timeout)
            except TimeoutError as error:
                print(json.dumps({"failures": [str(error)]}, ensure_ascii=False, indent=1))
                return 1
            page.wait_for_timeout(2000)
            result = measure(page, args.seconds)
        finally:
            browser.close()

    failures = evaluate(result, messages)
    print(json.dumps({"result": result, "failures": failures}, ensure_ascii=False, indent=1))
    return 1 if failures else 0


if __name__ == "__main__":
    sys.exit(main())
