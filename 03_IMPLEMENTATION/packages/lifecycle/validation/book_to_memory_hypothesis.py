"""Book-to-Memory Problem Matrix Traceability & Systematic Hypothesis Validation Engine.

Implements the formal hypothesis governance authority according to:
- 00_GOVERNANCE/rules/POLICY-LEARNING-QUALITY-02.md (Section 14: Biological -> Mechanism)
- 08_RESEARCH/BOOK_TO_MEMORY/RESEARCH-TRACK-CONTRACT.md (Evidence Chain & Anti-Gaming)
- 08_RESEARCH/BOOK_TO_MEMORY/BOOK_TO_HYPOTHESIS_MAPPING.md
- 08_RESEARCH/BOOK_TO_MEMORY/PROBLEM_MATRIX.md
"""
from __future__ import annotations

import re
import json
import hashlib
from enum import Enum
from dataclasses import dataclass, field, asdict
from datetime import datetime, timezone
from typing import Any, Dict, List, Optional, Set

from memory_controller.authorizer import Principal
from .book_to_memory_schema import (
    BookToMemoryValidationError,
    SecurityInjectionError,
    validate_untrusted_security,
)


class TrackState(str, Enum):
    """Canonical lifecycle states for Book-to-Memory research hypothesis tracks."""
    SOURCE_PENDING = "SOURCE_PENDING"
    HYPOTHESIS_READY = "HYPOTHESIS_READY"
    EXPERIMENT_READY = "EXPERIMENT_READY"
    EVIDENCE_PENDING = "EVIDENCE_PENDING"
    EVIDENCE_AVAILABLE = "EVIDENCE_AVAILABLE"
    DECISION_PENDING = "DECISION_PENDING"
    CLOSED_NO_CHANGE = "CLOSED_NO_CHANGE"
    CLOSED_CHANGE_VALIDATED = "CLOSED_CHANGE_VALIDATED"


CANONICAL_VAULT_PROBLEMS: Dict[str, str] = {
    "retrieval_indirect_cues": "Retrieval / indirect cues",
    "candidate_generation": "Candidate generation / recall",
    "consolidation": "Consolidation",
    "forgetting_decay": "Forgetting / decay",
    "interference_conflict": "Interference / conflict",
    "reconsolidation": "Reconsolidation",
    "context_budget": "Context / Working memory budget",
    "working_memory": "Working memory representation",
    "episodic_semantic": "Episodic vs semantic separation",
    "procedural_memory": "Procedural memory",
    "salience_attention": "Salience / attention",
    "confidence_familiarity": "Confidence / familiarity",
    "meta_memory": "Meta-memory",
    "agent_routing": "Agent routing",
    "feedback_stability": "Feedback / stability",
    "provenance_epistemic": "Provenance / epistemic state",
    "token_economy": "Token economy / disclosure",
}


class HypothesisError(ValueError):
    """Base error for hypothesis registry operations."""


class HypothesisStateTransitionError(HypothesisError):
    """Raised when an invalid or forbidden state progression is attempted."""


class AntiGamingViolationError(HypothesisError):
    """Raised when an evaluation or decision violates anti-gaming rules."""


class UnknownProblemError(HypothesisError):
    """Raised when an unrecognized problem slug is supplied."""


@dataclass
class HypothesisRecord:
    """Audit-proof record of a Book-to-Memory engineering hypothesis."""
    hypothesis_id: str
    problem_slug: str
    source_id: str
    source_location: str
    principle: str
    engineering_hypothesis: str
    mechanism_variant: str
    baseline: str
    control: str
    metric: str
    success_threshold: str
    failure_condition: str
    state: TrackState = TrackState.HYPOTHESIS_READY
    created_at: str = field(default_factory=lambda: datetime.now(timezone.utc).isoformat())
    updated_at: str = field(default_factory=lambda: datetime.now(timezone.utc).isoformat())

    def to_dict(self) -> Dict[str, Any]:
        d = asdict(self)
        d["state"] = self.state.value
        return d


@dataclass
class DecisionRecord:
    """Tamper-evident record of an empirical decision on an engineering hypothesis."""
    decision_id: str
    hypothesis_id: str
    baseline_id: str
    variant_id: str
    sample_count: int
    paired_evaluations: bool
    primary_metric_delta: float
    absolute_delta: float
    failed_run_count: int
    decision_outcome: TrackState
    rationale: str
    evaluator_principal: str = Principal.HUMAN.value
    timestamp: str = field(default_factory=lambda: datetime.now(timezone.utc).isoformat())

    def to_dict(self) -> Dict[str, Any]:
        d = asdict(self)
        d["decision_outcome"] = self.decision_outcome.value
        return d


class BookToMemoryHypothesisRegistry:
    """Formal registry governing the transition from literature findings to production mechanisms.

    Enforces all invariants of POLICY-LEARNING-QUALITY-02 Section 14 and RESEARCH-TRACK-CONTRACT.md:
    1. Evidence Chain: SOURCE -> HYPOTHESIS -> EXPERIMENT -> RUNTIME EVIDENCE -> DECISION
    2. Zero Jump: Cannot transition from HYPOTHESIS_READY directly to CLOSED_CHANGE_VALIDATED.
    3. Anti-Gaming: Failed runs cannot be omitted; absolute and relative deltas both required.
    4. Principal Scoping: Only HUMAN/ADMIN can approve a CLOSED_CHANGE_VALIDATED decision.
    """

    VERSION: str = "1.0.0"

    # Permitted forward transitions in evidence machine
    _VALID_TRANSITIONS: Dict[TrackState, Set[TrackState]] = {
        TrackState.SOURCE_PENDING: {TrackState.HYPOTHESIS_READY, TrackState.CLOSED_NO_CHANGE},
        TrackState.HYPOTHESIS_READY: {TrackState.EXPERIMENT_READY, TrackState.CLOSED_NO_CHANGE},
        TrackState.EXPERIMENT_READY: {TrackState.EVIDENCE_PENDING, TrackState.CLOSED_NO_CHANGE},
        TrackState.EVIDENCE_PENDING: {TrackState.EVIDENCE_AVAILABLE, TrackState.CLOSED_NO_CHANGE},
        TrackState.EVIDENCE_AVAILABLE: {TrackState.DECISION_PENDING, TrackState.CLOSED_NO_CHANGE},
        TrackState.DECISION_PENDING: {TrackState.CLOSED_CHANGE_VALIDATED, TrackState.CLOSED_NO_CHANGE},
        TrackState.CLOSED_NO_CHANGE: set(),
        TrackState.CLOSED_CHANGE_VALIDATED: set(),
    }

    def __init__(self):
        self._hypotheses: Dict[str, HypothesisRecord] = {}
        self._decisions: Dict[str, DecisionRecord] = {}

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
        initial_state: TrackState = TrackState.HYPOTHESIS_READY,
        **extra_fields: Any,
    ) -> HypothesisRecord:
        """Register and validate a new engineering hypothesis."""
        clean_hid = hypothesis_id.strip()
        if clean_hid in self._hypotheses:
            raise HypothesisError(f"Hypothesis '{clean_hid}' is already registered.")

        clean_slug = problem_slug.strip().lower()
        if clean_slug not in CANONICAL_VAULT_PROBLEMS:
            raise UnknownProblemError(
                f"Unknown problem slug '{clean_slug}'. Must be one of {list(CANONICAL_VAULT_PROBLEMS.keys())}."
            )

        # Validate minimum string lengths and content
        payload = {
            "hypothesis_id": clean_hid,
            "problem_slug": clean_slug,
            "source_id": source_id.strip(),
            "source_location": source_location.strip(),
            "principle": principle.strip(),
            "engineering_hypothesis": engineering_hypothesis.strip(),
            "mechanism_variant": mechanism_variant.strip(),
            "baseline": baseline.strip(),
            "control": control.strip(),
            "metric": metric.strip(),
            "success_threshold": success_threshold.strip(),
            "failure_condition": failure_condition.strip(),
        }
        payload.update(extra_fields)

        # Security check against prompt/directive injection
        validate_untrusted_security(payload)

        # Ensure required fields have substantive content
        for k, v in payload.items():
            if isinstance(v, str) and len(v.strip()) < 3:
                raise BookToMemoryValidationError(f"Field '{k}' has insufficient length ({len(v.strip())} < 3).")

        now_ts = datetime.now(timezone.utc).isoformat()
        record = HypothesisRecord(
            hypothesis_id=clean_hid,
            problem_slug=clean_slug,
            source_id=source_id.strip(),
            source_location=source_location.strip(),
            principle=principle.strip(),
            engineering_hypothesis=engineering_hypothesis.strip(),
            mechanism_variant=mechanism_variant.strip(),
            baseline=baseline.strip(),
            control=control.strip(),
            metric=metric.strip(),
            success_threshold=success_threshold.strip(),
            failure_condition=failure_condition.strip(),
            state=initial_state,
            created_at=now_ts,
            updated_at=now_ts,
        )
        self._hypotheses[clean_hid] = record
        return record

    def get_hypothesis(self, hypothesis_id: str) -> Optional[HypothesisRecord]:
        return self._hypotheses.get(hypothesis_id.strip())

    def list_hypotheses(self, problem_slug: Optional[str] = None) -> List[HypothesisRecord]:
        results = list(self._hypotheses.values())
        if problem_slug:
            clean_slug = problem_slug.strip().lower()
            results = [h for h in results if h.problem_slug == clean_slug]
        return sorted(results, key=lambda h: h.hypothesis_id)

    def transition_state(
        self,
        hypothesis_id: str,
        target_state: TrackState,
        actor: Principal = Principal.HUMAN,
    ) -> HypothesisRecord:
        """Advance a hypothesis track through the formal evidence state machine."""
        clean_hid = hypothesis_id.strip()
        record = self.get_hypothesis(clean_hid)
        if not record:
            raise HypothesisError(f"Hypothesis '{clean_hid}' not found.")

        current = record.state
        if target_state == current:
            return record

        allowed = self._VALID_TRANSITIONS.get(current, set())
        if target_state not in allowed:
            raise HypothesisStateTransitionError(
                f"GATE VIOLATION: Cannot transition directly from {current.value} to {target_state.value}. "
                f"Permitted next states: {[s.value for s in allowed]}."
            )

        # Principal authorization gate
        if target_state == TrackState.CLOSED_CHANGE_VALIDATED:
            if actor == Principal.AI_AGENT:
                raise HypothesisStateTransitionError(
                    "Principal 'ai_agent' cannot authorize CLOSED_CHANGE_VALIDATED (Owner attestation required)."
                )
            if clean_hid not in self._decisions:
                raise HypothesisStateTransitionError(
                    f"Cannot validate change for hypothesis '{clean_hid}' without an attached DecisionRecord."
                )

        record.state = target_state
        record.updated_at = datetime.now(timezone.utc).isoformat()
        return record

    def record_decision(
        self,
        decision: DecisionRecord,
        actor: Principal = Principal.HUMAN,
    ) -> DecisionRecord:
        """Register an empirical decision enforcing Anti-Gaming rules from RESEARCH-TRACK-CONTRACT.md."""
        clean_hid = decision.hypothesis_id.strip()
        record = self.get_hypothesis(clean_hid)
        if not record:
            raise HypothesisError(f"Cannot record decision for unregistered hypothesis '{clean_hid}'.")

        # Anti-Gaming Rule 1: Zero-sample or non-paired evaluation
        if decision.sample_count < 5:
            raise AntiGamingViolationError(
                f"Anti-Gaming Rule: Sample count must be >= 5 (got {decision.sample_count})."
            )
        if not decision.paired_evaluations:
            raise AntiGamingViolationError(
                "Anti-Gaming Rule: Evaluation must be strictly paired between baseline and variant."
            )

        # Anti-Gaming Rule 2: Cannot omit failed runs to manufacture wins
        if decision.failed_run_count < 0:
            raise AntiGamingViolationError("Failed run count cannot be negative.")

        # Anti-Gaming Rule 3: Absolute delta required alongside relative delta
        if decision.absolute_delta is None:
            raise AntiGamingViolationError(
                "Anti-Gaming Rule: Absolute delta must be recorded alongside relative metric delta."
            )

        # Authorization: ai_agent cannot approve CLOSED_CHANGE_VALIDATED
        if decision.decision_outcome == TrackState.CLOSED_CHANGE_VALIDATED and actor == Principal.AI_AGENT:
            raise HypothesisStateTransitionError(
                "Principal 'ai_agent' cannot sign a CLOSED_CHANGE_VALIDATED decision."
            )

        self._decisions[clean_hid] = decision
        return decision

    def get_decision(self, hypothesis_id: str) -> Optional[DecisionRecord]:
        return self._decisions.get(hypothesis_id.strip())

    def get_problem_matrix_status(self) -> Dict[str, Any]:
        """Compute the traceability and evidence resolution status across the 16 vault problems."""
        matrix: Dict[str, Dict[str, Any]] = {}
        resolved_count = 0
        active_hypotheses_count = len(self._hypotheses)

        for slug, name in CANONICAL_VAULT_PROBLEMS.items():
            matched = [h for h in self._hypotheses.values() if h.problem_slug == slug]
            states = [h.state.value for h in matched]
            is_resolved = any(
                h.state in (TrackState.CLOSED_CHANGE_VALIDATED, TrackState.CLOSED_NO_CHANGE)
                for h in matched
            )
            if is_resolved:
                resolved_count += 1

            matrix[slug] = {
                "name": name,
                "hypotheses_count": len(matched),
                "hypothesis_ids": [h.hypothesis_id for h in matched],
                "active_states": states,
                "status": "RESOLVED" if is_resolved else ("INVESTIGATING" if matched else "PENDING_FORMULATION"),
            }

        resolution_ratio = round(resolved_count / len(CANONICAL_VAULT_PROBLEMS), 4)

        return {
            "total_canonical_problems": len(CANONICAL_VAULT_PROBLEMS),
            "resolved_problems_count": resolved_count,
            "investigating_problems_count": sum(1 for m in matrix.values() if m["status"] == "INVESTIGATING"),
            "pending_formulation_count": sum(1 for m in matrix.values() if m["status"] == "PENDING_FORMULATION"),
            "resolution_ratio": resolution_ratio,
            "total_registered_hypotheses": active_hypotheses_count,
            "problems": matrix,
        }

    def compute_registry_digest(self) -> str:
        """Compute deterministic SHA-256 fingerprint of the hypothesis registry state."""
        canonical_payload = {
            "hypotheses": [
                {
                    "hypothesis_id": h.hypothesis_id,
                    "problem_slug": h.problem_slug,
                    "state": h.state.value,
                }
                for h in sorted(self._hypotheses.values(), key=lambda h: h.hypothesis_id)
            ],
            "decisions": [
                {
                    "hypothesis_id": d.hypothesis_id,
                    "decision_outcome": d.decision_outcome.value,
                    "primary_metric_delta": d.primary_metric_delta,
                }
                for d in sorted(self._decisions.values(), key=lambda d: d.hypothesis_id)
            ],
        }
        return hashlib.sha256(json.dumps(canonical_payload, sort_keys=True).encode("utf-8")).hexdigest()
