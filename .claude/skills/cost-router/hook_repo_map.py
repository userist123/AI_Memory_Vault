#!/usr/bin/env python3
"""SessionStart hook for the cost-router skill: inject a compact map of the current repository.

Runs at startup, after /clear and after compaction, so the agent knows what the project holds
without exploring it (see lib/repo_map.py; about 1k tokens, cached by HEAD). Silent outside a
git repository. Any internal error exits 0 with no output: this hook must never block a session.
"""
from __future__ import annotations

import json
import os
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent / "lib"))


def main() -> int:
    try:
        data = json.load(sys.stdin)
    except Exception:
        data = {}
    try:
        from route import is_shadowed  # noqa: E402
        if is_shadowed(Path(__file__)):
            return 0
        from repo_map import get_map  # noqa: E402
        cwd = os.environ.get("CLAUDE_PROJECT_DIR") or (data.get("cwd") if isinstance(data, dict) else None) or "."
        text = get_map(Path(cwd))
        if text:  # ASCII JSON: \u escapes survive a cp1252 stdout (Windows Git Bash)
            print(json.dumps({"hookSpecificOutput": {"hookEventName": "SessionStart", "additionalContext": text}}))
    except Exception:
        return 0
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
