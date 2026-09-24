"""WebGL テンプレートの読み込み中の表示と、失敗したときの知らせ方 (#102)。

テンプレート（Assets/WebGLTemplates/KCD/index.html）を kcd_e2e.template で展開して配り、
Build/*.loader.js を偽のローダー（fake_unity_loader.js）に差し替えて、ブラウザで開く。
Unity のビルドは要らない。確かめること:

- ローダーの取得が失敗したら、再読み込みの案内がエラー帯に出る（何も出ないまま止まらない）。
- 読み込みの途中で失敗したら、「読み込み中…」の覆いが消え、案内が出る。
- ローダーが帯だけでエラーを知らせて止まったときも同じ。その後に進み具合が届いても覆いを出し直さない。
- 成功したときの進み具合の表示と覆いの消え方は変わらない。

KCD_E2E_BUILD に Unity の WebGL ビルド（build/WebGL）を渡すと、本物のローダーでも
「loader の 404」と「data の取得の中断」の 2 つを確かめる。
"""

from __future__ import annotations

import os
from pathlib import Path

import pytest

pytest.importorskip("playwright.sync_api")

from playwright.sync_api import Error as PlaywrightError  # noqa: E402
from playwright.sync_api import sync_playwright  # noqa: E402

from kcd_e2e.browser import default_browser, launch_config  # noqa: E402
from kcd_e2e.server import serve_build  # noqa: E402
from kcd_e2e.template import LOADER_FILENAME, write_template_build  # noqa: E402

FAKE_LOADER = Path(__file__).with_name("fake_unity_loader.js").read_text(encoding="utf-8")
LOAD_FAILED = "ゲームの読み込みに失敗しました。ページを再読み込みしてください。"
# 偽のローダーは同期的に進むので、数秒あれば決着する。本物のビルドは data の取得まで時間がかかる。
FAKE_TIMEOUT_MS = 10_000
REAL_TIMEOUT_MS = 120_000

VIEW_JS = """() => {
  const overlay = document.querySelector('#unity-loading-container');
  const bar = document.querySelector('#unity-progress-bar-full');
  const warning = document.querySelector('#unity-warning');
  return {
    overlayShown: getComputedStyle(overlay).display !== 'none',
    barWidth: bar.style.width,
    warningShown: getComputedStyle(warning).display !== 'none',
    banners: Array.from(warning.children, (div) => ({
      text: div.textContent,
      error: div.style.backgroundColor === 'rgb(179, 38, 30)',
    })),
    canvasFocused: document.activeElement === document.querySelector('#unity-canvas'),
  };
}"""
HAS_FAILURE_JS = ("(text) => Array.from(document.querySelector('#unity-warning').children)"
                  ".some((div) => div.textContent === text)")


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


@pytest.fixture
def page(browser):
    context = browser.new_context(viewport={"width": 1280, "height": 800})
    opened = context.new_page()
    opened.page_errors = []
    opened.on("pageerror", lambda error: opened.page_errors.append(f"{error.name}: {error.message}"))
    try:
        yield opened
    finally:
        context.close()


def open_page(page, url: str, scenario: str | None) -> None:
    """scenario が None ならローダーを配らない（サーバーは 404 を返す）。"""
    if scenario is not None:
        page.add_init_script(f"window.__kcdFakeScenario = {scenario!r};")
        page.route(f"**/Build/{LOADER_FILENAME}", lambda route: route.fulfill(
            status=200, content_type="application/javascript", body=FAKE_LOADER))
    page.goto(url, wait_until="domcontentloaded")


def view(page) -> dict:
    return page.evaluate(VIEW_JS)


def wait_for_failure_notice(page, timeout_ms: int = FAKE_TIMEOUT_MS) -> dict:
    """再読み込みの案内が出るまで待ち、そのときの表示を返す。出なければ表示を添えて落とす。"""
    try:
        page.wait_for_function(HAS_FAILURE_JS, arg=LOAD_FAILED, timeout=timeout_ms, polling=100)
    except PlaywrightError:
        pytest.fail(f"{timeout_ms / 1000:.0f} 秒待っても再読み込みの案内が出ない。表示: {view(page)}")
    return view(page)


def assert_failure_shown(shown: dict) -> None:
    assert not shown["overlayShown"], "失敗したのに「読み込み中…」の覆いが残っている"
    assert shown["warningShown"], "エラー帯が隠れている"
    notices = [b for b in shown["banners"] if b["text"] == LOAD_FAILED]
    assert len(notices) == 1, f"再読み込みの案内は 1 度だけ出す: {shown['banners']}"
    assert notices[0]["error"], "再読み込みの案内がエラーの色（赤い帯）で出ていない"


def test_loader_404_shows_the_reload_notice(page, site):
    # 以前は <script> に onerror が無く、進み具合もエラーも出ないまま止まっていた。
    responses = []
    page.on("response", lambda response: responses.append((response.url, response.status)))
    open_page(page, site, scenario=None)
    shown = wait_for_failure_notice(page)
    assert any(url.endswith(f"/Build/{LOADER_FILENAME}") and status == 404
               for url, status in responses), responses
    assert_failure_shown(shown)
    assert any(LOADER_FILENAME in b["text"] for b in shown["banners"]), shown["banners"]
    assert page.page_errors == []


def test_rejected_load_hides_the_overlay_and_shows_the_reload_notice(page, site):
    # 以前の .catch は帯を出すだけで、「読み込み中…」の覆いがキャンバスを覆ったまま残っていた。
    open_page(page, site, "reject")
    shown = wait_for_failure_notice(page)
    assert_failure_shown(shown)
    assert any("reading 'subarray'" in b["text"] and b["error"] for b in shown["banners"]), \
        f"ローダーが返した失敗の中身も出す: {shown['banners']}"
    assert page.page_errors == []


def test_error_banner_from_the_loader_counts_as_a_failure(page, site):
    # framework.js が取れないとき、ローダーは帯を出すだけで reject しない。
    open_page(page, site, "error-banner")
    wait_for_failure_notice(page)
    # 偽のローダーは帯のあとに進み具合 0.6 を届ける。それで覆いを出し直さない。
    page.wait_for_timeout(300)
    shown = view(page)
    assert_failure_shown(shown)
    assert any(b["text"].startswith("Unable to load file") for b in shown["banners"]), shown["banners"]
    assert page.page_errors == []


def test_reload_notice_is_shown_once_even_if_the_loader_also_rejects(page, site):
    open_page(page, site, "error-banner-then-reject")
    wait_for_failure_notice(page)
    page.wait_for_function("() => document.querySelector('#unity-warning').textContent"
                           ".includes('results[0] is not a function')", timeout=FAKE_TIMEOUT_MS)
    assert_failure_shown(view(page))
    assert page.page_errors == []


def test_successful_load_shows_progress_then_hides_the_overlay(page, site):
    open_page(page, site, "success")
    page.wait_for_function("() => window.__kcdFake && window.__kcdFake.release",
                           timeout=FAKE_TIMEOUT_MS)
    during = view(page)
    assert during["overlayShown"], "読み込みの途中で「読み込み中…」の覆いが出ていない"
    assert during["barWidth"] == "50%"
    assert not during["warningShown"] and during["banners"] == []

    page.evaluate("() => window.__kcdFake.release()")
    page.wait_for_function("() => window.__kcdFake.ready", timeout=FAKE_TIMEOUT_MS)
    page.wait_for_function("() => document.activeElement === document.querySelector('#unity-canvas')",
                           timeout=FAKE_TIMEOUT_MS)
    after = view(page)
    assert not after["overlayShown"]
    assert not after["warningShown"] and after["banners"] == []
    assert page.page_errors == []


def test_warning_banner_keeps_loading(page, site):
    open_page(page, site, "warning")
    page.wait_for_function("() => document.querySelector('#unity-warning').children.length > 0",
                           timeout=FAKE_TIMEOUT_MS)
    shown = view(page)
    assert shown["overlayShown"], "注意の帯（warning）で読み込み中の覆いを畳んではいけない"
    assert [b["text"] for b in shown["banners"]] == [
        "You can reduce startup time if you configure your web server."]
    assert page.page_errors == []


def test_error_after_the_game_started_is_not_a_load_failure(page, site):
    open_page(page, site, "error-after-ready")
    page.wait_for_function("() => document.querySelector('#unity-warning').children.length > 0",
                           timeout=FAKE_TIMEOUT_MS)
    page.wait_for_timeout(200)
    shown = view(page)
    texts = [b["text"] for b in shown["banners"]]
    assert LOAD_FAILED not in texts, "起動したあとのエラーを読み込みの失敗として案内している"
    assert texts == ["An error occurred running the Unity content on this page."]
    assert not shown["overlayShown"]
    assert page.page_errors == []


# ---- 本物のビルド（KCD_E2E_BUILD）で確かめる。Unity でビルドした build/WebGL を渡す。----

REAL_BUILD = os.environ.get("KCD_E2E_BUILD")
needs_real_build = pytest.mark.skipif(
    not REAL_BUILD, reason="KCD_E2E_BUILD に Unity の WebGL ビルド（build/WebGL）を渡したときだけ走らせる")


@pytest.fixture(scope="module")
def real_site():
    with serve_build(Path(REAL_BUILD)) as base:
        yield base


@needs_real_build
def test_real_build_loader_404_shows_the_reload_notice(page, real_site):
    page.route("**/Build/*.loader.js", lambda route: route.fulfill(status=404, body="not found"))
    page.goto(real_site, wait_until="domcontentloaded")
    assert_failure_shown(wait_for_failure_notice(page, REAL_TIMEOUT_MS))


@needs_real_build
def test_real_build_data_download_cut_shows_the_reload_notice(page, real_site):
    page.route("**/Build/*.data*", lambda route: route.abort("internetdisconnected"))
    page.goto(real_site, wait_until="domcontentloaded")
    assert_failure_shown(wait_for_failure_notice(page, REAL_TIMEOUT_MS))
