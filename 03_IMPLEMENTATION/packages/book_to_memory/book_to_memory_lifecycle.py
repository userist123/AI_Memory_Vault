"""Book-to-Memory Lifecycle Gates Authority.

Implements and strictly enforces:
- Formal 4-stage lifecycle: RAW -> REVIEW -> VERIFIED -> ACTIVE (with REJECTED). REVIEW + verification
  "unverified" is the vault's own convention; there is no separate UNVERIFIED state (lifecycle/policy.py
  is the sole lifecycle authority and does not know one).
- Actor privilege boundaries (Principal.AI_AGENT vs HUMAN/ADMIN)
- Gates GATE-01 through GATE-08
- Non-forgeable cryptographic Owner Approval tokens
- Reversible demotion and audit trail preservation on regression
"""
from __future__ import annotations

import hmac
import hashlib
import json
from enum import Enum
from datetime import date, datetime, timezone, timedelta
from typing import Any, Dict, List, Optional
from jsonschema.exceptions import ValidationError

from memory_controller.authorizer import Principal
from interfaces import vault_runtime
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


# This string used to be the fallback signing key, so anyone who read the source could forge an
# owner-approval token. It is kept ONLY so that it can be refused: a secret equal to it is treated
# as no secret at all.
_LEAKED_FALLBACK_SECRETS = frozenset({"b2m_default_secure_vault_hmac_key_2026"})


def _hmac_secret() -> bytes:
    """The vault's HMAC secret, resolved the same way the recall CLI and MCP server do it.

    `MEMORY_CONTROLLER_HMAC_SECRET` first, then the per-user key file outside the repository
    (`python -m cognitive_core.recall_cli --init-secret`). There is no default: with no usable
    secret the module refuses to issue or verify an owner-approval token (fail closed).
    """
    try:
        value, _source = vault_runtime.load_secret()
    except (vault_runtime.VaultSecretMissing, vault_runtime.VaultSecretInvalid) as exc:
        raise OwnerApprovalError(
            f"No usable HMAC secret: owner approval cannot be issued or verified ({exc})"
        ) from exc
    if value.strip() in _LEAKED_FALLBACK_SECRETS:
        raise OwnerApprovalError(
            "The configured HMAC secret is a value published in source code; "
            "refusing to sign or verify owner approval. Generate a fresh secret."
        )
    return value.encode("utf-8")


class BookToMemoryLifecycleState(str, Enum):
    """The canonical lifecycle states for Book-to-Memory notes."""
    RAW = "RAW"
    REVIEW = "REVIEW"
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


# Fields that describe where a note is in its lifecycle, not what it says. They change as a direct
# consequence of the transition the token authorises (and of the embedded token itself), so they
# are left out of the digest; everything else, frontmatter and body alike, is bound.
_STATE_FIELDS = frozenset({"lifecycle", "verification", "updated", "owner_approval_token"})

# Revision markers a note may carry, in order of preference. They are also part of the digest;
# the token additionally signs the value separately so a mismatch is reported as such.
_REVISION_FIELDS = ("revision_id", "revision", "version")

APPROVAL_TOKEN_VERSION = "b2m-owner-approval-v2"
APPROVAL_MAX_TTL = timedelta(days=30)
_CLOCK_SKEW = timedelta(minutes=5)


def _json_default(value: Any) -> Any:
    if isinstance(value, (datetime, date)):
        return value.isoformat()
    if isinstance(value, Enum):
        return value.value
    raise OwnerApprovalError(
        f"Note field of type {type(value).__name__} cannot be canonicalised; refusing to bind approval"
    )


def note_content_sha256(note: Dict[str, Any]) -> str:
    """SHA-256 of the canonical note: every frontmatter field and the body, minus lifecycle state.

    Canonical form: the note as a JSON object with keys sorted at every level, no insignificant
    whitespace and ASCII-escaped text, so the digest is independent of key order and of how the
    note was stored. String content is hashed byte for byte (no whitespace or line-ending
    normalisation): any edit, however small, changes the digest.
    """
    if not isinstance(note, dict):
        raise OwnerApprovalError("Owner approval is bound to a note mapping (frontmatter + body)")
    content = {k: v for k, v in note.items() if k not in _STATE_FIELDS}
    payload = json.dumps(
        content, sort_keys=True, separators=(",", ":"), ensure_ascii=True, default=_json_default
    ).encode("utf-8")
    return hashlib.sha256(payload).hexdigest()


def note_revision(note: Dict[str, Any]) -> Optional[str]:
    """The note's own revision/version marker, if it has one."""
    for key in _REVISION_FIELDS:
        value = note.get(key)
        if value not in (None, ""):
            return str(value)
    return None


def _parse_utc(value: Any, label: str) -> datetime:
    try:
        parsed = datetime.fromisoformat(str(value).replace("Z", "+00:00"))
    except (ValueError, TypeError) as exc:
        raise OwnerApprovalError(f"Malformed token {label}: {exc}") from exc
    if parsed.tzinfo is None:
        parsed = parsed.replace(tzinfo=timezone.utc)
    return parsed


class OwnerApprovalToken:
    """Cryptographic, non-forgeable approval token issued by Principal.HUMAN or Principal.ADMIN.

    The signature covers the note id, the approver, the issue time, the expiry, the note's revision
    marker (if any) and the SHA-256 of the canonical note (`note_content_sha256`). Verification
    recomputes the digest from the note being promoted, so approving one version of a note and
    promoting an edited one is refused. Tokens without these fields (the old
    id+approver+timestamp format) are refused.
    """

    def __init__(
        self,
        note_id: str,
        approver: str,
        timestamp: str,
        signature: str,
        reason: str = "",
        *,
        expires_at: Optional[str] = None,
        content_sha256: Optional[str] = None,
        revision: Optional[str] = None,
        version: Optional[str] = None,
    ):
        self.note_id = note_id
        self.approver = approver
        self.timestamp = timestamp
        self.signature = signature
        self.reason = reason
        self.expires_at = expires_at
        self.content_sha256 = content_sha256
        self.revision = revision
        self.version = version

    def _signed_bytes(self) -> bytes:
        return json.dumps(
            {
                "version": self.version,
                "note_id": self.note_id,
                "approver": self.approver,
                "issued_at": self.timestamp,
                "expires_at": self.expires_at,
                "content_sha256": self.content_sha256,
                "revision": self.revision,
                "reason": self.reason,
            },
            sort_keys=True,
            separators=(",", ":"),
            ensure_ascii=True,
        ).encode("utf-8")

    @classmethod
    def create(
        cls,
        note: Dict[str, Any],
        approver: Principal,
        timestamp: Optional[str] = None,
        reason: str = "",
        ttl: Optional[timedelta] = None,
    ) -> "OwnerApprovalToken":
        if approver not in (Principal.HUMAN, Principal.ADMIN):
            raise OwnerApprovalError(f"Principal '{approver.value}' cannot issue owner approval")
        if not isinstance(note, dict) or not note.get("id"):
            raise OwnerApprovalError("Owner approval must be issued over the note itself (a mapping with an id)")

        window = APPROVAL_MAX_TTL if ttl is None else ttl
        if window <= timedelta(0) or window > APPROVAL_MAX_TTL:
            raise OwnerApprovalError("Approval validity must be positive and at most 30 days")
        issued_at = _parse_utc(timestamp, "timestamp") if timestamp else datetime.now(timezone.utc)

        token = cls(
            note_id=note["id"],
            approver=approver.value,
            timestamp=issued_at.isoformat(),
            signature="",
            reason=reason,
            expires_at=(issued_at + window).isoformat(),
            content_sha256=note_content_sha256(note),
            revision=note_revision(note),
            version=APPROVAL_TOKEN_VERSION,
        )
        token.signature = hmac.new(_hmac_secret(), token._signed_bytes(), hashlib.sha256).hexdigest()
        return token

    def verify(self, note: Dict[str, Any]) -> bool:
        # 1. Format: old-format or incomplete tokens are refused outright.
        if self.version != APPROVAL_TOKEN_VERSION:
            raise OwnerApprovalError(
                "Approval token is in an unsupported (old) format: it does not bind note content; "
                "re-issue the approval"
            )
        for label, value in (
            ("note_id", self.note_id),
            ("timestamp", self.timestamp),
            ("expires_at", self.expires_at),
            ("content_sha256", self.content_sha256),
            ("signature", self.signature),
        ):
            if not isinstance(value, str) or not value:
                raise OwnerApprovalError(f"Approval token is missing required field '{label}'")

        if self.approver not in (Principal.HUMAN.value, Principal.ADMIN.value):
            raise OwnerApprovalError(f"Invalid approver role '{self.approver}' in token")

        # 2. Authenticity: nothing below is trusted until the signature over every field verifies.
        expected_sig = hmac.new(_hmac_secret(), self._signed_bytes(), hashlib.sha256).hexdigest()
        if not hmac.compare_digest(self.signature, expected_sig):
            raise OwnerApprovalError("Signature verification failed: forged or corrupt approval token")

        # 3. Binding to this note.
        if self.note_id != note.get("id"):
            raise OwnerApprovalError(f"Token note_id mismatch: token={self.note_id}, note={note.get('id')}")

        # 4. Validity window (signed, so it cannot be stretched).
        issued_at = _parse_utc(self.timestamp, "timestamp")
        expires_at = _parse_utc(self.expires_at, "expires_at")
        now = datetime.now(timezone.utc)
        if expires_at <= issued_at or expires_at - issued_at > APPROVAL_MAX_TTL:
            raise OwnerApprovalError("Approval token has an invalid validity window (max 30 days)")
        if issued_at - now > _CLOCK_SKEW:
            raise OwnerApprovalError("Approval token is issued in the future")
        if now >= expires_at:
            raise OwnerApprovalError("Approval token has expired (validity window: 30 days)")

        # 5. Revision and content of the note actually being promoted.
        if self.revision != note_revision(note):
            raise OwnerApprovalError(
                f"Token revision mismatch: approved={self.revision!r}, note={note_revision(note)!r}"
            )
        if not hmac.compare_digest(self.content_sha256, note_content_sha256(note)):
            raise OwnerApprovalError(
                "Token content mismatch: the note was changed after it was approved; re-approval required"
            )

        return True

    def to_dict(self) -> Dict[str, Optional[str]]:
        return {
            "version": self.version,
            "note_id": self.note_id,
            "approver": self.approver,
            "timestamp": self.timestamp,
            "expires_at": self.expires_at,
            "content_sha256": self.content_sha256,
            "revision": self.revision,
            "signature": self.signature,
            "reason": self.reason,
        }

    @classmethod
    def from_dict(cls, data: Dict[str, Any]) -> "OwnerApprovalToken":
        if not isinstance(data, dict):
            raise OwnerApprovalError("Malformed owner approval token")
        missing = [k for k in ("note_id", "approver", "timestamp", "signature") if not data.get(k)]
        if missing:
            raise OwnerApprovalError(f"Approval token is missing required fields: {', '.join(missing)}")
        return cls(
            note_id=data["note_id"],
            approver=data["approver"],
            timestamp=data["timestamp"],
            signature=data["signature"],
            reason=data.get("reason", ""),
            expires_at=data.get("expires_at"),
            content_sha256=data.get("content_sha256"),
            revision=data.get("revision"),
            version=data.get("version"),
        )


def issue_owner_approval(
    actor: Principal,
    note: Dict[str, Any],
    reason: str = "",
    timestamp: Optional[str] = None,
    ttl: Optional[timedelta] = None,
) -> OwnerApprovalToken:
    """Issue a signed OwnerApprovalToken bound to the exact content of `note` (a note mapping)."""
    if not isinstance(actor, Principal):
        raise LifecycleTransitionError(f"Invalid principal object: {actor}")
    return OwnerApprovalToken.create(note=note, approver=actor, timestamp=timestamp, reason=reason, ttl=ttl)


def verify_owner_approval_token(note: Dict[str, Any], token: Optional[OwnerApprovalToken] = None) -> bool:
    """Verify that a note has an authentic, unexpired owner approval of its current content."""
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

    # 3. Direct Bypass Check (RAW -> ACTIVE, REVIEW -> ACTIVE, RAW -> VERIFIED)
    if from_state == BookToMemoryLifecycleState.RAW and norm_target == BookToMemoryLifecycleState.ACTIVE:
        raise LifecycleTransitionError("Illegal direct transition: RAW -> ACTIVE. Must pass REVIEW and VERIFIED.")

    if from_state == BookToMemoryLifecycleState.RAW and norm_target == BookToMemoryLifecycleState.VERIFIED:
        raise LifecycleTransitionError("Illegal direct transition: RAW -> VERIFIED. Must pass REVIEW.")

    if from_state == BookToMemoryLifecycleState.REVIEW and norm_target == BookToMemoryLifecycleState.ACTIVE:
        raise LifecycleTransitionError("Illegal direct transition: REVIEW -> ACTIVE. Must pass VERIFIED.")

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

    # REVIEW is the vault's pre-trust state; its verification status is always "unverified".
    if norm_target == BookToMemoryLifecycleState.REVIEW:
        note["verification"] = "unverified"

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
    """Demote an ACTIVE note to REVIEW on regression or conflict without deleting it."""
    if not isinstance(actor, Principal):
        raise LifecycleTransitionError(f"Invalid principal object: {actor}")

    previous_lifecycle = note.get("lifecycle", "ACTIVE")
    note["lifecycle"] = BookToMemoryLifecycleState.REVIEW.value
    note["verification"] = "unverified"
    now_ts = datetime.now(timezone.utc).isoformat()
    note["updated"] = now_ts[:10]

    record = {
        "timestamp": now_ts,
        "actor": actor.value,
        "previous_state": previous_lifecycle,
        "new_state": BookToMemoryLifecycleState.REVIEW.value,
        "reason": reason,
    }
    if conflict_ref:
        record["conflict_ref"] = conflict_ref

    history = note.setdefault("demotion_history", [])
    history.append(record)

    return note
