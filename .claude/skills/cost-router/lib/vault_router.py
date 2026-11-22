#!/usr/bin/env python3
"""On-demand vault Agent Router for the main advisor (Opus), usable from ANY project.

    python -I <installed cost-router>/lib/vault_router.py route --goal "<text>" [--json]

The vault root comes ONLY from the pinned roots in the installed config.json (route_state.pinned_vault_roots, the
M2 trust data): the first pinned root that is still a real directory (no link/junction, realpath equal to the stored
string) with the router entry point. Never from the current directory or from the project. Nothing in a project can
choose what runs: a project-local routing/route_cli.py is never imported (-I: no cwd, no script dir, no PYTHON*).

It runs the vault's `routing.route_cli route` as a child (argv list, no shell, cwd = pinned root, 20 s timeout),
reads its JSON decision and prints status, primary agent, verifier and execute_via. The goal is one argv element
(`--goal=<text>`) and is never printed or logged here.

Exit: 0 ROUTED/PLANNED, 3 not configured, 4 BLOCKED, 5 timeout / crash / unreadable decision, 2 usage.
Stdlib only; no network; no LLM.
"""
from __future__ import annotations

import argparse
import json
import os
import subprocess
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))

TIMEOUT_SECONDS = 20.0
ROUTER_PACKAGES_REL = ("03_IMPLEMENTATION", "packages")
# -I ignores PYTHONPATH, so `-m routing.route_cli` could not be found: the child puts the pinned packages dir on
# sys.path itself and runs the module as __main__ (same behaviour as `python -m routing.route_cli`).
BOOTSTRAP = (
    "import sys, runpy\n"
    "sys.path.insert(0, sys.argv.pop(1))\n"
    "sys.argv[0] = 'route_cli'\n"
    "runpy.run_module('routing.route_cli', run_name='__main__', alter_sys=True)\n"
)


def pinned_root():
    """The first pinned vault root that still passes the M2 link/realpath check, else None."""
    import route_state
    for stored in route_state.pinned_vault_roots():
        try:
            path = Path(stored)
            if not path.is_dir() or route_state._is_link(path) or route_state.normalize_root(stored) != stored:
                continue
            if not Path(os.path.realpath(stored), *route_state.ROUTE_CLI_REL).is_file():
                continue
            return Path(os.path.realpath(stored))
        except Exception:
            continue
    return None


def _timeout() -> float:
    try:  # a test/ops knob that can only SHORTEN the limit
        v = float(os.environ.get("COST_ROUTER_VAULT_ROUTER_TIMEOUT", ""))
        if 0 < v < TIMEOUT_SECONDS:
            return v
    except ValueError:
        pass
    return TIMEOUT_SECONDS


def _kill_tree(proc) -> None:
    """Kill the router child AND everything it started (a grandchild would keep the pipes open and keep running)."""
    try:
        if os.name == "nt":
            subprocess.run(["taskkill", "/T", "/F", "/PID", str(proc.pid)], stdin=subprocess.DEVNULL,
                           stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL, timeout=10)
        else:
            import signal
            os.killpg(proc.pid, signal.SIGKILL)
    except Exception:
        pass
    try:
        proc.kill()
    except Exception:
        pass


def _clean(value, limit: int = 200):
    """One printable line: control characters, CR/LF and Unicode line separators become spaces."""
    if value is None:
        return None
    text = "".join(" " if not c.isprintable() else c for c in str(value))  # isprintable() is False for CR/LF, C0/C1, U+2028/9
    return " ".join(text.split())[:limit]


def run_router(root: Path, goal: str):
    """Returns (decision dict or None, error string or None)."""
    packages = str(Path(root, *ROUTER_PACKAGES_REL))
    env = {k: v for k, v in os.environ.items() if not k.upper().startswith("PYTHON")}
    env["PYTHONPATH"] = packages  # as specified; the bootstrap does the real work under -I
    cmd = [sys.executable, "-I", "-c", BOOTSTRAP, packages, "route", "--goal=" + goal]
    kw = {"start_new_session": True} if os.name != "nt" else {}  # POSIX: own process group, so killpg reaches grandchildren
    try:
        proc = subprocess.Popen(cmd, cwd=str(root), env=env, shell=False, stdin=subprocess.DEVNULL,
                                stdout=subprocess.PIPE, stderr=subprocess.PIPE, **kw)
    except OSError as exc:
        return None, f"cannot start the router ({type(exc).__name__})"
    try:
        out, _err = proc.communicate(timeout=_timeout())
    except subprocess.TimeoutExpired:
        _kill_tree(proc)
        try:
            proc.communicate(timeout=5)
        except Exception:
            pass
        return None, "router timed out"
    try:
        decision = json.loads(out.decode("utf-8", "replace"))
    except ValueError:
        return None, f"router produced no decision (exit {proc.returncode})"
    if not isinstance(decision, dict) or not isinstance(decision.get("status"), str):
        return None, "router decision unreadable"
    return decision, None


def summarize(decision: dict) -> dict:
    primary = decision.get("primary") if isinstance(decision.get("primary"), dict) else {}
    verifier = decision.get("verifier") if isinstance(decision.get("verifier"), dict) else {}
    contract = decision.get("execution_contract") if isinstance(decision.get("execution_contract"), dict) else {}
    return {"status": _clean(decision.get("status"), 20),
            "primary": _clean(primary.get("agent_id")),
            "primary_runtime": _clean(primary.get("runtime_id")),
            "verifier": _clean(verifier.get("agent_id")),
            "execute_via": _clean(contract.get("execute_via"))}


def main(argv=None) -> int:
    ap = argparse.ArgumentParser(prog="vault_router.py", description=__doc__.splitlines()[0])
    sub = ap.add_subparsers(dest="cmd", required=True)
    r = sub.add_parser("route", help="ask the vault Agent Router for a decision")
    r.add_argument("--goal", required=True)
    r.add_argument("--json", action="store_true")
    args = ap.parse_args(argv)
    root = pinned_root()
    if root is None:
        print("vault_router: no pinned vault root is configured (re-run install.py with --pin-root)")
        return 3
    decision, err = run_router(root, args.goal)
    if err:
        print(f"vault_router: {err}")
        return 5
    info = summarize(decision)
    status = str(info["status"]).upper()
    if status not in ("ROUTED", "PLANNED", "BLOCKED"):
        print(f"vault_router: unexpected status {status[:20]}")
        return 5
    if args.json:
        print(json.dumps(info, ensure_ascii=True))
    else:
        print(f"status: {status}")
        print(f"primary: {info['primary'] or '-'}" + (f" ({info['primary_runtime']})" if info["primary_runtime"] else ""))
        print(f"verifier: {info['verifier'] or '-'}")
        print(f"execute_via: {info['execute_via'] or '-'}")
    return 4 if status == "BLOCKED" else 0


if __name__ == "__main__":
    sys.exit(main())
