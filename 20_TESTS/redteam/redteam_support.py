"""Shared helpers for the local red-team suite (synthetic inputs, temporary stores only, no network)."""
from __future__ import annotations

import importlib.util
import json
import os
import subprocess
import sys
from pathlib import Path

REPO = Path(__file__).resolve().parents[2]
PACKAGES = REPO / "03_IMPLEMENTATION" / "packages"
SKILL = REPO / ".claude" / "skills" / "cost-router"
if str(PACKAGES) not in sys.path:
    sys.path.insert(0, str(PACKAGES))


def load_file(path: Path, name: str):
    """Import a module from a file under a private name (no sys.path side effects beyond sys.modules)."""
    spec = importlib.util.spec_from_file_location(name, path)
    mod = importlib.util.module_from_spec(spec)
    sys.modules[name] = mod
    spec.loader.exec_module(mod)
    return mod


def load_root_security():
    """The repository-root `security` package (03_IMPLEMENTATION/packages/security shares the name)."""
    name = "_redteam_root_security"
    if name in sys.modules:
        return sys.modules[name]
    spec = importlib.util.spec_from_file_location(name, REPO / "security" / "__init__.py",
                                                  submodule_search_locations=[str(REPO / "security")])
    pkg = importlib.util.module_from_spec(spec)
    sys.modules[name] = pkg
    spec.loader.exec_module(pkg)
    return pkg


def hook_env(home: Path, **extra) -> dict:
    env = dict(os.environ, HOME=str(home), USERPROFILE=str(home), APPDATA=str(home),
               COST_ROUTER_NO_SELF_INSTALL="1", COST_ROUTER_OLLAMA_ENDPOINT="http://127.0.0.1:9")
    for k in ("COST_ROUTER_OFF", "COST_ROUTER_ENFORCE", "ANTHROPIC_MODEL", "COST_ROUTER_VERBOSE_ROUTE",
              "COST_ROUTER_READONLY_FLOOR", "CLAUDE_PROJECT_DIR"):
        env.pop(k, None)
    env.update(extra)
    return env


def run_hook(hook: str, payload, home: Path, **extra):
    """Run a cost-router hook as Claude Code does (stdin JSON); returns (returncode, stdout, stderr)."""
    stdin = payload if isinstance(payload, str) else json.dumps(payload)
    r = subprocess.run([sys.executable, str(SKILL / hook)], input=stdin, capture_output=True, text=True,
                       timeout=60, env=hook_env(home, **extra), encoding="utf-8")
    return r.returncode, r.stdout, r.stderr


def hook_output(stdout: str) -> dict:
    return json.loads(stdout)["hookSpecificOutput"] if stdout.strip() else {}
