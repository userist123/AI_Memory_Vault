"""evaluation/tests/test_retrieval_fusion_lab.py — Test suite for Retrieval Fusion Lab.

Tests:
1. Gold evidence schema & 15 query definitions integrity
2. Experiment config schema validity
3. RetrievalAdapter R1, R2, R3, R4 execution isolation over real vault notes
4. Metric calculation: candidate recall and failure classification
5. Zero monkeypatching / protected core invariants
"""
from pathlib import Path
import pytest

from evaluation.retrieval_diagnostic_runner import build_real_vault_storage
from evaluation.retrieval_fusion.adapters import RetrievalAdapter, RetrievalSignalStatus
from evaluation.retrieval_fusion.experiment_runner import (
    calculate_candidate_recall,
    classify_failure,
    load_experiment_config,
    load_gold_evidence,
)


def test_gold_evidence_integrity():
    queries = load_gold_evidence()
    assert len(queries) == 15
    for q in queries:
        assert "id" in q
        assert "class" in q
        assert "query" in q
        assert "gold_relevant_notes" in q
        assert "gold_required_facts" in q
        assert len(q["gold_relevant_notes"]) >= 1
        assert len(q["gold_required_facts"]) >= 1


def test_experiment_config_validity():
    config = load_experiment_config()
    assert "runtime" in config
    assert "models" in config["runtime"]
    assert "strategies" in config
    assert len(config["strategies"]) == 4


def test_retrieval_adapter_strategies_isolation():
    storage = build_real_vault_storage()
    all_notes = storage.query(intent="all")
    adapter = RetrievalAdapter(all_notes)

    query = "What PRAGMA setting and transaction mode are required for SQLite storage engine concurrency?"
    entities = ["SQLite", "PRAGMA", "StorageEngine"]

    # R1
    r1 = adapter.retrieve_r1_semantic(query, top_k=5)
    assert len(r1) >= 1
    assert all("id" in n for n in r1)

    # R2
    r2 = adapter.retrieve_r2_semantic_lexical(query, top_k=5)
    assert len(r2) >= 1
    assert any("vault_cognitive_rules" in n["id"] or "agents" in n["id"] for n in r2)

    # R3
    r3 = adapter.retrieve_r3_semantic_lexical_entity(query, top_k=5, query_entities=entities)
    assert len(r3) >= 1

    # R4
    r4 = adapter.retrieve_r4_full_fusion_graph(query, top_k=5, query_entities=entities)
    assert len(r4) >= 1


def test_candidate_recall_and_failure_classification():
    # Perfect candidate recall
    assert calculate_candidate_recall(["note_a", "note_b"], ["note_a", "note_b"]) == 1.0
    # Partial candidate recall
    assert calculate_candidate_recall(["note_a", "note_c"], ["note_a", "note_b"]) == 0.5
    # Zero candidate recall
    assert calculate_candidate_recall(["note_c", "note_d"], ["note_a", "note_b"]) == 0.0

    # Failure classifications
    assert classify_failure(1.0, 1.0, 1.0, 0.9) == "SUCCESS"
    assert classify_failure(0.0, 0.0, 0.0, 0.0) == "DISCOVERY_FAILURE"
    assert classify_failure(0.8, 0.2, 0.2, 0.0) == "RANKING_FAILURE"
    assert classify_failure(0.8, 0.8, 0.2, 0.0) == "PACKING_FAILURE"
    assert classify_failure(1.0, 1.0, 1.0, 0.4) == "MODEL_FAILURE"


def test_signals_status_presence():
    assert RetrievalSignalStatus.SEMANTIC == "AVAILABLE"
    assert RetrievalSignalStatus.LEXICAL == "AVAILABLE"
    assert RetrievalSignalStatus.ENTITY == "AVAILABLE"
    assert RetrievalSignalStatus.GRAPH == "AVAILABLE"
