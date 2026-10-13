"""スモークの結果（確認項目の合否と、読み込み時間・ヒープなどの数値）をまとめて書き出す。"""

from __future__ import annotations

import json
from dataclasses import asdict, dataclass, field
from pathlib import Path


@dataclass(frozen=True)
class Check:
    name: str
    ok: bool
    detail: str = ""


@dataclass
class Report:
    url: str
    target: str
    checks: list[Check] = field(default_factory=list)
    metrics: dict = field(default_factory=dict)
    screenshots: list[str] = field(default_factory=list)

    @property
    def ok(self) -> bool:
        return bool(self.checks) and all(check.ok for check in self.checks)

    def add(self, name: str, ok: bool, detail: str = "") -> bool:
        self.checks.append(Check(name, bool(ok), detail))
        return bool(ok)

    def failed(self) -> list[Check]:
        return [check for check in self.checks if not check.ok]

    def to_dict(self) -> dict:
        return {
            "ok": self.ok,
            "url": self.url,
            "target": self.target,
            "checks": [asdict(check) for check in self.checks],
            "metrics": self.metrics,
            "screenshots": self.screenshots,
        }

    def write_json(self, path: Path) -> None:
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text(json.dumps(self.to_dict(), ensure_ascii=False, indent=2) + "\n",
                        encoding="utf-8")

    def summary(self) -> str:
        lines = [f"{'OK' if self.ok else 'NG'}  {self.target}  {self.url}"]
        for check in self.checks:
            mark = "ok" if check.ok else "NG"
            lines.append(f"  [{mark}] {check.name}: {check.detail}")
        timings = self.metrics.get("timings_ms", {})
        if timings:
            lines.append("  timings(ms): " + ", ".join(f"{k}={v}" for k, v in timings.items()))
        for label, heap in self.metrics.get("heap", {}).items():
            lines.append(f"  heap[{label}]: " + ", ".join(f"{k}={format_bytes(v)}"
                                                         for k, v in heap.items()))
        return "\n".join(lines)


def format_bytes(value) -> str:
    if not isinstance(value, (int, float)):
        return str(value)
    mib = value / (1024 * 1024)
    return f"{mib:.1f}MiB"
