"""test_council_efficiency_report.py — B5 contract tests.

All reports here are constructed directly as CouncilUsageAuditReport
objects (B4 output shape) — no provider, no network, deterministic.
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


def test_empty_reports_returns_zeroed_report():
    report = build_efficiency_report([])
    assert report.run_count == 0
    assert report.actual_total == 0


def test_aggregation_sums_across_runs():
    reports = [_make_report("r1", 50, 55, 100, 120), _make_report("r2", 60, 58, 110, 130)]
    report = build_efficiency_report(reports)
    assert report.run_count == 2
    assert report.total_model_calls == 4
    assert report.actual_total == sum(r.actual_total for r in reports)


def test_variance_is_computed_independently_of_estimated():
    reports = [_make_report("r1", 50, 100, 100, 200)]
    report = build_efficiency_report(reports)
    assert report.variance.estimated == 300
    assert report.variance.actual == 600
    assert report.variance.delta == 300
    assert report.variance.delta_percent == 100.0


def test_variance_handles_zero_estimated_without_crash():
    reports = [_make_report("r1", 0, 10, 0, 20)]
    report = build_efficiency_report(reports)
    assert report.variance.estimated == 0
    assert report.variance.delta_percent == 0.0


def test_agent_breakdown_share_sums_to_100():
    reports = [_make_report("r1", 50, 50, 100, 100)]
    report = build_efficiency_report(reports)
    total_share = sum(b.share_of_total for b in report.agent_breakdown.values())
    assert round(total_share, 1) == 100.0


def test_tier_breakdown_separates_light_and_heavy():
    reports = [_make_report("r1", 50, 50, 100, 100)]
    report = build_efficiency_report(reports)
    assert set(report.tier_breakdown) == {"light", "heavy"}
    assert report.tier_breakdown["heavy"].actual_total > report.tier_breakdown["light"].actual_total


def test_provider_model_breakdown_keyed_correctly():
    reports = [_make_report("r1", 50, 50, 100, 100)]
    report = build_efficiency_report(reports)
    assert "fake/fake-light/light" in report.provider_model_breakdown
    assert "fake/fake-heavy/heavy" in report.provider_model_breakdown


def test_context_efficiency_ratios_computed():
    reports = [_make_report("r1", 50, 50, 100, 100)]
    report = build_efficiency_report(reports)
    assert report.context_efficiency.context_to_input_ratio > 0
    assert report.context_efficiency.output_to_input_ratio > 0


def test_cost_model_is_opt_in_and_zero_when_no_profiles():
    reports = [_make_report("r1", 50, 50, 100, 100)]
    report = build_efficiency_report(reports)
    assert report.cost is None


def test_cost_model_computes_when_profile_provided():
    reports = [_make_report("r1", 50, 50, 100, 100)]
    profiles = {
        ("fake", "fake-light"): CostProfile(provider="fake", model="fake-light", input_cost_per_1m=1.0, output_cost_per_1m=2.0),
        ("fake", "fake-heavy"): CostProfile(provider="fake", model="fake-heavy", input_cost_per_1m=5.0, output_cost_per_1m=10.0),
    }
    report = build_efficiency_report(reports, cost_profiles=profiles)
    assert report.cost is not None
    assert report.cost.actual_cost > 0


def test_verdict_identifies_top_optimization_candidate():
    reports = [_make_report("r1", 50, 50, 100, 300)]
    report = build_efficiency_report(reports)
    assert report.verdict.top_optimization_candidate is not None
    assert "SYNTHESIS" in report.verdict.top_optimization_candidate


def test_ab_comparison_detects_token_savings():
    baseline = [_make_report(f"b{i}", 50, 50, 100, 100) for i in range(3)]
    optimized = [_make_report(f"o{i}", 50, 45, 100, 60) for i in range(3)]
    result = compare_baseline_vs_optimized(baseline, optimized)
    assert result.tokens_saved > 0
    assert result.tokens_saved_percent > 0


def test_regression_detection_flags_token_regression():
    baseline = [_make_report(f"b{i}", 50, 50, 100, 100) for i in range(3)]
    baseline_eff = build_efficiency_report(baseline)
    regressed = [_make_report(f"r{i}", 50, 50, 100, 400) for i in range(3)]
    regressed_eff = build_efficiency_report(regressed)

    flags = detect_regressions(regressed_eff, baseline_eff, threshold_percent=20.0)
    assert "TOKEN REGRESSION" in flags
    assert regressed_eff.verdict.token_regression is True


def test_regression_detection_empty_when_stable():
    baseline = [_make_report(f"b{i}", 50, 50, 100, 100) for i in range(3)]
    baseline_eff = build_efficiency_report(baseline)
    stable = [_make_report(f"s{i}", 50, 51, 100, 101) for i in range(3)]
    stable_eff = build_efficiency_report(stable)

    flags = detect_regressions(stable_eff, baseline_eff, threshold_percent=20.0)
    assert flags == []


def test_csv_runs_has_header_and_one_row_per_run():
    reports = [_make_report("r1", 50, 50, 100, 100), _make_report("r2", 50, 50, 100, 100)]
    csv_text = to_csv_runs(reports)
    lines = [l for l in csv_text.strip().splitlines()]
    assert lines[0].startswith("run_id")
    assert len(lines) == 3


def test_csv_agents_tiers_models_are_nonempty():
    reports = [_make_report("r1", 50, 50, 100, 100)]
    report = build_efficiency_report(reports)
    assert "ROUTER" in to_csv_agents(report)
    assert "light" in to_csv_tiers(report)
    assert "fake" in to_csv_models(report)


def test_markdown_report_contains_verdict_section():
    reports = [_make_report("r1", 50, 50, 100, 100)]
    report = build_efficiency_report(reports)
    md = to_markdown(report)
    assert "Council efficiency verdict" in md
    assert "Token regression: NO" in md


def test_json_export_is_valid_and_complete():
    reports = [_make_report("r1", 50, 50, 100, 100)]
    report = build_efficiency_report(reports)
    parsed = json.loads(report.to_json())
    assert parsed["run_count"] == 1
    assert "verdict" in parsed
    assert "agent_breakdown" in parsed


def test_module_never_imports_frozen_or_orchestration_core():
    import inspect
    import cognitive_core.council_efficiency_report as mod
    source = inspect.getsource(mod)
    assert "from .Council_Orchestrator" not in source
    assert "import Council_Orchestrator" not in source
    assert "99_SYSTEM" not in source
    assert "from .planning" not in source
    assert "from .council_budget_controller" not in source
