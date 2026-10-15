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
"""
from __future__ import annotations

import hashlib
import json
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent / "lib"))

LOCAL_CLASSES = {"summarize"}  # text in -> short text out: work a local model does well
TRIVIAL = {"da", "nu", "yes", "no", "ok", "continua", "continue", "merci", "thanks", "mulțumesc", "multumesc"}


def owner_language() -> str:
    """The owner's reply language from ~/.claude/settings.json (`language`), or '' when unset."""
    try:
        value = json.loads((Path.home() / ".claude" / "settings.json").read_text(encoding="utf-8")).get("language")
    except Exception:
        return ""
    return value.strip() if isinstance(value, str) else ""


def persist(data: dict, prompt: str, d=None) -> None:
    """Write the session's route state; never raises."""
    try:
        import route_state  # noqa: E402
        sid = data.get("session_id")
        if not sid:
            return
        digest = hashlib.sha256(prompt.encode("utf-8")).hexdigest()[:16]
        if d is None:  # the router failed on a real prompt: no enforcement rather than a stale one
            state = route_state.new_state("unrouted", None, None, digest)
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
    if not prompt or prompt.startswith("/") or len(words) < 4 or prompt.lower().rstrip(".!") in TRIVIAL:
        return 0  # a short follow-up continues the current task: keep its route
    try:
        from route import decide, one_line  # noqa: E402
        d = decide(prompt)
    except Exception:
        persist(data, prompt)
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
    print(json.dumps({"hookSpecificOutput": {"hookEventName": "UserPromptSubmit", "additionalContext": ctx[:1000]}}))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
