#!/usr/bin/env python3
"""PreToolUse hook (matcher "Edit|Write|NotebookEdit|Bash|Read|Grep|Glob") for the cost-router skill.

Makes the route binding: when the current prompt's route (written by hook_prompt_route.py to
~/.claude/cache/cost-router/route-<session_id>.json) names a subagent and the main session has not
delegated yet, the main session gets FREE_MAIN_CALLS (3) tool calls of its own (one-minute work);
the next one is denied (read-only Read/Grep/Glob only get the reason as additionalContext once; COST_ROUTER_DENY_READS=1 denies them too) with a reason telling it to delegate with the Agent tool.

Only the main session is bound: a call carrying a non-empty agent_id or agent_type comes from a
subagent and is allowed without touching any state. Allowed silently as well: no state, route.subagent null (main-session classes, trivial prompts, "#direct"),
state.delegated true (monotonic for the rest of the prompt once any Agent/Task spawn happened),
COST_ROUTER_ENFORCE=0. A Bash command that actually invokes `python -m routing.route_cli route` or
`... dispatch` (at the start or after ; & | &&, not merely mentioned) marks state.vault_routed and
is always allowed; it also records the task class in the session's route receipt cache
(route_state.add_routed_class), which hook_agent_model.py accepts for later prompts of the same
class. Not counted (`exempt`): one single read-only or one-shot git/gh command (git status/log/diff,
gh pr view/checks/merge/edit, ...; no chain, pipe or redirect) and a short Edit/Write of MEMORY.md,
a memory/*.md note or a todo-*.md checkpoint. Never both paths closed: when hook_agent_model.py denied a spawn (marker delegation_denied, per prompt
epoch) up to BREAKER_EXTRA_CALLS (6) calls past the free ones are allowed (the first with a one-line
additionalContext), then denied.
Any internal error: no output, exit 0 (allow).
"""
from __future__ import annotations

import json
import os
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
    r"git(?:\s+-C\s+[^\s\"']+)?\s+(?:status|log|diff|show|rev-parse|ls-files)"
    r"|git(?:\s+-C\s+[^\s\"']+)?\s+branch(?:\s+--show-current|\s+-[avr]+|\s+--list)?\s*$"
    r"|git(?:\s+-C\s+[^\s\"']+)?\s+remote\s+-v\s*$"
    r"|gh\s+pr\s+(?:view|checks|merge|edit|list|diff|comment|status|ready)"
    r"|gh\s+run\s+(?:view|list)"
    r"|gh\s+issue\s+(?:view|list|comment)"
    r")(?:\s|$)")
# Flags that make a "read-only" git/gh command run a program, write a file or bypass protection.
UNSAFE_FLAG = re.compile(r"(?:^|\s)(?:-c(?=\s|$)|--(?:output|ext-diff|textconv|upload-pack|receive-pack|exec|config|admin)(?=[\s=]|$))")
SHELL_CHAIN = re.compile(r"[;&|`\n<>]|\$\(")  # a chain, pipe, redirect or substitution is not single
MEMORY_NOTE = re.compile(r"(?:^|[\\/])(?:MEMORY\.md|memory[\\/][^\\/]+\.md|todo-[^\\/]+\.md)$", re.I)


def exempt(tool_name, tool_input: dict) -> bool:
    if tool_name == "Bash":
        cmd = str(tool_input.get("command") or "")
        return (len(cmd) <= 300 and not SHELL_CHAIN.search(cmd) and not UNSAFE_FLAG.search(cmd)
                and bool(SINGLE_COMMAND.match(cmd)))
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
    # Main-session payloads carry no agent_id/agent_type (probe-verified, Claude Code 2.1.304).
    return any(str(data.get(k) or "").strip() for k in ("agent_id", "agent_type"))


BREAKER_EXTRA_CALLS = 6  # direct calls allowed past the free ones after a refused spawn


class AllowNote(str):
    """decide() result that allows the call and carries one line of additionalContext."""


ALLOW_NOTE = AllowNote("cost-router: delegation was refused by the Agent gate; direct work allowed for this"
                       " prompt (run the Vault router to delegate)")


def _tier(policy, value):
    """Policy alias of a model name/id/alias ('claude-opus-5-5', 'opus[1m]', 'sonnet'), or None."""
    v = str(value or "").strip().lower()
    if not v:
        return None
    if v in policy.models:
        return v
    for alias, spec in policy.models.items():
        if v.startswith(spec.model_id.lower()):
            return alias
    for alias in policy.models:
        if alias in v:
            return alias
    return None


def _advisory_note(sid, state: dict):
    """A note when the route's model tier EQUALS the session's model tier (delegating to the same tier only adds
    overhead); None when the session model is unknown, above the route tier (delegation saves money) or below it
    (a risk-floored or review route must still go to the stronger model). Never raises."""
    try:
        import route_state  # noqa: E402
        from route import _load_router, policy_path  # noqa: E402
        if state.get("same_tier"):  # the prompt hook found session tier == route tier (class/risk checked there)
            return f"cost-router: same tier ({state.get('model')}): direct work ok, delegation is advisory here"
        # Fallback for states written before the prompt hook learned `same_tier`: the same class/risk exclusions
        # apply (review, design and frontier routes, and any risk-floored route, are never relaxed).
        if state.get("task_class") in ("review", "design", "frontier") or state.get("risk") in ("high", "critical"):
            return None
        session = route_state.session_model(sid)
        if not session:
            return None
        policy = _load_router().ClaudeModelPolicy.from_file(policy_path())
        s_tier, r_tier = _tier(policy, session), _tier(policy, state.get("model"))
        if s_tier and r_tier and policy.rank(r_tier) == policy.rank(s_tier):  # equal tiers only: never relax a
            return (f"cost-router: delegation is advisory here (session model {session} is on the route"  # risk floor
                    f" tier {r_tier})")
    except Exception:
        return None
    return None


READ_TOOLS = ("Read", "Grep", "Glob")


def _soften(data: dict, calls: int, reason: str):
    """A deny of a read-only tool costs a whole retry request (prefix re-read) and the model re-issues the read
    anyway: allow it and deliver the reason once as context. COST_ROUTER_DENY_READS=1 restores the deny."""
    if data.get("tool_name") not in READ_TOOLS or os.environ.get("COST_ROUTER_DENY_READS", "").strip() == "1":
        return reason
    return AllowNote(reason) if calls == route_state_free() + 1 else None


def route_state_free() -> int:
    import route_state  # noqa: E402
    return route_state.FREE_MAIN_CALLS


def decide(data: dict):
    """Return a deny reason, an AllowNote (allow + context), or None to allow. Updates the session state as a side effect."""
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
    tid = str(data.get("tool_use_id") or "")
    prior = route_state.seen_get(sid, "pre:" + tid) if tid else None
    if prior is not None:  # replay of an already counted call: same decision, no second count
        calls = int(prior) if str(prior).isdigit() else 1
    else:
        try:
            calls = int(state.get("main_calls") or 0) + 1
        except (TypeError, ValueError):
            calls = 1
        state["main_calls"] = calls
        route_state.save(sid, state)
        if tid:
            route_state.seen_event(sid, "pre:" + tid, calls)
    if calls <= route_state.FREE_MAIN_CALLS:
        return None
    advisory = _advisory_note(sid, state)
    if advisory:  # delegating to a model that is not cheaper than this session is pure overhead: never deny
        return AllowNote(advisory) if calls == route_state.FREE_MAIN_CALLS + 1 else None
    if state.get("delegation_denied"):  # the Agent gate refused the spawn: a capped direct allowance
        cap = route_state.FREE_MAIN_CALLS + BREAKER_EXTRA_CALLS
        if calls > cap:
            return _soften(data, calls, f"cost-router: direct-work allowance after a refused dispatch is used up ({cap} calls);"
                           " fix the blocker named by the Agent gate or put #direct in the prompt")
        return ALLOW_NOTE if calls == route_state.FREE_MAIN_CALLS + 1 else None
    return _soften(data, calls, f"cost-router: route is {state.get('task_class')} -> delegate to {state.get('subagent')}"
                   f" ({state.get('model')}) with the Agent tool; the main session dispatches, it does not do"
                   " this work itself. Owner override: put #direct in the prompt.")


def main() -> int:
    try:  # COST_ROUTER_OFF=1: equal-arm benchmarks / owner kill switch; no output, no state
        import route_state as _rs  # noqa: E402
        if _rs.disabled_by_env():
            return 0
    except Exception:
        pass
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
    if isinstance(reason, AllowNote):
        print(json.dumps({"hookSpecificOutput": {"hookEventName": "PreToolUse", "additionalContext": str(reason)}}))
    elif reason:
        print(json.dumps({"hookSpecificOutput": {"hookEventName": "PreToolUse",
                                                 "permissionDecision": "deny",
                                                 "permissionDecisionReason": reason}}))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
