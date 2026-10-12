"""画質の段（Low / Medium / High）ごとに, ブラウザ版の fps・1% low・描画コール・三角形を測る (#70)。

段ごとに新しいプロファイルで開き（保存が無いので PC の Web 版は Medium で始まる）, タイトルの設定画面で段を
変えてからキャンパスに入る。キャンパスでは, 同じ地点に立つ（stand）場合と, 前へ歩き続ける（walk）場合を
それぞれ measure_s 秒ずつ記録する。立ち・歩きは NPC と時計が動くので, 回ごとに描く物が少し変わる。
そこで最後に 1 つのセッションでポーズし, 時間が止まった同じ絵のまま段だけを切り替えて, 段の差を測る。
数え方は perf_probe.js, 集計は summarize() を見る。
"""

from __future__ import annotations

import json
import math
import time
from collections.abc import Iterator
from contextlib import contextmanager
from dataclasses import dataclass, field
from pathlib import Path

from playwright.sync_api import Browser, Page, sync_playwright

from . import pixels
from .browser import LaunchConfig

PROBE_JS = Path(__file__).resolve().parents[1] / "perf_probe.js"
INSTRUMENT_JS = Path(__file__).with_name("instrument.js")
START_JS = "() => window.__kcdPerf.start()"
STOP_JS = "() => window.__kcdPerf.stop()"
READY_JS = "() => { const s = window.__kcdE2E; return !!s && (s.readyAt !== null || s.error !== null); }"
HEAP_JS = """() => {
  const s = window.__kcdE2E;
  let wasm = 0;
  if (s) for (const m of s.memories) { try { wasm = Math.max(wasm, m.buffer.byteLength); } catch (_) {} }
  return wasm || null;
}"""
CANVAS_JS = "() => { const c = document.querySelector('#unity-canvas'); return [c.width, c.height]; }"

TIERS = ("low", "medium", "high")
# 保存が無いときの PC の Web 版の段。設定画面の左右で, Low ← Medium → High と 1 つずつ動く。
WEB_DEFAULT_TIER = "medium"
# 設定画面の「画質」の行（SettingsView.RowQuality）。開くと一番上の行が選ばれている。
QUALITY_ROW = 4
# 上限なしで測るときの Edge の引数（docs/WEBGL_BUDGET.md 1.1）。
UNCAPPED_ARGS = ("--disable-gpu-vsync", "--disable-frame-rate-limit")
CAMPUS_LOADED = "[KCD] scene_load scene=Campus"
KEY_GAP_MS = 250


def uncapped_args(uncapped: bool) -> tuple[str, ...]:
    return UNCAPPED_ARGS if uncapped else ()


def percentile(values: list[float], percent: float) -> float:
    """最も近い順位の方法の百分位数。"""
    ordered = sorted(values)
    rank = max(1, math.ceil(percent / 100 * len(ordered)))
    return ordered[rank - 1]


def summarize(frames: list[dict]) -> dict:
    """perf_probe.js のフレームの配列を, fps・1% low・処理時間・描画コール・三角形にまとめる。

    fps は間隔の平均から, 1% low は間隔の遅い 1%（少なくとも 1 個）の平均から出す。
    """
    if len(frames) < 2:
        raise ValueError("フレームが 2 つ以上いる")
    times = [frame["t"] for frame in frames]
    intervals = [later - earlier for earlier, later in zip(times, times[1:])]
    slowest = sorted(intervals, reverse=True)[:max(1, math.ceil(len(intervals) * 0.01))]
    work = [frame["work"] for frame in frames]
    draws = [frame["draws"] for frame in frames]
    tris = [frame["triangles"] for frame in frames]
    return {
        "frames": len(frames),
        "seconds": (times[-1] - times[0]) / 1000,
        "fps": 1000 / (sum(intervals) / len(intervals)),
        "one_percent_low": 1000 / (sum(slowest) / len(slowest)),
        "work_ms": sum(work) / len(work),
        "work_p99_ms": percentile(work, 99),
        "draws": sum(draws) / len(draws),
        "draws_max": max(draws),
        "triangles": sum(tris) / len(tris),
        "triangles_max": max(tris),
    }


@dataclass(frozen=True)
class PerfOptions:
    url: str
    out_dir: Path
    launch: LaunchConfig
    tiers: tuple[str, ...] = TIERS
    measure_s: float = 10.0
    settle_s: float = 5.0
    frozen_rounds: int = 2
    frozen_s: float = 3.0
    load_timeout_s: float = 180.0
    scene_timeout_s: float = 60.0
    viewport: tuple[int, int] = (1280, 900)


@dataclass
class TierResult:
    tier: str
    scenes: dict[str, dict] = field(default_factory=dict)
    heap_campus: int | None = None
    canvas: tuple[int, int] | None = None
    # 起動時の「[KCD] graphics tier=」の行。設定画面で段を変える前の値で, 変えたあとには出ない。
    startup_log: str | None = None

    def as_dict(self) -> dict:
        return {"tier": self.tier, "scenes": self.scenes, "heap_campus": self.heap_campus,
                "canvas": self.canvas, "startup_log": self.startup_log}


@dataclass
class PerfResult:
    sessions: list[TierResult] = field(default_factory=list)
    # ポーズ中（時間が止まった同じ絵）で段だけを切り替えた記録。(回, 段, 集計) の並び。
    frozen: list[tuple[int, str, dict]] = field(default_factory=list)

    def as_dict(self) -> dict:
        return {"sessions": [result.as_dict() for result in self.sessions],
                "frozen": [{"round": r, "tier": t, **s} for r, t, s in self.frozen]}


def step_keys(current: str, target: str) -> list[str]:
    """設定画面の「画質」の行で current から target へ動かすキー。段は端で反対の端へ回る。"""
    for tier in (current, target):
        if tier not in TIERS:
            raise ValueError(f"tier は {TIERS} のどれか: {tier}")
    step = (TIERS.index(target) - TIERS.index(current)) % len(TIERS)
    if step == 0:
        return []
    return ["ArrowRight"] if step == 1 else ["ArrowLeft"]


def tier_keys(tier: str) -> list[str]:
    """Web 版の既定の段から tier へ動かすための, 設定画面の「画質」の行で押すキー。"""
    return step_keys(WEB_DEFAULT_TIER, tier)


def run_perf(options: PerfOptions) -> PerfResult:
    """段ごとに新しいプロファイルで立ち・歩きを測り, そのあと 1 つのセッションでポーズ中の段の差を測る。"""
    options.out_dir.mkdir(parents=True, exist_ok=True)
    result = PerfResult()
    with sync_playwright() as playwright:
        browser = playwright.chromium.launch(**options.launch.launch_kwargs())
        try:
            for tier in options.tiers:
                with _session(browser, options) as (page, console):
                    result.sessions.append(_measure_tier(page, console, tier, options))
            if options.frozen_rounds > 0:
                with _session(browser, options) as (page, console):
                    result.frozen = _measure_frozen(page, console, options)
        finally:
            browser.close()
    payload = {"url": options.url, "launch": list(options.launch.args), **result.as_dict()}
    (options.out_dir / "perf.json").write_text(json.dumps(payload, ensure_ascii=False, indent=2),
                                               encoding="utf-8")
    return result


@contextmanager
def _session(browser: Browser, options: PerfOptions) -> Iterator[tuple[Page, list[str]]]:
    """新しいプロファイルで開き, タイトルが描かれるまで待つ（保存が無いので PC の Web 版は Medium）。"""
    context = browser.new_context(
        viewport={"width": options.viewport[0], "height": options.viewport[1]},
        device_scale_factor=1, locale="ja-JP")
    try:
        context.add_init_script(path=str(INSTRUMENT_JS))
        context.add_init_script(path=str(PROBE_JS))
        page = context.new_page()
        console: list[str] = []
        page.on("console", lambda msg: console.append(msg.text))
        page.goto(options.url, wait_until="domcontentloaded", timeout=options.load_timeout_s * 1000)
        page.wait_for_function(READY_JS, timeout=options.load_timeout_s * 1000, polling=250)
        _wait_drawn(page, options.scene_timeout_s)
        # クリックはしない。Unity がマウスをロックし, 手元のマウスを取ってしまう。キーはフォーカスで届く。
        page.locator("#unity-canvas").focus()
        yield page, console
    finally:
        context.close()


def _enter_campus(page: Page, console: list[str], options: PerfOptions) -> None:
    page.keyboard.press("Enter")
    page.wait_for_timeout(2000)
    page.keyboard.press("Enter")
    _wait_console(page, console, CAMPUS_LOADED, options.scene_timeout_s)
    page.wait_for_timeout(options.settle_s * 1000)


def _measure_tier(page: Page, console: list[str], tier: str, options: PerfOptions) -> TierResult:
    result = TierResult(tier)
    result.startup_log = next((line for line in console if "[KCD] graphics tier=" in line), None)
    _press(page, ["o"] + ["ArrowDown"] * QUALITY_ROW + tier_keys(tier))
    _save_canvas(page, options.out_dir / f"{tier}-settings.png")
    _press(page, ["Escape"])
    _enter_campus(page, console, options)
    result.heap_campus = page.evaluate(HEAP_JS)
    result.canvas = tuple(page.evaluate(CANVAS_JS))

    result.scenes["stand"] = _record(page, options.measure_s)
    _save_canvas(page, options.out_dir / f"{tier}-stand.png")
    page.keyboard.down("w")
    try:
        result.scenes["walk"] = _record(page, options.measure_s)
    finally:
        page.keyboard.up("w")
    _save_canvas(page, options.out_dir / f"{tier}-walk.png")
    return result


def _measure_frozen(page: Page, console: list[str], options: PerfOptions) -> list[tuple[int, str, dict]]:
    """キャンパスでポーズし（時間が止まり, NPC も時計も動かない）, 設定画面の「画質」の行で段を順に変えて測る。

    絵は段の違いのほかは変わらないので, 描画コールと三角形の差がそのまま段の差になる。設定画面の分の描画は
    どの段にも同じだけ入る。ポーズ中はゲームの更新が軽くなるので, fps は遊んでいるときより高めに出る。
    """
    _enter_campus(page, console, options)
    # ポーズの一覧で「設定」は 4 行目（PauseMenu.Execute の case 3）。
    _press(page, ["Escape"] + ["ArrowDown"] * 3 + ["Enter"] + ["ArrowDown"] * QUALITY_ROW)
    records = []
    current = WEB_DEFAULT_TIER
    for round_index in range(1, options.frozen_rounds + 1):
        for tier in options.tiers:
            _press(page, step_keys(current, tier))
            current = tier
            page.wait_for_timeout(1000)
            records.append((round_index, tier, _record(page, options.frozen_s)))
            if round_index == 1:
                _save_canvas(page, options.out_dir / f"frozen-{tier}.png")
    return records


def _press(page: Page, keys: list[str]) -> None:
    for key in keys:
        page.keyboard.press(key)
        page.wait_for_timeout(KEY_GAP_MS)


def _record(page: Page, seconds: float) -> dict:
    page.evaluate(START_JS)
    page.wait_for_timeout(seconds * 1000)
    return summarize(page.evaluate(STOP_JS))


def _wait_drawn(page: Page, timeout_s: float) -> None:
    deadline = time.monotonic() + timeout_s
    while not pixels.frame_stats(_canvas_png(page)).drawn:
        if time.monotonic() >= deadline:
            raise TimeoutError(f"{timeout_s:.0f} 秒でタイトルが描かれない")
        page.wait_for_timeout(500)


def _wait_console(page: Page, console: list[str], needle: str, timeout_s: float) -> None:
    deadline = time.monotonic() + timeout_s
    while not any(needle in line for line in console):
        if time.monotonic() >= deadline:
            raise TimeoutError(f"{timeout_s:.0f} 秒で「{needle}」が出ない")
        page.wait_for_timeout(250)


def _canvas_png(page: Page) -> bytes:
    return page.locator("#unity-canvas").screenshot()


def _save_canvas(page: Page, path: Path) -> None:
    path.write_bytes(_canvas_png(page))
