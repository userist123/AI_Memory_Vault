"""Mandatory write boundary for persistent agent memory."""
from __future__ import annotations

from dataclasses import dataclass
from typing import Any

from .memory_integrity import MemoryLedger
from .trust_gate import TrustState


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
        state = trust_state.value if isinstance(trust_state, TrustState) else trust_state
        if state in ("BLOCKED", "UNTRUSTED"):
            return None, MemoryWriteDecision(False, "memory_trust_denied")
        if state == "REVIEW" and not human_approved:
            return None, MemoryWriteDecision(False, "memory_approval_required")
        if not isinstance(payload, dict):
            raise TypeError("payload must be a mapping")
        return self.ledger.prepare(namespace, payload), None

    def commit(
        self,
        record,
        trust_state: TrustState | str,
        *,
        human_approved: bool = False,
    ) -> MemoryWriteDecision:
        state = trust_state.value if isinstance(trust_state, TrustState) else trust_state
        if not self.ledger.commit_proposal(record, trust_state=state, human_approved=human_approved):
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
