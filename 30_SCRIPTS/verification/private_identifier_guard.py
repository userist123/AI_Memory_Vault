#!/usr/bin/env python3
"""Refuse a commit that carries one of the owner's private identifiers (AGENTS.md "Owner privacy").

The owner keeps a local, untracked list of identifiers (machine name, Windows user name, profile path, serials...)
at `%APPDATA%\\ai-memory-vault\\private_identifiers.txt` (one per line, `#` comments) or at the path in
`AI_VAULT_PRIVATE_IDS`. AGENTS.md asks every agent to grep the staged diff and the commit message against it
before committing. That step was manual, and it was skipped: on 2026-10-30 the public `main` held 63 files with
an identifier from the list (profile paths in docs/reports, the machine name in case ids, personal files).
`personal_data_guard.py` cannot cover this: CI has no access to the list.

This script makes the step one command, and a pre-commit hook if the owner wants one:

    python 30_SCRIPTS/verification/private_identifier_guard.py --staged [--message-file .git/COMMIT_EDITMSG]
    python 30_SCRIPTS/verification/private_identifier_guard.py --tree        # every tracked file
    python 30_SCRIPTS/verification/private_identifier_guard.py --ref origin/main

Matching is case-insensitive; an identifier made of letters and digits only matches as a whole word, so a short
name does not hit a longer one that contains it. Output names the file and the identifier's position in the
list (`#2`), never the identifier or the matching text: printing it would publish it again.

Without the list (CI, another machine) the guard says so and exits 0, unless `--require-list` is given.
Exit codes: 0 clean or skipped, 1 hit, 2 usage error.
"""
from __future__ import annotations

import argparse
import os
import re
import subprocess
import sys
from pathlib import Path
from typing import Dict, Iterable, List, Optional, Set

REPO = Path(__file__).resolve().parents[2]


def default_list_path() -> Optional[Path]:
    env = os.environ.get("AI_VAULT_PRIVATE_IDS")
    if env:
        return Path(env)
    appdata = os.environ.get("APPDATA")
    return Path(appdata) / "ai-memory-vault" / "private_identifiers.txt" if appdata else None


def load_identifiers(path: Path) -> List[str]:
    return [l.strip() for l in path.read_text(encoding="utf-8").splitlines() if l.strip() and not l.lstrip().startswith("#")]


def compile_patterns(identifiers: Iterable[str]) -> List[re.Pattern]:
    out = []
    for ident in identifiers:
        body = re.escape(ident)
        if re.fullmatch(r"[A-Za-z0-9]+", ident):
            body = r"(?<![A-Za-z0-9])" + body + r"(?![A-Za-z0-9])"
        out.append(re.compile(body, re.IGNORECASE))
    return out


def scan_text(text: str, patterns: List[re.Pattern]) -> Set[int]:
    return {n for n, rx in enumerate(patterns, 1) if rx.search(text)}


def _git(args: List[str], cwd: Path) -> str:
    return subprocess.run(["git", *args], cwd=cwd, capture_output=True, check=True).stdout.decode("utf-8", "replace")


def staged_added_text(cwd: Path) -> Dict[str, str]:
    """Added lines of the staged diff, per file (removing an identifier is not a hit)."""
    diff = _git(["-c", "core.quotepath=off", "diff", "--cached", "-U0", "--no-color"], cwd)
    files: Dict[str, List[str]] = {}
    current = None
    for line in diff.splitlines():
        if line.startswith("+++ "):
            current = line[6:] if line.startswith("+++ b/") else None
            if current:
                files.setdefault(current, [])
        elif current and line.startswith("+") and not line.startswith("+++"):
            files[current].append(line[1:])
    # a new file's NAME can carry an identifier too; a deleted file cannot publish anything (deleting is the fix)
    for name in _git(["-c", "core.quotepath=off", "diff", "--cached", "--name-only", "--diff-filter=d"], cwd).splitlines():
        files.setdefault(name, [])
    return {k: "\n".join(v) for k, v in files.items()}


def _text(data: bytes) -> str:
    return "" if b"\x00" in data[:8000] else data.decode("utf-8", "replace")


def ref_files(cwd: Path, ref: Optional[str]) -> Dict[str, str]:
    """Every file of <ref> (one `git cat-file --batch` process), or of the working tree when ref is None."""
    if not ref:
        out = {}
        for name in _git(["-c", "core.quotepath=off", "ls-files", "-z"], cwd).split("\0"):
            if name:
                try:
                    out[name] = _text((cwd / name).read_bytes())
                except OSError:
                    out[name] = ""
        return out
    entries = []
    for rec in _git(["-c", "core.quotepath=off", "ls-tree", "-r", "-z", ref], cwd).split("\0"):
        if rec:
            meta, _, name = rec.partition("\t")
            mode, kind, oid = meta.split()
            if kind == "blob":
                entries.append((name, oid))
    proc = subprocess.Popen(["git", "cat-file", "--batch"], cwd=cwd, stdin=subprocess.PIPE, stdout=subprocess.PIPE)
    out = {}
    for name, oid in entries:
        proc.stdin.write(oid.encode() + b"\n")
        proc.stdin.flush()
        header = proc.stdout.readline().split()
        size = int(header[2]) if len(header) == 3 else 0
        data = proc.stdout.read(size)
        proc.stdout.read(1)  # trailing newline
        out[name] = _text(data)
    proc.stdin.close()
    proc.wait()
    return out


def check(files: Dict[str, str], patterns: List[re.Pattern], message: Optional[str] = None) -> List[str]:
    findings = []
    for name, text in sorted(files.items()):
        hit = scan_text(name, patterns) | scan_text(text, patterns)
        if hit:
            findings.append(f"PRIVATE_IDENTIFIER:{name}:{','.join('#%d' % n for n in sorted(hit))}")
    if message is not None:
        hit = scan_text(message, patterns)
        if hit:
            findings.append(f"PRIVATE_IDENTIFIER:<commit message>:{','.join('#%d' % n for n in sorted(hit))}")
    return findings


def main(argv: Optional[List[str]] = None) -> int:
    ap = argparse.ArgumentParser(description=__doc__.split("\n")[0])
    mode = ap.add_mutually_exclusive_group(required=True)
    mode.add_argument("--staged", action="store_true")
    mode.add_argument("--tree", action="store_true")
    mode.add_argument("--ref")
    ap.add_argument("--message-file", type=Path)
    ap.add_argument("--list", type=Path, default=None)
    ap.add_argument("--require-list", action="store_true")
    ap.add_argument("--repo", type=Path, default=Path.cwd())
    a = ap.parse_args(argv)
    path = a.list or default_list_path()
    if not path or not path.is_file():
        print("private identifier list not found; guard skipped", file=sys.stderr)
        return 1 if a.require_list else 0
    patterns = compile_patterns(load_identifiers(path))
    if a.staged:
        files = staged_added_text(a.repo)
    else:
        files = ref_files(a.repo, a.ref)
    message = a.message_file.read_text(encoding="utf-8", errors="replace") if a.message_file else None
    findings = check(files, patterns, message)
    for f in findings:
        print(f)
    print(f"private identifiers: {'BLOCKED, ' + str(len(findings)) + ' file(s)' if findings else 'clean'} "
          f"({len(files)} file(s) checked)")
    return 1 if findings else 0


if __name__ == "__main__":
    raise SystemExit(main())
