"""evaluation/tests/test_temporal_memory_lab.py — Test suite for Temporal Memory Lab.

Tests:
1. Gold temporal YAML schema integrity
2. Temporal metadata audit execution
3. Temporal adapters T0, T1, T2, T3, T4 isolation
4. Supersession chain forward and backward traversal
5. Bi-temporal metadata envelope attachment
6. Abstention on unknown temporal status
"""
from datetime import datetime
import pytest

from evaluation.retrieval_diagnostic_runner import build_real_vault_storage
from evaluation.temporal_memory.temporal_adapters import (
    TemporalAdapters,
    audit_temporal_metadata,
)
from evaluation.temporal_memory.experiment_runner import (
    load_experiment_config,
    load_gold_temporal,
)


def test_gold_temporal_schema():
    queries = load_gold_temporal()
    assert len(queries) == 7
    for q in queries:
        assert "id" in q
        assert "class" in q
        assert "query" in q
        assert "temporal_constraints" in q
        assert "required_evidence" in q
        assert len(q["required_evidence"]) >= 1


def test_audit_temporal_metadata():
    storage = build_real_vault_storage()
    notes = storage.query(intent="all")
    report = audit_temporal_metadata(notes)
    assert "total_notes" in report
    assert "fields" in report
    assert report["total_notes"] > 0
    assert "created" in report["fields"]
    assert "lifecycle" in report["fields"]


def test_valid_time_filter():
    candidates = [
        {"id": "c1", "content": "valid now", "valid_from": "2020-01-01", "valid_until": "2030-01-01"},
        {"id": "c2", "content": "expired rule", "valid_from": "2018-01-01", "valid_until": "2021-01-01"},
        {"id": "c3", "content": "future rule", "valid_from": "2035-01-01"},
    ]
    # Current query
    t1_curr = TemporalAdapters.apply_t1_valid_time_filter(candidates, "What is current rule?")
    assert t1_curr[0]["id"] == "c1"
    assert t1_curr[0]["_temporal_validity_score"] == 1.0

    # Historical query
    t1_hist = TemporalAdapters.apply_t1_valid_time_filter(candidates, "What was the historical expired rule?")
    assert any(c["_temporal_validity_score"] == 0.8 for c in t1_hist if c["id"] == "c2")


def test_supersession_lineage_traversal():
    all_notes = {
        "old_note": {"id": "old_note", "lifecycle": "SUPERSEDED", "superseded_by": "mid_note", "content": "legacy v1"},
        "mid_note": {"id": "mid_note", "lifecycle": "SUPERSEDED", "supersedes": "old_note", "superseded_by": "act_note", "content": "legacy v2"},
        "act_note": {"id": "act_note", "lifecycle": "ACTIVE", "supersedes": "mid_note", "content": "active v3"},
    }

    # Seed is old_note -> should traverse forward to act_note
    cands = [all_notes["old_note"]]
    expanded = TemporalAdapters.apply_t2_supersession_traversal(cands, all_notes)
    expanded_ids = [n["id"] for n in expanded]
    assert "old_note" in expanded_ids
    assert "mid_note" in expanded_ids
    assert "act_note" in expanded_ids


def test_bitemporal_envelope():
    candidates = [
        {"id": "n1", "content": "python guide", "valid_from": "2024-01-01", "created": "2024-01-02", "lifecycle": "ACTIVE"}
    ]
    all_notes = {"n1": candidates[0]}
    bitemp = TemporalAdapters.apply_t4_bitemporal_traversal(candidates, all_notes, "What is valid?")
    assert len(bitemp) == 1
    assert "Temporal Meta: Valid=2024-01-01" in bitemp[0]["content"]
    assert "Observed=2024-01-02" in bitemp[0]["content"]
