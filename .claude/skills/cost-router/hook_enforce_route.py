#!/usr/bin/env python3
"""PreToolUse hook (matcher "Edit|Write|NotebookEdit|Bash|Read|Grep|Glob") for the cost-router skill.

Makes the route binding: when the current prompt's route (written by hook_prompt_route.py to
~/.claude/cache/cost-router/route-<session_id>.json) names a subagent and the main session has not
delegated yet, the main session gets FREE_MAIN_CALLS (3) tool calls of its own (one-minute work);
the next one is denied with a reason telling it to delegate with the Agent tool.

Allowed silently: no state, route.subagent null (main-session classes, trivial prompts, "#direct"),
state.delegated true, COST_ROUTER_ENFORCE=0. A Bash command running `routing.route_cli route` or
`routing.route_cli dispatch` marks state.vault_routed and is always allowed. Any internal error:
no output, exit 0 (allow).
"""
from __future__ import annotations

import json
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent / "lib"))

ROUTER_MARKS = ("routing.route_cli route", "routing.route_cli dispatch")


def decide(data: dict):
    """Return a deny reason, or None to allow. Updates the session state as a side effect."""
    import route_state  # noqa: E402
    if route_state.enforcement_disabled():
        return None
    sid = data.get("session_id")
    state = route_state.load(sid)
    if state is None:
        return None
    tool_input = data.get("tool_input") if isinstance(data.get("tool_input"), dict) else {}
    if data.get("tool_name") == "Bash" and any(m in str(tool_input.get("command") or "") for m in ROUTER_MARKS):
        if not state.get("vault_routed"):
            state["vault_routed"] = True
            route_state.save(sid, state)
        return None
    if not state.get("subagent") or state.get("delegated"):
        return None
    try:
        calls = int(state.get("main_calls") or 0) + 1
    except (TypeError, ValueError):
        calls = 1
    state["main_calls"] = calls
    route_state.save(sid, state)
    if calls <= route_state.FREE_MAIN_CALLS:
        return None
    return (f"cost-router: route is {state.get('task_class')} -> delegate to {state.get('subagent')}"
            f" ({state.get('model')}) with the Agent tool; the main session dispatches, it does not do"
            " this work itself. Owner override: put #direct in the prompt.")


def main() -> int:
    try:
        data = json.load(sys.stdin)
        if not isinstance(data, dict):
            return 0
        from route import is_shadowed  # noqa: E402
        if is_shadowed(Path(__file__)):
            return 0
        reason = decide(data)
    except Exception:
        return 0
    if reason:
        print(json.dumps({"hookSpecificOutput": {"hookEventName": "PreToolUse",
                                                 "permissionDecision": "deny",
                                                 "permissionDecisionReason": reason}}))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
