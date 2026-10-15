#!/usr/bin/env python3
"""PreToolUse hook (matcher "Agent|Task") for the cost-router skill.

Sets the model of every subagent spawn from the policy route of its brief (description + prompt),
whatever the subagent type is:
  - no model given             -> the route's model;
  - a model given              -> kept between the route's model and one tier above it (a one-notch
                                  escalation is allowed; a model cheaper than the route is raised);
  - risk detected in the brief -> never below the risk floor (Opus);
  - Fable is never a subagent model -> Opus;
  - a reviewer/verifier subagent_type ("review"/"verif" in it) -> never below Opus.
A brief that matches no task class and carries no risk is left alone (the subagent's own
frontmatter model applies). A model value it does not recognise is left alone.
Route state (lib/route_state.py): every spawn sets state.delegated=true, which unlocks the main
session for hook_enforce_route.py. Inside the AI Memory Vault (the project has
03_IMPLEMENTATION/packages/routing/route_cli.py) a spawn for an implement/review/design/frontier
route is denied until the Vault router ran (state.vault_routed, set by hook_enforce_route.py when
Bash runs `routing.route_cli route|dispatch`); delegated is not set then. "#direct" and
COST_ROUTER_ENFORCE=0 skip that gate. Outside the Vault it is never required.
Any other error: no output, exit 0; apart from that gate this hook only rewrites `model`.
"""
from __future__ import annotations

import json
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent / "lib"))

RISK_PREFIXES = ("risk_detected", "risk_floor")
SPAWN_TOOLS = ("Agent", "Task")  # "Task" is the legacy name of the spawn tool
REVIEWER_MARKS = ("review", "verif")  # vault-reviewer, code-review, verifier: never below Opus
VAULT_ROUTER_REASON = ("cost-router: run the Vault router first: python -m routing.route_cli route --goal \"<task>\""
                       " (PYTHONPATH=03_IMPLEMENTATION/packages), show the owner agent/model/cost, then dispatch.")


def gate_and_mark(data: dict):
    """Return a deny reason for the Vault-router gate, else mark the session delegated and return None."""
    try:
        import route_state  # noqa: E402
        sid = data.get("session_id")
        state = route_state.load(sid)
        if state is None:
            return None
        if (not route_state.enforcement_disabled() and not state.get("direct")
                and state.get("task_class") in route_state.VAULT_GATED_CLASSES
                and not state.get("vault_routed") and route_state.in_vault(data)):
            return VAULT_ROUTER_REASON
        if not state.get("delegated"):
            route_state.mark(sid, "delegated")  # a marker file: no counter write can clear it
    except Exception:
        return None
    return None


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
    reviewer = any(m in str(tool_input.get("subagent_type") or "").lower() for m in REVIEWER_MARKS)
    if d.task_class == "unclassified" and not risky:
        if reviewer and (explicit is None or policy.rank(explicit) < policy.rank("opus") or explicit == "fable"):
            return "opus", d
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
    if reviewer and policy.rank(target) < policy.rank("opus"):
        target = "opus"  # verification is the step a cheap model must not do
    return target, d


def main() -> int:
    try:
        data = json.load(sys.stdin)
    except Exception:
        return 0
    if not isinstance(data, dict) or data.get("tool_name") not in SPAWN_TOOLS:
        return 0
    tool_input = data.get("tool_input")
    if not isinstance(tool_input, dict):
        return 0
    try:
        from route import _load_router, is_shadowed, policy_path  # noqa: E402
        if is_shadowed(Path(__file__)):
            return 0
    except Exception:
        return 0
    reason = gate_and_mark(data)
    if reason:
        print(json.dumps({"hookSpecificOutput": {"hookEventName": "PreToolUse", "permissionDecision": "deny",
                                                 "permissionDecisionReason": reason}}))
        return 0
    try:
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
