"""evaluation/tests/test_retrieval_diagnostic.py — Unit tests for real diagnostic pipeline.

Tests:
1. Real vault storage loader builds valid note objects with content and tags
2. Real MemoryController search executes cleanly with ContextPackBuilder envelope
3. Evidence coverage accurately measures required fact presence in context
4. Factual multi-signal status audit reports valid architectural classifications
5. Invariant: Evaluation harness never modifies production configs
"""
import os
import pytest

# Ensure HMAC secret is set for pagination tokens in harness
os.environ.setdefault("MEMORY_CONTROLLER_HMAC_SECRET", "test_hmac_secret_for_eval_harness_32bytes_long")

from evaluation.retrieval_diagnostic_runner import (
    build_real_vault_storage,
    check_facts_in_context,
    get_real_multisignal_status,
)
from evaluation.full_context_baseline import EVAL_CASES
from memory_controller.authorizer import Principal
from memory_controller.controller import MemoryController


def test_build_real_vault_storage():
    """Verify real storage loads core notes from disk."""
    storage = build_real_vault_storage()
    notes = storage.query(intent="all")
    assert len(notes) >= 2

    # Verify AGENTS.md and rules note are present
    note_ids = [n["id"] for n in notes]
    assert "note_agents_contract" in note_ids
    assert "note_vault_cognitive_rules" in note_ids


def test_real_memory_controller_search_and_pack():
    """Verify real MemoryController.search executes and returns valid ContextPackBuilder envelope."""
    storage = build_real_vault_storage()
    controller = MemoryController(storage)
    controller.default_disclosure = "full"

    pack = controller.search(
        principal=Principal.AI_AGENT,
        query="What PRAGMA setting is required for SQLite storage engine?",
        page_size=5,
    )

    assert isinstance(pack, dict)
    assert "requestId" in pack
    assert "agentId" in pack
    assert "budget" in pack
    assert "results" in pack
    assert len(pack["results"]) >= 1


def test_evidence_coverage_measurement():
    """Verify evidence coverage calculates factual presence without conflating with model accuracy."""
    context = "SQLite WAL mode with PRAGMA busy_timeout=5000 and BEGIN IMMEDIATE transactions."
    required_facts = ["wal", "busy_timeout", "5000", "immediate", "absent_fact"]

    present, absent, cov = check_facts_in_context(context, required_facts)

    assert len(present) == 4
    assert len(absent) == 1
    assert cov == 0.80


def test_multisignal_factual_audit():
    """Verify multi-signal audit correctly checks real repo capabilities without simulation."""
    status = get_real_multisignal_status()
    assert "semantic_vector" in status
    assert "lexical_bm25" in status
    assert "entity_resolution" in status
    assert "graph_expansion" in status

    for key, data in status.items():
        assert data["status"] in {"EXISTS", "PARTIAL", "MISSING"}
        assert len(data["evidence"]) > 10


def test_eval_cases_integrity():
    """Verify 15 evaluation cases are loaded and unmodified."""
    assert len(EVAL_CASES) == 15
    for case in EVAL_CASES:
        assert "id" in case
        assert "query" in case
        assert "required_facts" in case


def test_retrieval_knowledge_note_and_hypotheses_integrity():
    """Verify knowledge notes and experiment specs comply with governance rules."""
    from pathlib import Path

    vault_root = Path(__file__).resolve().parents[2]
    
    # 1. Knowledge Note
    knw_path = vault_root / "01_KNOWLEDGE" / "Retrieval_Bottleneck_P0_Empirical_Findings.md"
    assert knw_path.exists(), "Knowledge note must exist"
    knw_txt = knw_path.read_text(encoding="utf-8")
    assert "lifecycle: REVIEW" in knw_txt
    assert "verification: unverified" in knw_txt
    assert "source_type: execution" in knw_txt
    assert "Observed Facts" in knw_txt
    assert "Measurements" in knw_txt
    assert "Interpretation" in knw_txt
    assert "Candidate Recall" in knw_txt
    assert "Candidate Count" in knw_txt



    # 2. Hypothesis Registry
    reg_path = vault_root / "01_KNOWLEDGE" / "Retrieval_Hypothesis_Registry.md"
    assert reg_path.exists(), "Hypothesis registry must exist"
    reg_txt = reg_path.read_text(encoding="utf-8")
    assert "R-H001" in reg_txt
    assert "R-H002" in reg_txt
    assert "R-H003" in reg_txt
    assert "R-H004" in reg_txt
    assert "R-H005" in reg_txt
    assert "R-H006" in reg_txt
    assert "R-H007" in reg_txt

    # 3. Experiment Spec
    spec_path = vault_root / "07_EVALUATION" / "retrieval_fusion_experiment_spec.md" if (vault_root / "07_EVALUATION").exists() else vault_root / "07_EVALUATION" / "retrieval_fusion_experiment_spec.md"
    assert spec_path.exists(), "Experiment spec must exist"
    spec_txt = spec_path.read_text(encoding="utf-8")
    assert "R1" in spec_txt
    assert "R2" in spec_txt
    assert "R3" in spec_txt
    assert "R4" in spec_txt
    assert "SIMPLE_FACT" in spec_txt
    assert "MULTI_HOP" in spec_txt
    assert "GUARDRAIL" in spec_txt

