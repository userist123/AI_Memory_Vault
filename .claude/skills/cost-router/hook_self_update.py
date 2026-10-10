#!/usr/bin/env python3
"""SessionStart hook for the cost-router skill: keep the user-scope install current.

Registered by install.py in ~/.claude/settings.json (matcher startup|resume), so one install keeps
working and stays up to date in every project on the machine. At most once per THROTTLE_SECONDS it
fetches the skill's files from the canonical repository (sparse, shallow clone of main, about two
seconds) and runs that copy's `install.py --self-update`, which swaps the skill in atomically under
a lock, refreshes agents the owner has not edited and re-merges the hooks. Offline, slow or broken
fetches leave the installed copy as it is. It prints nothing (SessionStart stdout would become
context), never blocks a session, stays under the 60 s hook timeout and always exits 0. Nothing runs
after `install.py --uninstall` (the disabled marker).

Trust model: it runs install.py from the `main` branch of REPO_URL, fixed here (no environment
override), as the owner approved on 2026-10-10. Tests pass another URL to update() directly.
"""
from __future__ import annotations

import os
import shutil
import subprocess
import sys
import tempfile
import time
from pathlib import Path

REPO_URL = "https://github.com/userist123/AI_Memory_Vault_CODEX_READY"
SPARSE = (  # no leading "/": Git Bash would rewrite it into a Windows path
    ".claude/skills/cost-router",
    ".claude/agents",
    "03_IMPLEMENTATION/packages/routing/claude_model_router.py",
    "04_CONFIG/claude_model_routing.json",
)
THROTTLE_SECONDS = 3600
STAMP = "cost-router.last-update"
DISABLED_MARKER = "cost-router.disabled"


def _rm_readonly(func, path, _exc):
    os.chmod(path, 0o700)  # git marks pack files read-only; Windows refuses to delete them otherwise
    func(path)


def _git(args, timeout):
    return subprocess.run(["git", *args], capture_output=True, text=True, timeout=timeout,
                          stdin=subprocess.DEVNULL)


def update(home: Path, now: float | None = None, url: str = REPO_URL) -> str:
    """Return what happened (for tests); never raises for an expected failure."""
    claude = home / ".claude"
    if (claude / DISABLED_MARKER).exists():
        return "disabled"
    stamp = claude / STAMP
    now = time.time() if now is None else now
    try:
        age = now - float(stamp.read_text(encoding="utf-8").strip() or 0)
        if -60 <= age < THROTTLE_SECONDS:  # a stamp further in the future (clock moved back) is stale
            return "throttled"
    except (OSError, ValueError):
        pass
    if shutil.which("git") is None:
        return "no-git"
    claude.mkdir(parents=True, exist_ok=True)
    stamp.write_text(f"{now:.0f}\n", encoding="utf-8")  # one attempt per window, even if it fails
    tmp = Path(tempfile.mkdtemp(prefix="cost-router-update-"))
    try:
        # sub-timeouts sum to 50 s, under the 60 s the hook is registered with
        if _git(["clone", "-q", "--filter=blob:none", "--sparse", "--depth", "1", url, str(tmp / "r")], 15).returncode:
            return "fetch-failed"
        if _git(["-C", str(tmp / "r"), "sparse-checkout", "set", "--no-cone", *SPARSE], 10).returncode:
            return "fetch-failed"
        ref = _git(["-C", str(tmp / "r"), "rev-parse", "--short", "HEAD"], 5).stdout.strip()
        installer = tmp / "r" / ".claude" / "skills" / "cost-router" / "install.py"
        if not installer.exists():
            return "fetch-failed"
        r = subprocess.run([sys.executable, str(installer), "--home", str(home), "--self-update",
                            "--source-ref", ref or "unknown"],
                           capture_output=True, text=True, timeout=20, stdin=subprocess.DEVNULL)
        return "updated" if r.returncode == 0 else "install-failed"
    except subprocess.TimeoutExpired:
        return "timeout"
    finally:
        if sys.version_info >= (3, 12):
            shutil.rmtree(tmp, onexc=_rm_readonly)
        else:
            shutil.rmtree(tmp, onerror=_rm_readonly)


def main() -> int:
    try:
        sys.stdin.read()
    except Exception:
        pass
    try:
        update(Path.home())
    except Exception:
        pass
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
