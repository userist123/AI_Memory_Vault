"""Per-session route state shared by the cost-router hooks.

The UserPromptSubmit hook writes the route of the current prompt to
~/.claude/cache/cost-router/route-<session_id>.json; the PreToolUse hooks read it to make the route
binding (hook_enforce_route.py) and to mark delegation (hook_agent_model.py). Stdlib only. Every
function here swallows its own errors: a broken state file must never block a tool call.
"""
from __future__ import annotations

import json
import os
import re
import tempfile
from pathlib import Path
from typing import Optional

VAULT_GATED_CLASSES = {"implement", "review", "design", "frontier"}
DIRECT_MARK = "#direct"
FREE_MAIN_CALLS = 3  # one-minute work stays in the main session


def state_dir() -> Path:
    return Path.home() / ".claude" / "cache" / "cost-router"


def state_path(session_id) -> Optional[Path]:
    sid = str(session_id or "").strip()
    if not sid or not re.fullmatch(r"[A-Za-z0-9_.-]{1,128}", sid) or sid in {".", ".."}:
        return None
    return state_dir() / f"route-{sid}.json"


def load(session_id) -> Optional[dict]:
    path = state_path(session_id)
    if path is None:
        return None
    try:
        data = json.loads(path.read_text(encoding="utf-8"))
    except (OSError, ValueError):
        return None
    return data if isinstance(data, dict) else None


def save(session_id, state: dict) -> bool:
    """Atomic write (temp file + os.replace in the same directory). Returns False on any error."""
    path = state_path(session_id)
    if path is None:
        return False
    tmp = None
    try:
        path.parent.mkdir(parents=True, exist_ok=True)
        fd, tmp = tempfile.mkstemp(prefix=path.name + ".", suffix=".tmp", dir=str(path.parent))
        with os.fdopen(fd, "w", encoding="utf-8") as fh:
            json.dump(state, fh)
        os.replace(tmp, path)
        return True
    except Exception:
        if tmp:
            try:
                os.unlink(tmp)
            except OSError:
                pass
        return False


def new_state(task_class: str, model: Optional[str], subagent: Optional[str], prompt_hash: str,
              direct: bool = False) -> dict:
    return {"task_class": task_class, "model": model, "subagent": None if direct else subagent,
            "prompt_hash": prompt_hash, "delegated": False, "main_calls": 0, "vault_routed": False,
            "direct": bool(direct)}


def enforcement_disabled() -> bool:
    return os.environ.get("COST_ROUTER_ENFORCE", "").strip() == "0"


def project_dir(data: Optional[dict] = None) -> Path:
    env = os.environ.get("CLAUDE_PROJECT_DIR")
    if env:
        return Path(env)
    cwd = (data or {}).get("cwd") if isinstance(data, dict) else None
    return Path(cwd) if cwd else Path.cwd()


def in_vault(data: Optional[dict] = None) -> bool:
    try:
        return (project_dir(data) / "03_IMPLEMENTATION" / "packages" / "routing" / "route_cli.py").is_file()
    except Exception:
        return False
