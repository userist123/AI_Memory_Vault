from planning_influence_mve import build_scenarios, compile_memory, normalize, run_planner, run_experiment


def test_scenarios_have_four_distinct_branches():
    for scenario in build_scenarios(30):
        assert len(scenario.branches) == 4
        assert len(set(scenario.branches)) == 4


def test_treatment_prior_is_soft_and_keeps_all_actions():
    scenario = build_scenarios(1)[0]
    memory = compile_memory(scenario, "APPLICABLE", "m1")
    assert set(memory.priors) == set(scenario.branches)
    assert max(memory.priors.values()) == 0.65
    assert all(value > 0 for value in memory.priors.values())
    assert abs(sum(normalize(memory.priors, scenario.branches).values()) - 1.0) < 1e-12


def test_non_applicable_memory_neutralizes_to_uniform():
    scenario = build_scenarios(1)[0]
    stale = compile_memory(scenario, "NOT_APPLICABLE", "stale")
    assert stale.applicability == "NOT_APPLICABLE"
    assert stale.priors == {branch: 0.25 for branch in scenario.branches}


def test_treatment_changes_search_behavior_against_advisory_control():
    scenario = build_scenarios(1)[0]
    uniform = {branch: 0.25 for branch in scenario.branches}
    treatment = compile_memory(scenario, "APPLICABLE", "m1").priors
    control_trace = run_planner(scenario, uniform)
    treatment_trace = run_planner(scenario, treatment)
    assert control_trace.selected_branches != treatment_trace.selected_branches
    assert treatment_trace.success is True


def test_experiment_has_thirty_scenarios_and_four_arms():
    result = run_experiment(30)
    assert result["scenario_count"] == 30
    assert set(result["aggregate"]) == {
        "arm1_baseline",
        "arm2_advisory",
        "arm3_treatment",
        "arm4_stale",
    }
    assert len(result["traces"]) == 120
