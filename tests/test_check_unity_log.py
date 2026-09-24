"""tools/check_unity_log.py が失敗の証拠を拾い、正常系の行を拾わないこと。"""

import io

import check_unity_log


def scan_text(tmp_path, text):
    log = tmp_path / "unity.log"
    log.write_text(text, encoding="utf-8")
    out = io.StringIO()
    return check_unity_log.scan(log, out), out.getvalue()


def test_clean_log_passes(tmp_path):
    code, text = scan_text(tmp_path, "Loading scene\n[KCD] 入口トリガーを 9 個置きました。\n")
    assert code == 0
    assert "NavMesh  : 0" in text
    assert "[KCD] 入口トリガーを 9 個置きました。" in text


def test_navmesh_agent_failure_is_reported(tmp_path):
    # #63: Campus を開くと 4 体の NPC がこの行を出して NavMesh に載れない。
    lines = ["Failed to create agent because there is no valid NavMesh"] * 4
    code, text = scan_text(tmp_path, "\n".join(lines) + "\n")
    assert code == 1
    assert "NavMesh  : 4" in text
    assert text.count("[NAV] Failed to create agent") == 4


def test_inactive_agent_calls_are_reported(tmp_path):
    line = '"SetDestination" can only be called on an active agent that has been placed on a NavMesh.\n'
    code, text = scan_text(tmp_path, line)
    assert code == 1
    assert "[NAV]" in text


def test_compile_errors_and_exceptions_still_fail(tmp_path):
    code, text = scan_text(tmp_path, "Assets/A.cs(1,1): error CS0103: x\nNullReferenceException: y\n")
    assert code == 1
    assert "[CS]" in text and "[EX]" in text


def test_ignored_tokens_do_not_count_as_exceptions(tmp_path):
    code, _ = scan_text(tmp_path, "UnityEngine.StackTraceUtility:ExtractStackTrace () ExceptionHandler\n")
    assert code == 0


def test_missing_log_is_unreadable(tmp_path):
    out = io.StringIO()
    assert check_unity_log.scan(tmp_path / "none.log", out) == 2


def test_test_framework_frames_under_an_expected_warning_are_not_exceptions(tmp_path):
    # EditMode のテストが LogAssert.Expect で待つ警告にも、Test Framework のフレームが付く。
    frame = (
        "UnityEngine.TestRunner.NUnitExtensions.Runner.UnityLogCheckDelegatingCommand:CaptureException "
        "(NUnit.Framework.Internal.TestResult,System.Action) (at ./Library/PackageCache/"
        "com.unity.test-framework@7a3849e09bd0/UnityEngine.TestRunner/NUnitExtensions/Runner/"
        "UnityLogCheckDelegatingCommand.cs:93)\n"
    )
    code, text = scan_text(tmp_path, "TreeChunkCombiner: 1 個はまとめずに 1 つずつ描く（odd）\n" + frame)
    assert code == 0
    assert "Exception: 0" in text


def test_exception_thrown_inside_a_test_still_fails(tmp_path):
    frame = "UnityEngine.TestRunner.NUnitExtensions.Runner.UnityLogCheckDelegatingCommand:CaptureException (x)\n"
    code, text = scan_text(tmp_path, "NullReferenceException: Object reference not set\n" + frame)
    assert code == 1
    assert "Exception: 1" in text
