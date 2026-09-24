"""ブラウザのコンソールに出たメッセージを「失敗」「許す」「記録だけ」に分ける。

エラー（console.error、捕まらなかった例外、本文が C# の例外の形のもの）は、下の許可リストに
理由付きで載っているもの以外をすべて失敗にする。警告は、許可リストに載っている既知のものを
「許す」に、それ以外を「未知の警告」に分ける。未知の警告は strict のときだけ失敗にする。
"""

from __future__ import annotations

import re
from dataclasses import dataclass, field

# 失敗にする種類。Playwright の ConsoleMessage.type と、pageerror を "pageerror" として扱う。
ERROR_TYPES = frozenset({"error", "assert", "pageerror"})
WARNING_TYPES = frozenset({"warning"})

# C# の例外は、ログの種類によっては console.log / console.warn に流れる。本文の形でも拾う。
EXCEPTION_TEXT = re.compile(r"\b[A-Z]\w*Exception(?::|\s+at\s)")


@dataclass(frozen=True)
class ConsoleEntry:
    type: str
    text: str
    url: str = ""


def is_error(entry: ConsoleEntry) -> bool:
    return entry.type in ERROR_TYPES or EXCEPTION_TEXT.search(entry.text) is not None


def is_warning(entry: ConsoleEntry) -> bool:
    return entry.type in WARNING_TYPES and not is_error(entry)


@dataclass(frozen=True)
class AllowRule:
    """許すメッセージ。pattern は本文への re.search、types はこの規則を当てる種類。

    例外の形の本文は、types が警告だけの規則では許さない（エラーの規則にだけ当たる）。
    """

    pattern: str
    reason: str
    types: frozenset[str] = WARNING_TYPES

    def matches(self, entry: ConsoleEntry) -> bool:
        if is_error(entry):
            if not self.types & ERROR_TYPES:
                return False
        elif entry.type not in self.types:
            return False
        return re.search(self.pattern, entry.text) is not None


# 既知のメッセージの許可リスト。足すときは、なぜ害が無いか・どの環境で出るか・追っている Issue を
# reason に書く。直ったら消す（消し忘れても害は無いが、リストが嘘になる）。
ALLOWLIST: tuple[AllowRule, ...] = (
    AllowRule(
        r"Shader 'Hidden/Universal Render Pipeline/Edge Adaptive Spatial Upsampling' "
        r"is not supported or has been stripped",
        "URP の FSR 拡大シェーダーがビルドから削られている。後処理が止まる不具合として #72 で追う。"
        "起動と操作は続くので、このスモークでは失敗にしない"),
    AllowRule(
        r"Manual synchronization of Unity Application\.persistentDataPath via "
        r"JS_FileSystem_Sync\(\) is deprecated",
        "Unity 6 の予告。テンプレートの createUnityInstance に autoSyncPersistentDataPath を"
        "渡していないと出る。セーブは今の方式で書けている"),
    AllowRule(
        r"^Failed to create agent because there is no valid NavMesh",
        "キャンパスの NPC 4 体の NavMeshAgent が作れない。#63 で追う。NPC が歩かないだけで"
        "プレイヤーの操作は続く"),
    AllowRule(
        r"^getFrequency\(\) is not supported for compressed sound\.",
        "Unity の WebGL 音声層が、ブラウザが展開する圧縮音声の周波数を聞いたときに出す。"
        "再生は続く。音を出せる Chromium でだけ出て、headless の Edge では出ない"),
    AllowRule(
        r"^Additional Lights Cookie Format \(GrayscaleHigh\) is not supported by the platform\. "
        r"Falling back to 32-bit format",
        "SwiftShader（GPU の無い CI）だけで出る。URP がライトクッキーの形式を RGBA8 に"
        "落として続ける。実 GPU では出ない"),
)


@dataclass
class ConsoleVerdict:
    errors: list[ConsoleEntry] = field(default_factory=list)
    allowed: list[tuple[ConsoleEntry, AllowRule]] = field(default_factory=list)
    warnings: list[ConsoleEntry] = field(default_factory=list)

    @property
    def ok(self) -> bool:
        return not self.errors

    @property
    def ok_strict(self) -> bool:
        return not self.errors and not self.warnings


def classify(entries: list[ConsoleEntry],
             allowlist: tuple[AllowRule, ...] = ALLOWLIST) -> ConsoleVerdict:
    verdict = ConsoleVerdict()
    for entry in entries:
        if not is_error(entry) and not is_warning(entry):
            continue
        rule = next((r for r in allowlist if r.matches(entry)), None)
        if rule is not None:
            verdict.allowed.append((entry, rule))
        elif is_error(entry):
            verdict.errors.append(entry)
        else:
            verdict.warnings.append(entry)
    return verdict
