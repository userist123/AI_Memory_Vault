"""Per-session route state shared by the cost-router hooks.

The UserPromptSubmit hook writes the route of the current prompt to
~/.claude/cache/cost-router/route-<session_id>.json; the PreToolUse hooks read it to make the route
binding (hook_enforce_route.py) and to mark delegation (hook_agent_model.py). Stdlib only. Every
function here swallows its own errors: a broken state file must never block a tool call.

`delegated` and `vault_routed` are monotonic within one prompt: each is a separate marker file
(route-<sid>.delegated, route-<sid>.vault_routed) that a concurrent counter write of the JSON can
never clear. load() ORs them in; only reset() (a new non-trivial prompt) removes them. Each marker
holds the `epoch` (a random nonce reset() puts in the state) of the prompt that set it; a marker
whose content differs from the current epoch is void, so a marker reset() could not delete never
leaks into the next prompt. reset() also prunes cache files older than CACHE_MAX_AGE_SECONDS.
"""
from __future__ import annotations

import json
import os
import re
import tempfile
import time
import uuid
from pathlib import Path
from typing import Optional

VAULT_GATED_CLASSES = {"implement", "review", "design", "frontier"}
DIRECT_MARK = "#direct"
FREE_MAIN_CALLS = 3  # one-minute work stays in the main session
MARKERS = ("delegated", "vault_routed")
CACHE_MAX_AGE_SECONDS = 7 * 86400


def state_dir() -> Path:
    return Path.home() / ".claude" / "cache" / "cost-router"


def state_path(session_id) -> Optional[Path]:
    sid = str(session_id or "").strip()
    if not sid or not re.fullmatch(r"[A-Za-z0-9_.-]{1,128}", sid) or sid in {".", ".."}:
        return None
    return state_dir() / f"route-{sid}.json"


def marker_path(session_id, name: str) -> Optional[Path]:
    path = state_path(session_id)
    return None if path is None or name not in MARKERS else path.with_suffix("." + name)


def mark(session_id, name: str) -> bool:
    """Set a monotonic flag for the current prompt. Returns False on any error."""
    path = marker_path(session_id, name)
    if path is None:
        return False
    state = load(session_id)
    epoch = str((state or {}).get("epoch") or "")
    if not epoch:
        return False  # no current prompt to attach the flag to
    try:
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text(epoch, encoding="utf-8")
        return True
    except Exception:
        return False


def _marker_valid(session_id, name: str, epoch: str) -> bool:
    try:
        path = marker_path(session_id, name)
        return bool(epoch) and path is not None and path.read_text(encoding="utf-8").strip() == epoch
    except Exception:
        return False


def load(session_id) -> Optional[dict]:
    path = state_path(session_id)
    if path is None:
        return None
    try:
        data = json.loads(path.read_text(encoding="utf-8"))
    except (OSError, ValueError):
        return None
    if not isinstance(data, dict):
        return None
    epoch = str(data.get("epoch") or "")
    for name in MARKERS:
        data[name] = bool(data.get(name)) or _marker_valid(session_id, name, epoch)
    return data


def prune_cache(keep: Optional[Path] = None, max_age: float = CACHE_MAX_AGE_SECONDS) -> None:
    """Best effort: delete route-* files older than max_age seconds. Never raises."""
    try:
        cutoff = time.time() - max_age
        for path in state_dir().glob("route-*"):
            try:
                if keep is not None and path.stem == keep.stem:
                    continue
                if path.is_file() and path.stat().st_mtime < cutoff:
                    path.unlink()
            except Exception:
                continue
    except Exception:
        pass


def reset(session_id, state: dict) -> bool:
    """Replace the state for a new prompt. A fresh epoch voids every marker of the previous prompt
    even when deleting it fails; deletion is only tidiness. Old cache files are pruned."""
    state = dict(state)
    state["epoch"] = uuid.uuid4().hex
    for name in MARKERS:
        path = marker_path(session_id, name)
        if path is not None:
            try:
                path.unlink()
            except Exception:
                pass  # void anyway: its content is the old epoch
    ok = save(session_id, state)
    prune_cache(keep=state_path(session_id))
    return ok


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
