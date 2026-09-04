"""actual_usage_telemetry.py — A5: separate layer tracking REAL provider usage.

Kept completely independent from 99_SYSTEM/council_token_telemetry.py
(which stays frozen, estimation-only, exactly as designed for A1-A4).

    ESTIMATED                    ACTUAL
        |                           |
CouncilTokenTelemetry      ActualUsageTelemetry

This module never imports 99_SYSTEM/council_token_telemetry.py and is
never imported by it. Composition, not modification. A future caller
can hold BOTH objects side by side and compare estimated_total_tokens
vs actual_total_tokens without either object knowing about the other.

Semantics:
  source="provider"           -> usage.actual_* was reported by the
                                  provider itself (a real measurement).
  source="estimated_fallback" -> the provider reported no actual usage;
                                  the estimated_* value was used as a
                                  stand-in for operational reporting.
                                  This is NOT a real measurement and must
                                  never be confused with source="provider"
                                  when comparing local vs OpenAI usage.
"""
from __future__ import annotations

from dataclasses import dataclass, field
from typing import List, Literal

from .model_provider import TokenUsage

UsageSource = Literal["provider", "estimated_fallback"]
UsageKind = Literal["specialist", "synthesis"]


@dataclass(frozen=True)
class UsageEvent:
    provider: str
    model: str
    model_tier: str
    kind: str
    input_tokens: int
    output_tokens: int
    cached_tokens: int
    reasoning_tokens: int
    source: str


@dataclass
class ActualUsageTelemetry:
    specialist_actual_input: int = 0
    specialist_actual_output: int = 0
    specialist_cached_input: int = 0
    specialist_reasoning_tokens: int = 0

    synthesis_actual_input: int = 0
    synthesis_actual_output: int = 0
    synthesis_cached_input: int = 0
    synthesis_reasoning_tokens: int = 0

    events: List[UsageEvent] = field(default_factory=list)

    @staticmethod
    def _input_tokens(usage: TokenUsage) -> int:
        if usage.actual_input is not None:
            return int(usage.actual_input)
        return int(usage.estimated_input)

    @staticmethod
    def _output_tokens(usage: TokenUsage) -> int:
        if usage.actual_output is not None:
            return int(usage.actual_output)
        return int(usage.estimated_output)

    @staticmethod
    def _cached_tokens(usage: TokenUsage) -> int:
        return int(usage.cached_input or 0)

    @staticmethod
    def _reasoning_tokens(usage: TokenUsage) -> int:
        return int(usage.reasoning_tokens or 0)

    @staticmethod
    def _source(usage: TokenUsage) -> str:
        if usage.actual_input is not None and usage.actual_output is not None:
            return "provider"
        return "estimated_fallback"

    def _record(
        self,
        kind: str,
        usage: TokenUsage,
        provider: str,
        model: str,
        model_tier: str,
    ) -> None:
        input_tokens = self._input_tokens(usage)
        output_tokens = self._output_tokens(usage)
        cached_tokens = self._cached_tokens(usage)
        reasoning_tokens = self._reasoning_tokens(usage)
        source = self._source(usage)

        if kind == "specialist":
            self.specialist_actual_input += input_tokens
            self.specialist_actual_output += output_tokens
            self.specialist_cached_input += cached_tokens
            self.specialist_reasoning_tokens += reasoning_tokens
        elif kind == "synthesis":
            self.synthesis_actual_input += input_tokens
            self.synthesis_actual_output += output_tokens
            self.synthesis_cached_input += cached_tokens
            self.synthesis_reasoning_tokens += reasoning_tokens
        else:
            raise ValueError(
                f"Unknown usage kind: {kind!r}. Expected 'specialist' or 'synthesis'."
            )

        self.events.append(
            UsageEvent(
                provider=provider,
                model=model,
                model_tier=model_tier,
                kind=kind,
                input_tokens=input_tokens,
                output_tokens=output_tokens,
                cached_tokens=cached_tokens,
                reasoning_tokens=reasoning_tokens,
                source=source,
            )
        )

    def record_specialist_actual(
        self,
        usage: TokenUsage,
        provider: str = "unknown",
        model: str = "unknown",
        model_tier: str = "unknown",
    ) -> None:
        self._record("specialist", usage, provider, model, model_tier)

    def record_synthesis_actual(
        self,
        usage: TokenUsage,
        provider: str = "unknown",
        model: str = "unknown",
        model_tier: str = "unknown",
    ) -> None:
        self._record("synthesis", usage, provider, model, model_tier)

    @property
    def actual_total_tokens(self) -> int:
        return (
            self.specialist_actual_input
            + self.specialist_actual_output
            + self.synthesis_actual_input
            + self.synthesis_actual_output
        )

    @property
    def actual_cached_tokens(self) -> int:
        return self.specialist_cached_input + self.synthesis_cached_input

    @property
    def actual_reasoning_tokens(self) -> int:
        return self.specialist_reasoning_tokens + self.synthesis_reasoning_tokens

    @property
    def actual_input_tokens(self) -> int:
        return self.specialist_actual_input + self.synthesis_actual_input

    @property
    def actual_output_tokens(self) -> int:
        return self.specialist_actual_output + self.synthesis_actual_output

    @property
    def has_real_provider_usage(self) -> bool:
        """True only if at least one event carries a genuine provider measurement.

        False if every recorded event fell back to estimated_* values.
        Use this to avoid presenting fallback estimates as real usage.
        """
        return any(event.source == "provider" for event in self.events)
