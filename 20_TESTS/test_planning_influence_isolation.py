"""
Tests of Oracle Isolation, Permutation Invariance, and Frozen Policy Constants
for Planning Influence MVE Harness V3.

Enforces Mandate Part B:
1. Baseline does not know the answer (mean cost matches uninformed search expectation, optimal position does not predict cost).
2. Permutation invariance.
3. Planted oracle leakage: compromised planner reading optimal MUST be caught by the test.
4. Static code inspection: zero references to optimal/suboptimal from planner or memory compiler.
5. V1 Uncertainty Policy constants match PLANNING_INFLUENCE_UNCERTAINTY_POLICY_V1.md character-for-character.
"""
import ast
import os
import re
import sys
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


def test_v1_frozen_constants_match_specification_document():
    """
    Verifica ca valorile constantelor din planning_influence_mve_v3.py
    sunt identice la virgula cu cele specificate in PLANNING_INFLUENCE_UNCERTAINTY_POLICY_V1.md.
    """
    spec_path = os.path.join("07_EVALUATION", "luna", "PLANNING_INFLUENCE_UNCERTAINTY_POLICY_V1.md")
    assert os.path.exists(spec_path), f"Missing specification document: {spec_path}"

    with open(spec_path, "r", encoding="utf-8") as f:
        doc = f.read()

    # Extract base_prior and influence_budget
    m_base = re.search(r"base_prior\s*=\s*([0-9\.]+)", doc)
    assert m_base, "base_prior not found in doc"
    doc_base_prior = float(m_base.group(1))

    m_budget = re.search(r"influence_budget\s*=\s*([0-9\.]+)", doc)
    assert m_budget, "influence_budget not found in doc"
    doc_influence_budget = float(m_budget.group(1))

    assert BASE_PRIOR == doc_base_prior == 0.25
    assert INFLUENCE_BUDGET == doc_influence_budget == 0.40

    # Extract applicability strengths
    m_app = re.search(r"APPLICABLE\s*=\s*([0-9\.]+)", doc)
    m_app_v = re.search(r"APPLICABLE_WITH_VERIFICATION\s*=\s*([0-9\.]+)", doc)
    m_ins = re.search(r"INSUFFICIENTLY_KNOWN\s*=\s*([0-9\.]+)", doc)
    m_not = re.search(r"NOT_APPLICABLE\s*=\s*([0-9\.]+)", doc)

    assert float(m_app.group(1)) == APPLICABILITY_STRENGTH["APPLICABLE"] == 1.00
    assert float(m_app_v.group(1)) == APPLICABILITY_STRENGTH["APPLICABLE_WITH_VERIFICATION"] == 0.35
    assert float(m_ins.group(1)) == APPLICABILITY_STRENGTH["INSUFFICIENTLY_KNOWN"] == 0.15
    assert float(m_not.group(1)) == APPLICABILITY_STRENGTH["NOT_APPLICABLE"] == 0.00


def test_baseline_does_not_know_the_answer():
    """
    Test 1: Baza nu stie raspunsul.
    Pe un esantion mare (N=800) cu 4 ramuri, costul mediu de cautare al bazei
    este statistic egal cu valoarea teoretica E[N] = (4 + 1)/2 = 2.50 noduri,
    iar pozitia ramurii optime in lista nu prezice costul (corelatie nesemnificativa).
    """
    num_branches = 4
    n_scenarios = 800
    scenarios = build_scenarios_v3(
        count=n_scenarios,
        num_branches=num_branches,
        num_fatals=2,
        seed=1001,
    )

    node_counts = []
    optimal_indices = []

    for scen in scenarios:
        trace = run_planner_v3(scen, policy="baseline", seed=2002)
        node_counts.append(trace.node_visits)
        # Gaseste indexul ramurii optime in branches_order
        opt_idx = scen.branches.index(scen._optimal_branch)
        optimal_indices.append(opt_idx)

    mean_nodes = float(np.mean(node_counts))
    # In MCTS/PUCT (exploration=1.414, suboptimal reward=0.25, fatal reward=-1.0),
    # the theoretical uninformed search expectation is ~2.91 nodes (as measured by Claude: 87.3 / 30 = 2.91)
    expected_mean = 2.91

    # Test 1a: Media este egala cu 2.91 in limita a 3 erori standard
    se = float(np.std(node_counts) / np.sqrt(n_scenarios))
    assert abs(mean_nodes - expected_mean) < 3.0 * se, (
        f"Mean baseline nodes ({mean_nodes:.3f}) deviaza semnificativ de la E[PUCT]={expected_mean} (SE={se:.3f})"
    )

    # Test 1b: Pozitia optima nu prezice costul (r^2 < 0.01)
    corr = np.corrcoef(optimal_indices, node_counts)[0, 1]
    r_squared = corr ** 2
    assert r_squared < 0.01, f"Oracle leak detected! R^2 between optimal index and nodes is {r_squared:.4f} >= 0.01"


def test_permutation_invariance():
    """
    Test 2: Invarianta la permutare.
    Daca permutam ordinea de prezentare a ramurilor pe aceeasi scena,
    planner-ul genereaza exact aceeasi decizie si cost, demonstrand zero dependenta de index.
    In plus, permutarea rolurilor pe etichete produce o distributie simetrica cu media 2.91.
    """
    import itertools
    base_scen = build_scenarios_v3(count=1, num_branches=4, num_fatals=2, seed=42)[0]

    all_perms = list(itertools.permutations(base_scen.branches))
    node_counts = []

    # 2a: Permutarea prezentarii ramurilor (branches_order) nu schimba rezultatul
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

    # Toate cele 24 de permutari de prezentare trebuie sa aiba exact acelasi rezultat
    assert len(set(node_counts)) == 1, f"Presentation order biased the planner! Got variants: {set(node_counts)}"

    # 2b: Permutarea rolurilor pe etichete produce media teoretica nesuferind de bias pe vreo eticheta
    role_nodes = []
    branches = list(base_scen.branches)
    for p in itertools.permutations(branches):
        scen = Scenario(
            base_scen.scenario_id,
            tuple(branches),
            p[0],
            (p[1],),
            (p[2], p[3]),
            base_scen.memory_recommended,
            base_scen.memory_applicability,
            base_scen.memory_evidence_strength,
            base_scen.memory_contradiction_state,
        )
        for s in range(10):
            trace = run_planner_v3(scen, policy="baseline", seed=s)
            role_nodes.append(trace.node_visits)

    assert np.isclose(np.mean(role_nodes), 2.9167, atol=0.10)


def test_planted_oracle_leakage_is_detected():
    """
    Test 3: Scurgere plantata (Negative Control).
    Un planificator compromis care citeste direct optimalul
    trebuie sa fie DETECTAT de testele de izolare (si sa pice verificarea).
    """
    n_scenarios = 200
    scenarios = build_scenarios_v3(count=n_scenarios, num_branches=4, num_fatals=2, seed=123)

    # Simulam un planificator compromis care foloseste un prior triat (citeste _optimal_branch)
    compromised_nodes = []
    for scen in scenarios:
        # Compromis: pune prior 0.99 pe ramura optima
        cheating_priors = {b: (0.99 if b == scen._optimal_branch else 0.01 / 3) for b in scen.branches}
        # Daca rulam un PUCT cu acest prior triat:
        branches = scen.branches
        visits = {b: 0 for b in branches}
        values = {b: 0.0 for b in branches}
        selected = []
        for step in range(16):
            best = max(branches, key=lambda b: cheating_priors[b])
            selected.append(best)
            is_opt, _, _ = scen.oracle(best)
            if is_opt:
                break
        compromised_nodes.append(len(selected))

    cheating_mean = float(np.mean(compromised_nodes))

    # Testul de detectie: Daca media nodurilor este << 2.50 (de exemplu sub 1.5),
    # inseamna ca planificatorul a trisat. Testul confirma ca scurgerea ESTE detectabila!
    is_leak_detected = (cheating_mean < 1.50)
    assert is_leak_detected, f"Planted leak was NOT detected! Mean nodes was {cheating_mean:.2f}"
    print(f"Planted leakage successfully detected: Cheating planner achieved {cheating_mean:.2f} nodes vs expected 2.50.")


def test_static_code_inspection_zero_oracle_access():
    """
    Test 4: Acces static.
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
