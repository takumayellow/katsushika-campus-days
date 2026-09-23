"""ブラウザ版を開いて、読み込み → タイトル → キャラ選択 → キャンパスまで進めて確かめる。

確認項目（Report.checks）:
    gl          ... gl="gpu" のとき、WebGL が実 GPU で動いている（SwiftShader ではない）
    load        ... 制限時間内に Unity のインスタンスが立ち上がった（createUnityInstance が解決した）
    build_files ... Build/ のローダー・フレームワーク・wasm・データがすべて 200 で届いた
    http        ... ほかのリクエストにも 4xx / 5xx と通信失敗が無い
    title_drawn ... キャンバスに絵が描かれている（一色塗りでない）
    overlay     ... キャンバスの中の案内（#kcd-overlay）が、キー入力のあと出たままにならない (#48)
    select      ... Enter でキャラ選択へ進み、画面が変わった
    campus      ... もう一度 Enter でキャンパスに入り、絵が描かれている
    console     ... コンソールにエラーが無い（許可リストのものを除く）
"""

from __future__ import annotations

import time
import traceback
from dataclasses import dataclass, field
from pathlib import Path
from urllib.parse import urlsplit

from playwright.sync_api import Error as PlaywrightError
from playwright.sync_api import Page, Response, sync_playwright

from . import pixels
from .browser import RENDERER_JS, LaunchConfig, is_software_renderer
from .console_policy import ConsoleEntry, classify
from .report import Report

INSTRUMENT_JS = Path(__file__).with_name("instrument.js")
STAGES = ("ready", "title", "select", "campus")

# テンプレートの OVERLAY_MS（3.5 秒）より少し長く待ってから、案内が消えたかを見る。
OVERLAY_SETTLE_S = 4.0
# 画面が変わったとみなす変化量（画素の割合）。キャラ選択はタイトルの文字が入れ替わるだけなので小さい。
# タイトルで勝手に動く画素（点滅する案内）は数えない。
MIN_SELECT_CHANGE = 0.02
MIN_SCENE_CHANGE = 0.10
# Build/ に必ず届くファイル（名前の一部）。ハッシュ名にしても末尾はこの形のまま。
REQUIRED_BUILD_PARTS = (".loader.js", ".framework.js", ".wasm", ".data")
# ページ自身が取りやめた読み込み。サーバーの失敗（4xx / 5xx は応答で、接続や DNS の失敗は別の
# net::ERR_* で届く）ではないので、数えて残すだけにする。SwiftShader の Chromium では、<audio> が
# blob: の音声を読み直すときと、読み終えた wasm の fetch が、この形で報告されることがある。
CANCELLED_FAILURE = "net::ERR_ABORTED"

READY_JS = "() => { const s = window.__kcdE2E; return !!s && (s.readyAt !== null || s.error !== null); }"
STATE_JS = """() => {
  const s = window.__kcdE2E;
  if (!s) return null;
  return { loaderLoadedAt: s.loaderLoadedAt, firstProgressAt: s.firstProgressAt,
           progressDoneAt: s.progressDoneAt, readyAt: s.readyAt, lastProgress: s.lastProgress,
           error: s.error, instanceCaptured: s.instance !== null };
}"""
NAVIGATION_JS = """() => {
  const nav = performance.getEntriesByType('navigation')[0];
  return nav ? { domContentLoaded: nav.domContentLoadedEventEnd, load: nav.loadEventEnd } : {};
}"""
# wasm_memory は instrument.js が捕まえた WebAssembly.Memory の大きさ（= Unity のヒープ全体）。
# Unity のテンプレートは unityInstance.Module を外に出さないので、Module.HEAPU8 からは読めない。
HEAP_JS = """() => {
  const s = window.__kcdE2E;
  let wasm = 0;
  if (s) for (const m of s.memories) { try { wasm = Math.max(wasm, m.buffer.byteLength); } catch (_) {} }
  const pm = performance.memory || null;
  return {
    wasm_memory: wasm || null,
    js_heap_used: pm ? pm.usedJSHeapSize : null,
    js_heap_total: pm ? pm.totalJSHeapSize : null,
  };
}"""
OVERLAY_HIDDEN_JS = """() => {
  const el = document.querySelector('#kcd-overlay');
  if (!el) return null;
  return el.classList.contains('kcd-hidden') || getComputedStyle(el).visibility === 'hidden';
}"""


@dataclass(frozen=True)
class SmokeOptions:
    url: str
    target: str
    out_dir: Path
    launch: LaunchConfig
    until: str = "campus"
    load_timeout_s: float = 180.0
    frame_timeout_s: float = 30.0
    scene_timeout_s: float = 60.0
    viewport: tuple[int, int] = (1440, 960)
    strict_warnings: bool = False


@dataclass
class _Collected:
    console: list[ConsoleEntry] = field(default_factory=list)
    responses: list[Response] = field(default_factory=list)
    failed: list[tuple[str, str, str]] = field(default_factory=list)


def stage_index(stage: str) -> int:
    if stage not in STAGES:
        raise ValueError(f"until は {STAGES} のどれか: {stage}")
    return STAGES.index(stage)


def run_smoke(options: SmokeOptions) -> Report:
    report = Report(url=options.url, target=options.target)
    options.out_dir.mkdir(parents=True, exist_ok=True)
    with sync_playwright() as playwright:
        browser = playwright.chromium.launch(**options.launch.launch_kwargs())
        try:
            context = browser.new_context(
                viewport={"width": options.viewport[0], "height": options.viewport[1]},
                device_scale_factor=1, locale="ja-JP")
            context.add_init_script(path=str(INSTRUMENT_JS))
            page = context.new_page()
            report.metrics["browser"] = {
                "name": options.launch.browser, "version": browser.version,
                "headless": options.launch.headless, "gl": options.launch.gl,
                "args": list(options.launch.args),
            }
            _SmokeRun(page, options, report).run()
        finally:
            browser.close()
    report.write_json(options.out_dir / "metrics.json")
    return report


class _SmokeRun:
    def __init__(self, page: Page, options: SmokeOptions, report: Report):
        self.page = page
        self.options = options
        self.report = report
        self.collected = _Collected()
        self.shots: dict[str, bytes] = {}
        self._nav_started = 0.0
        self._listening = False

    # ---- 全体の流れ ----

    def run(self) -> None:
        until = stage_index(self.options.until)
        try:
            if not self._check_gl():
                return
            self._attach_listeners()
            if not self._load():
                return
            self._record_heap("ready")
            if until >= stage_index("title") and not self._title():
                return
            if until >= stage_index("select") and not self._select():
                return
            if until >= stage_index("campus"):
                self._campus()
        except PlaywrightError as error:
            self.report.add("playwright", False, _first_line(str(error)))
        except Exception as error:  # noqa: BLE001 - 落ちた理由を metrics.json に残して NG にする
            self.report.add("exception", False, f"{type(error).__name__}: {_first_line(str(error))}")
            self.report.metrics["traceback"] = traceback.format_exc()
        finally:
            self._finish()

    def _finish(self) -> None:
        try:
            self._screenshot_page("last")
        except PlaywrightError:
            pass
        if not self._listening:
            return
        self._check_network()
        self._check_console()
        self._write_console_log()

    # ---- 各段階 ----

    def _check_gl(self) -> bool:
        self.page.goto("about:blank")
        info = self.page.evaluate(RENDERER_JS)
        self.report.metrics["webgl"] = info
        renderer = info.get("renderer")
        if not info.get("webgl2"):
            return self.report.add("gl", False, "WebGL 2 が使えない")
        software = is_software_renderer(renderer)
        if self.options.launch.gl == "gpu":
            # レンダラー名が取れないときは実 GPU かどうか分からないので、通さない。
            return self.report.add("gl", bool(renderer) and not software, f"renderer={renderer}")
        self.report.add("gl", True, f"renderer={renderer}（gl={self.options.launch.gl}）")
        return True

    def _attach_listeners(self) -> None:
        collected = self.collected
        page = self.page
        page.on("console", lambda msg: collected.console.append(
            ConsoleEntry(msg.type, msg.text, (msg.location or {}).get("url", ""))))
        page.on("pageerror", lambda exc: collected.console.append(
            ConsoleEntry("pageerror", f"{exc.name}: {exc.message}")))
        # Playwright は handler に属性を足すので、組み込みの list.append は直接渡せない。
        page.on("response", lambda res: collected.responses.append(res))
        page.on("requestfailed", lambda req: collected.failed.append(
            (req.url, req.failure or "", req.resource_type)))
        self._listening = True

    def _load(self) -> bool:
        timeout_ms = self.options.load_timeout_s * 1000
        self._nav_started = time.monotonic()
        self.page.goto(self.options.url, wait_until="domcontentloaded", timeout=timeout_ms)
        remaining_ms = max(1000.0, timeout_ms - self._elapsed_ms())
        try:
            self.page.wait_for_function(READY_JS, timeout=remaining_ms, polling=250)
        except PlaywrightError:
            pass
        state = self.page.evaluate(STATE_JS) or {}
        navigation = self.page.evaluate(NAVIGATION_JS)
        self.report.metrics["timings_ms"] = _timings(navigation, state)
        self.report.metrics["unity"] = {"last_progress": state.get("lastProgress"),
                                        "instance_captured": state.get("instanceCaptured")}
        if state.get("error"):
            return self.report.add("load", False,
                                   f"createUnityInstance が失敗: {_first_line(state['error'])}")
        ready = state.get("readyAt")
        if ready is None:
            return self.report.add(
                "load", False,
                f"{self.options.load_timeout_s:.0f} 秒で立ち上がらない（進み具合 "
                f"{state.get('lastProgress')}、計測スクリプト {'あり' if state else 'なし'}）")
        return self.report.add("load", ready <= timeout_ms,
                               f"{ready / 1000:.1f} 秒（上限 {self.options.load_timeout_s:.0f} 秒）")

    def _title(self) -> bool:
        deadline = time.monotonic() + self.options.frame_timeout_s
        while True:
            png = self._canvas_png()
            stats = pixels.frame_stats(png)
            if stats.drawn or time.monotonic() >= deadline:
                break
            self.page.wait_for_timeout(500)
        self.report.metrics.setdefault("frames", {})["title"] = stats.as_dict()
        if stats.drawn:
            self.report.metrics["timings_ms"]["first_drawn_frame"] = round(self._elapsed_ms())
        self._save_shot("title", png)
        if not self.report.add("title_drawn", stats.drawn, _frame_detail(stats)):
            return False
        hidden = self.page.evaluate(OVERLAY_HIDDEN_JS)
        self.report.add("overlay_before_input", hidden is True,
                        _overlay_detail(hidden, "案内は隠れている", "入力の前から案内が出ている"))
        return True

    def _select(self) -> bool:
        # タイトルを続けて撮り、点滅する「Enter ではじめる」など勝手に動く画素を集めておく。
        frames = [self.shots["title"]]
        for _ in range(2):
            self.page.wait_for_timeout(700)
            frames.append(self._canvas_png())
        noise = pixels.noise_mask(frames)
        title = frames[-1]
        # 実際の遊び方どおり、キャンバスをクリックしてからキーを押す（音声の再生許可もこれで得る）。
        self.page.locator("#unity-canvas").click()
        self.page.keyboard.press("Enter")
        self.page.wait_for_timeout(OVERLAY_SETTLE_S * 1000)
        hidden = self.page.evaluate(OVERLAY_HIDDEN_JS)
        self.report.add("overlay", hidden is True,
                        _overlay_detail(hidden, "キー入力のあと案内は消えた",
                                        "キー入力のあとも案内が出ている (#48)"))
        select = self._canvas_png()
        self._save_shot("select", select)
        change = pixels.changed_fraction(title, select, ignore=noise)
        self.report.metrics.setdefault("frames", {})["select"] = {
            **pixels.frame_stats(select).as_dict(),
            "title_noise": round(pixels.changed_fraction(frames[0], title), 4),
            "change_from_title": round(pixels.changed_fraction(title, select), 4),
            "change_outside_noise": round(change, 4)}
        return self.report.add("select", change >= MIN_SELECT_CHANGE,
                               f"タイトルで動かない画素の {change:.1%} が変わった"
                               f"（必要 {MIN_SELECT_CHANGE:.0%}）")

    def _campus(self) -> bool:
        before = self.shots["select"]
        self.page.keyboard.press("Enter")
        pressed = time.monotonic()
        png, change, stats = before, 0.0, None
        while time.monotonic() - pressed < self.options.scene_timeout_s:
            self.page.wait_for_timeout(1000)
            png = self._canvas_png()
            change = pixels.changed_fraction(before, png)
            stats = pixels.frame_stats(png)
            if change >= MIN_SCENE_CHANGE and stats.drawn:
                break
        entered_s = time.monotonic() - pressed
        # 入った直後は読み込みで止まっていることがあるので、少し歩かせてから撮る。
        self.page.wait_for_timeout(3000)
        png = self._canvas_png()
        stats = pixels.frame_stats(png)
        self._save_shot("campus", png)
        self._record_heap("campus")
        self.report.metrics["timings_ms"]["campus_after_enter"] = round(entered_s * 1000)
        self.report.metrics.setdefault("frames", {})["campus"] = {
            **stats.as_dict(), "change_from_select": round(change, 4)}
        ok = change >= MIN_SCENE_CHANGE and stats.drawn
        return self.report.add("campus", ok, f"キャラ選択からの変化 {change:.1%}、{entered_s:.1f} 秒、"
                                             + _frame_detail(stats))

    # ---- 後片付けの確認 ----

    def _check_network(self) -> None:
        page_path = urlsplit(self.options.url).path
        files = [_build_file(res) for res in self.collected.responses
                 if _is_build_path(urlsplit(res.url).path, page_path)]
        self.report.metrics["build_files"] = files
        self.report.metrics["build_bytes_total"] = sum(f["bytes"] or 0 for f in files)
        if files or any(c.name == "load" for c in self.report.checks):
            delivered = [f["name"] for f in files if f["status"] == 200]
            missing = [part for part in REQUIRED_BUILD_PARTS
                       if not any(part in name for name in delivered)]
            not_ok = [f"{f['name']}={f['status']}" for f in files if f["status"] != 200]
            self.report.add("build_files", not missing and not not_ok,
                            f"200 が {len(delivered)} 本、計 "
                            f"{self.report.metrics['build_bytes_total']:,} B"
                            + (f"、届かない: {missing}" if missing else "")
                            + (f"、200 以外: {not_ok}" if not_ok else ""))
        self._check_http()

    def _check_http(self) -> None:
        cancelled = [(u, kind) for u, why, kind in self.collected.failed
                     if why == CANCELLED_FAILURE]
        failed = [(u, why) for u, why, _ in self.collected.failed if why != CANCELLED_FAILURE]
        self.report.metrics["cancelled_requests"] = [
            {"url": _short_url(u), "resource_type": kind} for u, kind in cancelled]
        problems = [f"{_short_url(r.url)} {r.status}" for r in self.collected.responses
                    if r.status >= 400]
        problems += [f"{_short_url(u)} {why}" for u, why in failed]
        detail = "4xx / 5xx / 通信失敗なし" if not problems else "; ".join(problems[:5])
        if cancelled:
            detail += f"（ページが取りやめた読み込み {len(cancelled)} 件は数えない）"
        self.report.add("http", not problems, detail)

    def _check_console(self) -> None:
        verdict = classify(self.collected.console)
        allowed: dict[str, dict] = {}
        for entry, rule in verdict.allowed:
            key = _first_line(entry.text)[:160]
            allowed.setdefault(key, {"text": key, "count": 0, "reason": rule.reason})["count"] += 1
        self.report.metrics["console"] = {
            "messages": len(self.collected.console),
            "errors": [e.text[:300] for e in verdict.errors],
            "allowed": list(allowed.values()),
            "unknown_warnings": _count_kinds(verdict.warnings),
            "unity_quality": next((e.text.strip() for e in self.collected.console
                                   if e.text.startswith("[KCD] quality=")), None),
        }
        detail = (f"エラー {len(verdict.errors)} 件、許可リストの既知 {len(verdict.allowed)} 件、"
                  f"未知の警告 {len(verdict.warnings)} 件")
        if verdict.errors:
            detail += "。先頭: " + _first_line(verdict.errors[0].text)
        elif verdict.warnings:
            detail += "。先頭: " + _first_line(verdict.warnings[0].text)
        strict = self.options.strict_warnings
        self.report.add("console", verdict.ok_strict if strict else verdict.ok,
                        detail + ("（strict: 未知の警告も失敗）" if strict else ""))

    def _write_console_log(self) -> None:
        lines = [f"[{e.type}] {e.text}" for e in self.collected.console]
        (self.options.out_dir / "console.log").write_text("\n".join(lines) + "\n", encoding="utf-8")

    # ---- 道具 ----

    def _elapsed_ms(self) -> float:
        return (time.monotonic() - self._nav_started) * 1000

    def _canvas_png(self) -> bytes:
        return self.page.locator("#unity-canvas").screenshot(type="png", animations="allow")

    def _save_shot(self, name: str, png: bytes) -> None:
        self.shots[name] = png
        path = self.options.out_dir / f"{len(self.report.screenshots) + 1:02d}-{name}.png"
        path.write_bytes(png)
        self.report.screenshots.append(str(path))

    def _screenshot_page(self, name: str) -> None:
        path = self.options.out_dir / f"page-{name}.png"
        self.page.screenshot(path=str(path))
        self.report.screenshots.append(str(path))

    def _record_heap(self, label: str) -> None:
        self.report.metrics.setdefault("heap", {})[label] = self.page.evaluate(HEAP_JS)


def _timings(navigation: dict, state: dict) -> dict:
    keys = (("dom_content_loaded", navigation.get("domContentLoaded")),
            ("loader_loaded", state.get("loaderLoadedAt")),
            ("first_progress", state.get("firstProgressAt")),
            ("download_done", state.get("progressDoneAt")),
            ("unity_ready", state.get("readyAt")))
    return {name: round(value) for name, value in keys if isinstance(value, (int, float))}


def _is_build_path(path: str, page_path: str) -> bool:
    """path がページと同じ場所の Build/ の下か。page_path は .../ でも .../index.html でもよい。"""
    base = page_path[:page_path.rfind("/") + 1] or "/"
    return path.startswith(base) and "/Build/" in path[len(base) - 1:]


def _build_file(response: Response) -> dict:
    headers = response.headers
    timing = response.request.timing or {}
    length = headers.get("content-length")
    return {
        "name": urlsplit(response.url).path.rsplit("/", 1)[-1],
        "status": response.status,
        "bytes": int(length) if length and length.isdigit() else None,
        "content_encoding": headers.get("content-encoding"),
        "last_modified": headers.get("last-modified"),
        "ms": round(timing["responseEnd"]) if timing.get("responseEnd", -1) >= 0 else None,
    }


def _short_url(url: str) -> str:
    return url if len(url) <= 160 else url[:157] + "..."


def _count_kinds(entries: list[ConsoleEntry]) -> dict[str, int]:
    """同じ警告が何度も出るので、1 行目（先頭 160 文字）ごとに数える。"""
    counts: dict[str, int] = {}
    for entry in entries:
        key = _first_line(entry.text)[:160]
        counts[key] = counts.get(key, 0) + 1
    return counts


def _overlay_detail(hidden: bool | None, when_hidden: str, when_shown: str) -> str:
    if hidden is None:
        return "#kcd-overlay が無い（WebGL テンプレートが変わった？）"
    return when_hidden if hidden else when_shown


def _frame_detail(stats: pixels.FrameStats) -> str:
    return (f"輝度 平均 {stats.mean_luma:.0f} / ばらつき {stats.luma_std:.1f}、"
            f"最多色 {stats.dominant_fraction:.0%}、色数 {stats.distinct_colors}")


def _first_line(text: str) -> str:
    return text.strip().splitlines()[0][:300] if text.strip() else text
