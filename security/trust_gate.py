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


def validate_severity_transition(from_severity: str, to_severity: str) -> bool:
    """Validate that high-severity findings (HARD_BLOCKER) cannot be silently downgraded to WARNING."""
    blocker_levels = {"HARD_BLOCKER", "BLOCKER", "CRITICAL", "P0"}
    downgrade_levels = {"WARNING", "INFO", "LOW", "P2", "P3"}
    from_clean = str(from_severity).strip().upper()
    to_clean = str(to_severity).strip().upper()
    if from_clean in blocker_levels and to_clean in downgrade_levels:
        return False
    return True
