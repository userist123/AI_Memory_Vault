#!/usr/bin/env python3
"""UserPromptSubmit hook for the cost-router skill.

Reads the hook JSON on stdin, classifies the prompt with the routing policy and injects one
short line of `additionalContext` telling Claude which class/model/subagent the policy
recommends and to apply the `cost-router` skill. The route line is printed only when the (class, model, subagent) differs from the previous prompt's, on risk >= high or with COST_ROUTER_VERBOSE_ROUTE=1. It is silent (no output) on trivial prompts
(slash commands, fewer than 4 words, plain yes/no) so it costs nothing there. Any internal
error exits 0 with no output: this hook must never block a prompt.

It also persists the route to ~/.claude/cache/cost-router/route-<session_id>.json (lib/route_state.py)
so the PreToolUse hooks can make it binding. A trivial prompt (continua, ok, fewer than 4 words, a
slash command) keeps the current route: it continues that task. Only a new non-trivial prompt
replaces the state; a prompt containing "#direct" stores subagent null (owner override: the main
session does the work itself).

Once the main transcript's context (last assistant usage, lib/transcript_usage.py) exceeds
MAIN_WARN_TOKENS (250k; env COST_ROUTER_MAIN_MAX_TOKENS) every prompt, trivial ones included, also
gets a reminder to write the checkpoint and continue in a new session with /clear.
"""
from __future__ import annotations

import hashlib
import json
import os
import re
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent / "lib"))

LOCAL_CLASSES = {"summarize"}  # text in -> short text out: work a local model does well
MAIN_WARN_TOKENS = 250_000  # env COST_ROUTER_MAIN_MAX_TOKENS
TRIVIAL = {"da", "nu", "yes", "no", "ok", "continua", "continue", "merci", "thanks", "mulțumesc", "multumesc"}


def owner_language() -> str:
    """The owner's reply language from ~/.claude/settings.json (`language`), or '' when unset."""
    try:
        value = json.loads((Path.home() / ".claude" / "settings.json").read_text(encoding="utf-8")).get("language")
    except Exception:
        return ""
    return value.strip() if isinstance(value, str) else ""


def main_context_warning(data: dict) -> str:
    """A reminder once the main transcript carries more than MAIN_WARN_TOKENS of context (owner rule:
    one task per session; at a milestone write the checkpoint and start the next phase with /clear)."""
    try:
        import os
        from transcript_usage import context_tokens  # noqa: E402
        limit = int(os.environ.get("COST_ROUTER_MAIN_MAX_TOKENS", "") or MAIN_WARN_TOKENS)
        tokens = context_tokens(data.get("transcript_path")) if data.get("transcript_path") else None
    except Exception:
        return ""
    if not tokens or tokens <= limit:
        return ""
    return (f"cost-router: this main transcript carries ~{tokens // 1000}k context tokens (> {limit // 1000}k), re-read"
            " on every call. At the next milestone write the checkpoint"
            " 00_GOVERNANCE/coordination/tasks/todo-claude-<task>.md (task, branch/PR, done, next, key files)"
            " and start the next phase in a new session with /clear from that checkpoint.")


def _emit(ctx: str) -> None:
    print(json.dumps({"hookSpecificOutput": {"hookEventName": "UserPromptSubmit", "additionalContext": ctx}}))


NO_SAME_TIER_CLASSES = {"review", "design", "frontier"}


def _risk_of(d) -> str:
    risk = ""
    for rule in getattr(d, "applied_rules", ()):  # risk_detected:high(..), risk_floor[_deferred..]:critical->opus
        m = re.match(r"risk_(?:detected|floor\w*):(high|critical)\b", str(rule))
        if m and (m.group(1) == "critical" or not risk):
            risk = m.group(1)
    return risk


def resolve_model(data: dict) -> str | None:
    """Session model for this prompt (stored for the PreToolUse hooks); None when unknown. Never raises."""
    try:
        import route_state  # noqa: E402
        return route_state.resolve_session_model(data.get("session_id"), data, data.get("transcript_path"))
    except Exception:
        return None


def is_same_tier(session_model, d) -> bool:
    """True when the session model's tier equals the route's tier on a class that needs no stronger/independent
    model (not review/design/frontier) and risk < high: delegating would only add overhead."""
    try:
        if not session_model or not d.subagent or d.task_class in NO_SAME_TIER_CLASSES or _risk_of(d):
            return False
        from route import _load_router, policy_path  # noqa: E402
        sys.path.insert(0, str(Path(__file__).resolve().parent))
        from hook_enforce_route import _tier  # noqa: E402
        policy = _load_router().ClaudeModelPolicy.from_file(policy_path())
        s_tier, r_tier = _tier(policy, session_model), _tier(policy, d.model)
        return bool(s_tier and r_tier and policy.rank(s_tier) == policy.rank(r_tier))
    except Exception:
        return False


# Envelopes the harness wraps around turns the owner did not type. Anchored at the very start of the prompt:
# owner text that merely mentions these words mid-sentence is still an owner prompt.
SYNTHETIC_RE = re.compile(
    r"\A\s*(?:\[Subagent hand-back\]|\[SYSTEM NOTIFICATION - NOT USER INPUT\]"
    r"|<(?:agent-message|task-notification|side-sessions-event|ci-monitor-event)(?=[\s>/]))",
    re.IGNORECASE,
)


def is_synthetic_turn(prompt: str) -> bool:
    """True for subagent hand-backs, background-task notifications and side-session/CI events."""
    return bool(SYNTHETIC_RE.match(prompt or ""))


ESCALATE_RE =re.compile(r"(?<![\w#])#escalate\b", re.IGNORECASE)


def escalation_authorized(prompt: str) -> bool:
    """True only for the owner's own UserPromptSubmit text: tool_input can never reach this parser."""
    return bool(ESCALATE_RE.search(prompt or ""))


def sync_escalation(data: dict, flag: bool) -> None:
    """A prompt that is not routed (short/trivial/slash) still moves the per-prompt flag on the current state."""
    try:
        import route_state  # noqa: E402
        sid = data.get("session_id")
        state = route_state.load(sid) if sid else None
        if state is not None and bool(state.get("escalation_authorized")) != flag:
            state["escalation_authorized"] = flag
            route_state.save(sid, state)
    except Exception:
        pass


def persist(data: dict, prompt: str, d=None, direct: bool = False, same_tier: bool = False) -> None:
    """Write the session's route state; never raises."""
    try:
        import route_state  # noqa: E402
        sid = data.get("session_id")
        if not sid:
            return
        digest = hashlib.sha256(prompt.encode("utf-8")).hexdigest()[:16]
        if d is None:  # "#direct" or the router failed: no enforcement rather than a stale one
            state = route_state.new_state("direct" if direct else "unrouted", None, None, digest, direct=direct)
        else:
            state = route_state.new_state(d.task_class, d.model, d.subagent, digest,
                                          direct=route_state.DIRECT_MARK in prompt.lower())
        state["escalation_authorized"] = escalation_authorized(prompt)  # this prompt epoch only
        if d is not None:
            state["risk"] = _risk_of(d)
        if same_tier and not state.get("direct"):
            state["same_tier"] = True  # telemetry keeps `subagent`; enforcement is advisory
        route_state.reset(sid, state)
    except Exception:
        pass


def _route_changed(data: dict, d, risky: bool, same_tier: bool = False) -> bool:
    """True when the route line must be printed: first prompt of the session, a changed (class, model, subagent),
    risk >= high. Records the signature. COST_ROUTER_VERBOSE_ROUTE=1 never reaches here (always prints)."""
    try:
        import route_state  # noqa: E402
        sid = data.get("session_id")
        sig = f"{d.task_class}|{d.model}|{d.subagent}" + ("|same" if same_tier else "")
        if not sid:
            return True
        same = route_state.last_route_sig(sid) == sig
        route_state.set_last_route_sig(sid, sig)
        return risky or not same
    except Exception:
        return True


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
        return 0
    if not isinstance(data, dict):
        return 0
    prompt = str(data.get("prompt") or data.get("user_prompt") or "").strip()
    words = prompt.split()
    try:
        from route import is_shadowed  # noqa: E402
        if is_shadowed(Path(__file__)):
            return 0
    except Exception:
        return 0
    try:
        import route_state  # noqa: E402
        pid = str(data.get("prompt_id") or "")
        if pid and route_state.seen_event(data.get("session_id"), "prompt:" + pid):
            return 0  # the same prompt delivered twice: route and write state once
    except Exception:
        pass
    warning = main_context_warning(data)
    if is_synthetic_turn(prompt):  # not typed by the owner: keep the route state untouched
        _emit("cost-router: synthetic turn (subagent/notification) - route unchanged" + (" " + warning if warning else ""))
        return 0
    esc = escalation_authorized(prompt)
    if "#direct" in prompt.lower() and len(words) < 4:
        persist(data, prompt, None, direct=True)  # owner override, however short the prompt
        if warning:
            _emit(warning)
        return 0
    if not prompt or prompt.startswith("/") or len(words) < 4 or prompt.lower().rstrip(".!") in TRIVIAL:
        sync_escalation(data, esc)
        if warning:  # a short follow-up keeps its route, but the size reminder still applies
            _emit(warning)
        return 0
    try:
        from route import decide, one_line  # noqa: E402
        d = decide(prompt)
    except Exception:
        persist(data, prompt)
        if warning:
            _emit(warning)
        return 0
    same_tier = "#direct" not in prompt.lower() and is_same_tier(resolve_model(data), d)
    persist(data, prompt, d, same_tier=same_tier)
    if os.environ.get("COST_ROUTER_VERBOSE_ROUTE", "").strip() != "1":  # compact is the default (<=120 chars)
        target = f"{d.subagent} subagent" if d.subagent else "main"
        lang_code = owner_language().split()[0][:8] if owner_language() else ""
        suffix = f" lang={lang_code}" if lang_code else ""
        risk = f" risk={_risk_of(d)}" if _risk_of(d) else ""
        if same_tier:
            line = f"cost-router: {d.task_class} same tier ({d.model}): direct work ok"[:120 - len(suffix)] + suffix
        else:
            line = f"cost-router: {d.task_class} -> {target}, {d.model}{risk}"[:120 - len(suffix)] + suffix
        if not _route_changed(data, d, bool(risk), same_tier):  # same route as the previous prompt: say nothing (state is saved)
            if warning:
                _emit(warning)
            return 0
        _emit(line + (" " + warning if warning else ""))
        return 0
    ctx = one_line(d) + "."
    lang = owner_language()
    if lang:  # the injected context is English; without this the reply drifts to English
        ctx += (f" Write to the owner in {lang}: replies, commit messages, PR titles, descriptions and comments."
                " Keep file contents and internal text in English: code, skills, prompts, .md files, subagent"
                " briefs, self check-ins.")
    if same_tier:
        ctx += f" Same tier as this session ({d.model}): direct work ok, no subagent needed."
    elif d.subagent:
        ctx += (f" Do the work in a subagent of any type ({d.subagent} fits); the Agent hook sets its model"
                f" to {d.model}. Keep only its conclusion in this session.")
        if "#direct" not in prompt.lower():
            ctx += " Enforced: after 3 direct tool calls the main session must delegate (unless the Agent gate itself refused the spawn)."
    if d.task_class in LOCAL_CLASSES and not d.verifier_model:
        try:
            from local_llm import probe  # noqa: E402
            p = probe()
        except Exception:
            p = {"reachable": False}
        model = (p.get("chosen") or {}).get("text") if p.get("reachable") else None
        if model:
            tool = Path(__file__).resolve().parent / "lib" / "local_llm.py"
            ctx += (f" Free local LLM available ({model}): run `python \"{tool}\" --file <path> \"<instruction>\"`"
                    " first; fall back to the subagent only on exit 3 or a wrong answer.")
    ctx += " Apply the cost-router skill: excerpts not whole files, prove before DONE."
    _emit(ctx[:1000] + (" " + warning if warning else ""))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
