"""画質の段ごとの計測 (#70) の集計と, ページに入れる計測スクリプト (perf_probe.js)。

集計は数値だけで確かめる。perf_probe.js は SwiftShader のブラウザで小さな WebGL 2 のページを動かし,
requestAnimationFrame の 1 回ごとに描画コールと三角形を数えられるか, そのうち影のマップ（16 ビットの深度のテクスチャ）に
描いた分を分けられるかを確かめる（Unity のビルドは要らない）。
スマホの画面では, テンプレートを偽のローダーで開き, 案内を叩いてからの読み込みの時刻を数えられるかを確かめる。
run_webgl_perf.py の引数の組み合わせも確かめる。
_session が CPU を遅くする命令を送る順と, スマホでの段の扱い（設定画面を開くか, ポーズ中の最初の段, タップの順）は,
ブラウザの代わりに呼ばれた順を記録して確かめる。
"""

from __future__ import annotations

from contextlib import contextmanager
from pathlib import Path

import pytest

from kcd_e2e import perf
from run_webgl_perf import parse_args


def frames_at(times, work=1.0, draws=10, triangles=300):
    return [{"t": t, "work": work, "draws": draws, "triangles": triangles, "shadow_draws": 2, "shadow_triangles": 60}
            for t in times]


def test_summarize_uses_intervals_between_frames():
    frames = frames_at([0.0, 10.0, 20.0, 30.0, 40.0])
    summary = perf.summarize(frames)
    assert summary["frames"] == 5
    assert summary["seconds"] == pytest.approx(0.04)
    assert summary["fps"] == pytest.approx(100.0)
    assert summary["one_percent_low"] == pytest.approx(100.0)


def test_one_percent_low_is_fps_of_the_slowest_one_percent_of_intervals():
    # 間隔 200 個のうち 2 個（1%）だけ 50 ms。1% low はその平均から出す。
    times = [0.0]
    for i in range(200):
        times.append(times[-1] + (50.0 if i in (7, 150) else 10.0))
    summary = perf.summarize(frames_at(times))
    assert summary["one_percent_low"] == pytest.approx(20.0)
    assert summary["fps"] == pytest.approx(1000.0 / ((198 * 10.0 + 2 * 50.0) / 200))


def test_one_percent_low_takes_at_least_one_interval():
    summary = perf.summarize(frames_at([0.0, 10.0, 30.0, 40.0]))
    assert summary["one_percent_low"] == pytest.approx(50.0)


def test_summarize_work_draws_and_triangles():
    frames = [
        {"t": 0.0, "work": 2.0, "draws": 500, "triangles": 700_000, "shadow_draws": 90, "shadow_triangles": 200_000},
        {"t": 16.0, "work": 4.0, "draws": 560, "triangles": 720_000, "shadow_draws": 110, "shadow_triangles": 230_000},
        {"t": 32.0, "work": 6.0, "draws": 540, "triangles": 740_000, "shadow_draws": 100, "shadow_triangles": 260_000},
    ]
    summary = perf.summarize(frames)
    assert summary["work_ms"] == pytest.approx(4.0)
    assert summary["work_p99_ms"] == pytest.approx(6.0)
    assert summary["draws"] == pytest.approx(533.33, abs=0.01)
    assert summary["draws_max"] == 560
    assert summary["triangles"] == pytest.approx(720_000)
    assert summary["triangles_max"] == 740_000
    assert summary["shadow_draws"] == pytest.approx(100.0)
    assert summary["shadow_triangles"] == pytest.approx(230_000)


def test_p99_is_nearest_rank():
    values = list(range(1, 201))
    assert perf.percentile(values, 99) == 198
    assert perf.percentile([5.0], 99) == 5.0


def test_summarize_needs_two_frames():
    with pytest.raises(ValueError):
        perf.summarize(frames_at([0.0]))


@pytest.mark.parametrize("tier, keys", [
    ("low", ["ArrowLeft"]),
    ("medium", []),
    ("high", ["ArrowRight"]),
])
def test_tier_keys_step_from_the_web_default(tier, keys):
    assert perf.tier_keys(tier) == keys


@pytest.mark.parametrize("current, target, keys", [
    ("medium", "low", ["ArrowLeft"]),
    ("low", "high", ["ArrowLeft"]),
    ("high", "low", ["ArrowRight"]),
    ("high", "medium", ["ArrowLeft"]),
    ("low", "low", []),
])
def test_step_keys_takes_the_short_way_round(current, target, keys):
    assert perf.step_keys(current, target) == keys


def test_tier_keys_rejects_unknown_tier():
    with pytest.raises(ValueError):
        perf.tier_keys("ultra")


def test_launch_args_for_uncapped_frames():
    assert perf.uncapped_args(True) == ("--disable-gpu-vsync", "--disable-frame-rate-limit")
    assert perf.uncapped_args(False) == ()


def test_cpu_throttle_command_slows_the_cpu_by_the_rate():
    assert perf.cpu_throttle_command(4) == ("Emulation.setCPUThrottlingRate", {"rate": 4})
    assert perf.cpu_throttle_command(1) is None


def test_cpu_throttle_command_rejects_rates_below_one():
    with pytest.raises(ValueError):
        perf.cpu_throttle_command(0.5)


# ---- スマホの画面 ----

@pytest.mark.parametrize("phone, tier", [(None, "medium"), (perf.PHONES["portrait"], "low")])
def test_start_tier_is_low_on_phones(phone, tier):
    assert perf.start_tier(phone) == tier


@pytest.mark.parametrize("tier, keys", [
    ("low", []),
    ("medium", ["ArrowRight"]),
    ("high", ["ArrowLeft"]),
])
def test_tier_keys_step_from_the_phone_default(tier, keys):
    assert perf.tier_keys(tier, perf.PHONES["landscape"]) == keys


def test_context_options_for_pc_are_the_desktop_page():
    assert perf.context_options(None, (1280, 900)) == {
        "viewport": {"width": 1280, "height": 900}, "device_scale_factor": 1, "locale": "ja-JP"}


@pytest.mark.parametrize("name, size", [("portrait", (390, 844)), ("landscape", (844, 390))])
def test_context_options_for_phones_are_a_touch_phone(name, size):
    options = perf.context_options(perf.PHONES[name], (1280, 900))
    assert options["viewport"] == {"width": size[0], "height": size[1]}
    assert options["device_scale_factor"] == 3
    assert options["is_mobile"] and options["has_touch"]
    assert options["user_agent"].startswith("Mozilla/5.0") and "Android" in options["user_agent"]
    assert options["locale"] == "ja-JP"


@pytest.mark.parametrize("phone", perf.PHONES.values(), ids=list(perf.PHONES))
def test_phone_touches_are_on_screen_and_start_the_stick(phone):
    width, height = phone.viewport
    for x, y in (phone.title_tap, phone.select_tap, phone.stick_from, phone.stick_to):
        assert 0 <= x < width and 0 <= y < height
    # スティックは画面の左 45%・下から 85% の中で指を置く（UIFactory.Touch.BuildStick）。
    assert phone.stick_from[0] < 0.45 * width and phone.stick_from[1] > 0.15 * height
    # 真上へ, スティックの届く範囲より長く倒す。範囲は 90 単位で, タッチ用の Canvas は短い辺を 720 に合わせる。
    assert phone.stick_to[0] == phone.stick_from[0]
    assert phone.stick_from[1] - phone.stick_to[1] > 90 * min(width, height) / 720


def test_slide_points_go_straight_to_the_end():
    assert perf.slide_points((90, 650), (90, 530), 4) == [(90, 620), (90, 590), (90, 560), (90, 530)]
    assert perf.slide_points((0, 0), (80, 40), 4) == [(20, 10), (40, 20), (60, 30), (80, 40)]


@pytest.mark.parametrize("steps", [0, -1])
def test_slide_points_need_at_least_one_step(steps):
    with pytest.raises(ValueError):
        perf.slide_points((0, 0), (1, 1), steps)


def test_load_times_count_from_the_loader_request():
    times = perf.load_times({"loader_requested": 1200.4, "loader_loaded": 1300.2, "first_progress": 1310.6,
                             "download_done": 9000.3, "unity_ready": 15000.7})
    assert times == {"loader_requested": 1200, "loader_loaded": 1300, "first_progress": 1311,
                     "download_done": 9000, "unity_ready": 15001, "ready_after_request": 13800}


def test_load_times_without_a_start_or_an_end_have_no_duration():
    times = perf.load_times({"loader_requested": None, "loader_loaded": None, "first_progress": None,
                             "download_done": None, "unity_ready": 15000.7})
    assert times["loader_requested"] is None and times["ready_after_request"] is None
    assert perf.load_times({"loader_requested": 10.0, "unity_ready": None})["ready_after_request"] is None


# ---- 指とキーの送り方（ブラウザの代わりに, 送った順に記録する） ----

class Recorder:
    """Page と CDPSession の代わり。キー・待ち・タッチを送った順に記録する。"""

    def __init__(self):
        self.sent = []
        self.keyboard = self

    def down(self, key):
        self.sent.append(("down", key))

    def up(self, key):
        self.sent.append(("up", key))

    def press(self, key):
        self.sent.append(("press", key))

    def wait_for_timeout(self, ms):
        self.sent.append(("wait", ms))

    def send(self, method, params):
        assert method == "Input.dispatchTouchEvent"
        self.sent.append((params["type"], [(p["x"], p["y"]) for p in params["touchPoints"]]))


def test_tap_lifts_the_finger_after_holding():
    recorder = Recorder()
    perf._tap(recorder, recorder, (195, 422))
    assert recorder.sent == [("touchStart", [(195, 422)]), ("wait", perf.TAP_HOLD_MS), ("touchEnd", [])]


def test_walking_on_pc_holds_w_until_the_end():
    page = Recorder()
    with pytest.raises(RuntimeError):
        with perf._walking(page, None, None):
            assert page.sent == [("down", "w")]
            raise RuntimeError
    assert page.sent == [("down", "w"), ("up", "w")]


def test_walking_on_a_phone_keeps_the_finger_on_the_stick_until_the_end():
    phone = perf.PHONES["portrait"]
    recorder = Recorder()
    slide = [("touchStart", [phone.stick_from])]
    for point in perf.slide_points(phone.stick_from, phone.stick_to, perf.SLIDE_STEPS):
        slide += [("wait", perf.SLIDE_GAP_MS), ("touchMove", [point])]
    with pytest.raises(RuntimeError):
        with perf._walking(recorder, recorder, phone):
            assert recorder.sent == slide
            raise RuntimeError
    assert recorder.sent == slide + [("touchEnd", [])]


# ---- run_webgl_perf.py の引数 ----

def test_cli_phone_and_throttle_load_are_off_by_default():
    args = parse_args(["--serve", "build/WebGL"])
    assert args.phone is None and args.throttle_load is False and args.cpu_throttle == 1.0


def test_cli_takes_a_phone_and_a_throttle_from_the_load():
    args = parse_args(["--serve", "build/WebGL", "--phone", "landscape", "--cpu-throttle", "4",
                       "--throttle-load"])
    assert args.phone == "landscape" and args.throttle_load is True and args.cpu_throttle == 4.0


@pytest.mark.parametrize("extra", [
    ["--throttle-load"],  # 遅くする倍率が無い
    ["--throttle-load", "--cpu-throttle", "1"],  # 1 倍は掛けないのと同じ
    ["--cpu-throttle", "0.5"],
    ["--phone", "tablet"],
    ["--tiers", "low,ultra"],
])
def test_cli_rejects_bad_arguments(extra):
    with pytest.raises(SystemExit) as error:
        parse_args(["--serve", "build/WebGL", *extra])
    assert error.value.code == 2


# ---- 計測の手順（ブラウザの代わりに, 呼ばれた順に記録する） ----

URL = "http://127.0.0.1:8000/"


def options_for(phone, **overrides):
    return perf.PerfOptions(url=URL, out_dir=Path("perf-out"), launch=None,
                            phone=None if phone is None else perf.PHONES[phone], **overrides)


class FakeLocator:
    def __init__(self, calls, selector):
        self.calls = calls
        self.selector = selector

    def tap(self, **kwargs):
        self.calls.append(("tap", self.selector))

    def focus(self):
        self.calls.append(("focus", self.selector))


class FakeBrowser:
    """Browser・BrowserContext・Page・CDPSession を 1 つで兼ねる。_session が呼んだ順に記録する。"""

    def __init__(self):
        self.calls = []

    def new_context(self, **options):
        self.calls.append(("new_context", options))
        return self

    def add_init_script(self, path):
        self.calls.append(("init_script", Path(path).name))

    def new_page(self):
        self.calls.append(("new_page",))
        return self

    def on(self, event, handler):
        self.calls.append(("on", event))

    def new_cdp_session(self, page):
        self.calls.append(("cdp",))
        return self

    def send(self, method, params):
        self.calls.append(("send", method, params))

    def goto(self, url, **kwargs):
        self.calls.append(("goto", url))

    def locator(self, selector):
        return FakeLocator(self.calls, selector)

    def wait_for_function(self, expression, **kwargs):
        self.calls.append(("wait_for_function", expression))

    def close(self):
        self.calls.append(("close",))


THROTTLE = ("send", "Emulation.setCPUThrottlingRate", {"rate": 4.0})
NOTICE = ("tap", perf.DATA_NOTICE_BUTTON)
READY = [("wait_for_function", perf.READY_JS), ("drawn",)]


@pytest.mark.parametrize("phone, rate, throttle_load, expected", [
    # 開く前に掛け, 読み込みの間も遅いままにする。
    ("portrait", 4.0, True, [("cdp",), THROTTLE, ("goto", URL), NOTICE, *READY]),
    # 既定では, タイトルが描かれてから掛ける。
    (None, 4.0, False, [("cdp",), ("goto", URL), *READY, THROTTLE]),
    # PC で遅くしないなら CDP はつながない。
    (None, 1.0, False, [("goto", URL), *READY]),
    # スマホはタッチを CDP で送るので, 遅くしなくてもつなぐ。
    ("portrait", 1.0, False, [("cdp",), ("goto", URL), NOTICE, *READY]),
])
def test_session_connects_cdp_and_throttles_at_the_right_time(monkeypatch, phone, rate, throttle_load,
                                                              expected):
    browser = FakeBrowser()
    monkeypatch.setattr(perf, "_wait_drawn", lambda page, timeout_s: browser.calls.append(("drawn",)))
    options = options_for(phone, cpu_throttle=rate, throttle_load=throttle_load)
    opened = [("new_context", perf.context_options(options.phone, options.viewport)),
              ("init_script", perf.INSTRUMENT_JS.name), ("init_script", perf.PROBE_JS.name),
              ("new_page",), ("on", "console")]
    with perf._session(browser, options) as (page, console, cdp):
        assert browser.calls == opened + expected + [("focus", "#unity-canvas")]
        assert page is browser and console == []
        assert cdp is (browser if ("cdp",) in expected else None)
    assert browser.calls[-1] == ("close",)


class StepRecorder:
    """_measure_tier・_measure_frozen に渡す Page の代わり。差し替えた手順と待ちを, 呼ばれた順に記録する。"""

    def __init__(self):
        self.calls = []

    def evaluate(self, expression):
        return {perf.LOAD_JS: {}, perf.HEAP_JS: 322174976, perf.CANVAS_JS: [585, 1266]}[expression]

    def wait_for_timeout(self, ms):
        self.calls.append(("wait", ms))


@pytest.fixture
def steps(monkeypatch):
    recorder = StepRecorder()
    calls = recorder.calls

    def record(page, seconds):
        calls.append(("record", seconds))
        return {}

    @contextmanager
    def walking(page, cdp, phone):
        calls.append(("walk", "down"))
        yield
        calls.append(("walk", "up"))

    monkeypatch.setattr(perf, "_press", lambda page, keys: calls.append(("press", keys)))
    monkeypatch.setattr(perf, "_save_canvas", lambda page, path: calls.append(("save", path.name)))
    monkeypatch.setattr(perf, "_record", record)
    monkeypatch.setattr(perf, "_enter_campus", lambda page, console, cdp, options: calls.append(("enter",)))
    monkeypatch.setattr(perf, "_walking", walking)
    return recorder


@pytest.mark.parametrize("phone, tier, keys", [
    ("portrait", "low", None),  # スマホの保存が無いときの段なので, 設定画面を開かない
    ("portrait", "medium", ["ArrowRight"]),
    ("portrait", "high", ["ArrowLeft"]),
    (None, "low", ["ArrowLeft"]),
    (None, "medium", []),  # PC は段を変えなくても開く
])
def test_measure_tier_opens_the_settings_unless_a_phone_keeps_its_tier(steps, phone, tier, keys):
    result = perf._measure_tier(steps, [], None, tier, options_for(phone))
    opened = [] if keys is None else [("press", ["o"] + ["ArrowDown"] * perf.QUALITY_ROW + keys),
                                      ("save", f"{tier}-settings.png"), ("press", ["Escape"])]
    assert steps.calls == opened + [("enter",), ("record", 10.0), ("save", f"{tier}-stand.png"),
                                    ("walk", "down"), ("record", 10.0), ("walk", "up"),
                                    ("save", f"{tier}-walk.png")]
    assert result.tier == tier and result.canvas == (585, 1266)


@pytest.mark.parametrize("phone, first", [
    ("portrait", []),  # スマホは Low で始まる
    (None, ["ArrowLeft"]),  # PC は Medium で始まる
])
def test_measure_frozen_steps_from_the_tier_without_a_save(steps, phone, first):
    options = options_for(phone, frozen_rounds=1)
    records = perf._measure_frozen(steps, [], None, options)
    menu = ["Escape"] + ["ArrowDown"] * 3 + ["Enter"] + ["ArrowDown"] * perf.QUALITY_ROW
    expected = [("enter",), ("press", menu)]
    for tier, keys in zip(perf.TIERS, [first, ["ArrowRight"], ["ArrowRight"]]):
        expected += [("press", keys), ("wait", 1000), ("record", options.frozen_s),
                     ("save", f"frozen-{tier}.png")]
    assert steps.calls == expected
    assert [(index, tier) for index, tier, _ in records] == [(1, "low"), (1, "medium"), (1, "high")]


@pytest.mark.parametrize("phone", [None, "portrait", "landscape"])
def test_enter_campus_starts_the_title_then_picks_the_character(monkeypatch, phone):
    recorder = Recorder()
    monkeypatch.setattr(perf, "_wait_console",
                        lambda page, console, needle, timeout_s: recorder.sent.append(("console", needle)))
    options = options_for(phone)
    perf._enter_campus(recorder, [], recorder, options)
    if phone is None:
        title, select = [("press", "Enter")], [("press", "Enter")]
    else:
        def tap(point):
            return [("touchStart", [point]), ("wait", perf.TAP_HOLD_MS), ("touchEnd", [])]
        title, select = tap(options.phone.title_tap), tap(options.phone.select_tap)
    assert recorder.sent == (title + [("wait", perf.SELECT_WAIT_MS)] + select
                             + [("console", perf.CAMPUS_LOADED), ("wait", options.settle_s * 1000)])


# ---- perf_probe.js をブラウザで動かす ----

PAGE = """<!doctype html><canvas id="c" width="64" height="64"></canvas><script>
const gl = document.getElementById('c').getContext('webgl2');
const vs = gl.createShader(gl.VERTEX_SHADER);
gl.shaderSource(vs, '#version 300 es\\nin vec2 p; void main(){ gl_Position = vec4(p, 0.0, 1.0); }');
gl.compileShader(vs);
const fs = gl.createShader(gl.FRAGMENT_SHADER);
gl.shaderSource(fs, '#version 300 es\\nprecision mediump float; out vec4 o; void main(){ o = vec4(1.0); }');
gl.compileShader(fs);
const prog = gl.createProgram();
gl.attachShader(prog, vs); gl.attachShader(prog, fs); gl.linkProgram(prog); gl.useProgram(prog);
const buf = gl.createBuffer();
gl.bindBuffer(gl.ARRAY_BUFFER, buf);
gl.bufferData(gl.ARRAY_BUFFER, new Float32Array(24), gl.STATIC_DRAW);
gl.enableVertexAttribArray(0); gl.vertexAttribPointer(0, 2, gl.FLOAT, false, 0, 0);
const idx = gl.createBuffer();
gl.bindBuffer(gl.ELEMENT_ARRAY_BUFFER, idx);
gl.bufferData(gl.ELEMENT_ARRAY_BUFFER, new Uint16Array([0, 1, 2, 3, 4, 5]), gl.STATIC_DRAW);
function texture(allocate) {
  const made = gl.createTexture();
  gl.bindTexture(gl.TEXTURE_2D, made);
  allocate();
  return made;
}
function withDepth(depth) {
  const made = gl.createFramebuffer();
  gl.bindFramebuffer(gl.FRAMEBUFFER, made);
  gl.framebufferTexture2D(gl.FRAMEBUFFER, gl.DEPTH_ATTACHMENT, gl.TEXTURE_2D, depth, 0);
  return made;
}
// 影のマップ: 16 ビットの深度のテクスチャ（texStorage2D と texImage2D の両方で確保する）。
const shadowA = withDepth(texture(() => gl.texStorage2D(gl.TEXTURE_2D, 1, gl.DEPTH_COMPONENT16, 64, 64)));
const shadowB = withDepth(texture(() => gl.texImage2D(
  gl.TEXTURE_2D, 0, gl.DEPTH_COMPONENT16, 64, 64, 0, gl.DEPTH_COMPONENT, gl.UNSIGNED_SHORT, null)));
// 影のマップではないもの: 色と 24 ビットの深度のカメラ, 16 ビットの深度のテクスチャを renderbuffer に付け替えたもの。
const camera = withDepth(texture(() => gl.texStorage2D(gl.TEXTURE_2D, 1, gl.DEPTH_COMPONENT24, 64, 64)));
gl.framebufferTexture2D(gl.FRAMEBUFFER, gl.COLOR_ATTACHMENT0, gl.TEXTURE_2D,
  texture(() => gl.texStorage2D(gl.TEXTURE_2D, 1, gl.RGBA8, 64, 64)), 0);
const swapped = withDepth(texture(() => gl.texStorage2D(gl.TEXTURE_2D, 1, gl.DEPTH_COMPONENT16, 64, 64)));
const depthBuffer = gl.createRenderbuffer();
gl.bindRenderbuffer(gl.RENDERBUFFER, depthBuffer);
gl.renderbufferStorage(gl.RENDERBUFFER, gl.DEPTH_COMPONENT16, 64, 64);
gl.framebufferRenderbuffer(gl.FRAMEBUFFER, gl.DEPTH_ATTACHMENT, gl.RENDERBUFFER, depthBuffer);
// 1 フレームに: 影のマップへ 2 回（三角形 2 枚と, 索引で 1 枚）, カメラと付け替えたものへ 1 枚ずつ,
// 画面へ 三角形 2 枚（6 頂点）, ストリップ 3 枚（5 頂点）, 索引で 2 枚, 4 インスタンス × 1 枚, 線（数えない）。
function frame() {
  gl.bindFramebuffer(gl.FRAMEBUFFER, shadowA);
  gl.drawArrays(gl.TRIANGLES, 0, 6);
  gl.bindFramebuffer(gl.DRAW_FRAMEBUFFER, shadowB);
  gl.drawElements(gl.TRIANGLES, 3, gl.UNSIGNED_SHORT, 0);
  gl.bindFramebuffer(gl.FRAMEBUFFER, camera);
  gl.drawArrays(gl.TRIANGLES, 0, 3);
  gl.bindFramebuffer(gl.FRAMEBUFFER, swapped);
  gl.drawArrays(gl.TRIANGLES, 0, 3);
  gl.bindFramebuffer(gl.FRAMEBUFFER, null);
  gl.drawArrays(gl.TRIANGLES, 0, 6);
  gl.drawArrays(gl.TRIANGLE_STRIP, 0, 5);
  gl.drawElements(gl.TRIANGLES, 6, gl.UNSIGNED_SHORT, 0);
  gl.drawArraysInstanced(gl.TRIANGLES, 0, 3, 4);
  gl.drawArrays(gl.LINES, 0, 2);
  requestAnimationFrame(frame);
}
requestAnimationFrame(frame);
</script>"""


@pytest.fixture(scope="module")
def browser():
    pytest.importorskip("playwright.sync_api")
    from playwright.sync_api import Error as PlaywrightError
    from playwright.sync_api import sync_playwright

    from kcd_e2e.browser import default_browser, launch_config

    config = launch_config(default_browser(), headless=True, gl="software")
    with sync_playwright() as playwright:
        try:
            launched = playwright.chromium.launch(**config.launch_kwargs())
        except PlaywrightError as error:
            pytest.skip(f"ブラウザを起動できない: {error}")
        try:
            yield launched
        finally:
            launched.close()


@pytest.fixture(scope="module")
def page(browser):
    context = browser.new_context()
    context.add_init_script(path=str(perf.PROBE_JS))
    tab = context.new_page()
    tab.set_content(PAGE)
    yield tab
    context.close()


def test_probe_counts_draws_and_triangles_per_frame(page):
    page.evaluate(perf.START_JS)
    page.wait_for_timeout(500)
    frames = page.evaluate(perf.STOP_JS)
    assert len(frames) >= 5
    # 最後のフレームは描き終わる前に止めたかもしれないので除く。
    for frame in frames[:-1]:
        assert frame["draws"] == 2 + 2 + 5
        assert frame["triangles"] == (2 + 1) + (1 + 1) + (2 + 3 + 2 + 4)
        assert frame["work"] >= 0.0
    times = [frame["t"] for frame in frames]
    assert times == sorted(times) and len(set(times)) == len(times)


def test_probe_counts_only_draws_into_16_bit_depth_textures_as_shadow_map(page):
    page.evaluate(perf.START_JS)
    page.wait_for_timeout(500)
    frames = page.evaluate(perf.STOP_JS)
    assert len(frames) >= 5
    for frame in frames[:-1]:
        assert frame["shadow_draws"] == 2
        assert frame["shadow_triangles"] == 2 + 1


def test_probe_stops_recording(page):
    page.evaluate(perf.START_JS)
    page.wait_for_timeout(200)
    first = page.evaluate(perf.STOP_JS)
    page.wait_for_timeout(200)
    again = page.evaluate(perf.STOP_JS)
    assert first and again == []


# ---- スマホの画面で, 案内を叩いてからの読み込みの時刻を数える ----

FAKE_LOADER = Path(__file__).with_name("fake_unity_loader.js").read_text(encoding="utf-8")
TIMEOUT_MS = 10_000


@pytest.fixture(scope="module")
def template_site(tmp_path_factory):
    from kcd_e2e.server import serve_build
    from kcd_e2e.template import write_template_build

    root = write_template_build(tmp_path_factory.mktemp("kcd-template") / "WebGL")
    with serve_build(root) as base:
        yield base


def test_phone_loads_after_the_notice_and_times_count_from_the_loader(browser, template_site):
    from kcd_e2e.template import LOADER_FILENAME

    context = browser.new_context(**perf.context_options(perf.PHONES["portrait"], (1280, 900)))
    try:
        context.add_init_script(path=str(perf.INSTRUMENT_JS))
        context.add_init_script("window.__kcdFakeScenario = 'success';")
        tab = context.new_page()
        tab.route(f"**/Build/{LOADER_FILENAME}", lambda route: route.fulfill(
            status=200, content_type="application/javascript", body=FAKE_LOADER))
        tab.goto(template_site, wait_until="domcontentloaded")
        assert tab.evaluate("() => devicePixelRatio") == perf.PHONE_DPR
        # 指だけの端末では「読み込む」を叩くまでローダーを取りに行かない。
        tab.wait_for_timeout(300)
        assert tab.evaluate(perf.LOAD_JS)["loader_requested"] is None
        tab.locator(perf.DATA_NOTICE_BUTTON).tap()
        tab.wait_for_function("() => window.__kcdFake && window.__kcdFake.release", timeout=TIMEOUT_MS)
        tab.evaluate("() => window.__kcdFake.release()")
        tab.wait_for_function(perf.READY_JS, timeout=TIMEOUT_MS)
        times = perf.load_times(tab.evaluate(perf.LOAD_JS))
        order = [times[key] for key in
                 ("loader_requested", "loader_loaded", "first_progress", "download_done", "unity_ready")]
        assert None not in order and order == sorted(order), times
        assert times["ready_after_request"] > 0
        assert tab.evaluate("() => document.querySelector('#unity-canvas').classList.contains('unity-mobile')")
    finally:
        context.close()
