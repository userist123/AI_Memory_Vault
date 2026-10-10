#!/usr/bin/env python3
"""Install the cost-router skill once, for every project on this machine.

    python3 .claude/skills/cost-router/install.py            # user scope: ~/.claude
    python3 .claude/skills/cost-router/install.py --no-hook  # skill + agents only
    python3 .claude/skills/cost-router/install.py --uninstall
    python3 .claude/skills/cost-router/install.py --home <dir>    # another HOME (tests)
    python3 .claude/skills/cost-router/install.py --session-start # what the SessionStart hook runs:
                                                                  # skill + agents only, and nothing at
                                                                  # all after an --uninstall (marker file)

What it does (idempotent, prints every path it touched):
  1. copies this skill directory to ~/.claude/skills/cost-router/ and the canonical router
     module + policy into its lib/ so it works outside this repository;
  2. copies the three subagents (Explore, vault-worker, vault-reviewer) to ~/.claude/agents/; a file
     of that name is replaced only when it is still the copy an earlier install wrote (owner edits are
     kept; --force overwrites them too);
  3. merges three hooks into ~/.claude/settings.json (kept out with --no-hook): UserPromptSubmit (one
     route line per non-trivial prompt), PreToolUse on Agent (sets each subagent's model from the
     route of its brief, whatever the subagent type) and SessionStart (hook_self_update.py: at most
     hourly, fetches the latest skill from the canonical repository and re-runs this installer, so
     one install stays current).
The installer itself runs nothing from the network and writes only under ~/.claude. The self-update
hook it registers does run install.py from the canonical repository's `main` at most hourly (owner-
approved trust model, 2026-10-10); --no-hook installs without it. Every install holds
~/.claude/cost-router.lock and swaps the skill directory in with a rename, so concurrent sessions or
a killed run never leave a half-deleted skill behind.
"""
from __future__ import annotations

import argparse
import hashlib
import json
import os
import shutil
import sys
import time
from contextlib import contextmanager
from pathlib import Path

SKILL_SRC = Path(__file__).resolve().parent
REPO = SKILL_SRC.parents[2]
ROUTER_SRC = REPO / "03_IMPLEMENTATION" / "packages" / "routing" / "claude_model_router.py"
POLICY_SRC = REPO / "04_CONFIG" / "claude_model_routing.json"
AGENTS_SRC = REPO / ".claude" / "agents"
HOOK_MARK = "cost-router/hook_"  # every hook this installer owns
HOOK_SPECS = (  # (event, script, matcher)
    ("UserPromptSubmit", "hook_prompt_route.py", None),
    ("PreToolUse", "hook_agent_model.py", "Agent"),
    ("SessionStart", "hook_self_update.py", "startup|resume"),
)
HOOK_TIMEOUT = {"hook_self_update.py": 60}  # seconds; a fetch is about two, the rest is margin
AGENT_MANIFEST = "agents.sha256.json"  # under the installed skill: hashes of the agent files it wrote
DISABLED_MARKER = "cost-router.disabled"  # written by --uninstall under ~/.claude; honoured by --session-start


def hook_entry(skill_dir: Path, script: str = "hook_prompt_route.py", matcher=None) -> dict:
    cmd = (f'for py in python3 python; do if "$py" -c "import sys" >/dev/null 2>&1; then '
           f'"$py" "{skill_dir / script}"; exit 0; fi; done; exit 0')
    entry = {"hooks": [{"type": "command", "command": cmd, "timeout": HOOK_TIMEOUT.get(script, 10)}]}
    return {"matcher": matcher, **entry} if matcher else entry


def _owned(entry) -> bool:
    return HOOK_MARK in json.dumps(entry).replace("\\\\", "/")


def load_settings(settings_path: Path) -> dict:
    """Parse the user settings or raise ValueError with a message that names the file."""
    if not settings_path.exists():
        return {}
    try:
        data = json.loads(settings_path.read_text(encoding="utf-8"))
    except json.JSONDecodeError as exc:
        raise ValueError(f"{settings_path} is not strict JSON ({exc.msg} at line {exc.lineno}); "
                         "remove comments/trailing commas or run with --no-hook") from exc
    if not isinstance(data, dict):
        raise ValueError(f"{settings_path} must contain a JSON object")
    return data


def merge_hook(settings_path: Path, skill_dir: Path, remove: bool = False) -> str:
    settings = load_settings(settings_path)
    hooks = settings.setdefault("hooks", {})
    for event, script, matcher in HOOK_SPECS:
        entries = [e for e in hooks.get(event, []) if not _owned(e)]
        if not remove:
            entries.append(hook_entry(skill_dir, script, matcher))
        if entries:
            hooks[event] = entries
        else:
            hooks.pop(event, None)
    if not hooks:
        settings.pop("hooks", None)
    settings_path.parent.mkdir(parents=True, exist_ok=True)
    settings_path.write_text(json.dumps(settings, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")
    return str(settings_path)


def _sha(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


def _written(skill_dir: Path) -> dict:
    """Hashes of the agent files the previous install wrote (empty when unknown)."""
    try:
        data = json.loads((skill_dir / AGENT_MANIFEST).read_text(encoding="utf-8"))
        return data if isinstance(data, dict) else {}
    except (OSError, ValueError):
        return {}


class LockBusy(Exception):
    pass


LOCK_STALE_SECONDS = 120  # a lock older than this belongs to a run that was killed


@contextmanager
def install_lock(home: Path, wait: float):
    """Hold ~/.claude/cost-router.lock; raise LockBusy if another live run keeps it past `wait`."""
    path = home / ".claude" / "cost-router.lock"
    path.parent.mkdir(parents=True, exist_ok=True)
    deadline = time.time() + wait
    while True:
        try:
            fd = os.open(path, os.O_CREAT | os.O_EXCL | os.O_WRONLY)
            os.write(fd, f"{os.getpid()}\n".encode())
            os.close(fd)
            break
        except FileExistsError:
            try:
                if time.time() - path.stat().st_mtime > LOCK_STALE_SECONDS:
                    path.unlink()
                    continue
            except OSError:
                continue
            if time.time() >= deadline:
                raise LockBusy(f"{path} is held by another install")
            time.sleep(0.2)
    try:
        yield
    finally:
        try:
            path.unlink()
        except OSError:
            pass


def _swap_in(staging: Path, skill_dir: Path) -> None:
    """Replace skill_dir with staging by renames; on failure the previous copy stays in place."""
    old = staging.with_name(f"cost-router.old-{os.getpid()}")
    skill_dir.parent.mkdir(parents=True, exist_ok=True)
    if skill_dir.exists():
        os.replace(skill_dir, old)
    try:
        os.replace(staging, skill_dir)
    except OSError:
        if old.exists():
            os.replace(old, skill_dir)
        raise
    shutil.rmtree(old, ignore_errors=True)


def install(home: Path, with_hook: bool, force: bool, session_start: bool = False,
            source_ref: str | None = None, self_update: bool = False) -> list[str]:
    touched: list[str] = []
    marker = home / ".claude" / DISABLED_MARKER
    if (session_start or self_update) and marker.exists():
        return [f"skipped: {marker} exists (cost-router was uninstalled on this machine; run install.py to re-enable)"]
    if with_hook:
        load_settings(home / ".claude" / "settings.json")  # fail before anything is copied
    skill_dir = home / ".claude" / "skills" / "cost-router"
    written = _written(skill_dir)
    staging = home / ".claude" / f"cost-router.staging-{os.getpid()}"  # same filesystem, outside skills/
    shutil.rmtree(staging, ignore_errors=True)
    shutil.copytree(SKILL_SRC, staging, ignore=shutil.ignore_patterns("__pycache__", "chat"))  # chat/ is the claude.ai upload
    (staging / "lib").mkdir(exist_ok=True)
    shutil.copy2(ROUTER_SRC, staging / "lib" / "claude_model_router.py")
    shutil.copy2(POLICY_SRC, staging / "policy.json")
    if source_ref:
        (staging / "VERSION").write_text(source_ref + "\n", encoding="utf-8")
    agents_dir = home / ".claude" / "agents"
    agents_dir.mkdir(parents=True, exist_ok=True)
    manifest = {}
    for src in sorted(AGENTS_SRC.glob("*.md")):
        dst = agents_dir / src.name
        if dst.exists() and not force:
            current = _sha(dst)
            if current == _sha(src):
                manifest[src.name] = current
                touched.append(f"{dst} (already current)")
                continue
            if written.get(src.name) != current:
                touched.append(f"{dst} (kept existing: edited here; --force to overwrite)")
                continue
        shutil.copy2(src, dst)
        manifest[src.name] = _sha(dst)
        touched.append(str(dst))
    (staging / AGENT_MANIFEST).write_text(json.dumps(manifest, indent=2) + "\n", encoding="utf-8")
    try:
        _swap_in(staging, skill_dir)
    finally:
        shutil.rmtree(staging, ignore_errors=True)
    touched.insert(0, str(skill_dir) + (f" (source {source_ref})" if source_ref else ""))
    if with_hook:
        touched.append(merge_hook(home / ".claude" / "settings.json", skill_dir) + " (UserPromptSubmit + PreToolUse/Agent + SessionStart self-update hooks)")
    if not (session_start or self_update) and marker.exists():
        marker.unlink()
        touched.append(f"removed {marker} (re-enabled)")
    return touched


def uninstall(home: Path) -> list[str]:
    touched: list[str] = []
    skill_dir = home / ".claude" / "skills" / "cost-router"
    written = _written(skill_dir)
    if skill_dir.exists():
        shutil.rmtree(skill_dir)
        touched.append(f"removed {skill_dir}")
    for name in ("Explore.md", "vault-worker.md", "vault-reviewer.md"):
        p = home / ".claude" / "agents" / name
        if p.exists() and _sha(p) in {_sha(AGENTS_SRC / name), written.get(name)}:
            p.unlink()
            touched.append(f"removed {p}")
    settings = home / ".claude" / "settings.json"
    if settings.exists():
        touched.append(merge_hook(settings, skill_dir, remove=True) + " (hook removed)")
    marker = home / ".claude" / DISABLED_MARKER
    marker.parent.mkdir(parents=True, exist_ok=True)
    marker.write_text("cost-router uninstalled; the SessionStart hook will not reinstall it. Delete this file or run install.py to re-enable.\n", encoding="utf-8")
    touched.append(f"wrote {marker} (SessionStart will not reinstall)")
    return touched


def main(argv=None) -> int:
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--home", type=Path, default=Path.home())
    ap.add_argument("--no-hook", action="store_true")
    ap.add_argument("--force", action="store_true")
    ap.add_argument("--uninstall", action="store_true")
    ap.add_argument("--self-update", action="store_true",
                    help="what the self-update hook runs: hooks included, skipped after an --uninstall, never re-enables")
    ap.add_argument("--source-ref", help="commit the files came from (written to VERSION; set by the self-update hook)")
    ap.add_argument("--session-start", action="store_true", help="quiet mode for the SessionStart hook: implies --no-hook, honours the disabled marker")
    a = ap.parse_args(argv)
    if a.session_start:
        a.no_hook = True
    for p in (ROUTER_SRC, POLICY_SRC, AGENTS_SRC):
        if not p.exists():
            print(f"missing source: {p}", file=sys.stderr)
            return 1
    quiet = a.session_start or a.self_update  # hook-driven: never wait on another run, skip instead
    try:
        with install_lock(a.home, wait=0 if quiet else 30):
            touched = uninstall(a.home) if a.uninstall else install(
                a.home, not a.no_hook, a.force, a.session_start, a.source_ref, a.self_update)
    except LockBusy as exc:
        print(f"skipped: {exc}" if quiet else f"not installed: {exc}", file=sys.stdout if quiet else sys.stderr)
        return 0 if quiet else 1
    except ValueError as exc:
        print(f"not installed: {exc}", file=sys.stderr)
        return 1
    print("\n".join(touched) or "nothing to do")
    if not a.uninstall:
        print("\nDone. New sessions in every project load the skill; type /cost-router to invoke it by hand."
              + ("" if a.no_hook else " Each non-trivial prompt now gets a one-line route hint, and each"
                                        " session start keeps the install current (at most hourly)."))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
