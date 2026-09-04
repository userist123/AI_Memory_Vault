from planning_influence_mve import (
    APPLICABILITY_STRENGTH,
    MEMORY_APPLICABILITY,
    MEMORY_CONTRADICTION_STATE,
    MEMORY_EVIDENCE_STRENGTH,
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
    assert memory.verification_required is False
    assert memory.verification_cost == 0.0
    assert abs(sum(normalize(memory.priors, scenario.branches).values()) - 1.0) < 1e-12


def test_applicable_with_verification_emits_explicit_verification_route():
    scenario = build_scenarios(1)[0]
    memory = compile_memory(
        scenario,
        "APPLICABLE_WITH_VERIFICATION",
        "verify",
        evidence_strength=0.60,
    )
    assert memory.verification_required is True
    assert memory.verification_cost == 1.4
    assert 0.0 < memory.influence_strength < 1.0
    assert all(value > 0 for value in memory.priors.values())


def test_insufficiently_known_attenuates_more_than_verification():
    scenario = build_scenarios(1)[0]
    verification = compile_memory(
        scenario,
        "APPLICABLE_WITH_VERIFICATION",
        "verify",
        evidence_strength=0.60,
    )
    uncertain = compile_memory(
        scenario,
        "INSUFFICIENTLY_KNOWN",
        "unknown",
        evidence_strength=0.60,
    )
    assert uncertain.influence_strength < verification.influence_strength
    assert max(uncertain.priors.values()) < max(verification.priors.values())
    assert uncertain.verification_required is False


def test_confirmed_contradiction_neutralizes_planner_influence():
    scenario = build_scenarios(1)[0]
    contradicted = compile_memory(
        scenario,
        "APPLICABLE",
        "c1",
        evidence_strength=1.0,
        contradiction_state="CONFIRMED_CONTRADICTION",
    )
    assert contradicted.influence_strength == 0.0
    assert contradicted.priors == {branch: 0.25 for branch in scenario.branches}
    assert contradicted.verification_required is False


def test_contradiction_cannot_increase_influence():
    scenario = build_scenarios(1)[0]
    clean = compile_memory(scenario, "APPLICABLE", "clean", evidence_strength=0.90)
    possible = compile_memory(
        scenario,
        "APPLICABLE",
        "possible",
        evidence_strength=0.90,
        contradiction_state="POSSIBLE_CONTRADICTION",
    )
    confirmed = compile_memory(
        scenario,
        "APPLICABLE",
        "confirmed",
        evidence_strength=0.90,
        contradiction_state="CONFIRMED_CONTRADICTION",
    )
    assert possible.influence_strength == clean.influence_strength
    assert confirmed.influence_strength == 0.0


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


def test_frozen_inputs_are_oracle_independent_and_bounded():
    assert set(MEMORY_APPLICABILITY) == set(APPLICABILITY_STRENGTH)
    assert len(MEMORY_EVIDENCE_STRENGTH) == 8
    assert len(MEMORY_CONTRADICTION_STATE) == 8
    assert all(0.0 <= value <= 1.0 for value in MEMORY_EVIDENCE_STRENGTH)
    assert set(MEMORY_CONTRADICTION_STATE) == {
        "NONE", "POSSIBLE_CONTRADICTION", "CONFIRMED_CONTRADICTION",
    }


def test_experiment_routes_all_applicability_states_into_treatment():
    result = run_experiment(30)
    treatment_states = {
        trace["applicability"]
        for trace in result["traces"]
        if trace["arm"] == "arm3_treatment"
    }
    assert treatment_states == set(APPLICABILITY_STRENGTH)


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
