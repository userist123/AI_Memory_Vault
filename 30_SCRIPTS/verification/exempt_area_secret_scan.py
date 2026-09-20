"""Scan the 83% of the repository that gitleaks is told to skip.

`.gitleaks.toml` exempts `.agents/`, `50_ARTIFACTS/`, `07_EVALUATION/`,
`06_INBOX/RAW_IMPORTS/`, `20_TESTS/` and the test trees — 16,675 of 20,030
tracked files. The exemptions are reasonable on their own terms: a corpus of
3,661 imported skills, a security-review skill whose whole job is to list
secret patterns, and test fixtures full of deliberately fake keys would drown a
scanner in false positives.

The result, though, is that most of a public repository is unscanned. This
closes that without weakening gitleaks: a small set of high-confidence patterns
— the ones that are almost never a coincidence — run over exactly the exempt
areas, with a committed allowlist naming each known-benign hit and why.

    python 30_SCRIPTS/verification/exempt_area_secret_scan.py
"""
from __future__ import annotations

import json
import re
import subprocess
import sys
from pathlib import Path

REPO = Path(__file__).resolve().parents[2]
ALLOWLIST = REPO / "20_TESTS" / "fixtures" / "exempt_area_secret_allowlist.json"
MAX_FILE_BYTES = 3_000_000

#: Paths `.gitleaks.toml` exempts. Kept here rather than parsed from the TOML so
#: a change to either file shows up as a disagreement in the tests.
EXEMPT_PATTERNS = (
    r"(^|/)\.agents/", r"(^|/)50_ARTIFACTS/", r"(^|/)07_EVALUATION/",
    r"(^|/)06_INBOX/RAW_IMPORTS/", r"(^|/)20_TESTS/", r"(^|/)40_EXPERIMENTS/",
    r"(^|/)02_PRODUCT/.*/tests/", r"(^|/)tests/", r"(^|/)projects/.*/tests/",
)

#: Only shapes that are almost never a coincidence. A generic `password = "..."`
#: is deliberately absent: it appears in documentation everywhere, and a rule
#: that fires constantly is a rule that gets switched off.
RULES: dict[str, re.Pattern[str]] = {
    "private_key": re.compile(r"-----BEGIN (?:RSA |EC |OPENSSH |PGP |DSA )?PRIVATE KEY-----"),
    "aws_access_key": re.compile(r"\b(?:AKIA|ASIA)[A-Z0-9]{16}\b"),
    "github_token": re.compile(r"\b(?:ghp|gho|ghs|ghu)_[A-Za-z0-9]{36}\b|\bgithub_pat_[A-Za-z0-9_]{60,}"),
    "slack_token": re.compile(r"\bxox[abprs]-[A-Za-z0-9-]{10,}"),
    "google_api_key": re.compile(r"\bAIza[0-9A-Za-z_-]{35}\b"),
    "stripe_live_key": re.compile(r"\b(?:sk|rk)_live_[0-9A-Za-z]{20,}"),
    "openai_key": re.compile(r"\bsk-(?:proj-)?[A-Za-z0-9_-]{32,}\b"),
    "anthropic_key": re.compile(r"\bsk-ant-[A-Za-z0-9_-]{30,}"),
}

_EXEMPT = tuple(re.compile(p) for p in EXEMPT_PATTERNS)


def is_exempt(path: str) -> bool:
    return any(pattern.search(path) for pattern in _EXEMPT)


def exempt_files() -> list[str]:
    tracked = subprocess.run(["git", "-C", str(REPO), "ls-files"], capture_output=True,
                             text=True, encoding="utf-8", check=True).stdout.splitlines()
    return [path for path in tracked if is_exempt(path)]


def load_allowlist() -> dict[str, set[str]]:
    if not ALLOWLIST.exists():
        return {}
    data = json.loads(ALLOWLIST.read_text(encoding="utf-8"))
    return {entry["path"]: set(entry["rules"]) for entry in data.get("entries", [])}


def scan_text(text: str) -> set[str]:
    return {name for name, pattern in RULES.items() if pattern.search(text)}


def scan(allowlist: dict[str, set[str]] | None = None) -> list[dict]:
    allowlist = load_allowlist() if allowlist is None else allowlist
    findings: list[dict] = []
    for relative in exempt_files():
        path = REPO / relative
        try:
            if path.stat().st_size > MAX_FILE_BYTES:
                continue
            text = path.read_text(encoding="utf-8", errors="ignore")
        except OSError:
            continue
        allowed = allowlist.get(relative, set())
        for rule in sorted(scan_text(text) - allowed):
            findings.append({"path": relative, "rule": rule})
    return findings


def main() -> int:
    files = exempt_files()
    findings = scan()
    print(f"EXEMPT_FILES={len(files)}")
    print(f"FINDINGS={len(findings)}")
    for finding in findings[:40]:
        # The path and the rule, never the value: printing a secret to a CI log
        # would publish the thing the scan exists to protect.
        print(f"  {finding['rule']}: {finding['path']}")
    return 1 if findings else 0


if __name__ == "__main__":
    sys.exit(main())
