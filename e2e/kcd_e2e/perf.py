"""画質の段（Low / Medium / High）ごとに, ブラウザ版の fps・1% low・描画コール・三角形を測る (#70)。

段ごとに新しいプロファイルで開き（保存が無いので PC の Web 版は Medium で始まる）, タイトルの設定画面で段を
変えてからキャンパスに入る。キャンパスでは, 同じ地点に立つ（stand）場合と, 前へ歩き続ける（walk）場合を
それぞれ measure_s 秒ずつ記録する。立ち・歩きは NPC と時計が動くので, 回ごとに描く物が少し変わる。
そこで最後に 1 つのセッションでポーズし, 時間が止まった同じ絵のまま段だけを切り替えて, 段の差を測る。
数え方は perf_probe.js, 集計は summarize() を見る。

phone を渡すと, スマホの画面（PHONES の縦・横, DPR 3, タッチ, Android の UA）で開く。テンプレートは指だけの
端末に通信量の案内を出し,「読み込む」を叩くまで読み込まないので, それを叩いてから待つ。タイトルと選択は
タップで進め, 歩きは左下のスティックを指で倒したままにする。保存が無いときの段はスマホでは Low なので,
段はそこから動かす。
"""

from __future__ import annotations

import json
import math
import time
from collections.abc import Iterator
from contextlib import contextmanager
from dataclasses import dataclass, field
from pathlib import Path

from playwright.sync_api import Browser, CDPSession, Page, sync_playwright

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
# 読み込みの時刻（ページを開いてからの ms）。ローダーを取りに行った時刻は resource timing から取る。
LOAD_JS = """() => {
  const s = window.__kcdE2E;
  const loader = performance.getEntriesByType('resource')
    .find((e) => new URL(e.name).pathname.endsWith('.loader.js'));
  return {
    loader_requested: loader ? loader.startTime : null,
    loader_loaded: s ? s.loaderLoadedAt : null,
    first_progress: s ? s.firstProgressAt : null,
    download_done: s ? s.progressDoneAt : null,
    unity_ready: s ? s.readyAt : null,
  };
}"""

TIERS = ("low", "medium", "high")
# 保存が無いときの PC の Web 版の段。設定画面の左右で, Low ← Medium → High と 1 つずつ動く。
WEB_DEFAULT_TIER = "medium"
# 設定画面の「画質」の行（SettingsView.RowQuality）。開くと一番上の行が選ばれている。
QUALITY_ROW = 4
# 上限なしで測るときの Edge の引数（docs/WEBGL_BUDGET.md 1.1）。
UNCAPPED_ARGS = ("--disable-gpu-vsync", "--disable-frame-rate-limit")
CAMPUS_LOADED = "[KCD] scene_load scene=Campus"
KEY_GAP_MS = 250
# タイトルをはじめてから, キャラクターの選択を受け付けるまで待つ。
SELECT_WAIT_MS = 2000

# スマホ（Application.isMobilePlatform）では, 保存が無いときの段が Low になる（QualityTier.DefaultFor）。
PHONE_DEFAULT_TIER = "low"
PHONE_DPR = 3
PHONE_UA = ("Mozilla/5.0 (Linux; Android 14; Pixel 8) AppleWebKit/537.36 (KHTML, like Gecko) "
            "Chrome/129.0.0.0 Mobile Safari/537.36")
# 指だけの端末でテンプレートが出す, 通信量の案内の「読み込む」。
DATA_NOTICE_BUTTON = "#kcd-data-notice-continue"
# 指を置いてから離すまで。CPU を遅くしても, タップが 2 フレーム以上に掛かるようにする。
TAP_HOLD_MS = 150
# スティックを倒すときに指を動かす回数と間隔。
SLIDE_STEPS = 8
SLIDE_GAP_MS = 16


@dataclass(frozen=True)
class Phone:
    """スマホの画面の大きさ（CSS の px）と, そこで指を置く位置。"""
    name: str
    viewport: tuple[int, int]
    # タイトルは, セーブが無ければどこを叩いてもはじめる。キャラクターの選択は名前を叩くと決まる。
    title_tap: tuple[float, float]
    select_tap: tuple[float, float]
    # 歩くときにスティックを倒す指の始点と終点。スティックは画面の左 45%・下から 85% の中で,
    # 指を置いた所を中心に倒れる（UIFactory.Touch.BuildStick）。終点を上にすると前へ歩く。
    stick_from: tuple[float, float]
    stick_to: tuple[float, float]


PHONES = {
    "portrait": Phone("portrait", (390, 844), title_tap=(195, 422), select_tap=(195, 696),
                      stick_from=(90, 650), stick_to=(90, 530)),
    "landscape": Phone("landscape", (844, 390), title_tap=(422, 195), select_tap=(400, 257),
                       stick_from=(150, 300), stick_to=(150, 180)),
}


def uncapped_args(uncapped: bool) -> tuple[str, ...]:
    return UNCAPPED_ARGS if uncapped else ()


def cpu_throttle_command(rate: float) -> tuple[str, dict[str, float]] | None:
    """遅い CPU を真似る CDP の命令。rate 倍だけメインスレッドを遅くする。1 なら掛けない。"""
    if rate < 1:
        raise ValueError(f"CPU を遅くする倍率は 1 以上: {rate}")
    if rate == 1:
        return None
    return "Emulation.setCPUThrottlingRate", {"rate": rate}


def context_options(phone: Phone | None, viewport: tuple[int, int]) -> dict:
    """ブラウザの文脈の設定。PC は viewport の大きさで DPR 1, スマホは phone の画面・DPR 3・タッチ・Android の UA。"""
    if phone is None:
        return {"viewport": {"width": viewport[0], "height": viewport[1]}, "device_scale_factor": 1,
                "locale": "ja-JP"}
    return {"viewport": {"width": phone.viewport[0], "height": phone.viewport[1]},
            "device_scale_factor": PHONE_DPR, "is_mobile": True, "has_touch": True,
            "user_agent": PHONE_UA, "locale": "ja-JP"}


def slide_points(start: tuple[float, float], end: tuple[float, float], steps: int) -> list[tuple[float, float]]:
    """start から end まで指を動かすときの, start の次から end までの steps 個の位置。"""
    if steps < 1:
        raise ValueError(f"steps は 1 以上: {steps}")
    return [(start[0] + (end[0] - start[0]) * k / steps, start[1] + (end[1] - start[1]) * k / steps)
            for k in range(1, steps + 1)]


def load_times(raw: dict) -> dict:
    """LOAD_JS の時刻を ms に丸め, ローダーを取りに行ってから Unity が立ち上がるまで（ready_after_request）を足す。

    スマホのテンプレートは「読み込む」を叩くまで読み込まないので, 叩くまでの時間を除くためにローダーから数える。
    """
    times = {key: None if value is None else round(value) for key, value in raw.items()}
    start, ready = raw.get("loader_requested"), raw.get("unity_ready")
    times["ready_after_request"] = None if start is None or ready is None else round(ready - start)
    return times


def percentile(values: list[float], percent: float) -> float:
    """最も近い順位の方法の百分位数。"""
    ordered = sorted(values)
    rank = max(1, math.ceil(percent / 100 * len(ordered)))
    return ordered[rank - 1]


def summarize(frames: list[dict]) -> dict:
    """perf_probe.js のフレームの配列を, fps・1% low・処理時間・描画コール・三角形にまとめる。

    fps は間隔の平均から, 1% low は間隔の遅い 1%（少なくとも 1 個）の平均から出す。
    shadow_draws と shadow_triangles は, 描画コールと三角形のうち影のマップに描いた分の平均。
    """
    if len(frames) < 2:
        raise ValueError("フレームが 2 つ以上いる")
    times = [frame["t"] for frame in frames]
    intervals = [later - earlier for earlier, later in zip(times, times[1:])]
    slowest = sorted(intervals, reverse=True)[:max(1, math.ceil(len(intervals) * 0.01))]
    work = [frame["work"] for frame in frames]
    draws = [frame["draws"] for frame in frames]
    tris = [frame["triangles"] for frame in frames]
    shadow_draws = [frame["shadow_draws"] for frame in frames]
    shadow_tris = [frame["shadow_triangles"] for frame in frames]
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
        "shadow_draws": sum(shadow_draws) / len(shadow_draws),
        "shadow_triangles": sum(shadow_tris) / len(shadow_tris),
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
    # CPU の遅さ（倍）。既定では読み込みが済んでから, 測る間だけ掛ける。throttle_load なら開く前から掛け,
    # 読み込みの時間にも効かせる。CDP の遅さはページのメインスレッドに掛かり, Worker には掛からない。
    cpu_throttle: float = 1.0
    throttle_load: bool = False
    # スマホの画面とタッチで測る。None なら PC（viewport の大きさ, DPR 1, キーボード）。
    phone: Phone | None = None


@dataclass
class TierResult:
    tier: str
    scenes: dict[str, dict] = field(default_factory=dict)
    heap_title: int | None = None
    heap_campus: int | None = None
    canvas: tuple[int, int] | None = None
    # 起動時の「[KCD] graphics tier=」の行。設定画面で段を変える前の値で, 変えたあとには出ない。
    startup_log: str | None = None
    # load_times() の時刻（ms）。
    load: dict | None = None

    def as_dict(self) -> dict:
        return {"tier": self.tier, "scenes": self.scenes, "heap_title": self.heap_title,
                "heap_campus": self.heap_campus, "canvas": self.canvas, "startup_log": self.startup_log,
                "load": self.load}


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


def start_tier(phone: Phone | None) -> str:
    """保存が無いときの段。スマホは Low, PC の Web 版は Medium。"""
    return WEB_DEFAULT_TIER if phone is None else PHONE_DEFAULT_TIER


def tier_keys(tier: str, phone: Phone | None = None) -> list[str]:
    """保存が無いときの段から tier へ動かすための, 設定画面の「画質」の行で押すキー。"""
    return step_keys(start_tier(phone), tier)


def run_perf(options: PerfOptions) -> PerfResult:
    """段ごとに新しいプロファイルで立ち・歩きを測り, そのあと 1 つのセッションでポーズ中の段の差を測る。"""
    options.out_dir.mkdir(parents=True, exist_ok=True)
    result = PerfResult()
    with sync_playwright() as playwright:
        browser = playwright.chromium.launch(**options.launch.launch_kwargs())
        try:
            for tier in options.tiers:
                with _session(browser, options) as (page, console, cdp):
                    result.sessions.append(_measure_tier(page, console, cdp, tier, options))
            if options.frozen_rounds > 0:
                with _session(browser, options) as (page, console, cdp):
                    result.frozen = _measure_frozen(page, console, cdp, options)
        finally:
            browser.close()
    payload = {"url": options.url, "launch": list(options.launch.args), "cpu_throttle": options.cpu_throttle,
               "throttle_load": options.throttle_load, "phone": options.phone.name if options.phone else None,
               **result.as_dict()}
    (options.out_dir / "perf.json").write_text(json.dumps(payload, ensure_ascii=False, indent=2),
                                               encoding="utf-8")
    return result


@contextmanager
def _session(browser: Browser, options: PerfOptions) -> Iterator[tuple[Page, list[str], CDPSession | None]]:
    """新しいプロファイルで開き, タイトルが描かれるまで待つ（保存が無いので段は start_tier）。

    CDP はタッチか CPU の遅さに使うときだけつなぐ（PC で遅さを掛けないなら None）。
    """
    context = browser.new_context(**context_options(options.phone, options.viewport))
    try:
        context.add_init_script(path=str(INSTRUMENT_JS))
        context.add_init_script(path=str(PROBE_JS))
        page = context.new_page()
        console: list[str] = []
        page.on("console", lambda msg: console.append(msg.text))
        throttle = cpu_throttle_command(options.cpu_throttle)
        cdp = context.new_cdp_session(page) if options.phone is not None or throttle is not None else None
        # 開く前に掛けた遅さは, 開いたあとのページにも掛かったままになる。
        if throttle is not None and options.throttle_load:
            cdp.send(*throttle)
        page.goto(options.url, wait_until="domcontentloaded", timeout=options.load_timeout_s * 1000)
        if options.phone is not None:
            # 案内は HTML のボタンなので, 出るのを待って Playwright で叩く。キャンバスの中は指を置いたままにしたり
            # 動かしたりするので CDP で送る（_tap・_slide）。
            page.locator(DATA_NOTICE_BUTTON).tap(timeout=options.scene_timeout_s * 1000)
        page.wait_for_function(READY_JS, timeout=options.load_timeout_s * 1000, polling=250)
        _wait_drawn(page, options.scene_timeout_s)
        if throttle is not None and not options.throttle_load:
            cdp.send(*throttle)
        # クリックはしない。Unity がマウスをロックし, 手元のマウスを取ってしまう。キーはフォーカスで届く。
        page.locator("#unity-canvas").focus()
        yield page, console, cdp
    finally:
        context.close()


def _enter_campus(page: Page, console: list[str], cdp: CDPSession | None, options: PerfOptions) -> None:
    if options.phone is None:
        page.keyboard.press("Enter")
        page.wait_for_timeout(SELECT_WAIT_MS)
        page.keyboard.press("Enter")
    else:
        _tap(page, cdp, options.phone.title_tap)
        page.wait_for_timeout(SELECT_WAIT_MS)
        _tap(page, cdp, options.phone.select_tap)
    _wait_console(page, console, CAMPUS_LOADED, options.scene_timeout_s)
    page.wait_for_timeout(options.settle_s * 1000)


def _measure_tier(page: Page, console: list[str], cdp: CDPSession | None, tier: str,
                  options: PerfOptions) -> TierResult:
    result = TierResult(tier)
    result.startup_log = next((line for line in console if "[KCD] graphics tier=" in line), None)
    result.load = load_times(page.evaluate(LOAD_JS))
    result.heap_title = page.evaluate(HEAP_JS)
    keys = tier_keys(tier, options.phone)
    # 設定画面はキーで開く。キーを押すと HUD はキーボードの表示になり, 次のタップで指の表示に戻る。
    # スマホで段を変えないときは開かない。
    if options.phone is None or keys:
        _press(page, ["o"] + ["ArrowDown"] * QUALITY_ROW + keys)
        _save_canvas(page, options.out_dir / f"{tier}-settings.png")
        _press(page, ["Escape"])
    _enter_campus(page, console, cdp, options)
    result.heap_campus = page.evaluate(HEAP_JS)
    result.canvas = tuple(page.evaluate(CANVAS_JS))

    result.scenes["stand"] = _record(page, options.measure_s)
    _save_canvas(page, options.out_dir / f"{tier}-stand.png")
    with _walking(page, cdp, options.phone):
        result.scenes["walk"] = _record(page, options.measure_s)
    _save_canvas(page, options.out_dir / f"{tier}-walk.png")
    return result


def _measure_frozen(page: Page, console: list[str], cdp: CDPSession | None,
                    options: PerfOptions) -> list[tuple[int, str, dict]]:
    """キャンパスでポーズし（時間が止まり, NPC も時計も動かない）, 設定画面の「画質」の行で段を順に変えて測る。

    絵は段の違いのほかは変わらないので, 描画コールと三角形の差がそのまま段の差になる。設定画面の分の描画は
    どの段にも同じだけ入る。ポーズ中はゲームの更新が軽くなるので, fps は遊んでいるときより高めに出る。
    """
    _enter_campus(page, console, cdp, options)
    # ポーズの一覧で「設定」は 4 行目（PauseMenu.Execute の case 3）。
    _press(page, ["Escape"] + ["ArrowDown"] * 3 + ["Enter"] + ["ArrowDown"] * QUALITY_ROW)
    records = []
    current = start_tier(options.phone)
    for round_index in range(1, options.frozen_rounds + 1):
        for tier in options.tiers:
            _press(page, step_keys(current, tier))
            current = tier
            page.wait_for_timeout(1000)
            records.append((round_index, tier, _record(page, options.frozen_s)))
            if round_index == 1:
                _save_canvas(page, options.out_dir / f"frozen-{tier}.png")
    return records


@contextmanager
def _walking(page: Page, cdp: CDPSession | None, phone: Phone | None) -> Iterator[None]:
    """抜けるまで前へ歩かせる。PC は w を押したまま, スマホはスティックを倒した指を置いたままにする。"""
    if phone is None:
        page.keyboard.down("w")
    else:
        _slide(page, cdp, phone.stick_from, phone.stick_to)
    try:
        yield
    finally:
        if phone is None:
            page.keyboard.up("w")
        else:
            _touch(cdp, "touchEnd")


def _press(page: Page, keys: list[str]) -> None:
    for key in keys:
        page.keyboard.press(key)
        page.wait_for_timeout(KEY_GAP_MS)


def _touch(cdp: CDPSession, kind: str, point: tuple[float, float] | None = None) -> None:
    """指 1 本のタッチを CDP で送る。touchEnd は指を離すので位置が要らない。"""
    points = [] if point is None else [{"x": point[0], "y": point[1], "id": 0}]
    cdp.send("Input.dispatchTouchEvent", {"type": kind, "touchPoints": points})


def _tap(page: Page, cdp: CDPSession, point: tuple[float, float]) -> None:
    _touch(cdp, "touchStart", point)
    page.wait_for_timeout(TAP_HOLD_MS)
    _touch(cdp, "touchEnd")


def _slide(page: Page, cdp: CDPSession, start: tuple[float, float], end: tuple[float, float]) -> None:
    """指を start に置いて end まで動かす。指は離さない（離すのは呼んだ側）。"""
    _touch(cdp, "touchStart", start)
    for point in slide_points(start, end, SLIDE_STEPS):
        page.wait_for_timeout(SLIDE_GAP_MS)
        _touch(cdp, "touchMove", point)


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
