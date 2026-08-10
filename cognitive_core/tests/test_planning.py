import pytest
from cognitive_core.planning import Planner, ActivePlan

def test_planner_create_plan():
    planner = Planner()
    context = [{"id": "node1"}]
    
    plan = planner.create_plan("migrate memory", context)
    assert not plan.is_complete()
    
    step = plan.get_next_step()
    assert step["action"] == "search"
    assert step["query"] == "migrate memory"

def test_planner_evaluate_plan():
    planner = Planner()
    plan = ActivePlan("goal", [{"step": 1}])
    
    assert planner.evaluate_plan(plan, []) is True
    plan.complete_current_step()
    assert planner.evaluate_plan(plan, []) is False
