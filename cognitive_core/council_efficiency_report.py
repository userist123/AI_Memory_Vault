"""council_efficiency_report.py — B5: historical aggregation over multiple
B4 audit reports (cognitive_core.council_usage_audit.CouncilUsageAuditReport).

Answers questions B4 cannot answer alone:
  - How much does the Council consume over time, and where do tokens go?
  - What savings do the gates actually produce?
  - Which tier/model is too expensive?
  - Where does estimated vs actual diverge?

Provider-neutral: accepts reports from Fake, Local/Ollama, OpenAI, or any
future provider without special-casing any of them.

This module never imports Planner, Analyzer, CouncilBudgetController,
Executive, or the Council orchestration file. It is a pure read-only
aggregation layer over a list of CouncilUsageAuditReport objects.
"""
from __future__ import annotations

import csv
import io
import json
import statistics
from dataclasses import dataclass, field, asdict
from typing import Any, Dict, List, Optional, Sequence, Tuple

from .council_usage_audit import CouncilUsageAuditReport


@dataclass
class TokenStats:
    avg: float = 0.0
    median: float = 0.0
    min: float = 0.0
    max: float = 0.0
    p95: float = 0.0
    n: int = 0


def _stats(values: Sequence[float]) -> TokenStats:
    values = [float(v) for v in values]
    if not values:
        return TokenStats()
    if len(values) == 1:
        v = values[0]
        return TokenStats(avg=v, median=v, min=v, max=v, p95=v, n=1)
    quantiles = statistics.quantiles(values, n=100, method="inclusive")
    return TokenStats(
        avg=statistics.fmean(values),
        median=statistics.median(values),
        min=min(values),
        max=max(values),
        p95=quantiles[94],
        n=len(values),
    )


@dataclass
class VarianceReport:
    estimated: int = 0
    actual: int = 0
    delta: int = 0
    delta_percent: float = 0.0


def _variance(estimated: int, actual: int) -> VarianceReport:
    delta = actual - estimated
    delta_percent = 0.0 if estimated == 0 else round((delta / estimated) * 100.0, 4)
    return VarianceReport(estimated=estimated, actual=actual, delta=delta, delta_percent=delta_percent)


@dataclass
class AgentBreakdown:
    calls: int = 0
    estimated: int = 0
    actual: int = 0
    avg: float = 0.0
    p95: float = 0.0
    share_of_total: float = 0.0


@dataclass
class TierBreakdown:
    calls: int = 0
    actual_input: int = 0
    actual_output: int = 0
    actual_total: int = 0
    avg_total: float = 0.0
    tokens_per_call: float = 0.0
    share_of_total: float = 0.0


@dataclass
class ProviderModelBreakdown:
    provider: str = ""
    model: str = ""
    model_tier: str = ""
    calls: int = 0
    actual_total: int = 0
    estimated_total: int = 0
    share_of_total: float = 0.0


@dataclass
class ContextEfficiency:
    context_estimated_tokens_total: int = 0
    actual_input_total: int = 0
    actual_output_total: int = 0
    context_to_input_ratio: float = 0.0
    output_to_input_ratio: float = 0.0


@dataclass
class CostProfile:
    provider: str
    model: str
    input_cost_per_1m: float = 0.0
    cached_input_cost_per_1m: float = 0.0
    output_cost_per_1m: float = 0.0


def compute_cost(input_tokens: int, cached_tokens: int, output_tokens: int, profile: CostProfile) -> float:
    non_cached_input = max(0, input_tokens - cached_tokens)
    cost = (non_cached_input / 1_000_000.0) * profile.input_cost_per_1m
    cost += (cached_tokens / 1_000_000.0) * profile.cached_input_cost_per_1m
    cost += (output_tokens / 1_000_000.0) * profile.output_cost_per_1m
    return cost


@dataclass
class CostSummary:
    estimated_cost: float = 0.0
    actual_cost: float = 0.0
    cost_delta: float = 0.0
    cost_per_run: float = 0.0
    cost_per_specialist: float = 0.0
    cost_per_synthesis: float = 0.0


@dataclass
class ABComparisonResult:
    baseline_run_count: int = 0
    optimized_run_count: int = 0
    tokens_saved: float = 0.0
    tokens_saved_percent: float = 0.0
    calls_saved: float = 0.0
    latency_delta_seconds: float = 0.0


@dataclass
class EfficiencyVerdict:
    runs_analyzed: int = 0
    avg_actual_tokens_per_run: float = 0.0
    median_actual_tokens_per_run: float = 0.0
    p95_actual_tokens_per_run: float = 0.0
    estimated_vs_actual_percent: float = 0.0
    specialist_share_percent: float = 0.0
    synthesis_share_percent: float = 0.0
    heavy_tier_share_percent: float = 0.0
    context_to_input_ratio: float = 0.0
    token_regression: bool = False
    top_optimization_candidate: Optional[str] = None
    top_optimization_reason: Optional[str] = None


@dataclass
class CouncilEfficiencyReport:
    run_count: int = 0
    total_model_calls: int = 0
    total_specialist_calls: int = 0
    total_synthesis_calls: int = 0

    estimated_input: int = 0
    actual_input: int = 0
    estimated_output: int = 0
    actual_output: int = 0
    estimated_total: int = 0
    actual_total: int = 0
    cached_input: int = 0
    reasoning_tokens: int = 0

    variance: VarianceReport = field(default_factory=VarianceReport)

    actual_total_per_run: TokenStats = field(default_factory=TokenStats)
    actual_total_per_specialist: TokenStats = field(default_factory=TokenStats)
    actual_total_per_synthesis: TokenStats = field(default_factory=TokenStats)
    wall_time_stats: TokenStats = field(default_factory=TokenStats)

    agent_breakdown: Dict[str, AgentBreakdown] = field(default_factory=dict)
    tier_breakdown: Dict[str, TierBreakdown] = field(default_factory=dict)
    provider_model_breakdown: Dict[str, ProviderModelBreakdown] = field(default_factory=dict)

    context_efficiency: ContextEfficiency = field(default_factory=ContextEfficiency)
    cost: Optional[CostSummary] = None

    regressions: List[str] = field(default_factory=list)
    verdict: EfficiencyVerdict = field(default_factory=EfficiencyVerdict)

    def to_dict(self) -> Dict[str, Any]:
        return asdict(self)

    def to_json(self, indent: int = 2) -> str:
        return json.dumps(self.to_dict(), indent=indent, ensure_ascii=False)


def build_efficiency_report(
    reports: Sequence[CouncilUsageAuditReport],
    cost_profiles: Optional[Dict[Tuple[str, str], CostProfile]] = None,
) -> CouncilEfficiencyReport:
    reports = list(reports)
    result = CouncilEfficiencyReport(run_count=len(reports))
    if not reports:
        return result

    result.total_model_calls = sum(r.total_model_calls for r in reports)
    result.total_specialist_calls = sum(r.specialist_calls for r in reports)
    result.total_synthesis_calls = sum(r.synthesis_calls for r in reports)

    result.estimated_input = sum(r.estimated_input for r in reports)
    result.actual_input = sum(r.actual_input for r in reports)
    result.estimated_output = sum(r.estimated_output for r in reports)
    result.actual_output = sum(r.actual_output for r in reports)
    result.estimated_total = sum(r.estimated_total for r in reports)
    result.actual_total = sum(r.actual_total for r in reports)
    result.cached_input = sum(r.cached_input for r in reports)
    result.reasoning_tokens = sum(r.reasoning_tokens for r in reports)

    result.variance = _variance(result.estimated_total, result.actual_total)

    result.actual_total_per_run = _stats([r.actual_total for r in reports])
    specialist_totals = [c.actual_total for r in reports for c in r.calls if c.kind == "specialist"]
    synthesis_totals = [c.actual_total for r in reports for c in r.calls if c.kind == "synthesis"]
    result.actual_total_per_specialist = _stats(specialist_totals)
    result.actual_total_per_synthesis = _stats(synthesis_totals)
    result.wall_time_stats = _stats([r.wall_time_seconds for r in reports])

    agent_calls: Dict[str, List[Any]] = {}
    for r in reports:
        for c in r.calls:
            agent_calls.setdefault(c.agent_id, []).append(c)
    grand_total = result.actual_total or 1
    for agent_id, calls in agent_calls.items():
        estimated = sum(c.estimated_total for c in calls)
        actual = sum(c.actual_total for c in calls)
        stats = _stats([c.actual_total for c in calls])
        result.agent_breakdown[agent_id] = AgentBreakdown(
            calls=len(calls), estimated=estimated, actual=actual,
            avg=stats.avg, p95=stats.p95,
            share_of_total=round((actual / grand_total) * 100.0, 4),
        )

    tier_calls: Dict[str, List[Any]] = {}
    for r in reports:
        for c in r.calls:
            tier_calls.setdefault(c.model_tier, []).append(c)
    for tier, calls in tier_calls.items():
        actual_input = sum(c.actual_input or 0 for c in calls)
        actual_output = sum(c.actual_output or 0 for c in calls)
        actual_total = sum(c.actual_total for c in calls)
        n = len(calls) or 1
        result.tier_breakdown[tier] = TierBreakdown(
            calls=len(calls), actual_input=actual_input, actual_output=actual_output,
            actual_total=actual_total, avg_total=actual_total / n,
            tokens_per_call=actual_total / n,
            share_of_total=round((actual_total / grand_total) * 100.0, 4),
        )

    pm_calls: Dict[str, List[Any]] = {}
    for r in reports:
        for c in r.calls:
            key = f"{c.provider}/{c.model}/{c.model_tier}"
            pm_calls.setdefault(key, []).append(c)
    for key, calls in pm_calls.items():
        provider, model, tier = key.split("/", 2)
        actual_total = sum(c.actual_total for c in calls)
        estimated_total = sum(c.estimated_total for c in calls)
        result.provider_model_breakdown[key] = ProviderModelBreakdown(
            provider=provider, model=model, model_tier=tier,
            calls=len(calls), actual_total=actual_total, estimated_total=estimated_total,
            share_of_total=round((actual_total / grand_total) * 100.0, 4),
        )

    context_tokens_total = sum(r.context_estimated_tokens for r in reports)
    actual_input_total = result.actual_input
    actual_output_total = result.actual_output
    result.context_efficiency = ContextEfficiency(
        context_estimated_tokens_total=context_tokens_total,
        actual_input_total=actual_input_total,
        actual_output_total=actual_output_total,
        context_to_input_ratio=(context_tokens_total / actual_input_total) if actual_input_total else 0.0,
        output_to_input_ratio=(actual_output_total / actual_input_total) if actual_input_total else 0.0,
    )

    if cost_profiles:
        estimated_cost = 0.0
        actual_cost = 0.0
        for r in reports:
            for c in r.calls:
                profile = cost_profiles.get((c.provider, c.model))
                if profile is None:
                    continue
                actual_cost += compute_cost(c.actual_input or 0, c.cached_input, c.actual_output or 0, profile)
                estimated_cost += compute_cost(c.estimated_input, 0, c.estimated_output, profile)
        n_runs = len(reports) or 1
        n_specialist = result.total_specialist_calls or 1
        n_synthesis = result.total_synthesis_calls or 1
        result.cost = CostSummary(
            estimated_cost=estimated_cost, actual_cost=actual_cost,
            cost_delta=actual_cost - estimated_cost,
            cost_per_run=actual_cost / n_runs,
            cost_per_specialist=actual_cost * (result.total_specialist_calls / max(1, result.total_model_calls)) / n_specialist if result.total_model_calls else 0.0,
            cost_per_synthesis=actual_cost * (result.total_synthesis_calls / max(1, result.total_model_calls)) / n_synthesis if result.total_model_calls else 0.0,
        )

    specialist_share = (sum(specialist_totals) / grand_total * 100.0) if specialist_totals else 0.0
    synthesis_share = (sum(synthesis_totals) / grand_total * 100.0) if synthesis_totals else 0.0
    heavy_share = result.tier_breakdown.get("heavy", TierBreakdown()).share_of_total

    # Fix: the top optimization candidate must be a REAL (agent, tier) pair
    # actually observed together in the calls, not an independently-selected
    # top agent concatenated with an independently-selected top tier (which
    # can name a tier that agent never ran on, e.g. "CRITIC / light" when
    # CRITIC always runs on "standard"). We group by the exact (agent_id,
    # model_tier) pair that appears on calls and pick the pair with the
    # highest actual_total.
    agent_tier_pairs: Dict[Tuple[str, str], int] = {}
    for r in reports:
        for c in r.calls:
            key = (c.agent_id, c.model_tier)
            agent_tier_pairs[key] = agent_tier_pairs.get(key, 0) + c.actual_total

    top_candidate = None
    top_reason = None
    if agent_tier_pairs:
        (top_agent_id, top_agent_tier), _top_pair_total = max(
            agent_tier_pairs.items(), key=lambda kv: kv[1]
        )
        agent_share = result.agent_breakdown[top_agent_id].share_of_total
        tier_share = result.tier_breakdown[top_agent_tier].share_of_total
        top_candidate = f"{top_agent_id} / {top_agent_tier}"
        top_reason = (
            f"{round(agent_share, 1)}% of total tokens; "
            f"tier '{top_agent_tier}' (this agent's actual tier) accounts for "
            f"{round(tier_share, 1)}% of total tokens overall"
        )

    result.verdict = EfficiencyVerdict(
        runs_analyzed=result.run_count,
        avg_actual_tokens_per_run=result.actual_total_per_run.avg,
        median_actual_tokens_per_run=result.actual_total_per_run.median,
        p95_actual_tokens_per_run=result.actual_total_per_run.p95,
        estimated_vs_actual_percent=result.variance.delta_percent,
        specialist_share_percent=round(specialist_share, 4),
        synthesis_share_percent=round(synthesis_share, 4),
        heavy_tier_share_percent=heavy_share,
        context_to_input_ratio=result.context_efficiency.context_to_input_ratio,
        token_regression=False,
        top_optimization_candidate=top_candidate,
        top_optimization_reason=top_reason,
    )

    return result


def compare_baseline_vs_optimized(
    baseline_reports: Sequence[CouncilUsageAuditReport],
    optimized_reports: Sequence[CouncilUsageAuditReport],
) -> ABComparisonResult:
    baseline = build_efficiency_report(baseline_reports)
    optimized = build_efficiency_report(optimized_reports)

    baseline_avg = baseline.actual_total_per_run.avg
    optimized_avg = optimized.actual_total_per_run.avg
    tokens_saved = baseline_avg - optimized_avg
    tokens_saved_percent = round((tokens_saved / baseline_avg) * 100.0, 4) if baseline_avg else 0.0

    baseline_calls_avg = (baseline.total_model_calls / baseline.run_count) if baseline.run_count else 0.0
    optimized_calls_avg = (optimized.total_model_calls / optimized.run_count) if optimized.run_count else 0.0
    calls_saved = baseline_calls_avg - optimized_calls_avg

    latency_delta = optimized.wall_time_stats.avg - baseline.wall_time_stats.avg

    return ABComparisonResult(
        baseline_run_count=baseline.run_count,
        optimized_run_count=optimized.run_count,
        tokens_saved=tokens_saved,
        tokens_saved_percent=tokens_saved_percent,
        calls_saved=calls_saved,
        latency_delta_seconds=latency_delta,
    )


def detect_regressions(
    current: CouncilEfficiencyReport,
    baseline: CouncilEfficiencyReport,
    threshold_percent: float = 20.0,
) -> List[str]:
    flags: List[str] = []

    if baseline.actual_total_per_run.avg > 0:
        change = (current.actual_total_per_run.avg - baseline.actual_total_per_run.avg) / baseline.actual_total_per_run.avg * 100.0
        if change > threshold_percent:
            flags.append("TOKEN REGRESSION")

    baseline_ctx = baseline.context_efficiency.context_estimated_tokens_total / max(1, baseline.run_count)
    current_ctx = current.context_efficiency.context_estimated_tokens_total / max(1, current.run_count)
    if baseline_ctx > 0 and (current_ctx - baseline_ctx) / baseline_ctx * 100.0 > threshold_percent:
        flags.append("CONTEXT REGRESSION")

    baseline_calls = baseline.total_model_calls / max(1, baseline.run_count)
    current_calls = current.total_model_calls / max(1, current.run_count)
    if baseline_calls > 0 and (current_calls - baseline_calls) / baseline_calls * 100.0 > threshold_percent:
        flags.append("CALL COUNT REGRESSION")

    if baseline.verdict.synthesis_share_percent > 0:
        change = current.verdict.synthesis_share_percent - baseline.verdict.synthesis_share_percent
        if change > threshold_percent:
            flags.append("SYNTHESIS REGRESSION")

    if abs(current.variance.delta_percent) - abs(baseline.variance.delta_percent) > threshold_percent:
        flags.append("ESTIMATION DRIFT")

    current.regressions = flags
    current.verdict.token_regression = "TOKEN REGRESSION" in flags
    return flags


def to_csv_runs(reports: Sequence[CouncilUsageAuditReport]) -> str:
    buf = io.StringIO()
    writer = csv.writer(buf)
    writer.writerow(["run_id", "total_model_calls", "specialist_calls", "synthesis_calls",
                      "estimated_total", "actual_total", "variance_percent", "wall_time_seconds"])
    for r in reports:
        variance = _variance(r.estimated_total, r.actual_total)
        writer.writerow([r.run_id, r.total_model_calls, r.specialist_calls, r.synthesis_calls,
                          r.estimated_total, r.actual_total, variance.delta_percent, r.wall_time_seconds])
    return buf.getvalue()


def to_csv_agents(report: CouncilEfficiencyReport) -> str:
    buf = io.StringIO()
    writer = csv.writer(buf)
    writer.writerow(["agent_id", "calls", "estimated", "actual", "avg", "p95", "share_of_total_percent"])
    for agent_id, b in report.agent_breakdown.items():
        writer.writerow([agent_id, b.calls, b.estimated, b.actual, b.avg, b.p95, b.share_of_total])
    return buf.getvalue()


def to_csv_tiers(report: CouncilEfficiencyReport) -> str:
    buf = io.StringIO()
    writer = csv.writer(buf)
    writer.writerow(["model_tier", "calls", "actual_input", "actual_output", "actual_total",
                      "avg_total", "tokens_per_call", "share_of_total_percent"])
    for tier, b in report.tier_breakdown.items():
        writer.writerow([tier, b.calls, b.actual_input, b.actual_output, b.actual_total,
                          b.avg_total, b.tokens_per_call, b.share_of_total])
    return buf.getvalue()


def to_csv_models(report: CouncilEfficiencyReport) -> str:
    buf = io.StringIO()
    writer = csv.writer(buf)
    writer.writerow(["provider", "model", "model_tier", "calls", "actual_total",
                      "estimated_total", "share_of_total_percent"])
    for b in report.provider_model_breakdown.values():
        writer.writerow([b.provider, b.model, b.model_tier, b.calls, b.actual_total,
                          b.estimated_total, b.share_of_total])
    return buf.getvalue()


def to_markdown(report: CouncilEfficiencyReport) -> str:
    v = report.verdict
    lines: List[str] = []
    lines.append("# B5 Token Efficiency Report\n")
    lines.append(f"**Runs analyzed:** {report.run_count}\n")
    lines.append("## Estimated vs Actual\n")
    lines.append(f"- Estimated total: {report.variance.estimated}")
    lines.append(f"- Actual total: {report.variance.actual}")
    lines.append(f"- Delta: {report.variance.delta} ({report.variance.delta_percent}%)\n")
    lines.append("## Distribution (actual tokens per run)\n")
    lines.append(f"- avg: {report.actual_total_per_run.avg:.1f}")
    lines.append(f"- median: {report.actual_total_per_run.median:.1f}")
    lines.append(f"- p95: {report.actual_total_per_run.p95:.1f}")
    lines.append(f"- min/max: {report.actual_total_per_run.min:.1f} / {report.actual_total_per_run.max:.1f}\n")
    lines.append("## Agent breakdown\n")
    lines.append("| Agent | Calls | Estimated | Actual | Avg | P95 | Share % |")
    lines.append("|---|---|---|---|---|---|---|")
    for agent_id, b in report.agent_breakdown.items():
        lines.append(f"| {agent_id} | {b.calls} | {b.estimated} | {b.actual} | {b.avg:.1f} | {b.p95:.1f} | {b.share_of_total}% |")
    lines.append("\n## Tier breakdown\n")
    lines.append("| Tier | Calls | Actual Total | Avg/Call | Share % |")
    lines.append("|---|---|---|---|---|")
    for tier, b in report.tier_breakdown.items():
        lines.append(f"| {tier} | {b.calls} | {b.actual_total} | {b.tokens_per_call:.1f} | {b.share_of_total}% |")
    lines.append("\n## Context efficiency\n")
    ce = report.context_efficiency
    lines.append(f"- context_to_input_ratio: {ce.context_to_input_ratio:.3f}")
    lines.append(f"- output_to_input_ratio: {ce.output_to_input_ratio:.3f}\n")
    if report.regressions:
        lines.append("## ⚠ Regressions detected\n")
        for flag in report.regressions:
            lines.append(f"- ⚠ {flag}")
        lines.append("")
    else:
        lines.append("## Regressions\n\nNone detected.\n")
    lines.append("## Council efficiency verdict\n")
    lines.append(f"- Average actual tokens/run: {v.avg_actual_tokens_per_run:.1f}")
    lines.append(f"- Median: {v.median_actual_tokens_per_run:.1f}")
    lines.append(f"- P95: {v.p95_actual_tokens_per_run:.1f}")
    lines.append(f"- Estimated vs actual: {v.estimated_vs_actual_percent:+.1f}%")
    lines.append(f"- Specialist share: {v.specialist_share_percent:.1f}%")
    lines.append(f"- Synthesis share: {v.synthesis_share_percent:.1f}%")
    lines.append(f"- Heavy tier share: {v.heavy_tier_share_percent:.1f}%")
    lines.append(f"- Context/input ratio: {v.context_to_input_ratio:.3f}")
    lines.append(f"- Token regression: {'YES' if v.token_regression else 'NO'}")
    if v.top_optimization_candidate:
        lines.append(f"\n**Top optimization candidate:** {v.top_optimization_candidate}")
        lines.append(f"\n**Reason:** {v.top_optimization_reason}")
    return "\n".join(lines) + "\n"
