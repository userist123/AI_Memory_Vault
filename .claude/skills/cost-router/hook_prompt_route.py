#!/usr/bin/env python3
"""UserPromptSubmit hook for the cost-router skill.

Reads the hook JSON on stdin, classifies the prompt with the routing policy and injects one
short line of `additionalContext` telling Claude which class/model/subagent the policy
recommends and to apply the `cost-router` skill. It is silent (no output) on trivial prompts
(slash commands, fewer than 4 words, plain yes/no) so it costs nothing there. Any internal
error exits 0 with no output: this hook must never block a prompt.
"""
from __future__ import annotations

import json
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent / "lib"))

LOCAL_CLASSES = {"summarize"}  # text in -> short text out: work a local model does well
TRIVIAL = {"da", "nu", "yes", "no", "ok", "continua", "continue", "merci", "thanks", "mulțumesc", "multumesc"}


def main() -> int:
    try:
        data = json.load(sys.stdin)
    except Exception:
        return 0
    prompt = str(data.get("prompt") or data.get("user_prompt") or "").strip()
    words = prompt.split()
    if not prompt or prompt.startswith("/") or len(words) < 4 or prompt.lower().rstrip(".!") in TRIVIAL:
        return 0
    try:
        from route import decide, is_shadowed, one_line  # noqa: E402
        if is_shadowed(Path(__file__)):
            return 0
        d = decide(prompt)
    except Exception:
        return 0
    ctx = one_line(d) + "."
    if d.subagent:
        ctx += (f" Do the work in a subagent of any type ({d.subagent} fits); the Agent hook sets its model"
                f" to {d.model}. Keep only its conclusion in this session.")
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
