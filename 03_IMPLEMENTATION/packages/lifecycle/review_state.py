"""Fail-closed state machine for auditable memory review workflows."""

from __future__ import annotations

from dataclasses import dataclass
from enum import Enum
from typing import Any


class ReviewState(str, Enum):
    OPEN = "OPEN"
    EVIDENCE_PENDING = "EVIDENCE_PENDING"
    VERIFIED = "VERIFIED"
    DECISION_PENDING = "DECISION_PENDING"
    APPROVED = "APPROVED"
    REJECTED = "REJECTED"
    DEFERRED = "DEFERRED"
    CLOSED = "CLOSED"


_ALLOWED: dict[ReviewState, set[ReviewState]] = {
    ReviewState.OPEN: {ReviewState.EVIDENCE_PENDING, ReviewState.DEFERRED},
    ReviewState.EVIDENCE_PENDING: {ReviewState.VERIFIED, ReviewState.DEFERRED},
    ReviewState.VERIFIED: {ReviewState.DECISION_PENDING, ReviewState.DEFERRED},
    ReviewState.DECISION_PENDING: {ReviewState.APPROVED, ReviewState.REJECTED, ReviewState.DEFERRED},
    ReviewState.APPROVED: {ReviewState.CLOSED},
    ReviewState.REJECTED: {ReviewState.CLOSED},
    ReviewState.DEFERRED: {ReviewState.EVIDENCE_PENDING, ReviewState.DECISION_PENDING, ReviewState.CLOSED},
    ReviewState.CLOSED: set(),
}


@dataclass(frozen=True)
class ReviewTransition:
    case_id: str
    previous: ReviewState
    current: ReviewState
    actor: str
    reason: str

    def as_dict(self) -> dict[str, Any]:
        return {
            "case_id": self.case_id,
            "previous": self.previous.value,
            "current": self.current.value,
            "actor": self.actor,
            "reason": self.reason,
        }


@dataclass
class ReviewStateMachine:
    case_id: str
    state: ReviewState = ReviewState.OPEN

    def transition(self, target: ReviewState | str, *, actor: str, reason: str) -> ReviewTransition:
        target = ReviewState(target)
        if not actor.strip():
            raise ValueError("Review actor is required")
        if not reason.strip():
            raise ValueError("Transition reason is required")
        if target not in _ALLOWED[self.state]:
            raise ValueError(f"Invalid review transition: {self.state.value} -> {target.value}")
        transition = ReviewTransition(self.case_id, self.state, target, actor.strip(), reason.strip())
        self.state = target
        return transition

    def can_apply_mutation(self) -> bool:
        return self.state is ReviewState.APPROVED

    def as_dict(self) -> dict[str, Any]:
        return {"case_id": self.case_id, "state": self.state.value, "can_apply_mutation": self.can_apply_mutation()}
