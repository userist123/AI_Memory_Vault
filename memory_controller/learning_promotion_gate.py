"""Authorized promotion gate for JARVIS learning candidates."""

from __future__ import annotations

from dataclasses import dataclass
from datetime import datetime, timezone
from typing import Any, Mapping

from .authorizer import Operation, Principal


@dataclass(frozen=True)
class LearningPromotionResult:
    memory_id: str
    reviewer: str
    action: str
    changed: bool
    status: str
    evidence_bundle_hash: str
    confidence_score: float
    confidence_snapshot_fingerprint: str
    promoted_at: str

    def as_dict(self) -> dict[str, Any]:
        return {
            "memory_id": self.memory_id,
            "reviewer": self.reviewer,
            "action": self.action,
            "changed": self.changed,
            "status": self.status,
            "evidence_bundle_hash": self.evidence_bundle_hash,
            "confidence_score": self.confidence_score,
            "confidence_snapshot_fingerprint": self.confidence_snapshot_fingerprint,
            "promoted_at": self.promoted_at,
        }


class LearningPromotionGate:
    """Apply learning promotion only with verified evidence and bound confidence."""

    def __init__(self, controller: Any):
        self.controller = controller

    def apply(
        self,
        *,
        principal: Principal,
        reviewer: str,
        memory_id: str,
        evidence_verification: Mapping[str, Any],
        evidence_bundle_hash: str,
        confidence: Mapping[str, Any],
        confidence_snapshot: Mapping[str, Any],
        action: str = "promote",
    ) -> LearningPromotionResult:
        if not self.controller.authorizer.is_allowed(principal, Operation.PROMOTE):
            raise PermissionError(f"{principal.value} cannot promote learning candidates")
        if principal not in {Principal.HUMAN, Principal.ADMIN}:
            raise PermissionError("Learning promotion requires a human/admin reviewer")
        if not reviewer.strip():
            raise ValueError("Reviewer identity is required")
        if not memory_id.strip():
            raise ValueError("Memory ID is required")
        if not evidence_bundle_hash.strip():
            raise ValueError("Evidence bundle hash is required")
        if not evidence_verification.get("valid"):
            raise ValueError("Verified evidence is required for learning promotion")
        if not evidence_verification.get("bundle_hash_matches"):
            raise ValueError("Evidence bundle hash mismatch")
        if evidence_verification.get("stale_memory_ids") or evidence_verification.get("missing_memory_ids"):
            raise ValueError("Evidence contains stale or missing memories")
        if evidence_verification.get("bundle_hash") != evidence_bundle_hash:
            raise ValueError("Evidence verification hash does not match requested promotion")
        if not bool(confidence.get("promotable")):
            raise ValueError("Learning confidence does not satisfy promotion criteria")
        try:
            confidence_score = float(confidence.get("score"))
        except (TypeError, ValueError) as exc:
            raise ValueError("Learning confidence score is invalid") from exc
        if not 0.0 <= confidence_score <= 1.0:
            raise ValueError("Learning confidence score must be between 0 and 1")
        snapshot_fp = str(confidence_snapshot.get("fingerprint") or "")
        if not snapshot_fp:
            raise ValueError("Confidence snapshot fingerprint is required")
        try:
            snapshot_score = float((confidence_snapshot.get("confidence") or {}).get("score"))
        except (TypeError, ValueError) as exc:
            raise ValueError("Confidence snapshot score is invalid") from exc
        if abs(snapshot_score - confidence_score) >= 1e-9:
            raise ValueError("Confidence does not match bound snapshot")
        if not bool((confidence_snapshot.get("confidence") or {}).get("promotable")):
            raise ValueError("Bound confidence snapshot is not promotable")
        if action != "promote":
            raise ValueError("Learning promotion gate supports only action=promote")

        self.controller.promote(principal, memory_id)
        return LearningPromotionResult(
            memory_id=memory_id,
            reviewer=reviewer.strip(),
            action="promote",
            changed=True,
            status="applied",
            evidence_bundle_hash=evidence_bundle_hash,
            confidence_score=confidence_score,
            confidence_snapshot_fingerprint=snapshot_fp,
            promoted_at=datetime.now(timezone.utc).isoformat(),
        )
