"""test_council_efficiency_report_fix.py — regression tests for the
top_optimization_candidate bug found by Antigravity in commit 6e256a9.

Bug: build_efficiency_report() picked the globally top agent and the
globally top tier INDEPENDENTLY, then concatenated them into a string,
producing pairs that were never actually observed together (e.g.
"CRITIC / light" when CRITIC always runs on "standard").

Fix: pick the (agent_id, model_tier) pair with the highest actual_total
as it actually appears in the raw calls.
"""
import json

import pytest

from cognitive_core.council_usage_audit import CouncilUsageAuditReport, PerCallUsage
from cognitive_core.council_efficiency_report import (
    build_efficiency_report,
    compare_baseline_vs_optimized,
    detect_regressions,
    to_csv_agents,
    to_csv_models,
    to_csv_runs,
    to_csv_tiers,
    to_markdown,
    CostProfile,
)


def _make_report(run_id, router_est, router_act, synth_est, synth_act, wall_time=1.0):
    router_call = PerCallUsage(
        agent_id="ROUTER", kind="specialist", provider="fake", model="fake-light",
        model_tier="light", estimated_input=router_est, estimated_output=router_est,
        actual_input=router_act, actual_output=router_act, cached_input=0,
        reasoning_tokens=0, actual_total=router_act * 2, estimated_total=router_est * 2,
        source="provider",
    )
    synth_call = PerCallUsage(
        agent_id="SYNTHESIS", kind="synthesis", provider="fake", model="fake-heavy",
        model_tier="heavy", estimated_input=synth_est, estimated_output=synth_est,
        actual_input=synth_act, actual_output=synth_act, cached_input=0,
        reasoning_tokens=0, actual_total=synth_act * 2, estimated_total=synth_est * 2,
        source="provider",
    )
    return CouncilUsageAuditReport(
        run_id=run_id, specialist_calls=1, synthesis_calls=1, total_model_calls=2,
        estimated_input=router_est + synth_est, actual_input=router_act + synth_act,
        estimated_output=router_est + synth_est, actual_output=router_act + synth_act,
        estimated_total=(router_est + synth_est) * 2, actual_total=(router_act + synth_act) * 2,
        context_bytes=100, context_estimated_tokens=router_est + synth_est,
        wall_time_seconds=wall_time, calls=[router_call, synth_call],
    )


def test_verdict_pairing_is_self_consistent():
    reports = [_make_report("r1", 50, 50, 100, 300)]
    report = build_efficiency_report(reports)
    candidate = report.verdict.top_optimization_candidate
    assert candidate == "SYNTHESIS / heavy"


def test_bug_regression_agent_tier_pair_must_be_real():
    """Reproduces the exact scenario reported by Antigravity: an agent
    (CRITIC, tier=standard) has the highest INDIVIDUAL agent total, while
    a different tier (light, from 3 other agents combined) has the
    highest GLOBAL tier total. The verdict must never pair CRITIC with
    light, since CRITIC never ran on light.
    """
    def make_call(agent_id, tier, total):
        return PerCallUsage(
            agent_id=agent_id, kind="specialist", provider="fake", model=f"fake-{tier}",
            model_tier=tier, estimated_input=total // 2, estimated_output=total // 2,
            actual_input=total // 2, actual_output=total // 2, cached_input=0,
            reasoning_tokens=0, actual_total=total, estimated_total=total, source="provider",
        )

    report = CouncilUsageAuditReport(
        run_id="r1", specialist_calls=4, synthesis_calls=0, total_model_calls=4,
        actual_total=1000, estimated_total=1000,
        calls=[
            make_call("ROUTER", "light", 100),
            make_call("RETRIEVAL", "light", 100),
            make_call("VERIFIER", "light", 100),
            make_call("CRITIC", "standard", 300),
        ],
    )

    eff = build_efficiency_report([report])
    agent, tier = eff.verdict.top_optimization_candidate.split(" / ")
    real_pairs = {(c.agent_id, c.model_tier) for c in report.calls}
    assert (agent, tier) in real_pairs
    assert (agent, tier) == ("CRITIC", "standard")


def test_empty_reports_returns_zeroed_report():
    report = build_efficiency_report([])
    assert report.run_count == 0


def test_aggregation_sums_across_runs():
    reports = [_make_report("r1", 50, 55, 100, 120), _make_report("r2", 60, 58, 110, 130)]
    report = build_efficiency_report(reports)
    assert report.run_count == 2
    assert report.actual_total == sum(r.actual_total for r in reports)


def test_variance_handles_zero_estimated_without_crash():
    reports = [_make_report("r1", 0, 10, 0, 20)]
    report = build_efficiency_report(reports)
    assert report.variance.delta_percent == 0.0


def test_agent_breakdown_share_sums_to_100():
    reports = [_make_report("r1", 50, 50, 100, 100)]
    report = build_efficiency_report(reports)
    total_share = sum(b.share_of_total for b in report.agent_breakdown.values())
    assert round(total_share, 1) == 100.0


def test_ab_comparison_detects_token_savings():
    baseline = [_make_report(f"b{i}", 50, 50, 100, 100) for i in range(3)]
    optimized = [_make_report(f"o{i}", 50, 45, 100, 60) for i in range(3)]
    result = compare_baseline_vs_optimized(baseline, optimized)
    assert result.tokens_saved > 0


def test_regression_detection_flags_token_regression():
    baseline = [_make_report(f"b{i}", 50, 50, 100, 100) for i in range(3)]
    baseline_eff = build_efficiency_report(baseline)
    regressed = [_make_report(f"r{i}", 50, 50, 100, 400) for i in range(3)]
    regressed_eff = build_efficiency_report(regressed)
    flags = detect_regressions(regressed_eff, baseline_eff, threshold_percent=20.0)
    assert "TOKEN REGRESSION" in flags


def test_csv_and_markdown_exports_still_work():
    reports = [_make_report("r1", 50, 50, 100, 100)]
    report = build_efficiency_report(reports)
    assert "ROUTER" in to_csv_agents(report)
    md = to_markdown(report)
    assert "Council efficiency verdict" in md


def test_json_export_is_valid():
    reports = [_make_report("r1", 50, 50, 100, 100)]
    report = build_efficiency_report(reports)
    parsed = json.loads(report.to_json())
    assert parsed["run_count"] == 1
