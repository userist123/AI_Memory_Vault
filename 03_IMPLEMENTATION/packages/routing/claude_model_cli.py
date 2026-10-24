"""CLI for the Claude Code model router.

    python -m routing.claude_model_cli route --goal "<task>" [--input-tokens N] [--output-tokens N]
                                              [--risk low|medium|high|critical] [--model alias] [--subagent]
    python -m routing.claude_model_cli report [--project-dir PATH] [--all-projects] [--json]
    python -m routing.claude_model_cli policy [--json]
"""
from __future__ import annotations

import argparse
import json
from pathlib import Path

from .claude_model_router import DEFAULT_POLICY_PATH, ClaudeModelPolicy, route
from .claude_usage_report import aggregate, default_projects_dir, format_table, iter_transcripts, iter_usage_records, priced_report


def build_parser() -> argparse.ArgumentParser:
    p = argparse.ArgumentParser(prog="python -m routing.claude_model_cli")
    p.add_argument("--policy", type=Path, default=DEFAULT_POLICY_PATH)
    sub = p.add_subparsers(dest="cmd", required=True)
    r = sub.add_parser("route", help="recommend a model + effort for a task")
    r.add_argument("--goal", required=True)
    r.add_argument("--input-tokens", type=int, default=20_000)
    r.add_argument("--output-tokens", type=int, default=4_000)
    r.add_argument("--risk", choices=["low", "medium", "high", "critical"], default="medium")
    r.add_argument("--model", help="explicitly requested model alias")
    r.add_argument("--subagent", action="store_true", help="route for a subagent (Fable excluded)")
    r.add_argument("--json", action="store_true")
    u = sub.add_parser("report", help="aggregate real token usage from local Claude Code transcripts")
    u.add_argument("--project-dir", type=Path, default=Path.cwd())
    u.add_argument("--all-projects", action="store_true")
    u.add_argument("--projects-dir", type=Path, default=None)
    u.add_argument("--json", action="store_true")
    pol = sub.add_parser("policy", help="print the routing table")
    pol.add_argument("--json", action="store_true")
    return p


def _print_route(d) -> None:
    print(f"class      : {d.task_class} (confidence {d.confidence}; matched {', '.join(d.matched_keywords) or '-'})")
    print(f"model      : {d.model} -> {d.model_id}   effort: {d.effort}")
    print(f"subagent   : {d.subagent or '- (main session)'}")
    print(f"verifier   : {d.verifier_model or '-'}   escalate on failure to: {d.escalate_to or '-'}")
    print(f"est. cost  : ${d.estimated_cost_usd:.4f}  (same tokens on Fable ${d.cost_on_fable_usd:.4f}; saves ${d.savings_vs_fable_usd:.4f})")
    print(f"rules      : {', '.join(d.applied_rules) or '-'}")
    print(f"why        : {d.rationale}")


def main(argv=None) -> int:
    a = build_parser().parse_args(argv)
    policy = ClaudeModelPolicy.from_file(a.policy)
    if a.cmd == "route":
        d = route(policy, a.goal, input_tokens=a.input_tokens, output_tokens=a.output_tokens,
                  risk=a.risk, requested_model=a.model, for_subagent=a.subagent)
        print(json.dumps(d.to_dict(), ensure_ascii=False, indent=2)) if a.json else _print_route(d)
        return 0
    if a.cmd == "report":
        projects = a.projects_dir or default_projects_dir()
        paths = list(iter_transcripts(projects, None if a.all_projects else a.project_dir))
        if not paths:
            print(f"no transcripts found under {projects}" + ("" if a.all_projects else f" for {a.project_dir}"))
            return 1
        rep = priced_report(policy, aggregate(iter_usage_records(paths)))
        rep["transcripts"] = len(paths)
        print(json.dumps(rep, indent=2) if a.json else format_table(rep) + f"\n({len(paths)} transcript files)")
        return 0
    if a.cmd == "policy":
        if a.json:
            print(json.dumps(policy.raw, ensure_ascii=False, indent=2))
            return 0
        print(f"{'class':<12}{'model':<8}{'effort':<8}{'subagent':<16}rationale")
        for tc in policy.task_classes.values():
            print(f"{tc.name:<12}{tc.model:<8}{tc.effort:<8}{(tc.subagent or '-'):<16}{tc.rationale}")
        print(f"\ndefault: {policy.default_model}/{policy.default_effort}; escalation: {' -> '.join(policy.escalation_order)}")
        print(f"\n{'model':<8}{'id':<20}{'$in':>6}{'$out':>6}{'$cache_rd':>10}  (per MTok, as of {policy.raw.get('pricing_as_of')})")
        for m in sorted(policy.models.values(), key=lambda s: -s.tier_rank):
            print(f"{m.alias:<8}{m.model_id:<20}{m.price.input:>6.2f}{m.price.output:>6.2f}{m.price.cache_read:>10.2f}")
        return 0
    return 2


if __name__ == "__main__":
    raise SystemExit(main())
