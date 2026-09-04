"""
Memory Governance, Trust Boundaries & Invariants (P0-P18).
"""

from enum import Enum
from typing import List, Dict, Any, Optional, Literal
import uuid
from pydantic import BaseModel, Field, field_validator


class Principal(str, Enum):
    """Execution identity hierarchy."""
    HUMAN = "human"
    AI_AGENT = "ai_agent"
    ADMIN = "admin"


class Operation(str, Enum):
    """Governed operations on memory notes."""
    PROPOSE = "propose"
    UPDATE = "update"
    ATTEST = "attest"
    PROMOTE = "promote"
    ARCHIVE = "archive"
    SUPERSEDE = "supersede"
    READ = "read"
    SEARCH = "search"
    DELETE = "delete"


class Lifecycle(str, Enum):
    """Canonical memory lifecycle states."""
    RAW = "RAW"
    CLASSIFIED = "CLASSIFIED"
    NORMALIZED = "NORMALIZED"
    REVIEW = "REVIEW"
    VERIFIED = "VERIFIED"
    ACTIVE = "ACTIVE"
    RECONSOLIDATING = "RECONSOLIDATING"
    SUPERSEDED = "SUPERSEDED"
    ARCHIVED = "ARCHIVED"


class NoteType(str, Enum):
    """Permitted canonical note categories."""
    KNOWLEDGE = "knowledge"
    PROJECT = "project"
    PROCEDURE = "procedure"
    DECISION = "decision"
    EXPERIENCE = "experience"
    ERROR = "error"
    LESSON = "lesson"
    PREFERENCE = "preference"
    RESOURCE = "resource"
    HYPOTHESIS = "hypothesis"
    SYSTEM = "system"
    CORE = "core"


class ProvenanceModel(BaseModel):
    """Provenance tracking origin of cognitive memory."""
    source_type: Literal[
        "user", "official", "execution", "experience", "ai", "inference", "import", "unknown"
    ]
    source_ref: str
    source_date: Optional[str] = None
    original_path: Optional[str] = None
    extraction_date: Optional[str] = None
    redaction: Optional[Literal["none", "applied", "not_applicable"]] = "none"
    provenance_status: Optional[Literal["complete", "incomplete"]] = "complete"


class RelationModel(BaseModel):
    """Semantic relation link between notes."""
    relation: str  # related_to, supports, contradicts, derived_from, replaces, supersedes
    target: str    # wikilink or type
    target_id: Optional[str] = None


class NoteFrontmatter(BaseModel):
    """Strict Obsidian YAML Frontmatter schema conforming to canonical specification."""
    id: str = Field(default_factory=lambda: str(uuid.uuid4()))
    type: NoteType = NoteType.KNOWLEDGE
    lifecycle: Lifecycle = Lifecycle.REVIEW
    category: str = "general"
    tags: List[str] = Field(default_factory=list)
    created: str
    updated: str
    provenance: ProvenanceModel
    confidence: Literal["very_high", "high", "medium", "low", "unknown"] = "medium"
    verification: Literal["verified", "partially_verified", "unverified", "inferred"] = "unverified"
    valid_from: Optional[str] = None
    valid_until: Optional[str] = None
    version_range: Optional[str] = None
    applies_to: Optional[str] = None
    supersedes: Optional[str] = None
    superseded_by: Optional[str] = None
    conflicts_with: Optional[str] = None
    relations: List[RelationModel] = Field(default_factory=list)

    @field_validator("id")
    @classmethod
    def validate_uuid(cls, v: str) -> str:
        try:
            uuid.UUID(str(v))
        except ValueError as err:
            raise ValueError(f"Note id must be a valid UUID string, got '{v}'") from err
        return str(v)


class MemoryNote(BaseModel):
    """In-memory representation of a canonical vault note."""
    frontmatter: NoteFrontmatter
    content: str = ""

    @property
    def id(self) -> str:
        return self.frontmatter.id

    @property
    def lifecycle(self) -> Lifecycle:
        return self.frontmatter.lifecycle

    @property
    def verification(self) -> str:
        return self.frontmatter.verification

    def to_dict(self) -> Dict[str, Any]:
        data = self.frontmatter.model_dump()
        data["content"] = self.content
        return data


# ============================================================================
# Invariant Validation Functions (P0 - P18)
# ============================================================================

def validate_hardware_telemetry_invariants(principal: Principal, field_name: str) -> None:
    """Enforces P16-P18 Hardware Telemetry & Forensics Immutability."""
    immutable_hardware_fields = {
        "hardware_serial", "vendor_id", "product_id", "physical_capacity",
        "system_host_id", "telemetry_timestamp", "evidence_sha256"
    }
    if field_name in immutable_hardware_fields and principal != Principal.ADMIN:
        raise PermissionError(f"Hardware telemetry field '{field_name}' is strictly read-only (P16-P18).")


def validate_propose_invariants(principal: Principal, note_data: Dict[str, Any]) -> None:
    """Enforces proposal invariants (P0-001, P0-002, P0-004, P0-005, P16-P18)."""
    # Check hardware telemetry immutability (P16-P18)
    for key in note_data:
        validate_hardware_telemetry_invariants(principal, key)

    verification = note_data.get("verification", "unverified")
    lifecycle = note_data.get("lifecycle", Lifecycle.REVIEW.value)
    if isinstance(lifecycle, Lifecycle):
        lifecycle = lifecycle.value

    provenance = note_data.get("provenance", {})
    if isinstance(provenance, ProvenanceModel):
        source_type = provenance.source_type
    elif isinstance(provenance, dict):
        source_type = provenance.get("source_type", "unknown")
    else:
        source_type = "unknown"

    # P0-001 / P0-005: AI Self-Verification Gate
    if verification == "verified":
        raise ValueError("Verification status 'verified' cannot be set via propose. Use attest() instead.")

    if principal == Principal.AI_AGENT:
        # P0-002: Privileged Provenance Types
        forbidden_sources = {"user", "official", "experience", "import"}
        if source_type in forbidden_sources:
            raise ValueError(
                f"Principal 'ai_agent' is not permitted to claim provenance source_type '{source_type}'."
            )

        # P0-004: Creation Lifecycles
        allowed_lifecycles = {Lifecycle.RAW.value, Lifecycle.CLASSIFIED.value, Lifecycle.NORMALIZED.value, Lifecycle.REVIEW.value}
        if lifecycle not in allowed_lifecycles:
            raise ValueError(
                f"Principal 'ai_agent' cannot set lifecycle to '{lifecycle}' at creation. Allowed: {allowed_lifecycles}"
            )


def validate_update_invariants(principal: Principal, current_note: Dict[str, Any], updates: Dict[str, Any]) -> None:
    """Enforces update invariants (P0-003, P0-006, P0-007, P0-011, P16-P18)."""
    # P16-P18: Check hardware telemetry immutability
    for key in updates:
        validate_hardware_telemetry_invariants(principal, key)

    # P0-011: Verification status escalation check
    if "verification" in updates:
        new_ver = updates["verification"]
        if new_ver == "verified" and current_note.get("verification") != "verified":
            raise ValueError("Verification status 'verified' cannot be escalated via update. Use attest() instead.")

    # P0-003: Provenance immutability post-creation
    if "provenance" in updates:
        new_prov = updates["provenance"]
        new_st = new_prov.source_type if isinstance(new_prov, ProvenanceModel) else (new_prov.get("source_type") if isinstance(new_prov, dict) else None)
        curr_prov = current_note.get("provenance", {})
        curr_st = curr_prov.get("source_type") if isinstance(curr_prov, dict) else getattr(curr_prov, "source_type", None)
        if new_st and curr_st and new_st != curr_st:
            raise ValueError("Field provenance.source_type is immutable post-creation.")

    # P0-007: Lifecycle immutability on normal update
    if "lifecycle" in updates:
        new_lc = updates["lifecycle"]
        if isinstance(new_lc, Lifecycle):
            new_lc = new_lc.value
        curr_lc = current_note.get("lifecycle")
        if isinstance(curr_lc, Lifecycle):
            curr_lc = curr_lc.value
        if new_lc and curr_lc and new_lc != curr_lc:
            raise ValueError("Field lifecycle is immutable via update. Use promote(), archive(), or supersede() instead.")


def validate_attest_invariants(principal: Principal, note_id: str) -> None:
    """Enforces attestation invariants (P0-005)."""
    if principal == Principal.AI_AGENT:
        raise PermissionError("ai_agent not allowed to perform attest. Only human and admin may attest memories.")


def validate_promote_invariants(principal: Principal, current_note: Dict[str, Any]) -> None:
    """Enforces promotion invariants (P0-004, P0-008)."""
    if principal == Principal.AI_AGENT:
        raise PermissionError("ai_agent not allowed to promote notes to ACTIVE directly.")


def validate_supersession_invariants(
    old_note: Dict[str, Any],
    new_note: Dict[str, Any],
    ancestor_ids: Optional[set] = None,
) -> None:
    """Enforces supersession invariants (P0-012, P0-013) including transitive cycle detection."""
    old_id = old_note.get("id")
    new_id = new_note.get("id")
    if old_id == new_id:
        raise ValueError(f"Self-supersession prohibited: note cannot supersede itself ({old_id}).")
    if old_note.get("supersedes") == new_id:
        raise ValueError(f"Cyclic supersession detected between {old_id} and {new_id}.")
    if ancestor_ids and new_id in ancestor_ids:
        raise ValueError(f"Cyclic supersession detected: note '{new_id}' is already an ancestor of '{old_id}' (P0-012/P0-013).")
