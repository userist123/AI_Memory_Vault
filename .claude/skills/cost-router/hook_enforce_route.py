#!/usr/bin/env python3
"""PreToolUse hook (matcher "Edit|Write|NotebookEdit|Bash|Read|Grep|Glob") for the cost-router skill.

Makes the route binding: when the current prompt's route (written by hook_prompt_route.py to
~/.claude/cache/cost-router/route-<session_id>.json) names a subagent and the main session has not
delegated yet, the main session gets FREE_MAIN_CALLS (3) tool calls of its own (one-minute work);
the next one is denied (read-only Read/Grep/Glob only get the reason as additionalContext once; COST_ROUTER_DENY_READS=1 denies them too) with a reason telling it to delegate with the Agent tool.

Only the main session is bound: a call carrying a non-empty agent_id or agent_type comes from a
subagent and is allowed without touching any state. Allowed silently as well: no state, route.subagent null (main-session classes, trivial prompts, "#direct"),
state.delegated true (monotonic for the rest of the prompt once any Agent/Task spawn happened),
COST_ROUTER_ENFORCE=0. A Bash command that is ONE plain command (no ; & | redirect, comment or substitution) running
`python -m routing.route_cli route|dispatch` or the installed lib/vault_router.py route (see invokes_router) marks state.vault_routed and
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
import posixpath
import re
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent / "lib"))

# A router RECEIPT counts only for a SINGLE command: no ; & | < > backtick $( newline or comment outside/inside quotes
# (so nothing can ride along), whose interpreter is a recognised launcher (python/python3[.x][.exe] by name, `py [-3]`,
# `uv run python`, optionally behind `env` and VAR=value assignments) and that runs EITHER
#   - the INSTALLED lib/vault_router.py (absolute path, `~` and $HOME expanded, compared after realpath + normcase) with
#     the sub-command `route`, OR
#   - `-m routing.route_cli route|dispatch` (module form after -m only, never inside a -c string).
# Anything else (echo, a comment, a project copy of the script, a path elsewhere, `-c`) is not a receipt.
MAX_COMMAND = 2000
_ASSIGN = re.compile(r"^[A-Za-z_][A-Za-z0-9_]*=")
_PY_NAME = re.compile(r"^(?:python[0-9.]*|py)(?:\.exe)?$", re.I)
_FLAGS = re.compile(r"^-[IEsSBdOqRuvb]+$")  # value-less interpreter flags; -c, -m and friends are handled explicitly


def _tokens(command: str):
    """Shell-ish words with quotes removed; None when the command is not a single plain command."""
    if not command or len(command) > MAX_COMMAND or any(c in command for c in "\n\r\x00`") or "$(" in command:
        return None
    out: list[str] = []
    cur: list[str] = []
    started = False
    quote = ""
    for ch in command:
        if quote:
            if ch == quote:
                quote = ""
            else:
                cur.append(ch)
        elif ch in "\"'":
            quote, started = ch, True
        elif ch in " \t":
            if started or cur:
                out.append("".join(cur))
                cur, started = [], False
        elif ch in ";&|<>(){}":
            return None
        elif ch == "#" and not (started or cur):
            return None  # a comment, or a word that starts with #
        else:
            cur.append(ch)
    if quote:
        return None
    if started or cur:
        out.append("".join(cur))
    return out or None


def _norm(path: str) -> str:
    return os.path.normcase(os.path.realpath(os.path.expandvars(os.path.expanduser(path))))


def _installed_wrappers() -> set:
    here = Path(__file__).resolve().parent / "lib" / "vault_router.py"
    home = Path(os.path.expanduser("~")) / ".claude" / "skills" / "cost-router" / "lib" / "vault_router.py"
    return {_norm(str(here)), _norm(str(home))}


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
    try:
        toks = _tokens(command)
        if not toks:
            return False
        i = 0
        if toks[i] == "env":
            i += 1
        while i < len(toks) and _ASSIGN.match(toks[i]):
            i += 1
        if i + 1 < len(toks) and toks[i] == "uv" and toks[i + 1] == "run":
            i += 2
        if i >= len(toks) or not _PY_NAME.match(os.path.basename(toks[i].replace("\\", "/"))):
            return False
        launcher = os.path.basename(toks[i].replace("\\", "/")).lower()
        i += 1
        if launcher.startswith("py") and not launcher.startswith("python") and i < len(toks) and re.match(r"^-[0-9][0-9.]*$", toks[i]):
            i += 1  # py -3 / py -3.12
        while i < len(toks):
            t = toks[i]
            if t in ("-X", "-W"):
                i += 2
            elif _FLAGS.match(t):
                i += 1
            else:
                break
        if i >= len(toks):
            return False
        if toks[i] == "-m":
            return i + 2 < len(toks) and toks[i + 1] == "routing.route_cli" and toks[i + 2] in ("route", "dispatch")
        if toks[i].startswith("-"):
            return False
        script = os.path.expandvars(os.path.expanduser(toks[i]))
        return (i + 1 < len(toks) and toks[i + 1] == "route" and os.path.isabs(script)  # absolute only: a relative path is the project's copy
                and _norm(script) in _installed_wrappers())
    except Exception:
        return False


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


# --- Best-effort role guard (declared limits of dfir-analyst and test-author) ---------------------------------------
# Both roles have Bash, so their limits are DECLARED in the agent file; this guard denies the obvious violations when
# the payload carries agent_type. Bash is Turing-complete (python -c, encoded commands, aliases): this is a speed bump,
# not a sandbox. It fails open on any error; only the explicit denials below are enforced.
_CMDPOS = (r"(?:^|[;&|(`]|\$\()\s*(?:(?:sudo|env|time|command|call|start|&)\s+|[A-Za-z_]\w*=\S*\s+)*")
_DFIR_CMDS = (
    r"curl|wget|iwr|irm|invoke-webrequest|invoke-restmethod|ssh|scp|sftp|ftp|nc|ncat|npm|npx|pnpm|yarn|"
    r"pip3?\s+install|python[\d.]*(?:\.exe)?\s+(?:-\w+\s+)*-m\s+(?:http\.server|pip\s+install|ensurepip)|py\s+(?:-[\d.]+\s+)?-m\s+(?:http\.server|pip\s+install)|"
    r"git(?:\s+-C\s+\S+)?\s+(?:push|fetch|clone|pull)|"
    r"out-file|set-content|add-content|tee|cp|mv|rm|del|erase|rmdir|copy|move|remove-item|new-item")
_DFIR_RE = re.compile(_CMDPOS + r"(?:" + _DFIR_CMDS + r")(?![\w.-])", re.I)
_BS = chr(92)
_QUOTED = re.compile('"(?:' + _BS * 2 + '.|[^"' + _BS * 2 + '])*"' + "|'[^']*'")
_SAFE_REDIR = re.compile(r"\d?>&\d|\d?>>?\s*(?:/dev/null|nul|\$null)(?![\w./-])", re.I)
_REDIR = re.compile(r"(?<![<-])>")
_TEST_DIRS = {"20_tests", "tests", "test"}
_TEST_FILE = re.compile(r"(?:^test_.*\.py$|_test\.py$|Tests\.cs$)", re.I)


def _deny(reason: str):
    return {"hookSpecificOutput": {"hookEventName": "PreToolUse", "permissionDecision": "deny",
                                   "permissionDecisionReason": reason}}


def role_guard(data: dict):
    """A deny payload for an obvious violation by dfir-analyst or test-author, else None. Never raises."""
    try:
        role = str(data.get("agent_type") or "").strip().lower()
        tool = data.get("tool_name")
        ti = data.get("tool_input") if isinstance(data.get("tool_input"), dict) else {}
        if role == "dfir-analyst" and tool == "Bash":
            cmd = ti.get("command")
            if not isinstance(cmd, str):
                return None
            flat = _SAFE_REDIR.sub(" ", _QUOTED.sub('""', cmd))
            if _DFIR_RE.search(flat) or _REDIR.search(flat):
                return _deny("cost-router: dfir-analyst is read-only and offline (network none, write none); this Bash"
                             " command reaches the network or writes a file. Report what you need instead.")
        elif role == "test-author" and tool in ("Write", "Edit", "NotebookEdit", "MultiEdit"):
            path = ti.get("file_path") or ti.get("notebook_path")
            if not isinstance(path, str) or not path.strip():
                return None
            parts = [x for x in posixpath.normpath(path.replace("\\", "/")).split("/") if x]
            if parts and not (_TEST_FILE.search(parts[-1]) or any(x.lower() in _TEST_DIRS for x in parts[:-1])):
                return _deny("cost-router: test-author writes tests only (a 20_TESTS/tests/test directory, test_*.py,"
                             " *_test.py, *Tests.cs); this path is production code. Report the needed change instead.")
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
        guard = None
        try:
            import route_state as _rs2  # noqa: E402
            if not _rs2.enforcement_disabled():
                guard = role_guard(data)
        except Exception:
            guard = None
        if guard:
            print(json.dumps(guard))
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
