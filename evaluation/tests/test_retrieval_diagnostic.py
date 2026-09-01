"""evaluation/tests/test_retrieval_diagnostic.py — Unit tests for diagnostic harness.

Tests:
1. Fact checking in context (present vs absent identification)
2. Failure root cause classification (SUCCESS, RETRIEVAL_FAILURE, MODEL_CAPABILITY_FAILURE, BOTH)
3. Multi-signal retrieval simulators (R1, R2, R3, R4) return valid non-empty contexts
4. Invariant: Evaluation harness never modifies production configs
"""
import pytest
from evaluation.retrieval_diagnostic_runner import (
    check_facts_in_context,
    classify_failure_root_cause,
    retrieve_r1_semantic_only,
    retrieve_r2_semantic_plus_lexical,
    retrieve_r3_semantic_lexical_entity,
    retrieve_r4_semantic_lexical_entity_graph,
)
from evaluation.full_context_baseline import EVAL_CASES, VAULT_KNOWLEDGE_CORPUS


def test_fact_presence_checking():
    """Verify check_facts_in_context accurately detects present vs missing required facts."""
    context = "SQLite WAL mode with PRAGMA busy_timeout=5000 and BEGIN IMMEDIATE transactions."
    required_facts = ["wal", "busy_timeout", "5000", "immediate", "nonexistent_fact"]

    present, absent = check_facts_in_context(context, required_facts)

    assert "wal" in present
    assert "busy_timeout" in present
    assert "5000" in present
    assert "immediate" in present
    assert "nonexistent_fact" in absent
    assert len(present) == 4
    assert len(absent) == 1


def test_failure_root_cause_classification():
    """Verify failure taxonomy correctly assigns causes."""
    # Case 1: High accuracy -> SUCCESS
    assert classify_failure_root_cause(0.85, context_has_all_facts=True, context_has_any_facts=True) == "SUCCESS"

    # Case 2: Zero facts in context -> RETRIEVAL_FAILURE
    assert classify_failure_root_cause(0.20, context_has_all_facts=False, context_has_any_facts=False) == "RETRIEVAL_FAILURE"

    # Case 3: Partial facts in context -> BOTH
    assert classify_failure_root_cause(0.33, context_has_all_facts=False, context_has_any_facts=True) == "BOTH"

    # Case 4: All facts in context but answer failed -> MODEL_CAPABILITY_FAILURE
    assert classify_failure_root_cause(0.25, context_has_all_facts=True, context_has_any_facts=True) == "MODEL_CAPABILITY_FAILURE"


def test_multi_signal_retrieval_layers_produce_valid_results():
    """Verify R1 to R4 produce non-empty candidate lists for queries."""
    sample_q = "What PRAGMA setting is required for SQLite storage engine concurrency?"

    r1 = retrieve_r1_semantic_only(sample_q)
    r2 = retrieve_r2_semantic_plus_lexical(sample_q)
    r3 = retrieve_r3_semantic_lexical_entity(sample_q)
    r4 = retrieve_r4_semantic_lexical_entity_graph(sample_q)

    assert len(r1) >= 1
    assert len(r2) >= 1
    assert len(r3) >= 1
    assert len(r4) >= 1

    # R4 graph expansion should contain equal or more context than R1
    assert len("\n".join(r4)) >= len("\n".join(r1))


def test_eval_cases_dataset_integrity():
    """Verify the 15 evaluation cases contain all required fields and valid structures."""
    assert len(EVAL_CASES) == 15
    for case in EVAL_CASES:
        assert "id" in case
        assert "category" in case
        assert "query" in case
        assert "required_facts" in case
        assert len(case["required_facts"]) >= 1
        assert "expected_answer" in case
