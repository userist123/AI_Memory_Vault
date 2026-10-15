#!/usr/bin/env python3
"""PreToolUse hook (matcher "Edit|Write|NotebookEdit|Bash|Read|Grep|Glob") for the cost-router skill.

Makes the route binding: when the current prompt's route (written by hook_prompt_route.py to
~/.claude/cache/cost-router/route-<session_id>.json) names a subagent and the main session has not
delegated yet, the main session gets FREE_MAIN_CALLS (3) tool calls of its own (one-minute work);
the next one is denied with a reason telling it to delegate with the Agent tool.

Only the main session is bound: a call carrying a non-empty agent_id or agent_type comes from a
subagent and is allowed without touching any state. Allowed silently as well: no state, route.subagent null (main-session classes, trivial prompts, "#direct"),
state.delegated true (monotonic for the rest of the prompt once any Agent/Task spawn happened),
COST_ROUTER_ENFORCE=0. A Bash command that actually invokes `python -m routing.route_cli route` or
`... dispatch` (at the start or after ; & | &&, not merely mentioned) marks state.vault_routed and
is always allowed. Any internal error:
no output, exit 0 (allow).
"""
from __future__ import annotations

import json
import re
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent / "lib"))

# An actual interpreter call of `-m routing.route_cli route|dispatch`, at the command start or after
# a separator. Accepted launchers: python/python3[.x][.exe] (any path), `py [-3[.x]]`, `uv run python`,
# `env [VAR=x ...]`, leading VAR=value assignments, and interpreter flags before -m (`-I`, `-X utf8`).
ROUTER_CALL = re.compile(
    r"(?:^|[;&|\n(])\s*"
    r"(?:env\s+)?(?:[A-Za-z_][A-Za-z0-9_]*=\S*\s+)*"
    r"(?:uv\s+run\s+)?"
    r"(?:\S*python[0-9.]*(?:\.exe)?|py(?:\.exe)?(?:\s+-[0-9][0-9.]*)?)"
    r"(?:\s+-[XW]\s+[^\s;&|-][^\s;&|]*|\s+-(?!m\b)[A-Za-z]+)*"  # only -X/-W take a value
    r"\s+-m\s+routing\.route_cli\s+(?:route|dispatch)\b")


def invokes_router(command: str) -> bool:
    # A real router call is short; scanning only the head keeps the regex linear-time in practice
    # (a long run of separators, e.g. inline minified JSON, made it quadratic past the hook timeout).
    return bool(ROUTER_CALL.search((command or "")[:2000]))


def from_subagent(data: dict) -> bool:
    return any(str(data.get(k) or "").strip() for k in ("agent_id", "agent_type"))


def decide(data: dict):
    """Return a deny reason, or None to allow. Updates the session state as a side effect."""
    import route_state  # noqa: E402
    if route_state.enforcement_disabled() or from_subagent(data):
        return None
    sid = data.get("session_id")
    state = route_state.load(sid)
    if state is None:
        return None
    tool_input = data.get("tool_input") if isinstance(data.get("tool_input"), dict) else {}
    if data.get("tool_name") == "Bash" and invokes_router(str(tool_input.get("command") or "")):
        if not state.get("vault_routed"):
            route_state.mark(sid, "vault_routed")
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
