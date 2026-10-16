#!/usr/bin/env python3
"""PreToolUse hook (matcher "SendMessage") for the cost-router skill: bounded subagent resumes.

Measured 2026-10-14..16: workers and reviewers resumed with SendMessage for 3-5 rounds grew to
600k-900k context tokens, and every further call re-read all of it (78% of 1.9 B cache-read
tokens came from subagents). Owner rule: a subagent is resumed at most once; any further round
starts a NEW agent seeded with a <=40-line checkpoint (task, branch/PR, head SHA, findings to fix,
files, test commands).

For a SendMessage whose `to` names a subagent of this session (its transcript is
`<session>/subagents/agent-<id>.jsonl`, see lib/transcript_usage.py):
  - a second resume of the same agent is denied, with the checkpoint recipe in the reason;
  - a first resume of an agent whose context exceeds RESUME_WARN_TOKENS (150k; env
    COST_ROUTER_RESUME_MAX_TOKENS) is allowed with a warning suggesting a fresh agent.
Anything else (a teammate, another session, "main", an unknown name) is left alone, as is every
call when COST_ROUTER_ENFORCE=0 or the prompt carries "#direct". Any error: no output, exit 0.
"""
from __future__ import annotations

import json
import os
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent / "lib"))

RESUME_WARN_TOKENS = 150_000
MAX_RESUMES = 1
CHECKPOINT = ("start a NEW agent (Agent tool) seeded with a <=40-line checkpoint: task, branch/PR, head SHA,"
              " findings to fix, files, test commands")


def threshold() -> int:
    try:
        return max(1, int(os.environ.get("COST_ROUTER_RESUME_MAX_TOKENS", "") or RESUME_WARN_TOKENS))
    except ValueError:
        return RESUME_WARN_TOKENS


def decide(data: dict):
    """Return (decision, reason) with decision in {"deny", "warn"}, or None to stay silent."""
    import route_state  # noqa: E402
    import transcript_usage  # noqa: E402
    if route_state.enforcement_disabled():
        return None
    tool_input = data.get("tool_input") if isinstance(data.get("tool_input"), dict) else {}
    found = transcript_usage.resolve_subagent(data.get("transcript_path"), str(tool_input.get("to") or ""))
    if found is None:
        return None
    agent_id, transcript = found
    sid = data.get("session_id")
    state = route_state.load(sid) or {}
    if state.get("direct"):
        return None
    tokens = transcript_usage.context_tokens(transcript)
    size = f"~{tokens // 1000}k context tokens" if tokens else "unknown size"
    done = route_state.resume_count(sid, agent_id)
    if done >= MAX_RESUMES:
        return "deny", (f"cost-router: subagent {agent_id} was already resumed {done}x ({size}); a subagent is"
                        f" resumed at most once. Instead {CHECKPOINT}. Owner override: #direct.")
    route_state.record_resume(sid, agent_id)
    if tokens and tokens > threshold():
        return "warn", (f"cost-router: subagent {agent_id} carries {size} (> {threshold() // 1000}k); every call of"
                        f" this resume re-reads all of it. Prefer to {CHECKPOINT}.")
    return None


def main() -> int:
    try:
        data = json.load(sys.stdin)
        if not isinstance(data, dict) or data.get("tool_name") != "SendMessage":
            return 0
        from route import is_shadowed  # noqa: E402
        if is_shadowed(Path(__file__)):
            return 0
        result = decide(data)
    except Exception:
        return 0
    if not result:
        return 0
    decision, reason = result
    if decision == "deny":
        out = {"hookEventName": "PreToolUse", "permissionDecision": "deny", "permissionDecisionReason": reason}
    else:  # a warning only: the normal permission flow decides
        out = {"hookEventName": "PreToolUse", "additionalContext": reason}
    print(json.dumps({"hookSpecificOutput": out}))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
