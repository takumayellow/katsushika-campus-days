#!/usr/bin/env python3
"""Unity のバッチログから、失敗の証拠になる行だけを拾う。

    python tools/check_unity_log.py unity/logs/import.log unity/logs/scene.log

拾うもの:
    - コンパイルエラー  ... "error CS"
    - 実行時の例外      ... "Exception" を含む行とスタックの先頭
    - NavMesh の失敗    ... "Failed to create agent" / "can only be called on an active agent"
                            （NavMesh に載れなかった NPC。#63 の 4 体がこれ）
    - 進捗              ... "[KCD] " で始まる行
    - シーンの保存      ... "Saved scene:"
    - ビルド結果        ... "ビルド結果:"

終了コード: 0 = エラーも例外も NavMesh の失敗も無い / 1 = どれかがある / 2 = ログが読めない
"""

from __future__ import annotations

import io
import re
import sys
from pathlib import Path

ERROR = re.compile(r"error CS\d+")
EXCEPTION = re.compile(r"\b\w*Exception\b")
# NavMeshAgent が NavMesh に載れなかったときに Unity が出す行。警告扱いでも NPC は動かない。
NAVMESH = re.compile(r"Failed to create agent|can only be called on an active agent")
SAVED = re.compile(r"Saved scene:\s*(\S+)")
RESULT = re.compile(r"ビルド結果:\s*(\S+)")

# Unity が正常系で出す、例外とは無関係な行
IGNORED = (
    "ExceptionHandl",
    "StackTraceUtility",
    "ExitDontLaunchBugReporter",
    # Test Framework がテスト中のログすべてに付けるフレーム（LogAssert.Expect で待つ警告にも付く）
    "UnityLogCheckDelegatingCommand:CaptureException",
    "-executeMethod",
    " kb	",
)


def read(path: Path) -> list[str]:
    raw = path.read_bytes()
    for encoding in ("utf-8", "cp932", "latin-1"):
        try:
            return raw.decode(encoding).splitlines()
        except UnicodeDecodeError:
            continue
    return raw.decode("latin-1").splitlines()


def scan(path: Path, out: io.TextIOBase) -> int:
    if not path.exists():
        out.write(f"[NG] ログがありません: {path}\n")
        return 2

    lines = read(path)
    errors = [line for line in lines if ERROR.search(line)]
    exceptions = [
        line
        for line in lines
        if EXCEPTION.search(line) and not any(token in line for token in IGNORED)
    ]
    navmesh = [line for line in lines if NAVMESH.search(line)]
    saved = [m.group(1) for line in lines for m in [SAVED.search(line)] if m]
    results = [m.group(1) for line in lines for m in [RESULT.search(line)] if m]
    progress = [line for line in lines if "[KCD] " in line]

    out.write(f"=== {path} ({len(lines)} 行) ===\n")
    out.write(f"error CS : {len(errors)}\n")
    out.write(f"Exception: {len(exceptions)}\n")
    out.write(f"NavMesh  : {len(navmesh)}\n")

    for line in errors[:20]:
        out.write(f"  [CS] {line.strip()}\n")
    for line in exceptions[:20]:
        out.write(f"  [EX] {line.strip()}\n")
    for line in navmesh[:20]:
        out.write(f"  [NAV] {line.strip()}\n")
    for line in progress:
        out.write(f"  {line.strip()}\n")
    for scene in saved:
        out.write(f"  [SCENE] {scene}\n")
    for result in results:
        out.write(f"  [BUILD] {result}\n")

    failed_builds = [r for r in results if "Succeeded" not in r]
    return 1 if errors or exceptions or navmesh or failed_builds else 0


def main(argv: list[str]) -> int:
    if len(argv) < 2:
        sys.stderr.write(__doc__ or "")
        return 2

    out = io.TextIOWrapper(sys.stdout.buffer, encoding="utf-8", newline="\n")
    worst = 0
    for name in argv[1:]:
        worst = max(worst, scan(Path(name), out))

    out.write("[OK] エラーも例外も NavMesh の失敗もありません\n" if worst == 0 else "[NG] 上の行を直してください\n")
    out.flush()
    return worst


if __name__ == "__main__":
    sys.exit(main(sys.argv))
