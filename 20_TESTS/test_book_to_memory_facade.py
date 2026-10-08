"""Test suite for Phase 11: Unified Book-to-Memory Facade.

Validates end-to-end integration of all 10 preceding phases through BookToMemoryFacade.
"""

import pytest

from memory_controller.authorizer import Principal
from lifecycle.validation.book_to_memory_schema import (
    BookToMemoryType,
    SecurityInjectionError,
)
from lifecycle.validation.book_to_memory_lifecycle import (
    BookToMemoryLifecycleState,
    issue_owner_approval,
)
from lifecycle.validation.book_to_memory_hypothesis import TrackState
from lifecycle.validation.book_to_memory_experiment import (
    ExperimentConfig,
    ExperimentValidationError,
)
from lifecycle.validation.book_to_memory_usage_test import (
    EvaluationAttempt,
    RubricDimension,
    TaskSpecification,
)
from lifecycle.validation.book_to_memory_facade import BookToMemoryFacade
from lifecycle.validation.book_to_memory_run_config import RunConfig


def _synthetic_eval(note_id: str) -> dict:
    """Explicit, clearly synthetic evaluation inputs (TEST FIXTURE, not research evidence).

    The pipeline has no default attempt, rubric or ablation data (PR #209 B01).
    """
    def rubric(n):
        return {d.value: n for d in RubricDimension}

    trials = {}
    for model in ("model_primary", "model_secondary"):
        for rep in range(1, 4):
            trials[f"{model}:{rep}:WITH_NOTE"] = {"answer": "synthetic", "evidence": "synthetic", "rubric": rubric(2)}
            trials[f"{model}:{rep}:WITHOUT_NOTE"] = {"answer": "synthetic", "evidence": "synthetic", "rubric": rubric(1)}
    return {
        "simulated_attempt": EvaluationAttempt(
            attempt_number=1, agent_id="synthetic-fixture-evaluator", note_id=note_id,
            answer="synthetic", evidence="synthetic",
        ),
        "rubric": rubric(2),
        "ablation_trial_data": trials,
        "ablation_run_configs": {
            m: RunConfig.build(model_id=m, temperature=0.0, seed=1, max_tokens=512,
                               prompt_template="synthetic fixture template")
            for m in ("model_primary", "model_secondary")
        },
    }


@pytest.fixture
def facade():
    return BookToMemoryFacade()


@pytest.fixture
def sample_valid_note():
    return {
        "id": "NOTE-SYS-CONS-001",
        "title": "Systems Consolidation in AI Memory",
        "type": BookToMemoryType.CONCEPT.value,
        "lifecycle": "RAW",
        "atomic_concept": "Systems consolidation reorganizes memory representations over time.",
        "evidence": "Extensive empirical evidence from hippocampal trace studies shows progressive transfer to neocortical areas.",
        "source_title": "Memory: From Mind to Molecules",
        "source_identity": "squire_kandel_mind_to_molecules",
        "authors": ["Larry Squire", "Eric Kandel"],
        "chapter": "Chapter 8: Systems Consolidation",
        "page_range": "205-224",
        "exact_page": 210,
        "tags": ["consolidation", "memory", "systems"],
    }


def test_facade_initialization(facade):
    assert facade.VERSION == "1.0.0"
    status = facade.get_track_status()
    assert "catalog" in status
    assert "problem_matrix" in status
    assert "master_digest" in status
    assert len(status["master_digest"]) == 64


def test_facade_book_registration(facade):
    record = facade.register_book(
        source_identity="squire_kandel_mind_to_molecules",
        title="Memory: From Mind to Molecules",
        authors=["Larry Squire", "Eric Kandel"],
        chapter_coverage={"Chapter 8: Systems Consolidation": ["Systems Consolidation"]},
        edition="2nd Edition (2008)",
        linked_problems=["consolidation", "working_memory"],
    )
    assert record["source_identity"] == "squire_kandel_mind_to_molecules"
    status = facade.get_track_status()
    assert status["catalog"]["registered_books"] == 1


def test_facade_ingest_note_and_link_to_catalog(facade, sample_valid_note):
    # Register book first
    facade.register_book(
        source_identity="squire_kandel_mind_to_molecules",
        title="Memory: From Mind to Molecules",
        authors=["Larry Squire", "Eric Kandel"],
        chapter_coverage={"Chapter 8: Systems Consolidation": ["Systems Consolidation"]},
        edition="2nd Edition (2008)",
        linked_problems=["consolidation"],
    )

    task_spec = TaskSpecification(
        task_id="TASK-CONS-01",
        title="Agent Consolidation Protocol",
        description="Design a memory consolidation protocol that transfers ephemeral working traces to permanent semantic memory.",
        task_type="application",
        expected_criteria={"specifies_consolidation": True},
    )

    # The owner approves the content the pipeline produced (a dry run on a throw-away facade),
    # not the raw input: the token is bound to that exact content.
    dry = BookToMemoryFacade()
    dry.register_book(
        source_identity="squire_kandel_mind_to_molecules",
        title="Memory: From Mind to Molecules",
        authors=["Larry Squire", "Eric Kandel"],
        chapter_coverage={"Chapter 8: Systems Consolidation": ["Systems Consolidation"]},
        edition="2nd Edition (2008)",
        linked_problems=["consolidation"],
    )
    dry_report = dry.ingest_note(
        note_dict=sample_valid_note,
        task_spec=task_spec,
        caller_principal=Principal.HUMAN,
        evaluator_principal=Principal.HUMAN,
        **_synthetic_eval("NOTE-SYS-CONS-001"),
    )
    assert dry_report.final_lifecycle == "VERIFIED"
    token = issue_owner_approval(actor=Principal.HUMAN, note=dry_report.candidate_note)

    report = facade.ingest_note(
        note_dict=sample_valid_note,
        task_spec=task_spec,
        caller_principal=Principal.HUMAN,
        evaluator_principal=Principal.HUMAN,
        owner_approval_token=token,
        **_synthetic_eval("NOTE-SYS-CONS-001"),
    )
    assert report.final_lifecycle == "ACTIVE"

    # Check that note is linked in catalog
    book_map = facade.catalog.get_book_map("squire_kandel_mind_to_molecules")
    assert book_map is not None
    linked_notes = facade.catalog._linked_notes.get("squire_kandel_mind_to_molecules", [])
    assert any(n.get("id") == "NOTE-SYS-CONS-001" for n in linked_notes)


def test_facade_ingest_note_rejects_malicious_directive(facade, sample_valid_note):
    sample_valid_note["exec"] = "import os; os.system('calc')"
    with pytest.raises(SecurityInjectionError):
        facade.ingest_note(note_dict=sample_valid_note)


def test_facade_hypothesis_and_controlled_experiment_approval(facade):
    facade.register_hypothesis(
        hypothesis_id="H1-BOOK-001",
        problem_slug="interference_conflict",
        source_id="Carti/Psychology_of_Memory_Baddeley.pdf",
        source_location="Chapter 4, pp. 68-92",
        principle="Prior stored information actively competes with new traces during associative retrieval.",
        engineering_hypothesis="Similarity and temporal discriminant boundaries reduce false associative retrieval by at least 15%.",
        mechanism_variant="proactive_interference_discriminator",
        baseline="undifferentiated_rrf_retrieval",
        control="fixed_token_budget_and_query_set",
        metric="retrieval_precision_under_distraction",
        success_threshold="Delta >= +0.15 with zero regression",
        failure_condition="Delta < +0.05 or increased latency > 20%",
    )

    sample_cases = [
        {"case_id": f"CASE-00{i}", "query": f"test query {i}", "expected_note": f"NOTE-00{i}"}
        for i in range(1, 6)
    ]
    cfg = ExperimentConfig(
        experiment_id="EXP-H1-001-INTERFERENCE",
        hypothesis_id="H1-BOOK-001",
        protocol_name="Controlled Proactive Interference Mitigation",
        sample_cases=sample_cases,
    )

    exp_res = facade.execute_controlled_experiment(
        config=cfg,
        case_evaluator=lambda c: (0.50, 0.80, {"cue": "indirect"}),
        actor=Principal.AI_AGENT,
    )
    assert exp_res.is_statistically_improved is True

    # Human Owner finalizes decision
    final_hyp = facade.finalize_decision(
        experiment_id="EXP-H1-001-INTERFERENCE",
        hypothesis_id="H1-BOOK-001",
        approved=True,
        rationale="Owner attested empirical improvement of +0.30.",
        actor=Principal.HUMAN,
    )
    assert final_hyp.state == TrackState.CLOSED_CHANGE_VALIDATED


def test_facade_hypothesis_rejects_ai_agent_attestation(facade):
    facade.register_hypothesis(
        hypothesis_id="H1-BOOK-001",
        problem_slug="interference_conflict",
        source_id="Carti/Psychology_of_Memory_Baddeley.pdf",
        source_location="Chapter 4, pp. 68-92",
        principle="Prior stored information actively competes with new traces during associative retrieval.",
        engineering_hypothesis="Similarity and temporal discriminant boundaries reduce false associative retrieval by at least 15%.",
        mechanism_variant="proactive_interference_discriminator",
        baseline="undifferentiated_rrf_retrieval",
        control="fixed_token_budget_and_query_set",
        metric="retrieval_precision_under_distraction",
        success_threshold="Delta >= +0.15 with zero regression",
        failure_condition="Delta < +0.05 or increased latency > 20%",
    )

    sample_cases = [
        {"case_id": f"CASE-00{i}", "query": f"test query {i}", "expected_note": f"NOTE-00{i}"}
        for i in range(1, 6)
    ]
    cfg = ExperimentConfig(
        experiment_id="EXP-H1-001-INTERFERENCE",
        hypothesis_id="H1-BOOK-001",
        protocol_name="Controlled Proactive Interference Mitigation",
        sample_cases=sample_cases,
    )

    facade.execute_controlled_experiment(
        config=cfg,
        case_evaluator=lambda c: (0.50, 0.80, {}),
        actor=Principal.AI_AGENT,
    )

    with pytest.raises(PermissionError, match="AI Agent cannot authorize CLOSED_CHANGE_VALIDATED"):
        facade.finalize_decision(
            experiment_id="EXP-H1-001-INTERFERENCE",
            hypothesis_id="H1-BOOK-001",
            approved=True,
            rationale="Automated validation claim",
            actor=Principal.AI_AGENT,
        )


def test_facade_hypothesis_negative_outcome(facade):
    facade.register_hypothesis(
        hypothesis_id="H1-BOOK-001",
        problem_slug="interference_conflict",
        source_id="Carti/Psychology_of_Memory_Baddeley.pdf",
        source_location="Chapter 4, pp. 68-92",
        principle="Prior stored information actively competes with new traces during associative retrieval.",
        engineering_hypothesis="Similarity and temporal discriminant boundaries reduce false associative retrieval by at least 15%.",
        mechanism_variant="proactive_interference_discriminator",
        baseline="undifferentiated_rrf_retrieval",
        control="fixed_token_budget_and_query_set",
        metric="retrieval_precision_under_distraction",
        success_threshold="Delta >= +0.15 with zero regression",
        failure_condition="Delta < +0.05 or increased latency > 20%",
    )

    sample_cases = [
        {"case_id": f"CASE-00{i}", "query": f"test query {i}", "expected_note": f"NOTE-00{i}"}
        for i in range(1, 6)
    ]
    cfg = ExperimentConfig(
        experiment_id="EXP-H1-001-INTERFERENCE",
        hypothesis_id="H1-BOOK-001",
        protocol_name="Controlled Proactive Interference Mitigation",
        sample_cases=sample_cases,
    )

    facade.execute_controlled_experiment(
        config=cfg,
        case_evaluator=lambda c: (0.50, 0.52, {}),  # Low delta (+0.02)
        actor=Principal.AI_AGENT,
    )

    final_hyp = facade.finalize_decision(
        experiment_id="EXP-H1-001-INTERFERENCE",
        hypothesis_id="H1-BOOK-001",
        approved=False,
        rationale="Delta insufficient (+0.02 vs +0.15 required).",
        actor=Principal.AI_AGENT,
    )
    assert final_hyp.state == TrackState.CLOSED_NO_CHANGE


def test_facade_without_evaluation_data_does_not_advance_note(facade, sample_valid_note):
    """No usage-test/ablation data means INSUFFICIENT_DATA, never a built-in pass (PR #209 B01)."""
    report = facade.ingest_note(
        note_dict=sample_valid_note,
        caller_principal=Principal.HUMAN,
        evaluator_principal=Principal.HUMAN,
    )
    assert report.final_lifecycle in ("RAW", "REVIEW")
    assert report.final_lifecycle not in ("VERIFIED", "ACTIVE")
