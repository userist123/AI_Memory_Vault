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
is always allowed; it also records the task class in the session's route receipt cache
(route_state.add_routed_class), which hook_agent_model.py accepts for later prompts of the same
class. Not counted (`exempt`): one single read-only or one-shot git/gh command (git status/log/diff,
gh pr view/checks/merge/edit, ...; no chain, pipe or redirect) and a short Edit/Write of MEMORY.md,
a memory/*.md note or a todo-*.md checkpoint. Any internal error: no output, exit 0 (allow).
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


# Exempt from the delegation counter (owner rule 2026-10-16): a single read-only or one-shot git/gh
# command and a one-line memory/checkpoint note. A subagent for these costs more than the action.
SINGLE_COMMAND = re.compile(
    r"^\s*(?:"
    r"git(?:\s+-C\s+\S+)?\s+(?:status|log|diff|show|branch|fetch|rev-parse|remote|ls-files|ls-remote)"
    r"|gh\s+pr\s+(?:view|checks|merge|edit|list|diff|comment|status|ready)"
    r"|gh\s+run\s+(?:view|list)"
    r"|gh\s+issue\s+(?:view|list|comment)"
    r")(?:\s|$)")
SHELL_CHAIN = re.compile(r"[;&|`\n<>]|\$\(")  # a chain, pipe, redirect or substitution is not single
MEMORY_NOTE = re.compile(r"(?:^|[\\/])(?:MEMORY\.md|memory[\\/][^\\/]+\.md|todo-[^\\/]+\.md)$", re.I)


def exempt(tool_name, tool_input: dict) -> bool:
    if tool_name == "Bash":
        cmd = str(tool_input.get("command") or "")
        return len(cmd) <= 300 and not SHELL_CHAIN.search(cmd) and bool(SINGLE_COMMAND.match(cmd))
    if tool_name in ("Edit", "Write"):
        path = str(tool_input.get("file_path") or "")
        if not MEMORY_NOTE.search(path):
            return False
        text = str(tool_input.get("new_string") if tool_name == "Edit" else tool_input.get("content") or "")
        return text.count("\n") <= (5 if tool_name == "Edit" else 60)  # a note or a checkpoint, not a dump
    return False


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
        route_state.add_routed_class(sid, state.get("task_class"))  # receipt reused for this class
        return None
    if not state.get("subagent") or state.get("delegated"):
        return None
    if exempt(data.get("tool_name"), tool_input):
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
