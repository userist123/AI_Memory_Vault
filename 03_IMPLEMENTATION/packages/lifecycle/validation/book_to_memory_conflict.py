"""Book-to-Memory Conflict Registry and Management.

Implements the formal Conflict Management authority according to:
- 00_GOVERNANCE/rules/POLICY-LEARNING-QUALITY-02.md (Section 5: Contradictii)
- 00_GOVERNANCE/protocols/Confidence_Model.md
- 03_IMPLEMENTATION/packages/lifecycle/validation/book_to_memory_schema.py
- 03_IMPLEMENTATION/packages/lifecycle/validation/book_to_memory_lifecycle.py
"""
from __future__ import annotations

import re
import hashlib
from enum import Enum
from datetime import datetime, timezone
from typing import Any, Dict, List, Optional

from memory_controller.authorizer import Principal
from .book_to_memory_schema import (
    ConflictSeverity,
    ConflictStatus,
    ProvenanceGateError,
    _LAZY_PROVENANCE_PATTERNS,
)


class ConflictResolutionType(str, Enum):
    """Canonical resolution outcomes for book conflicts."""
    EVIDENCE_STRONGER_FOR_A = "evidence_stronger_for_a"
    EVIDENCE_STRONGER_FOR_B = "evidence_stronger_for_b"
    CONTEXT_DEPENDENT_BOTH_VALID = "context_dependent_both_valid"
    SOURCE_OBSOLETE = "source_obsolete"
    INSUFFICIENT_EVIDENCE = "insufficient_evidence"
    UNRESOLVED = "unresolved"


class ConflictError(ValueError):
    """Base exception for conflict registry errors."""


class ConflictValidationError(ConflictError):
    """Raised when conflict schema or identity format is invalid."""


class ConflictPermissionError(ConflictError):
    """Raised when an unauthorized actor attempts a privileged conflict operation."""


class DuplicateConflictError(ConflictError):
    """Raised when an identical conflict divergence is already registered."""


_DOMAIN_PATTERN = re.compile(r"^[a-zA-Z0-9_\-]+$")
_FORMULA_PATTERNS = [
    re.compile(r"[=^]\s*[-+]?[0-9]"),
    re.compile(r"\bO\([^\)]+\)"),
    re.compile(r"\b(sqrt|delta|zeta)\b", re.IGNORECASE),
    re.compile(r"\b\d+(\.\d+)?\s*[\+\*\/\^]\s*\w+\b"),
    re.compile(r"\bformula\b|\bequation\b", re.IGNORECASE),
]


def generate_conflict_id(domain: str, claim_a: str, claim_b: str) -> str:
    """Generate a deterministic, order-independent conflict identifier: CONFLICT-<domain>-<slug>."""
    if not domain or not isinstance(domain, str) or not _DOMAIN_PATTERN.match(domain.strip()):
        raise ConflictValidationError(
            f"Invalid domain identifier '{domain}'. Domain must be non-empty and alphanumeric with underscores/hyphens."
        )

    # Normalize claims
    clean_a = re.sub(r"\s+", " ", claim_a.strip().lower())
    clean_b = re.sub(r"\s+", " ", claim_b.strip().lower())

    if not clean_a or not clean_b:
        raise ConflictValidationError("Conflict claims cannot be empty strings.")

    # Canonical order invariance (A vs B is identical to B vs A)
    pair = sorted([clean_a, clean_b])
    pair_digest = hashlib.sha256(f"{pair[0]}|||{pair[1]}".encode("utf-8")).hexdigest()[:16]

    # Human-readable prefix derived from first claim
    words = re.findall(r"[a-z0-9]+", pair[0])[:4]
    prefix = "_".join(words)[:24].strip("_") or "divergence"

    slug = f"{prefix}_{pair_digest}"
    return f"CONFLICT-{domain.strip()}-{slug}"


def _validate_position_provenance(pos_name: str, claim: str, source: Dict[str, Any], evidence: str) -> None:
    if not source or not isinstance(source, dict):
        raise ProvenanceGateError(f"Missing required provenance dictionary for {pos_name}.")

    source_title = source.get("source_title")
    if not source_title or not isinstance(source_title, str) or not source_title.strip():
        raise ProvenanceGateError(f"Missing required provenance fields: 'source_title' is mandatory for {pos_name}.")

    # Allow own experience
    if source_title.strip().lower() in ("experienta proprie", "own experience"):
        return

    chapter = source.get("chapter")
    page_range = source.get("page_range")

    if not chapter or not isinstance(chapter, str) or not chapter.strip():
        raise ProvenanceGateError(f"Missing required provenance fields: 'chapter' is mandatory for {pos_name}.")

    if not page_range or not str(page_range).strip():
        raise ProvenanceGateError(f"Missing required provenance fields: 'page_range' is mandatory for {pos_name}.")

    # Check lazy strings
    for field_name, value in [("source_title", source_title), ("chapter", chapter), ("page_range", str(page_range))]:
        for pattern in _LAZY_PROVENANCE_PATTERNS:
            if pattern.search(value):
                raise ProvenanceGateError(f"Lazy provenance string detected in {pos_name} '{field_name}': '{value}'.")

    # Formula exact page check
    has_formula = any(p.search(claim + " " + evidence) for p in _FORMULA_PATTERNS)
    if has_formula:
        exact_page = source.get("exact_page")
        if exact_page is None or not str(exact_page).strip():
            raise ProvenanceGateError(
                f"exact_page is mandatory for precise formulas, numbers, and critical calculations in {pos_name}."
            )


def validate_conflict_record(data: Dict[str, Any]) -> bool:
    """Validate a conflict payload against all required fields, provenance, and schemas."""
    required = ["domain", "claim_a", "source_a", "claim_b", "source_b", "evidence_a", "evidence_b", "severity"]
    for rk in required:
        if rk not in data or data[rk] is None:
            raise ConflictValidationError(f"Missing required field in conflict record: '{rk}'")

    _validate_position_provenance("source_a", data["claim_a"], data["source_a"], data["evidence_a"])
    _validate_position_provenance("source_b", data["claim_b"], data["source_b"], data["evidence_b"])

    sev = data["severity"]
    if sev not in [s.value for s in ConflictSeverity]:
        raise ConflictValidationError(f"Invalid conflict severity '{sev}'. Must be one of {[s.value for s in ConflictSeverity]}")

    return True


class ConflictRecord:
    """A first-class record of an empirical or conceptual conflict between two sources."""

    def __init__(
        self,
        id: str,
        domain: str,
        title: str,
        claim_a: str,
        source_a: Dict[str, Any],
        evidence_a: str,
        claim_b: str,
        source_b: Dict[str, Any],
        evidence_b: str,
        severity: str,
        status: str = ConflictStatus.OPEN.value,
        resolution_state: Optional[str] = None,
        resolution_evidence: Optional[str] = None,
        resolution_actor: Optional[str] = None,
        resolution_timestamp: Optional[str] = None,
        created_at: Optional[str] = None,
        created_by: str = "human",
        updated_at: Optional[str] = None,
        history: Optional[List[Dict[str, Any]]] = None,
    ):
        self.id = id
        self.domain = domain
        self.title = title
        self.claim_a = claim_a
        self.source_a = source_a
        self.evidence_a = evidence_a
        self.claim_b = claim_b
        self.source_b = source_b
        self.evidence_b = evidence_b
        self.severity = severity
        self.status = status
        self.resolution_state = resolution_state
        self.resolution_evidence = resolution_evidence
        self.resolution_actor = resolution_actor
        self.resolution_timestamp = resolution_timestamp
        self.created_at = created_at or datetime.now(timezone.utc).isoformat()
        self.created_by = created_by
        self.updated_at = updated_at or self.created_at
        self.history = history or []

    @classmethod
    def from_dict(cls, data: Dict[str, Any]) -> "ConflictRecord":
        validate_conflict_record(data)
        domain = data["domain"]
        claim_a = data["claim_a"]
        claim_b = data["claim_b"]
        cid = data.get("id") or generate_conflict_id(domain, claim_a, claim_b)

        return cls(
            id=cid,
            domain=domain,
            title=data.get("title", f"Conflict in {domain}"),
            claim_a=claim_a,
            source_a=data["source_a"],
            evidence_a=data["evidence_a"],
            claim_b=claim_b,
            source_b=data["source_b"],
            evidence_b=data["evidence_b"],
            severity=data["severity"],
            status=data.get("status", ConflictStatus.OPEN.value),
            resolution_state=data.get("resolution_state"),
            resolution_evidence=data.get("resolution_evidence"),
            resolution_actor=data.get("resolution_actor"),
            resolution_timestamp=data.get("resolution_timestamp"),
            created_at=data.get("created_at"),
            created_by=data.get("created_by", Principal.HUMAN.value),
            updated_at=data.get("updated_at"),
            history=data.get("history", []),
        )

    def to_dict(self) -> Dict[str, Any]:
        return {
            "id": self.id,
            "type": "conflict",
            "domain": self.domain,
            "title": self.title,
            "claim_a": self.claim_a,
            "source_a": self.source_a,
            "evidence_a": self.evidence_a,
            "claim_b": self.claim_b,
            "source_b": self.source_b,
            "evidence_b": self.evidence_b,
            "severity": self.severity,
            "status": self.status,
            "resolution_state": self.resolution_state,
            "resolution_evidence": self.resolution_evidence,
            "resolution_actor": self.resolution_actor,
            "resolution_timestamp": self.resolution_timestamp,
            "created_at": self.created_at,
            "created_by": self.created_by,
            "updated_at": self.updated_at,
            "history": self.history,
        }


class ConflictRegistry:
    """Formal, tamper-evident registry of Book-to-Memory conflicts."""

    def __init__(self):
        self._records: Dict[str, ConflictRecord] = {}

    def register_conflict(self, payload: Dict[str, Any], actor: Principal) -> ConflictRecord:
        """Register a new conflict between two claims."""
        if not isinstance(actor, Principal):
            raise ConflictPermissionError(f"Invalid actor: {actor}")

        validate_conflict_record(payload)
        domain = payload["domain"]
        cid = generate_conflict_id(domain, payload["claim_a"], payload["claim_b"])

        if cid in self._records:
            raise DuplicateConflictError(f"Conflict already registered: {cid}")

        payload["id"] = cid
        payload["created_by"] = actor.value
        record = ConflictRecord.from_dict(payload)
        self._records[cid] = record
        return record

    def get_conflict(self, conflict_id: str) -> Optional[ConflictRecord]:
        return self._records.get(conflict_id)

    def update_severity(
        self, conflict_id: str, new_severity: ConflictSeverity, actor: Principal, reason: str
    ) -> ConflictRecord:
        """Update conflict severity with mandatory justification and actor verification."""
        if actor == Principal.AI_AGENT:
            raise ConflictPermissionError("Principal 'ai_agent' is not permitted to modify conflict severity")

        if not isinstance(new_severity, ConflictSeverity):
            raise ConflictValidationError(f"Invalid ConflictSeverity: {new_severity}")

        record = self._records.get(conflict_id)
        if not record:
            raise ConflictValidationError(f"Conflict not found: {conflict_id}")

        old_sev = record.severity
        record.severity = new_severity.value
        now_ts = datetime.now(timezone.utc).isoformat()
        record.updated_at = now_ts
        record.history.append({
            "timestamp": now_ts,
            "action": "severity_change",
            "actor": actor.value,
            "old_severity": old_sev,
            "new_severity": new_severity.value,
            "reason": reason,
        })
        return record

    def resolve_conflict(
        self,
        conflict_id: str,
        resolution_type: str,
        actor: Principal,
        evidence_reference: str,
        justification: str,
    ) -> ConflictRecord:
        """Resolve a conflict with a specific canonical resolution type and evidence."""
        if actor == Principal.AI_AGENT:
            raise ConflictPermissionError("Principal 'ai_agent' is not permitted to resolve conflicts")

        if resolution_type not in [t.value for t in ConflictResolutionType]:
            raise ConflictValidationError(f"Unknown resolution type: {resolution_type}")

        record = self._records.get(conflict_id)
        if not record:
            raise ConflictValidationError(f"Conflict not found: {conflict_id}")

        now_ts = datetime.now(timezone.utc).isoformat()
        record.status = ConflictStatus.RESOLVED.value
        record.resolution_state = resolution_type
        record.resolution_evidence = evidence_reference
        record.resolution_actor = actor.value
        record.resolution_timestamp = now_ts
        record.updated_at = now_ts
        record.history.append({
            "timestamp": now_ts,
            "action": "resolve",
            "actor": actor.value,
            "resolution_state": resolution_type,
            "evidence_reference": evidence_reference,
            "justification": justification,
        })
        return record

    def reopen_conflict(
        self,
        conflict_id: str,
        new_evidence: str,
        actor: Principal,
        new_severity: ConflictSeverity = ConflictSeverity.HIGH,
    ) -> ConflictRecord:
        """Reopen a resolved conflict upon discovery of new contradictory evidence."""
        if actor == Principal.AI_AGENT:
            raise ConflictPermissionError("Principal 'ai_agent' is not permitted to reopen conflicts")

        record = self._records.get(conflict_id)
        if not record:
            raise ConflictValidationError(f"Conflict not found: {conflict_id}")

        now_ts = datetime.now(timezone.utc).isoformat()
        old_status = record.status
        record.status = ConflictStatus.OPEN.value
        record.severity = new_severity.value
        record.updated_at = now_ts
        record.history.append({
            "timestamp": now_ts,
            "action": "reopen",
            "actor": actor.value,
            "previous_status": old_status,
            "new_status": ConflictStatus.OPEN.value,
            "new_severity": new_severity.value,
            "new_evidence": new_evidence,
        })
        return record

    def delete_conflict(self, conflict_id: str, actor: Principal) -> None:
        """Privileged soft/hard removal of an erroneous conflict entry."""
        if actor == Principal.AI_AGENT:
            raise ConflictPermissionError("Principal 'ai_agent' cannot delete or unregister conflicts")

        if conflict_id in self._records:
            del self._records[conflict_id]
