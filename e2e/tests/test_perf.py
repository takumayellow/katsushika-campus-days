"""画質の段ごとの計測 (#70) の集計と, ページに入れる計測スクリプト (perf_probe.js)。

集計は数値だけで確かめる。perf_probe.js は SwiftShader のブラウザで小さな WebGL 2 のページを動かし,
requestAnimationFrame の 1 回ごとに描画コールと三角形を数えられるかを確かめる（Unity のビルドは要らない）。
"""

from __future__ import annotations

import pytest

from kcd_e2e import perf


def frames_at(times, work=1.0, draws=10, triangles=300):
    return [{"t": t, "work": work, "draws": draws, "triangles": triangles} for t in times]


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
        {"t": 0.0, "work": 2.0, "draws": 500, "triangles": 700_000},
        {"t": 16.0, "work": 4.0, "draws": 560, "triangles": 720_000},
        {"t": 32.0, "work": 6.0, "draws": 540, "triangles": 740_000},
    ]
    summary = perf.summarize(frames)
    assert summary["work_ms"] == pytest.approx(4.0)
    assert summary["work_p99_ms"] == pytest.approx(6.0)
    assert summary["draws"] == pytest.approx(533.33, abs=0.01)
    assert summary["draws_max"] == 560
    assert summary["triangles"] == pytest.approx(720_000)
    assert summary["triangles_max"] == 740_000


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
// 1 フレームに: 三角形 2 枚（6 頂点）, ストリップ 3 枚（5 頂点）, 索引で 2 枚, 4 インスタンス × 1 枚, 線（数えない）。
function frame() {
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
def page():
    pytest.importorskip("playwright.sync_api")
    from playwright.sync_api import Error as PlaywrightError
    from playwright.sync_api import sync_playwright

    from kcd_e2e.browser import default_browser, launch_config

    config = launch_config(default_browser(), headless=True, gl="software")
    with sync_playwright() as playwright:
        try:
            browser = playwright.chromium.launch(**config.launch_kwargs())
        except PlaywrightError as error:
            pytest.skip(f"ブラウザを起動できない: {error}")
        try:
            context = browser.new_context()
            context.add_init_script(path=str(perf.PROBE_JS))
            tab = context.new_page()
            tab.set_content(PAGE)
            yield tab
        finally:
            browser.close()


def test_probe_counts_draws_and_triangles_per_frame(page):
    page.evaluate(perf.START_JS)
    page.wait_for_timeout(500)
    frames = page.evaluate(perf.STOP_JS)
    assert len(frames) >= 5
    # 最後のフレームは描き終わる前に止めたかもしれないので除く。
    for frame in frames[:-1]:
        assert frame["draws"] == 5
        assert frame["triangles"] == 2 + 3 + 2 + 4
        assert frame["work"] >= 0.0
    times = [frame["t"] for frame in frames]
    assert times == sorted(times) and len(set(times)) == len(times)


def test_probe_stops_recording(page):
    page.evaluate(perf.START_JS)
    page.wait_for_timeout(200)
    first = page.evaluate(perf.STOP_JS)
    page.wait_for_timeout(200)
    again = page.evaluate(perf.STOP_JS)
    assert first and again == []
