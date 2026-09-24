#!/usr/bin/env python3
"""Unity の EditMode / PlayMode テストと pytest を 1 回で回し、結果をまとめる。

    python tools/run_tests.py                     # EditMode → PlayMode → pytest
    python tools/run_tests.py --platform editmode # EditMode だけ
    python tools/run_tests.py --wait              # Unity が動いていたら終わるまで待つ
    python tools/run_tests.py --filter KCD.Tests.CampusSmokeTests
    python tools/run_tests.py --skip-unity        # pytest だけ（Unity を使えないとき）
    python tools/run_tests.py --dry-run           # 何を実行するかだけ出す

Unity.exe はマシンで 1 台だけ動かす約束なので、tasklist に Unity.exe がいれば起動しない
（--wait なら居なくなるまで待つ）。他のセッションの Unity は止めない。

各プラットフォームの結果は unity/logs/tests_<platform>.xml と .log に絶対パスで書く
（相対パスだと Unity はプロジェクトのフォルダから解決する）。XML の失敗・スキップ・未確定、
tools/check_unity_log.py がログに見つけたエラー、pytest の失敗は、どれか 1 つでもあれば exit 1。

batchmode で開くと、テストと無関係に次のファイルが書き換わる。実行前に clean だったものだけ
git で元に戻す（--keep-noise で戻さない）。
    Assets/Settings/UniversalRenderPipelineGlobalSettings.asset
    ProjectSettings/ProjectSettings.asset（preloadedAssets）
    ProjectSettings/TimeManager.asset（Fixed Timestep を 6000.6 の有理数の書き方に直す）
    *.mat（浮動小数点の書き方の揺れ）

終了コード: 0 = 全部通った / 1 = 失敗・スキップ・ログのエラーがある /
            2 = 走らせられなかった（Unity が無い・XML が出ない・時間切れ） / 3 = Unity が既に動いている
"""

from __future__ import annotations

import argparse
import dataclasses
import io
import os
import re
import subprocess
import sys
import time
import xml.etree.ElementTree as ET
from pathlib import Path

TOOLS = Path(__file__).resolve().parent
REPO = TOOLS.parent
PROJECT = REPO / "unity" / "KatsushikaCampusDays"
LOGS = REPO / "unity" / "logs"
TESTS = REPO / "tests"

sys.path.insert(0, str(TOOLS))
import check_unity_log  # noqa: E402  (tools/ を path に足してから読む)

UNITY_IMAGE = "Unity.exe"
PLATFORMS = ("EditMode", "PlayMode")

EXIT_OK = 0
EXIT_FAILED = 1
EXIT_ERROR = 2
EXIT_BUSY = 3

# Unity -runTests の終了コード
UNITY_TESTS_PASSED = 0
UNITY_TESTS_FAILED = 2
UNITY_RUN_ERROR = 3

# batchmode で開くだけで書き換わるファイル。プロジェクトからの相対パス（/ 区切り）。
NOISE_FILES = (
    "Assets/Settings/UniversalRenderPipelineGlobalSettings.asset",
    "ProjectSettings/ProjectSettings.asset",
    "ProjectSettings/TimeManager.asset",
)
NOISE_SUFFIXES = (".mat",)

MAX_MESSAGE_LINES = 12
MAX_STACK_LINES = 4


# ---------------------------------------------------------------- Unity の同時起動ガード


def parse_tasklist_csv(text: str, image: str = UNITY_IMAGE) -> list[int]:
    """tasklist /FO CSV /NH の出力から、image と同名のプロセスの PID を拾う。

    該当なしのときは「情報: ...」のような 1 行だけが返る（言語で文面が変わる）ので、
    引用符で始まる CSV 行だけを見る。
    """
    pids: list[int] = []
    for line in text.splitlines():
        line = line.strip()
        if not line.startswith('"'):
            continue
        fields = [field.strip('"') for field in line.split('","')]
        if len(fields) < 2 or fields[0].lower() != image.lower():
            continue
        try:
            pids.append(int(fields[1]))
        except ValueError:
            continue
    return pids


def running_unity_pids() -> list[int]:
    """いま動いている Unity.exe の PID。調べられなければ空。"""
    if os.name == "nt":
        try:
            raw = subprocess.run(
                ["tasklist", "/FI", f"IMAGENAME eq {UNITY_IMAGE}", "/FO", "CSV", "/NH"],
                capture_output=True,
                check=False,
                timeout=30,
            ).stdout
        except (OSError, subprocess.SubprocessError) as error:
            raise RuntimeError(f"tasklist を実行できません: {error}") from error
        return parse_tasklist_csv(raw.decode("mbcs", errors="replace"))

    try:
        raw = subprocess.run(
            ["ps", "-A", "-o", "pid=,comm="], capture_output=True, check=False, timeout=30
        ).stdout.decode("utf-8", errors="replace")
    except (OSError, subprocess.SubprocessError) as error:
        raise RuntimeError(f"ps を実行できません: {error}") from error
    pids = []
    for line in raw.splitlines():
        parts = line.split(None, 1)
        if len(parts) == 2 and Path(parts[1].strip()).name in ("Unity", UNITY_IMAGE):
            pids.append(int(parts[0]))
    return pids


def wait_for_free_unity(
    wait: bool, timeout_seconds: float, poll_seconds: float, out: io.TextIOBase, probe=running_unity_pids
) -> bool:
    """Unity が 1 台も動いていなければ True。wait なら居なくなるまで待つ。止めはしない。"""
    deadline = time.monotonic() + timeout_seconds
    announced = False
    while True:
        pids = probe()
        if not pids:
            return True
        if not wait:
            out.write(
                f"[BUSY] {UNITY_IMAGE} が動いています (PID {', '.join(map(str, pids))})。"
                "Unity は同時に 1 台だけなので起動しません。--wait で終わるまで待てます。\n"
            )
            return False
        if time.monotonic() >= deadline:
            out.write(f"[BUSY] {timeout_seconds:.0f} 秒待っても {UNITY_IMAGE} が終わりません (PID {pids})。\n")
            return False
        if not announced:
            out.write(f"[WAIT] {UNITY_IMAGE} (PID {pids}) が終わるのを待っています ...\n")
            out.flush()
            announced = True
        time.sleep(poll_seconds)


def remove_stale_lockfile(project: Path, out: io.TextIOBase, probe=running_unity_pids) -> None:
    """Unity が 1 台も動いていないのに残っている Temp/UnityLockfile を消す（強制終了の残り）。"""
    lockfile = project / "Temp" / "UnityLockfile"
    if lockfile.exists() and not probe():
        try:
            lockfile.unlink()
            out.write(f"[LOCK] 残っていた {lockfile} を消しました。\n")
        except OSError as error:
            out.write(f"[LOCK] {lockfile} を消せません: {error}\n")


# ---------------------------------------------------------------- Unity の場所


def msys_to_windows(path: str) -> str:
    """Git Bash の /c/Program Files/... を C:/Program Files/... に直す。それ以外はそのまま。"""
    match = re.match(r"^/([a-zA-Z])(/.*)?$", path)
    if match and os.name == "nt":
        return f"{match.group(1).upper()}:{match.group(2) or '/'}"
    return path


def editor_version(project: Path) -> str | None:
    version_file = project / "ProjectSettings" / "ProjectVersion.txt"
    if not version_file.exists():
        return None
    match = re.search(r"^m_EditorVersion:\s*(\S+)", version_file.read_text(encoding="utf-8"), re.MULTILINE)
    return match.group(1) if match else None


def resolve_unity(explicit: str | None, project: Path, environ=os.environ) -> Path | None:
    """--unity → 環境変数 UNITY_EXE / UNITY → Hub の既定の場所（ProjectVersion.txt の版）の順。"""
    candidates = [explicit, environ.get("UNITY_EXE"), environ.get("UNITY")]
    for candidate in candidates:
        if candidate:
            return Path(msys_to_windows(candidate))
    version = editor_version(project)
    if version is None:
        return None
    return Path(f"C:/Program Files/Unity/Hub/Editor/{version}/Editor/Unity.exe")


def unity_command(
    unity: Path, project: Path, platform: str, results: Path, log: Path, nographics: bool, test_filter: str | None
) -> list[str]:
    command = [
        str(unity),
        "-batchmode",
        "-projectPath",
        str(project),
        "-runTests",
        "-testPlatform",
        platform,
        "-testResults",
        str(results.resolve()),
        "-logFile",
        str(log.resolve()),
    ]
    if nographics:
        command.append("-nographics")
    if test_filter:
        command += ["-testFilter", test_filter]
    return command


# ---------------------------------------------------------------- 結果 XML


@dataclasses.dataclass(frozen=True)
class CaseResult:
    fullname: str
    result: str
    label: str
    message: str
    stack: str


@dataclasses.dataclass(frozen=True)
class RunSummary:
    total: int
    passed: int
    failed: int
    skipped: int
    inconclusive: int
    duration: float
    problems: tuple[CaseResult, ...]

    @property
    def ok(self) -> bool:
        return self.total > 0 and self.failed == 0 and self.skipped == 0 and self.inconclusive == 0


def _int(node: ET.Element, name: str) -> int:
    try:
        return int(node.get(name, "0"))
    except ValueError:
        return 0


def _text(node: ET.Element | None, path: str) -> str:
    if node is None:
        return ""
    found = node.find(path)
    return (found.text or "").strip() if found is not None else ""


def parse_results(path: Path) -> RunSummary:
    """NUnit 3 形式の結果 XML を読む。失敗・スキップ・未確定のテストと、失敗した準備処理を拾う。"""
    root = ET.parse(path).getroot()
    run = root if root.tag == "test-run" else root.find("test-run")
    if run is None:
        raise ValueError(f"{path} に test-run がありません")

    problems: list[CaseResult] = []
    for case in run.iter("test-case"):
        result = case.get("result", "")
        if result == "Passed":
            continue
        detail = case.find("failure") if result == "Failed" else case.find("reason")
        problems.append(
            CaseResult(
                fullname=case.get("fullname", case.get("name", "?")),
                result=result,
                label=case.get("label", ""),
                message=_text(detail, "message"),
                stack=_text(detail, "stack-trace"),
            )
        )

    # OneTimeSetUp などフィクスチャ側の失敗は test-case に載らないことがある。
    for suite in run.iter("test-suite"):
        if suite.get("result") == "Failed" and suite.get("site") in ("SetUp", "TearDown"):
            failure = suite.find("failure")
            problems.append(
                CaseResult(
                    fullname=suite.get("fullname", suite.get("name", "?")),
                    result="Failed",
                    label=suite.get("site", ""),
                    message=_text(failure, "message"),
                    stack=_text(failure, "stack-trace"),
                )
            )

    try:
        duration = float(run.get("duration", "0"))
    except ValueError:
        duration = 0.0

    return RunSummary(
        total=_int(run, "total"),
        passed=_int(run, "passed"),
        failed=_int(run, "failed"),
        skipped=_int(run, "skipped"),
        inconclusive=_int(run, "inconclusive"),
        duration=duration,
        problems=tuple(problems),
    )


def _clip(text: str, limit: int) -> list[str]:
    lines = [line.rstrip() for line in text.splitlines() if line.strip()]
    if len(lines) > limit:
        return lines[:limit] + [f"... (あと {len(lines) - limit} 行)"]
    return lines


def write_summary(platform: str, summary: RunSummary, out: io.TextIOBase, filtered: bool = False) -> None:
    out.write(
        f"=== {platform}: total {summary.total} / passed {summary.passed} / failed {summary.failed} / "
        f"skipped {summary.skipped} / inconclusive {summary.inconclusive} ({summary.duration:.1f} 秒) ===\n"
    )
    if summary.total == 0 and filtered:
        out.write("  [--] --filter に当たるテストはこのプラットフォームにありません\n")
    elif summary.total == 0:
        out.write("  [NG] テストが 1 本も走っていません（テストのアセンブリが読めていない）\n")
    for case in summary.problems:
        tag = case.result if not case.label else f"{case.result}/{case.label}"
        out.write(f"  [{tag}] {case.fullname}\n")
        for line in _clip(case.message, MAX_MESSAGE_LINES):
            out.write(f"      {line}\n")
        for line in _clip(case.stack, MAX_STACK_LINES):
            out.write(f"      | {line.strip()}\n")


# ---------------------------------------------------------------- batchmode のノイズ


def dirty_files(repo: Path, project: Path) -> set[str]:
    """プロジェクト配下で、変更のある追跡済みファイル（リポジトリからの相対パス、/ 区切り）。"""
    relative = project.relative_to(repo).as_posix()
    raw = subprocess.run(
        ["git", "-C", str(repo), "status", "--porcelain=v1", "-z", "--untracked-files=no", "--", relative],
        capture_output=True,
        check=True,
    ).stdout.decode("utf-8", errors="replace")
    return parse_porcelain_z(raw)


def parse_porcelain_z(raw: str) -> set[str]:
    """git status --porcelain=v1 -z の出力から、変更のあるパスを拾う（改名は新しい名前）。"""
    paths: set[str] = set()
    entries = raw.split("\0")
    index = 0
    while index < len(entries):
        entry = entries[index]
        index += 1
        if len(entry) < 4:
            continue
        status, path = entry[:2], entry[3:]
        if "R" in status or "C" in status:
            index += 1  # 次の要素は元の名前
        paths.add(path)
    return paths


def is_noise(path: str) -> bool:
    named = any(path == name or path.endswith("/" + name) for name in NOISE_FILES)
    return named or path.endswith(NOISE_SUFFIXES)


def noise_to_revert(before: set[str], after: set[str]) -> tuple[list[str], list[str]]:
    """実行で新しく汚れたファイルを、戻すノイズとそれ以外に分ける。実行前から汚れていたものは触らない。"""
    fresh = sorted(after - before)
    revert = [path for path in fresh if is_noise(path)]
    other = [path for path in fresh if not is_noise(path)]
    return revert, other


def snapshot_dirty(repo: Path, out: io.TextIOBase) -> set[str] | None:
    """実行前の変更ファイル。git で読めなければ None（そのときはノイズを戻さない）。"""
    try:
        return dirty_files(repo, PROJECT)
    except (OSError, subprocess.CalledProcessError) as error:
        out.write(f"[NOISE] git status を読めないので、実行後にノイズを戻しません: {error}\n")
        return None


def revert_noise(repo: Path, before: set[str], out: io.TextIOBase) -> None:
    try:
        after = dirty_files(repo, PROJECT)
    except (OSError, subprocess.CalledProcessError) as error:
        out.write(f"[NOISE] git status を読めないので戻しません: {error}\n")
        return
    revert, other = noise_to_revert(before, after)
    if revert:
        subprocess.run(["git", "-C", str(repo), "checkout", "--", *revert], check=False)
        for path in revert:
            out.write(f"[NOISE] 戻しました: {path}\n")
    kept_noise = sorted(path for path in before & after if is_noise(path))
    for path in kept_noise:
        out.write(f"[NOISE] 実行前から変更があったので戻していません: {path}\n")
    for path in other:
        out.write(f"[DIRTY] テストの実行で変わりました（戻していません）: {path}\n")


# ---------------------------------------------------------------- 実行


def run_unity(args: argparse.Namespace, platform: str, unity: Path, out: io.TextIOBase) -> "PlatformOutcome":
    LOGS.mkdir(parents=True, exist_ok=True)
    results = LOGS / f"tests_{platform.lower()}.xml"
    log = LOGS / f"tests_{platform.lower()}.log"
    nographics = args.nographics or platform == "EditMode"
    command = unity_command(unity, PROJECT, platform, results, log, nographics, args.filter)
    out.write(f"[RUN] {platform}: {subprocess.list2cmdline(command)}\n")
    out.flush()
    if args.dry_run:
        return PlatformOutcome(EXIT_OK, 0)

    # プラットフォームごとに見直す（EditMode の間に別のセッションが Unity を開いたかもしれない）。
    if not wait_for_free_unity(args.wait, args.wait_minutes * 60, 15, out):
        return PlatformOutcome(EXIT_BUSY, 0)
    remove_stale_lockfile(PROJECT, out)

    for stale in (results, log):
        if stale.exists():
            stale.unlink()

    before = None if args.keep_noise else snapshot_dirty(REPO, out)
    started = time.monotonic()
    process = subprocess.Popen(command)
    try:
        code = process.wait(timeout=args.timeout_minutes * 60)
    except subprocess.TimeoutExpired:
        # 自分が起動した 1 台だけを止める（Unity.exe を名前で止めると他のセッションまで落ちる）。
        process.kill()
        process.wait()
        out.write(f"[TIMEOUT] {platform} が {args.timeout_minutes} 分で終わらないので止めました。\n")
        remove_stale_lockfile(PROJECT, out)
        code = None
    finally:
        if before is not None:
            revert_noise(REPO, before, out)

    out.write(f"[DONE] {platform}: 終了コード {code}（{time.monotonic() - started:.0f} 秒）\n")
    return judge_run(platform, results, log, code, bool(args.filter), out)


@dataclasses.dataclass(frozen=True)
class PlatformOutcome:
    code: int
    total: int


def judge_run(
    platform: str, results: Path, log: Path, unity_code: int | None, filtered: bool, out: io.TextIOBase
) -> PlatformOutcome:
    """結果 XML・Unity の終了コード・ログから、1 プラットフォームの判定と走ったテストの本数を出す。"""
    worst = EXIT_OK
    total = 0
    if results.exists():
        try:
            summary = parse_results(results)
        except (ET.ParseError, ValueError) as error:
            out.write(f"[NG] {results} を読めません: {error}\n")
            worst = EXIT_ERROR
        else:
            total = summary.total
            write_summary(platform, summary, out, filtered)
            nothing_matched = filtered and summary.total == 0
            if not summary.ok and not nothing_matched:
                worst = EXIT_FAILED
            elif unity_code == UNITY_TESTS_FAILED and not nothing_matched:
                out.write("  [NG] Unity は失敗を返したのに XML に失敗がありません（ログを見てください）\n")
                worst = EXIT_FAILED
    else:
        out.write(f"[NG] 結果 XML が出ていません: {results}（ログの error CS を見てください）\n")
        worst = EXIT_ERROR

    if unity_code not in (UNITY_TESTS_PASSED, UNITY_TESTS_FAILED):
        worst = max(worst, EXIT_ERROR)

    log_code = check_unity_log.scan(log, out)
    if log_code == 1:
        worst = max(worst, EXIT_FAILED)
    elif log_code == 2:
        worst = max(worst, EXIT_ERROR)
    return PlatformOutcome(worst, total)


def run_pytest(args: argparse.Namespace, out: io.TextIOBase) -> int:
    if not TESTS.is_dir():
        out.write("[PYTEST] tests/ が無いので飛ばします。\n")
        return EXIT_OK
    # 対象のディレクトリは pytest.ini の testpaths（tests, blender/tests, tools/tests）に従う。
    command = [sys.executable, "-m", "pytest", "-q"]
    out.write(f"[RUN] pytest: {subprocess.list2cmdline(command)}\n")
    out.flush()
    if args.dry_run:
        return EXIT_OK
    code = subprocess.run(command, cwd=REPO, check=False).returncode
    if code == 5:
        out.write("[PYTEST] tests/ にテストがありません。\n")
        return EXIT_OK
    out.write(f"[PYTEST] 終了コード {code}\n")
    return EXIT_OK if code == 0 else EXIT_FAILED


def parse_args(argv: list[str]) -> argparse.Namespace:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--platform", choices=("all", "editmode", "playmode"), default="all")
    parser.add_argument("--filter", help="Unity の -testFilter にそのまま渡す（テスト名かクラス名の正規表現）")
    parser.add_argument("--unity", help="Unity.exe の場所（既定: 環境変数 UNITY_EXE / UNITY → Hub の既定の場所）")
    parser.add_argument("--wait", action="store_true", help="Unity が動いていたら終わるまで待つ")
    parser.add_argument("--wait-minutes", type=float, default=60.0, help="--wait で待つ上限（分）")
    parser.add_argument("--timeout-minutes", type=float, default=45.0, help="1 プラットフォームの上限（分）")
    parser.add_argument("--nographics", action="store_true", help="PlayMode も -nographics で走らせる")
    parser.add_argument("--keep-noise", action="store_true", help="batchmode のノイズを戻さない")
    parser.add_argument("--skip-unity", action="store_true", help="Unity を使わず pytest だけ")
    parser.add_argument("--skip-pytest", action="store_true", help="pytest を飛ばす")
    parser.add_argument("--dry-run", action="store_true", help="実行するコマンドを出すだけ")
    return parser.parse_args(argv)


def selected_platforms(choice: str) -> list[str]:
    if choice == "all":
        return list(PLATFORMS)
    return [platform for platform in PLATFORMS if platform.lower() == choice]


def main(argv: list[str]) -> int:
    args = parse_args(argv)
    out = io.TextIOWrapper(sys.stdout.buffer, encoding="utf-8", newline="\n", write_through=True)
    worst = EXIT_OK

    if not args.skip_unity:
        unity = resolve_unity(args.unity, PROJECT)
        if unity is None or (not args.dry_run and not unity.exists()):
            out.write(f"[NG] Unity.exe が見つかりません: {unity}（--unity か環境変数 UNITY で指定）\n")
            return EXIT_ERROR
        ran = 0
        for platform in selected_platforms(args.platform):
            outcome = run_unity(args, platform, unity, out)
            if outcome.code == EXIT_BUSY:
                return EXIT_BUSY
            worst = max(worst, outcome.code)
            ran += outcome.total
        if args.filter and ran == 0 and not args.dry_run:
            out.write(f"[NG] --filter {args.filter} に当たるテストがどのプラットフォームにもありません\n")
            worst = max(worst, EXIT_FAILED)

    if not args.skip_pytest:
        worst = max(worst, run_pytest(args, out))

    verdict = {EXIT_OK: "[OK] 全部通りました", EXIT_FAILED: "[NG] 失敗があります", EXIT_ERROR: "[NG] 走らせられませんでした"}
    out.write(verdict.get(worst, "[NG]") + "\n")
    out.flush()
    return worst


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
