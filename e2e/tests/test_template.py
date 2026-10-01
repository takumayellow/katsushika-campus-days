import json

import pytest

from kcd_e2e.template import (DEFAULT_VARIABLES, TEMPLATE_DIR, evaluate_condition, evaluate_slot,
                              render, write_template_build)


def test_slots_take_names_and_json():
    values = {"NAME": "a.loader.js", "WIDTH": 960, "FILES": ["Build/x.data"], "TEXT": "葛飾"}
    assert evaluate_slot("NAME", values) == "a.loader.js"
    assert evaluate_slot("WIDTH", values) == "960"
    assert evaluate_slot("JSON.stringify(FILES)", values) == '["Build/x.data"]'
    assert evaluate_slot("JSON.stringify(TEXT)", values) == '"葛飾"'


@pytest.mark.parametrize("expression", ["NAME.replace(/'/g, '%27')", "1 + 1", "lower"])
def test_unknown_slot_expressions_are_rejected(expression):
    with pytest.raises(ValueError):
        evaluate_slot(expression, {"NAME": "x"})


def test_missing_variable_is_an_error():
    with pytest.raises(KeyError):
        evaluate_slot("LOADER_FILENAME", {})


@pytest.mark.parametrize("value, expected", [
    ("", False), ("a", True), (0, False), (1, True), (False, False), (True, True),
    (None, False), ([], True), ({}, True),
])
def test_conditions_follow_javascript_truthiness(value, expected):
    assert evaluate_condition("X", {"X": value}) is expected


def test_render_keeps_the_taken_branches_and_drops_directives():
    source = (
        "a\n"
        "#if ON\n"
        "b {{{ NAME }}}\n"
        "  #if OFF\n"
        "c\n"
        "  #else\n"
        "d\n"
        "  #endif\n"
        "#else\n"
        "e\n"
        "#endif\n"
        "#unity-canvas { color: red; }\n"
    )
    html = render(source, {"ON": True, "OFF": "", "NAME": "n"})
    assert html == "a\nb n\nd\n#unity-canvas { color: red; }\n"


def test_render_does_not_evaluate_skipped_lines():
    assert render("#if OFF\n{{{ MISSING }}}\n#endif\nok\n", {"OFF": False}) == "ok\n"


@pytest.mark.parametrize("source", ["#if X\n", "#endif\n", "#else\n", "#if X\n#else\n#else\n#endif\n"])
def test_render_rejects_unbalanced_directives(source):
    with pytest.raises(ValueError):
        render(source, {"X": True})


def test_the_kcd_template_renders_completely(tmp_path):
    root = write_template_build(tmp_path / "WebGL")
    html = (root / "index.html").read_text(encoding="utf-8")
    assert "{{{" not in html and "}}}" not in html
    assert not any(line.lstrip().startswith(("#if", "#else", "#endif")) for line in html.splitlines())
    assert f'var loaderUrl = buildUrl + "/{DEFAULT_VARIABLES["LOADER_FILENAME"]}";' in html
    assert f'dataUrl: buildUrl + "/{DEFAULT_VARIABLES["DATA_FILENAME"]}",' in html
    assert "primaryDataUrls" not in html
    assert f"productName: {json.dumps(DEFAULT_VARIABLES['PRODUCT_NAME'], ensure_ascii=False)}," in html
    assert (root / "TemplateData" / "style.css").is_file()
    assert not list(root.rglob("*.meta"))
    assert (root / "Build").is_dir() and not list((root / "Build").iterdir())


def test_template_dir_points_at_the_unity_project():
    assert (TEMPLATE_DIR / "index.html").is_file()
    assert (TEMPLATE_DIR / "TemplateData" / "style.css").is_file()
