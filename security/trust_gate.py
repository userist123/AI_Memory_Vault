"""Trust gate for externally sourced agent instructions.

External content is DATA by default, never authority. A scanned artifact must
pass provenance and policy checks before its instructions can become executable
guidance.

Deterministic and side-effect free. No content is executed.
"""
from __future__ import annotations

from dataclasses import dataclass, field
from enum import Enum


class TrustState(str, Enum):
    UNTRUSTED = "UNTRUSTED"
    REVIEW = "REVIEW"
    TRUSTED = "TRUSTED"
    BLOCKED = "BLOCKED"


def normalize_trust_state(state: Any) -> TrustState | None:
    """Normalize input to a canonical TrustState enum or return None if unknown/invalid."""
    if isinstance(state, TrustState):
        return state
    if isinstance(state, str):
        cleaned = state.strip()
        if not cleaned:
            return None
        try:
            return TrustState(cleaned)
        except ValueError:
            try:
                return TrustState[cleaned.upper()]
            except KeyError:
                return None
    return None


@dataclass(frozen=True)
class TrustDecision:
    state: TrustState
    reasons: tuple[str, ...] = field(default_factory=tuple)
    requires_human_approval: bool = False


@dataclass(frozen=True)
class ArtifactAssessment:
    source: str | None
    provenance_verified: bool
    scanner_verdict: str
    has_active_override: bool = False
    has_active_data_access: bool = False
    has_active_network: bool = False
    requested_side_effect: bool = False


def assess_artifact(a: ArtifactAssessment) -> TrustDecision:
    reasons: list[str] = []
    if a.has_active_override:
        reasons.append("contains an active instruction-override indicator")
    if a.has_active_data_access and a.has_active_network:
        reasons.append("contains an active data-access-to-network chain")
        return TrustDecision(TrustState.BLOCKED, tuple(reasons))
    if a.scanner_verdict == "BLOCK":
        reasons.append("static scanner verdict is BLOCK")
        return TrustDecision(TrustState.BLOCKED, tuple(reasons))
    if not a.provenance_verified:
        reasons.append("source provenance is not verified")
        return TrustDecision(TrustState.UNTRUSTED, tuple(reasons), a.requested_side_effect)
    if a.scanner_verdict == "REVIEW" or a.has_active_override:
        reasons.append("manual security review is required")
        return TrustDecision(TrustState.REVIEW, tuple(reasons), True)
    if a.requested_side_effect:
        reasons.append("external content requests a side effect")
        return TrustDecision(TrustState.REVIEW, tuple(reasons), True)
    return TrustDecision(TrustState.TRUSTED, tuple(reasons))


def can_execute_instruction(decision: TrustDecision) -> bool:
    return decision.state is TrustState.TRUSTED


def external_content_is_data() -> bool:
    return True


# Severity labels used across the vault (blocker registry, audit findings, priorities), on one
# ordinal scale. A label that is not here has no rank, and a transition involving it is refused:
# an unknown label must not be a way around the ordering.
SEVERITY_RANK: dict[str, int] = {
    "INFO": 0,
    "LOW": 1,
    "P3": 1,
    "WARNING": 2,
    "MEDIUM": 2,
    "P2": 2,
    "SOFT_BLOCKER": 3,
    "HIGH": 3,
    "P1": 3,
    "HARD_BLOCKER": 4,
    "BLOCKER": 4,
    "CRITICAL": 4,
    "P0": 4,
}

# The top tier: lowering any of these is the downgrade this gate exists to stop.
BLOCKER_SEVERITIES = frozenset({"HARD_BLOCKER", "BLOCKER", "CRITICAL", "P0"})


class SeverityAttestationError(PermissionError):
    """A severity downgrade was attempted without a valid owner attestation."""


def severity_rank(label: Any) -> int:
    """Ordinal rank of a severity label; ValueError for a label the scale does not know."""
    key = str(label).strip().upper()
    if key not in SEVERITY_RANK:
        raise ValueError(f"unknown severity label: {label!r}")
    return SEVERITY_RANK[key]


def is_severity_decrease(from_severity: Any, to_severity: Any) -> bool:
    """True when `to_severity` ranks strictly below `from_severity` (ValueError on unknown labels)."""
    return severity_rank(to_severity) < severity_rank(from_severity)


@dataclass(frozen=True)
class SeverityDowngradeAttestation:
    """An owner's explicit decision to lower one finding's severity.

    `principal` must be a typed owner ``Principal`` (HUMAN or ADMIN: the vault's own ATTEST matrix,
    checked by ``require_owner_principal`` as the proposal queue does); a string, a missing
    principal and ``AI_AGENT`` are all refused. `evidence_ref` points at what justifies the
    decision (a commit, a PR, a document) and cannot be empty. The attestation names the finding
    and the exact transition, so one decision cannot be reused for another finding or another
    step.
    """

    principal: Any
    evidence_ref: str
    attested_by: str
    blocker_id: str
    from_severity: str
    to_severity: str


def verify_severity_attestation(
    attestation: Any, blocker_id: str, from_severity: str, to_severity: str
) -> str:
    """Return the attesting principal's value if `attestation` authorises exactly this downgrade.

    Raises SeverityAttestationError (a PermissionError) otherwise; never returns falsy.
    """
    if not isinstance(attestation, SeverityDowngradeAttestation):
        raise SeverityAttestationError(
            "a severity downgrade needs an explicit owner attestation record"
        )
    # Imported here: `security` is a leaf package and the vault's attestation rule lives with the
    # proposal queue; loading it only when a downgrade is actually attested keeps the import graph
    # of the rest of this module unchanged.
    from cognitive_core.proposal_queue import require_owner_principal

    try:
        principal = require_owner_principal(attestation.principal)
    except PermissionError as exc:
        raise SeverityAttestationError(str(exc)) from exc
    if not isinstance(attestation.evidence_ref, str) or not attestation.evidence_ref.strip():
        raise SeverityAttestationError("a severity downgrade attestation needs an evidence reference")
    if not isinstance(attestation.attested_by, str) or not attestation.attested_by.strip():
        raise SeverityAttestationError("a severity downgrade attestation needs the attester's name")
    if str(attestation.blocker_id).strip() != str(blocker_id).strip():
        raise SeverityAttestationError(
            f"attestation is for {attestation.blocker_id!r}, not {blocker_id!r}"
        )
    if (
        str(attestation.from_severity).strip().upper() != str(from_severity).strip().upper()
        or str(attestation.to_severity).strip().upper() != str(to_severity).strip().upper()
    ):
        raise SeverityAttestationError(
            f"attestation covers {attestation.from_severity}->{attestation.to_severity}, "
            f"not {from_severity}->{to_severity}"
        )
    return principal


def validate_severity_transition(
    from_severity: str,
    to_severity: str,
    attestation: "SeverityDowngradeAttestation | None" = None,
    blocker_id: str | None = None,
) -> bool:
    """Whether a finding's severity may change from `from_severity` to `to_severity`.

    Raising or keeping a severity is always allowed. Lowering a blocker-tier severity
    (HARD_BLOCKER, BLOCKER, CRITICAL, P0) to anything below it is allowed only with a valid
    ``SeverityDowngradeAttestation`` for that finding and that transition (typed owner principal
    plus evidence reference). A label outside ``SEVERITY_RANK`` is refused. Lowering a lower-tier
    severity (e.g. WARNING to INFO) is not gated here; the blocker registry validator
    (30_SCRIPTS/verification/validate_blocker_registry.py) gates every decrease for registered
    blockers.
    """
    try:
        decrease = is_severity_decrease(from_severity, to_severity)
    except ValueError:
        return False
    if not decrease:
        return True
    if str(from_severity).strip().upper() not in BLOCKER_SEVERITIES:
        return True
    if attestation is None:
        return False
    try:
        verify_severity_attestation(
            attestation,
            blocker_id if blocker_id is not None else getattr(attestation, "blocker_id", ""),
            from_severity,
            to_severity,
        )
    except SeverityAttestationError:
        return False
    return True
