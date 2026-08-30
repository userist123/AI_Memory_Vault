"""Bridge provider usage into the existing CouncilTokenTelemetry schema.

The adapter receives both ModelRequest and ModelResponse so estimated input is
based on the actual request payload, while provider-reported usage is recorded
separately as actual usage. Existing telemetry methods remain untouched.
"""
from __future__ import annotations

from typing import Any

from .model_provider import ModelRequest, ModelResponse


def record_specialist_response(
    telemetry: Any,
    request: ModelRequest,
    response: ModelResponse,
) -> None:
    """Record one specialist request/response pair into Council telemetry."""
    telemetry.record_specialist(request.prompt, response.content)
    _record_actual_usage(telemetry, "specialist", response)


def record_synthesis_response(
    telemetry: Any,
    request: ModelRequest,
    response: ModelResponse,
) -> None:
    """Record one synthesis request/response pair into Council telemetry."""
    telemetry.record_synthesis(request.prompt, response.content)
    _record_actual_usage(telemetry, "synthesis", response)


def _record_actual_usage(telemetry: Any, kind: str, response: ModelResponse) -> None:
    usage = response.usage
    telemetry.record_actual_usage(
        kind=kind,
        provider=response.provider,
        model=response.model,
        model_tier=response.model_tier,
        actual_input=usage.actual_input,
        actual_output=usage.actual_output,
        cached_input=usage.cached_input,
        reasoning_tokens=usage.reasoning_tokens,
        total=usage.total,
    )
