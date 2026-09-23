import re

import pytest

from kcd_e2e.console_policy import (ALLOWLIST, ERROR_TYPES, AllowRule, ConsoleEntry, classify,
                                    is_error)

# 2026-09-24 に公開版で実際に出た文面（Edge の headless と、SwiftShader の Chromium）。
KNOWN_WARNINGS = [
    "Shader 'Hidden/Universal Render Pipeline/Edge Adaptive Spatial Upsampling' is not supported "
    "or has been stripped from the build (in 'Blit FSR Upscaling'). PostProcessing render passes "
    "will not execute. Check for missing reference in the renderer resources.",
    "Manual synchronization of Unity Application.persistentDataPath via JS_FileSystem_Sync() is "
    "deprecated and will be later removed in a future Unity version. The persistent data "
    "directory will be automatically synchronized instead on file modification.",
    "Failed to create agent because there is no valid NavMesh",
    "getFrequency() is not supported for compressed sound.",
    "Additional Lights Cookie Format (GrayscaleHigh) is not supported by the platform. Falling "
    "back to 32-bit format (RGBA8 UNorm)",
]


def warning(text: str) -> ConsoleEntry:
    return ConsoleEntry("warning", text)


def test_unlisted_console_error_fails():
    verdict = classify([ConsoleEntry("error", "Uncaught RuntimeError: memory access out of bounds")])
    assert not verdict.ok
    assert len(verdict.errors) == 1


def test_pageerror_fails():
    verdict = classify([ConsoleEntry("pageerror", "TypeError: x is undefined")])
    assert not verdict.ok


@pytest.mark.parametrize("kind", ["log", "warning", "info"])
def test_managed_exception_in_any_level_fails(kind):
    entry = ConsoleEntry(kind, "NullReferenceException: Object reference not set to an instance")
    assert is_error(entry)
    assert not classify([entry]).ok


def test_exception_word_without_exception_shape_is_not_an_error():
    assert not is_error(ConsoleEntry("log", "Exception handling: enabled"))
    assert not is_error(ConsoleEntry("log", "no exceptions were thrown"))


@pytest.mark.parametrize("text", KNOWN_WARNINGS)
def test_known_warnings_are_allowed_with_a_reason(text):
    verdict = classify([warning(text)])
    assert verdict.ok_strict
    assert len(verdict.allowed) == 1
    assert verdict.allowed[0][1].reason


def test_unknown_warning_is_kept_but_only_fails_in_strict_mode():
    verdict = classify([warning("Something new happened")])
    assert verdict.ok
    assert not verdict.ok_strict
    assert [e.text for e in verdict.warnings] == ["Something new happened"]


def test_known_warning_text_as_console_error_is_not_allowed():
    # 警告の規則は、同じ文面でも console.error なら許さない。
    verdict = classify([ConsoleEntry("error", "Failed to create agent because there is no valid NavMesh")])
    assert not verdict.ok


def test_warning_rule_does_not_allow_an_exception():
    rule = AllowRule(r"NavMesh", "test")
    entry = warning("InvalidOperationException: NavMesh is missing")
    assert not rule.matches(entry)
    assert not classify([entry], (rule,)).ok


def test_error_rule_allows_matching_error():
    rule = AllowRule(r"^known broken thing", "test", types=ERROR_TYPES)
    verdict = classify([ConsoleEntry("error", "known broken thing happened")], (rule,))
    assert verdict.ok
    assert verdict.allowed[0][1] is rule


def test_plain_logs_are_ignored():
    verdict = classify([ConsoleEntry("log", "[KCD] quality=Mobile pipeline=Mobile_RPAsset\n"),
                        ConsoleEntry("debug", "anything")])
    assert verdict.ok_strict
    assert not verdict.allowed


def test_allowlist_rules_compile_and_have_reasons():
    assert ALLOWLIST
    for rule in ALLOWLIST:
        re.compile(rule.pattern)
        assert len(rule.reason) > 20
