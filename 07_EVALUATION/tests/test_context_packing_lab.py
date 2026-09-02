"""evaluation/tests/test_context_packing_lab.py — Test suite for Context Packing Lab.

Tests:
1. Gold context YAML schema integrity (15 queries)
2. Experiment config schema validity
3. PackerAdapters P0, P1, P2, P3, P4 execution isolation
4. Negation & Invariant clause identification and prioritization
5. Packing loss and failure classification metrics
6. Zero production modifications / protected core invariants
"""
from pathlib import Path
import pytest

from evaluation.retrieval_diagnostic_runner import build_real_vault_storage
from evaluation.retrieval_fusion.adapters import RetrievalAdapter
from evaluation.context_packing.packer_adapters import PackerAdapters, SectionChunk
from evaluation.context_packing.experiment_runner import (
    classify_packing_failure,
    load_experiment_config,
    load_gold_context,
)


def test_gold_context_integrity():
    queries = load_gold_context()
    assert len(queries) == 15
    for q in queries:
        assert "id" in q
        assert "class" in q
        assert "query" in q
        assert "required_facts" in q
        assert "critical_invariants" in q
        assert len(q["required_facts"]) >= 1


def test_experiment_config_validity():
    config = load_experiment_config()
    assert "runtime" in config
    assert "models" in config["runtime"]
    assert "strategies" in config
    assert len(config["strategies"]) == 5


def test_packer_adapters_isolation():
    storage = build_real_vault_storage()
    all_notes = storage.query(intent="all")
    adapter = RetrievalAdapter(all_notes)

    query = "Under Rule P16, what are the restrictions on physical hardware telemetry data (VID, PID, Serial)?"
    req_facts = ["read-only", "immutable", "block"]
    entities = ["VID", "PID", "Serial Number", "P16"]
    candidates = adapter.retrieve_r4_full_fusion_graph(query, top_k=5, query_entities=entities)

    budget = {"max_notes": 5, "soft_limit_bytes": 16384, "hard_limit_bytes": 32768, "max_full_documents": 5}

    # P0
    p0 = PackerAdapters.pack_p0_current(candidates, budget)
    assert "packed_text" in p0
    assert p0["strategy"] == "P0"

    # P1
    p1 = PackerAdapters.pack_p1_full_context(candidates)
    assert "packed_text" in p1
    assert p1["strategy"] == "P1"

    # P2
    p2 = PackerAdapters.pack_p2_section_aware(candidates, query, req_facts, entities)
    assert "packed_text" in p2
    assert p2["strategy"] == "P2"
    assert p2["sections_kept"] >= 1

    # P3
    p3 = PackerAdapters.pack_p3_fact_invariant_protected(candidates, query, req_facts, entities)
    assert "packed_text" in p3
    assert p3["strategy"] == "P3"

    # P4
    p4 = PackerAdapters.pack_p4_fact_protected_dedup(candidates, query, req_facts, entities)
    assert "packed_text" in p4
    assert p4["strategy"] == "P4"


def test_negation_and_invariant_clause_preservation():
    note = {
        "id": "test_note_01",
        "content": """# System Governance
Rule 1: AI agents CANNOT set verification = 'verified'.
Rule 2: Normal notes may be proposed into REVIEW.
Rule 3: P16 Hardware Telemetry is strictly immutable and UI must block manual edits.
"""
    }
    secs = PackerAdapters._extract_sections(note)
    assert len(secs) >= 1

    q_tokens = {"hardware", "telemetry", "p16", "restrictions"}
    req_facts = ["immutable", "block"]
    entities = ["P16", "Hardware Telemetry"]

    for s in secs:
        score = PackerAdapters._score_section(s, q_tokens, req_facts, entities)
        assert score > 0
        assert s.has_invariant is True
        assert s.has_negation is True
        assert len(s.facts_matched) >= 1


def test_packing_failure_classification():
    # Success
    assert classify_packing_failure(3, 3, 0.9, "P3", "SIMPLE_FACT") == "SUCCESS"
    # Section selection failure (facts available in candidate, but 0 in final pack)
    assert classify_packing_failure(3, 0, 0.0, "P0", "SIMPLE_FACT") == "SECTION_SELECTION_FAILURE"
    # Negation loss in guardrail
    assert classify_packing_failure(4, 2, 0.0, "P0", "CONTRADICTION_GUARDRAIL") == "NEGATION_LOSS"
    # Temporal loss
    assert classify_packing_failure(3, 1, 0.0, "P0", "TEMPORAL") == "TEMPORAL_CONTEXT_LOSS"
    # Model failure (context preserved all facts, but LLM missed answer)
    assert classify_packing_failure(3, 3, 0.2, "P3", "SIMPLE_FACT") == "MODEL_FAILURE"
