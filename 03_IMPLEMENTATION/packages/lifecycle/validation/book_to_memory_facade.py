"""Book-to-Memory Unified Research Facade.

Authoritative Governance:
- 00_GOVERNANCE/rules/POLICY-LEARNING-QUALITY-02.md
- 08_RESEARCH/BOOK_TO_MEMORY/RESEARCH-TRACK-CONTRACT.md

Unifies all 10 architectural phases into a single, cohesive, production-grade interface:
1. Schema & Ontology (Phase 1)
2. Lifecycle Gates (Phase 2)
3. Conflict Registry (Phase 3)
4. Usage Test Engine (Phase 4)
5. Paired Ablation Engine (Phase 5)
6. Semantic Retrieval & Working Memory (Phase 6)
7. End-to-End Ingestion Pipeline (Phase 7)
8. Multi-Book Catalog & Consolidation (Phase 8)
9. Problem Matrix & Hypothesis Registry (Phase 9)
10. Controlled Experimentation Harness & Shadow Mode (Phase 10)
"""

from __future__ import annotations

import hashlib
import json
from dataclasses import asdict
from typing import Any, Callable, Dict, List, Optional, Tuple

from memory_controller.authorizer import Principal
from lifecycle.validation.book_to_memory_schema import (
    BookToMemoryValidationError,
    SecurityInjectionError,
    validate_book_to_memory_note,
    validate_untrusted_security,
)
from lifecycle.validation.book_to_memory_lifecycle import (
    BookToMemoryLifecycleState,
    OwnerApprovalToken,
)
from lifecycle.validation.book_to_memory_conflict import (
    ConflictRegistry,
    ConflictRecord,
)
from lifecycle.validation.book_to_memory_usage_test import (
    TaskSpecification,
    EvaluationAttempt,
)
from lifecycle.validation.book_to_memory_pipeline import (
    BookToMemoryPipeline,
    BookIngestionAuditReport,
)
from lifecycle.validation.book_to_memory_catalog import (
    BookToMemoryCatalog,
)
from lifecycle.validation.book_to_memory_hypothesis import (
    BookToMemoryHypothesisRegistry,
    HypothesisRecord,
    DecisionRecord,
    TrackState,
    CANONICAL_VAULT_PROBLEMS,
)
from lifecycle.validation.book_to_memory_experiment import (
    BookToMemoryExperimentHarness,
    ExperimentConfig,
    ExperimentResult,
    ExperimentValidationError,
)


class BookToMemoryFacade:
    """Master facade providing coordinated execution of the entire Book-to-Memory system."""

    VERSION: str = "1.0.0"

    def __init__(self) -> None:
        self.pipeline = BookToMemoryPipeline()
        self.catalog = BookToMemoryCatalog()
        self.hypothesis_registry = BookToMemoryHypothesisRegistry()
        self.experiment_harness = BookToMemoryExperimentHarness(self.hypothesis_registry)

    # -------------------------------------------------------------------------
    # 1. Catalog & Book Map Management
    # -------------------------------------------------------------------------
    def register_book(
        self,
        source_identity: str,
        title: str,
        authors: List[str],
        chapter_coverage: Dict[str, Any],
        edition: Optional[str] = None,
        linked_problems: Optional[List[str]] = None,
        conflicts_and_limitations: Optional[List[str]] = None,
        actor: Principal = Principal.HUMAN,
    ) -> Dict[str, Any]:
        """Register a monograph and its initial book map."""
        return self.catalog.register_book_map(
            source_identity=source_identity,
            title=title,
            authors=authors,
            chapter_coverage=chapter_coverage,
            edition=edition,
            linked_problems=linked_problems,
            conflicts_and_limitations=conflicts_and_limitations,
            actor=actor,
        )

    # -------------------------------------------------------------------------
    # 2. Pipeline Note Ingestion & Quality Gates
    # -------------------------------------------------------------------------
    def ingest_note(
        self,
        note_dict: Dict[str, Any],
        task_spec: Optional[TaskSpecification] = None,
        caller_principal: Principal = Principal.AI_AGENT,
        evaluator_principal: Principal = Principal.HUMAN,
        owner_approval_token: Optional[OwnerApprovalToken] = None,
        simulated_attempt: Optional[Any] = None,
        rubric: Optional[Dict[str, int]] = None,
        ablation_models: Optional[List[str]] = None,
        ablation_repetitions: int = 3,
        ablation_trial_data: Optional[Dict[str, Any]] = None,
        ablation_run_configs: Optional[Dict[str, Any]] = None,
    ) -> BookIngestionAuditReport:
        """Process a raw extracted concept note through all validation gates.

        The usage-test attempt/rubric and the ablation trial data must come from a real evaluation;
        without them the corresponding gates report INSUFFICIENT_DATA and the note does not advance.
        ``ablation_run_configs`` (model -> RunConfig) records how the paired runs were configured; without
        it the ablation is flagged not comparable and the note does not advance (PR #209 B08).
        """
        # Validate security upfront as front-door gate
        validate_untrusted_security(note_dict)

        if task_spec is None:
            task_spec = TaskSpecification(
                task_id=f"TASK-{note_dict.get('id', 'default')}",
                title=f"Validation for {note_dict.get('title', 'note')}",
                description="Real-world application test requiring candidate note reasoning and factual constraints.",
                task_type="application",
                expected_criteria={"applies_correctly": True},
            )

        report = self.pipeline.process_candidate_note(
            note=note_dict,
            task_spec=task_spec,
            caller_principal=caller_principal,
            evaluator_principal=evaluator_principal,
            owner_approval_token=owner_approval_token,
            simulated_attempt=simulated_attempt,
            rubric=rubric,
            ablation_models=ablation_models,
            ablation_repetitions=ablation_repetitions,
            ablation_trial_data=ablation_trial_data,
            ablation_run_configs=ablation_run_configs,
        )

        if report.final_lifecycle in ("VERIFIED", "ACTIVE"):
            source_id = note_dict.get("source_identity") or note_dict.get("source_title", "")
            if self.catalog.get_book_map(source_id):
                self.catalog.link_atomic_note(source_identity=source_id, note=note_dict)

        return report

    # -------------------------------------------------------------------------
    # 3. Hypothesis Registration & Controlled Experimentation
    # -------------------------------------------------------------------------
    def register_hypothesis(
        self,
        hypothesis_id: str,
        problem_slug: str,
        source_id: str,
        source_location: str,
        principle: str,
        engineering_hypothesis: str,
        mechanism_variant: str,
        baseline: str,
        control: str,
        metric: str,
        success_threshold: str,
        failure_condition: str,
    ) -> HypothesisRecord:
        """Register a new falsifiable engineering hypothesis tied to a canonical vault problem."""
        return self.hypothesis_registry.register_hypothesis(
            hypothesis_id=hypothesis_id,
            problem_slug=problem_slug,
            source_id=source_id,
            source_location=source_location,
            principle=principle,
            engineering_hypothesis=engineering_hypothesis,
            mechanism_variant=mechanism_variant,
            baseline=baseline,
            control=control,
            metric=metric,
            success_threshold=success_threshold,
            failure_condition=failure_condition,
        )

    def execute_controlled_experiment(
        self,
        config: ExperimentConfig,
        case_evaluator: Callable[[Dict[str, Any]], Tuple[float, float, Dict[str, Any]]],
        actor: Principal = Principal.AI_AGENT,
    ) -> ExperimentResult:
        """Configure and execute a controlled paired experiment under shadow mode."""
        self.experiment_harness.configure_experiment(config, actor=actor)
        return self.experiment_harness.run_experiment(
            experiment_id=config.experiment_id,
            case_evaluator=case_evaluator,
            actor=actor,
        )

    def finalize_decision(
        self,
        experiment_id: str,
        hypothesis_id: str,
        approved: bool,
        rationale: str,
        actor: Principal,
    ) -> HypothesisRecord:
        """Package decision and finalize hypothesis state (I-001/I-004 enforced)."""
        self.experiment_harness.prepare_decision_package(experiment_id, actor=actor)
        return self.experiment_harness.finalize_hypothesis_decision(
            hypothesis_id=hypothesis_id,
            approved=approved,
            rationale=rationale,
            actor=actor,
        )

    # -------------------------------------------------------------------------
    # 4. Global State & Cryptographic Verification
    # -------------------------------------------------------------------------
    def get_track_status(self) -> Dict[str, Any]:
        """Return quantitative summary of all active research subsystems."""
        problem_status = self.hypothesis_registry.get_problem_matrix_status()
        registered_maps = self.catalog.list_book_maps()
        catalog_summary = {
            "registered_books": len(registered_maps),
            "book_maps": [m["source_identity"] for m in registered_maps],
        }
        experiment_ledger = self.experiment_harness.get_experiment_ledger()

        return {
            "facade_version": self.VERSION,
            "catalog": catalog_summary,
            "problem_matrix": problem_status,
            "experiments_conducted": len(experiment_ledger),
            "experiment_ledger": experiment_ledger,
            "master_digest": self.compute_master_digest(),
        }

    def compute_master_digest(self) -> str:
        """Compute holistic deterministic SHA-256 digest of entire research vault state."""
        state_repr = json.dumps(
            {
                "catalog_digest": self.catalog.compute_catalog_digest(),
                "hypothesis_digest": self.hypothesis_registry.compute_registry_digest(),
                "harness_digest": self.experiment_harness.compute_harness_digest(),
            },
            sort_keys=True,
        )
        return hashlib.sha256(state_repr.encode("utf-8")).hexdigest()
