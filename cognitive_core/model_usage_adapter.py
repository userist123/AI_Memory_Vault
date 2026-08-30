"""Bridge provider usage into the existing CouncilTokenTelemetry schema.

This module is deliberately additive: it does not change the provider contract,
Council orchestration, memory flow, or existing telemetry call sites.  It maps
ModelResponse.usage into the canonical CouncilTokenTelemetry counters while
preserving the distinction between estimated and actual provider usage.
"""
from __future__ import annotations

from typing import Any

from .model_provider import ModelResponse


def record_specialist_response(telemetry: Any, response: ModelResponse) -> None:
    """Record one specialist ModelResponse into existing telemetry.

    The existing telemetry API accepts text for ``record_specialist`` and
    estimates token counts.  For a provider-backed response we additionally
    attach exact/actual usage when available through an additive hook, without
    changing the legacy method signature.
    """
    telemetry.record_specialist(response.content, response.content)
    _record_actual_usage(telemetry, "specialist", response)


def record_synthesis_response(telemetry: Any, response: ModelResponse) -> None:
    """Record one synthesis ModelResponse into existing telemetry."""
    telemetry.record_synthesis(response.content, response.content)
    _record_actual_usage(telemetry, "synthesis", response)


def _record_actual_usage(telemetry: Any, kind: str, response: ModelResponse) -> None:
    usage = response.usage
    actual_input = usage.actual_input
    actual_output = usage.actual_output
    cached_input = usage.cached_input
    reasoning_tokens = usage.reasoning_tokens

    # Store provider truth separately from legacy estimated counters.
    actual_input_total = getattr(telemetry, "actual_input_tokens", 0)
    actual_output_total = getattr(telemetry, "actual_output_tokens", 0)
    cached_total = getattr(telemetry, "cached_input_tokens", 0)
    reasoning_total = getattr(telemetry, "reasoning_tokens", 0)

    if actual_input is not None:
        actual_input_total += int(actual_input)
    if actual_output is not None:
        actual_output_total += int(actual_output)
    if cached_input is not None:
        cached_total += int(cached_input)
    if reasoning_tokens is not None:
        reasoning_total += int(reasoning_tokens)

    setattr(telemetry, "actual_input_tokens", actual_input_total)
    setattr(telemetry, "actual_output_tokens", actual_output_total)
    setattr(telemetry, "cached_input_tokens", cached_total)
    setattr(telemetry, "reasoning_tokens", reasoning_total)

    actual_total = getattr(telemetry, "actual_total_tokens", 0)
    if usage.total is not None:
        actual_total += int(usage.total)
    elif actual_input is not None or actual_output is not None:
        actual_total += int(actual_input or 0) + int(actual_output or 0)
    setattr(telemetry, "actual_total_tokens", actual_total)

    telemetry.events.append({
        "type": "model_usage",
        "kind": kind,
        "provider": response.provider,
        "model": response.model,
        "model_tier": response.model_tier,
        "actual_input": actual_input,
        "actual_output": actual_output,
        "cached_input": cached_input,
        "reasoning_tokens": reasoning_tokens,
        "total": usage.total,
    })
