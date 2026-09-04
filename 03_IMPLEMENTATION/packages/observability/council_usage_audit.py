from __future__ import annotations

import json
from dataclasses import dataclass, field, asdict
from typing import Any, Dict, List, Optional

from .model_provider import ModelResponse


def _chars_to_tokens(text: str) -> int:
    return max(1, (len(text) + 2) // 3)


def _serialize(pack: Any) -> str:
    try:
        return json.dumps(pack, ensure_ascii=False, default=str)
    except (TypeError, ValueError):
        return str(pack)


@dataclass
class PerCallUsage:
    agent_id: str
    kind: str
    provider: str
    model: str
    model_tier: str
    estimated_input: int
    estimated_output: int
    actual_input: Optional[int]
    actual_output: Optional[int]
    cached_input: int
    reasoning_tokens: int
    actual_total: int
    estimated_total: int
    source: str


@dataclass
class CouncilUsageAuditReport:
    run_id: str

    specialist_calls: int = 0
    synthesis_calls: int = 0
    total_model_calls: int = 0

    estimated_input: int = 0
    actual_input: int = 0
    estimated_output: int = 0
    actual_output: int = 0

    cached_input: int = 0
    reasoning_tokens: int = 0
    actual_total: int = 0
    estimated_total: int = 0

    per_agent_usage: Dict[str, Dict[str, Any]] = field(default_factory=dict)
    per_tier_usage: Dict[str, Dict[str, Any]] = field(default_factory=dict)

    context_bytes: int = 0
    context_estimated_tokens: int = 0

    wall_time_seconds: float = 0.0

    tokens_per_specialist: float = 0.0
    tokens_per_council_run: float = 0.0
    tokens_per_synthesis: float = 0.0

    calls: List[PerCallUsage] = field(default_factory=list)

    def to_dict(self) -> Dict[str, Any]:
        return asdict(self)

    def to_json(self, indent: int = 2) -> str:
        return json.dumps(self.to_dict(), indent=indent, ensure_ascii=False)


def _usage_to_call(agent_id: str, kind: str, response: ModelResponse) -> PerCallUsage:
    usage = response.usage
    has_actual = usage.actual_input is not None and usage.actual_output is not None
    actual_total = usage.effective_total
    estimated_total = usage.estimated_input + usage.estimated_output

    return PerCallUsage(
        agent_id=agent_id,
        kind=kind,
        provider=response.provider,
        model=response.model,
        model_tier=response.model_tier,
        estimated_input=usage.estimated_input,
        estimated_output=usage.estimated_output,
        actual_input=usage.actual_input,
        actual_output=usage.actual_output,
        cached_input=int(usage.cached_input or 0),
        reasoning_tokens=int(usage.reasoning_tokens or 0),
        actual_total=actual_total,
        estimated_total=estimated_total,
        source="provider" if has_actual else "estimated_fallback",
    )


def build_audit_report(
    run_id: str,
    council_run_with_execution: Any,
    wall_time_seconds: float = 0.0,
) -> CouncilUsageAuditReport:
    """Build a full usage audit report from a CouncilRunWithExecution (A7/B1).

    Reads directly from specialist_results / synthesis_result (raw
    ModelResponse objects) rather than from ActualUsageTelemetry.events,
    because events store the RESOLVED value (actual if present, else
    estimated), which would make estimated_total == actual_total
    whenever a provider reports usage. This function needs both figures
    independently to answer the original project question: how much did
    every optimization actually save.
    """
    report = CouncilUsageAuditReport(run_id=run_id, wall_time_seconds=wall_time_seconds)

    calls: List[PerCallUsage] = []
    for agent_id, response in council_run_with_execution.specialist_results.items():
        calls.append(_usage_to_call(agent_id, "specialist", response))

    if council_run_with_execution.synthesis_result is not None:
        calls.append(_usage_to_call("SYNTHESIS", "synthesis", council_run_with_execution.synthesis_result))

    report.calls = calls
    report.specialist_calls = sum(1 for c in calls if c.kind == "specialist")
    report.synthesis_calls = sum(1 for c in calls if c.kind == "synthesis")
    report.total_model_calls = len(calls)

    report.estimated_input = sum(c.estimated_input for c in calls)
    report.estimated_output = sum(c.estimated_output for c in calls)
    report.actual_input = sum(c.actual_input or 0 for c in calls)
    report.actual_output = sum(c.actual_output or 0 for c in calls)
    report.cached_input = sum(c.cached_input for c in calls)
    report.reasoning_tokens = sum(c.reasoning_tokens for c in calls)
    report.actual_total = sum(c.actual_total for c in calls)
    report.estimated_total = sum(c.estimated_total for c in calls)

    per_agent: Dict[str, Dict[str, Any]] = {}
    for c in calls:
        per_agent.setdefault(c.agent_id, {"estimated_total": 0, "actual_total": 0, "calls": 0})
        per_agent[c.agent_id]["estimated_total"] += c.estimated_total
        per_agent[c.agent_id]["actual_total"] += c.actual_total
        per_agent[c.agent_id]["calls"] += 1
    report.per_agent_usage = per_agent

    per_tier: Dict[str, Dict[str, Any]] = {}
    for c in calls:
        per_tier.setdefault(c.model_tier, {"estimated_total": 0, "actual_total": 0, "calls": 0})
        per_tier[c.model_tier]["estimated_total"] += c.estimated_total
        per_tier[c.model_tier]["actual_total"] += c.actual_total
        per_tier[c.model_tier]["calls"] += 1
    report.per_tier_usage = per_tier

    agent_packs = getattr(council_run_with_execution, "agent_packs", {}) or {}
    context_bytes = 0
    context_tokens = 0
    for pack in agent_packs.values():
        serialized = _serialize(pack)
        context_bytes += len(serialized.encode("utf-8"))
        context_tokens += _chars_to_tokens(serialized)
    report.context_bytes = context_bytes
    report.context_estimated_tokens = context_tokens

    report.tokens_per_specialist = (
        sum(c.actual_total for c in calls if c.kind == "specialist") / report.specialist_calls
        if report.specialist_calls else 0.0
    )
    report.tokens_per_synthesis = (
        sum(c.actual_total for c in calls if c.kind == "synthesis") / report.synthesis_calls
        if report.synthesis_calls else 0.0
    )
    report.tokens_per_council_run = float(report.actual_total)

    return report
