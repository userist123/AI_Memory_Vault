"""Deterministic Planning Influence MVE harness.

Isolated mechanics experiment. It does not claim hidden-state change. It measures
whether an external-memory-derived soft prior changes planner node selection
under otherwise matched conditions, and now models bounded verification plus a
terminal task result explicitly.
"""
from __future__ import annotations

from dataclasses import dataclass
from enum import Enum
import math
from typing import Dict, List, Sequence, Tuple

BRANCHES: Tuple[str, ...] = ("strategy_a", "strategy_b", "strategy_c", "strategy_d")
MEMORY_RECOMMENDATIONS: Tuple[str, ...] = (
    "strategy_a", "strategy_c", "strategy_b", "strategy_d",
    "strategy_c", "strategy_a", "strategy_d", "strategy_b",
)

# Frozen independently of task outcomes. These are experiment inputs, not oracle-derived labels.
MEMORY_APPLICABILITY: Tuple[str, ...] = (
    "APPLICABLE", "APPLICABLE_WITH_VERIFICATION", "INSUFFICIENTLY_KNOWN", "NOT_APPLICABLE",
    "APPLICABLE_WITH_VERIFICATION", "APPLICABLE", "INSUFFICIENTLY_KNOWN", "NOT_APPLICABLE",
)
MEMORY_EVIDENCE_STRENGTH: Tuple[float, ...] = (0.90, 0.60, 0.25, 0.00, 0.75, 0.50, 0.20, 0.00)
MEMORY_CONTRADICTION_STATE: Tuple[str, ...] = (
    "NONE", "NONE", "POSSIBLE_CONTRADICTION", "CONFIRMED_CONTRADICTION",
    "NONE", "POSSIBLE_CONTRADICTION", "NONE", "CONFIRMED_CONTRADICTION",
)

APPLICABILITY_STRENGTH: Dict[str, float] = {
    "APPLICABLE": 1.0,
    "APPLICABLE_WITH_VERIFICATION": 0.35,
    "INSUFFICIENTLY_KNOWN": 0.15,
    "NOT_APPLICABLE": 0.0,
}
VALID_CONTRADICTION_STATES = {
    "NONE", "POSSIBLE_CONTRADICTION", "CONFIRMED_CONTRADICTION",
}


class TerminalStatus(str, Enum):
    """Terminal outcomes for the current task resolution."""

    RESOLVED = "RESOLVED"
    ABSTAINED = "ABSTAINED"
    HUMAN_CONFIRMATION_REQUIRED = "HUMAN_CONFIRMATION_REQUIRED"


class ResolutionStage(str, Enum):
    """Observable semantic stages; verification is the only repeatable sub-loop."""

    TASK = "TASK"
    EXPERIENCE = "EXPERIENCE"
    PATTERN = "MODEL_PATTERN"
    APPLICABILITY = "APPLICABILITY"
    INFLUENCE = "INFLUENCE"
    DECISION_CANDIDATE = "DECISION_CANDIDATE"
    VERIFYING = "VERIFYING"
    REORGANIZING = "REORGANIZING"
    TERMINAL = "TERMINAL"
    FINAL_RESPONSE = "FINAL_RESPONSE"


@dataclass(frozen=True)
class Scenario:
    scenario_id: str
    branches_order: Tuple[str, ...]
    optimal: str
    suboptimal: str
    fatal_a: str
    fatal_b: str
    memory_recommended: str

    @property
    def branches(self) -> Tuple[str, ...]:
        return self.branches_order

    def oracle(self, branch: str) -> Tuple[bool, float]:
        if branch == self.optimal:
            return True, 1.0
        if branch == self.suboptimal:
            return True, 0.25
        return False, -1.0


@dataclass(frozen=True)
class MemoryInfluenceState:
    memory_id: str
    applicability: str
    evidence_strength: float
    contradiction_state: str
    priors: Dict[str, float]
    source_branch: str
    influence_strength: float
    verification_required: bool
    verification_cost: float


@dataclass(frozen=True)
class PlannerTrace:
    selected_branches: Tuple[str, ...]
    node_visits: int
    fatal_visits: int
    success: bool


@dataclass(frozen=True)
class VerificationTrace:
    attempted_branches: Tuple[str, ...]
    successful_branch: str | None
    steps: int
    exhausted: bool
    contradiction_detected: bool
    cost: float


@dataclass(frozen=True)
class ReorganizationTrace:
    applied: bool
    reason: str
    verified_branch: str | None


@dataclass(frozen=True)
class TerminalResolution:
    status: TerminalStatus
    decision: str | None
    evidence_refs: Tuple[str, ...]
    verification: VerificationTrace
    reorganization: ReorganizationTrace
    stages: Tuple[ResolutionStage, ...]

    @property
    def terminal(self) -> bool:
        return self.status in {
            TerminalStatus.RESOLVED,
            TerminalStatus.ABSTAINED,
            TerminalStatus.HUMAN_CONFIRMATION_REQUIRED,
        }


def build_scenarios(count: int = 30) -> List[Scenario]:
    scenarios: List[Scenario] = []
    for idx in range(count):
        shift = (idx * 3 + 1) % len(BRANCHES)
        order = tuple(BRANCHES[(shift + offset) % len(BRANCHES)] for offset in range(4))
        memory_recommended = MEMORY_RECOMMENDATIONS[idx % len(MEMORY_RECOMMENDATIONS)]
        scenarios.append(Scenario(
            scenario_id=f"S{idx + 1:02d}",
            branches_order=order,
            optimal=order[0],
            suboptimal=order[1],
            fatal_a=order[2],
            fatal_b=order[3],
            memory_recommended=memory_recommended,
        ))
    return scenarios


def matched_context(memory_lesson: str) -> str:
    return f"MEMORY_LESSON:{memory_lesson[:64]}"


def _verification_cost(applicability: str, evidence_strength: float) -> float:
    if applicability != "APPLICABLE_WITH_VERIFICATION":
        return 0.0
    return round(1.0 + (1.0 - evidence_strength), 6)


def compile_memory(
    scenario: Scenario,
    applicability: str,
    memory_id: str,
    *,
    evidence_strength: float = 1.0,
    contradiction_state: str = "NONE",
) -> MemoryInfluenceState:
    if applicability not in APPLICABILITY_STRENGTH:
        raise ValueError(f"Unknown applicability state: {applicability}")
    if contradiction_state not in VALID_CONTRADICTION_STATES:
        raise ValueError(f"Unknown contradiction state: {contradiction_state}")
    if not 0.0 <= float(evidence_strength) <= 1.0:
        raise ValueError("evidence_strength must be within [0.0, 1.0]")

    # The compiler never reads scenario.optimal. Recommendation/evidence/applicability are
    # independent experiment inputs, preventing oracle leakage.
    recommended = scenario.memory_recommended
    evidence_strength = float(evidence_strength)
    applicability_strength = APPLICABILITY_STRENGTH[applicability]
    verification_required = applicability == "APPLICABLE_WITH_VERIFICATION"
    verification_cost = _verification_cost(applicability, evidence_strength)

    # Confirmed contradiction or explicit non-applicability is a safety veto.
    if contradiction_state == "CONFIRMED_CONTRADICTION" or applicability == "NOT_APPLICABLE":
        priors = {branch: 0.25 for branch in scenario.branches}
        influence_strength = 0.0
    else:
        influence_strength = applicability_strength * evidence_strength
        winner_prior = 0.25 + (0.40 * influence_strength)
        loser_mass = 1.0 - winner_prior
        priors = {
            branch: (winner_prior if branch == recommended else loser_mass / 3)
            for branch in scenario.branches
        }

    return MemoryInfluenceState(
        memory_id=memory_id,
        applicability=applicability,
        evidence_strength=evidence_strength,
        contradiction_state=contradiction_state,
        priors=priors,
        source_branch=recommended,
        influence_strength=influence_strength,
        verification_required=verification_required,
        verification_cost=verification_cost,
    )


def normalize(priors: Dict[str, float], branches: Sequence[str]) -> Dict[str, float]:
    values = {branch: max(0.0, float(priors.get(branch, 0.0))) for branch in branches}
    total = sum(values.values())
    if total <= 0:
        return {branch: 1.0 / len(branches) for branch in branches}
    return {branch: values[branch] / total for branch in branches}


def puct_score(q: float, visits: int, parent_visits: int, prior: float, exploration: float) -> float:
    return q + exploration * prior * math.sqrt(max(1, parent_visits)) / (1 + visits)


def run_planner(
    scenario: Scenario,
    priors: Dict[str, float],
    *,
    rollouts: int = 16,
    exploration: float = 1.414,
) -> PlannerTrace:
    branches = scenario.branches
    priors = normalize(priors, branches)
    visits = {branch: 0 for branch in branches}
    values = {branch: 0.0 for branch in branches}
    selected: List[str] = []
    for _ in range(rollouts):
        parent_visits = sum(visits.values())
        branch = max(branches, key=lambda candidate: (
            puct_score(
                values[candidate] / visits[candidate] if visits[candidate] else 0.0,
                visits[candidate], parent_visits, priors[candidate], exploration,
            ),
            -branches.index(candidate),
        ))
        selected.append(branch)
        visits[branch] += 1
        _, reward = scenario.oracle(branch)
        values[branch] += reward
        if branch == scenario.optimal:
            return PlannerTrace(
                tuple(selected), len(selected),
                sum(item in (scenario.fatal_a, scenario.fatal_b) for item in selected),
                True,
            )
    return PlannerTrace(
        tuple(selected), len(selected),
        sum(item in (scenario.fatal_a, scenario.fatal_b) for item in selected),
        False,
    )


def verify_candidate_sequence(
    scenario: Scenario,
    candidates: Sequence[str],
    *,
    verification_budget: int = 3,
    verification_cost_per_step: float = 1.0,
) -> VerificationTrace:
    """Verify planner candidates without changing their order or querying memory again.

    The scenario oracle is an explicit verification instrument for this isolated harness,
    not a source for memory compilation or planner-prior generation.
    """
    if verification_budget < 1:
        raise ValueError("verification_budget must be >= 1")

    attempted: List[str] = []
    contradiction_detected = False
    for branch in candidates[:verification_budget]:
        attempted.append(branch)
        valid, _ = scenario.oracle(branch)
        if valid and branch == scenario.optimal:
            return VerificationTrace(
                tuple(attempted), branch, len(attempted), False,
                contradiction_detected, round(len(attempted) * verification_cost_per_step, 6),
            )
        if branch in (scenario.fatal_a, scenario.fatal_b):
            contradiction_detected = True

    return VerificationTrace(
        tuple(attempted), None, len(attempted), True,
        contradiction_detected, round(len(attempted) * verification_cost_per_step, 6),
    )


def resolve_task(
    scenario: Scenario,
    planner_trace: PlannerTrace,
    memory: MemoryInfluenceState,
    *,
    verification_budget: int = 3,
    human_confirmation_required: bool = False,
) -> TerminalResolution:
    """Resolve one task to an explicit terminal result.

    Semantic pipeline:
        TASK -> EXPERIENCE -> PATTERN -> APPLICABILITY -> INFLUENCE ->
        DECISION_CANDIDATE -> bounded VERIFYING sub-loop -> REORGANIZING ->
        TERMINAL -> FINAL_RESPONSE.

    Reorganization records future-memory work only. It never re-enters the current task.
    """
    stages = [
        ResolutionStage.TASK,
        ResolutionStage.EXPERIENCE,
        ResolutionStage.PATTERN,
        ResolutionStage.APPLICABILITY,
        ResolutionStage.INFLUENCE,
        ResolutionStage.DECISION_CANDIDATE,
    ]

    if human_confirmation_required:
        verification = VerificationTrace((), None, 0, False, False, 0.0)
        reorganization = ReorganizationTrace(False, "human_confirmation_required", None)
        stages.extend([ResolutionStage.TERMINAL, ResolutionStage.FINAL_RESPONSE])
        return TerminalResolution(
            status=TerminalStatus.HUMAN_CONFIRMATION_REQUIRED,
            decision=None,
            evidence_refs=(f"verification:{scenario.scenario_id}:human_confirmation",),
            verification=verification,
            reorganization=reorganization,
            stages=tuple(stages),
        )

    stages.append(ResolutionStage.VERIFYING)
    verification = verify_candidate_sequence(
        scenario,
        planner_trace.selected_branches,
        verification_budget=verification_budget,
    )

    if verification.successful_branch is not None:
        status = TerminalStatus.RESOLVED
        decision = verification.successful_branch
        reorganization = ReorganizationTrace(
            True,
            "verified_outcome_available",
            verification.successful_branch,
        )
    else:
        status = TerminalStatus.ABSTAINED
        decision = None
        reason = "verification_budget_exhausted"
        if memory.contradiction_state == "CONFIRMED_CONTRADICTION":
            reason = "confirmed_memory_contradiction"
        reorganization = ReorganizationTrace(False, reason, None)

    # Reorganization happens after verification and cannot return control to the current task.
    stages.extend([ResolutionStage.REORGANIZING, ResolutionStage.TERMINAL, ResolutionStage.FINAL_RESPONSE])
    evidence_refs = (
        f"scenario:{scenario.scenario_id}",
        f"verified_steps:{verification.steps}",
    )
    if verification.successful_branch is not None:
        evidence_refs = evidence_refs + (f"verified_branch:{verification.successful_branch}",)

    return TerminalResolution(
        status=status,
        decision=decision,
        evidence_refs=evidence_refs,
        verification=verification,
        reorganization=reorganization,
        stages=tuple(stages),
    )


def run_experiment(count: int = 30) -> Dict[str, object]:
    scenarios = build_scenarios(count)
    aggregate: Dict[str, Dict[str, float]] = {
        arm: {"success": 0, "nodes": 0, "fatal": 0, "verification": 0}
        for arm in ("arm1_baseline", "arm2_advisory", "arm3_treatment", "arm4_stale")
    }
    traces: List[Dict[str, object]] = []
    for idx, scenario in enumerate(scenarios):
        uniform = {branch: 0.25 for branch in scenario.branches}
        applicability = MEMORY_APPLICABILITY[idx % len(MEMORY_APPLICABILITY)]
        evidence_strength = MEMORY_EVIDENCE_STRENGTH[idx % len(MEMORY_EVIDENCE_STRENGTH)]
        contradiction_state = MEMORY_CONTRADICTION_STATE[idx % len(MEMORY_CONTRADICTION_STATE)]
        memory = compile_memory(
            scenario,
            applicability,
            f"memory-{scenario.scenario_id}",
            evidence_strength=evidence_strength,
            contradiction_state=contradiction_state,
        )
        stale = compile_memory(
            scenario,
            "NOT_APPLICABLE",
            f"stale-{scenario.scenario_id}",
            evidence_strength=0.0,
        )
        arms = {
            "arm1_baseline": uniform,
            "arm2_advisory": uniform,
            "arm3_treatment": memory.priors,
            "arm4_stale": stale.priors,
        }
        for arm, priors in arms.items():
            trace = run_planner(scenario, priors)
            resolution = resolve_task(
                scenario,
                trace,
                memory if arm == "arm3_treatment" else stale if arm == "arm4_stale" else compile_memory(
                    scenario, "NOT_APPLICABLE", f"control-{scenario.scenario_id}", evidence_strength=0.0
                ),
                verification_budget=3,
            )
            aggregate[arm]["success"] += int(trace.success)
            aggregate[arm]["nodes"] += trace.node_visits
            aggregate[arm]["fatal"] += trace.fatal_visits
            if arm == "arm3_treatment":
                aggregate[arm]["verification"] += int(memory.verification_required)
            traces.append({
                "scenario_id": scenario.scenario_id,
                "arm": arm,
                "memory_id": memory.memory_id if arm == "arm3_treatment" else stale.memory_id if arm == "arm4_stale" else None,
                "applicability": memory.applicability if arm == "arm3_treatment" else stale.applicability if arm == "arm4_stale" else "NONE",
                "evidence_strength": memory.evidence_strength if arm == "arm3_treatment" else stale.evidence_strength if arm == "arm4_stale" else None,
                "contradiction_state": memory.contradiction_state if arm == "arm3_treatment" else stale.contradiction_state if arm == "arm4_stale" else "NONE",
                "influence_strength": memory.influence_strength if arm == "arm3_treatment" else stale.influence_strength if arm == "arm4_stale" else 0.0,
                "verification_required": memory.verification_required if arm == "arm3_treatment" else False,
                "verification_cost": memory.verification_cost if arm == "arm3_treatment" else 0.0,
                "memory_recommended": scenario.memory_recommended,
                "planner_prior": priors,
                "selected_branches": list(trace.selected_branches),
                "node_visits": trace.node_visits,
                "fatal_visits": trace.fatal_visits,
                "success": trace.success,
                "recommendation_matches_optimal": scenario.memory_recommended == scenario.optimal,
                "terminal_status": resolution.status.value,
                "terminal_decision": resolution.decision,
                "verification_steps": resolution.verification.steps,
                "verification_cost": resolution.verification.cost,
                "verification_exhausted": resolution.verification.exhausted,
                "verification_contradiction_detected": resolution.verification.contradiction_detected,
                "reorganization_applied": resolution.reorganization.applied,
                "reorganization_reason": resolution.reorganization.reason,
                "final_response_terminal": resolution.stages[-1] == ResolutionStage.FINAL_RESPONSE,
                "stages": [stage.value for stage in resolution.stages],
            })
    return {"scenario_count": count, "aggregate": aggregate, "traces": traces}


def summarize_treatment_by_memory_quality(results: Dict[str, object]) -> Dict[str, Dict[str, int]]:
    summary = {
        "match": {"count": 0, "success": 0, "nodes": 0, "fatal": 0},
        "mismatch": {"count": 0, "success": 0, "nodes": 0, "fatal": 0},
    }
    for trace in results["traces"]:
        if trace["arm"] != "arm3_treatment":
            continue
        group = "match" if trace["recommendation_matches_optimal"] else "mismatch"
        summary[group]["count"] += 1
        summary[group]["success"] += int(trace["success"])
        summary[group]["nodes"] += int(trace["node_visits"])
        summary[group]["fatal"] += int(trace["fatal_visits"])
    return summary


def render_report(results: Dict[str, object]) -> str:
    aggregate = results["aggregate"]
    count = int(results["scenario_count"])
    quality = summarize_treatment_by_memory_quality(results)
    lines = [
        "Planning Influence MVE V2 — deterministic mechanics pilot",
        "EVIDENCE_LEVEL=UNVERIFIED_UNTIL_CI_EXECUTION",
        f"scenario_count={count}",
        "",
    ]
    for arm, metrics in aggregate.items():
        lines.append(
            f"{arm}: success={int(metrics['success'])}/{count} nodes={int(metrics['nodes'])} "
            f"fatal={int(metrics['fatal'])} verification={int(metrics['verification'])}"
        )
    control_nodes = max(1, int(aggregate["arm2_advisory"]["nodes"]))
    treatment_nodes = int(aggregate["arm3_treatment"]["nodes"])
    reduction = 1.0 - (treatment_nodes / control_nodes)
    lines.append(f"treatment_vs_advisory_node_reduction={reduction:.4f}")
    lines.append(f"memory_recommendation_matches_optimal_count={quality['match']['count']}")
    lines.append(f"memory_recommendation_mismatches_optimal_count={quality['mismatch']['count']}")
    lines.append(f"treatment_match_nodes={quality['match']['nodes']} fatal={quality['match']['fatal']}")
    lines.append(f"treatment_mismatch_nodes={quality['mismatch']['nodes']} fatal={quality['mismatch']['fatal']}")
    terminal_counts = {
        status.value: sum(
            1 for trace in results["traces"]
            if trace["arm"] == "arm3_treatment" and trace["terminal_status"] == status.value
        )
        for status in TerminalStatus
    }
    lines.append(f"treatment_terminal_status_counts={terminal_counts}")
    lines.append("bounded_verification_budget=3")
    lines.append("oracle_leakage_guard=compiler_does_not_read_scenario.optimal")
    lines.append("terminality_guard=final_response_is_last_stage_and_never_reenters_current_task")
    return "\n".join(lines)


if __name__ == "__main__":
    print(render_report(run_experiment()))
