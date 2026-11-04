#!/usr/bin/env python3
"""PreToolUse hook: apply agent-integrity-guard to the tool call about to run.

Fail-closed. A security hook that answers "allow" (or stays silent, which
Claude Code reads as allow) whenever it cannot evaluate a call is a backdoor:
anyone who can make guard.py fail to import, or feed an unparseable payload,
would switch the shield off for the rest of the session. So this hook NEVER
emits a silent allow on an error path:

  - normal path: guard.decide() returns allow/ask/deny.
  - guard.py cannot be imported: a small dependency-free check in THIS file
    still denies the obvious dangerous shapes (egress, persistence, writing the
    guard/settings, reading secrets) and asks for everything else.
  - the payload cannot be parsed at all: ask (the owner confirms) — never allow.

`allow` is the only case that prints nothing; everything else prints an explicit
ask/deny decision.
"""
from __future__ import annotations

import json
import re
import sys
from pathlib import Path

ALLOW, ASK, DENY = "allow", "ask", "deny"


def _emit(action: str, reason: str) -> None:
    if action == ALLOW:
        print(json.dumps({}))
        return
    print(json.dumps({"hookSpecificOutput": {
        "hookEventName": "PreToolUse",
        "permissionDecision": "deny" if action == DENY else "ask",
        "permissionDecisionReason": reason,
    }}))


# ── dependency-free fallback, used only if guard.py will not import ──
_FALLBACK_DENY = re.compile(r"""(?ix)
    [a-z][a-z0-9+.\-]*://(?!(?:localhost|127\.0\.0\.1|api\.anthropic\.com|github\.com|api\.github\.com)[/:\s]) |
    (?<![\w.])(curl|wget|nc|ncat|netcat|scp|sftp|ssh|telnet|socat|ftp|tftp|dig|nslookup|httpie)(?![\w]) |
    \\\\[a-z0-9][a-z0-9.\-]*\\ |
    agent-integrity-guard | [/\\]\.claude[/\\]settings | disableallhooks |
    schtasks[^\n]*\/create | \bcrontab\b | \\run\b | \.git[/\\]hooks[/\\] |
    \.env\b | id_rsa | id_ed25519 | [/\\]\.ssh[/\\] | [/\\]\.aws[/\\]
""")


def _fallback(payload_text: str) -> tuple[str, str]:
    """Called when guard.py is unavailable. Deny obvious danger, ask otherwise."""
    if _FALLBACK_DENY.search(payload_text or ""):
        return DENY, ("[agent-integrity-guard] the guard module failed to load and this call "
                      "matches a dangerous shape (egress / persistence / guard or secret access); "
                      "refused. Fix the guard before continuing.")
    return ASK, ("[agent-integrity-guard] the guard module failed to load, so this call could not "
                 "be evaluated. Confirm it is safe. The shield is not protecting you until the guard is fixed.")


def main() -> int:
    raw = sys.stdin.read()
    try:
        payload = json.loads(raw) if raw.strip() else {}
        if not isinstance(payload, dict):
            raise ValueError("payload is not an object")
    except Exception:
        # Cannot even read the call. Do not allow silently.
        _emit(ASK, "[agent-integrity-guard] the tool call could not be parsed; confirm it is safe.")
        return 0

    tool_name = payload.get("tool_name", "")
    tool_input = payload.get("tool_input", {})
    if not isinstance(tool_input, dict):
        tool_input = {"_raw": tool_input}

    try:
        sys.path.insert(0, str(Path(__file__).resolve().parent))
        import guard  # noqa: E402
    except Exception:
        action, reason = _fallback(raw)
        _emit(action, reason)
        return 0

    try:
        d = guard.decide(tool_name, tool_input)
    except Exception as e:
        # guard.decide is itself defensive; if it still raised, do not allow.
        _emit(ASK, f"[agent-integrity-guard] internal error evaluating the call ({e}); confirm it is safe.")
        return 0

    if d.action == guard.ALLOW:
        _emit(ALLOW, "")
    else:
        _emit(d.action, f"[agent-integrity-guard {d.control}] {d.reason}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
