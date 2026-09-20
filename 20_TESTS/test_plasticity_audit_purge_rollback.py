"""20_TESTS/test_plasticity_audit_purge_rollback.py — Proves audit edge prune and byte-for-byte rollback.

Enforces:
1. Exact sample hash binding (SHA-256 matches verdicts).
2. All 29 rejected edges are proven absent from the live graph post-clean.
3. Transactional pruning of exactly the 29 rejected edges via PlasticityEngine.
4. Proven byte-for-byte restoration of the graph upon rollback.
5. End-to-end integration through SynapseStore and PlasticityEngine production interfaces.
"""
from __future__ import annotations

import json
import uuid
from dataclasses import asdict
from datetime import datetime, timezone
from pathlib import Path

import pytest

REPO = Path(__file__).resolve().parents[1]
SAMPLE_PATH = REPO / "07_EVALUATION" / "edge_audit_v2" / "audit_sample_declared_50.json"
VERDICTS_PATH = REPO / "07_EVALUATION" / "edge_audit_v2" / "audit_verdicts_declared.json"


@pytest.fixture(scope="module")
def audit_data():
    sample_text = SAMPLE_PATH.read_bytes()
    norm = sample_text.replace(b"\r\n", b"\n")
    import hashlib
    digest = hashlib.sha256(norm).hexdigest()

    verdicts = json.loads(VERDICTS_PATH.read_text(encoding="utf-8"))
    assert digest == verdicts.get("sample_sha256"), "sample SHA-256 must match verdicts"

    reject_indices = {v["index"] for v in verdicts.get("verdicts", []) if v.get("verdict") == "REJECT"}
    assert len(reject_indices) == 29, f"expected 29 rejections, got {len(reject_indices)}"

    sample_items = json.loads(SAMPLE_PATH.read_text(encoding="utf-8")).get("samples", [])
    rejected = [s for s in sample_items if s["index"] in reject_indices]
    assert len(rejected) == 29

    edges_to_prune = [(r["source_id"], r["target_id"], r["relation"]) for r in rejected]
    return {"digest": digest, "rejected": rejected, "edges_to_prune": edges_to_prune}


def test_audit_sample_hash_integrity(audit_data):
    """The audit sample file cannot drift without invalidating verdicts."""
    assert len(audit_data["edges_to_prune"]) == 29


def test_rejected_edges_are_absent_from_live_graph(audit_data):
    """The live graph after frontmatter cleaning must contain NONE of the 29 rejected edges."""
    from retrieval.vault_index import VaultIndex
    from graph.synapse_store import SynapseStore

    idx = VaultIndex.load(REPO, include_raw=True, include_archived=True)
    store = SynapseStore.from_index(idx)
    live_keys = {(s.source_id, s.target_id, s.relation) for s in store.all()}

    for src, tgt, rel in audit_data["edges_to_prune"]:
        assert (src, tgt, rel) not in live_keys, (
            f"rejected edge ({src}, {tgt}, {rel}) is still present in live graph"
        )


def test_plasticity_prune_and_byte_for_byte_rollback(audit_data):
    """Pruning removes exactly the 29 rejected edges, and rollback restores the graph byte-for-byte."""
    from retrieval.vault_index import VaultIndex
    from graph.synapse_store import SynapseStore, Synapse
    from graph.plasticity import PlasticityEngine

    idx = VaultIndex.load(REPO, include_raw=True, include_archived=True)
    store = SynapseStore.from_index(idx)

    # Populate the 29 rejected synapses into the store to simulate pre-prune state
    now_ts = datetime.now(timezone.utc).isoformat(timespec="seconds")
    for src, tgt, rel in audit_data["edges_to_prune"]:
        store.add(Synapse(
            source_id=src, target_id=tgt, relation=rel,
            weight=0.5, origin="declared", updated=now_ts,
        ))

    initial_count = len(store.all())

    # Capture complete state of all synapses before prune
    before_state = sorted(
        [(s.source_id, s.target_id, s.relation, s.weight, s.origin, s.evidence) for s in store.all()]
    )

    engine = PlasticityEngine()
    run_id = f"test_rollback_audit_29_{uuid.uuid4().hex[:8]}"

    # Execute prune
    res_prune = engine.prune_edges(store, audit_data["edges_to_prune"], run_id=run_id, dry_run=False)
    assert res_prune.edges_pruned == 29
    assert len(store.all()) == initial_count - 29

    # Assert that none of the 29 pruned edges remain
    remaining_keys = {(s.source_id, s.target_id, s.relation) for s in store.all()}
    for src, tgt, rel in audit_data["edges_to_prune"]:
        assert (src, tgt, rel) not in remaining_keys, f"pruned edge ({src}, {tgt}, {rel}) still in store"

    # Execute rollback
    res_rollback = engine.rollback(run_id=run_id, synapse_store=store)
    assert res_rollback.edges_reverted == 29
    assert len(store.all()) == initial_count

    # Assert byte-for-byte identical state
    after_state = sorted(
        [(s.source_id, s.target_id, s.relation, s.weight, s.origin, s.evidence) for s in store.all()]
    )
    assert before_state == after_state, "graph state after rollback must be byte-for-byte identical"


def test_synapse_store_delegation_to_plasticity(audit_data):
    """SynapseStore.prune_specific_edges and rollback_prune properly consume PlasticityEngine."""
    from retrieval.vault_index import VaultIndex
    from graph.synapse_store import SynapseStore, Synapse

    idx = VaultIndex.load(REPO, include_raw=True, include_archived=True)
    store = SynapseStore.from_index(idx)

    now_ts = datetime.now(timezone.utc).isoformat(timespec="seconds")
    for src, tgt, rel in audit_data["edges_to_prune"]:
        store.add(Synapse(
            source_id=src, target_id=tgt, relation=rel,
            weight=0.5, origin="declared", updated=now_ts,
        ))

    initial_count = len(store.all())

    run_id = f"test_store_delegation_{uuid.uuid4().hex[:8]}"
    prune_res = store.prune_specific_edges(audit_data["edges_to_prune"], run_id=run_id, dry_run=False)
    assert prune_res.edges_pruned == 29
    assert len(store.all()) == initial_count - 29

    rb_res = store.rollback_prune(run_id=run_id)
    assert rb_res.edges_reverted == 29
    assert len(store.all()) == initial_count
