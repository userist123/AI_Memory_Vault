#!/usr/bin/env python3
"""Reject known malformed generated citation markers."""
from __future__ import annotations

from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
DEFAULT_PATHS = ("README.md", "00_CORE", "01_KNOWLEDGE", "02_PROJECTS", "03_PROCEDURES",
                 "04_MEMORY", "05_RESOURCES", "06_INBOX/DERIVED", "07_EVALUATION")
BAD_MARKERS = ("fileciteturn", "filecite")


def find_malformed(paths: tuple[str, ...] = DEFAULT_PATHS) -> list[str]:
    findings: list[str] = []
    for relative in paths:
        path = ROOT / relative
        files = (path,) if path.is_file() else path.rglob("*") if path.is_dir() else ()
        for candidate in files:
            if not candidate.is_file() or candidate.suffix.lower() in {".pdf", ".pyc", ".ipynb"}:
                continue
            try:
                text = candidate.read_text(encoding="utf-8")
            except (OSError, UnicodeDecodeError):
                continue
            for line_number, line in enumerate(text.splitlines(), 1):
                if any(marker in line.lower() for marker in BAD_MARKERS):
                    findings.append(f"{candidate.relative_to(ROOT)}:{line_number}")
    return sorted(set(findings))


def main() -> int:
    findings = find_malformed()
    if findings:
        print("MALFORMED_GENERATED_ARTIFACTS=" + ",".join(findings))
        return 1
    print("MALFORMED_GENERATED_ARTIFACTS=0")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
