#!/usr/bin/env python3
"""Acceptance receipts: run a test/CI command and keep a small JSON record of what happened.

    python -I <S>/lib/check.py run [--session <id>] --label <name> -- <command...>
    python -I <S>/lib/check.py last [--label <name>] [--session <id>]

`run` executes the command (argv list, no shell), captures the exit code, the duration, the last 40 lines of output
and the sha256 of the command line, writes a receipt to <state dir>/checks/<session-or-pid>-<epoch>.json (state dir =
route_state.state_dir(), ~/.claude/cache/cost-router) and prints ONE line:

    check <label>: exit=<n> pass|fail (<summary>)

where the summary is parsed from a pytest (`N passed`, `N failed`) or `dotnet test` summary line, when there is one.
The exit status of this script is the command's own. `last` prints the newest receipt as JSON.
`--session` must match [A-Za-z0-9_-]{1,64} (else exit 2). A bare command name is resolved on PATH without the current
directory; on Windows a name found only in the cwd is refused (exit 2). A receipt file over 256 KB is never read.

A receipt is informational: the main session can forge one, so it never grants authority and no hook allows anything
because of it. Stdlib only; no network; no LLM.
"""
from __future__ import annotations

import argparse
import hashlib
import json
import os
import re
import shutil
import subprocess
import sys
import time
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))

TAIL_LINES = 40
MAX_LINE = 400
MAX_AGE_SECONDS = 7 * 86400
LABEL_RE = re.compile(r"[A-Za-z0-9_.-]{1,40}")
SESSION_RE = re.compile(r"[A-Za-z0-9_-]{1,64}")  # no dot, no separator: a session id is part of a file name
MAX_RECEIPT_BYTES = 256 * 1024  # a larger receipt is not one this script wrote: never read it
_PYTEST = re.compile(r"(\d+) (passed|failed|errors?|skipped|xfailed|xpassed|deselected)\b")
_PYTEST_LINE = re.compile(r"^\W*(?:\d+ (?:passed|failed|errors?|skipped|xfailed|xpassed|deselected)\b[^\n]*?)\bin [0-9.]+\s*s")
_DOTNET = re.compile(r"(?:Passed|Failed)!\s+-\s+Failed:\s*(\d+),\s*Passed:\s*(\d+),\s*Skipped:\s*(\d+),\s*Total:\s*(\d+)")


def checks_dir() -> Path:
    import route_state
    return route_state.state_dir() / "checks"


def command_sha256(argv) -> str:
    return hashlib.sha256(" ".join(argv).encode("utf-8", "replace")).hexdigest()


def parse_summary(lines) -> str:
    """A short summary from the last pytest or dotnet-test summary line in `lines`, else ''."""
    for line in reversed(list(lines)):
        m = _DOTNET.search(line)
        if m:
            failed, passed, skipped, total = m.groups()
            return f"dotnet {passed} passed, {failed} failed, {skipped} skipped, total {total}"
        if _PYTEST_LINE.search(line):
            counts = {}
            for n, word in _PYTEST.findall(line):
                counts[word.rstrip("s") if word.startswith("error") else word] = int(n)
            order = ("passed", "failed", "error", "skipped", "xfailed", "xpassed", "deselected")
            parts = [f"{counts[k]} {k}" for k in order if k in counts]
            if parts:
                return "pytest " + ", ".join(parts)
    return ""


def _session(value) -> str:
    sid = value or os.environ.get("CLAUDE_SESSION_ID") or ""
    return sid if isinstance(sid, str) and SESSION_RE.fullmatch(sid) else ""


def _cwd_dirs() -> set:
    try:
        return {os.path.normcase(os.path.realpath(os.getcwd()))}
    except OSError:
        return set()


def _search_path() -> list:
    """PATH without empty, '.' or relative entries and without the current directory itself."""
    cwd = _cwd_dirs()
    out = []
    for d in os.environ.get("PATH", "").split(os.pathsep):
        d = d.strip().strip('"')
        if not d or not os.path.isabs(d):
            continue
        try:
            if os.path.normcase(os.path.realpath(d)) in cwd:
                continue
        except OSError:
            continue
        out.append(d)
    return out


def _win_lookup(name: str, dirs) -> str | None:
    exts = [e for e in os.environ.get("PATHEXT", ".COM;.EXE;.BAT;.CMD").split(";") if e]
    cands = [name] if os.path.splitext(name)[1].lower() in {e.lower() for e in exts} else []
    cands += [name + e for e in exts]
    for d in dirs:
        for c in cands:
            p = os.path.join(d, c)
            if os.path.isfile(p):
                return p
    return None


def resolve_exe(name: str) -> str | None:
    """The executable a bare command name runs, searched on PATH WITHOUT the current directory (Windows otherwise
    searches the cwd first, so a planted `pytest.cmd` in the project would run). A name with a directory part is
    returned unchanged (explicit). None when nothing on the cleaned PATH matches."""
    if not name:
        return None
    if os.path.dirname(name) or os.path.isabs(name):
        return name
    dirs = _search_path()
    if os.name == "nt":
        return _win_lookup(name, dirs)
    return shutil.which(name, path=os.pathsep.join(dirs))


def cwd_refusal(argv):
    """Refuse a bare command name that is found only in the current directory (Windows resolution order)."""
    name = argv[0] if argv else ""
    if os.name != "nt" or not name or os.path.dirname(name) or resolve_exe(name) is not None:
        return None
    if _win_lookup(name, [os.getcwd()]) is not None:
        return (f"check: refused: {os.path.basename(name)} resolves only in the current directory; give the full path "
                "of a trusted executable or put its directory on PATH")
    return None


def line_for(receipt: dict) -> str:
    text = f"check {receipt['label']}: exit={receipt['exit']} {'pass' if receipt['exit'] == 0 else 'fail'}"
    return text + (f" ({receipt['summary']})" if receipt.get("summary") else "")


def write_receipt(receipt: dict, session: str):
    try:
        d = checks_dir()
        d.mkdir(parents=True, exist_ok=True)
        now = time.time()
        for old in d.glob("*.json"):  # keep the directory small; best effort
            try:
                if now - old.stat().st_mtime > MAX_AGE_SECONDS:
                    old.unlink()
            except OSError:
                pass
        path = d / f"{session or os.getpid()}-{receipt['epoch']}.json"
        n = 1
        while path.exists():
            path = d / f"{session or os.getpid()}-{receipt['epoch']}-{n}.json"
            n += 1
        tmp = path.with_name(path.name + ".tmp")
        tmp.write_text(json.dumps(receipt, ensure_ascii=False, indent=1) + "\n", encoding="utf-8")
        os.replace(tmp, path)
        return path
    except OSError:
        return None


CMD_METACHARS = frozenset('&|<>^%"'+chr(10)+chr(13))


def shim_refusal(argv):
    """A .cmd/.bat target is started through cmd.exe, which re-parses the command line: refuse an argument that holds a
    cmd metacharacter (argument injection). Returns the refusal text, or None when it is fine to run."""
    exe = resolve_exe(argv[0]) or argv[0]
    if str(exe).lower().endswith((".cmd", ".bat")):
        for a in argv[1:]:
            if any(c in CMD_METACHARS for c in a):
                return (f"check: refused: {os.path.basename(str(exe))} is a cmd.exe script and an argument holds a cmd "
                        "metacharacter (& | < > ^ % \" newline); run it through an .exe or drop the character")
    return None


def run(argv, label: str, session: str = "", timeout: float = 1800.0):
    start = time.time()
    exe = resolve_exe(argv[0]) or argv[0]  # resolve .cmd/.exe shims like a shell would, never from the cwd
    code, text = 127, ""
    try:
        proc = subprocess.run([exe, *argv[1:]], shell=False, stdin=subprocess.DEVNULL, stdout=subprocess.PIPE,
                              stderr=subprocess.STDOUT, timeout=timeout)
        code, text = proc.returncode, proc.stdout.decode("utf-8", "replace")
    except subprocess.TimeoutExpired as exc:
        code = 124
        text = (exc.stdout or b"").decode("utf-8", "replace") + "\n[check.py] timed out"
    except OSError as exc:
        text = f"[check.py] cannot run the command: {type(exc).__name__}"
    lines = [ln[:MAX_LINE] for ln in text.replace("\r\n", "\n").replace("\r", "\n").split("\n")]
    while lines and not lines[-1].strip():
        lines.pop()
    tail = lines[-TAIL_LINES:]
    receipt = {"schema": 1, "label": label, "session": session or None, "epoch": int(start), "t": round(start, 3), "exit": int(code),
               "duration_s": round(time.time() - start, 2), "cmd_sha256": command_sha256(argv),
               "summary": parse_summary(tail), "tail": tail}
    return receipt


def all_receipts(session: str = "", label: str = ""):
    out = []
    try:
        for p in checks_dir().glob("*.json"):
            try:
                if p.is_symlink() or not p.is_file() or p.stat().st_size > MAX_RECEIPT_BYTES:
                    continue
                r = json.loads(p.read_text(encoding="utf-8"))
            except (OSError, ValueError):
                continue
            if not isinstance(r, dict) or not isinstance(r.get("exit"), int) or not isinstance(r.get("label"), str):
                continue
            if session and r.get("session") != session:
                continue
            if label and r.get("label") != label:
                continue
            out.append(r)
    except OSError:
        return []
    return sorted(out, key=lambda r: (r.get("t") or r.get("epoch") or 0))


def last_failing(session_id) -> dict | None:
    """The newest failing receipt of this session, or None. Informational only: never a grant of authority."""
    sid = _session(session_id)
    if not sid:
        return None
    newest = {}
    for r in all_receipts(session=sid):  # a later receipt of the same label supersedes the earlier one
        newest[r["label"]] = r
    failing = [r for r in newest.values() if r["exit"] != 0]
    return max(failing, key=lambda r: r.get("t") or r.get("epoch") or 0) if failing else None


def main(argv=None) -> int:
    argv = list(sys.argv[1:] if argv is None else argv)
    cmd = []
    if "--" in argv:
        i = argv.index("--")
        argv, cmd = argv[:i], argv[i + 1:]
    ap = argparse.ArgumentParser(prog="check.py", description=__doc__.splitlines()[0])
    sub = ap.add_subparsers(dest="mode", required=True)
    r = sub.add_parser("run")
    r.add_argument("--label", required=True)
    r.add_argument("--session", default="")
    r.add_argument("--timeout", type=float, default=1800.0)
    la = sub.add_parser("last")
    la.add_argument("--label", default="")
    la.add_argument("--session", default="")
    args = ap.parse_args(argv)
    if args.session and not SESSION_RE.fullmatch(args.session):
        print("check: session must match [A-Za-z0-9_-]{1,64}")
        return 2
    if args.mode == "run":
        if not LABEL_RE.fullmatch(args.label):
            print("check: label must match [A-Za-z0-9_.-]{1,40}")
            return 2
        if not cmd:
            print("check: give the command after --")
            return 2
        refusal = cwd_refusal(cmd) or shim_refusal(cmd)
        if refusal:
            print(refusal)
            return 2
        session = _session(args.session)
        receipt = run(cmd, args.label, session, args.timeout)
        write_receipt(receipt, session)
        print(line_for(receipt))
        return receipt["exit"] if 0 <= receipt["exit"] < 256 else 1
    found = all_receipts(session=_session(args.session) if args.session else "", label=args.label)
    if not found:
        print("check: no receipt")
        return 1
    print(json.dumps(found[-1], ensure_ascii=False, indent=1))
    return 0


if __name__ == "__main__":
    sys.exit(main())
