#!/usr/bin/env python3
"""PreToolUse hook: apply agent-integrity-guard to the tool call about to run.

Reads the hook payload (JSON on stdin), asks guard.decide(), and prints the
PreToolUse permission decision Claude Code expects:
  deny  -> permissionDecision "deny"  (the call is blocked, reason shown)
  ask   -> permissionDecision "ask"   (the owner confirms)
  allow -> no decision emitted; other hooks and normal permissions still apply.

Fail-open only on an unexpected error that is NOT a risky shape — guard.decide
already turns a parse error on an egress/persistence-looking call into deny.
Stays silent (exit 0) for allow so it never adds noise to ordinary work.
"""
from __future__ import annotations

import json
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
try:
    import guard
except Exception:
    print(json.dumps({}))
    sys.exit(0)


def main() -> int:
    raw = sys.stdin.read()
    try:
        payload = json.loads(raw) if raw.strip() else {}
    except Exception:
        # Unreadable payload: do not block, but say nothing that could be read as approval.
        print(json.dumps({}))
        return 0

    tool_name = payload.get("tool_name", "")
    tool_input = payload.get("tool_input", {}) if isinstance(payload.get("tool_input"), dict) else {}

    try:
        d = guard.decide(tool_name, tool_input)
    except Exception as e:
        # guard.decide is itself defensive; this is the last resort.
        print(json.dumps({"systemMessage": f"agent-integrity-guard internal error: {e}"}))
        return 0

    if d.action == guard.ALLOW:
        print(json.dumps({}))
        return 0

    reason = f"[agent-integrity-guard {d.control}] {d.reason}"
    out = {
        "hookSpecificOutput": {
            "hookEventName": "PreToolUse",
            "permissionDecision": "deny" if d.action == guard.DENY else "ask",
            "permissionDecisionReason": reason,
        }
    }
    print(json.dumps(out))
    return 0


if __name__ == "__main__":
    sys.exit(main())
