"""Audited scanner-to-trust evaluation boundary."""
from __future__ import annotations

from dataclasses import dataclass, replace
from pathlib import Path

from .audit_trail import AuditTrail
from .skill_exfiltration_scanner import ScanResult, scan_text
from .trust_gate import ArtifactAssessment, TrustDecision, assess_artifact


@dataclass(frozen=True)
class AuditedEvaluation:
    scan: ScanResult
    decision: TrustDecision


def evaluate_artifact(
    path: Path,
    text: str,
    assessment: ArtifactAssessment,
    *,
    actor: str,
    correlation_id: str,
    audit_trail: AuditTrail,
) -> AuditedEvaluation:
    """Scan content, derive the effective trust decision, and audit both stages.

    The caller-supplied scanner verdict is deliberately not authoritative.
    The verdict used by assess_artifact() is the freshly computed scan result.
    """
    scan = scan_text(path, text, assessment.source and {"source": assessment.source})
    effective = replace(
        assessment,
        source=assessment.source or str(path),
        scanner_verdict=scan.verdict,
    )
    decision = assess_artifact(effective)

    audit_trail.record(
        event_type="CONTENT_SCAN",
        actor=actor,
        correlation_id=correlation_id,
        outcome=scan.verdict,
        target=str(path),
        payload={"sha256": scan.sha256},
        scanner_verdict=scan.verdict,
        metadata={
            "finding_count": str(len(scan.findings)),
            "score": str(scan.score),
        },
    )
    audit_trail.record(
        event_type="TRUST_DECISION",
        actor=actor,
        correlation_id=correlation_id,
        outcome=decision.state.value,
        target=str(path),
        trust_state=decision.state.value,
        scanner_verdict=scan.verdict,
        decision_reason="; ".join(decision.reasons),
    )
    return AuditedEvaluation(scan, decision)
