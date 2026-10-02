"""Host integration seam for persistent memory writes.

All writes are proposed through MemoryWriteBoundary before the host persistence
callback is invoked. Reads are intentionally outside this write policy and
should be treated as data by the caller.
"""
from __future__ import annotations

from dataclasses import dataclass
from typing import Any, Callable

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
    ) -> None:
        if not callable(persist):
            raise TypeError("persist must be callable")
        self._persist = persist
        self.boundary = boundary or MemoryWriteBoundary()

    def write(
        self,
        namespace: str,
        payload: dict[str, Any],
        trust_state: TrustState | str,
        *,
        human_approved: bool = False,
    ) -> MemoryWriteDecision:
        record, denied = self.boundary.prepare(
            namespace,
            payload,
            trust_state,
            human_approved=human_approved,
        )
        if denied is not None:
            return denied

        self._persist(namespace, dict(payload))
        return self.boundary.commit(
            record,
            trust_state,
            human_approved=human_approved,
        )
