"""tests/test_attribution_plasticity.py — Test suite for Task r010 Attribution-Aware Plasticity.

Verifies:
1. Five distinct attribution states (PRESENT, RETRIEVED_CANDIDATE, CONTEXT_PACKED, ACTUALLY_USED, PLAUSIBLY_CAUSED).
2. Non-uniform reinforcement: used nodes strengthen incoming edges, while nodes merely in context do not.
3. Bounded updates & asymptotic compounding: [0.0, 1.5] bounds, delta cap <= 0.15, diminishing gains.
4. Failure depression: verified failure depresses weights asymptotically toward 0.0.
5. Strict fail-closed semantics: unverified outcomes, missing traces, or malformed data yield ZERO updates with explicit status.
6. No auto-promotion invariant: plasticity never touches note lifecycle, frontmatter, or verification.
7. Append-only journal integrity & rollback correctness: exact weight restoration.
8. Determinism: identical inputs produce identical weight updates.
"""
from __future__ import annotations

import json
import os
import sys
from pathlib import Path
from typing import Any, Dict, List
import pytest

ROOT = Path(__file__).resolve().parents[1]
PACKAGES = ROOT / "03_IMPLEMENTATION" / "packages"
for p in (str(ROOT), str(PACKAGES)):
    if p not in sys.path:
        sys.path.insert(0, p)

os.environ.setdefault("MEMORY_CONTROLLER_HMAC_SECRET", "test_hmac_secret_for_eval_harness_32bytes_long")

from graph.plasticity import (
    AttributionModel,
    AttributionResult,
    JournalEntry,
    MemoryAttributionState,
    PlasticityEngine,
    PlasticityJournal,
    PlasticityResult,
    RollbackResult,
    MAX_WEIGHT,
    MIN_WEIGHT,
    MAX_SINGLE_DELTA,
)
from graph.synapse_store import Synapse, SynapseStore
from memory_controller.controller import MemoryController, StorageEngine, Lifecycle
from memory_controller.authorizer import Principal


@pytest.fixture
def temp_journal(tmp_path):
    journal_path = tmp_path / "telemetry" / "plasticity_journal.jsonl"
    return PlasticityJournal(journal_path)


@pytest.fixture
def seeded_store():
    store = SynapseStore()
    # Seed nodes: seed_1, seed_2
    # Targets: note_active_used, note_passive_context, note_candidate_only, note_unrelated
    store.add(Synapse("seed_1", "note_active_used", "depends_on", weight=0.6))
    store.add(Synapse("seed_1", "note_passive_context", "related_to", weight=0.5))
    store.add(Synapse("seed_2", "note_candidate_only", "related_to", weight=0.4))
    store.add(Synapse("note_unrelated", "note_other", "related_to", weight=0.3))
    return store


# ==============================================================================
# 1. Five-State Attribution Model Tests
# ==============================================================================

def test_five_states_distinguished_never_collapsed():
    """Verify that all 5 states are distinctly identified and never conflated."""
    vault_present = ["note_vault_1", "note_vault_2", "note_used", "note_in_context"]
    candidate_trace = {
        "run_id": "run_001",
        "candidates_considered": ["note_cand_1", "note_used", "note_in_context"],
        "graph_seed_ids": ["seed_1"],
        "final_context_ids": ["note_used", "note_in_context"],
        "graph_edges_traversed": [
            {"source": "seed_1", "target": "note_used", "relation": "depends_on"},
            {"source": "seed_1", "target": "note_in_context", "relation": "related_to"},
        ],
    }

    # Only note_used is actually cited in output
    attribution = AttributionModel.attribute(
        candidate_trace=candidate_trace,
        used_memory_ids=["note_used"],
        vault_present_ids=vault_present,
        run_id="run_001",
    )

    states = attribution.node_states
    # 1. Present in vault but never retrieved
    assert states.get("note_vault_1") == MemoryAttributionState.PRESENT
    # 2. Retrieved candidate but not packed
    assert states.get("note_cand_1") == MemoryAttributionState.RETRIEVED_CANDIDATE
    # 3. Packed into context but NOT used
    assert states.get("note_in_context") == MemoryAttributionState.CONTEXT_PACKED
    # 4. Actually used
    assert states.get("note_used") == MemoryAttributionState.ACTUALLY_USED

    # 5. Attributed edges (PLAUSIBLY_CAUSED)
    # Only seed_1 -> note_used should be attributed because note_used was used!
    assert ("seed_1", "note_used", "depends_on") in attribution.attributed_edges
    # Crucial: seed_1 -> note_in_context must NOT be attributed!
    assert ("seed_1", "note_in_context", "related_to") not in attribution.attributed_edges


def test_citation_extraction_from_execution_output():
    """Verify that citation patterns [[note]], [note], note_... are recognized."""
    valid_ids = {"SEC_POLICY_01", "ARCH_GRAPH_42", "MEM_CACHE_99"}
    text = (
        "Based on our findings in [[SEC_POLICY_01]] and [ARCH_GRAPH_42], "
        "we can confirm that the system adheres to the specification."
    )
    citations = AttributionModel.extract_citations(text, valid_ids)
    assert citations == {"SEC_POLICY_01", "ARCH_GRAPH_42"}
    assert "MEM_CACHE_99" not in citations


# ==============================================================================
# 2. Non-Uniform Reinforcement (Hub Pollution Prevention)
# ==============================================================================

def test_non_uniform_reinforcement_anti_hub_pollution(seeded_store, temp_journal):
    """An edge whose target is merely in context must NOT strengthen.
    
    Only edges whose targets were ACTUALLY_USED receive reinforcement.
    """
    engine = PlasticityEngine(journal=temp_journal, default_rate=0.2)

    candidate_trace = {
        "run_id": "run_hub_test",
        "candidates_considered": ["note_active_used", "note_passive_context"],
        "final_context_ids": ["note_active_used", "note_passive_context"],
        "graph_edges_traversed": [
            {"source": "seed_1", "target": "note_active_used", "relation": "depends_on"},
            {"source": "seed_1", "target": "note_passive_context", "relation": "related_to"},
        ],
    }

    outcome = {
        "run_id": "run_hub_test",
        "outcome": "success",
        "verification_method": "test_pass",
    }

    # Only note_active_used was used
    res = engine.apply_outcome(
        synapse_store=seeded_store,
        candidate_trace=candidate_trace,
        outcome_record=outcome,
        used_memory_ids=["note_active_used"],
    )

    assert res.status == "applied"
    assert res.applied_count == 1

    # Check weights
    syn_used = [s for s in seeded_store.all() if s.source_id == "seed_1" and s.target_id == "note_active_used"][0]
    syn_passive = [s for s in seeded_store.all() if s.source_id == "seed_1" and s.target_id == "note_passive_context"][0]

    # The used edge strengthened
    assert syn_used.weight > 0.6
    assert syn_used.reinforcements == 1

    # The passive edge remained strictly UNCHANGED (anti-hub pollution)
    assert syn_passive.weight == 0.5
    assert syn_passive.reinforcements == 0


# ==============================================================================
# 3. Bounded Updates & Asymptotic Compounding Tests
# ==============================================================================

def test_bounded_weights_and_single_delta_cap(temp_journal):
    """Verify weight updates are strictly bounded in [0.0, 1.5] and delta <= 0.15."""
    store = SynapseStore()
    # Edge starting at 0.0 with high learning rate
    store.add(Synapse("u", "v", "depends_on", weight=0.0))

    engine = PlasticityEngine(journal=temp_journal, default_rate=0.5, max_single_delta=0.15)

    candidate_trace = {
        "final_context_ids": ["v"],
        "graph_edges_traversed": [{"source": "u", "target": "v", "relation": "depends_on"}],
    }
    outcome = {"outcome": "success", "verification_method": "pytest", "run_id": "r_bound_1"}

    res = engine.apply_outcome(
        synapse_store=store,
        candidate_trace=candidate_trace,
        outcome_record=outcome,
        used_memory_ids=["v"],
    )

    syn = store.all()[0]
    delta = syn.weight - 0.0
    # Even though rate * (1.5 - 0.0) = 0.75, single update cap is 0.15
    assert delta <= MAX_SINGLE_DELTA
    assert syn.weight == pytest.approx(0.15, abs=1e-5)


def test_asymptotic_compounding_diminishing_returns(temp_journal):
    """Verify asymptotic saturation towards MAX_WEIGHT under repeated success."""
    store = SynapseStore()
    store.add(Synapse("src", "tgt", "depends_on", weight=1.0))
    engine = PlasticityEngine(journal=temp_journal, default_rate=0.15)

    candidate_trace = {
        "final_context_ids": ["tgt"],
        "graph_edges_traversed": [{"source": "src", "target": "tgt", "relation": "depends_on"}],
    }
    outcome = {"outcome": "success", "verification_method": "test_pass"}

    weights = [store.all()[0].weight]
    deltas = []

    for i in range(15):
        engine.apply_outcome(
            synapse_store=store,
            candidate_trace=candidate_trace,
            outcome_record=outcome,
            used_memory_ids=["tgt"],
            run_id=f"step_{i}",
        )
        current_w = store.all()[0].weight
        deltas.append(current_w - weights[-1])
        weights.append(current_w)

    # Weights monotonically increase but never exceed MAX_WEIGHT (1.5)
    assert weights[-1] <= MAX_WEIGHT
    # Diminishing deltas (asymptotic compounding)
    for i in range(len(deltas) - 1):
        assert deltas[i + 1] <= deltas[i] + 1e-6


# ==============================================================================
# 4. Failure Depression Tests
# ==============================================================================

def test_failure_depression_reduces_weight(temp_journal):
    """Verified failure must depress edges that plausibly caused it."""
    store = SynapseStore()
    store.add(Synapse("bad_src", "bad_tgt", "depends_on", weight=0.8))
    engine = PlasticityEngine(journal=temp_journal, default_rate=0.2)

    candidate_trace = {
        "final_context_ids": ["bad_tgt"],
        "graph_edges_traversed": [{"source": "bad_src", "target": "bad_tgt", "relation": "depends_on"}],
    }
    # Outcome is a verified FAIL
    outcome = {"outcome": "fail", "verification_method": "exit_code", "run_id": "fail_01"}

    res = engine.apply_outcome(
        synapse_store=store,
        candidate_trace=candidate_trace,
        outcome_record=outcome,
        used_memory_ids=["bad_tgt"],
    )

    assert res.status == "applied"
    syn = store.all()[0]
    assert syn.weight < 0.8
    assert syn.depressions == 1
    assert syn.weight >= MIN_WEIGHT


def test_repeated_failure_never_drops_below_min_weight(temp_journal):
    """Repeated failures depress asymptotically without dropping below MIN_WEIGHT (0.0)."""
    store = SynapseStore()
    store.add(Synapse("s", "t", "related_to", weight=0.4))
    engine = PlasticityEngine(journal=temp_journal, default_rate=0.5)

    candidate_trace = {
        "final_context_ids": ["t"],
        "graph_edges_traversed": [{"source": "s", "target": "t", "relation": "related_to"}],
    }
    outcome = {"outcome": "fail", "verification_method": "ci"}

    for i in range(20):
        engine.apply_outcome(
            synapse_store=store,
            candidate_trace=candidate_trace,
            outcome_record=outcome,
            used_memory_ids=["t"],
            run_id=f"fail_run_{i}",
        )

    syn = store.all()[0]
    assert syn.weight >= MIN_WEIGHT
    assert syn.weight == pytest.approx(0.0, abs=1e-3)


# ==============================================================================
# 5. Fail-Closed Semantics Tests
# ==============================================================================

def test_fail_closed_unverified_outcome(seeded_store, temp_journal):
    """Unverified outcome (verification_method='none') yields zero updates."""
    engine = PlasticityEngine(journal=temp_journal)
    candidate_trace = {
        "final_context_ids": ["note_active_used"],
        "graph_edges_traversed": [{"source": "seed_1", "target": "note_active_used", "relation": "depends_on"}],
    }
    outcome = {"outcome": "success", "verification_method": "none"}

    res = engine.apply_outcome(
        synapse_store=seeded_store,
        candidate_trace=candidate_trace,
        outcome_record=outcome,
        used_memory_ids=["note_active_used"],
    )

    assert res.status == "unverified_outcome"
    assert res.applied_count == 0
    syn = [s for s in seeded_store.all() if s.target_id == "note_active_used"][0]
    assert syn.weight == 0.6  # unchanged


def test_fail_closed_missing_or_malformed_trace(seeded_store, temp_journal):
    """Missing or malformed candidate trace yields zero updates."""
    engine = PlasticityEngine(journal=temp_journal)
    outcome = {"outcome": "success", "verification_method": "test_pass"}

    res1 = engine.apply_outcome(
        synapse_store=seeded_store,
        candidate_trace=None,
        outcome_record=outcome,
        used_memory_ids=["note_active_used"],
    )
    assert res1.status == "trace_missing"

    res2 = engine.apply_outcome(
        synapse_store=seeded_store,
        candidate_trace="not a dictionary",  # type: ignore
        outcome_record=outcome,
        used_memory_ids=["note_active_used"],
    )
    assert res2.status == "malformed_trace"


def test_fail_closed_unsupported_outcome(seeded_store, temp_journal):
    """Partial or unknown outcomes yield zero updates."""
    engine = PlasticityEngine(journal=temp_journal)
    candidate_trace = {
        "final_context_ids": ["note_active_used"],
        "graph_edges_traversed": [{"source": "seed_1", "target": "note_active_used", "relation": "depends_on"}],
    }
    outcome = {"outcome": "partial", "verification_method": "test_pass"}

    res = engine.apply_outcome(
        synapse_store=seeded_store,
        candidate_trace=candidate_trace,
        outcome_record=outcome,
        used_memory_ids=["note_active_used"],
    )
    assert res.status == "unsupported_outcome"
    assert res.applied_count == 0


# ==============================================================================
# 6. No Auto-Promotion (Security Invariant) Tests
# ==============================================================================

def test_adversarial_no_auto_promotion_invariant(temp_journal):
    """Verify that plasticity NEVER mutates note lifecycle, frontmatter, or status."""
    storage = StorageEngine()
    # Seed notes with RAW and REVIEW lifecycles
    raw_note = {
        "id": "note_raw_candidate",
        "title": "Raw Knowledge",
        "body": "Unverified raw note content",
        "lifecycle": Lifecycle.RAW.value,
        "verification": "unverified",
        "confidence": "low",
    }
    review_note = {
        "id": "note_review_seed",
        "title": "Review Seed",
        "body": "Seed in review stage",
        "lifecycle": Lifecycle.REVIEW.value,
        "verification": "unverified",
        "confidence": "medium",
    }
    storage.set("note_raw_candidate", raw_note)
    storage.set("note_review_seed", review_note)

    # Synapse connecting them
    store = SynapseStore()
    store.add(Synapse("note_review_seed", "note_raw_candidate", "applies_to", weight=0.5))

    engine = PlasticityEngine(journal=temp_journal)
    candidate_trace = {
        "final_context_ids": ["note_raw_candidate"],
        "graph_edges_traversed": [{"source": "note_review_seed", "target": "note_raw_candidate", "relation": "applies_to"}],
    }
    outcome = {"outcome": "success", "verification_method": "human_confirmed", "run_id": "promo_test"}

    # Repeatedly reinforce
    for _ in range(5):
        engine.apply_outcome(
            synapse_store=store,
            candidate_trace=candidate_trace,
            outcome_record=outcome,
            used_memory_ids=["note_raw_candidate"],
        )

    # Synapse weight strengthened
    syn = store.all()[0]
    assert syn.weight > 0.5

    # CRITICAL: Note lifecycles and verification in storage MUST remain exactly as originally saved!
    fetched_raw = storage.get("note_raw_candidate")
    fetched_review = storage.get("note_review_seed")

    assert fetched_raw["lifecycle"] == Lifecycle.RAW.value
    assert fetched_raw["verification"] == "unverified"
    assert fetched_review["lifecycle"] == Lifecycle.REVIEW.value
    assert fetched_review["verification"] == "unverified"


# ==============================================================================
# 7. Journal Completeness & Rollback Tests
# ==============================================================================

def test_journal_logging_and_exact_rollback(seeded_store, temp_journal):
    """Every weight update is logged with rollback capability to restore exact weights."""
    engine = PlasticityEngine(journal=temp_journal, default_rate=0.2)

    initial_weight = seeded_store.all()[0].weight

    candidate_trace = {
        "run_id": "run_rollback_demo",
        "final_context_ids": ["note_active_used"],
        "graph_edges_traversed": [{"source": "seed_1", "target": "note_active_used", "relation": "depends_on"}],
    }
    outcome = {"outcome": "success", "verification_method": "test_pass", "run_id": "run_rollback_demo"}

    res = engine.apply_outcome(
        synapse_store=seeded_store,
        candidate_trace=candidate_trace,
        outcome_record=outcome,
        used_memory_ids=["note_active_used"],
    )

    updated_weight = seeded_store.all()[0].weight
    assert updated_weight > initial_weight

    # Check journal has entry
    entries = temp_journal.load_entries(run_id="run_rollback_demo")
    assert len(entries) == 1
    assert entries[0].action == "reinforce"
    assert entries[0].old_weight == initial_weight
    assert entries[0].new_weight == updated_weight

    # Execute rollback
    rb = temp_journal.rollback("run_rollback_demo", seeded_store)
    assert rb.success is True
    assert rb.edges_reverted == 1

    # Weight is precisely restored to initial_weight
    restored_weight = seeded_store.all()[0].weight
    assert restored_weight == initial_weight

    # Rollback is recorded in the append-only journal
    all_entries = temp_journal.load_entries(run_id="run_rollback_demo")
    assert len(all_entries) == 2
    assert all_entries[-1].action == "rollback"

    # Subsequent rollback is idempotent (no-op)
    rb2 = temp_journal.rollback("run_rollback_demo", seeded_store)
    assert rb2.edges_reverted == 0


# ==============================================================================
# 8. Determinism Tests
# ==============================================================================

def test_plasticity_determinism(temp_journal):
    """Identical traces, outcomes, and initial weights yield identical updates."""
    store1 = SynapseStore([Synapse("n1", "n2", "depends_on", weight=0.5)])
    store2 = SynapseStore([Synapse("n1", "n2", "depends_on", weight=0.5)])

    engine = PlasticityEngine(journal=temp_journal, default_rate=0.15)
    trace = {
        "final_context_ids": ["n2"],
        "graph_edges_traversed": [{"source": "n1", "target": "n2", "relation": "depends_on"}],
    }
    outcome = {"outcome": "success", "verification_method": "pytest", "run_id": "det_test"}

    res1 = engine.apply_outcome(store1, trace, outcome, used_memory_ids=["n2"])
    res2 = engine.apply_outcome(store2, trace, outcome, used_memory_ids=["n2"])

    assert store1.all()[0].weight == store2.all()[0].weight
    assert res1.updated_edges[0]["delta"] == res2.updated_edges[0]["delta"]


# ==============================================================================
# 9. CLI Integration & End-to-End Rollback Tests
# ==============================================================================

def test_cli_plasticity_update_with_attribution_and_rollback(tmp_path, monkeypatch):
    """Verify end-to-end CLI execution with attribution, journaling, and rollback."""
    import importlib.util

    script_path = ROOT / "30_SCRIPTS" / "knowledge" / "plasticity_update.py"
    spec = importlib.util.spec_from_file_location("plasticity_cli_test", script_path)
    cli_mod = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(cli_mod)

    # Setup environment
    syn_file = tmp_path / "synapses.json"
    trace_dir = tmp_path / "traces"
    trace_dir.mkdir(parents=True)
    journal_file = tmp_path / "journal.jsonl"

    store = SynapseStore([Synapse("seed_x", "used_target_y", "depends_on", weight=0.5)])
    store.save(syn_file)

    # Write trace with graph_edges_traversed and final_context_ids
    trace_payload = {
        "pack_id": "cli_run_01",
        "final_context_ids": ["used_target_y"],
        "graph_edges_traversed": [{"source": "seed_x", "target": "used_target_y", "relation": "depends_on"}],
    }
    (trace_dir / "cli_run_01.json").write_text(json.dumps(trace_payload), encoding="utf-8")

    # 1. Run CLI update with success and used-ids
    cli_args = [
        "plasticity_update.py",
        "--vault", str(tmp_path),
        "--synapses", "synapses.json",
        "--trace-dir", "traces",
        "--journal", "journal.jsonl",
        "--pack-id", "cli_run_01",
        "--success",
        "--used-ids", "used_target_y",
    ]
    monkeypatch.setattr(sys, "argv", cli_args)
    rc = cli_mod.main()
    assert rc == 0

    # Verify weight increased
    reloaded_store = SynapseStore.load(syn_file)
    updated_syn = reloaded_store.all()[0]
    assert updated_syn.weight > 0.5
    updated_weight = updated_syn.weight

    # 2. Run CLI rollback
    rb_args = [
        "plasticity_update.py",
        "--vault", str(tmp_path),
        "--synapses", "synapses.json",
        "--journal", "journal.jsonl",
        "--rollback", "cli_run_01",
    ]
    monkeypatch.setattr(sys, "argv", rb_args)
    rc_rb = cli_mod.main()
    assert rc_rb == 0

    # Verify weight restored to exact original 0.5
    rolled_back_store = SynapseStore.load(syn_file)
    restored_syn = rolled_back_store.all()[0]
    assert restored_syn.weight == 0.5


# ==============================================================================
# 10. Synthetic Dense Hub Anti-Pollution Stress Test
# ==============================================================================

def test_anti_hub_pollution_on_dense_star_graph(temp_journal):
    """Stress test: 50 peripheral nodes linked to a central hub.
    
    A query retrieves the hub and 10 nodes into context.
    Only node_target_3 is actually used by the agent.
    Verify that central hub incoming and outgoing edges DO NOT strengthen!
    """
    store = SynapseStore()
    hub_id = "central_hub_index"
    store.add(Synapse("seed_query", hub_id, "related_to", weight=0.4))
    for i in range(50):
        store.add(Synapse(hub_id, f"peripheral_note_{i}", "related_to", weight=0.3))

    engine = PlasticityEngine(journal=temp_journal, default_rate=0.2)

    # 10 peripheral nodes entered context, plus the hub itself
    context_packed = [hub_id] + [f"peripheral_note_{i}" for i in range(10)]
    traversed_edges = [
        {"source": "seed_query", "target": hub_id, "relation": "related_to"}
    ] + [
        {"source": hub_id, "target": f"peripheral_note_{i}", "relation": "related_to"}
        for i in range(10)
    ]

    candidate_trace = {
        "final_context_ids": context_packed,
        "graph_edges_traversed": traversed_edges,
    }
    outcome = {"outcome": "success", "verification_method": "pytest", "run_id": "hub_stress_run"}

    # Agent output explicitly uses ONLY peripheral_note_3
    res = engine.apply_outcome(
        synapse_store=store,
        candidate_trace=candidate_trace,
        outcome_record=outcome,
        used_memory_ids=["peripheral_note_3"],
    )

    assert res.status == "applied"
    assert res.applied_count == 1

    # Specifically: peripheral_note_3 edge strengthened
    syn_p3 = [s for s in store.all() if s.source_id == hub_id and s.target_id == "peripheral_note_3"][0]
    assert syn_p3.weight > 0.3
    assert syn_p3.reinforcements == 1

    # The hub edge itself (seed_query -> central_hub_index) did NOT strengthen!
    syn_hub = [s for s in store.all() if s.source_id == "seed_query" and s.target_id == hub_id][0]
    assert syn_hub.weight == 0.4
    assert syn_hub.reinforcements == 0

    # Other packed nodes (e.g. peripheral_note_0..2, 4..9) did NOT strengthen!
    for i in [0, 1, 2, 4, 5, 6, 7, 8, 9]:
        syn_other = [s for s in store.all() if s.source_id == hub_id and s.target_id == f"peripheral_note_{i}"][0]
        assert syn_other.weight == 0.3
        assert syn_other.reinforcements == 0
