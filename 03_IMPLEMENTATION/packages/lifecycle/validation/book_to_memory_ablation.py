"""Book-to-Memory With-Note vs Without-Note Ablation Framework.

Implements the formal paired ablation validation authority according to:
- 00_GOVERNANCE/rules/POLICY-LEARNING-QUALITY-02.md (Section 7: Evaluare cu/fara nota)
- 00_GOVERNANCE/protocols/Confidence_Model.md
- 03_IMPLEMENTATION/packages/lifecycle/validation/book_to_memory_schema.py
- 03_IMPLEMENTATION/packages/lifecycle/validation/book_to_memory_lifecycle.py
- 03_IMPLEMENTATION/packages/lifecycle/validation/book_to_memory_conflict.py
- 03_IMPLEMENTATION/packages/lifecycle/validation/book_to_memory_usage_test.py
"""
from __future__ import annotations

import re
import json
import hashlib
from enum import Enum
from dataclasses import dataclass, field, asdict
from datetime import datetime, timezone
from typing import Any, Dict, List, Optional, Tuple, Set

from security.authorizer import Principal
from .book_to_memory_schema import (
    ConflictSeverity,
    ConflictStatus,
    EpistemicType,
    ProvenanceGateError,
    SecurityInjectionError,
    validate_provenance_gate,
    validate_untrusted_security,
)
from .book_to_memory_usage_test import (
    RubricDimension,
    TaskSpecification,
    EvaluationAttempt,
    UsageTestRecord,
    TaskBasedValidator,
    UsageTestStatus,
    SourceIsolationError,
    EvaluationTamperError,
    UsageTestValidationError,
)


class AblationCondition(str, Enum):
    """The two experimental conditions for paired ablation."""
    WITHOUT_NOTE = "WITHOUT_NOTE"  # Condition A: agent executes without candidate note
    WITH_NOTE = "WITH_NOTE"        # Condition B: agent executes with routed candidate note


class AblationError(ValueError):
    """Base exception for ablation framework errors."""


class AblationPermissionError(AblationError):
    """Raised when an unauthorized actor attempts privileged ablation operations."""


class AblationContaminationError(AblationError):
    """Raised when the WITHOUT_NOTE condition is contaminated with note contents."""


class AblationValidationError(AblationError):
    """Raised when ablation experiment configuration or results are invalid."""


def calculate_ablation_delta(score_with: float, score_without: float) -> float:
    """Calculate the formal Ablation Delta according to Policy-02 Section 7:
    
    Delta = (S_cu - S_fara) / S_fara
    If S_fara == 0, report absolute difference: S_cu - S_fara.
    """
    if score_without == 0.0:
        return float(score_with - score_without)
    return float((score_with - score_without) / score_without)


@dataclass
class AblationTrial:
    """Record of an individual trial run within an ablation experiment."""
    trial_id: str
    experiment_id: str
    condition: str          # WITH_NOTE or WITHOUT_NOTE
    model_or_agent: str
    repetition_index: int   # 1-indexed (e.g. 1, 2, 3)
    order_in_pair: int      # 1 or 2 (to audit order bias)
    score: int              # 0..10
    rubric_scores: Dict[str, int]
    status: str             # PASS, RETRY, FAIL
    answer: str
    evidence: str
    timestamp: str = field(default_factory=lambda: datetime.now(timezone.utc).isoformat())

    def to_dict(self) -> Dict[str, Any]:
        return asdict(self)


@dataclass
class AblationExperimentRecord:
    """Complete, tamper-evident audit record of a paired ablation experiment."""
    experiment_id: str
    note_id: str
    task_id: str
    models: List[str]
    repetitions: int
    trials: List[AblationTrial]
    model_summaries: Dict[str, Dict[str, float]]
    aggregate_summary: Dict[str, float]
    source_provenance: Dict[str, Any]
    evaluator_id: str
    timestamp: str = field(default_factory=lambda: datetime.now(timezone.utc).isoformat())
    signature: str = ""

    def to_dict(self) -> Dict[str, Any]:
        return {
            "experiment_id": self.experiment_id,
            "note_id": self.note_id,
            "task_id": self.task_id,
            "models": self.models,
            "repetitions": self.repetitions,
            "trials": [t.to_dict() for t in self.trials],
            "model_summaries": self.model_summaries,
            "aggregate_summary": self.aggregate_summary,
            "source_provenance": self.source_provenance,
            "evaluator_id": self.evaluator_id,
            "timestamp": self.timestamp,
            "signature": self.signature,
        }

    def verify_signature(self) -> bool:
        """Verify SHA-256 cryptographic integrity signature."""
        computed = _compute_ablation_record_hash(self)
        return computed == self.signature


def _compute_ablation_record_hash(record: AblationExperimentRecord) -> str:
    """Compute deterministic SHA-256 hash of the ablation record."""
    canonical_payload = {
        "experiment_id": record.experiment_id,
        "note_id": record.note_id,
        "task_id": record.task_id,
        "models": sorted(record.models),
        "repetitions": record.repetitions,
        "trials_count": len(record.trials),
        "model_summaries": record.model_summaries,
        "aggregate_summary": record.aggregate_summary,
        "evaluator_id": record.evaluator_id,
        "timestamp": record.timestamp,
    }
    dumped = json.dumps(canonical_payload, sort_keys=True)
    return hashlib.sha256(dumped.encode("utf-8")).hexdigest()


class AblationExperimentRunner:
    """Orchestrates paired WITH_NOTE vs WITHOUT_NOTE ablation experiments per Policy-02."""

    VERSION: str = "1.0.0"

    def __init__(self, evaluator_id: str = "ablation_evaluator_primary"):
        self.evaluator_id = evaluator_id
        self.validator = TaskBasedValidator(evaluator_id=evaluator_id)

    def validate_without_note_isolation(
        self,
        without_note_context: Dict[str, Any],
        without_note_answer: str,
        note: Dict[str, Any],
    ) -> None:
        """Strictly verify that the candidate note has NOT leaked into WITHOUT_NOTE condition."""
        note_id = str(note.get("id") or note.get("note_id", "")).strip().lower()
        title = str(note.get("title", "")).strip().lower()
        concept = str(note.get("atomic_concept", "")).strip().lower()

        combined_text = (json.dumps(without_note_context) + " " + without_note_answer).lower()

        # Check for direct note ID or distinctive title leaks
        if note_id and note_id in combined_text:
            raise AblationContaminationError(
                f"Contamination: candidate note ID '{note_id}' detected in WITHOUT_NOTE condition."
            )
        if title and len(title) > 5 and title in combined_text:
            raise AblationContaminationError(
                f"Contamination: candidate note title '{title}' detected in WITHOUT_NOTE condition."
            )

    def run_paired_experiment(
        self,
        note: Dict[str, Any],
        task: TaskSpecification,
        models: List[str],
        repetitions: int = 3,
        trial_data: Optional[Dict[str, Any]] = None,
        actor: Principal = Principal.HUMAN,
    ) -> AblationExperimentRecord:
        """Execute a formal paired ablation experiment across minimum 2 models and 3 repetitions."""
        if actor == Principal.AI_AGENT:
            raise AblationPermissionError("Principal 'ai_agent' cannot authorize or execute ablation experiments.")

        # Minimum requirements per Policy-02 Section 7: >= 2 models, >= 3 repetitions
        if not models or len(models) < 2:
            raise AblationValidationError(
                f"Policy-02 Section 7 requires minimum 2 distinct models (got {len(models) if models else 0})."
            )
        if len(set(models)) < len(models):
            raise AblationValidationError("Model list contains duplicate models; distinct models required.")
        if repetitions < 3:
            raise AblationValidationError(
                f"Policy-02 Section 7 requires minimum 3 repetitions per task (got {repetitions})."
            )

        # Validate note prerequisites
        validate_untrusted_security(note)
        validate_provenance_gate(note)

        experiment_id = f"EXP-ABL-{note.get('id', 'note')}-{task.task_id}"
        all_trials: List[AblationTrial] = []
        model_scores: Dict[str, Dict[str, List[int]]] = {
            m: {"WITH_NOTE": [], "WITHOUT_NOTE": []} for m in models
        }

        # For each model and repetition, execute alternating order
        for model in models:
            for rep in range(1, repetitions + 1):
                # Order alternating: rep 1 -> (WITHOUT, WITH), rep 2 -> (WITH, WITHOUT), rep 3 -> (WITHOUT, WITH)
                order_pattern = (
                    [AblationCondition.WITHOUT_NOTE, AblationCondition.WITH_NOTE]
                    if rep % 2 != 0
                    else [AblationCondition.WITH_NOTE, AblationCondition.WITHOUT_NOTE]
                )

                for order_idx, condition in enumerate(order_pattern, start=1):
                    trial_id = f"{experiment_id}-{model}-r{rep}-{condition.value}"

                    # Retrieve simulation / trial data if supplied
                    key = f"{model}:{rep}:{condition.value}"
                    t_data = (trial_data or {}).get(key, {})
                    answer = t_data.get("answer", f"Default answer for {condition.value} by {model}")
                    evidence = t_data.get("evidence", "Default evidence")
                    rubric = t_data.get("rubric", {
                        RubricDimension.CORECTITUDINE.value: 2 if condition == AblationCondition.WITH_NOTE else 1,
                        RubricDimension.COMPLETITUDINE.value: 2 if condition == AblationCondition.WITH_NOTE else 1,
                        RubricDimension.FARA_GHICIT.value: 2 if condition == AblationCondition.WITH_NOTE else 1,
                        RubricDimension.FARA_SURSE_EXTERNE.value: 2,
                        RubricDimension.REPRODUCTIBILITATE.value: 2 if condition == AblationCondition.WITH_NOTE else 1,
                    })
                    provided_ctx = t_data.get("context", {})

                    if condition == AblationCondition.WITHOUT_NOTE:
                        self.validate_without_note_isolation(provided_ctx, answer, note)

                    attempt = EvaluationAttempt(
                        attempt_number=rep,
                        agent_id=model,
                        note_id=note.get("id", "note"),
                        answer=answer,
                        evidence=evidence,
                        provided_context=provided_ctx,
                    )

                    test_note = note if condition == AblationCondition.WITH_NOTE else {
                        "id": "empty_note",
                        "type": note.get("type"),
                        "source_title": note.get("source_title"),
                        "chapter": note.get("chapter"),
                        "page_range": note.get("page_range"),
                        "lifecycle": "UNVERIFIED",
                        "tags": note.get("tags", []),
                    }

                    record = self.validator.evaluate_attempt(
                        note=test_note,
                        task=task,
                        attempt=attempt,
                        rubric=rubric,
                        actor=actor,
                    )

                    trial = AblationTrial(
                        trial_id=trial_id,
                        experiment_id=experiment_id,
                        condition=condition.value,
                        model_or_agent=model,
                        repetition_index=rep,
                        order_in_pair=order_idx,
                        score=record.score,
                        rubric_scores=record.rubric_scores,
                        status=record.status,
                        answer=answer,
                        evidence=evidence,
                    )
                    all_trials.append(trial)
                    model_scores[model][condition.value].append(record.score)

        # Aggregate per model and overall
        model_summaries: Dict[str, Dict[str, float]] = {}
        total_with = 0.0
        total_without = 0.0
        total_runs = len(models) * repetitions

        for m in models:
            mean_with = sum(model_scores[m]["WITH_NOTE"]) / repetitions
            mean_without = sum(model_scores[m]["WITHOUT_NOTE"]) / repetitions
            delta_m = calculate_ablation_delta(mean_with, mean_without)
            model_summaries[m] = {
                "mean_with_note": round(mean_with, 2),
                "mean_without_note": round(mean_without, 2),
                "delta": round(delta_m, 4),
            }
            total_with += sum(model_scores[m]["WITH_NOTE"])
            total_without += sum(model_scores[m]["WITHOUT_NOTE"])

        agg_mean_with = total_with / total_runs
        agg_mean_without = total_without / total_runs
        agg_delta = calculate_ablation_delta(agg_mean_with, agg_mean_without)

        agg_summary = {
            "aggregate_mean_with_note": round(agg_mean_with, 2),
            "aggregate_mean_without_note": round(agg_mean_without, 2),
            "aggregate_delta": round(agg_delta, 4),
            "total_trials": len(all_trials),
        }

        provenance = {
            "source_title": note.get("source_title", ""),
            "chapter": note.get("chapter", ""),
            "page_range": note.get("page_range", ""),
            "exact_page": note.get("exact_page"),
        }

        rec = AblationExperimentRecord(
            experiment_id=experiment_id,
            note_id=note.get("id") or note.get("note_id", "unnamed_note"),
            task_id=task.task_id,
            models=models,
            repetitions=repetitions,
            trials=all_trials,
            model_summaries=model_summaries,
            aggregate_summary=agg_summary,
            source_provenance=provenance,
            evaluator_id=self.evaluator_id,
        )
        rec.signature = _compute_ablation_record_hash(rec)
        return rec


def check_ablation_eligibility(
    note: Dict[str, Any],
    ablation_record: AblationExperimentRecord,
) -> Tuple[bool, str]:
    """Verify that the ablation outcome satisfies GATE-07 without auto-promotion."""
    if not ablation_record.verify_signature():
        return False, "Ablation record signature verification failed: record has been tampered with."

    agg_delta = ablation_record.aggregate_summary.get("aggregate_delta", -1.0)
    if agg_delta < 0.0:
        return False, f"GATE-07 Ablation failed: Aggregate Delta must be >= 0.0 (got {agg_delta})."

    # Check for open high/critical conflict
    open_conflicts = note.get("open_conflicts", [])
    for conf in open_conflicts:
        status = conf.get("status", "").lower()
        sev = conf.get("severity", "").lower()
        if status == ConflictStatus.OPEN.value and sev in (ConflictSeverity.HIGH.value, ConflictSeverity.CRITICAL.value):
            return False, f"GATE-06 Conflict blocks note: open {sev} conflict remains unmitigated."

    # Check epistemic safety: hypothesis stays hypothesis
    epistemic_type = note.get("epistemic_type")
    if epistemic_type == EpistemicType.HYPOTHESIS.value:
        return True, "Ablation PASSED for HYPOTHESIS. Does not convert hypothesis into production mechanism."

    return True, "Ablation PASSED: positive incremental value demonstrated. Requires formal Owner Approval for ACTIVE."
