"""Host integration seam for persistent memory writes."""
from __future__ import annotations

from dataclasses import dataclass
from threading import RLock
from typing import Any, Callable
from uuid import uuid4

from .audit_trail import AuditTrail
from .memory_boundary import MemoryWriteBoundary, MemoryWriteDecision
from .trust_gate import TrustState
from .security_update_policy import SecurityUpdatePolicy, SecurityUpdateRequired


class MemoryIntegrityError(RuntimeError):
    """Raised when memory persistence state enters an unrecoverable integrity failure."""
    pass


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
        rollback: Callable[[str, dict[str, Any]], Any] | None = None,
        audit_trail: AuditTrail | None = None,
        update_policy: SecurityUpdatePolicy | None = None,
    ) -> None:
        if not callable(persist):
            raise TypeError("persist must be callable")
        if rollback is not None and not callable(rollback):
            raise TypeError("rollback must be callable")
        self._persist = persist
        self._rollback = rollback
        self.boundary = boundary or MemoryWriteBoundary()
        self._audit_trail = audit_trail
        self._update_policy = update_policy
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
        state = trust_state.value if isinstance(trust_state, TrustState) else str(trust_state)
        with self._lock:
            if self._update_policy is not None:
                try:
                    self._update_policy.enforce(protected_operation=True)
                except SecurityUpdateRequired:
                    denied = MemoryWriteDecision(False, "security_update_required")
                    self._audit(
                        "MEMORY_WRITE_DENIED",
                        correlation,
                        state,
                        denied.reason,
                        payload=payload,
                        namespace=namespace,
                    )
                    return denied

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

            staged = False
            try:
                self._persist(namespace, dict(payload))
                staged = True
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
            if not decision.allowed:
                if staged:
                    if self._rollback is not None:
                        try:
                            self._rollback(namespace, dict(payload))
                            self._audit(
                                "MEMORY_ROLLBACK",
                                correlation,
                                state,
                                "ROLLED_BACK",
                                payload=payload,
                                namespace=namespace,
                                decision_reason=decision.reason,
                            )
                        except Exception as rb_exc:
                            self._audit(
                                "MEMORY_ROLLBACK_ERROR",
                                correlation,
                                state,
                                "INTEGRITY_FAILURE",
                                payload=payload,
                                namespace=namespace,
                                error=rb_exc,
                            )
                            raise MemoryIntegrityError(
                                f"INTEGRITY_FAILURE: persist succeeded but commit was denied and rollback failed: {rb_exc}"
                            ) from rb_exc
                    else:
                        self._audit(
                            "MEMORY_ROLLBACK_ERROR",
                            correlation,
                            state,
                            "INTEGRITY_FAILURE",
                            payload=payload,
                            namespace=namespace,
                            error="missing_rollback_handler",
                        )
                        raise MemoryIntegrityError(
                            "INTEGRITY_FAILURE: persist succeeded but commit was denied and no rollback handler was configured"
                        )
                self._audit(
                    "MEMORY_COMMIT_DENIED",
                    correlation,
                    state,
                    "DENIED",
                    payload=payload,
                    namespace=namespace,
                    decision_reason=decision.reason,
                )
                return decision

            self._audit(
                "MEMORY_COMMIT",
                correlation,
                state,
                "COMMITTED",
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
