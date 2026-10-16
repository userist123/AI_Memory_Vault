#!/usr/bin/env python3
"""UserPromptSubmit hook for the cost-router skill.

Reads the hook JSON on stdin, classifies the prompt with the routing policy and injects one
short line of `additionalContext` telling Claude which class/model/subagent the policy
recommends and to apply the `cost-router` skill. It is silent (no output) on trivial prompts
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


def persist(data: dict, prompt: str, d=None, direct: bool = False) -> None:
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
        route_state.reset(sid, state)
    except Exception:
        pass


def main() -> int:
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
    warning = main_context_warning(data)
    if "#direct" in prompt.lower() and len(words) < 4:
        persist(data, prompt, None, direct=True)  # owner override, however short the prompt
        if warning:
            _emit(warning)
        return 0
    if not prompt or prompt.startswith("/") or len(words) < 4 or prompt.lower().rstrip(".!") in TRIVIAL:
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
    persist(data, prompt, d)
    ctx = one_line(d) + "."
    lang = owner_language()
    if lang:  # the injected context is English; without this the reply drifts to English
        ctx += (f" Write to the owner in {lang}: replies, commit messages, PR titles, descriptions and comments."
                " Keep file contents and internal text in English: code, skills, prompts, .md files, subagent"
                " briefs, self check-ins.")
    if d.subagent:
        ctx += (f" Do the work in a subagent of any type ({d.subagent} fits); the Agent hook sets its model"
                f" to {d.model}. Keep only its conclusion in this session.")
        if "#direct" not in prompt.lower():
            ctx += " Enforced: after 3 direct tool calls the main session is blocked until it delegates."
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
