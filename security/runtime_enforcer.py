"""Runtime execution gate with parameter-bound, expiring, single-use approvals."""
from __future__ import annotations

import hashlib
import json
from dataclasses import dataclass, field
from datetime import datetime, timezone
from typing import Any, Callable
from uuid import uuid4

from .tool_capabilities import CapabilitySet
from .trust_gate import TrustDecision, TrustState


def _utc(value: datetime) -> datetime:
    if value.tzinfo is None:
        return value.replace(tzinfo=timezone.utc)
    return value.astimezone(timezone.utc)


@dataclass(frozen=True)
class ExecutionRequest:
    actor: str
    tool_name: str
    target: str
    parameters: dict[str, Any]
    side_effect: bool = False
    data_export: bool = False
    correlation_id: str = field(default_factory=lambda: uuid4().hex)

    def parameters_sha256(self) -> str:
        payload = json.dumps(
            self.parameters,
            sort_keys=True,
            separators=(",", ":"),
            ensure_ascii=True,
        ).encode("utf-8")
        return hashlib.sha256(payload).hexdigest()


@dataclass(frozen=True)
class ApprovalToken:
    approval_id: str
    actor: str
    tool_name: str
    target: str
    parameters_sha256: str
    issued_at: datetime
    expires_at: datetime
    nonce: str


@dataclass(frozen=True)
class RuntimeAuthorization:
    allowed: bool
    reason: str


@dataclass(frozen=True)
class ExecutionResult:
    authorization: RuntimeAuthorization
    value: Any = None


class RuntimeEnforcer:
    def __init__(self, capabilities: CapabilitySet | None = None) -> None:
        self._capabilities = capabilities
        self._used_approvals: set[str] = set()

    def authorize(
        self,
        request: ExecutionRequest,
        decision: TrustDecision,
        *,
        approval: ApprovalToken | None = None,
        now: datetime | None = None,
    ) -> RuntimeAuthorization:
        current = _utc(now or datetime.now(timezone.utc))

        if decision.state is TrustState.BLOCKED:
            return RuntimeAuthorization(False, "blocked_content")
        if decision.state is TrustState.UNTRUSTED:
            return RuntimeAuthorization(False, "untrusted_content")

        if self._capabilities is not None and not self._capabilities.allows(
            request.actor, request.tool_name, request.target, current
        ):
            return RuntimeAuthorization(False, "capability_denied")

        approval_required = (
            decision.state is TrustState.REVIEW
            or decision.requires_human_approval
            or request.side_effect
            or request.data_export
        )

        if not approval_required:
            return RuntimeAuthorization(True, "allowed")

        if approval is None:
            return RuntimeAuthorization(False, "approval_required")

        if approval.approval_id in self._used_approvals:
            return RuntimeAuthorization(False, "approval_replayed")

        if current >= _utc(approval.expires_at):
            return RuntimeAuthorization(False, "approval_expired")
        if current < _utc(approval.issued_at):
            return RuntimeAuthorization(False, "approval_not_yet_valid")

        if (
            approval.actor != request.actor
            or approval.tool_name != request.tool_name
            or approval.target != request.target
            or approval.parameters_sha256 != request.parameters_sha256()
        ):
            return RuntimeAuthorization(False, "approval_parameter_mismatch")

        self._used_approvals.add(approval.approval_id)
        return RuntimeAuthorization(True, "allowed")

    def execute(
        self,
        request: ExecutionRequest,
        decision: TrustDecision,
        executor: Callable[[ExecutionRequest], Any],
        *,
        approval: ApprovalToken | None = None,
        now: datetime | None = None,
    ) -> ExecutionResult:
        authorization = self.authorize(
            request,
            decision,
            approval=approval,
            now=now,
        )
        if not authorization.allowed:
            return ExecutionResult(authorization)
        return ExecutionResult(authorization, executor(request))
