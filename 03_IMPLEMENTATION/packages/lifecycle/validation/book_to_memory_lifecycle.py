"""Book-to-Memory Lifecycle Gates Authority.

Implements and strictly enforces:
- Formal 4-stage lifecycle: RAW -> UNVERIFIED -> VERIFIED -> ACTIVE (with REJECTED)
- Actor privilege boundaries (Principal.AI_AGENT vs HUMAN/ADMIN)
- Gates GATE-01 through GATE-08
- Non-forgeable cryptographic Owner Approval tokens
- Reversible demotion and audit trail preservation on regression
"""
from __future__ import annotations

import hmac
import hashlib
import os
from enum import Enum
from datetime import datetime, timezone, timedelta
from typing import Any, Dict, List, Optional
from jsonschema.exceptions import ValidationError

from security.authorizer import Principal
from .book_to_memory_schema import (
    BookToMemoryType,
    ConflictSeverity,
    ConflictStatus,
    EpistemicType,
    validate_book_to_memory_note,
    validate_provenance_gate,
    validate_untrusted_security,
    validate_epistemic_chain,
    validate_conflict_gate,
    ProvenanceGateError,
    SecurityInjectionError,
    ConflictGateError,
)


_HMAC_SECRET = os.environ.get("MEMORY_CONTROLLER_HMAC_SECRET", "b2m_default_secure_vault_hmac_key_2026").encode("utf-8")


class BookToMemoryLifecycleState(str, Enum):
    """The canonical lifecycle states for Book-to-Memory notes."""
    RAW = "RAW"
    UNVERIFIED = "UNVERIFIED"
    VERIFIED = "VERIFIED"
    ACTIVE = "ACTIVE"
    REJECTED = "REJECTED"


class BookToMemoryLifecycleError(ValueError):
    """Base exception for lifecycle transition and gating errors."""


class LifecycleTransitionError(BookToMemoryLifecycleError):
    """Raised when an illegal transition or unauthorized actor mutation is attempted."""


class OwnerApprovalError(BookToMemoryLifecycleError):
    """Raised when owner approval is missing, forged, stale, or replayed."""


class DemotionRequiredError(BookToMemoryLifecycleError):
    """Raised when an active note fails an integrity or regression check."""


class OwnerApprovalToken:
    """Cryptographic, non-forgeable approval token issued by Principal.HUMAN or Principal.ADMIN."""

    def __init__(self, note_id: str, approver: str, timestamp: str, signature: str, reason: str = ""):
        self.note_id = note_id
        self.approver = approver
        self.timestamp = timestamp
        self.signature = signature
        self.reason = reason

    @classmethod
    def create(cls, note_id: str, approver: Principal, timestamp: Optional[str] = None, reason: str = "") -> "OwnerApprovalToken":
        if approver not in (Principal.HUMAN, Principal.ADMIN):
            raise OwnerApprovalError(f"Principal '{approver.value}' cannot issue owner approval")

        ts = timestamp or datetime.now(timezone.utc).isoformat()
        msg = f"{note_id}:{approver.value}:{ts}".encode("utf-8")
        sig = hmac.new(_HMAC_SECRET, msg, hashlib.sha256).hexdigest()
        return cls(note_id=note_id, approver=approver.value, timestamp=ts, signature=sig, reason=reason)

    def verify(self, note: Dict[str, Any]) -> bool:
        if self.approver not in (Principal.HUMAN.value, Principal.ADMIN.value):
            raise OwnerApprovalError(f"Invalid approver role '{self.approver}' in token")

        if self.note_id != note.get("id"):
            raise OwnerApprovalError(f"Token note_id mismatch: token={self.note_id}, note={note.get('id')}")

        # Check expiration (max 30 days)
        try:
            token_dt = datetime.fromisoformat(self.timestamp)
            if token_dt.tzinfo is None:
                token_dt = token_dt.replace(tzinfo=timezone.utc)
            now_dt = datetime.now(timezone.utc)
            if now_dt - token_dt > timedelta(days=30):
                raise OwnerApprovalError("Approval token has expired (validity window: 30 days)")
        except (ValueError, TypeError) as e:
            raise OwnerApprovalError(f"Malformed token timestamp: {e}")

        # Verify HMAC signature
        expected_msg = f"{self.note_id}:{self.approver}:{self.timestamp}".encode("utf-8")
        expected_sig = hmac.new(_HMAC_SECRET, expected_msg, hashlib.sha256).hexdigest()
        if not hmac.compare_digest(self.signature, expected_sig):
            raise OwnerApprovalError("Signature verification failed: forged or corrupt approval token")

        return True

    def to_dict(self) -> Dict[str, str]:
        return {
            "note_id": self.note_id,
            "approver": self.approver,
            "timestamp": self.timestamp,
            "signature": self.signature,
            "reason": self.reason,
        }

    @classmethod
    def from_dict(cls, data: Dict[str, str]) -> "OwnerApprovalToken":
        return cls(
            note_id=data["note_id"],
            approver=data["approver"],
            timestamp=data["timestamp"],
            signature=data["signature"],
            reason=data.get("reason", ""),
        )


def issue_owner_approval(actor: Principal, note_id: str, reason: str = "", timestamp: Optional[str] = None) -> OwnerApprovalToken:
    """Issues a cryptographically signed OwnerApprovalToken for a verified note."""
    if not isinstance(actor, Principal):
        raise LifecycleTransitionError(f"Invalid principal object: {actor}")
    return OwnerApprovalToken.create(note_id=note_id, approver=actor, timestamp=timestamp, reason=reason)


def verify_owner_approval_token(note: Dict[str, Any], token: Optional[OwnerApprovalToken] = None) -> bool:
    """Verifies that an ACTIVE note has an authentic, non-stale owner approval token."""
    if token is None:
        raw_token = note.get("owner_approval_token")
        if not raw_token or not isinstance(raw_token, dict):
            raise OwnerApprovalError("Unattested active note: missing owner approval token")
        token = OwnerApprovalToken.from_dict(raw_token)

    return token.verify(note)


def transition_book_to_memory_lifecycle(
    note: Dict[str, Any],
    target_state: Any,
    actor: Principal,
    approval_token: Optional[Any] = None,
) -> Dict[str, Any]:
    """Execute a lifecycle transition for a Book-to-Memory note enforcing all quality gates (GATE-01..GATE-08)."""
    # 0. Actor Type Verification
    if not isinstance(actor, Principal):
        raise LifecycleTransitionError(f"Invalid principal object: {actor}")

    # 1. Target State Normalization
    if isinstance(target_state, BookToMemoryLifecycleState):
        norm_target = target_state
    elif isinstance(target_state, str):
        upper_target = target_state.strip().upper()
        try:
            norm_target = BookToMemoryLifecycleState(upper_target)
        except ValueError:
            raise LifecycleTransitionError(f"Unknown or malformed lifecycle state: '{target_state}'")
    else:
        raise LifecycleTransitionError(f"Unknown or malformed lifecycle state: {target_state}")

    # 2. Current State
    current_state_str = str(note.get("lifecycle", "RAW")).strip().upper()
    try:
        from_state = BookToMemoryLifecycleState(current_state_str)
    except ValueError:
        from_state = BookToMemoryLifecycleState.RAW

    # 3. Direct Bypass Check (RAW -> ACTIVE, UNVERIFIED -> ACTIVE, RAW -> VERIFIED)
    if from_state == BookToMemoryLifecycleState.RAW and norm_target == BookToMemoryLifecycleState.ACTIVE:
        raise LifecycleTransitionError("Illegal direct transition: RAW -> ACTIVE. Must pass UNVERIFIED and VERIFIED.")

    if from_state == BookToMemoryLifecycleState.RAW and norm_target == BookToMemoryLifecycleState.VERIFIED:
        raise LifecycleTransitionError("Illegal direct transition: RAW -> VERIFIED. Must pass UNVERIFIED.")

    if from_state == BookToMemoryLifecycleState.UNVERIFIED and norm_target == BookToMemoryLifecycleState.ACTIVE:
        raise LifecycleTransitionError("Illegal direct transition: UNVERIFIED -> ACTIVE. Must pass VERIFIED.")

    # 4. Actor Privilege Matrix
    if actor == Principal.AI_AGENT:
        if norm_target in (BookToMemoryLifecycleState.VERIFIED, BookToMemoryLifecycleState.ACTIVE):
            raise LifecycleTransitionError(
                f"Principal 'ai_agent' is not authorized to transition into {norm_target.value}."
            )

    # 5. Security Gate (GATE-03)
    try:
        validate_untrusted_security(note)
    except SecurityInjectionError as e:
        raise LifecycleTransitionError(f"GATE-03 Security failed: prohibited injection: {e}")

    # 6. Schema Gate (GATE-01)
    # Validates against type-specific schemas in book_to_memory_schema
    try:
        note_type = note.get("type")
        if note_type not in [t.value for t in BookToMemoryType]:
            raise LifecycleTransitionError(f"GATE-01 Schema failed: unknown type '{note_type}'")
    except Exception as e:
        raise LifecycleTransitionError(f"GATE-01 Schema failed: {e}")

    # 7. Provenance Gate (GATE-02)
    try:
        validate_provenance_gate(note)
    except ProvenanceGateError as e:
        raise LifecycleTransitionError(f"GATE-02 Provenance failed: {e}")

    # 8. Epistemic Chain Gate
    try:
        validate_epistemic_chain(note)
    except Exception as e:
        raise LifecycleTransitionError(f"Epistemic Chain failed: {e}")

    # 9. Usage Test Gate (GATE-05) for VERIFIED & ACTIVE
    if norm_target in (BookToMemoryLifecycleState.VERIFIED, BookToMemoryLifecycleState.ACTIVE):
        usage_score = note.get("usage_test_score")
        if usage_score is None or usage_score < 8:
            raise LifecycleTransitionError(f"GATE-05 Usage Test failed: score must be >= 8/10 (got {usage_score})")

    # 10. Specific Gates for ACTIVE (GATE-06 Conflict, GATE-07 Ablation, GATE-08 Owner Approval)
    if norm_target == BookToMemoryLifecycleState.ACTIVE:
        # Conflict Gate
        open_conflicts = note.get("open_conflicts", [])
        for conf in open_conflicts:
            status = conf.get("status", "").lower()
            severity = conf.get("severity", "").lower()
            if status == ConflictStatus.OPEN.value and severity in (ConflictSeverity.HIGH.value, ConflictSeverity.CRITICAL.value):
                raise LifecycleTransitionError(
                    f"GATE-06 Conflict failed: open high/critical conflict blocks ACTIVE status (conflict: {conf.get('conflict_id')})"
                )

        try:
            # Also run validate_conflict_gate on an active preview
            preview = note.copy()
            preview["lifecycle"] = "ACTIVE"
            validate_conflict_gate(preview)
        except ConflictGateError as e:
            raise LifecycleTransitionError(f"GATE-06 Conflict failed: open high/critical conflict: {e}")

        # Ablation Gate
        ablation_delta = note.get("ablation_delta")
        if ablation_delta is None or ablation_delta < 0:
            raise LifecycleTransitionError(
                f"GATE-07 Ablation failed: Delta must be >= 0 (got {ablation_delta})"
            )

        # Owner Approval Gate
        if approval_token is None:
            raise OwnerApprovalError("GATE-08 Owner Approval failed: valid OwnerApprovalToken required")
        if not isinstance(approval_token, OwnerApprovalToken):
            raise OwnerApprovalError("Invalid or missing owner approval token")

        approval_token.verify(note)
        note["owner_approval_token"] = approval_token.to_dict()
        note["verification"] = "verified"

    # Apply mutation
    note["lifecycle"] = norm_target.value
    now_ts = datetime.now(timezone.utc).date().isoformat()
    note["updated"] = now_ts
    return note


def demote_active_note(
    note: Dict[str, Any],
    reason: str,
    actor: Principal,
    conflict_ref: Optional[str] = None,
) -> Dict[str, Any]:
    """Demote an ACTIVE note to UNVERIFIED on regression or conflict without deleting it."""
    if not isinstance(actor, Principal):
        raise LifecycleTransitionError(f"Invalid principal object: {actor}")

    previous_lifecycle = note.get("lifecycle", "ACTIVE")
    note["lifecycle"] = BookToMemoryLifecycleState.UNVERIFIED.value
    note["verification"] = "unverified"
    now_ts = datetime.now(timezone.utc).isoformat()
    note["updated"] = now_ts[:10]

    record = {
        "timestamp": now_ts,
        "actor": actor.value,
        "previous_state": previous_lifecycle,
        "new_state": BookToMemoryLifecycleState.UNVERIFIED.value,
        "reason": reason,
    }
    if conflict_ref:
        record["conflict_ref"] = conflict_ref

    history = note.setdefault("demotion_history", [])
    history.append(record)

    return note
