"""20_TESTS/test_remaining_typed_edge_audit.py — Proves integrity of remaining 65 typed edge audit packet.

Guarantees:
1. Aggregate file contains exactly 65 distinct unjudged typed edges.
2. SHA-256 hash matches the committed .sha256 digest file.
3. Batches 1..7 partition the 65 samples completely and without duplicate indices.
4. None of the 65 samples intersect the 49 previously judged edges from audit v2.
5. Every sample corresponds to an active declared typed edge in the live graph.
"""
from __future__ import annotations

import hashlib
import json
from pathlib import Path

import pytest

REPO = Path(__file__).resolve().parents[1]
PKG_DIR = REPO / "07_EVALUATION" / "edge_audit_v2_remaining"
AGG_FILE = PKG_DIR / "audit_sample_declared_remaining_65.json"
SHA_FILE = PKG_DIR / "audit_sample_declared_remaining_65.json.sha256"
BATCHES_DIR = PKG_DIR / "batches"
V2_SAMPLE = REPO / "07_EVALUATION" / "edge_audit_v2" / "audit_sample_declared_50.json"
V2_VERDICTS = REPO / "07_EVALUATION" / "edge_audit_v2" / "audit_verdicts_declared.json"


def test_remaining_sample_sha_matches():
    raw_bytes = AGG_FILE.read_bytes()
    norm = raw_bytes.replace(b"\r\n", b"\n")
    digest = hashlib.sha256(norm).hexdigest()

    sha_text = SHA_FILE.read_text(encoding="utf-8").strip()
    recorded_hash = sha_text.split()[0]
    assert digest == recorded_hash, f"calculated {digest} != recorded {recorded_hash}"


def test_remaining_sample_structure_and_count():
    data = json.loads(AGG_FILE.read_text(encoding="utf-8"))
    samples = data.get("samples", [])
    assert len(samples) == 65
    assert data.get("population_size") == 65
    assert data.get("sample_size") == 65

    # Check distinct indices
    indices = [s["index"] for s in samples]
    assert len(indices) == len(set(indices)) == 65
    assert min(indices) == 1
    assert max(indices) == 65

    # Verify each item has required fields
    for s in samples:
        for field in ("source_id", "target_id", "relation", "source_title", "target_title", "source_excerpt", "target_excerpt"):
            assert s.get(field), f"missing field {field} in sample {s.get('index')}"


def test_batches_partition_remaining_sample():
    batch_files = sorted(BATCHES_DIR.glob("batch_*.json"))
    assert len(batch_files) == 7

    total_batch_items = 0
    all_batch_indices = set()

    for bf in batch_files:
        b_data = json.loads(bf.read_text(encoding="utf-8"))
        samples = b_data.get("samples", [])
        assert len(samples) <= 10
        total_batch_items += len(samples)
        for s in samples:
            idx = s["index"]
            assert idx not in all_batch_indices, f"duplicate index {idx} in {bf.name}"
            all_batch_indices.add(idx)

    assert total_batch_items == 65
    assert len(all_batch_indices) == 65


def test_no_intersection_with_v2_judged_edges():
    v2_samples = {s["index"]: s for s in json.loads(V2_SAMPLE.read_text(encoding="utf-8"))["samples"]}
    v2_verdicts = json.loads(V2_VERDICTS.read_text(encoding="utf-8"))["verdicts"]
    v2_judged_keys = {
        (v2_samples[v["index"]]["source_id"], v2_samples[v["index"]]["target_id"], v2_samples[v["index"]]["relation"])
        for v in v2_verdicts if v["index"] in v2_samples
    }
    assert len(v2_judged_keys) == 49

    rem_data = json.loads(AGG_FILE.read_text(encoding="utf-8"))
    for s in rem_data["samples"]:
        key = (s["source_id"], s["target_id"], s["relation"])
        assert key not in v2_judged_keys, f"remaining sample {s['index']} was already judged in v2: {key}"


def test_all_remaining_edges_present_in_live_graph():
    from retrieval.vault_index import VaultIndex
    from graph.synapse_store import SynapseStore

    idx = VaultIndex.load(REPO, include_raw=True, include_archived=True)
    store = SynapseStore.from_index(idx)
    live_keys = {(s.source_id, s.target_id, s.relation) for s in store.all() if s.relation != "related_to"}

    rem_data = json.loads(AGG_FILE.read_text(encoding="utf-8"))
    for s in rem_data["samples"]:
        key = (s["source_id"], s["target_id"], s["relation"])
        assert key in live_keys, f"remaining sample {s['index']} ({key}) not found in live graph"
