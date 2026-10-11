#!/usr/bin/env python3
"""PreToolUse hook (matcher "Agent") for the cost-router skill.

Sets the model of every subagent spawn from the policy route of its brief (description + prompt),
whatever the subagent type is:
  - no model given             -> the route's model;
  - a model given              -> kept between the route's model and one tier above it (a one-notch
                                  escalation is allowed; a model cheaper than the route is raised);
  - risk detected in the brief -> never below the risk floor (Opus);
  - Fable is never a subagent model -> Opus.
A brief that matches no task class and carries no risk is left alone (the subagent's own
frontmatter model applies). A model value it does not recognise is left alone.
Any error: no output, exit 0. This hook never blocks a spawn; it only rewrites `model`.
"""
from __future__ import annotations

import json
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent / "lib"))

RISK_PREFIXES = ("risk_detected", "risk_floor")


def _alias(policy, value):
    if value in (None, ""):
        return None
    v = str(value).strip().lower()
    if v in policy.models:
        return v
    for alias, spec in policy.models.items():
        if v == spec.model_id or v.startswith(spec.model_id):
            return alias
    return "unknown"


def choose(policy, mod, tool_input: dict):
    """Return (target_alias_or_None, route_decision)."""
    brief = f"{tool_input.get('description') or ''} {str(tool_input.get('prompt') or '')[:2000]}"
    d = mod.route(policy, brief, for_subagent=True)
    risky = any(r.startswith(RISK_PREFIXES) for r in d.applied_rules)
    explicit = _alias(policy, tool_input.get("model"))
    if explicit == "unknown":
        return None, d
    if d.task_class == "unclassified" and not risky:
        return None, d
    lo = d.model
    hi = policy.higher_tier(lo) or lo
    if hi == "fable":
        hi = "opus"
    target = explicit or lo
    if policy.rank(target) < policy.rank(lo):
        target = lo
    if policy.rank(target) > policy.rank(hi):
        target = hi
    if target == "fable":
        target = "opus"
    return target, d


def main() -> int:
    try:
        data = json.load(sys.stdin)
    except Exception:
        return 0
    if not isinstance(data, dict) or data.get("tool_name") != "Agent":
        return 0
    tool_input = data.get("tool_input")
    if not isinstance(tool_input, dict):
        return 0
    try:
        from route import _load_router, is_shadowed, policy_path  # noqa: E402
        if is_shadowed(Path(__file__)):
            return 0
        mod = _load_router()
        policy = mod.ClaudeModelPolicy.from_file(policy_path())
        target, d = choose(policy, mod, tool_input)
    except Exception:
        return 0
    if not target or target == tool_input.get("model"):
        return 0
    risky = any(r.startswith(RISK_PREFIXES) for r in d.applied_rules)
    before = tool_input.get("model") or "agent default"
    note = f"cost-router: subagent model {before} -> {target} (class {d.task_class}{', risk floor' if risky else ''})"
    updated = dict(tool_input)
    updated["model"] = target
    print(json.dumps({"hookSpecificOutput": {
        "hookEventName": "PreToolUse",
        "permissionDecision": "allow",
        "permissionDecisionReason": note,
        "updatedInput": updated,
        "additionalContext": note + ".",
    }}))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
