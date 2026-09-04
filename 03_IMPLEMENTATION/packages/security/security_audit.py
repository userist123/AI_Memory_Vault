"""Lightweight OWASP-adjacent static heuristics scanner for source trees.

Read-only: scans files for common risk patterns (hardcoded secrets, unsafe
deserialization, disabled TLS verification, dangerous eval/exec use) and
produces a report. Findings can optionally be turned into RAW memory
candidates via AtomicMemoryExtractor-compatible dicts, but nothing is ever
written to canonical memory automatically.
"""
from __future__ import annotations

from dataclasses import asdict, dataclass, field
from pathlib import Path
from typing import Dict, List
import re

_PATTERNS = (
    ("hardcoded_secret", re.compile(r"(?i)(api[_-]?key|secret|password|token)\s*=\s*['\"][^'\"]{6,}['\"]")),
    ("disabled_tls_verify", re.compile(r"verify\s*=\s*False")),
    ("dangerous_eval", re.compile(r"\b(eval|exec)\s*\(")),
    ("unsafe_deserialization", re.compile(r"pickle\.loads?\(")),
    ("debug_enabled", re.compile(r"(?i)debug\s*=\s*True")),
    ("shell_injection_risk", re.compile(r"subprocess\.(run|call|Popen)\([^)]*shell\s*=\s*True")),
)

_SKIP_DIRS = {".git", "__pycache__", "node_modules", ".venv", "venv"}
_TEXT_EXTENSIONS = {".py", ".js", ".ts", ".cs", ".ps1", ".cjs", ".json", ".yml", ".yaml"}


@dataclass
class Finding:
    rule: str
    file: str
    line: int
    snippet: str


@dataclass
class SecurityAuditReport:
    target: str
    files_scanned: int = 0
    findings: List[Finding] = field(default_factory=list)

    def to_dict(self) -> dict:
        return {"target": self.target, "files_scanned": self.files_scanned,
                "findings": [asdict(f) for f in self.findings]}

    def to_candidates(self, category: str = "security_audit") -> List[Dict[str, str]]:
        """Return RAW-compatible candidate dicts (not yet queued or proposed)."""
        return [
            {
                "type": "lesson",
                "category": category,
                "content": f"Security finding [{f.rule}] in {f.file}:{f.line} -> {f.snippet.strip()}",
            }
            for f in self.findings
        ]


class SecurityAuditor:
    def __init__(self, target_root: str | Path):
        self.root = Path(target_root).resolve()

    def run(self) -> SecurityAuditReport:
        report = SecurityAuditReport(target=str(self.root))
        for path in self.root.rglob("*"):
            if not path.is_file() or any(part in _SKIP_DIRS for part in path.parts):
                continue
            if path.suffix.lower() not in _TEXT_EXTENSIONS:
                continue
            report.files_scanned += 1
            try:
                lines = path.read_text(encoding="utf-8", errors="replace").splitlines()
            except OSError:
                continue
            rel = str(path.relative_to(self.root)) if self.root in path.parents or path == self.root else str(path)
            for lineno, line in enumerate(lines, start=1):
                for rule, pattern in _PATTERNS:
                    if pattern.search(line):
                        report.findings.append(Finding(rule=rule, file=rel, line=lineno, snippet=line[:200]))
        return report
