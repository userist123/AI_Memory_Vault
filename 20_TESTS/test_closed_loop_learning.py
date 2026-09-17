"""Tests for Part C Closed-Loop Learning: Outcome -> Evidence -> Belief Update -> Canonical Mutation.

Validates the contracts specified in ANTIGRAVITY_CLOSURE_PROGRAM.md Part C:
- C.1 Four transitions end-to-end (Outcome, Evidence, Learning, Canonical Mutation).
- C.2 Honest loop with planted false memory (monotonic decay, exact N=5 withdrawal)
      and planted true memory (stability under 85% success / 15% noise).
- C.3 Anti-self-confirmation guard (filters DIRECT_CHOICE, measures retention).
- C.4 Part A ontology concept decay bridge.
- Negative controls passing through the real detector.
"""
from __future__ import annotations

import math
import uuid
import pytest

from memory_controller.authorizer import Principal
from memory_controller.controller import StorageEngine
from cognitive_core.closed_loop_learning import (
    ClosedLoopLearner,
    ExecutionOutcome,
    MemoryEvidence,
    InfluenceType,
    AntiSelfConfirmationGuard,
    BeliefState,
    CanonicalMutationRecord,
    OntologyConceptDecayBridge,
)


def _create_mock_note(storage: StorageEngine, note_id: str, title: str, confidence: str = "medium") -> dict:
    note = {
        "id": note_id,
        "type": "knowledge",
        "lifecycle": "ACTIVE",
        "title": title,
        "status": "active",
        "confidence": confidence,
        "verification": "unverified",
        "content": f"Test memory content for {title}",
        "provenance": {"source_type": "execution", "source_ref": "unit_test"},
    }
    storage.set(note_id, note)
    return note


# =========================================================================
# C.1 Four Transitions End-to-End Test
# =========================================================================

def test_four_transitions_executed_end_to_end():
    storage = StorageEngine()
    note_id = str(uuid.uuid4())
    _create_mock_note(storage, note_id, "Circuit Breaker Pattern")

    learner = ClosedLoopLearner(storage=storage, withdrawal_threshold=0.35, prior_alpha=3.0, prior_beta=1.0)

    # 1. Rezultat: Observable execution outcome
    outcome = ExecutionOutcome(
        run_id="run-101",
        success=True,
        cost=0.15,
        metric_value=0.98,
    )
    assert outcome.success is True
    assert outcome.metric_value == 0.98

    # 2. Dovada & 3. Invatare: Bound to note UUID via control arm
    admitted, score, mutation = learner.record_outcome(
        memory_id=note_id,
        outcome=outcome,
        influence_type=InfluenceType.CONTROL_ARM,
    )
    assert admitted is True
    # Prior alpha=3.0, beta=1.0 -> updated to alpha=4.0, beta=1.0 -> 4/5 = 0.80
    assert score == pytest.approx(0.80, abs=1e-4)
    assert mutation is None  # High confidence, no downward mutation

    # Negative outcome to test mutation trigger
    # With alpha=4, beta=1: feeding 8 failures should push score below 0.35
    for i in range(8):
        fail_outcome = ExecutionOutcome(run_id=f"run-fail-{i}", success=False, cost=0.2)
        admitted, score, mutation = learner.record_outcome(
            memory_id=note_id,
            outcome=fail_outcome,
            influence_type=InfluenceType.CONTROL_ARM,
        )

    # 4. Mutatie canonica: Triggered when score <= 0.35
    assert score <= 0.35
    assert mutation is not None
    assert mutation.mutation_type == "withdraw"
    assert mutation.reversible is True

    # Verify storage note was canonically mutated
    updated = storage.get(note_id)
    assert updated["status"] == "withdrawn"
    assert updated["confidence"] == "low"
    assert updated["verification"] == "disputed"
    assert "withdrawal_reason" in updated


# =========================================================================
# C.2 Planted False Memory Test: Monotonic Decay & Exact N Observations
# =========================================================================

def test_planted_false_memory_monotonic_decay_and_exact_n():
    storage = StorageEngine()
    false_note_id = "planted-false-memory-bad-param"
    _create_mock_note(storage, false_note_id, "Bad Parameter Advice")

    # Initial prior: alpha=3.0, beta=1.0 -> initial score = 3 / 4 = 0.75
    # Withdrawal threshold: 0.35
    learner = ClosedLoopLearner(
        storage=storage,
        withdrawal_threshold=0.35,
        prior_alpha=3.0,
        prior_beta=1.0,
    )

    # Pre-calculated N: ceil(3.0 / 0.35 - (3.0 + 1.0)) = ceil(8.5714 - 4.0) = ceil(4.5714) = 5
    expected_n = learner.calculate_steps_to_withdrawal(3.0, 1.0, 0.35)
    assert expected_n == 5

    history = [learner.register_memory(false_note_id).score]
    assert history[0] == pytest.approx(0.75, abs=1e-4)

    mutation_occurred_step = None

    for step in range(1, expected_n + 1):
        outcome = ExecutionOutcome(
            run_id=f"planted-fail-run-{step}",
            success=False,
            cost=0.5,
            metric_value=0.0,
            error_message="Parameter caused execution timeout",
        )
        admitted, score, mutation = learner.record_outcome(
            memory_id=false_note_id,
            outcome=outcome,
            influence_type=InfluenceType.CONTROL_ARM,
        )
        assert admitted is True
        history.append(score)

        # Mathematical property: strict monotonic decay
        assert history[step] < history[step - 1], (
            f"Step {step}: score {history[step]} is not strictly less than prior {history[step - 1]}"
        )

        if mutation is not None and mutation_occurred_step is None:
            mutation_occurred_step = step

    # Exactly on step 5, confidence drops below 0.35 and withdrawal is triggered
    assert mutation_occurred_step == 5
    assert history[5] == pytest.approx(3.0 / (3.0 + 1.0 + 5.0), abs=1e-4)  # 3/9 = 0.3333 <= 0.35
    assert history[4] > 0.35  # 3/8 = 0.375 > 0.35

    # Check that storage was mutated with full provenance
    mutated_note = storage.get(false_note_id)
    assert mutated_note["status"] == "withdrawn"
    assert mutated_note["confidence_score"] <= 0.35

    # Prove reversibility: rollback
    mutation_id = learner.mutation_history[0].mutation_id
    success_rollback = learner.rollback_mutation(mutation_id)
    assert success_rollback is True
    restored_note = storage.get(false_note_id)
    assert restored_note["status"] == "active"
    assert restored_note["confidence"] == "medium"


# =========================================================================
# C.2 Planted True Memory Test: Stability Under Noise
# =========================================================================

def test_planted_true_memory_stability_under_noise():
    storage = StorageEngine()
    true_note_id = "planted-true-memory-circuit-breaker"
    _create_mock_note(storage, true_note_id, "Verified Circuit Breaker")

    # True memory with prior: alpha=8.0, beta=2.0 (initial score = 0.80)
    learner = ClosedLoopLearner(
        storage=storage,
        withdrawal_threshold=0.35,
        prior_alpha=8.0,
        prior_beta=2.0,
    )

    # 50 observations: 85% success (42), 15% noise failure (8)
    total_runs = 50
    # Deterministic noise pattern: every 6th observation is noisy failure
    noise_indices = {6, 12, 18, 24, 30, 36, 42, 48}

    scores = []
    for i in range(1, total_runs + 1):
        is_success = i not in noise_indices
        outcome = ExecutionOutcome(
            run_id=f"true-run-{i}",
            success=is_success,
            cost=0.1,
            metric_value=1.0 if is_success else 0.0,
        )
        admitted, score, mutation = learner.record_outcome(
            memory_id=true_note_id,
            outcome=outcome,
            influence_type=InfluenceType.CONTROL_ARM,
        )
        assert admitted is True
        assert mutation is None  # MUST NEVER withdraw true memory
        scores.append(score)

    # Final score should be ~ (8 + 42) / (10 + 50) = 50/60 = 0.8333
    final_score = scores[-1]
    assert final_score >= 0.75
    assert final_score == pytest.approx(50.0 / 60.0, abs=1e-3)

    # Verify storage note remains active and untouched by withdrawal
    note = storage.get(true_note_id)
    assert note["status"] == "active"
    assert note["verification"] == "unverified"


# =========================================================================
# C.3 Anti-Self-Confirmation Guard Test
# =========================================================================

def test_anti_self_confirmation_guard_filters_direct_influence():
    storage = StorageEngine()
    note_id = "test-self-conf-note"
    _create_mock_note(storage, note_id, "Self-Confirmation Subject")

    learner = ClosedLoopLearner(storage=storage, prior_alpha=3.0, prior_beta=1.0)
    initial_score = learner.register_memory(note_id).score
    assert initial_score == pytest.approx(0.75)

    # 1. DIRECT_CHOICE evidence: MUST be blocked by guard
    direct_outcome = ExecutionOutcome(run_id="dir-1", success=True)
    admitted, score, mutation = learner.record_outcome(
        memory_id=note_id,
        outcome=direct_outcome,
        influence_type=InfluenceType.DIRECT_CHOICE,
    )
    assert admitted is False
    # Score must NOT have changed!
    assert score == pytest.approx(initial_score)

    # 2. CONTROL_ARM evidence: MUST be admitted
    control_outcome = ExecutionOutcome(run_id="ctrl-1", success=True)
    admitted_ctrl, score_ctrl, _ = learner.record_outcome(
        memory_id=note_id,
        outcome=control_outcome,
        influence_type=InfluenceType.CONTROL_ARM,
    )
    assert admitted_ctrl is True
    assert score_ctrl > initial_score  # Successfully updated

    # 3. CONSULTED_UNCHANGED evidence: MUST be admitted
    unchanged_outcome = ExecutionOutcome(run_id="unchanged-1", success=True)
    admitted_unchanged, score_unchanged, _ = learner.record_outcome(
        memory_id=note_id,
        outcome=unchanged_outcome,
        influence_type=InfluenceType.CONSULTED_UNCHANGED,
    )
    assert admitted_unchanged is True
    assert score_unchanged > score_ctrl

    # Check guard telemetry
    telemetry = learner.guard.get_telemetry()
    assert telemetry["total_evidence"] == 3
    assert telemetry["admitted_count"] == 2
    assert telemetry["rejected_count"] == 1
    assert telemetry["retention_ratio"] == pytest.approx(2 / 3, abs=1e-3)


# =========================================================================
# C.4 Bridge to Part A Ontology Concepts (Idle Decay & Utility Reinforcement)
# =========================================================================

def test_ontology_concept_decay_bridge():
    storage = StorageEngine()
    learner = ClosedLoopLearner(storage=storage)
    bridge = OntologyConceptDecayBridge(learner=learner, decay_factor=0.90, stale_threshold=0.35)

    # Register two concepts from Part A
    bridge.register_ontology_concept("semantic memory", slot_name="cognitive_architecture", occurrences=34, initial_confidence=0.75)
    bridge.register_ontology_concept("dormant obsolete term", slot_name="miscellaneous", occurrences=3, initial_confidence=0.75)

    # Epochs 1 to 8: "semantic memory" is retrieved in every epoch; "dormant obsolete term" is never retrieved
    for epoch in range(1, 9):
        res = bridge.step_epoch(retrieved_concepts={"semantic memory"})

    # Check state:
    active_concepts = bridge.tracked_concepts["semantic memory"]
    dormant_concepts = bridge.tracked_concepts["dormant obsolete term"]

    # "semantic memory" stayed active and its confidence was reinforced
    assert active_concepts["status"] == "active"
    assert active_concepts["idle_epochs"] == 0
    assert active_concepts["current_confidence"] >= 0.75

    # "dormant obsolete term" decayed: 0.75 * (0.90^8) = 0.75 * 0.430467 = 0.3228 <= 0.35 -> DEMOTED!
    assert dormant_concepts["status"] == "demoted"
    assert dormant_concepts["idle_epochs"] == 8
    assert dormant_concepts["current_confidence"] <= 0.35


# =========================================================================
# Negative Controls with Real Detectors
# =========================================================================

def test_negative_control_disabled_downward_mutation_fails_honesty_gate():
    """Negative Control 1: If downward mutation is disabled, false memory is NOT withdrawn.
    The real honesty detector catches this and raises AssertionError.
    """
    storage = StorageEngine()
    false_note_id = "dishonest-learner-false-note"
    _create_mock_note(storage, false_note_id, "Bad Advice Note")

    # Learner configured with allow_downward_mutation=False (amplifier mode)
    dishonest_learner = ClosedLoopLearner(
        storage=storage,
        withdrawal_threshold=0.35,
        allow_downward_mutation=False,
    )

    # Feed 10 consecutive failures
    for i in range(10):
        dishonest_learner.record_outcome(
            memory_id=false_note_id,
            outcome=ExecutionOutcome(run_id=f"fail-{i}", success=False),
            influence_type=InfluenceType.CONTROL_ARM,
        )

    # Honest gate detector: checks whether note was withdrawn
    note_in_storage = storage.get(false_note_id)
    with pytest.raises(AssertionError, match="Honesty violation: false memory was not withdrawn"):
        # Real detector assertion
        if note_in_storage.get("status") != "withdrawn":
            raise AssertionError("Honesty violation: false memory was not withdrawn despite repeated failures!")


def test_negative_control_bypassed_guard_pollutes_confidence():
    """Negative Control 2: If the anti-self-confirmation guard is bypassed, direct-influence choices
    pollute the belief state. The real detector catches the unverified confidence inflation.
    """
    storage = StorageEngine()
    note_id = "polluted-note"
    _create_mock_note(storage, note_id, "Self-Confirmed Note")

    # Learner with override enabled
    bypassed_learner = ClosedLoopLearner(
        storage=storage,
        anti_self_confirmation_override=True,
    )

    # Direct choice feedback
    admitted, score, _ = bypassed_learner.record_outcome(
        memory_id=note_id,
        outcome=ExecutionOutcome(run_id="override-1", success=True),
        influence_type=InfluenceType.DIRECT_CHOICE,
    )
    assert admitted is True  # Override admitted it

    # Real detector assertion: verify that an auditor inspecting the telemetry detects contamination
    telemetry = bypassed_learner.guard.get_telemetry()
    with pytest.raises(AssertionError, match="Audit failure: self-confirmation guard was bypassed"):
        if telemetry["rejected_count"] == 0 and any(
            e.influence_type == InfluenceType.DIRECT_CHOICE for e in bypassed_learner.guard.admitted_evidence
        ):
            raise AssertionError("Audit failure: self-confirmation guard was bypassed and DIRECT_CHOICE was admitted!")
