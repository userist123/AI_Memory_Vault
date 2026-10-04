"""Tests for Book-to-Memory Problem Matrix Traceability & Hypothesis Validation Engine (Phase 9).

Validates all contracts under:
- 00_GOVERNANCE/rules/POLICY-LEARNING-QUALITY-02.md (Section 14: Biological -> Mechanism)
- 08_RESEARCH/BOOK_TO_MEMORY/RESEARCH-TRACK-CONTRACT.md (Evidence Chain & Anti-Gaming)
- 08_RESEARCH/BOOK_TO_MEMORY/BOOK_TO_HYPOTHESIS_MAPPING.md
- 08_RESEARCH/BOOK_TO_MEMORY/PROBLEM_MATRIX.md
"""
import pytest
from security.authorizer import Principal
from lifecycle.validation.book_to_memory_schema import (
    BookToMemoryValidationError,
    SecurityInjectionError,
)
from lifecycle.validation.book_to_memory_hypothesis import (
    TrackState,
    CANONICAL_VAULT_PROBLEMS,
    HypothesisError,
    HypothesisStateTransitionError,
    AntiGamingViolationError,
    UnknownProblemError,
    HypothesisRecord,
    DecisionRecord,
    BookToMemoryHypothesisRegistry,
)


@pytest.fixture
def clean_registry():
    return BookToMemoryHypothesisRegistry()


@pytest.fixture
def sample_hypothesis_data():
    return {
        "hypothesis_id": "H1-BOOK-001",
        "problem_slug": "candidate_generation",
        "source_id": "laird_soar_cognitive_architecture",
        "source_location": "Chapter 2, pp. 29-45; Chapter 8-9, pp. 241-310",
        "principle": "Cognitive architectures require structural differentiation between working, semantic, and episodic memory.",
        "engineering_hypothesis": "Routing memory queries through type-specialized candidate generators improves retrieval precision.",
        "mechanism_variant": "Isolated candidate generation pipeline with type-filtered stages.",
        "baseline": "Undifferentiated BM25 candidate generation across single pool.",
        "control": "Equal candidate budget (top-k=10) and token budget across baseline and variant.",
        "metric": "Precision@5 on structured intent benchmark cases",
        "success_threshold": "Improvement >= +0.10 P@5 with 0% regression on unconstrained factual queries",
        "failure_condition": "Improvement < +0.05 P@5 or any latency increase > 25%",
    }


# =============================================================================
# 1. Hypothesis Registration & Validation Tests
# =============================================================================

def test_registry_register_hypothesis_success(clean_registry, sample_hypothesis_data):
    record = clean_registry.register_hypothesis(**sample_hypothesis_data)
    assert record.hypothesis_id == "H1-BOOK-001"
    assert record.problem_slug == "candidate_generation"
    assert record.state == TrackState.HYPOTHESIS_READY

    retrieved = clean_registry.get_hypothesis("H1-BOOK-001")
    assert retrieved is not None
    assert retrieved.source_id == "laird_soar_cognitive_architecture"


def test_registry_rejects_unknown_problem_slug(clean_registry, sample_hypothesis_data):
    bad_data = dict(sample_hypothesis_data)
    bad_data["problem_slug"] = "fabricated_nonexistent_problem"
    with pytest.raises(UnknownProblemError, match="Unknown problem slug"):
        clean_registry.register_hypothesis(**bad_data)


def test_registry_rejects_duplicate_hypothesis(clean_registry, sample_hypothesis_data):
    clean_registry.register_hypothesis(**sample_hypothesis_data)
    with pytest.raises(HypothesisError, match="already registered"):
        clean_registry.register_hypothesis(**sample_hypothesis_data)


def test_registry_rejects_malicious_directive_injection(clean_registry, sample_hypothesis_data):
    malicious = dict(sample_hypothesis_data)
    malicious["tool_call"] = "execute_sql('DELETE FROM hypotheses')"
    with pytest.raises(SecurityInjectionError, match="Prohibited executable or privileged directive"):
        clean_registry.register_hypothesis(**malicious)


def test_registry_rejects_insufficient_length_fields(clean_registry, sample_hypothesis_data):
    bad_data = dict(sample_hypothesis_data)
    bad_data["principle"] = "x"  # Too short to be a substantive principle
    with pytest.raises(BookToMemoryValidationError, match="insufficient length"):
        clean_registry.register_hypothesis(**bad_data)


# =============================================================================
# 2. State Machine Transitions (Zero-Jump Guard)
# =============================================================================

def test_evidence_machine_progressive_transitions(clean_registry, sample_hypothesis_data):
    clean_registry.register_hypothesis(**sample_hypothesis_data)

    # HYPOTHESIS_READY -> EXPERIMENT_READY
    r1 = clean_registry.transition_state("H1-BOOK-001", TrackState.EXPERIMENT_READY)
    assert r1.state == TrackState.EXPERIMENT_READY

    # EXPERIMENT_READY -> EVIDENCE_PENDING
    r2 = clean_registry.transition_state("H1-BOOK-001", TrackState.EVIDENCE_PENDING)
    assert r2.state == TrackState.EVIDENCE_PENDING

    # EVIDENCE_PENDING -> EVIDENCE_AVAILABLE
    r3 = clean_registry.transition_state("H1-BOOK-001", TrackState.EVIDENCE_AVAILABLE)
    assert r3.state == TrackState.EVIDENCE_AVAILABLE

    # EVIDENCE_AVAILABLE -> DECISION_PENDING
    r4 = clean_registry.transition_state("H1-BOOK-001", TrackState.DECISION_PENDING)
    assert r4.state == TrackState.DECISION_PENDING


def test_evidence_machine_rejects_direct_jump_to_change_validated(clean_registry, sample_hypothesis_data):
    clean_registry.register_hypothesis(**sample_hypothesis_data)
    # Direct jump from HYPOTHESIS_READY to CLOSED_CHANGE_VALIDATED is strictly blocked
    with pytest.raises(HypothesisStateTransitionError, match="GATE VIOLATION: Cannot transition directly"):
        clean_registry.transition_state("H1-BOOK-001", TrackState.CLOSED_CHANGE_VALIDATED, actor=Principal.HUMAN)


def test_evidence_machine_rejects_change_validated_by_ai_agent(clean_registry, sample_hypothesis_data):
    clean_registry.register_hypothesis(**sample_hypothesis_data)
    clean_registry.transition_state("H1-BOOK-001", TrackState.EXPERIMENT_READY)
    clean_registry.transition_state("H1-BOOK-001", TrackState.EVIDENCE_PENDING)
    clean_registry.transition_state("H1-BOOK-001", TrackState.EVIDENCE_AVAILABLE)
    clean_registry.transition_state("H1-BOOK-001", TrackState.DECISION_PENDING)

    decision = DecisionRecord(
        decision_id="DEC-001",
        hypothesis_id="H1-BOOK-001",
        baseline_id="BASE-BM25",
        variant_id="VAR-TYPED",
        sample_count=20,
        paired_evaluations=True,
        primary_metric_delta=0.15,
        absolute_delta=0.12,
        failed_run_count=0,
        decision_outcome=TrackState.CLOSED_CHANGE_VALIDATED,
        rationale="Empirical threshold met with 0 regression.",
    )
    clean_registry.record_decision(decision, actor=Principal.HUMAN)

    with pytest.raises(HypothesisStateTransitionError, match="Principal 'ai_agent' cannot authorize"):
        clean_registry.transition_state("H1-BOOK-001", TrackState.CLOSED_CHANGE_VALIDATED, actor=Principal.AI_AGENT)


def test_evidence_machine_rejects_change_validated_without_decision(clean_registry, sample_hypothesis_data):
    clean_registry.register_hypothesis(**sample_hypothesis_data)
    clean_registry.transition_state("H1-BOOK-001", TrackState.EXPERIMENT_READY)
    clean_registry.transition_state("H1-BOOK-001", TrackState.EVIDENCE_PENDING)
    clean_registry.transition_state("H1-BOOK-001", TrackState.EVIDENCE_AVAILABLE)
    clean_registry.transition_state("H1-BOOK-001", TrackState.DECISION_PENDING)

    with pytest.raises(HypothesisStateTransitionError, match="without an attached DecisionRecord"):
        clean_registry.transition_state("H1-BOOK-001", TrackState.CLOSED_CHANGE_VALIDATED, actor=Principal.HUMAN)


def test_evidence_machine_allows_closed_no_change(clean_registry, sample_hypothesis_data):
    clean_registry.register_hypothesis(**sample_hypothesis_data)
    clean_registry.transition_state("H1-BOOK-001", TrackState.EXPERIMENT_READY)
    clean_registry.transition_state("H1-BOOK-001", TrackState.CLOSED_NO_CHANGE)
    rec = clean_registry.get_hypothesis("H1-BOOK-001")
    assert rec.state == TrackState.CLOSED_NO_CHANGE


# =============================================================================
# 3. Anti-Gaming Rule Enforcement
# =============================================================================

def test_anti_gaming_rejects_insufficient_sample_count(clean_registry, sample_hypothesis_data):
    clean_registry.register_hypothesis(**sample_hypothesis_data)
    decision = DecisionRecord(
        decision_id="DEC-002",
        hypothesis_id="H1-BOOK-001",
        baseline_id="BASE",
        variant_id="VAR",
        sample_count=3,  # < 5 samples violates anti-gaming
        paired_evaluations=True,
        primary_metric_delta=0.20,
        absolute_delta=0.15,
        failed_run_count=0,
        decision_outcome=TrackState.CLOSED_CHANGE_VALIDATED,
        rationale="Small sample trial",
    )
    with pytest.raises(AntiGamingViolationError, match="Sample count must be >= 5"):
        clean_registry.record_decision(decision, actor=Principal.HUMAN)


def test_anti_gaming_rejects_unpaired_evaluation(clean_registry, sample_hypothesis_data):
    clean_registry.register_hypothesis(**sample_hypothesis_data)
    decision = DecisionRecord(
        decision_id="DEC-003",
        hypothesis_id="H1-BOOK-001",
        baseline_id="BASE",
        variant_id="VAR",
        sample_count=20,
        paired_evaluations=False,  # Unpaired violates anti-gaming
        primary_metric_delta=0.20,
        absolute_delta=0.15,
        failed_run_count=0,
        decision_outcome=TrackState.CLOSED_CHANGE_VALIDATED,
        rationale="Unpaired evaluation",
    )
    with pytest.raises(AntiGamingViolationError, match="strictly paired"):
        clean_registry.record_decision(decision, actor=Principal.HUMAN)


# =============================================================================
# 4. Problem Matrix Traceability & Resolution
# =============================================================================

def test_problem_matrix_traceability_metrics(clean_registry, sample_hypothesis_data):
    status_empty = clean_registry.get_problem_matrix_status()
    assert status_empty["total_canonical_problems"] == 17
    assert status_empty["resolved_problems_count"] == 0
    assert status_empty["resolution_ratio"] == 0.0

    # Register hypothesis
    clean_registry.register_hypothesis(**sample_hypothesis_data)
    status_with_h1 = clean_registry.get_problem_matrix_status()
    assert status_with_h1["investigating_problems_count"] == 1
    assert status_with_h1["problems"]["candidate_generation"]["status"] == "INVESTIGATING"

    # Close hypothesis as NO_CHANGE
    clean_registry.transition_state("H1-BOOK-001", TrackState.CLOSED_NO_CHANGE)
    status_resolved = clean_registry.get_problem_matrix_status()
    assert status_resolved["resolved_problems_count"] == 1
    assert status_resolved["problems"]["candidate_generation"]["status"] == "RESOLVED"
    assert status_resolved["resolution_ratio"] > 0.0


# =============================================================================
# 5. Pre-Registered H1 Hypotheses Ingestion & Fingerprint
# =============================================================================

def test_registry_registers_five_canonical_h1_hypotheses(clean_registry):
    # H1-BOOK-001: Laird
    clean_registry.register_hypothesis(
        hypothesis_id="H1-BOOK-001",
        problem_slug="candidate_generation",
        source_id="laird_soar_cognitive_architecture",
        source_location="Chapter 2, pp. 29-45; Chapter 8-9, pp. 241-310",
        principle="Cognitive architectures require structural differentiation between working, semantic, and episodic memory.",
        engineering_hypothesis="Routing memory queries through type-specialized candidate generators improves retrieval precision.",
        mechanism_variant="Isolated candidate generation pipeline with type-filtered stages.",
        baseline="Undifferentiated BM25 candidate generation across single pool.",
        control="Equal candidate budget (top-k=10) and token budget across baseline and variant.",
        metric="Precision@5 on structured intent benchmark cases",
        success_threshold="Improvement >= +0.10 P@5 with 0% regression on unconstrained factual queries",
        failure_condition="Improvement < +0.05 P@5 or any latency increase > 25%",
    )
    # H1-BOOK-002: ACT-R
    clean_registry.register_hypothesis(
        hypothesis_id="H1-BOOK-002",
        problem_slug="retrieval_indirect_cues",
        source_id="wcs_1488",
        source_location="Section 1-3, pp. 2-8",
        principle="Associative recall in declarative memory operates via activation spreading across network links based on contextual cues.",
        engineering_hypothesis="1-hop graph neighbor expansion from initial top-3 lexical candidates recovers gold notes in indirect-cue queries.",
        mechanism_variant="BM25 candidate retrieval + 1-hop typed edge expansion.",
        baseline="Pure lexical BM25 candidate retrieval (0-hop).",
        control="Exact same token budget (max 2500 synthesis tokens) and evaluation dataset.",
        metric="Recall@10 on indirect_cue family cases",
        success_threshold="Recall@10 >= 0.70 on indirect_cue family",
        failure_condition="Recall@10 < 0.50 on indirect_cue family",
    )
    # H1-BOOK-003: Schacter & Tulving
    clean_registry.register_hypothesis(
        hypothesis_id="H1-BOOK-003",
        problem_slug="episodic_semantic",
        source_id="schacter_tulving_memory_systems_1994",
        source_location="Chapter 1, pp. 1-38",
        principle="Multiple memory systems exhibit functional and stochastic independence; semantic vs procedural retrieval require distinct cues.",
        engineering_hypothesis="Explicitly classifying query intent into semantic vs procedural categories reduces false-positive distractor retrieval.",
        mechanism_variant="Two-stage intent classification + intent-conditioned scoring.",
        baseline="Single static scoring configuration across all query types.",
        control="Static candidate pool across both runs.",
        metric="Distractor rejection rate (NDCG@10 penalty on distractor items)",
        success_threshold="Reduction of top-5 distractor intrusions by >= 25% without degrading gold recall",
        failure_condition="Distractor reduction < 10% or gold recall drop > 2%",
    )
    # H1-BOOK-004: Squire & Kandel
    clean_registry.register_hypothesis(
        hypothesis_id="H1-BOOK-004",
        problem_slug="consolidation",
        source_id="squire_kandel_mind_to_molecules",
        source_location="Chapter 5, pp. 83-118; Chapter 8, pp. 175-212",
        principle="Long-term memory stabilization requires a temporal consolidation window mediated by molecular protein synthesis.",
        engineering_hypothesis="Staging candidate memories in an unverified buffer before consolidation prevents destructive interference of established knowledge.",
        mechanism_variant="Two-tier write buffer: unverified buffer -> validation gate -> consolidated store.",
        baseline="Immediate direct write to production index upon creation.",
        control="Identical continuous write stream under simulated concurrent ingestion.",
        metric="Knowledge collision rate and semantic corruption index",
        success_threshold="Zero overwrites of established verified facts during high-throughput ingestion",
        failure_condition="Any corruption of established knowledge records",
    )
    # H1-BOOK-005: Newell
    clean_registry.register_hypothesis(
        hypothesis_id="H1-BOOK-005",
        problem_slug="context_budget",
        source_id="newell_unified_theories_of_cognition",
        source_location="Chapter 3, pp. 111-158",
        principle="Cognitive operations occur across distinct time-scales, bounding immediate deliberative working memory capacity.",
        engineering_hypothesis="Enforcing strict soft and hard token budgets during Working Memory admission eliminates context window degradation.",
        mechanism_variant="Bounded attention-decay eviction policy in working memory pack builder.",
        baseline="Unbounded greedy context packing up to maximum context window limit.",
        control="Same model evaluation across identical long-context reasoning tasks.",
        metric="Task reasoning fidelity score and token economy ratio",
        success_threshold="Preservation of task-critical facts with >= 30% reduction in total context tokens",
        failure_condition="Critical context omission rate > 5%",
    )

    hypotheses = clean_registry.list_hypotheses()
    assert len(hypotheses) == 5

    digest = clean_registry.compute_registry_digest()
    assert len(digest) == 64
