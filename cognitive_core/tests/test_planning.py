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

def test_active_plan_save_and_load(tmp_path):
    plan_file = str(tmp_path / "test_plan.json")
    steps = [
        {"step": 1, "action": "search", "query": "test query"},
        {"step": 2, "action": "verify", "query": "verify query"}
    ]
    plan = ActivePlan("test goal", steps)
    plan.complete_current_step()
    assert plan.current_step_index == 1
    assert plan.remaining_steps() == 1
    
    # Save state atomically
    plan.save_state(plan_file)
    assert (tmp_path / "test_plan.json").exists()
    
    # Load state
    loaded_plan = ActivePlan.load_state(plan_file)
    assert loaded_plan is not None
    assert loaded_plan.goal == "test goal"
    assert loaded_plan.current_step_index == 1
    assert loaded_plan.get_next_step()["action"] == "verify"
    assert loaded_plan.remaining_steps() == 1

def test_active_plan_load_missing(tmp_path):
    missing_file = str(tmp_path / "non_existent_plan.json")
    assert ActivePlan.load_state(missing_file) is None

