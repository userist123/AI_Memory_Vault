from cognitive_core.planning import Planner, ActivePlan
from cognitive_core.plan_complexity_analyzer import PlanComplexityAnalyzer, ExecutionMode
from cognitive_core.council_budget_controller import CouncilBudgetController
from cognitive_core.orchestrator import MultiAgentOrchestrator
from cognitive_core.executive import Executive
from memory_controller.authorizer import Principal


def _full_chain(query, context):
    planner = Planner()
    analyzer = PlanComplexityAnalyzer()
    cbc = CouncilBudgetController()
    orch = MultiAgentOrchestrator(None)
    plan = planner.create_plan(query, context)
    pc = analyzer.analyze(plan)
    decision = cbc.decide(
        query,
        complexity=pc.council_complexity,
        require_review=pc.require_review,
    )
    report = None
    if decision.should_dispatch:
        report = orch.route_and_dispatch(
            "P", query, context,
            skip_retrieval=not decision.run_retrieval,
            run_verifier=decision.run_verifier,
            max_context_items=5,
        )
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
# retrieval skipped since context already exists, verifier runs).
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


# Invariant 5: classification depends on plan structure, not wording.
def test_invariant_wording_independent_given_same_plan_shape():
    context = [{"verification": "unverified"}]
    queries = ["update my project notes", "refresh the project notes please", "sync notes"]
    classifications = set()
    for q in queries:
        plan, pc, decision, report = _full_chain(q, context)
        classifications.add((pc.execution_mode.value, pc.step_count))
    assert len(classifications) == 1, f"wording changed plan-shape classification: {classifications}"


# Invariant 6: replan must be re-analyzed from the NEW plan. The production
# Planner.replan() is intentionally generic and currently produces a one-step
# alternative search plan; this test therefore supplies a legitimate
# high-risk replanned ActivePlan to model a recovery path that escalates risk.
def test_invariant_replan_triggers_fresh_tier():
    planner = Planner()
    analyzer = PlanComplexityAnalyzer()
    cbc = CouncilBudgetController()

    plan = planner.create_plan("update my notes", [])
    pc_before = analyzer.analyze(plan)
    decision_before = cbc.decide(
        "update my notes",
        complexity=pc_before.council_complexity,
        require_review=pc_before.require_review,
    )
    assert decision_before.tier.value == "none"

    # A recovery planner is allowed to replace the failed plan with a
    # genuinely higher-risk plan. We model that NEW plan explicitly rather
    # than falsely claiming Planner.replan() currently creates delete steps.
    new_plan = ActivePlan(
        "update my notes",
        [{
            "step": 1,
            "action": "delete_canonical",
            "query": "update my notes",
            "description": "Escalated recovery requiring review",
        }],
    )
    pc_after = analyzer.analyze(new_plan)
    decision_after = cbc.decide(
        "update my notes",
        complexity=pc_after.council_complexity,
        require_review=pc_after.require_review,
    )

    assert pc_after.destructive_steps == 1
    assert pc_after.execution_mode == ExecutionMode.HIGH_RISK
    assert decision_after.tier.value == "high_risk"
    assert decision_after.tier != decision_before.tier


def test_executive_replan_rederives_and_dispatches_new_high_risk_tier():
    """Production integration proof for C1.5b.

    The real Executive exception path must analyze the replacement ActivePlan
    again. We inject only the failure/replan boundary; all classification and
    Council-tier logic remains the real implementation.
    """

    class FailingRouter:
        def execute(self, principal, action, kwargs):
            raise RuntimeError("simulated execution failure")

    class RecordingOrchestrator:
        def __init__(self):
            self.calls = []

        def route_and_dispatch(self, principal, query, context, **kwargs):
            self.calls.append({
                "principal": principal,
                "query": query,
                "context": context,
                **kwargs,
            })
            return {
                "orchestration_history": [],
                "total_context_used": len(context),
                "status": "completed",
            }

    controller = type("ControllerStub", (), {})()
    executive = Executive(controller)
    executive.router = FailingRouter()
    executive.orchestrator = RecordingOrchestrator()

    class PlannerStub:
        def evaluate_plan(self, plan, context):
            return plan is not None and not plan.is_complete()

        def replan(self, goal, context, failed_action, error):
            return ActivePlan(
                goal,
                [{
                    "step": 1,
                    "action": "delete_canonical",
                    "query": goal,
                    "description": "Escalated recovery requiring review",
                }],
            )

    executive.planner = PlannerStub()
    executive.active_plan = ActivePlan(
        "update my notes",
        [{
            "step": 1,
            "action": "search",
            "query": "update my notes",
            "description": "Initial read-only attempt",
        }],
    )
    executive._retry_count = 0
    executive._max_retries = 1

    result = executive.step_loop(Principal.AI_AGENT)

    assert result["replanned"] is True
    assert "post_replan_dispatch_report" in result
    post_report = result["post_replan_dispatch_report"]
    assert post_report["council_tier"] == "high_risk"
    assert post_report["plan_complexity"]["execution_mode"] == "high_risk"
    assert post_report["plan_complexity"]["destructive_steps"] == 1
    assert len(executive.orchestrator.calls) == 1
    assert executive.orchestrator.calls[0]["require_review"] is True
