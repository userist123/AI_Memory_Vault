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
