#!/usr/bin/env python3
"""SessionStart hook for the cost-router skill: keep the user-scope install current.

Registered by install.py in ~/.claude/settings.json (matcher startup|resume), so one install keeps
working and stays up to date in every project on the machine. At most once per THROTTLE_SECONDS it
re-syncs the skill to the release pinned in the installed release_pin.json (one shallow fetch of that
commit, about two seconds, every byte hash-verified) and runs the installed `install.py --self-update`
on the verified copy, which swaps the skill in atomically under a lock, refreshes agents the owner has
not edited and re-merges the hooks. Offline, slow or broken fetches leave the installed copy as it is.
It prints nothing (SessionStart stdout would become context), never blocks a session, stays under the
60 s hook timeout and always exits 0. Nothing runs after `install.py --uninstall` (the disabled
marker), and nothing at all unless the owner opted in (default OFF since 2026-10-22):
COST_ROUTER_SELF_INSTALL=1 or ~/.claude/cost-router.auto-refresh.

Trust model (HIGH-2 fixed by pin + hash): the trust anchor is the INSTALLED copy's release_pin.json (from the
owner's checkout or an earlier verified release). The hook never reads a pin, manifest or installer from the
network: it fetches exactly the commit the pin names (by SHA), checks the commit id, writes that commit's blobs
to a fresh staging dir, verifies every byte against the pin, and only then runs the INSTALLED install.py with
--source-root <staging>. Fetched code is never executed before verification
(the pinned code runs later as the installed hooks). No pin installed -> it does nothing ("no-pin").
Residual: the pin is not signed, and a self-update only re-syncs to the installed pin; advancing the release
needs the owner (commit a new pin, run install.py from a trusted checkout; a lower seq is refused). REPO_URL is
fixed here (no environment override); tests pass another URL to update() directly. Migration: a copy installed
before 2026-10-22 has no gate; re-run install.py once (or --uninstall) on every machine that registered this hook.
"""
from __future__ import annotations

import os
import shutil
import subprocess
import sys
import tempfile
import time
from pathlib import Path


def _load_pin_module():
    """lib/release_pin.py next to this file (stdlib only); None if the install is damaged."""
    try:
        import importlib.util
        path = Path(__file__).resolve().parent / "lib" / "release_pin.py"
        spec = importlib.util.spec_from_file_location("cost_router_release_pin", path)
        mod = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(mod)
        return mod
    except Exception:
        return None


_rp = _load_pin_module()
SPARSE = _rp.SPARSE if _rp else ()  # the release file set lives in lib/release_pin.py
REPO_URL = "https://github.com/userist123/AI_Memory_Vault_CODEX_READY"
THROTTLE_SECONDS = 3600
STAMP = "cost-router.last-update"
DISABLED_MARKER = "cost-router.disabled"
OPT_IN_MARKER = "cost-router.auto-refresh"  # same opt-in as install.py (default OFF since 2026-10-22)


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
    skill = claude / "skills" / "cost-router"
    if _rp is None or not (skill / _rp.PIN_NAME).is_file():
        return "no-pin"  # fail closed: without a trusted pin there is nothing to verify against
    try:
        pin_bytes = (skill / _rp.PIN_NAME).read_bytes()  # read once: validate and hand over exactly these bytes
        pin = _rp.parse_pin(pin_bytes)
    except (OSError, _rp.PinError):
        return "pin-invalid"
    try:
        installed_ref = (skill / "VERSION").read_text(encoding="utf-8").splitlines()[0].strip()
    except (OSError, ValueError, IndexError):
        installed_ref = ""
    if installed_ref != pin["commit"]:
        return "not-pinned-install"  # a checkout/dirty/unknown install is the owner's; never roll it back
    claude.mkdir(parents=True, exist_ok=True)
    stamp.write_text(f"{now:.0f}\n", encoding="utf-8")  # one attempt per window, even if it fails
    tmp = Path(tempfile.mkdtemp(prefix="cost-router-update-"))
    try:
        staging = tmp / "staging"
        try:  # fetch budget 30 s + installer 20 s stay under the 60 s the hook is registered with
            _rp.fetch_verified(url, pin, staging, 30)
        except _rp.FetchError:
            return "fetch-failed"
        except _rp.PinError:
            return "verify-failed"
        pin_copy = tmp / "pin" / _rp.PIN_NAME  # the installer reads this copy, not the swappable installed file
        pin_copy.parent.mkdir()
        pin_copy.write_bytes(pin_bytes)
        installer = skill / "install.py"
        if not installer.is_file():
            return "install-failed"
        r = subprocess.run([sys.executable, str(installer), "--home", str(home), "--self-update",
                            "--source-ref", pin["commit"], "--source-root", str(staging),
                            "--release-pin", str(pin_copy)],
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
    if os.environ.get("COST_ROUTER_OFF", "").strip().lower() in ("1", "true", "yes", "on"):
        return 0  # same switch as lib/route_state.disabled_by_env(); the only lib file this hook loads is lib/release_pin.py
    if os.environ.get("COST_ROUTER_NO_SELF_INSTALL", "").strip() == "1":
        return 0  # isolation (tests, benchmark children, owner opt-out): no git, no network, no write under ~/.claude
    if not (os.environ.get("COST_ROUTER_SELF_INSTALL", "").strip() == "1"
            or (Path.home() / ".claude" / OPT_IN_MARKER).exists()):
        return 0  # default OFF (owner, 2026-10-22): no remote fetch without an explicit opt-in
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
