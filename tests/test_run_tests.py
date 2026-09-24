"""tools/run_tests.py の判定まわり（Unity を起動せずに確かめられる部分）。"""

import io
import os
import subprocess
import sys
from pathlib import Path

import pytest

import run_tests

REPO = Path(__file__).resolve().parents[1]

PASSED_XML = """<?xml version="1.0" encoding="utf-8"?>
<test-run id="2" testcasecount="2" result="Passed" total="2" passed="2" failed="0" inconclusive="0"
          skipped="0" duration="1.5">
  <test-suite type="TestFixture" name="A" fullname="KCD.Tests.A" result="Passed">
    <test-case name="One" fullname="KCD.Tests.A.One" result="Passed" />
    <test-case name="Two" fullname="KCD.Tests.A.Two" result="Passed" />
  </test-suite>
</test-run>
"""

MIXED_XML = """<?xml version="1.0" encoding="utf-8"?>
<test-run id="2" testcasecount="4" result="Failed" total="4" passed="1" failed="2" inconclusive="0"
          skipped="1" duration="12.25">
  <test-suite type="TestFixture" name="Npc" fullname="KCD.Tests.Npc" result="Failed" site="Child">
    <test-case name="Agents" fullname="KCD.Tests.Npc.Agents" result="Failed" label="">
      <failure>
        <message><![CDATA[4 体が NavMesh に載っていない
  npc_a: enabled=False]]></message>
        <stack-trace><![CDATA[at KCD.Tests.Npc.Agents () [0x00001] in Npc.cs:10
at X
at Y
at Z
at W]]></stack-trace>
      </failure>
    </test-case>
    <test-case name="Passing" fullname="KCD.Tests.Npc.Passing" result="Passed" />
    <test-case name="Ignored" fullname="KCD.Tests.Npc.Ignored" result="Skipped" label="Ignored">
      <reason><message><![CDATA[あとで]]></message></reason>
    </test-case>
  </test-suite>
  <test-suite type="TestFixture" name="Setup" fullname="KCD.Tests.Setup" result="Failed" site="SetUp">
    <failure><message><![CDATA[OneTimeSetUp: シーンが読めない]]></message></failure>
    <test-case name="Inner" fullname="KCD.Tests.Setup.Inner" result="Failed" label="Error">
      <failure><message><![CDATA[OneTimeSetUp: シーンが読めない]]></message></failure>
    </test-case>
  </test-suite>
</test-run>
"""


def write(tmp_path, name, text):
    path = tmp_path / name
    path.write_text(text, encoding="utf-8")
    return path


# ---------------------------------------------------------------- Unity の同時起動ガード


def test_tasklist_csv_finds_unity_pids():
    text = (
        '"Unity.exe","41236","Console","1","2,345,678 K"\r\n'
        '"Unity Hub.exe","100","Console","1","100 K"\r\n'
        '"unity.exe","777","Console","1","1 K"\r\n'
    )
    assert run_tests.parse_tasklist_csv(text) == [41236, 777]


@pytest.mark.parametrize(
    "text",
    [
        "INFO: No tasks are running which match the specified criteria.\r\n",
        "情報: 指定された条件に一致するタスクは実行されていません。\r\n",
        "",
    ],
)
def test_tasklist_without_unity_is_empty(text):
    assert run_tests.parse_tasklist_csv(text) == []


def test_busy_unity_stops_without_wait():
    out = io.StringIO()
    assert not run_tests.wait_for_free_unity(False, 60, 0, out, probe=lambda: [41236])
    assert "[BUSY]" in out.getvalue() and "41236" in out.getvalue()


def test_wait_returns_once_unity_is_gone():
    answers = iter([[41236], [41236], []])
    out = io.StringIO()
    assert run_tests.wait_for_free_unity(True, 60, 0, out, probe=lambda: next(answers))
    assert "[WAIT]" in out.getvalue()


def test_wait_gives_up_after_the_limit():
    out = io.StringIO()
    assert not run_tests.wait_for_free_unity(True, 0, 0, out, probe=lambda: [1])
    assert "[BUSY]" in out.getvalue()


def test_free_unity_passes_immediately():
    assert run_tests.wait_for_free_unity(False, 0, 0, io.StringIO(), probe=lambda: [])


def test_lockfile_is_kept_while_any_unity_runs(tmp_path):
    lockfile = tmp_path / "Temp" / "UnityLockfile"
    lockfile.parent.mkdir()
    lockfile.write_text("")
    run_tests.remove_stale_lockfile(tmp_path, io.StringIO(), probe=lambda: [1])
    assert lockfile.exists()
    run_tests.remove_stale_lockfile(tmp_path, io.StringIO(), probe=lambda: [])
    assert not lockfile.exists()


# ---------------------------------------------------------------- Unity の場所とコマンド


@pytest.mark.skipif(os.name != "nt", reason="Git Bash のパスは Windows だけ")
def test_msys_path_becomes_windows_path():
    assert (
        run_tests.msys_to_windows("/c/Program Files/Unity/Hub/Editor/6000.6.2f1/Editor/Unity.exe")
        == "C:/Program Files/Unity/Hub/Editor/6000.6.2f1/Editor/Unity.exe"
    )
    assert run_tests.msys_to_windows("D:/Unity/Unity.exe") == "D:/Unity/Unity.exe"


def test_unity_is_found_from_argument_then_environment_then_project_version(tmp_path):
    settings = tmp_path / "ProjectSettings"
    settings.mkdir()
    (settings / "ProjectVersion.txt").write_text("m_EditorVersion: 6000.6.2f1\n", encoding="utf-8")

    assert run_tests.resolve_unity("D:/U/Unity.exe", tmp_path, {"UNITY": "E:/U.exe"}) == Path("D:/U/Unity.exe")
    assert run_tests.resolve_unity(None, tmp_path, {"UNITY": "E:/U.exe"}) == Path("E:/U.exe")
    assert run_tests.resolve_unity(None, tmp_path, {"UNITY_EXE": "F:/U.exe", "UNITY": "E:/U.exe"}) == Path("F:/U.exe")
    assert run_tests.resolve_unity(None, tmp_path, {}) == Path(
        "C:/Program Files/Unity/Hub/Editor/6000.6.2f1/Editor/Unity.exe"
    )
    assert run_tests.resolve_unity(None, tmp_path / "missing", {}) is None


def test_unity_command_uses_absolute_result_and_log_paths(tmp_path, monkeypatch):
    monkeypatch.chdir(tmp_path)
    command = run_tests.unity_command(
        Path("U.exe"), tmp_path, "PlayMode", Path("out.xml"), Path("out.log"), False, "KCD.Tests.Npc"
    )
    results = Path(command[command.index("-testResults") + 1])
    log = Path(command[command.index("-logFile") + 1])
    assert results.is_absolute() and results == tmp_path / "out.xml"
    assert log.is_absolute() and log == tmp_path / "out.log"
    assert command[command.index("-testPlatform") + 1] == "PlayMode"
    assert command[command.index("-testFilter") + 1] == "KCD.Tests.Npc"
    assert "-nographics" not in command
    assert "-quit" not in command  # -runTests は走り終えると自分で閉じる（-quit は付けない）


def test_editmode_command_runs_without_graphics(tmp_path):
    command = run_tests.unity_command(
        Path("U.exe"), tmp_path, "EditMode", tmp_path / "a.xml", tmp_path / "a.log", True, None
    )
    assert "-nographics" in command
    assert "-testFilter" not in command


def test_platform_choice():
    assert run_tests.selected_platforms("all") == ["EditMode", "PlayMode"]
    assert run_tests.selected_platforms("playmode") == ["PlayMode"]


# ---------------------------------------------------------------- 結果 XML


def test_passed_run_is_ok(tmp_path):
    summary = run_tests.parse_results(write(tmp_path, "r.xml", PASSED_XML))
    assert (summary.total, summary.passed, summary.failed, summary.skipped) == (2, 2, 0, 0)
    assert summary.problems == ()
    assert summary.ok


def test_failures_skips_and_fixture_failures_are_listed(tmp_path):
    summary = run_tests.parse_results(write(tmp_path, "r.xml", MIXED_XML))
    assert (summary.total, summary.failed, summary.skipped) == (4, 2, 1)
    assert not summary.ok

    names = [(case.fullname, case.result) for case in summary.problems]
    assert ("KCD.Tests.Npc.Agents", "Failed") in names
    assert ("KCD.Tests.Npc.Ignored", "Skipped") in names
    assert ("KCD.Tests.Setup", "Failed") in names  # OneTimeSetUp の失敗
    agents = next(case for case in summary.problems if case.fullname == "KCD.Tests.Npc.Agents")
    assert "npc_a: enabled=False" in agents.message

    out = io.StringIO()
    run_tests.write_summary("PlayMode", summary, out)
    text = out.getvalue()
    assert "failed 2 / skipped 1" in text
    assert "[Failed] KCD.Tests.Npc.Agents" in text
    assert "[Skipped/Ignored] KCD.Tests.Npc.Ignored" in text
    assert "あと 1 行" in text  # スタックは先頭 4 行まで


def test_skipped_only_run_is_not_ok():
    summary = run_tests.RunSummary(3, 2, 0, 1, 0, 0.0, ())
    assert not summary.ok


def test_empty_run_is_not_ok():
    assert not run_tests.RunSummary(0, 0, 0, 0, 0, 0.0, ()).ok


# ---------------------------------------------------------------- 1 プラットフォームの判定


def judge(tmp_path, xml, log_text, code, filtered=False):
    results = write(tmp_path, "r.xml", xml) if xml is not None else tmp_path / "missing.xml"
    log = write(tmp_path, "r.log", log_text)
    return run_tests.judge_run("PlayMode", results, log, code, filtered, io.StringIO())


def test_judge_all_green(tmp_path):
    assert judge(tmp_path, PASSED_XML, "ok\n", 0) == run_tests.PlatformOutcome(run_tests.EXIT_OK, 2)


def test_judge_failures_exit_1(tmp_path):
    assert judge(tmp_path, MIXED_XML, "ok\n", 2).code == run_tests.EXIT_FAILED


def test_judge_navmesh_log_exit_1(tmp_path):
    log = "Failed to create agent because there is no valid NavMesh\n"
    assert judge(tmp_path, PASSED_XML, log, 0).code == run_tests.EXIT_FAILED


def test_judge_missing_xml_exit_2(tmp_path):
    assert judge(tmp_path, None, "error CS0103: x\n", 3).code == run_tests.EXIT_ERROR


def test_judge_run_error_exit_2(tmp_path):
    assert judge(tmp_path, PASSED_XML, "ok\n", 3).code == run_tests.EXIT_ERROR
    assert judge(tmp_path, PASSED_XML, "ok\n", None).code == run_tests.EXIT_ERROR


def test_judge_unity_failure_without_failed_cases_exit_1(tmp_path):
    assert judge(tmp_path, PASSED_XML, "ok\n", 2).code == run_tests.EXIT_FAILED


def test_judge_empty_run(tmp_path):
    empty = PASSED_XML.replace('total="2" passed="2"', 'total="0" passed="0"')
    assert judge(tmp_path, empty, "ok\n", 0).code == run_tests.EXIT_FAILED
    # --filter が片方のプラットフォームに当たらないのは失敗にしない（両方 0 本なら main が落とす）。
    assert judge(tmp_path, empty, "ok\n", 0, filtered=True) == run_tests.PlatformOutcome(run_tests.EXIT_OK, 0)


# ---------------------------------------------------------------- batchmode のノイズ

URP = "unity/KatsushikaCampusDays/Assets/Settings/UniversalRenderPipelineGlobalSettings.asset"
PROJECT_SETTINGS = "unity/KatsushikaCampusDays/ProjectSettings/ProjectSettings.asset"
MAT = "unity/KatsushikaCampusDays/Assets/Materials/Brick.mat"
SCRIPT = "unity/KatsushikaCampusDays/Assets/Scripts/Runtime/A.cs"


def test_porcelain_z_lists_changed_paths():
    raw = f" M {URP}\0M  {MAT}\0R  new.mat\0old.mat\0"
    assert run_tests.parse_porcelain_z(raw) == {URP, MAT, "new.mat"}


def test_only_fresh_noise_is_reverted():
    before = {MAT}
    after = {MAT, URP, PROJECT_SETTINGS, SCRIPT, "unity/KatsushikaCampusDays/Assets/Materials/Glass.mat"}
    revert, other = run_tests.noise_to_revert(before, after)
    assert revert == sorted([URP, PROJECT_SETTINGS, "unity/KatsushikaCampusDays/Assets/Materials/Glass.mat"])
    assert MAT not in revert  # 実行前から触っていたものは、人の作業なので戻さない
    assert other == [SCRIPT]


def test_urp_project_settings_is_not_noise():
    # 似た名前の URPProjectSettings.asset は設定の本体なので戻さない。
    assert not run_tests.is_noise("unity/KatsushikaCampusDays/ProjectSettings/URPProjectSettings.asset")


# ---------------------------------------------------------------- 通しの dry-run


def test_dry_run_prints_both_platforms_without_touching_unity():
    completed = subprocess.run(
        [sys.executable, str(REPO / "tools" / "run_tests.py"), "--dry-run", "--skip-pytest", "--unity", "X:/U.exe"],
        capture_output=True,
        check=False,
    )
    text = completed.stdout.decode("utf-8")
    assert completed.returncode == 0, text
    assert "[RUN] EditMode" in text and "[RUN] PlayMode" in text
    assert str(REPO / "unity" / "logs" / "tests_playmode.xml") in text
