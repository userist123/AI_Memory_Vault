#!/usr/bin/env python3
"""Install / uninstall agent-integrity-guard's hooks into a Claude Code settings.json.

The skill files live in the repo (versioned). This script only merges three hook
entries into a settings file and can remove them again:

  PreToolUse  (Bash|PowerShell|Edit|Write|NotebookEdit|WebFetch|WebSearch|mcp__*) -> hook_guard.py
  PostToolUse (WebFetch|WebSearch|Read|Grep|mcp__*)                               -> hook_scan_output.py

Global install (owner choice 2026-10-24): run with --home pointing at the user's
Claude config (default ~/.claude). It merges, keeps every other setting and hook,
is idempotent, and --remove reverses it. It never edits the guard's own files, so
the guard's C7 rule does not fight the installer.

Usage:
  python install.py              # into ~/.claude/settings.json
  python install.py --home D:\\w\\<repo>   # into a project's .claude/settings.json
  python install.py --remove
  python install.py --verify
"""
from __future__ import annotations

import argparse
import json
import os
import sys
from pathlib import Path

SKILL_DIR = Path(__file__).resolve().parent
MARK = "agent-integrity-guard"

# (event, matcher, script)
SPECS = [
    ("PreToolUse", "Bash|PowerShell|Edit|Write|NotebookEdit|WebFetch|WebSearch|mcp__.*", "hook_guard.py"),
    ("PostToolUse", "WebFetch|WebSearch|Read|Grep|mcp__.*", "hook_scan_output.py"),
]


def _command(script: str, project: bool = False) -> str:
    # Resolve python at run time (python3 or python). Global install uses the hook's
    # absolute path; a project install uses a ${CLAUDE_PROJECT_DIR}-relative path so the
    # committed settings.json works in every checkout of the repo, on any machine.
    if project:
        p = "${CLAUDE_PROJECT_DIR:-.}/.claude/skills/agent-integrity-guard/" + script
    else:
        p = (SKILL_DIR / script).as_posix()
    return (f'for py in python3 python; do if "$py" -c "import sys" >/dev/null 2>&1; '
            f'then "$py" "{p}"; exit 0; fi; done; exit 0')


def _entry(script: str, matcher: str, project: bool = False) -> dict:
    return {"matcher": matcher,
            "hooks": [{"type": "command", "command": _command(script, project), "timeout": 10}]}


def _owned(entry: dict) -> bool:
    return any(MARK in h.get("command", "") for h in entry.get("hooks", []))


def load(path: Path) -> dict:
    if not path.exists():
        return {}
    return json.loads(path.read_text(encoding="utf-8"))


def merge(settings_path: Path, remove: bool = False, project: bool = False) -> str:
    settings = load(settings_path)
    hooks = settings.get("hooks", {})
    for event, matcher, script in SPECS:
        entries = [e for e in hooks.get(event, []) if not _owned(e)]
        if not remove:
            entries.append(_entry(script, matcher, project))
        if entries:
            hooks[event] = entries
        else:
            hooks.pop(event, None)
    if hooks:
        settings["hooks"] = hooks
    else:
        settings.pop("hooks", None)
    settings_path.parent.mkdir(parents=True, exist_ok=True)
    settings_path.write_text(json.dumps(settings, ensure_ascii=False, indent=2) + "\n",
                             encoding="utf-8", newline="\n")
    return ("removed" if remove else "installed") + f" {len(SPECS)} hooks in {settings_path}"


def verify(settings_path: Path) -> int:
    settings = load(settings_path)
    hooks = settings.get("hooks", {})
    missing = [event for event, _, _ in SPECS
               if not any(_owned(e) for e in hooks.get(event, []))]
    if missing:
        print(f"agent-integrity-guard NOT fully installed in {settings_path}: missing {missing}")
        return 1
    print(f"agent-integrity-guard installed in {settings_path}")
    return 0


def main(argv: list[str] | None = None) -> int:
    ap = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    ap.add_argument("--home", default=str(Path.home() / ".claude"),
                    help="settings directory (default ~/.claude); for a project pass its .claude")
    ap.add_argument("--remove", action="store_true")
    ap.add_argument("--verify", action="store_true")
    ap.add_argument("--project", action="store_true",
                    help="write ${CLAUDE_PROJECT_DIR}-relative hook commands for a repo's committed "
                         ".claude/settings.json (default: absolute paths, for a global ~/.claude install)")
    args = ap.parse_args(argv)

    home = Path(args.home)
    settings_path = home / "settings.json" if home.name == ".claude" else home / ".claude" / "settings.json"

    if args.verify:
        return verify(settings_path)
    try:
        print(merge(settings_path, remove=args.remove, project=args.project))
    except json.JSONDecodeError as e:
        print(f"settings file is not valid JSON, refusing to touch it: {e}", file=sys.stderr)
        return 2
    return 0


if __name__ == "__main__":
    sys.exit(main())
