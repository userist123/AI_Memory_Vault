"""
Tests of Oracle Isolation, Permutation Invariance, Contradiction Veto,
and Frozen Policy Constants for Planning Influence MVE Harness V3.

Enforces Mandate:
1. Baseline does not know the answer (mean cost matches PUCT benchmark expectation,
   optimal position does not predict cost).
2. Permutation invariance (presentation order does not change planner trace).
3. Contradiction veto enforcement (CONFIRMED_CONTRADICTION unconditionally forces uniform priors across all policies).
4. Planted oracle leakage negative control (runtime monkeypatch replicating legacy defect is caught).
5. Strict XFAIL test on legacy planning_influence_mve.py documenting known oracle leakage.
6. Static code inspection (zero access to _optimal_branch, _suboptimal_branches, etc. from planner or memory compiler).
7. Frozen V1 Uncertainty Policy constants match PLANNING_INFLUENCE_UNCERTAINTY_POLICY_V1.md.
"""
import ast
import os
import re
import sys
from typing import List, Tuple
import numpy as np
import pytest

# Ensure root is in sys.path
sys.path.insert(0, os.path.abspath("."))
from importlib import import_module

v3_mod = import_module("07_EVALUATION.luna.planning_influence_mve_v3")
build_scenarios_v3 = v3_mod.build_scenarios_v3
run_planner_v3 = v3_mod.run_planner_v3
compile_memory_v3 = v3_mod.compile_memory_v3
Scenario = v3_mod.Scenario
PlannerTrace = v3_mod.PlannerTrace
BASE_PRIOR = v3_mod.BASE_PRIOR
INFLUENCE_BUDGET = v3_mod.INFLUENCE_BUDGET
APPLICABILITY_STRENGTH = v3_mod.APPLICABILITY_STRENGTH

puct_bench = import_module("07_EVALUATION.luna.simulate_puct_benchmark")
BENCHMARK_MEAN = puct_bench.BENCHMARK_MEAN


def test_v1_frozen_constants_match_specification_document():
    """
    Verifica ca valorile constantelor din planning_influence_mve_v3.py
    sunt identice la virgula cu cele specificate in PLANNING_INFLUENCE_UNCERTAINTY_POLICY_V1.md.
    """
    spec_path = os.path.join("07_EVALUATION", "luna", "PLANNING_INFLUENCE_UNCERTAINTY_POLICY_V1.md")
    assert os.path.exists(spec_path), f"Missing specification document: {spec_path}"

    with open(spec_path, "r", encoding="utf-8") as f:
        doc = f.read()

    m_base = re.search(r"base_prior\s*=\s*([0-9\.]+)", doc)
    assert m_base, "base_prior not found in doc"
    doc_base_prior = float(m_base.group(1))

    m_budget = re.search(r"influence_budget\s*=\s*([0-9\.]+)", doc)
    assert m_budget, "influence_budget not found in doc"
    doc_influence_budget = float(m_budget.group(1))

    assert BASE_PRIOR == doc_base_prior == 0.25
    assert INFLUENCE_BUDGET == doc_influence_budget == 0.40

    m_app = re.search(r"APPLICABLE\s*=\s*([0-9\.]+)", doc)
    m_app_v = re.search(r"APPLICABLE_WITH_VERIFICATION\s*=\s*([0-9\.]+)", doc)
    m_ins = re.search(r"INSUFFICIENTLY_KNOWN\s*=\s*([0-9\.]+)", doc)
    m_not = re.search(r"NOT_APPLICABLE\s*=\s*([0-9\.]+)", doc)

    assert float(m_app.group(1)) == APPLICABILITY_STRENGTH["APPLICABLE"] == 1.00
    assert float(m_app_v.group(1)) == APPLICABILITY_STRENGTH["APPLICABLE_WITH_VERIFICATION"] == 0.35
    assert float(m_ins.group(1)) == APPLICABILITY_STRENGTH["INSUFFICIENTLY_KNOWN"] == 0.15
    assert float(m_not.group(1)) == APPLICABILITY_STRENGTH["NOT_APPLICABLE"] == 0.00


def evaluate_baseline_isolation(scenarios: List[Scenario], seed: int = 2002) -> Tuple[float, float, float]:
    """
    Evaluates baseline search cost and correlation with optimal branch position.
    Returns (mean_nodes, standard_error, r_squared).
    """
    node_counts = []
    optimal_indices = []

    for idx, scen in enumerate(scenarios):
        trace = run_planner_v3(scen, policy="baseline", seed=seed + idx)
        node_counts.append(trace.node_visits)
        opt_idx = scen.branches.index(scen._optimal_branch)
        optimal_indices.append(opt_idx)

    arr = np.array(node_counts, dtype=np.float64)
    m_nodes = float(np.mean(arr))
    se = float(np.std(arr) / np.sqrt(len(arr)))

    if np.std(node_counts) == 0 or np.std(optimal_indices) == 0:
        r_squared = 1.0  # degenerate / perfect collapse
    else:
        corr = np.corrcoef(optimal_indices, node_counts)[0, 1]
        r_squared = float(corr ** 2) if not np.isnan(corr) else 0.0
    return m_nodes, se, r_squared


def test_baseline_does_not_know_the_answer():
    """
    Test 1: Baza nu stie raspunsul.
    Pe un esantion mare (N=800) cu 4 ramuri, costul mediu de cautare al bazei
    este statistic compatibil cu reperul PUCT Monte Carlo (BENCHMARK_MEAN ~ 2.92),
    iar pozitia ramurii optime in lista nu prezice costul (r^2 < 0.01).
    """
    num_branches = 4
    n_scenarios = 800
    scenarios = build_scenarios_v3(
        count=n_scenarios,
        num_branches=num_branches,
        num_fatals=2,
        seed=1001,
    )

    mean_nodes, se, r_squared = evaluate_baseline_isolation(scenarios, seed=2002)

    # 1a: Media este compatibila cu reperul preinregistrat in limita a 3 erori standard
    assert abs(mean_nodes - BENCHMARK_MEAN) < 3.0 * se, (
        f"Mean baseline nodes ({mean_nodes:.3f}) deviaza semnificativ de la reperul PUCT "
        f"({BENCHMARK_MEAN:.3f}) cu SE={se:.3f}"
    )

    # 1b: Pozitia optima nu prezice costul (r^2 < 0.01)
    assert r_squared < 0.01, (
        f"Oracle leak detected! R^2 between optimal index and nodes is {r_squared:.4f} >= 0.01"
    )


def test_permutation_invariance():
    """
    Test 2: Invarianta la permutare.
    Permutarea ordinii de prezentare a ramurilor pe aceeasi scena genereaza
    exact aceeasi decizie si cost, demonstrand zero dependenta de index.
    """
    import itertools
    base_scen = build_scenarios_v3(count=1, num_branches=4, num_fatals=2, seed=42)[0]
    all_perms = list(itertools.permutations(base_scen.branches))
    node_counts = []

    for perm in all_perms:
        perm_scen = Scenario(
            scenario_id=base_scen.scenario_id,
            branches_order=perm,
            _optimal_branch=base_scen._optimal_branch,
            _suboptimal_branches=base_scen._suboptimal_branches,
            _fatal_branches=base_scen._fatal_branches,
            memory_recommended=base_scen.memory_recommended,
            memory_applicability=base_scen.memory_applicability,
            memory_evidence_strength=base_scen.memory_evidence_strength,
            memory_contradiction_state=base_scen.memory_contradiction_state,
        )
        trace = run_planner_v3(perm_scen, policy="baseline", seed=42)
        node_counts.append(trace.node_visits)

    assert len(set(node_counts)) == 1, (
        f"Presentation order biased the planner! Got variants: {set(node_counts)}"
    )


def test_contradiction_veto_enforces_strictly_uniform_priors():
    """
    Test 3: Vetoul de contradicție are prioritate absolută.
    Pe scenarii marcate CONFIRMED_CONTRADICTION, indiferent de politica selectată
    ('baseline', 'advisory', 'v1_uncertainty', 'v2_verification_action') sau de
    starea de aplicabilitate inițială, memoria este complet neutralizată:
    - priorii sunt strict uniformi (1/K);
    - influence_strength este 0.0;
    - verification_required este False și verification_cost este 0.0;
    - verification_actions consumate sunt 0;
    - urmele de execuție sunt identice cu brațul de bază.
    """
    n_scenarios = 50
    stale_scenarios = build_scenarios_v3(
        count=n_scenarios,
        num_branches=4,
        num_fatals=2,
        accuracy=0.0,
        applicability_mode="calibrated",
        stale=True,
        seed=333,
    )

    for scen in stale_scenarios:
        assert scen.memory_contradiction_state == "CONFIRMED_CONTRADICTION"

        t_base = run_planner_v3(scen, policy="baseline", seed=444)

        for pol in ["advisory", "v1_uncertainty", "v2_verification_action"]:
            mem_state = compile_memory_v3(scen, policy=pol)
            assert mem_state.influence_strength == 0.0, (
                f"Contradiction veto failed: non-zero influence_strength ({mem_state.influence_strength}) under {pol}"
            )
            assert mem_state.verification_required is False, (
                f"Contradiction veto failed: verification_required is True under {pol}"
            )
            assert mem_state.verification_cost == 0.0
            for b in scen.branches:
                assert np.isclose(mem_state.priors[b], 0.25), (
                    f"Contradiction veto failed: non-uniform prior {mem_state.priors[b]} on {b} under {pol}"
                )

            trace = run_planner_v3(scen, policy=pol, seed=444)
            assert trace.verification_actions == 0
            assert trace.verification_nodes == 0.0
            assert trace.selected_branches == t_base.selected_branches
            assert trace.node_visits == t_base.node_visits


def test_planted_oracle_leakage_is_detected(monkeypatch):
    """
    Test 4: Control Negativ pe Scurgere (Monkeypatch al Defectului Reconstrucției Vechi).
    Injectează runtime defectul structural din harnașamentul original:
    1. Ramura optimă este plasată întotdeauna pe prima poziție (order[0]);
    2. Departajarea PUCT alege indexul cel mai mic în caz de egalitate (-branches.index(candidate)).
    Rulează verificarea de izolare a bazei și demonstrează că testul eșuează zgomotos
    (media scade spre 1.0 și r^2 devine masiv).
    """
    n_scenarios = 200
    clean_scenarios = build_scenarios_v3(count=n_scenarios, num_branches=4, num_fatals=2, seed=777)

    # Defectul 1: plasarea optimei pe index 0
    leaky_scenarios = []
    for sc in clean_scenarios:
        other = [b for b in sc.branches if b != sc._optimal_branch]
        leaky_order = (sc._optimal_branch, *other)
        leaky_scenarios.append(Scenario(
            scenario_id=sc.scenario_id,
            branches_order=leaky_order,
            _optimal_branch=sc._optimal_branch,
            _suboptimal_branches=sc._suboptimal_branches,
            _fatal_branches=sc._fatal_branches,
            memory_recommended=sc.memory_recommended,
            memory_applicability=sc.memory_applicability,
            memory_evidence_strength=sc.memory_evidence_strength,
            memory_contradiction_state=sc.memory_contradiction_state,
        ))

    # Defectul 2: departajare după poziția în listă (-branches.index(candidate))
    scen_map = {sc.scenario_id: sc for sc in leaky_scenarios}

    def leaky_candidate_hash(scenario_id: str, candidate: str, step: int, seed: int) -> float:
        # Rather than orthogonal SHA-256 jitter, bias monotonically towards candidate position 0
        sc = scen_map[scenario_id]
        return -float(sc.branches.index(candidate))

    monkeypatch.setattr(v3_mod, "get_position_independent_hash", leaky_candidate_hash)

    # Executăm evaluarea de izolare pe scenariile compromise
    m_nodes, se, r_squared = evaluate_baseline_isolation(leaky_scenarios, seed=888)

    # Confirmăm că defectul este surprins de aserțiunile de izolare:
    # 1. Media este masiv compromisă (~1.0 vs 2.92)
    assert m_nodes < 1.50, f"Expected compromised mean < 1.50, got {m_nodes:.3f}"
    # 2. Testul de izolare pe reper (abs(mean - BENCHMARK_MEAN) < 3*se) EȘUEAZĂ
    with pytest.raises(AssertionError, match="deviaza semnificativ de la reperul PUCT"):
        assert abs(m_nodes - BENCHMARK_MEAN) < 3.0 * se, (
            f"Mean baseline nodes ({m_nodes:.3f}) deviaza semnificativ de la reperul PUCT "
            f"({BENCHMARK_MEAN:.3f}) cu SE={se:.3f}"
        )


@pytest.mark.xfail(strict=True, reason="Legacy planning_influence_mve.py contains known oracle leakage fixed in V3")
def test_legacy_planning_influence_mve_does_not_leak_oracle_to_baseline():
    """
    Test 5: Validare Strictă XFAIL a defectului din harnașamentul vechi.
    Harnașamentul original 07_EVALUATION/luna/planning_influence_mve.py avea defectul
    structural de scurgere a oracolului în brațul de bază (optimal=order[0] și
    departajare -branches.index(candidate)), obținând cost mediu de exact 1.0 nod.
    Acest test cere ca baza să aibă cost > 2.0 noduri.
    Testul EȘUEAZĂ în mod strict așteptat (xfail strict=True).
    Dacă cineva alterează harnașamentul retras pe main, testul va deveni XPASS și va pica CI-ul.
    """
    legacy = import_module("07_EVALUATION.luna.planning_influence_mve")
    scenarios = legacy.build_scenarios(30)
    traces = [
        legacy.run_planner(s, {b: 0.25 for b in s.branches})
        for s in scenarios
    ]
    mean_nodes = float(np.mean([t.node_visits for t in traces]))
    # An uninformed search on 4 branches requires on average ~2.92 nodes.
    # The legacy harness leaks the oracle to the baseline, yielding mean_nodes == 1.0.
    assert mean_nodes > 2.0, (
        f"Legacy harness leaked oracle to baseline! Mean nodes was {mean_nodes:.2f} <= 2.0"
    )


def test_static_code_inspection_zero_oracle_access():
    """
    Test 6: Acces static.
    Verifica pe AST ca in run_planner_v3 si compile_memory_v3 nu exista
    niciun apel sau acces de atribut catre _optimal_branch, _suboptimal_branches,
    _fatal_branches, scenario.optimal sau scenario.suboptimal.
    """
    harness_path = os.path.join("07_EVALUATION", "luna", "planning_influence_mve_v3.py")
    with open(harness_path, "r", encoding="utf-8") as f:
        code = f.read()

    parsed = ast.parse(code)

    forbidden_attrs = {
        "optimal", "suboptimal", "fatal_a", "fatal_b",
        "_optimal_branch", "_suboptimal_branches", "_fatal_branches",
    }

    target_functions = {"run_planner_v3", "compile_memory_v3"}

    for node in ast.walk(parsed):
        if isinstance(node, ast.FunctionDef) and node.name in target_functions:
            for subnode in ast.walk(node):
                if isinstance(subnode, ast.Attribute):
                    assert subnode.attr not in forbidden_attrs, (
                        f"Oracle leakage found in function '{node.name}': accessed '{subnode.attr}'!"
                    )
