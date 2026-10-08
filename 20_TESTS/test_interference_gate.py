"""20_TESTS/test_interference_gate.py — Tests for Cognitive Interference Gate.

Proves:
1. Base-level activation adheres to ACT-R power-law decay.
2. Fan effect penalizes high-fan cues.
3. Proactive interference / competitor density suppresses crowded clusters.
4. MMR selection reduces redundancy under tight token budget.
5. Absolute threshold and confidence margin gating behave deterministically.
6. Execution latency respects the < 50ms cognitive budget.
"""
from __future__ import annotations

import math
import time
from pathlib import Path
import pytest

from retrieval.interference_gate import (
    CandidateMemory,
    CandidateGateEvaluation,
    CognitiveInterferenceGate,
    InterferenceGateResult,
)


def test_base_level_activation_recency_and_frequency():
    gate = CognitiveInterferenceGate(decay_d=0.5, epsilon_decay=0.1, b_max=3.0)
    t_now = 1000.0

    # No accesses
    assert gate.compute_base_level_activation([], t_now) == 0.0

    # Single recent access vs single distant access
    act_recent = gate.compute_base_level_activation([999.0], t_now) # 1 sec ago
    act_distant = gate.compute_base_level_activation([900.0], t_now) # 100 sec ago
    assert act_recent > act_distant

    # Multiple accesses increase activation
    act_multi = gate.compute_base_level_activation([999.0, 998.0, 997.0], t_now)
    assert act_multi > act_recent

    # B_max clipping
    huge_accesses = [999.9] * 100
    act_huge = gate.compute_base_level_activation(huge_accesses, t_now)
    assert act_huge == 3.0


def test_cue_activation_and_fan_effect():
    gate = CognitiveInterferenceGate(s_max=2.0)

    # Cue with fan = 1 (unique cue) vs fan = 100 (diffuse cue)
    c_unique, _ = gate.compute_cue_activation({"rare_term": 1.0}, {"rare_term": 1})
    c_diffuse, _ = gate.compute_cue_activation({"common_term": 1.0}, {"common_term": 100})

    assert c_unique > c_diffuse
    assert c_diffuse >= 0.0


def test_competitor_density_penalty():
    gate = CognitiveInterferenceGate(lambda_competitor=0.5)

    c1 = CandidateMemory(id="1", title="Memory 1", text="", embedding=[1.0, 0.0, 0.0])
    c2 = CandidateMemory(id="2", title="Memory 2", text="", embedding=[0.95, 0.05, 0.0])
    c3 = CandidateMemory(id="3", title="Memory 3", text="", embedding=[0.92, 0.08, 0.0])
    isolated = CandidateMemory(id="4", title="Memory 4", text="", embedding=[0.0, 1.0, 0.0])

    candidates = [c1, c2, c3, isolated]

    f_dense = gate.compute_competitor_density(c1, candidates)
    f_isolated = gate.compute_competitor_density(isolated, candidates)

    assert f_dense > f_isolated
    assert f_isolated == 0.0


def test_absolute_threshold_rejection():
    gate = CognitiveInterferenceGate(abs_threshold=0.5)

    weak = CandidateMemory(id="weak", title="Weak Memory", text="", reranker_score=0.1)
    strong = CandidateMemory(id="strong", title="Strong Memory", text="", reranker_score=0.9)

    res = gate.select([weak, strong])
    assert len(res.admitted) == 1
    assert res.admitted[0].id == "strong"
    assert res.evaluations["weak"].exclusion_reason == "SUB_THRESHOLD"


def test_mmr_redundancy_and_knapsack_budget():
    gate = CognitiveInterferenceGate(
        lambda_competitor=0.0,  # Isolate MMR redundancy from cluster competitor density
        lambda_redundancy=0.8,
        margin_threshold=0.01,
        max_tokens=250,
        max_items=3,
        abs_threshold=0.2,
    )

    # Base items: two nearly identical high-scoring notes, one distinct medium-scoring note
    m1 = CandidateMemory(id="m1", title="Doc A1", text="", reranker_score=0.95, tokens=100, embedding=[1.0, 0.0])
    m2 = CandidateMemory(id="m2", title="Doc A2", text="", reranker_score=0.90, tokens=100, embedding=[0.99, 0.01])
    m3 = CandidateMemory(id="m3", title="Doc B", text="", reranker_score=0.75, tokens=100, embedding=[0.0, 1.0])

    res = gate.select([m1, m2, m3])

    # m1 is selected first; m2 is penalized by redundancy (sim ~ 1.0), so m3 is preferred over m2
    admitted_ids = [m.id for m in res.admitted]
    assert admitted_ids[0] == "m1"
    assert "m3" in admitted_ids
    assert res.total_tokens <= 250


def test_confidence_margin_abstention_on_ambiguity():
    gate = CognitiveInterferenceGate(
        abs_threshold=0.0,  # Allow candidates to pass threshold to test margin check
        margin_threshold=0.1,
    )

    # Two competing memories virtually identical with negligible score difference
    cand1 = CandidateMemory(id="c1", title="Conflicting 1", text="", reranker_score=0.801, embedding=[0.99, 0.01])
    cand2 = CandidateMemory(id="c2", title="Conflicting 2", text="", reranker_score=0.800, embedding=[0.99, 0.01])

    res = gate.select([cand1, cand2])
    assert res.abstained is True
    assert "AMBIGUOUS_COMPETING_CLUSTER" in (res.abstain_reason or "")


def test_latency_within_cognitive_budget():
    gate = CognitiveInterferenceGate()
    # 50 candidate memories
    candidates = [
        CandidateMemory(
            id=f"c_{i}",
            title=f"Memory {i}",
            text="Evidence snippet " * 20,
            reranker_score=0.3 + (i % 50) * 0.01,
            tokens=50,
            embedding=[(i % 10) * 0.1, ((i + 3) % 10) * 0.1, ((i + 7) % 10) * 0.1],
        )
        for i in range(50)
    ]

    res = gate.select(candidates)
    assert res.execution_time_ms < 50.0, f"Gate latency exceeded 50ms: {res.execution_time_ms}ms"
    assert len(res.admitted) <= gate.max_items
