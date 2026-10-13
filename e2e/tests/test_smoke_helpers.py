import pytest

from kcd_e2e import smoke
from kcd_e2e.console_policy import ConsoleEntry


@pytest.mark.parametrize("path, base, expected", [
    ("/katsushika-campus-days/Build/WebGL.wasm.unityweb", "/katsushika-campus-days/", True),
    ("/katsushika-campus-days/TemplateData/style.css", "/katsushika-campus-days/", False),
    ("/other/Build/WebGL.wasm.unityweb", "/katsushika-campus-days/", False),
    ("/Build/WebGL.loader.js", "/", True),
    ("/game/Build/WebGL.loader.js", "/game/index.html", True),
    ("/Build/WebGL.loader.js", "/game/index.html", False),
    ("/Build/WebGL.loader.js", "", True),
])
def test_is_build_path(path, base, expected):
    assert smoke._is_build_path(path, base) is expected


def test_timings_keep_only_measured_values():
    timings = smoke._timings({"domContentLoaded": 979.4, "load": 0},
                             {"loaderLoadedAt": 980.2, "firstProgressAt": None,
                              "progressDoneAt": 2458.0, "readyAt": 2459.6})
    assert timings == {"dom_content_loaded": 979, "loader_loaded": 980,
                       "download_done": 2458, "unity_ready": 2460}


def test_count_kinds_groups_by_first_line():
    entries = [ConsoleEntry("warning", "Failed to create agent\nstack a"),
               ConsoleEntry("warning", "Failed to create agent\nstack b"),
               ConsoleEntry("warning", "other")]
    assert smoke._count_kinds(entries) == {"Failed to create agent": 2, "other": 1}


def test_stage_index():
    assert smoke.stage_index("ready") == 0
    assert smoke.stage_index("campus") == 3
    with pytest.raises(ValueError):
        smoke.stage_index("ending")


def test_first_line_and_short_url():
    assert smoke._first_line("  a\nb ") == "a"
    assert smoke._first_line("") == ""
    assert smoke._short_url("x" * 10) == "x" * 10
    assert len(smoke._short_url("x" * 500)) == 160


def test_overlay_detail_names_a_missing_element():
    assert "#kcd-overlay" in smoke._overlay_detail(None, "hidden", "shown")
    assert smoke._overlay_detail(True, "hidden", "shown") == "hidden"
    assert smoke._overlay_detail(False, "hidden", "shown") == "shown"
