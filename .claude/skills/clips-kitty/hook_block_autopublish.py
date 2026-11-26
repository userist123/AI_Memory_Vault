"""PreToolUse hook for mcp__clips-kitty__queue_video.

Denies the call when `publish_when_done` is truthy: that flag makes the engine publish EVERY clip of the
video when the job ends (jobs.py _run_follow_up), with no pick, no caption check and no plan, and it is not
covered by the deny rules on the execute tools. Publishing goes only through the plan flow in SKILL.md
section 6. Stdlib only; never crashes (any error -> no decision, the normal permission flow applies).
"""
import json
import sys


def decide(data) -> dict | None:
    if not isinstance(data, dict):
        return None
    args = data.get("tool_input")
    if not isinstance(args, dict):
        return None
    flag = args.get("publish_when_done")
    if flag in (None, False, 0, "", "false", "False", "0", "no"):
        return None
    return {
        "hookSpecificOutput": {
            "hookEventName": "PreToolUse",
            "permissionDecision": "deny",
            "permissionDecisionReason": (
                "publish_when_done is blocked: it would publish every clip automatically. Queue without it; "
                "publishing goes through the plan flow (clips-kitty SKILL.md section 6) after the owner picks clips."
            ),
        }
    }


def main():
    try:
        data = json.loads(sys.stdin.buffer.read().decode("utf-8-sig", errors="replace"))
        out = decide(data)
    except Exception:
        return
    if out:
        sys.stdout.reconfigure(encoding="utf-8")
        print(json.dumps(out, ensure_ascii=False))


if __name__ == "__main__":
    main()
