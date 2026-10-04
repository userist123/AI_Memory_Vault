"""Tests for Book-to-Memory End-to-End Pipeline & Pilot Validation (Phase 7).

Validates all contracts under:
- 00_GOVERNANCE/rules/POLICY-LEARNING-QUALITY-02.md
- 03_IMPLEMENTATION/packages/lifecycle/validation/book_to_memory_pipeline.py
"""
import pytest
from security.authorizer import Principal
from lifecycle.validation.book_to_memory_schema import (
    BookToMemoryType,
    EpistemicType,
    ConflictSeverity,
    ConflictStatus,
    BookToMemoryValidationError,
    ProvenanceGateError,
    SecurityInjectionError,
)
from lifecycle.validation.book_to_memory_lifecycle import (
    BookToMemoryLifecycleState,
    OwnerApprovalToken,
    issue_owner_approval,
)
from lifecycle.validation.book_to_memory_conflict import (
    ConflictRegistry,
    ConflictRecord,
)
from lifecycle.validation.book_to_memory_usage_test import (
    TaskSpecification,
    EvaluationAttempt,
    RubricDimension,
    UsageTestStatus,
)
from lifecycle.validation.book_to_memory_pipeline import (
    BookToMemoryPipeline,
    PipelineStage,
    BookIngestionAuditReport,
)


@pytest.fixture
def clean_pipeline():
    return BookToMemoryPipeline()


@pytest.fixture
def valid_concept_note():
    return {
        "id": "NOTE-heuristics-sys1-sys2",
        "type": BookToMemoryType.CONCEPT.value,
        "title": "Dual-Process Cognitive Architecture: System 1 and System 2",
        "atomic_concept": "Cognition operates via two distinct modes: System 1 is fast, autonomous, and heuristic; System 2 is slow, deliberative, and effortful.",
        "evidence": "Extensive empirical experiments in bat-and-ball problem show 80%+ error rate when System 2 is not explicitly engaged.",
        "source_title": "Thinking, Fast and Slow",
        "chapter": "Part 1: Two Systems",
        "page_range": "19-38",
        "exact_page": 21,
        "lifecycle": "RAW",
        "tags": ["cognitive_science", "dual_process", "heuristics"],
    }


@pytest.fixture
def valid_task():
    return TaskSpecification(
        task_id="TASK-heuristics-001",
        title="Agent Cognitive Load Allocation",
        description="Design a query triage gate that routes reflexive queries to fast-path heuristics and complex reasoning to deliberative planning.",
        task_type="application",
        expected_criteria={
            "identifies_dual_mode": True,
            "protects_deliberative_budget": True,
        },
    )


# =============================================================================
# 1. Book Map Registration Tests (Policy-02 Section 2)
# =============================================================================

def test_pipeline_register_book_map_success(clean_pipeline):
    book_map = clean_pipeline.register_book_map(
        source_identity="thinking-fast-slow-2011",
        title="Thinking, Fast and Slow",
        authors=["Daniel Kahneman"],
        chapter_coverage={
            "Part 1: Two Systems": ["System 1 & 2", "Attention & Effort"],
            "Part 2: Heuristics & Biases": ["Anchoring", "Availability"],
        },
        processing_status="in_progress",
        edition="1st Edition, Farrar, Straus and Giroux",
        linked_problems=["AI Reasoning Budgets", "Heuristic Prompt Triage"],
    )
    assert book_map["id"] == "map-thinking-fast-slow-2011"
    assert book_map["type"] == BookToMemoryType.BOOK_MAP.value
    assert clean_pipeline.get_book_map("thinking-fast-slow-2011") is not None


def test_pipeline_register_book_map_invalid_missing_fields(clean_pipeline):
    with pytest.raises(BookToMemoryValidationError):
        clean_pipeline.register_book_map(
            source_identity="bad-map",
            title="Incomplete Book",
            authors=[],  # Authors empty violates schema
            chapter_coverage={},
        )


# =============================================================================
# 2. End-to-End Pipeline Happy Path (RAW -> UNVERIFIED -> VERIFIED)
# =============================================================================

def test_pipeline_e2e_happy_path_stops_at_verified_without_owner_token(
    clean_pipeline, valid_concept_note, valid_task
):
    report: BookIngestionAuditReport = clean_pipeline.process_candidate_note(
        note=valid_concept_note,
        task_spec=valid_task,
        caller_principal=Principal.AI_AGENT,
        evaluator_principal=Principal.HUMAN,
    )
    assert report.initial_lifecycle == "RAW"
    assert report.final_lifecycle == "VERIFIED"
    assert report.usage_test_score == 10
    assert report.ablation_delta > 0.0
    assert report.retrieval_ready is True
    assert len(report.integrity_digest) == 64

    # Verify all passed stages
    stage_names = [s.stage for s in report.stage_results if s.passed]
    assert PipelineStage.SCHEMA_AND_SECURITY in stage_names
    assert PipelineStage.PROMOTION_TO_UNVERIFIED in stage_names
    assert PipelineStage.CONFLICT_CROSS_CHECK in stage_names
    assert PipelineStage.USAGE_TEST in stage_names
    assert PipelineStage.ABLATION_TEST in stage_names
    assert PipelineStage.PROMOTION_TO_VERIFIED in stage_names
    assert PipelineStage.RETRIEVAL_VERIFICATION in stage_names
    assert PipelineStage.OWNER_ATTESTATION in stage_names


# =============================================================================
# 3. Owner Attestation & Promotion to ACTIVE (GATE-08)
# =============================================================================

def test_pipeline_promotes_to_active_with_valid_owner_token(
    clean_pipeline, valid_concept_note, valid_task
):
    owner_token = issue_owner_approval(
        actor=Principal.HUMAN,
        note_id=valid_concept_note["id"],
        reason="Verified empirical concept with robust ablation delta.",
    )

    report = clean_pipeline.process_candidate_note(
        note=valid_concept_note,
        task_spec=valid_task,
        owner_approval_token=owner_token,
        caller_principal=Principal.AI_AGENT,
        evaluator_principal=Principal.HUMAN,
    )
    assert report.final_lifecycle == "ACTIVE"
    attestation_stage = [s for s in report.stage_results if s.stage == PipelineStage.OWNER_ATTESTATION][0]
    assert attestation_stage.passed is True
    assert attestation_stage.details.get("new_lifecycle") == "ACTIVE"


def test_pipeline_blocks_active_with_tampered_owner_token(
    clean_pipeline, valid_concept_note, valid_task
):
    owner_token = issue_owner_approval(
        actor=Principal.HUMAN,
        note_id=valid_concept_note["id"],
    )
    # Tamper token signature
    owner_token.signature = "0" * 64

    report = clean_pipeline.process_candidate_note(
        note=valid_concept_note,
        task_spec=valid_task,
        owner_approval_token=owner_token,
        caller_principal=Principal.AI_AGENT,
        evaluator_principal=Principal.HUMAN,
    )
    assert report.final_lifecycle == "VERIFIED"  # Kept in VERIFIED, cannot transition to ACTIVE
    attestation_stage = [s for s in report.stage_results if s.stage == PipelineStage.OWNER_ATTESTATION][0]
    assert attestation_stage.passed is False
    assert "Signature verification failed" in attestation_stage.error


# =============================================================================
# 4. Security & Untrusted Input Isolation (GATE-03)
# =============================================================================

def test_pipeline_rejects_adversarial_prompt_injection(clean_pipeline, valid_concept_note, valid_task):
    malicious_note = dict(valid_concept_note)
    malicious_note["tool_call"] = "execute_sql('DROP TABLE notes')"

    report = clean_pipeline.process_candidate_note(
        note=malicious_note,
        task_spec=valid_task,
    )
    assert report.final_lifecycle == "RAW"
    sec_stage = [s for s in report.stage_results if s.stage == PipelineStage.SCHEMA_AND_SECURITY][0]
    assert sec_stage.passed is False
    assert "Prohibited executable or privileged directive" in sec_stage.error


def test_pipeline_rejects_missing_provenance(clean_pipeline, valid_concept_note, valid_task):
    unprovenanced_note = dict(valid_concept_note)
    del unprovenanced_note["source_title"]
    del unprovenanced_note["page_range"]

    report = clean_pipeline.process_candidate_note(
        note=unprovenanced_note,
        task_spec=valid_task,
    )
    assert report.final_lifecycle == "RAW"
    sec_stage = [s for s in report.stage_results if s.stage == PipelineStage.SCHEMA_AND_SECURITY][0]
    assert sec_stage.passed is False


# =============================================================================
# 5. Conflict Gate Invariant (GATE-06: Open HIGH conflict blocks ACTIVE)
# =============================================================================

def test_pipeline_open_high_conflict_blocks_active_even_with_token(
    clean_pipeline, valid_concept_note, valid_task
):
    conflict_payload = {
        "domain": "heuristics",
        "title": "Dual Process vs Ecological Rationality",
        "claim_a": "Heuristics produce systematic cognitive biases",
        "source_a": {
            "source_title": "Thinking, Fast and Slow",
            "chapter": "Part 1: Two Systems",
            "page_range": "19-38",
        },
        "evidence_a": "Lab experiments demonstrate anchoring and base rate neglect",
        "claim_b": "Fast and frugal heuristics match or exceed complex models in real-world environments",
        "source_b": {
            "source_title": "Gut Feelings: The Intelligence of the Unconscious",
            "chapter": "Chapter 2",
            "page_range": "40-65",
        },
        "evidence_b": "Field studies in emergency medicine show take-the-best heuristic outperforms regression",
        "severity": ConflictSeverity.HIGH.value,
    }
    clean_pipeline.conflict_registry.register_conflict(conflict_payload, actor=Principal.HUMAN)

    owner_token = issue_owner_approval(
        actor=Principal.HUMAN,
        note_id=valid_concept_note["id"],
    )

    report = clean_pipeline.process_candidate_note(
        note=valid_concept_note,
        task_spec=valid_task,
        owner_approval_token=owner_token,
    )
    # Stays in VERIFIED, blocked from ACTIVE
    assert report.final_lifecycle == "VERIFIED"
    attestation_stage = [s for s in report.stage_results if s.stage == PipelineStage.OWNER_ATTESTATION][0]
    assert attestation_stage.passed is False
    assert "Open HIGH conflict blocks promotion to ACTIVE" in attestation_stage.error


# =============================================================================
# 6. Usage Test Failure Gate (Score < 8 stops at UNVERIFIED)
# =============================================================================

def test_pipeline_failing_usage_test_blocks_verified(
    clean_pipeline, valid_concept_note, valid_task
):
    failing_rubric = {
        RubricDimension.CORECTITUDINE.value: 1,
        RubricDimension.COMPLETITUDINE.value: 1,
        RubricDimension.FARA_GHICIT.value: 1,
        RubricDimension.FARA_SURSE_EXTERNE.value: 2,
        RubricDimension.REPRODUCTIBILITATE.value: 1,
    }  # Total score = 6 / 10 (< 8)

    report = clean_pipeline.process_candidate_note(
        note=valid_concept_note,
        task_spec=valid_task,
        rubric=failing_rubric,
    )
    assert report.final_lifecycle == "UNVERIFIED"
    usage_stage = [s for s in report.stage_results if s.stage == PipelineStage.USAGE_TEST][0]
    assert usage_stage.passed is False
    assert "Score 6/10" in usage_stage.error or "below required threshold" in usage_stage.error


# =============================================================================
# 7. Ablation Negative Delta Gate (Delta < 0 stops at UNVERIFIED)
# =============================================================================

def test_pipeline_negative_ablation_delta_blocks_verified(
    clean_pipeline, valid_concept_note, valid_task
):
    # Simulate regression in WITH_NOTE: score is lower with note than without note
    regression_data = {
        f"model_primary:{r}:WITH_NOTE": {
            "answer": "Degraded answer with note",
            "evidence": "Degraded evidence",
            "rubric": {d.value: 1 for d in RubricDimension},  # score 5
        }
        for r in range(1, 4)
    }
    regression_data.update({
        f"model_secondary:{r}:WITH_NOTE": {
            "answer": "Degraded answer with note",
            "evidence": "Degraded evidence",
            "rubric": {d.value: 1 for d in RubricDimension},  # score 5
        }
        for r in range(1, 4)
    })
    # WITHOUT_NOTE performs better (score 8)
    for m in ["model_primary", "model_secondary"]:
        for r in range(1, 4):
            regression_data[f"{m}:{r}:WITHOUT_NOTE"] = {
                "answer": "Clean baseline without note",
                "evidence": "Clean baseline evidence",
                "rubric": {
                    RubricDimension.CORECTITUDINE.value: 2,
                    RubricDimension.COMPLETITUDINE.value: 2,
                    RubricDimension.FARA_GHICIT.value: 1,
                    RubricDimension.FARA_SURSE_EXTERNE.value: 2,
                    RubricDimension.REPRODUCTIBILITATE.value: 1,
                },
            }

    report = clean_pipeline.process_candidate_note(
        note=valid_concept_note,
        task_spec=valid_task,
        ablation_trial_data=regression_data,
    )
    assert report.final_lifecycle == "UNVERIFIED"
    ablation_stage = [s for s in report.stage_results if s.stage == PipelineStage.ABLATION_TEST][0]
    assert ablation_stage.passed is False
    assert "negative (performance regression)" in ablation_stage.error


# =============================================================================
# 8. Pilot Ingestions from Inbox Corpus
# =============================================================================

def test_pipeline_pilot_kahneman_thinking_fast_and_slow(clean_pipeline):
    # 1. Register Book Map
    clean_pipeline.register_book_map(
        source_identity="kahneman-tfs-2011",
        title="Thinking, Fast and Slow",
        authors=["Daniel Kahneman"],
        chapter_coverage={
            "Part 1: Two Systems": ["System 1 & 2", "Attention & Effort"],
            "Part 2: Heuristics & Biases": ["Anchoring", "Availability"],
            "Part 3: Overconfidence": ["Illusion of Understanding"],
            "Part 4: Choices": ["Prospect Theory", "Loss Aversion"],
            "Part 5: Two Selves": ["Experienced vs Remembering Self"],
        },
        processing_status="completed",
        edition="1st Edition, Farrar, Straus and Giroux 2011",
    )

    # 2. Pilot Concept Note
    pilot_note = {
        "id": "NOTE-kahneman-loss-aversion-2011",
        "type": BookToMemoryType.CONCEPT.value,
        "title": "Loss Aversion Asymmetry (Prospect Theory)",
        "atomic_concept": "Losses loom larger than corresponding gains by a psychological factor of roughly 1.5 to 2.5.",
        "evidence": "Choice experiments between 50/50 chance to lose $100 or gain $X require X ~ $200 for acceptance.",
        "source_title": "Thinking, Fast and Slow",
        "chapter": "Part 4: Choices, Chapter 26: Prospect Theory",
        "page_range": "278-299",
        "exact_page": 284,
        "lifecycle": "RAW",
        "tags": ["decision_theory", "loss_aversion", "behavioral_economics"],
    }

    pilot_task = TaskSpecification(
        task_id="TASK-pilot-loss-001",
        title="Risk-Adjusted Trade Sizing Strategy",
        description="Calculate trade sizing adjustments for asymmetric downside penalty.",
        task_type="application",
        expected_criteria={"applies_loss_aversion_ratio": True},
    )

    report = clean_pipeline.process_candidate_note(
        note=pilot_note,
        task_spec=pilot_task,
        caller_principal=Principal.AI_AGENT,
        evaluator_principal=Principal.HUMAN,
    )
    assert report.final_lifecycle == "VERIFIED"
    assert report.usage_test_score == 10
    assert report.ablation_delta > 0.0
    assert report.retrieval_ready is True


def test_pipeline_pilot_ashby_design_for_a_brain(clean_pipeline):
    # 1. Register Book Map
    clean_pipeline.register_book_map(
        source_identity="ashby-dfb-1952",
        title="Design for a Brain",
        authors=["W. Ross Ashby"],
        chapter_coverage={
            "Chapter 1: The Problem": ["Homeostasis", "Adaptive Behavior"],
            "Chapter 2: Dynamic Systems": ["Variables", "Equilibrium"],
            "Chapter 7: The Ultrastable System": ["Step-mechanisms", "Essential Variables"],
        },
        processing_status="completed",
        edition="Chapman & Hall 1952",
    )

    # 2. Pilot Concept Note
    ashby_note = {
        "id": "NOTE-ashby-ultrastability-1952",
        "type": BookToMemoryType.CONCEPT.value,
        "title": "Ultrastability in Cybernetic Feedback Systems",
        "atomic_concept": "An ultrastable system utilizes secondary feedback step-mechanisms to alter internal parameter configurations whenever essential variables exceed physiological limits.",
        "evidence": "Homeostat experiment demonstrated automatic restabilization across 391 trials following arbitrary wiring reversals.",
        "source_title": "Design for a Brain",
        "chapter": "Chapter 7: The Ultrastable System",
        "page_range": "80-102",
        "exact_page": 93,
        "lifecycle": "RAW",
        "tags": ["cybernetics", "ultrastability", "homeostasis", "adaptive_control"],
    }

    ashby_task = TaskSpecification(
        task_id="TASK-pilot-ashby-001",
        title="Self-Healing Cognitive Budget Controller",
        description="Design a secondary step-mechanism that reconfigures context window budget allocation when token exhaustion threshold is crossed.",
        task_type="application",
        expected_criteria={"identifies_step_mechanism": True, "protects_essential_variable": True},
    )

    report = clean_pipeline.process_candidate_note(
        note=ashby_note,
        task_spec=ashby_task,
        caller_principal=Principal.AI_AGENT,
        evaluator_principal=Principal.HUMAN,
    )
    assert report.final_lifecycle == "VERIFIED"
    assert report.usage_test_score == 10
    assert report.ablation_delta > 0.0
    assert report.retrieval_ready is True


# =============================================================================
# 9. Extended Governance & Audit Tests
# =============================================================================

def test_pipeline_biological_epistemic_chain_enforcement(clean_pipeline, valid_task):
    bio_note = {
        "id": "NOTE-bio-synaptic-plasticity",
        "type": BookToMemoryType.CONCEPT.value,
        "title": "Hebbian Synaptic Plasticity",
        "atomic_concept": "Neurons that fire together wire together through long-term potentiation.",
        "evidence": "Observed EPSP potentiation in hippocampal slice preparations.",
        "source_title": "The Organization of Behavior",
        "chapter": "Chapter 4",
        "page_range": "60-75",
        "lifecycle": "RAW",
        "tags": ["biological", "neuroscience", "plasticity"],
        "epistemic_type": EpistemicType.ENGINEERING_MECHANISM.value,
        # Missing cognitive_chain for biological mechanism claim
    }
    report = clean_pipeline.process_candidate_note(
        note=bio_note,
        task_spec=valid_task,
    )
    assert report.final_lifecycle == "RAW"
    sec_stage = [s for s in report.stage_results if s.stage == PipelineStage.SCHEMA_AND_SECURITY][0]
    assert sec_stage.passed is False
    assert "Biological claim requires verified cognitive chain" in sec_stage.error


def test_pipeline_repro_test_processing(clean_pipeline):
    repro_note = {
        "id": "NOTE-repro-anchoring-001",
        "type": BookToMemoryType.REPRO_TEST.value,
        "title": "Empirical Replication of Wheel of Fortune Anchoring Effect",
        "hypothesis_claim": "Arbitrary numerical anchors bias subsequent quantitative estimates.",
        "test_procedure": "Present subjects with random number 10 or 65, then ask for percentage of African nations in UN.",
        "inputs": {"anchor_low": 10, "anchor_high": 65},
        "expected_result": "Mean estimate for anchor 10 is ~25%; for anchor 65 is ~45%.",
        "actual_result": "Replication produced 26% and 47% respectively.",
        "reproducibility_status": "reproduced",
        "source_evidence": "Thinking, Fast and Slow, Chapter 11, page 119.",
        "source_title": "Thinking, Fast and Slow",
        "chapter": "Chapter 11: Anchors",
        "page_range": "119-128",
        "lifecycle": "RAW",
        "tags": ["replication", "heuristics", "anchoring"],
    }
    task = TaskSpecification(
        task_id="TASK-repro-001",
        title="Anchoring Mitigation in Estimation Tasks",
        description="Design a prompting procedure to neutralize anchoring bias.",
        task_type="application",
        expected_criteria={"identifies_anchor_mechanism": True},
    )
    report = clean_pipeline.process_candidate_note(
        note=repro_note,
        task_spec=task,
    )
    assert report.final_lifecycle == "VERIFIED"
    assert report.usage_test_score == 10
    assert report.retrieval_ready is True


def test_pipeline_audit_report_serialization_and_integrity(clean_pipeline, valid_concept_note, valid_task):
    report = clean_pipeline.process_candidate_note(
        note=valid_concept_note,
        task_spec=valid_task,
    )
    d = report.to_dict()
    assert isinstance(d, dict)
    assert d["book_title"] == "Thinking, Fast and Slow"
    assert d["final_lifecycle"] == "VERIFIED"
    assert len(d["integrity_digest"]) == 64
    assert len(d["stage_results"]) == 8


def test_pipeline_retrieval_negative_query_discrimination(clean_pipeline, valid_concept_note, valid_task):
    report = clean_pipeline.process_candidate_note(
        note=valid_concept_note,
        task_spec=valid_task,
    )
    assert report.retrieval_ready is True
    # Test negative retrieval on the note
    irrelevant_query = "Quantum electrodynamics Feynman path integrals"
    score = clean_pipeline.retrieval_validator.score_relevance(irrelevant_query, valid_concept_note)
    assert score == 0.0
    assert clean_pipeline.retrieval_validator.verify_negative_retrieval(irrelevant_query, valid_concept_note) is True

