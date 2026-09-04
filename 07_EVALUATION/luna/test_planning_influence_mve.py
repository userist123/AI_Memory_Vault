from planning_influence_mve import (
    APPLICABILITY_STRENGTH,
    build_scenarios,
    compile_memory,
    normalize,
    run_planner,
    run_experiment,
    summarize_treatment_by_memory_quality,
)


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
    assert memory.influence_strength == APPLICABILITY_STRENGTH["APPLICABLE"]
    assert abs(sum(normalize(memory.priors, scenario.branches).values()) - 1.0) < 1e-12


def test_applicable_with_verification_attenuates_influence():
    scenario = build_scenarios(1)[0]
    full = compile_memory(scenario, "APPLICABLE", "full")
    verification = compile_memory(scenario, "APPLICABLE_WITH_VERIFICATION", "verify")
    assert verification.influence_strength < full.influence_strength
    assert max(verification.priors.values()) < max(full.priors.values())
    assert all(value > 0 for value in verification.priors.values())


def test_insufficiently_known_attenuates_more_than_verification():
    scenario = build_scenarios(1)[0]
    verification = compile_memory(scenario, "APPLICABLE_WITH_VERIFICATION", "verify")
    uncertain = compile_memory(scenario, "INSUFFICIENTLY_KNOWN", "unknown")
    assert uncertain.influence_strength < verification.influence_strength
    assert max(uncertain.priors.values()) < max(verification.priors.values())


def test_non_applicable_memory_neutralizes_to_uniform():
    scenario = build_scenarios(1)[0]
    stale = compile_memory(scenario, "NOT_APPLICABLE", "stale")
    assert stale.applicability == "NOT_APPLICABLE"
    assert stale.influence_strength == 0.0
    assert stale.priors == {branch: 0.25 for branch in scenario.branches}


def test_memory_recommendation_is_independent_of_oracle_outcome():
    scenarios = build_scenarios(30)
    assert any(s.memory_recommended != s.optimal for s in scenarios)
    assert any(s.memory_recommended == s.optimal for s in scenarios)
    for scenario in scenarios:
        memory = compile_memory(scenario, "APPLICABLE", f"m-{scenario.scenario_id}")
        assert memory.source_branch == scenario.memory_recommended
        assert memory.source_branch in scenario.branches


def test_treatment_changes_search_behavior_against_advisory_control():
    scenario = next(s for s in build_scenarios(30) if s.memory_recommended != s.optimal)
    uniform = {branch: 0.25 for branch in scenario.branches}
    treatment = compile_memory(scenario, "APPLICABLE", "m1").priors
    control_trace = run_planner(scenario, uniform)
    treatment_trace = run_planner(scenario, treatment)
    assert control_trace.selected_branches != treatment_trace.selected_branches


def test_memory_quality_summary_separates_match_and_mismatch():
    result = run_experiment(30)
    quality = summarize_treatment_by_memory_quality(result)
    assert quality["match"]["count"] > 0
    assert quality["mismatch"]["count"] > 0
    assert quality["match"]["count"] + quality["mismatch"]["count"] == 30
    assert quality["mismatch"]["fatal"] >= 0


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
