"""Scan imported material for text that tries to act as an instruction.

Everything under `06_INBOX/`, `.agents/skills/` and the imported project trees
arrives from outside. An agent reads it as data; the danger is content written
to be read as a command — a fake `SYSTEM:` header, a line telling the reader to
ignore what came before, a claim that the owner already approved something, or
characters that are invisible in a diff and in Obsidian.

This does not judge intent. It reports where such text is, so a human decides.
Findings are allowlisted by exact path and rule in
`20_TESTS/fixtures/untrusted_content_allowlist.json`, with a reason: a security
skill that carries these patterns as detection rules is a legitimate hit, and
saying so in a committed file is the difference between a guard and noise.

    python 30_SCRIPTS/verification/untrusted_content_guard.py
    python 30_SCRIPTS/verification/untrusted_content_guard.py --json
"""
from __future__ import annotations

import argparse
import json
import re
import subprocess
import sys
from pathlib import Path

REPO = Path(__file__).resolve().parents[2]
ALLOWLIST = REPO / "20_TESTS" / "fixtures" / "untrusted_content_allowlist.json"

#: Trees whose content came from outside this repository.
UNTRUSTED_ROOTS = ("06_INBOX", ".agents/skills", "02_PRODUCT/projects/imported")

TEXT_SUFFIXES = {".md", ".txt", ".json", ".jsonl", ".yaml", ".yml", ".py", ".js", ".ts", ".sh", ".ps1"}

#: Executables have no business in a tree an agent reads for knowledge.
EXECUTABLE_SUFFIXES = {".exe", ".dll", ".scr", ".msi", ".bat", ".cmd", ".com", ".vbs", ".jar"}

RULES: dict[str, re.Pattern[str]] = {
    # Text addressed to the reader as a command.
    "override_instruction": re.compile(
        r"(?:ignore|disregard|forget)\s+(?:all\s+|any\s+|the\s+)?(?:previous|prior|above|earlier|system)\s+"
        r"(?:instruction|prompt|rule|direction|message)|"
        r"ignoră\s+(?:instrucțiunile|regulile)\s+(?:de\s+mai\s+sus|anterioare)",
        re.I),
    "role_header": re.compile(
        r"^\s*(?:SYSTEM|ASSISTANT|DEVELOPER|USER)\s*:|^\s*<\s*/?\s*(?:system|assistant|developer)\s*>|"
        r"^\s*\[/?INST\]|^\s*###\s*(?:Instruction|System)\b",
        re.I | re.M),
    "identity_rewrite": re.compile(
        r"you\s+are\s+now\s+(?:a|an|the)\b|from\s+now\s+on,?\s+you\s+(?:are|will|must)\b|"
        r"de\s+acum\s+(?:ești|esti)\s+", re.I),
    # Claims of an authority the content cannot hold.
    "false_authorization": re.compile(
        r"(?:the\s+)?(?:user|owner|admin(?:istrator)?)\s+(?:has\s+)?(?:already\s+)?"
        r"(?:approved|authorized|authorised|confirmed|permitted)\b|"
        r"you\s+(?:now\s+)?have\s+(?:full\s+)?permission\s+to\b|"
        r"proprietarul\s+a\s+aprobat", re.I),
    "exfiltration_request": re.compile(
        r"(?:send|post|upload|exfiltrate)\s+(?:the\s+)?(?:api[_\s-]?key|token|password|credential|secret|\.env)\b|"
        r"curl\s+[^\n|]*\|\s*(?:ba)?sh\b", re.I),
    # Characters that are invisible where a human would review them.
    "hidden_characters": re.compile("[​‌‎‏‪-‮⁠-⁤⁦-⁩﻿󠀀-󠁿]"),
    # An encoded command line, the shape a dropper takes.
    "encoded_command": re.compile(r"powershell(?:\.exe)?\s+[^\n]*-e(?:nc|ncodedcommand)\b|FromBase64String\s*\(", re.I),
}


#: Rules that fail the build. Each is rare in honest material: an executable in
#: a knowledge tree, characters invisible to a reviewer, a request to send a
#: credential somewhere, an encoded command line.
BLOCKING_RULES = frozenset({
    "executable_in_untrusted_tree", "hidden_characters", "exfiltration_request", "encoded_command",
})

#: Reported, never blocking. `SYSTEM:` headers and "ignore previous
#: instructions" are the working vocabulary of prompt-engineering and security
#: skills; 153 files carry a role header legitimately. Counting them is useful,
#: failing on them would train everyone to ignore the guard.
REPORT_ONLY_RULES = frozenset(RULES) - BLOCKING_RULES


def tracked_files() -> list[str]:
    out = subprocess.run(["git", "-C", str(REPO), "ls-files"] + list(UNTRUSTED_ROOTS),
                         capture_output=True, text=True, encoding="utf-8", check=True).stdout
    return out.splitlines()


def load_allowlist() -> dict[str, set[str]]:
    if not ALLOWLIST.exists():
        return {}
    data = json.loads(ALLOWLIST.read_text(encoding="utf-8"))
    return {entry["path"]: set(entry["rules"]) for entry in data.get("entries", [])}


def scan_text(text: str, rules: dict[str, re.Pattern[str]] = RULES) -> dict[str, int]:
    """Rule name -> number of matches. Separated from the filesystem so it is testable."""
    # A byte-order mark at position 0 is how an editor saved the file, not a
    # hidden payload; ten of the eighteen first hits were exactly that.
    text = text.lstrip("﻿") if text.startswith("﻿") else text
    hits = {}
    for name, pattern in rules.items():
        found = len(pattern.findall(text))
        if found:
            hits[name] = found
    return hits


def scan_repository(allowlist: dict[str, set[str]] | None = None) -> list[dict]:
    allowlist = allowlist if allowlist is not None else load_allowlist()
    findings: list[dict] = []
    for rel in tracked_files():
        path = REPO / rel
        suffix = path.suffix.lower()
        allowed = allowlist.get(rel, set())
        if suffix in EXECUTABLE_SUFFIXES:
            if "executable_in_untrusted_tree" not in allowed:
                findings.append({"path": rel, "rule": "executable_in_untrusted_tree", "count": 1})
            continue
        if suffix not in TEXT_SUFFIXES:
            continue
        try:
            text = path.read_text(encoding="utf-8")
        except (UnicodeDecodeError, OSError):
            continue
        for rule, count in scan_text(text).items():
            if rule not in allowed:
                findings.append({"path": rel, "rule": rule, "count": count})
    return findings


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--json", action="store_true", help="machine-readable output")
    args = parser.parse_args()

    findings = scan_repository()
    blocking = [f for f in findings if f["rule"] in BLOCKING_RULES]
    if args.json:
        print(json.dumps(findings, ensure_ascii=False, indent=2))
    else:
        by_rule: dict[str, int] = {}
        for finding in findings:
            by_rule[finding["rule"]] = by_rule.get(finding["rule"], 0) + 1
        print(f"UNTRUSTED_ROOTS={','.join(UNTRUSTED_ROOTS)}")
        print(f"FINDINGS={len(findings)}")
        print(f"BLOCKING={len(blocking)}")
        for rule, count in sorted(by_rule.items(), key=lambda kv: -kv[1]):
            print(f"  {rule}: {count} files")
        for finding in blocking[:40]:
            print(f"    BLOCKING {finding['rule']}:{finding['path']} ({finding['count']})")
        if len(blocking) > 40:
            print(f"    ... and {len(blocking) - 40} more")
    return 1 if blocking else 0


if __name__ == "__main__":
    sys.exit(main())
