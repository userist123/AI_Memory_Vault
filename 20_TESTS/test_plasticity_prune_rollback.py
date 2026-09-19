"""Unit tests for transactional edge pruning and bit-for-bit rollback in graph/plasticity.py."""
import sys
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parents[1]
PACKAGES = REPO_ROOT / "03_IMPLEMENTATION" / "packages"
for p in (str(REPO_ROOT), str(PACKAGES)):
    if p not in sys.path:
        sys.path.insert(0, p)

import pytest
from graph.synapse_store import Synapse, SynapseStore
from graph.plasticity import PlasticityEngine, PlasticityJournal, PruneResult


@pytest.fixture
def temp_journal(tmp_path):
    jpath = tmp_path / "telemetry" / "test_plasticity_journal.jsonl"
    return PlasticityJournal(journal_path=jpath)


@pytest.fixture
def sample_synapses():
    return [
        Synapse(
            source_id="note_a",
            target_id="note_b",
            relation="depends_on",
            weight=0.9,
            origin="declared",
            activations=3,
            reinforcements=1,
            depressions=0,
            evidence=["run_001"],
            updated="2026-09-01T10:00:00",
        ),
        Synapse(
            source_id="note_b",
            target_id="note_c",
            relation="related_to",
            weight=0.4,
            origin="proposed_weak",
            activations=0,
            reinforcements=0,
            depressions=0,
            evidence=[],
            updated="2026-09-01T10:00:00",
        ),
        Synapse(
            source_id="note_c",
            target_id="note_d",
            relation="supersedes",
            weight=1.1,
            origin="declared",
            activations=5,
            reinforcements=2,
            depressions=1,
            evidence=["run_002", "run_003"],
            updated="2026-09-02T12:00:00",
        ),
    ]


def test_prune_dry_run_leaves_store_and_journal_untouched(temp_journal, sample_synapses):
    store = SynapseStore(sample_synapses)
    engine = PlasticityEngine(journal=temp_journal)

    # Attempt dry-run pruning of note_a -> note_b
    result = engine.prune_edges(
        synapse_store=store,
        edges_to_prune=[("note_a", "note_b")],
        dry_run=True,
        reason="test_audit",
    )

    assert result.dry_run is True
    assert result.edges_pruned == 1
    assert len(result.pruned_synapses) == 1
    assert result.pruned_synapses[0]["source_id"] == "note_a"
    assert result.pruned_synapses[0]["target_id"] == "note_b"
    assert len(result.journal_entry_ids) == 0

    # Store must be unmodified
    assert len(store.all()) == 3
    assert ("note_a", "note_b", "depends_on") in store._by_key
    assert any(s.target_id == "note_b" for s in store.neighbors("note_a"))

    # Journal file must not have prune entries
    entries = temp_journal.load_entries()
    assert len(entries) == 0


def test_prune_live_removes_edges_and_records_complete_state(temp_journal, sample_synapses):
    store = SynapseStore(sample_synapses)
    engine = PlasticityEngine(journal=temp_journal)

    run_id = "prune_test_run_01"
    result = engine.prune_edges(
        synapse_store=store,
        edges_to_prune=[("note_a", "note_b"), {"source": "note_b", "target": "note_c"}],
        run_id=run_id,
        dry_run=False,
        reason="audit_rejection",
    )

    assert result.dry_run is False
    assert result.edges_pruned == 2
    assert len(result.journal_entry_ids) == 2
    assert len(store.all()) == 1

    # Only note_c -> note_d remains in store
    assert ("note_a", "note_b", "depends_on") not in store._by_key
    assert ("note_b", "note_c", "related_to") not in store._by_key
    assert ("note_c", "note_d", "supersedes") in store._by_key
    assert len(store.neighbors("note_a")) == 0
    assert len(store.neighbors("note_b")) == 0

    # Verify journal content
    entries = temp_journal.load_entries(run_id=run_id)
    assert len(entries) == 2
    assert all(e.action == "prune" for e in entries)
    assert all("synapse_state" in e.metadata for e in entries)

    # First entry metadata must preserve original synapse attributes
    e1 = entries[0]
    assert e1.source_id == "note_a"
    assert e1.target_id == "note_b"
    assert e1.metadata["synapse_state"]["activations"] == 3
    assert e1.metadata["synapse_state"]["reinforcements"] == 1
    assert e1.metadata["synapse_state"]["evidence"] == ["run_001"]


def test_prune_rollback_restores_bit_for_bit_state(temp_journal, sample_synapses):
    store = SynapseStore(sample_synapses)
    engine = PlasticityEngine(journal=temp_journal)

    original_syn_a_b = store._by_key[("note_a", "note_b", "depends_on")]

    run_id = "prune_test_run_02"
    result = engine.prune_edges(
        synapse_store=store,
        edges_to_prune=[("note_a", "note_b")],
        run_id=run_id,
        dry_run=False,
        reason="audit_rejection",
    )
    assert result.edges_pruned == 1
    assert ("note_a", "note_b", "depends_on") not in store._by_key

    # Perform Rollback
    rb_result = temp_journal.rollback(run_id=run_id, synapse_store=store)
    assert rb_result.success is True
    assert rb_result.edges_reverted == 1

    # The synapse must be restored in the store with bit-for-bit equality
    assert ("note_a", "note_b", "depends_on") in store._by_key
    restored = store._by_key[("note_a", "note_b", "depends_on")]

    assert restored.source_id == original_syn_a_b.source_id
    assert restored.target_id == original_syn_a_b.target_id
    assert restored.relation == original_syn_a_b.relation
    assert restored.weight == pytest.approx(original_syn_a_b.weight)
    assert restored.origin == original_syn_a_b.origin
    assert restored.activations == original_syn_a_b.activations
    assert restored.reinforcements == original_syn_a_b.reinforcements
    assert restored.depressions == original_syn_a_b.depressions
    assert restored.evidence == original_syn_a_b.evidence
    assert restored.updated == original_syn_a_b.updated

    # Adjacency must also be rebuilt
    assert any(s.target_id == "note_b" for s in store.neighbors("note_a"))

    # Second rollback attempt must be idempotent (0 reverted)
    rb_result2 = temp_journal.rollback(run_id=run_id, synapse_store=store)
    assert rb_result2.success is True
    assert rb_result2.edges_reverted == 0
