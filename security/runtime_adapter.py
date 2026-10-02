"""Host integration seam that makes runtime security checks mandatory.

The adapter owns registered tool executors. Callers cannot supply an executor at
execution time, so a host integration cannot accidentally bypass the registry,
tool pin, runtime authorization, or tool-output trust boundary.
"""
from __future__ import annotations

from dataclasses import dataclass
from datetime import datetime
from typing import Any, Callable

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
    ) -> None:
        self._enforcer = enforcer or RuntimeEnforcer()
        self._event_sink = event_sink
        self._registered: dict[str, tuple[RegisteredTool, Callable[[ExecutionRequest], Any]]] = {}

    def register(
        self,
        definition: ToolDefinition,
        executor: Callable[[ExecutionRequest], Any],
    ) -> ToolPin:
        if definition.name in self._registered:
            raise ValueError(f"tool already registered: {definition.name}")
        if not callable(executor):
            raise TypeError("executor must be callable")

        pin = pin_tool(definition)
        self._registered[definition.name] = (RegisteredTool(definition, pin), executor)
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
        registered = self._registered.get(request.tool_name)
        if registered is None:
            return self._deny(request, decision, "tool_not_registered")

        registered_tool, executor = registered
        if (
            definition.name != request.tool_name
            or not verify_tool(definition, registered_tool.pin)
        ):
            return self._deny(request, decision, "tool_definition_mismatch")

        result = self._enforcer.execute(
            request,
            decision,
            executor,
            approval=approval,
            now=now,
        )
        if not result.authorization.allowed:
            self._emit(request, decision, result.authorization)
            return result

        response = validate_tool_response(request.tool_name, result.value)
        if not response.allowed:
            authorization = RuntimeAuthorization(False, response.reason)
            self._emit(request, decision, authorization)
            return ExecutionResult(authorization)

        self._emit(request, decision, result.authorization)
        return ExecutionResult(result.authorization, response.data)

    def _deny(
        self,
        request: ExecutionRequest,
        decision: TrustDecision,
        reason: str,
    ) -> ExecutionResult:
        authorization = RuntimeAuthorization(False, reason)
        self._emit(request, decision, authorization)
        return ExecutionResult(authorization)

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
                correlation_id=request.tool_name,
                trust_state=decision.state.value,
                tool_name=request.tool_name,
                tool_allowed=authorization.allowed,
                decision_reason=authorization.reason,
            )
        )
