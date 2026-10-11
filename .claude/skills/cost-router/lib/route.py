#!/usr/bin/env python3
"""Self-contained route helper for the cost-router skill.

Usage: python3 route.py "<task description>" [--risk high] [--subagent] [--json]

Finds the router module and the policy in this order: `lib/claude_model_router.py` +
`policy.json` next to this file (installed copy), else the AI Memory Vault repository
layout relative to this file (`03_IMPLEMENTATION/packages/routing` and `04_CONFIG`).
Stdlib only; no network.
"""
from __future__ import annotations

import importlib.util
import json
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
SKILL_DIR = HERE.parent


def _load_router():
    local = HERE / "claude_model_router.py"
    repo = SKILL_DIR.parents[2] / "03_IMPLEMENTATION" / "packages" / "routing" / "claude_model_router.py"
    for path in (local, repo):
        if path.exists():
            spec = importlib.util.spec_from_file_location("claude_model_router", path)
            mod = importlib.util.module_from_spec(spec)
            sys.modules[spec.name] = mod  # dataclasses need the module registered before exec
            spec.loader.exec_module(mod)
            return mod
    raise FileNotFoundError("claude_model_router.py not found next to route.py nor in the repository")


def policy_path() -> Path:
    local = SKILL_DIR / "policy.json"
    repo = SKILL_DIR.parents[2] / "04_CONFIG" / "claude_model_routing.json"
    return local if local.exists() else repo


def is_shadowed(hook_file) -> bool:
    """True when `hook_file` is a project copy and the same hook of the user-scope install is
    registered in ~/.claude/settings.json: the user copy answers, the project copy stays silent
    (one route line per prompt, one model decision per subagent spawn)."""
    home_claude = Path.home() / ".claude"
    try:
        Path(hook_file).resolve().relative_to(home_claude.resolve())
        return False  # this is the user copy itself
    except ValueError:
        pass
    try:
        text = (home_claude / "settings.json").read_text(encoding="utf-8")
    except OSError:
        return False
    return f"cost-router/{Path(hook_file).name}" in text.replace("\\\\", "/").replace("\\", "/")


def decide(goal: str, *, risk: str = "medium", for_subagent: bool = False):
    mod = _load_router()
    policy = mod.ClaudeModelPolicy.from_file(policy_path())
    return mod.route(policy, goal, risk=risk, for_subagent=for_subagent)


def one_line(d) -> str:
    target = f"{d.subagent} subagent" if d.subagent else "main session"
    line = f"cost-router: class={d.task_class} ({d.confidence}) -> {target}, {d.model}/{d.effort}"
    if d.verifier_model:
        line += f"; independent verifier: {d.verifier_model}"
    if d.escalate_to:
        line += f"; on failed check escalate to {d.escalate_to}"
    return line


def main(argv=None) -> int:
    args = list(sys.argv[1:] if argv is None else argv)
    as_json = "--json" in args
    sub = "--subagent" in args
    risk = "medium"
    if "--risk" in args:
        risk = args[args.index("--risk") + 1]
        del args[args.index("--risk"):args.index("--risk") + 2]
    goal = " ".join(a for a in args if not a.startswith("--"))
    if not goal:
        print(__doc__)
        return 2
    d = decide(goal, risk=risk, for_subagent=sub)
    print(json.dumps(d.to_dict(), ensure_ascii=False, indent=2) if as_json else one_line(d)
          + f"\n  est ${d.estimated_cost_usd:.4f} vs Fable ${d.cost_on_fable_usd:.4f}; why: {d.rationale}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
