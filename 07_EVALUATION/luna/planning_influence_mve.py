"""Deterministic Planning Influence MVE harness.

Isolated mechanics experiment. It does not claim hidden-state change. It measures
whether an external-memory-derived soft prior changes planner node selection
under otherwise matched conditions.
"""
from __future__ import annotations

from dataclasses import dataclass
import math
from typing import Dict, List, Sequence, Tuple

BRANCHES: Tuple[str, ...] = ("strategy_a", "strategy_b", "strategy_c", "strategy_d")
MEMORY_RECOMMENDATIONS: Tuple[str, ...] = (
    "strategy_a", "strategy_c", "strategy_b", "strategy_d",
    "strategy_c", "strategy_a", "strategy_d", "strategy_b",
)

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
    priors: Dict[str, float]
    source_branch: str

@dataclass(frozen=True)
class PlannerTrace:
    selected_branches: Tuple[str, ...]
    node_visits: int
    fatal_visits: int
    success: bool

def build_scenarios(count: int = 30) -> List[Scenario]:
    scenarios: List[Scenario] = []
    for idx in range(count):
        # Outcome assignment and memory recommendation are generated independently.
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

def compile_memory(scenario: Scenario, applicability: str, memory_id: str) -> MemoryInfluenceState:
    # Critically, this compiler never reads scenario.optimal. The recommendation
    # is an independently frozen memory input, preventing oracle leakage.
    recommended = scenario.memory_recommended
    if applicability == "APPLICABLE":
        priors = {branch: (0.65 if branch == recommended else 0.35 / 3) for branch in scenario.branches}
        return MemoryInfluenceState(memory_id, applicability, priors, recommended)
    return MemoryInfluenceState(memory_id, applicability, {branch: 0.25 for branch in scenario.branches}, recommended)

def normalize(priors: Dict[str, float], branches: Sequence[str]) -> Dict[str, float]:
    values = {branch: max(0.0, float(priors.get(branch, 0.0))) for branch in branches}
    total = sum(values.values())
    if total <= 0:
        return {branch: 1.0 / len(branches) for branch in branches}
    return {branch: values[branch] / total for branch in branches}

def puct_score(q: float, visits: int, parent_visits: int, prior: float, exploration: float) -> float:
    return q + exploration * prior * math.sqrt(max(1, parent_visits)) / (1 + visits)

def run_planner(scenario: Scenario, priors: Dict[str, float], *, rollouts: int = 16, exploration: float = 1.414) -> PlannerTrace:
    branches = scenario.branches
    priors = normalize(priors, branches)
    visits = {branch: 0 for branch in branches}
    values = {branch: 0.0 for branch in branches}
    selected: List[str] = []
    for _ in range(rollouts):
        parent_visits = sum(visits.values())
        branch = max(branches, key=lambda candidate: (
            puct_score(values[candidate] / visits[candidate] if visits[candidate] else 0.0,
                       visits[candidate], parent_visits, priors[candidate], exploration),
            -branches.index(candidate)))
        selected.append(branch)
        visits[branch] += 1
        _, reward = scenario.oracle(branch)
        values[branch] += reward
        if branch == scenario.optimal:
            return PlannerTrace(tuple(selected), len(selected), sum(item in (scenario.fatal_a, scenario.fatal_b) for item in selected), True)
    return PlannerTrace(tuple(selected), len(selected), sum(item in (scenario.fatal_a, scenario.fatal_b) for item in selected), False)

def run_experiment(count: int = 30) -> Dict[str, object]:
    scenarios = build_scenarios(count)
    aggregate: Dict[str, Dict[str, float]] = {arm: {"success": 0, "nodes": 0, "fatal": 0} for arm in ("arm1_baseline", "arm2_advisory", "arm3_treatment", "arm4_stale")}
    traces: List[Dict[str, object]] = []
    for scenario in scenarios:
        uniform = {branch: 0.25 for branch in scenario.branches}
        memory = compile_memory(scenario, "APPLICABLE", f"memory-{scenario.scenario_id}")
        stale = compile_memory(scenario, "NOT_APPLICABLE", f"stale-{scenario.scenario_id}")
        arms = {"arm1_baseline": uniform, "arm2_advisory": uniform, "arm3_treatment": memory.priors, "arm4_stale": stale.priors}
        for arm, priors in arms.items():
            trace = run_planner(scenario, priors)
            aggregate[arm]["success"] += int(trace.success)
            aggregate[arm]["nodes"] += trace.node_visits
            aggregate[arm]["fatal"] += trace.fatal_visits
            traces.append({"scenario_id": scenario.scenario_id, "arm": arm,
                           "memory_id": memory.memory_id if arm == "arm3_treatment" else stale.memory_id if arm == "arm4_stale" else None,
                           "applicability": memory.applicability if arm == "arm3_treatment" else stale.applicability if arm == "arm4_stale" else "NONE",
                           "memory_recommended": scenario.memory_recommended,
                           "planner_prior": priors, "selected_branches": list(trace.selected_branches),
                           "node_visits": trace.node_visits, "fatal_visits": trace.fatal_visits, "success": trace.success,
                           "recommendation_matches_optimal": scenario.memory_recommended == scenario.optimal})
    return {"scenario_count": count, "aggregate": aggregate, "traces": traces}

def render_report(results: Dict[str, object]) -> str:
    aggregate = results["aggregate"]
    count = int(results["scenario_count"])
    lines = ["Planning Influence MVE V2 — deterministic mechanics pilot", "EVIDENCE_LEVEL=UNVERIFIED_UNTIL_CI_EXECUTION", f"scenario_count={count}", ""]
    for arm, metrics in aggregate.items():
        lines.append(f"{arm}: success={int(metrics['success'])}/{count} nodes={int(metrics['nodes'])} fatal={int(metrics['fatal'])}")
    control_nodes = max(1, int(aggregate["arm2_advisory"]["nodes"]))
    reduction = 1.0 - (int(aggregate["arm3_treatment"]["nodes"]) / control_nodes)
    lines.append(f"treatment_vs_advisory_node_reduction={reduction:.4f}")
    lines.append(f"memory_recommendation_matches_optimal_count={sum(int(t['recommendation_matches_optimal']) for t in results['traces'] if t['arm'] == 'arm3_treatment')}")
    lines.append("oracle_leakage_guard=compiler_does_not_read_scenario.optimal")
    return "\n".join(lines)

if __name__ == "__main__":
    print(render_report(run_experiment()))
