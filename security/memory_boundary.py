"""Mandatory write boundary for persistent agent memory."""
from __future__ import annotations

from dataclasses import dataclass
from typing import Any

from .memory_integrity import MemoryLedger
from .trust_gate import TrustState, normalize_trust_state


@dataclass(frozen=True)
class MemoryWriteDecision:
    allowed: bool
    reason: str
    record_version: int | None = None


class MemoryWriteBoundary:
    def __init__(self, ledger: MemoryLedger | None = None) -> None:
        self.ledger = ledger or MemoryLedger()

    def prepare(
        self,
        namespace: str,
        payload: dict[str, Any],
        trust_state: TrustState | str,
        *,
        human_approved: bool = False,
    ):
        norm = normalize_trust_state(trust_state)
        if norm is None:
            return None, MemoryWriteDecision(False, "memory_trust_unknown_denied")
        if norm in (TrustState.BLOCKED, TrustState.UNTRUSTED):
            return None, MemoryWriteDecision(False, "memory_trust_denied")
        if norm is TrustState.REVIEW and not human_approved:
            return None, MemoryWriteDecision(False, "memory_approval_required")
        if norm not in (TrustState.TRUSTED, TrustState.REVIEW):
            return None, MemoryWriteDecision(False, "memory_trust_denied")
        if not isinstance(payload, dict):
            raise TypeError("payload must be a mapping")
        # B01 Synthetic evidence cannot be promoted to verified or active knowledge
        is_synthetic = (
            payload.get("synthetic") is True
            or payload.get("source_type") == "synthetic"
            or payload.get("provenance") == "synthetic"
            or str(payload.get("corpus", "")).lower() == "synthetic"
        )
        claims_empirical = (
            payload.get("verification") == "verified"
            or payload.get("lifecycle") == "ACTIVE"
            or payload.get("status") == "ACTIVE"
            or payload.get("empirically_confirmed") is True
            or payload.get("validated") is True
        )
        if is_synthetic and claims_empirical:
            return None, MemoryWriteDecision(False, "synthetic_evidence_promotion_blocked")
        return self.ledger.prepare(namespace, payload), None

    def commit(
        self,
        record,
        trust_state: TrustState | str,
        *,
        human_approved: bool = False,
    ) -> MemoryWriteDecision:
        norm = normalize_trust_state(trust_state)
        if norm is None:
            return MemoryWriteDecision(False, "memory_trust_unknown_denied")
        if not self.ledger.commit_proposal(record, trust_state=norm.value, human_approved=human_approved):
            return MemoryWriteDecision(False, "memory_integrity_rejected")
        return MemoryWriteDecision(True, "memory_write_allowed", record.version)

    def propose(
        self,
        namespace: str,
        payload: dict[str, Any],
        trust_state: TrustState | str,
        *,
        human_approved: bool = False,
    ) -> MemoryWriteDecision:
        record, denied = self.prepare(
            namespace, payload, trust_state, human_approved=human_approved
        )
        if denied is not None:
            return denied
        return self.commit(record, trust_state, human_approved=human_approved)
