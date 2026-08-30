from cognitive_core.planning import Planner
from cognitive_core.plan_complexity_analyzer import PlanComplexityAnalyzer, ExecutionMode
from cognitive_core.council_budget_controller import CouncilBudgetController
from cognitive_core.orchestrator import MultiAgentOrchestrator


def _full_chain(query, context):
    planner = Planner()
    analyzer = PlanComplexityAnalyzer()
    cbc = CouncilBudgetController()
    orch = MultiAgentOrchestrator(None)
    plan = planner.create_plan(query, context)
    pc = analyzer.analyze(plan)
    decision = cbc.decide(query, complexity=pc.council_complexity, require_review=pc.require_review)
    report = None
    if decision.should_dispatch:
        report = orch.route_and_dispatch("P", query, context)
        report["council_tier"] = decision.tier.value
    return plan, pc, decision, report


# Invariant 1: simple task -> Council NEVER runs.
def test_invariant_simple_task_council_never_runs():
    for query in ["hello", "what time is it", "summarize nothing"]:
        plan, pc, decision, report = _full_chain(query, [])
        assert pc.execution_mode == ExecutionMode.SIMPLE
        assert decision.should_dispatch is False
        assert report is None


# Invariant 2: moderate task -> only the permitted Council path (LIGHT,
# retrieval skipped since it's already in working memory, verifier runs).
def test_invariant_moderate_task_only_light_path():
    context = [{"verification": "unverified"}]
    plan, pc, decision, report = _full_chain("hello", context)
    assert pc.execution_mode == ExecutionMode.MODERATE
    assert decision.tier.value == "light"
    assert decision.run_retrieval is False
    assert decision.run_verifier is True


# Invariant 3: destructive task -> HIGH_RISK, regardless of step_count.
def test_invariant_destructive_task_is_high_risk():
    plan, pc, decision, report = _full_chain("delete_canonical old_note", [])
    assert pc.destructive_steps == 1
    assert decision.tier.value == "high_risk"
    assert report["council_tier"] == "high_risk"


# Invariant 4: same plan -> same complexity classification (determinism).
def test_invariant_same_plan_same_classification():
    context = [{"verification": "unverified"}, {"relations": [{"x": 1}]}]
    results = set()
    for _ in range(5):
        plan, pc, decision, report = _full_chain("hello", context)
        results.add((pc.execution_mode.value, decision.tier.value))
    assert len(results) == 1, f"non-deterministic classification: {results}"


# Invariant 5: different query wording, same plan shape -> same complexity
# classification (classification depends on the PLAN's structure, not on
# incidental wording of the query text).
def test_invariant_wording_independent_given_same_plan_shape():
    context = [{"verification": "unverified"}]
    queries = ["update my project notes", "refresh the project notes please", "sync notes"]
    classifications = set()
    for q in queries:
        plan, pc, decision, report = _full_chain(q, context)
        classifications.add((pc.execution_mode.value, pc.step_count))
    assert len(classifications) == 1, f"wording changed plan-shape classification: {classifications}"


# Invariant 6: Planner replans -> new ActivePlan -> Analyzer runs AGAIN ->
# new CouncilTier. A task that becomes MORE complex/risky after a failure
# must not keep the OLD (stale) tier. This was CONFIRMED as a real gap in
# executive.py before the WIRE-C1.5b fix (single analyze()+decide() call in
# process_intent(), never re-run after planner.replan() inside step_loop()).
def test_invariant_replan_triggers_fresh_tier():
    planner = Planner()
    analyzer = PlanComplexityAnalyzer()
    cbc = CouncilBudgetController()

    plan = planner.create_plan("update my notes", [])
    pc_before = analyzer.analyze(plan)
    decision_before = cbc.decide("update my notes", complexity=pc_before.council_complexity, require_review=pc_before.require_review)
    assert decision_before.tier.value == "none"  # started SIMPLE, Council skipped

    new_plan = planner.replan("update my notes", [], {"action": "search"}, "simulated failure")
    pc_after = analyzer.analyze(new_plan)
    decision_after = cbc.decide("update my notes", complexity=pc_after.council_complexity, require_review=pc_after.require_review)

    assert pc_after.destructive_steps > 0
    assert decision_after.tier.value == "high_risk"
    assert decision_after.tier != decision_before.tier, (
        "STALE TIER BUG: replanned task must be re-classified, not silently "
        "keep the tier decided against the pre-failure plan."
    )
