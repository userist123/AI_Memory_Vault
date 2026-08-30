import pytest
from cognitive_core.planning import Planner
from cognitive_core.plan_complexity_analyzer import PlanComplexityAnalyzer, ExecutionMode


def test_single_step_plan_is_simple():
    planner = Planner()
    analyzer = PlanComplexityAnalyzer()
    plan = planner.create_plan("hello", [])
    pc = analyzer.analyze(plan)
    assert pc.step_count == 1
    assert pc.execution_mode == ExecutionMode.SIMPLE
    assert pc.council_complexity == 1
    assert pc.require_review is False


def test_two_step_plan_is_moderate():
    planner = Planner()
    analyzer = PlanComplexityAnalyzer()
    context = [{"verification": "unverified"}]
    plan = planner.create_plan("hello", context)
    pc = analyzer.analyze(plan)
    assert pc.step_count == 2
    assert pc.execution_mode == ExecutionMode.MODERATE
    assert pc.council_complexity == 2
    assert pc.verification_steps == 1
    assert pc.retrieval_steps == 1


def test_three_step_plan_is_complex():
    planner = Planner()
    analyzer = PlanComplexityAnalyzer()
    context = [{"verification": "unverified"}, {"relations": [{"x": 1}]}]
    plan = planner.create_plan("hello", context)
    pc = analyzer.analyze(plan)
    assert pc.step_count == 3
    assert pc.execution_mode == ExecutionMode.COMPLEX
    assert pc.council_complexity == 2


def test_destructive_plan_is_high_risk_regardless_of_step_count():
    planner = Planner()
    analyzer = PlanComplexityAnalyzer()
    plan = planner.create_plan("delete_canonical some_note", [])
    pc = analyzer.analyze(plan)
    assert pc.step_count == 1
    assert pc.destructive_steps == 1
    assert pc.execution_mode == ExecutionMode.HIGH_RISK
    assert pc.council_complexity == 2
    assert pc.require_review is True


def test_council_complexity_matches_old_threshold_semantics_exactly():
    # Backward-compat guarantee: this must match the OLD
    # Executive._estimate_complexity(plan=...) behavior exactly
    # (2 if len(plan.steps) >= 2 else 1), so swapping the call site in
    # process_intent cannot silently change existing dispatch outcomes.
    planner = Planner()
    analyzer = PlanComplexityAnalyzer()
    for context, expected_old_complexity in [
        ([], 1),
        ([{"verification": "unverified"}], 2),
        ([{"verification": "unverified"}, {"relations": [{"x": 1}]}], 2),
    ]:
        plan = planner.create_plan("hello", context)
        old_complexity = 2 if len(plan.steps) >= 2 else 1
        pc = analyzer.analyze(plan)
        assert pc.council_complexity == old_complexity == expected_old_complexity
