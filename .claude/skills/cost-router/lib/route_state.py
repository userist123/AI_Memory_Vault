"""Per-session route state shared by the cost-router hooks.

The UserPromptSubmit hook writes the route of the current prompt to
~/.claude/cache/cost-router/route-<session_id>.json; the PreToolUse hooks read it to make the route
binding (hook_enforce_route.py) and to mark delegation (hook_agent_model.py). Stdlib only. Every
function here swallows its own errors: a broken state file must never block a tool call.

`delegated`, `vault_routed` and `delegation_denied` (the Agent gate refused a spawn) are monotonic
within one prompt: each is a separate marker file (route-<sid>.delegated, ...) that a concurrent counter write of the JSON can
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
MARKERS = ("delegated", "vault_routed", "delegation_denied")
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


def _session_json(session_id, suffix: str) -> Optional[Path]:
    path = state_path(session_id)
    return None if path is None else path.with_suffix(suffix)


def _read_json(path: Optional[Path], default):
    try:
        value = json.loads(path.read_text(encoding="utf-8")) if path is not None else default
        return value if isinstance(value, type(default)) else default
    except Exception:
        return default


def _write_json(path: Optional[Path], value) -> bool:
    """Atomic JSON write. On Windows os.replace raises PermissionError while another hook process holds
    the target, so it is retried with a short backoff (10 ms * 2**i, up to 8 tries, 0.3 s in total);
    the temp file is always removed on failure."""
    if path is None:
        return False
    tmp = None
    try:
        path.parent.mkdir(parents=True, exist_ok=True)
        tmp = path.with_name(path.name + f".{uuid.uuid4().hex[:8]}.tmp")
        tmp.write_text(json.dumps(value), encoding="utf-8")
        waited = 0.0
        for i in range(8):
            try:
                os.replace(tmp, path)
                return True
            except PermissionError:
                delay = min(0.01 * 2 ** i, 0.3 - waited)
                if i == 7 or delay <= 0:
                    raise
                time.sleep(delay)
                waited += delay
    except Exception:
        pass
    if tmp is not None:
        try:
            tmp.unlink(missing_ok=True)
        except Exception:
            pass
    return False


def add_routed_class(session_id, task_class) -> bool:
    """Session-level route receipt cache: the Vault router ran for this task class in this session.
    Unlike the per-prompt markers, reset() keeps it; prune_cache() drops it with the session."""
    if not task_class:
        return False
    path = _session_json(session_id, ".routed_classes")
    classes = _read_json(path, [])
    if task_class in classes:
        return True
    return _write_json(path, sorted(set(classes) | {str(task_class)}))


def class_routed(session_id, task_class) -> bool:
    return bool(task_class) and task_class in _read_json(_session_json(session_id, ".routed_classes"), [])


def set_session_model(session_id, model) -> bool:
    """The session's main model (SessionStart payload `model`); session-level, kept across prompts."""
    if not model or not isinstance(model, str):
        return False
    return _write_json(_session_json(session_id, ".session_model"), {"model": model[:80]})


def session_model(session_id) -> Optional[str]:
    value = _read_json(_session_json(session_id, ".session_model"), {}).get("model")
    return value if isinstance(value, str) and value else None


def _settings_model(path) -> Optional[str]:
    try:
        value = json.loads(Path(path).read_text(encoding="utf-8")).get("model")
    except Exception:
        return None
    return value.strip()[:80] if isinstance(value, str) and value.strip() else None


def resolve_session_model_ex(session_id, data: Optional[dict] = None, transcript_path=None):
    """(model, source) with source in {payload, transcript, stored, env, settings, none}. Precedence: (1) hook payload
    `model`; (2) last assistant `message.model` in the transcript tail (wins over a stale stored value); (3) stored
    session_model, only when the transcript has none; (4) env ANTHROPIC_MODEL; (5) `model` of .claude/settings.local.json,
    .claude/settings.json (project) then ~/.claude/settings.json (read-only); (6) (None, "none"). A `settings` value is
    only a GUESS (the real model may come from --model or /model): escalation decisions treat `settings`/`none` as
    unknown (fail closed, R5). Never raises; stores a payload/transcript-derived value."""
    try:
        data = data if isinstance(data, dict) else {}
        found = data.get("model") if isinstance(data.get("model"), str) and data.get("model").strip() else None
        if found:
            set_session_model(session_id, found)
            return found.strip()[:80], "payload"
        path = transcript_path or data.get("transcript_path")
        if path:  # the transcript's last assistant model is ground truth: it wins over a stored (stale) value
            try:
                from transcript_usage import last_assistant_model  # noqa: E402
                found = last_assistant_model(path)
            except Exception:
                found = None
            if found:
                set_session_model(session_id, found)
                return found, "transcript"
        stored = session_model(session_id)
        if stored:
            return stored, "stored"
        env_model = os.environ.get("ANTHROPIC_MODEL", "").strip()
        if env_model:  # Claude Code's own env override, below --model but above the settings files
            return env_model[:80], "env"
        proj = project_dir(data)
        for f in (proj / ".claude" / "settings.local.json", proj / ".claude" / "settings.json",
                  Path.home() / ".claude" / "settings.json"):
            found = _settings_model(f)
            if found:
                return found, "settings"  # not stored: a settings default is only a guess, re-read each time
    except Exception:
        return None, "none"
    return None, "none"


def resolve_session_model(session_id, data: Optional[dict] = None, transcript_path=None) -> Optional[str]:
    """The session's main model (display/advisory use); see resolve_session_model_ex for the source."""
    return resolve_session_model_ex(session_id, data, transcript_path)[0]


def set_last_route_sig(session_id, sig) -> bool:
    """Session-level (class, model, subagent) signature of the last emitted/persisted route."""
    return _write_json(_session_json(session_id, ".last_route_sig"), {"sig": str(sig)[:200]})


def last_route_sig(session_id) -> Optional[str]:
    value = _read_json(_session_json(session_id, ".last_route_sig"), {}).get("sig")
    return value if isinstance(value, str) and value else None


def resume_count(session_id, agent_id: str) -> int:
    try:
        return int(_read_json(_session_json(session_id, ".resumes"), {}).get(agent_id, 0))
    except (TypeError, ValueError):
        return 0


LOCK_STALE_SECONDS = 10.0  # a lock file older than this belongs to a hook process that was killed
LOCK_WAIT_SECONDS = 3.0


class _Locked:
    """Exclusive O_CREAT|O_EXCL lock file next to `path` for a read-modify-write across hook processes.
    A stale lock (killed holder) is broken after LOCK_STALE_SECONDS; `acquired` is False on timeout."""

    def __init__(self, path: Path, wait: float = LOCK_WAIT_SECONDS):
        self.lock = path.with_name(path.name + ".lock")
        self.wait = wait
        self.fd = None
        self.acquired = False

    def __enter__(self):
        deadline = time.monotonic() + self.wait
        try:
            self.lock.parent.mkdir(parents=True, exist_ok=True)
        except OSError:
            return self
        while True:
            try:
                self.fd = os.open(str(self.lock), os.O_CREAT | os.O_EXCL | os.O_WRONLY)
                self.acquired = True
                return self
            except (FileExistsError, PermissionError):  # Windows: PermissionError while a delete is pending
                try:
                    if time.time() - self.lock.stat().st_mtime > LOCK_STALE_SECONDS:
                        self.lock.unlink(missing_ok=True)
                        continue
                except OSError:
                    pass
            except OSError:
                return self
            if time.monotonic() >= deadline:
                return self
            time.sleep(0.003)

    def __exit__(self, *exc):
        if self.fd is not None:
            try:
                os.close(self.fd)
            except OSError:
                pass
            try:
                self.lock.unlink(missing_ok=True)
            except OSError:
                pass
        return False


def record_resume(session_id, agent_id: str) -> bool:
    """Increment the resume counter of one subagent. Read-modify-write under a cross-process lock, so
    concurrent hook processes never lose an increment (audit B LOW-6); False when the lock or write fails."""
    path = _session_json(session_id, ".resumes")
    if path is None:
        return False
    with _Locked(path) as lk:
        if not lk.acquired:
            return False
        counts = _read_json(path, {})
        try:
            counts[agent_id] = int(counts.get(agent_id, 0)) + 1
        except (TypeError, ValueError):
            counts[agent_id] = 1
        return _write_json(path, counts)


SEEN_MAX = 500
USAGE_MAX = 200


def seen_get(session_id, key: str):
    """Value stored for an idempotency key of this session, or None when the key is unseen."""
    return _read_json(_session_json(session_id, ".seen"), {}).get(str(key))


def seen_event(session_id, key: str, value=True, record: bool = True) -> bool:
    """Idempotency marker: True when (session_id, key) was already recorded, else record it (with
    `value`, bounded to the last SEEN_MAX keys) and return False. Missing/corrupt state counts as
    unseen. record=False only peeks. Lives in a sidecar file (route-<sid>.seen) so reset() of the
    per-prompt state cannot clear it. A failed write degrades to 'unseen' (fail open)."""
    path = _session_json(session_id, ".seen")
    if path is None:
        return False
    seen = _read_json(path, {})
    key = str(key)
    if key in seen:
        return True
    if record:
        seen[key] = value
        while len(seen) > SEEN_MAX:
            seen.pop(next(iter(seen)))
        _write_json(path, seen)
    return False


def record_agent_usage(session_id, entry: dict) -> bool:
    """Append one per-subagent usage entry as one JSON line to route-<sid>.agent_usage.jsonl.
    Append-only on purpose: no read-modify-write, so parallel hooks cannot lose or tmp-leak entries;
    a single small O_APPEND write is atomic enough for this purpose."""
    path = _session_json(session_id, ".agent_usage.jsonl")
    if path is None:
        return False
    try:
        path.parent.mkdir(parents=True, exist_ok=True)
        prefix = ""
        if path.exists() and path.stat().st_size:  # a torn last line (crash mid-write) must not swallow this entry
            with path.open("rb") as fh:
                fh.seek(-1, os.SEEK_END)
                prefix = "" if fh.read(1) == b"\n" else "\n"
        with path.open("a", encoding="utf-8") as fh:
            fh.write(prefix + json.dumps(entry) + "\n")
        return True
    except Exception:
        return False


def agent_usage(session_id) -> list:
    """Recorded entries (last USAGE_MAX); corrupt lines are skipped."""
    path = _session_json(session_id, ".agent_usage.jsonl")
    out: list = []
    try:
        for line in path.read_text(encoding="utf-8", errors="replace").splitlines():
            try:
                e = json.loads(line)
            except ValueError:
                continue
            if isinstance(e, dict):
                out.append(e)
    except Exception:
        return []
    return out[-USAGE_MAX:]


def new_state(task_class: str, model: Optional[str], subagent: Optional[str], prompt_hash: str,
              direct: bool = False) -> dict:
    return {"task_class": task_class, "model": model, "subagent": None if direct else subagent,
            "prompt_hash": prompt_hash, "delegated": False, "main_calls": 0, "vault_routed": False,
            "direct": bool(direct)}


def disabled_by_env() -> bool:
    """COST_ROUTER_OFF=1 (also true/yes/on): every hook returns at once, no output, no state (equal-arm benchmarks)."""
    return os.environ.get("COST_ROUTER_OFF", "").strip().lower() in ("1", "true", "yes", "on")


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
