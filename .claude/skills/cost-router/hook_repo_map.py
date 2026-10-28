#!/usr/bin/env python3
"""SessionStart hook for the cost-router skill: inject a compact map of the current repository.

Runs at startup only by default (COST_ROUTER_REPO_MAP=always: also after /clear and compaction; compaction otherwise gets one state line), so the agent knows what the project holds
without exploring it (see lib/repo_map.py; about 1k tokens, cached by HEAD). Silent outside a
git repository. Any internal error exits 0 with no output: this hook must never block a session.
"""
from __future__ import annotations

import json
import os
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent / "lib"))


def state_line(session_id) -> str:
    """One line (<200 chars) of cost-router state for the context after a compaction; '' if absent."""
    try:
        import route_state  # noqa: E402
        state = route_state.load(session_id)
        if not state:
            return ""
        entries = route_state.agent_usage(session_id)
        costs = [e["est_cost_usd"] for e in entries if isinstance(e.get("est_cost_usd"), (int, float))]
        cost = f"{sum(costs):.4f}" if costs else "n/a"
        return (f"cost-router: route class={state.get('task_class')} model={state.get('model')} "
                f"main_calls={state.get('main_calls', 0)} subagents={len(entries)} est_cost_usd={cost}")[:199]
    except Exception:
        return ""


def max_chars() -> int:
    """Map size cap: env COST_ROUTER_REPO_MAP_MAX_CHARS, default 1500."""
    try:
        return max(500, int(os.environ.get("COST_ROUTER_REPO_MAP_MAX_CHARS", "") or 1500))
    except ValueError:
        return 1500


def main() -> int:
    try:  # COST_ROUTER_OFF=1: equal-arm benchmarks / owner kill switch; no output, no state
        import route_state as _rs  # noqa: E402
        if _rs.disabled_by_env():
            return 0
    except Exception:
        pass
    try:
        data = json.load(sys.stdin)
    except Exception:
        data = {}
    try:
        from route import is_shadowed  # noqa: E402
        if is_shadowed(Path(__file__)):
            return 0
        if isinstance(data, dict) and data.get("session_id"):
            try:  # the session's model (payload > stored > transcript > settings): hook_enforce_route.py makes
                import route_state  # noqa: E402  # delegation advisory when it is not above the route
                route_state.resolve_session_model(data.get("session_id"), data, data.get("transcript_path"))
            except Exception:
                pass
        if os.environ.get("COST_ROUTER_NO_REPO_MAP", "").strip() == "1":
            return 0
        source = data.get("source") if isinstance(data, dict) else None
        always = os.environ.get("COST_ROUTER_REPO_MAP", "").strip().lower() == "always"
        if not always and source == "compact":  # the map is usually still in the prefix: one state line only
            line = state_line(data.get("session_id"))
            if line:
                print(json.dumps({"hookSpecificOutput": {"hookEventName": "SessionStart", "additionalContext": line}}))
            return 0
        if not always and source not in (None, "", "startup"):  # clear/resume: no map by default
            return 0
        from repo_map import get_map  # noqa: E402
        cwd = os.environ.get("CLAUDE_PROJECT_DIR") or (data.get("cwd") if isinstance(data, dict) else None) or "."
        text = get_map(Path(cwd), max_chars())
        if isinstance(data, dict) and data.get("source") == "compact":
            text = ((text or "") + "\n" + state_line(data.get("session_id"))).strip()
        if text:  # ASCII JSON: \u escapes survive a cp1252 stdout (Windows Git Bash)
            print(json.dumps({"hookSpecificOutput": {"hookEventName": "SessionStart", "additionalContext": text}}))
    except Exception:
        return 0
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
