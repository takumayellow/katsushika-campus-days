"""WebGL テンプレートが端末の GPU を見て data を選ぶこと (#187)。

テンプレートを kcd_e2e.template で展開し, tools/deploy_pages.py --build と同じように ASTC の data の
名前を書き込んで配る。ローダーは偽物（fake_unity_loader.js）に差し替え, createUnityInstance に
渡された config.dataUrl を読む。GPU が読める圧縮の形式は WebGL2RenderingContext.getExtension を
差し替えて決める。Unity のビルドは要らない。
"""

from __future__ import annotations

from pathlib import Path

import pytest

pytest.importorskip("playwright.sync_api")

from playwright.sync_api import sync_playwright  # noqa: E402

from kcd_e2e.browser import default_browser, launch_config  # noqa: E402
from kcd_e2e.server import serve_build  # noqa: E402
from kcd_e2e.template import DEFAULT_VARIABLES, LOADER_FILENAME, write_template_build  # noqa: E402

FAKE_LOADER = Path(__file__).with_name("fake_unity_loader.js").read_text(encoding="utf-8")
ASTC_SLOT = "var astcDataUrl = null; // KCD_ASTC_DATA_URL"
DXT_DATA = f"Build/{DEFAULT_VARIABLES['DATA_FILENAME']}"
ASTC_DATA = "Build/kcd-astc.data.unityweb"
TIMEOUT_MS = 10_000

S3TC = "WEBGL_compressed_texture_s3tc"
ASTC = "WEBGL_compressed_texture_astc"

# GPU が読める圧縮の形式を決める。列挙に無い圧縮の拡張は無いことにし, ほかの拡張はそのまま返す。
STUB_EXTENSIONS_JS = """(supported) => {
  const original = WebGL2RenderingContext.prototype.getExtension;
  WebGL2RenderingContext.prototype.getExtension = function (name) {
    if (name.startsWith("WEBGL_compressed_texture_")) {
      return supported.includes(name) ? {} : null;
    }
    return original.call(this, name);
  };
}"""
NO_WEBGL2_JS = """() => {
  const original = HTMLCanvasElement.prototype.getContext;
  HTMLCanvasElement.prototype.getContext = function (type, ...rest) {
    return type === "webgl2" ? null : original.call(this, type, ...rest);
  };
}"""


@pytest.fixture(scope="module")
def browser():
    config = launch_config(default_browser(), headless=True, gl="software")
    with sync_playwright() as playwright:
        launched = playwright.chromium.launch(**config.launch_kwargs())
        try:
            yield launched
        finally:
            launched.close()


def serve(root: Path, astc_data: str | None):
    root = write_template_build(root)
    if astc_data is not None:
        index = root / "index.html"
        html = index.read_text(encoding="utf-8")
        assert html.count(ASTC_SLOT) == 1
        name = astc_data[len("Build/"):]
        index.write_text(html.replace(ASTC_SLOT, f'var astcDataUrl = buildUrl + "/{name}";'), encoding="utf-8")
    return serve_build(root)


@pytest.fixture(scope="module")
def site(tmp_path_factory):
    with serve(tmp_path_factory.mktemp("kcd-astc") / "WebGL", ASTC_DATA) as base:
        yield base


@pytest.fixture(scope="module")
def site_without_astc(tmp_path_factory):
    with serve(tmp_path_factory.mktemp("kcd-dxt") / "WebGL", None) as base:
        yield base


@pytest.fixture
def page(browser):
    context = browser.new_context(viewport={"width": 1280, "height": 800})
    opened = context.new_page()
    opened.page_errors = []
    opened.on("pageerror", lambda error: opened.page_errors.append(f"{error.name}: {error.message}"))
    opened.route(f"**/Build/{LOADER_FILENAME}", lambda route: route.fulfill(
        status=200, content_type="application/javascript", body=FAKE_LOADER))
    try:
        yield opened
    finally:
        context.close()


def chosen_data(page, url: str, supported: list[str] | None) -> str:
    """ページを開き, ローダーに渡った data の URL を返す。supported が None なら WebGL2 が無い端末。"""
    if supported is None:
        page.add_init_script(f"({NO_WEBGL2_JS})()")
    else:
        page.add_init_script(f"({STUB_EXTENSIONS_JS})({supported!r})")
    page.goto(url, wait_until="domcontentloaded")
    page.wait_for_function("() => window.__kcdFake && window.__kcdFake.dataUrl", timeout=TIMEOUT_MS)
    data = page.evaluate("() => window.__kcdFake.dataUrl")
    assert page.page_errors == []
    return data


@pytest.mark.parametrize("supported, expected", [
    ([S3TC, ASTC], DXT_DATA),   # PC と, DXT も読める一部の Android
    ([ASTC], ASTC_DATA),        # iPhone・多くの Android
    ([S3TC], DXT_DATA),         # 古い PC
    ([], DXT_DATA),             # どちらも読めない端末では, Unity が DXT を展開する
    (None, DXT_DATA),           # WebGL2 が無い端末は調べられないので DXT のまま
])
def test_data_follows_gpu_formats(page, site, supported, expected):
    assert chosen_data(page, site, supported) == expected


@pytest.mark.parametrize("query, supported, expected", [
    ("?textures=astc", [S3TC, ASTC], ASTC_DATA),
    ("?textures=dxt", [ASTC], DXT_DATA),
    ("?textures=etc2", [ASTC], ASTC_DATA),  # 知らない値は無視して GPU で決める
])
def test_query_overrides_gpu_formats(page, site, query, supported, expected):
    assert chosen_data(page, site + query, supported) == expected


@pytest.mark.parametrize("query", ["", "?textures=astc"])
def test_build_without_astc_data_reads_dxt(page, site_without_astc, query):
    assert chosen_data(page, site_without_astc + query, [ASTC]) == DXT_DATA
