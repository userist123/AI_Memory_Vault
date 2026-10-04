"""Host integration seam that makes runtime security checks mandatory."""
from __future__ import annotations

from dataclasses import dataclass
from datetime import datetime
from typing import Any, Callable

from .audit_trail import AuditTrail
from .runtime_enforcer import (
    ApprovalToken,
    ExecutionRequest,
    ExecutionResult,
    RuntimeAuthorization,
    RuntimeEnforcer,
)
from .security_event import SecurityEvent
from .tool_integrity import ToolDefinition, ToolPin, pin_tool, verify_tool
from .tool_response_boundary import validate_tool_response
from .trust_gate import TrustDecision
from .supply_chain_policy import ComponentProvenance, SoftwareAISupplyChainPolicy


@dataclass(frozen=True)
class RegisteredTool:
    definition: ToolDefinition
    pin: ToolPin


class RuntimeAdapter:
    """Single host-facing execution boundary for registered tools."""

    def __init__(
        self,
        enforcer: RuntimeEnforcer | None = None,
        *,
        event_sink: Callable[[SecurityEvent], None] | None = None,
        audit_trail: AuditTrail | None = None,
        supply_chain_policy: SoftwareAISupplyChainPolicy | None = None,
        broker: ApprovalBroker | None = None,
        production_mode: bool = False,
    ) -> None:
        if production_mode and supply_chain_policy is None:
            raise PermissionError("production runtime requires mandatory supply_chain_policy")
        self._enforcer = enforcer or (RuntimeEnforcer(broker=broker) if broker else RuntimeEnforcer())
        self._event_sink = event_sink
        self._audit_trail = audit_trail
        self._supply_chain_policy = supply_chain_policy
        self._production_mode = production_mode
        self._registered: dict[str, tuple[RegisteredTool, Callable[[ExecutionRequest], Any]]] = {}

    @property
    def broker(self) -> ApprovalBroker:
        return self._enforcer.broker

    def issue_approval(self, **kwargs: Any) -> ApprovalToken:
        if self._production_mode:
            raise PermissionError("issue_approval is forbidden on RuntimeAdapter in production mode; approvals must originate from the external authority broker")
        return self._enforcer.issue_approval(**kwargs)

    def register(
        self,
        definition: ToolDefinition,
        executor: Callable[[ExecutionRequest], Any],
        *,
        provenance: ComponentProvenance | None = None,
    ) -> ToolPin:
        if definition.name in self._registered:
            raise ValueError(f"tool already registered: {definition.name}")
        if not callable(executor):
            raise TypeError("executor must be callable")

        if self._supply_chain_policy is not None or self._production_mode:
            if self._supply_chain_policy is None:
                raise PermissionError("supply chain policy is required in production mode")
            if provenance is None:
                raise PermissionError("tool provenance is required")
            if provenance.component_type.value != "tool":
                raise ValueError("tool registration requires component_type=tool")
            decision = self._supply_chain_policy.evaluate(provenance)
            if decision.disposition.value != "VERIFIED":
                raise PermissionError(decision.reason)

        pin = pin_tool(definition)
        self._registered[definition.name] = (RegisteredTool(definition, pin), executor)
        if self._audit_trail is not None:
            self._audit_trail.record(
                event_type="TOOL_REGISTERED",
                actor="host",
                correlation_id=f"register:{definition.name}",
                outcome="REGISTERED",
                tool_name=definition.name,
                target=definition.server_id,
            )
        return pin

    def execute(
        self,
        request: ExecutionRequest,
        definition: ToolDefinition,
        decision: TrustDecision,
        *,
        approval: ApprovalToken | None = None,
        now: datetime | None = None,
    ) -> ExecutionResult:
        self._audit(
            "TOOL_REQUEST",
            request,
            decision,
            "STARTED",
            parameters_sha256=request.parameters_sha256(),
            approval_id=approval.approval_id if approval else None,
        )

        registered = self._registered.get(request.tool_name)
        if registered is None:
            return self._deny(request, decision, "tool_not_registered")

        registered_tool, executor = registered
        if (
            definition.name != request.tool_name
            or not verify_tool(definition, registered_tool.pin)
        ):
            return self._deny(request, decision, "tool_definition_mismatch")

        try:
            result = self._enforcer.execute(
                request,
                decision,
                executor,
                approval=approval,
                now=now,
            )
        except Exception as exc:
            self._audit(
                "TOOL_EXECUTION_ERROR",
                request,
                decision,
                "ERROR",
                error=exc,
            )
            raise

        self._audit(
            "TOOL_AUTHORIZATION",
            request,
            decision,
            "ALLOWED" if result.authorization.allowed else "DENIED",
            decision_reason=result.authorization.reason,
            approval_id=approval.approval_id if approval else None,
        )
        if not result.authorization.allowed:
            self._emit(request, decision, result.authorization)
            return result

        try:
            response = validate_tool_response(request.tool_name, result.value)
        except Exception as exc:
            self._audit(
                "TOOL_RESPONSE_ERROR",
                request,
                decision,
                "ERROR",
                error=exc,
            )
            raise

        if not response.allowed:
            authorization = RuntimeAuthorization(False, response.reason)
            self._audit(
                "TOOL_RESPONSE",
                request,
                decision,
                "DENIED",
                decision_reason=response.reason,
                scanner_verdict=response.verdict,
            )
            self._emit(request, decision, authorization)
            return ExecutionResult(authorization)

        self._audit(
            "TOOL_RESPONSE",
            request,
            decision,
            "ALLOWED",
            scanner_verdict=response.verdict,
            payload=response.data,
        )
        self._emit(request, decision, result.authorization)
        return ExecutionResult(result.authorization, response.data)

    def _deny(
        self,
        request: ExecutionRequest,
        decision: TrustDecision,
        reason: str,
    ) -> ExecutionResult:
        authorization = RuntimeAuthorization(False, reason)
        self._audit(
            "TOOL_AUTHORIZATION",
            request,
            decision,
            "DENIED",
            decision_reason=reason,
        )
        self._emit(request, decision, authorization)
        return ExecutionResult(authorization)

    def _audit(
        self,
        event_type: str,
        request: ExecutionRequest,
        decision: TrustDecision,
        outcome: str,
        *,
        parameters_sha256: str | None = None,
        approval_id: str | None = None,
        decision_reason: str | None = None,
        scanner_verdict: str | None = None,
        payload: Any = None,
        error: BaseException | None = None,
    ) -> None:
        if self._audit_trail is None:
            return
        self._audit_trail.record(
            event_type=event_type,
            actor=request.actor,
            correlation_id=request.correlation_id,
            outcome=outcome,
            trust_state=decision.state.value,
            tool_name=request.tool_name,
            target=request.target,
            parameters_sha256=parameters_sha256,
            payload=payload,
            approval_id=approval_id,
            decision_reason=decision_reason,
            scanner_verdict=scanner_verdict,
            error=error,
        )

    def _emit(
        self,
        request: ExecutionRequest,
        decision: TrustDecision,
        authorization: RuntimeAuthorization,
    ) -> None:
        if self._event_sink is None:
            return
        self._event_sink(
            SecurityEvent(
                event_type="tool_execution",
                source="runtime_adapter",
                actor=request.actor,
                correlation_id=request.correlation_id,
                trust_state=decision.state.value,
                tool_name=request.tool_name,
                tool_allowed=authorization.allowed,
                decision_reason=authorization.reason,
            )
        )
