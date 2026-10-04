"""Book-to-Memory Usage Test and Task-Based Validation.

Implements the formal Task-Based Validation authority according to:
- 00_GOVERNANCE/rules/POLICY-LEARNING-QUALITY-02.md (Section 6: Test de utilizare & Section 7: Evaluare cu/fara nota)
- 00_GOVERNANCE/protocols/Confidence_Model.md
- 03_IMPLEMENTATION/packages/lifecycle/validation/book_to_memory_schema.py
- 03_IMPLEMENTATION/packages/lifecycle/validation/book_to_memory_lifecycle.py
- 03_IMPLEMENTATION/packages/lifecycle/validation/book_to_memory_conflict.py
"""
from __future__ import annotations

import re
import json
import hashlib
from enum import Enum
from dataclasses import dataclass, field, asdict
from datetime import datetime, timezone
from typing import Any, Dict, List, Optional, Tuple

from security.authorizer import Principal
from .book_to_memory_schema import (
    BookToMemoryType,
    ConflictSeverity,
    ConflictStatus,
    EpistemicType,
    ProvenanceGateError,
    SecurityInjectionError,
    validate_provenance_gate,
    validate_untrusted_security,
    _LAZY_PROVENANCE_PATTERNS,
)


class RubricDimension(str, Enum):
    """The 5 canonical criteria of the Policy-02 Usage Test rubric."""
    CORECTITUDINE = "corectitudine"          # 0-2 (minimum 1 required for PASS)
    COMPLETITUDINE = "completitudine"        # 0-2
    FARA_GHICIT = "fara_ghicit"              # 0-2
    FARA_SURSE_EXTERNE = "fara_surse_externe" # 0-2
    REPRODUCTIBILITATE = "reproductibilitate" # 0-2


class UsageTestStatus(str, Enum):
    """Canonical test outcome states."""
    PASS = "PASS"          # score >= 8/10 and corectitudine >= 1
    RETRY = "RETRY"        # 5 <= score <= 7
    FAIL = "FAIL"          # score < 5 or isolation breach or missing provenance
    REJECTED = "REJECTED"  # terminal rejection


class UsageTestError(ValueError):
    """Base exception for usage test errors."""


class SourceIsolationError(UsageTestError):
    """Raised when an attempt attempts to access original book, PDF, or external source."""


class EvaluationTamperError(UsageTestError):
    """Raised when an unauthorized actor attempts to manipulate test specifications or results."""


class ContaminatedEvaluatorError(UsageTestError):
    """Raised when multi-agent evaluation suffers from cross-agent result contamination."""


class UsageTestValidationError(UsageTestError):
    """Raised when usage test payload or rubric format is invalid."""


_FORBIDDEN_SOURCE_PATTERNS = [
    re.compile(r"\b(original\s*pdf|read\s*pdf|open\s*pdf)\b", re.IGNORECASE),
    re.compile(r"\b(raw\s*book|original\s*book|full\s*book)\b", re.IGNORECASE),
    re.compile(r"\b06_inbox[/\\]carti\b", re.IGNORECASE),
    re.compile(r"https?://[^\s]+", re.IGNORECASE),
    re.compile(r"\b(web\s*search|google\s*search|bing\s*search)\b", re.IGNORECASE),
    re.compile(r"\bexternal\s*source\b", re.IGNORECASE),
]

_ROTE_MEMORIZATION_PATTERNS = [
    re.compile(r"\breproduce\s+word[\s\-]+for[\s\-]+word\b", re.IGNORECASE),
    re.compile(r"\bmemorize\s+literal\s+text\b", re.IGNORECASE),
    re.compile(r"\bquote\s+the\s+entire\s+chapter\b", re.IGNORECASE),
]


@dataclass(frozen=True)
class TaskSpecification:
    """Pre-defined, immutable task specification for candidate note evaluation."""
    task_id: str
    title: str
    description: str
    task_type: str  # application, diagnosis, alternative_selection, transfer, error_identification, rule_application
    expected_criteria: Dict[str, Any]
    allowed_context_types: Tuple[str, ...] = field(default=("note", "declared_dependencies"))
    created_at: str = field(default_factory=lambda: datetime.now(timezone.utc).isoformat())

    def __post_init__(self):
        if not self.task_id or not isinstance(self.task_id, str):
            raise UsageTestValidationError("TaskSpecification requires a valid non-empty 'task_id'.")
        if not self.description or len(self.description.strip()) < 10:
            raise UsageTestValidationError("TaskSpecification requires a detailed 'description' (>= 10 chars).")
        valid_types = {
            "application",
            "diagnosis",
            "alternative_selection",
            "transfer",
            "error_identification",
            "rule_application",
        }
        if self.task_type not in valid_types:
            raise UsageTestValidationError(f"Invalid task_type '{self.task_type}'. Must be one of {valid_types}.")
        for pattern in _ROTE_MEMORIZATION_PATTERNS:
            if pattern.search(self.description):
                raise UsageTestValidationError(
                    "Task cannot be a rote memorization or word-for-word copy task."
                )


@dataclass
class EvaluationAttempt:
    """A single execution attempt by an agent or model under evaluation."""
    attempt_number: int
    agent_id: str
    note_id: str
    answer: str
    evidence: str
    provided_context: Dict[str, Any] = field(default_factory=dict)
    timestamp: str = field(default_factory=lambda: datetime.now(timezone.utc).isoformat())


@dataclass
class UsageTestRecord:
    """Audit-proof record of a completed usage test evaluation."""
    test_id: str
    note_id: str
    task_id: str
    task_description: str
    allowed_context: Dict[str, Any]
    model_or_agent: str
    attempt: int
    answer: str
    expected_criteria: Dict[str, Any]
    rubric_scores: Dict[str, int]
    score: int
    max_score: int
    status: str
    evidence: str
    failure_reason: Optional[str]
    timestamp: str
    evaluator_id: str
    evaluator_version: str
    source_provenance: Dict[str, Any]
    signature: str = ""

    def to_dict(self) -> Dict[str, Any]:
        return asdict(self)

    def verify_signature(self) -> bool:
        """Verify the tamper-evident signature of the test record."""
        computed = _compute_test_record_hash(self)
        return computed == self.signature


def _compute_test_record_hash(record: UsageTestRecord) -> str:
    """Compute deterministic SHA-256 hash of the record contents."""
    canonical_payload = {
        "test_id": record.test_id,
        "note_id": record.note_id,
        "task_id": record.task_id,
        "model_or_agent": record.model_or_agent,
        "attempt": record.attempt,
        "score": record.score,
        "max_score": record.max_score,
        "rubric_scores": record.rubric_scores,
        "status": record.status,
        "timestamp": record.timestamp,
        "evaluator_id": record.evaluator_id,
    }
    dumped = json.dumps(canonical_payload, sort_keys=True)
    return hashlib.sha256(dumped.encode("utf-8")).hexdigest()


class TaskBasedValidator:
    """Formal evaluator for Book-to-Memory notes under Policy-02."""

    VERSION: str = "1.0.0"

    def __init__(self, evaluator_id: str = "evaluator_primary"):
        self.evaluator_id = evaluator_id
        self._execution_history: List[UsageTestRecord] = []

    @property
    def history(self) -> List[UsageTestRecord]:
        """Read-only view of test execution history."""
        return list(self._execution_history)

    def validate_source_isolation(self, attempt: EvaluationAttempt, note: Dict[str, Any]) -> None:
        """Ensure the agent and note make zero access to original books or external sources."""
        # 1. Check for prompt injection in note or attempt
        validate_untrusted_security(note)

        # 2. Check for forbidden external or book source access in answer
        for pattern in _FORBIDDEN_SOURCE_PATTERNS:
            if pattern.search(attempt.answer):
                raise SourceIsolationError(
                    f"Source isolation violation: attempt accessed forbidden source '{pattern.pattern}'."
                )

        # 3. Check for external URLs in declared context
        for key, val in attempt.provided_context.items():
            if isinstance(val, str):
                for pattern in _FORBIDDEN_SOURCE_PATTERNS:
                    if pattern.search(val):
                        raise SourceIsolationError(
                            f"Source isolation violation in context key '{key}': forbidden source '{pattern.pattern}'."
                        )

    def validate_note_prerequisites(self, note: Dict[str, Any]) -> None:
        """Validate that the note has complete provenance before test execution."""
        validate_provenance_gate(note)

    def evaluate_attempt(
        self,
        note: Dict[str, Any],
        task: TaskSpecification,
        attempt: EvaluationAttempt,
        rubric: Dict[str, int],
        actor: Principal = Principal.HUMAN,
    ) -> UsageTestRecord:
        """Evaluate a single task attempt against the Policy-02 5-dimension rubric."""
        # AI Agent cannot act as the evaluator/arbiter of scores
        if actor == Principal.AI_AGENT:
            raise EvaluationTamperError("Principal 'ai_agent' cannot score or arbitrate Usage Tests.")

        note_id = note.get("id") or note.get("note_id", "unnamed_note")
        test_id = f"UT-{note_id}-{task.task_id}-{attempt.attempt_number}"

        # 1. Validate Note Prerequisites (Provenance & Schema)
        failure_reason = None
        try:
            self.validate_note_prerequisites(note)
        except Exception as e:
            record = self._build_record(
                test_id=test_id,
                note=note,
                task=task,
                attempt=attempt,
                rubric={d.value: 0 for d in RubricDimension},
                score=0,
                status=UsageTestStatus.FAIL.value,
                failure_reason=f"Prerequisite failure: {e}",
            )
            self._execution_history.append(record)
            return record

        # 2. Validate Source Isolation
        try:
            self.validate_source_isolation(attempt, note)
        except SourceIsolationError as e:
            record = self._build_record(
                test_id=test_id,
                note=note,
                task=task,
                attempt=attempt,
                rubric={d.value: 0 for d in RubricDimension},
                score=0,
                status=UsageTestStatus.FAIL.value,
                failure_reason=str(e),
            )
            self._execution_history.append(record)
            return record

        # 3. Validate Rubric Structure
        for dim in RubricDimension:
            if dim.value not in rubric:
                raise UsageTestValidationError(f"Missing required rubric dimension: '{dim.value}'")
            val = rubric[dim.value]
            if not isinstance(val, int) or val < 0 or val > 2:
                raise UsageTestValidationError(
                    f"Rubric dimension '{dim.value}' score must be integer between 0 and 2 (got {val})"
                )

        # 4. Calculate Score and Status per Policy-02
        corectitudine = rubric[RubricDimension.CORECTITUDINE.value]
        total_score = sum(rubric[d.value] for d in RubricDimension)

        if total_score >= 8 and corectitudine >= 1:
            status = UsageTestStatus.PASS.value
        elif 5 <= total_score <= 7:
            status = UsageTestStatus.RETRY.value
            failure_reason = f"Score {total_score}/10 in retry range (5-7). Note needs refinement."
        else:
            status = UsageTestStatus.FAIL.value
            failure_reason = f"Score {total_score}/10 below threshold (<8) or corectitudine is 0."

        record = self._build_record(
            test_id=test_id,
            note=note,
            task=task,
            attempt=attempt,
            rubric=rubric,
            score=total_score,
            status=status,
            failure_reason=failure_reason,
        )
        self._execution_history.append(record)
        return record

    def evaluate_multi_agent(
        self,
        note: Dict[str, Any],
        task: TaskSpecification,
        attempts: List[EvaluationAttempt],
        rubrics: List[Dict[str, int]],
        actor: Principal = Principal.HUMAN,
    ) -> Tuple[bool, List[UsageTestRecord]]:
        """Perform isolated multi-agent validation for important notes.
        
        Requires minimum 2 distinct agents/models.
        Detects evaluator contamination.
        Both agents must achieve PASS (>= 8/10) for overall PASS.
        """
        if len(attempts) < 2:
            raise UsageTestValidationError("Multi-agent validation requires at least 2 distinct agent attempts.")
        if len(attempts) != len(rubrics):
            raise UsageTestValidationError("Mismatch between number of attempts and provided rubrics.")

        # Check agent distinctness
        agent_ids = [att.agent_id for att in attempts]
        if len(set(agent_ids)) < len(agent_ids):
            raise UsageTestValidationError("Multi-agent validation requires distinct agent IDs.")

        # Contamination check: ensure attempt 2 does not leak attempt 1's score or answer
        for i in range(len(attempts)):
            for j in range(i + 1, len(attempts)):
                ans_j = attempts[j].answer.lower()
                ans_i = attempts[i].answer.lower()
                agent_i = attempts[i].agent_id.lower()
                if f"agent {agent_i}" in ans_j or "previous agent" in ans_j or "agent 1 scored" in ans_j:
                    raise ContaminatedEvaluatorError(
                        f"Evaluator contamination detected: {attempts[j].agent_id} referenced prior agent results."
                    )

        records: List[UsageTestRecord] = []
        all_passed = True

        for att, rub in zip(attempts, rubrics):
            rec = self.evaluate_attempt(note=note, task=task, attempt=att, rubric=rub, actor=actor)
            records.append(rec)
            if rec.status != UsageTestStatus.PASS.value:
                all_passed = False

        return all_passed, records

    def _build_record(
        self,
        test_id: str,
        note: Dict[str, Any],
        task: TaskSpecification,
        attempt: EvaluationAttempt,
        rubric: Dict[str, int],
        score: int,
        status: str,
        failure_reason: Optional[str] = None,
    ) -> UsageTestRecord:
        provenance = {
            "source_title": note.get("source_title", ""),
            "chapter": note.get("chapter", ""),
            "page_range": note.get("page_range", ""),
            "exact_page": note.get("exact_page"),
        }
        rec = UsageTestRecord(
            test_id=test_id,
            note_id=note.get("id") or note.get("note_id", "unnamed_note"),
            task_id=task.task_id,
            task_description=task.description,
            allowed_context=attempt.provided_context,
            model_or_agent=attempt.agent_id,
            attempt=attempt.attempt_number,
            answer=attempt.answer,
            expected_criteria=task.expected_criteria,
            rubric_scores=rubric,
            score=score,
            max_score=10,
            status=status,
            evidence=attempt.evidence,
            failure_reason=failure_reason,
            timestamp=datetime.now(timezone.utc).isoformat(),
            evaluator_id=self.evaluator_id,
            evaluator_version=self.VERSION,
            source_provenance=provenance,
        )
        rec.signature = _compute_test_record_hash(rec)
        return rec


def check_lifecycle_eligibility(
    note: Dict[str, Any],
    test_record: UsageTestRecord,
    conflict_registry: Optional[Any] = None,
) -> Tuple[bool, str]:
    """Check whether a test record qualifies the note for promotion without auto-promoting."""
    # 1. Verify record integrity
    if not test_record.verify_signature():
        return False, "Test record signature verification failed: record has been tampered with."

    # 2. Check score threshold
    if test_record.score < 8:
        return False, f"Usage test score {test_record.score}/10 is below threshold 8."
    if test_record.status != UsageTestStatus.PASS.value:
        return False, f"Usage test status is '{test_record.status}', not PASS."

    # 3. Check conflict blocking
    open_conflicts = note.get("open_conflicts", [])
    for conf in open_conflicts:
        status = conf.get("status", "").lower()
        sev = conf.get("severity", "").lower()
        if status == ConflictStatus.OPEN.value and sev in (ConflictSeverity.HIGH.value, ConflictSeverity.CRITICAL.value):
            return False, f"Open {sev} conflict blocks note eligibility despite passing Usage Test."

    # 4. Epistemic check: hypothesis cannot become mechanism
    epistemic_type = note.get("epistemic_type")
    if epistemic_type == EpistemicType.HYPOTHESIS.value:
        # Note is eligible for verified hypothesis, NOT production mechanism
        return True, "Eligible as VERIFIED HYPOTHESIS. Usage test does not promote to mechanism."

    return True, "Eligible for VERIFIED status (requires formal attestation and owner approval for ACTIVE)."
