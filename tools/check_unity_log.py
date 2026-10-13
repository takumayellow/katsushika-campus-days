#!/usr/bin/env python3
"""Unity のバッチログから、失敗の証拠になる行だけを拾う。

    python tools/check_unity_log.py unity/logs/import.log unity/logs/scene.log
    python tools/check_unity_log.py unity/logs/tests_editmode.xml

拾うもの:
    - コンパイルエラー  ... "error CS"
    - コンパイル警告    ... "warning CS"（非推奨 API や名前の隠蔽も直す, #64）
    - 実行時の例外      ... "Exception" を含む行とスタックの先頭
    - NavMesh の失敗    ... "Failed to create agent" / "can only be called on an active agent"
                            （NavMesh に載れなかった NPC。#63 の 4 体がこれ）
    - シェーダの欠け    ... "Shader ... not found" / "シェーダ ... が見つかりません"
    - 編集時の複製      ... "Instantiating material due to calling renderer.material"
                            （シーンに複製したマテリアルが埋め込まれる, #64）
    - テストの失敗      ... .xml を渡すと NUnit の結果として読み、失敗したテストを拾う
    - 進捗              ... "[KCD] " で始まる行
    - シーンの保存      ... "Saved scene:"
    - ビルド結果        ... "ビルド結果:"

上の失敗に当たっても、KNOWN_LINES に理由付きで載せた行は失敗に数えない（[KNOWN] として件数だけ出す）。

終了コード: 0 = 失敗の証拠が無い / 1 = どれかがある / 2 = ログが読めない
"""

from __future__ import annotations

import io
import re
import sys
import xml.etree.ElementTree as ET
from pathlib import Path
from typing import NamedTuple

ERROR = re.compile(r"error CS\d+")
WARNING = re.compile(r"warning CS\d+")
EXCEPTION = re.compile(r"\b\w*Exception\b")
# NavMeshAgent が NavMesh に載れなかったときに Unity が出す行。警告扱いでも NPC は動かない。
NAVMESH = re.compile(r"Failed to create agent|can only be called on an active agent")
# シェーダが見つからないと、そのマテリアルはピンクになるかビルドに入らない。
SHADER = re.compile(r"\bshader\b.*\bnot found\b|シェーダ.*が見つかりません", re.IGNORECASE)
# 編集時に renderer.material / MeshFilter.mesh を呼ぶと、複製がシーンに埋め込まれて残る。
LEAK = re.compile(r"Instantiating (?:material|mesh) due to calling")
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


class KnownLine(NamedTuple):
    """直せない理由が分かっていて、失敗に数えない行。pattern は前後の空白を落とした行全体に当てる。"""

    pattern: re.Pattern[str]
    reason: str


# 足すときは、実際のログの行と、直せない理由を必ず書く。
KNOWN_LINES: tuple[KnownLine, ...] = (
    KnownLine(
        re.compile(
            r"^Shader 'Hidden/Terrain/Terrain (?:Standard 4 Layers URP|Simple)': "
            r"dependency '(?:AddPassShader|BaseMapGenShader)' "
            r"shader 'Hidden/Hidden/Terrain/Terrain (?:Standard 4 Layers URP|Simple)_(?:AddPass|BaseMapGen)' not found$"
        ),
        "URP と Shader Graph のパッケージにある Terrain 用テンプレート（GraphTemplates/Terrain*.shadergraph）を"
        "取り込み直すときに出る。テンプレートの名前がもう Hidden/Terrain/ で始まるのに、URP の Terrain の"
        "サブターゲットが依存先を \"Hidden/{Name}_AddPass\" / \"Hidden/{Name}_BaseMapGen\" と名付けるので、"
        "Hidden/Hidden/... を探して見つからない。パッケージの中身は書き換えられず、プロジェクトは Terrain を"
        "使わない（ビルドにも入らない）。ビルド先の切り替えなどでテンプレートを取り込み直すと 1 回に 8 行出る (#64)",
    ),
    KnownLine(
        re.compile(
            r"^Shader '(?:Universal Render Pipeline/Terrain/Lit|Hidden/Terrain/Terrain (?:Standard 4 Layers URP|Simple)"
            r"|Universal Render Pipeline/Nature/SpeedTree\d+)': dependency '\w+' "
            r"shader '(?:Hidden/Universal Render Pipeline/Terrain/Lit \([^)]+\)"
            r"|Universal Render Pipeline/Nature/SpeedTree\d+ Billboard)' not found$"
        ),
        "新しい Library を初めて取り込むとき、URP の Terrain / SpeedTree のシェーダが依存先のシェーダより先に"
        "取り込まれて出る（worktree を作った直後の初回取り込みだけで、2 回目からは出ない）。"
        "プロジェクトは Terrain も SpeedTree も使わない (#64)",
    ),
)


def read(path: Path) -> list[str]:
    raw = path.read_bytes()
    for encoding in ("utf-8", "cp932", "latin-1"):
        try:
            return raw.decode(encoding).splitlines()
        except UnicodeDecodeError:
            continue
    return raw.decode("latin-1").splitlines()


def known_reason(line: str) -> str | None:
    """KNOWN_LINES に当たる行なら理由を、当たらなければ None を返す。"""
    text = line.strip()
    for known in KNOWN_LINES:
        if known.pattern.search(text):
            return known.reason
    return None


def scan(path: Path, out: io.TextIOBase) -> int:
    if not path.exists():
        out.write(f"[NG] ログがありません: {path}\n")
        return 2
    if path.suffix.lower() == ".xml":
        return scan_results(path, out)

    lines = read(path)
    known: dict[str, int] = {}
    checked: list[str] = []
    for line in lines:
        reason = known_reason(line)
        if reason is None:
            checked.append(line)
        else:
            known[reason] = known.get(reason, 0) + 1

    errors = [line for line in checked if ERROR.search(line)]
    warnings = [line for line in checked if WARNING.search(line)]
    exceptions = [
        line
        for line in checked
        if EXCEPTION.search(line) and not any(token in line for token in IGNORED)
    ]
    navmesh = [line for line in checked if NAVMESH.search(line)]
    shaders = [line for line in checked if SHADER.search(line)]
    leaks = [line for line in checked if LEAK.search(line)]
    saved = [m.group(1) for line in lines for m in [SAVED.search(line)] if m]
    results = [m.group(1) for line in lines for m in [RESULT.search(line)] if m]
    progress = [line for line in lines if "[KCD] " in line]

    out.write(f"=== {path} ({len(lines)} 行) ===\n")
    out.write(f"error CS : {len(errors)}\n")
    out.write(f"warn CS  : {len(warnings)}\n")
    out.write(f"Exception: {len(exceptions)}\n")
    out.write(f"NavMesh  : {len(navmesh)}\n")
    out.write(f"Shader   : {len(shaders)}\n")
    out.write(f"Leak     : {len(leaks)}\n")
    out.write(f"Known    : {sum(known.values())}\n")

    for tag, found in (
        ("CS", errors),
        ("WARN", warnings),
        ("EX", exceptions),
        ("NAV", navmesh),
        ("SHADER", shaders),
        ("LEAK", leaks),
    ):
        for line in found[:20]:
            out.write(f"  [{tag}] {line.strip()}\n")
    for reason, count in known.items():
        out.write(f"  [KNOWN] {count} 行: {reason}\n")
    for line in progress:
        out.write(f"  {line.strip()}\n")
    for scene in saved:
        out.write(f"  [SCENE] {scene}\n")
    for result in results:
        out.write(f"  [BUILD] {result}\n")

    failed_builds = [r for r in results if "Succeeded" not in r]
    failed = errors or warnings or exceptions or navmesh or shaders or leaks or failed_builds
    return 1 if failed else 0


def scan_results(path: Path, out: io.TextIOBase) -> int:
    """NUnit 3 形式の結果 XML から、失敗したテストと失敗した準備・後始末を拾う。"""
    try:
        root = ET.parse(path).getroot()
    except ET.ParseError as error:
        out.write(f"[NG] 結果 XML を読めません: {path}: {error}\n")
        return 2
    run = root if root.tag == "test-run" else root.find("test-run")
    if run is None:
        out.write(f"[NG] {path} に test-run がありません\n")
        return 2

    failures = [
        case.get("fullname", case.get("name", "?"))
        for case in run.iter("test-case")
        if case.get("result") == "Failed"
    ]
    # OneTimeSetUp などフィクスチャ側の失敗は test-case に載らないことがある。
    failures += [
        f"{suite.get('fullname', suite.get('name', '?'))} ({suite.get('site')})"
        for suite in run.iter("test-suite")
        if suite.get("result") == "Failed" and suite.get("site") in ("SetUp", "TearDown")
    ]

    total = sum(1 for _ in run.iter("test-case"))
    out.write(f"=== {path} (テスト {total} 本) ===\n")
    out.write(f"Failed   : {len(failures)}\n")
    for name in failures[:20]:
        out.write(f"  [FAIL] {name}\n")
    if total == 0:
        out.write("  [NG] テストが 1 本も走っていません（テストのアセンブリが読めていない）\n")
    elif run.get("result", "").startswith("Failed") and not failures:
        out.write("  [NG] test-run は Failed なのに、失敗したテストが見つかりません\n")

    return 1 if failures or total == 0 or run.get("result", "").startswith("Failed") else 0


def main(argv: list[str]) -> int:
    if len(argv) < 2:
        sys.stderr.write(__doc__ or "")
        return 2

    out = io.TextIOWrapper(sys.stdout.buffer, encoding="utf-8", newline="\n")
    worst = 0
    for name in argv[1:]:
        worst = max(worst, scan(Path(name), out))

    out.write("[OK] 失敗の証拠はありません\n" if worst == 0 else "[NG] 上の行を直してください\n")
    out.flush()
    return worst


if __name__ == "__main__":
    sys.exit(main(sys.argv))
