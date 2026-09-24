import json

import pytest

from kcd_e2e.report import Report, format_bytes
from run_webgl_smoke import PUBLIC_URL, resolve_url


def test_public_site_is_the_default():
    assert resolve_url(None, None, {}) == (PUBLIC_URL, "public")


def test_public_site_without_trailing_slash_is_still_public():
    assert resolve_url(PUBLIC_URL.rstrip("/"), None, {}) == (PUBLIC_URL, "public")


def test_environment_variable_is_used_when_no_flag():
    env = {"KCD_E2E_URL": "http://localhost:8000"}
    assert resolve_url(None, None, env) == ("http://localhost:8000/", "url")


def test_flag_wins_over_environment_variable():
    env = {"KCD_E2E_URL": "http://localhost:8000/"}
    assert resolve_url("http://127.0.0.1:9000/game/index.html", None, env) == (
        "http://127.0.0.1:9000/game/index.html", "url")


def test_serve_decides_url_later():
    assert resolve_url(None, "build/WebGL", {"KCD_E2E_URL": "http://x/"}) == (None, "local")


@pytest.mark.parametrize("url", ["file:///C:/build/index.html", "javascript:alert(1)",
                                 "localhost:8000", "http://"])
def test_rejects_non_http_urls(url):
    with pytest.raises(ValueError):
        resolve_url(url, None, {})


def test_report_needs_at_least_one_check():
    report = Report(url="u", target="t")
    assert not report.ok
    report.add("load", True, "1.0 秒")
    assert report.ok
    report.add("console", False, "エラー 1 件")
    assert not report.ok
    assert [c.name for c in report.failed()] == ["console"]


def test_report_summary_and_json(tmp_path):
    report = Report(url="http://127.0.0.1:1/", target="local")
    report.add("load", True, "2.5 秒")
    report.metrics["timings_ms"] = {"unity_ready": 2459}
    report.metrics["heap"] = {"ready": {"wasm_memory": 256 * 1024 * 1024, "js_heap_used": None}}
    text = report.summary()
    assert text.startswith("OK  local")
    assert "unity_ready=2459" in text
    assert "wasm_memory=256.0MiB" in text and "js_heap_used=None" in text
    path = tmp_path / "out" / "metrics.json"
    report.write_json(path)
    data = json.loads(path.read_text(encoding="utf-8"))
    assert data["ok"] is True
    assert data["checks"] == [{"name": "load", "ok": True, "detail": "2.5 秒"}]


def test_format_bytes():
    assert format_bytes(307.2 * 1024 * 1024) == "307.2MiB"
    assert format_bytes(None) == "None"
