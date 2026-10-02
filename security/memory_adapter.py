"""Host integration seam for persistent memory writes."""
from __future__ import annotations

from dataclasses import dataclass
from threading import RLock
from typing import Any, Callable
from uuid import uuid4

from .audit_trail import AuditTrail
from .memory_boundary import MemoryWriteBoundary, MemoryWriteDecision
from .trust_gate import TrustState


@dataclass(frozen=True)
class MemoryWriteResult:
    decision: MemoryWriteDecision
    persisted: bool


class MemoryAdapter:
    """Single host-facing write seam for protected agent memory."""

    def __init__(
        self,
        persist: Callable[[str, dict[str, Any]], Any],
        boundary: MemoryWriteBoundary | None = None,
        *,
        audit_trail: AuditTrail | None = None,
    ) -> None:
        if not callable(persist):
            raise TypeError("persist must be callable")
        self._persist = persist
        self.boundary = boundary or MemoryWriteBoundary()
        self._audit_trail = audit_trail
        self._lock = RLock()

    def write(
        self,
        namespace: str,
        payload: dict[str, Any],
        trust_state: TrustState | str,
        *,
        human_approved: bool = False,
        correlation_id: str | None = None,
    ) -> MemoryWriteDecision:
        correlation = correlation_id or uuid4().hex
        state = trust_state.value if isinstance(trust_state, TrustState) else trust_state
        with self._lock:
            record, denied = self.boundary.prepare(
                namespace,
                payload,
                trust_state,
                human_approved=human_approved,
            )
            if denied is not None:
                self._audit(
                    "MEMORY_WRITE_DENIED",
                    correlation,
                    state,
                    denied.reason,
                    payload=payload,
                    namespace=namespace,
                )
                return denied

            try:
                self._persist(namespace, dict(payload))
            except Exception as exc:
                self._audit(
                    "MEMORY_PERSIST_ERROR",
                    correlation,
                    state,
                    "backend_error",
                    payload=payload,
                    namespace=namespace,
                    error=exc,
                )
                raise

            self._audit(
                "MEMORY_PERSIST",
                correlation,
                state,
                "PERSISTED",
                payload=payload,
                namespace=namespace,
            )
            decision = self.boundary.commit(
                record,
                trust_state,
                human_approved=human_approved,
            )
            self._audit(
                "MEMORY_COMMIT" if decision.allowed else "MEMORY_COMMIT_DENIED",
                correlation,
                state,
                "COMMITTED" if decision.allowed else "DENIED",
                payload=payload,
                namespace=namespace,
                decision_reason=decision.reason,
            )
            return decision

    def _audit(
        self,
        event_type: str,
        correlation_id: str,
        trust_state: str,
        outcome: str,
        *,
        payload: Any = None,
        namespace: str | None = None,
        decision_reason: str | None = None,
        error: BaseException | None = None,
    ) -> None:
        if self._audit_trail is None:
            return
        self._audit_trail.record(
            event_type=event_type,
            actor="memory_adapter",
            correlation_id=correlation_id,
            outcome=outcome,
            trust_state=trust_state,
            target=namespace,
            payload=payload,
            decision_reason=decision_reason,
            error=error,
        )
