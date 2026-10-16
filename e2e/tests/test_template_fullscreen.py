"""スマホで全画面にする手立て (#187)。

スマホでは下の案内（全画面ボタンを含む）を隠し、キャンバスの中の案内も指で触ると出さないので、
全画面にする手立てが無かった。テンプレートは次のようにする:

- 要素の全画面に対応する端末（Android の Chrome など）: 「全画面で読み込む」のタップで全画面にし、
  抜けたあとはキャンバスをタップすると戻す。マウスのクリックでは全画面にしない。
- 対応しない端末（iPhone の Safari）: ホーム画面に追加して開く案内を、読み込む前の画面に出す。
  ホーム画面から開いたときは出さない。そのための manifest とアイコンを配る。

テンプレートを kcd_e2e.template で展開し、ローダーは偽物（fake_unity_loader.js）に差し替える。
Unity のビルドは要らない。
"""

from __future__ import annotations

import json
from pathlib import Path

import pytest

pytest.importorskip("playwright.sync_api")

from playwright.sync_api import sync_playwright  # noqa: E402

from kcd_e2e.browser import default_browser, launch_config  # noqa: E402
from kcd_e2e.server import serve_build  # noqa: E402
from kcd_e2e.template import LOADER_FILENAME, write_template_build  # noqa: E402

FAKE_LOADER = Path(__file__).with_name("fake_unity_loader.js").read_text(encoding="utf-8")
TIMEOUT_MS = 10_000

ANDROID_UA = ("Mozilla/5.0 (Linux; Android 14; Pixel 8) AppleWebKit/537.36 (KHTML, like Gecko) "
              "Chrome/129.0.0.0 Mobile Safari/537.36")
IPHONE_UA = ("Mozilla/5.0 (iPhone; CPU iPhone OS 18_0 like Mac OS X) AppleWebKit/605.1.15 "
             "(KHTML, like Gecko) Version/18.0 Mobile/15E148 Safari/604.1")

# iPhone の Safari と同じく、要素の全画面を持たないことにする。
NO_ELEMENT_FULLSCREEN_JS = """
delete Element.prototype.requestFullscreen;
delete Element.prototype.webkitRequestFullscreen;
"""
STANDALONE_JS = "Object.defineProperty(Navigator.prototype, 'standalone', { get: () => true });"

FULLSCREEN_ID_JS = "() => document.fullscreenElement ? document.fullscreenElement.id : null"
GATE_JS = """() => ({
  button: document.querySelector('#kcd-data-notice-continue').textContent,
  hint: !document.querySelector('#kcd-home-screen-hint').hidden,
})"""


@pytest.fixture(scope="module")
def browser():
    config = launch_config(default_browser(), headless=True, gl="software")
    with sync_playwright() as playwright:
        launched = playwright.chromium.launch(**config.launch_kwargs())
        try:
            yield launched
        finally:
            launched.close()


@pytest.fixture(scope="module")
def site(tmp_path_factory):
    root = write_template_build(tmp_path_factory.mktemp("kcd-template") / "WebGL")
    with serve_build(root) as base:
        yield base


def open_page(browser, url: str, *, user_agent: str | None = None, touch: bool = True,
              init_scripts: tuple[str, ...] = ()):
    """偽のローダーを配るページを開く。偽のローダーは release() まで起動を止めるので、すぐ起動させる。"""
    options = {"viewport": {"width": 390, "height": 844}, "is_mobile": True, "has_touch": True,
               "user_agent": user_agent} if touch else {"viewport": {"width": 1280, "height": 800}}
    context = browser.new_context(**{k: v for k, v in options.items() if v is not None})
    page = context.new_page()
    page.page_errors = []
    page.on("pageerror", lambda error: page.page_errors.append(f"{error.name}: {error.message}"))
    page.add_init_script("window.__kcdFakeScenario = 'success';")
    for script in init_scripts:
        page.add_init_script(script)
    page.route(f"**/Build/{LOADER_FILENAME}", lambda route: route.fulfill(
        status=200, content_type="application/javascript", body=FAKE_LOADER))
    page.goto(url, wait_until="domcontentloaded")
    return context, page


def start_game(page) -> None:
    page.wait_for_function("() => window.__kcdFake && window.__kcdFake.release", timeout=TIMEOUT_MS)
    page.evaluate("() => window.__kcdFake.release()")
    page.wait_for_function("() => window.__kcdFake.ready", timeout=TIMEOUT_MS)


def wait_fullscreen(page, expected: str | None) -> None:
    page.wait_for_function(f"() => ({FULLSCREEN_ID_JS})() === {json.dumps(expected)}", timeout=TIMEOUT_MS)


def test_load_tap_enters_fullscreen(browser, site):
    context, page = open_page(browser, site, user_agent=ANDROID_UA)
    try:
        assert page.evaluate(GATE_JS) == {"button": "全画面で読み込む", "hint": False}
        page.tap("#kcd-data-notice-continue")
        wait_fullscreen(page, "unity-fullscreen-container")
        assert page.page_errors == []
    finally:
        context.close()


def test_tapping_the_game_returns_to_fullscreen(browser, site):
    context, page = open_page(browser, site, user_agent=ANDROID_UA)
    try:
        page.tap("#kcd-data-notice-continue")
        start_game(page)
        wait_fullscreen(page, "unity-fullscreen-container")
        page.evaluate("() => document.exitFullscreen()")
        wait_fullscreen(page, None)
        page.tap("#unity-canvas")
        wait_fullscreen(page, "unity-fullscreen-container")
        assert page.page_errors == []
    finally:
        context.close()


def test_fullscreen_box_fills_the_screen(browser, site):
    # スマホ用の高さ（100dvh から切り欠きの分を引いたもの）が全画面の 100% より強く効かないこと。
    context, page = open_page(browser, site, user_agent=ANDROID_UA)
    try:
        page.tap("#kcd-data-notice-continue")
        wait_fullscreen(page, "unity-fullscreen-container")
        box = page.evaluate("""() => {
          const r = document.querySelector('#unity-fullscreen-container').getBoundingClientRect();
          const c = document.querySelector('#unity-canvas').getBoundingClientRect();
          return { box: [r.width, r.height], canvas: [c.width, c.height], screen: [innerWidth, innerHeight] };
        }""")
        assert box["box"] == box["screen"], box
        assert box["canvas"] == box["screen"], box
    finally:
        context.close()


def test_iphone_shows_how_to_play_full_screen(browser, site):
    context, page = open_page(browser, site, user_agent=IPHONE_UA, init_scripts=(NO_ELEMENT_FULLSCREEN_JS,))
    try:
        assert page.evaluate(GATE_JS) == {"button": "読み込む", "hint": True}
        page.tap("#kcd-data-notice-continue")
        start_game(page)
        page.tap("#unity-canvas")
        assert page.evaluate(FULLSCREEN_ID_JS) is None
        assert page.page_errors == []
    finally:
        context.close()


def test_iphone_opened_from_the_home_screen_has_no_hint(browser, site):
    context, page = open_page(browser, site, user_agent=IPHONE_UA,
                              init_scripts=(NO_ELEMENT_FULLSCREEN_JS, STANDALONE_JS))
    try:
        assert page.evaluate(GATE_JS) == {"button": "読み込む", "hint": False}
    finally:
        context.close()


def test_mouse_click_does_not_enter_fullscreen(browser, site):
    # PC は全画面ボタンと Ctrl+F で全画面にする。キャンバスのクリック（マウス視点）で勝手に広げない。
    context, page = open_page(browser, site, touch=False)
    try:
        start_game(page)
        page.click("#unity-canvas")
        page.wait_for_timeout(300)
        assert page.evaluate(FULLSCREEN_ID_JS) is None
        assert page.page_errors == []
    finally:
        context.close()


def test_manifest_opens_the_game_full_screen(browser, site):
    context, page = open_page(browser, site, touch=False)
    try:
        info = page.evaluate("""async () => {
          const link = document.querySelector('link[rel="manifest"]');
          const manifest = await (await fetch(link.href)).json();
          const icon = new URL(manifest.icons[0].src, link.href);
          const touchIcon = document.querySelector('link[rel="apple-touch-icon"]').href;
          const types = await Promise.all([icon.href, touchIcon].map(
            async (url) => (await fetch(url)).headers.get('content-type')));
          return {
            display: manifest.display,
            start: new URL(manifest.start_url, link.href).href,
            scope: new URL(manifest.scope, link.href).href,
            page: new URL('./', location.href).href,
            types,
          };
        }""")
        assert info["display"] == "fullscreen"
        assert info["start"] == info["page"] and info["scope"] == info["page"], info
        assert info["types"] == ["image/png", "image/png"], info
    finally:
        context.close()
