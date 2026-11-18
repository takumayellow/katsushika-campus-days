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


def test_exited_unity_left_in_tasklist_is_not_counted():
    # 終わったのにハンドルが残って tasklist に出続ける Unity.exe は、動いているものに数えない
    alive = {41236: True, 63316: False}
    assert run_tests.drop_exited([41236, 63316], alive=alive.__getitem__) == [41236]


EDITOR_EXE = r"C:\Program Files\Unity\Hub\Editor\6000.6.2f1\Editor\Unity.exe"
HUB_HELPER_EXE = (r"C:\Program Files\WindowsApps\UnityTechnologies.UnityHub_3.22.3.65535_x64__2vrhnee42bhxm"
                  r"\app\resources\unity.exe")


@pytest.mark.parametrize(
    "path, helper",
    [
        (HUB_HELPER_EXE, True),
        (r"C:\Program Files\Unity Hub\resources\unity.exe", True),
        (EDITOR_EXE, False),
        (r"D:\Unity\6000.0.58f2\editor\unity.exe", False),
        # --unity や UNITY_EXE で指した、Editor フォルダの外の本体も数える
        (r"C:\Tools\Unity.exe", False),
        (r"D:\UnityHub\Editors\6000.6.2f1\Editor\Unity.exe", False),
        (r"C:\Program Files\Unity Hub\resources\app.exe", False),
        (None, False),
    ],
)
def test_only_the_unity_hub_helper_is_not_counted(path, helper):
    assert run_tests.is_hub_helper(path) is helper


def test_tasklist_to_unity_pids_to_wait_for():
    # 本体、Hub の常駐、終わったのに残っているもの、パスが分からないものが並んだ tasklist
    text = (
        '"Unity.exe","41236","Console","1","2,345,678 K"\r\n'
        '"unity.exe","4752","Console","1","45,000 K"\r\n'
        '"Unity.exe","63316","Console","1","0 K"\r\n'
        '"Unity.exe","900","Console","1","100 K"\r\n'
    )
    alive = {41236: True, 4752: True, 63316: False, 900: True}
    # 終わったもの (63316) はパスを引く前に除くので、ここに無くてよい
    paths = {41236: EDITOR_EXE, 4752: HUB_HELPER_EXE, 900: None}
    pids = run_tests.unity_pids_from_tasklist(text, alive=alive.__getitem__, path_of=paths.__getitem__)
    assert pids == [41236, 900]


@pytest.mark.skipif(os.name != "nt", reason="Windows のプロセス API を使う")
def test_this_process_path_is_an_exe():
    path = run_tests.windows_process_path(os.getpid())
    assert path is not None and Path(path).is_absolute() and path.lower().endswith(".exe")


class FakeKernel32:
    """windows_process_path が呼ぶ 3 つの関数だけを持つ偽物。"""

    def __init__(self, handle, query_ok):
        self.closed = []
        self.OpenProcess = lambda access, inherit, pid: handle
        self.QueryFullProcessImageNameW = lambda process, flags, buffer, size: query_ok
        self.CloseHandle = self.closed.append


@pytest.mark.parametrize("handle, query_ok, closed", [(None, True, 0), (1234, False, 1)])
def test_process_path_is_none_when_it_cannot_be_read(monkeypatch, handle, query_ok, closed):
    # 開けない PID と、開けても名前を引けない PID は None（呼び出し側は数える側に倒す）
    import ctypes

    fake = FakeKernel32(handle, query_ok)
    monkeypatch.setattr(ctypes, "WinDLL", lambda name, use_last_error=False: fake, raising=False)
    assert run_tests.windows_process_path(4752) is None
    assert len(fake.closed) == closed


@pytest.mark.skipif(os.name != "nt", reason="Windows のプロセス API を使う")
def test_this_process_is_alive_and_unknown_pid_is_not():
    assert run_tests.windows_process_alive(os.getpid())
    proc = subprocess.Popen([sys.executable, "-c", "pass"])
    proc.wait()
    # Popen がハンドルを持ったまま終わったプロセス = tasklist に残る形
    assert not run_tests.windows_process_alive(proc.pid)


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


KNOWN_SKIP_XML = """<?xml version="1.0" encoding="utf-8"?>
<test-run id="2" testcasecount="3" result="Passed" total="3" passed="2" failed="0" inconclusive="0"
          skipped="1" duration="3.0">
  <test-suite type="TestFixture" name="A" fullname="KCD.Tests.A" result="Passed">
    <test-case name="One" fullname="KCD.Tests.A.One" result="Passed" />
    <test-case name="Two" fullname="KCD.Tests.A.Two" result="Passed" />
    <test-case name="Shot" fullname="KCD.Tests.A.Shot" result="Skipped" label="Ignored">
      <reason><message><![CDATA[KCD_SHOT_DIR が無いので撮らない]]></message></reason>
    </test-case>
  </test-suite>
</test-run>
"""


def test_known_skip_does_not_fail_the_run(tmp_path, monkeypatch):
    monkeypatch.setattr(run_tests, "KNOWN_SKIPS", {"KCD.Tests.A.Shot": "撮るときだけ走らせる"})
    summary = run_tests.parse_results(write(tmp_path, "r.xml", KNOWN_SKIP_XML))
    assert (summary.skipped, summary.known_skips) == (1, 1)
    assert summary.ok

    out = io.StringIO()
    run_tests.write_summary("PlayMode", summary, out)
    text = out.getvalue()
    assert "[既知のスキップ] KCD.Tests.A.Shot（撮るときだけ走らせる）" in text
    assert "[Skipped/Ignored]" not in text


def test_unlisted_skip_still_fails_the_run(tmp_path, monkeypatch):
    monkeypatch.setattr(run_tests, "KNOWN_SKIPS", {})
    summary = run_tests.parse_results(write(tmp_path, "r.xml", KNOWN_SKIP_XML))
    assert summary.known_skips == 0
    assert not summary.ok


def test_unknown_skip_in_the_list_fails_even_if_the_count_matches(monkeypatch):
    """集計の skipped が既知の分しか無くても、一覧に既知でないスキップがあれば NG。"""
    monkeypatch.setattr(run_tests, "KNOWN_SKIPS", {"KCD.Tests.A.Shot": "撮るときだけ"})
    known = run_tests.CaseResult("KCD.Tests.A.Shot", "Skipped", "Ignored", "", "")
    other = run_tests.CaseResult("KCD.Tests.A.Other", "Skipped", "Ignored", "", "")
    summary = run_tests.RunSummary(3, 2, 0, 1, 0, 0.0, (known, other), known_skips=1)
    assert not summary.ok


def test_known_skip_list_names_the_capture_test():
    assert "KCD.Tests.MobCrowdCaptureTests.水盤の前に集めて撮る" in run_tests.KNOWN_SKIPS


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


def test_time_manager_upgrade_is_noise():
    # 6000.6 は開くたびに Fixed Timestep を有理数の書き方に直す。値は 0.02 のまま。
    assert run_tests.is_noise("unity/KatsushikaCampusDays/ProjectSettings/TimeManager.asset")


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
