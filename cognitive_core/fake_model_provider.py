"""fake_model_provider.py — A2: Deterministic, zero-cost ModelProvider.

Used to test the entire Council -> model_tier -> provider -> usage ->
telemetry flow without any network call, API key, or real model.

Same invariants as model_provider.py: deterministic, provider-neutral,
no network, no secrets, no SDK dependency, no Planner/Council/
MemoryController dependency.
"""
from __future__ import annotations

from typing import Any, Dict, List

from .model_provider import ModelRequest, ModelResponse, TokenUsage


class FakeModelProvider:
    """Drop-in ModelProvider for tests. Records every call for introspection."""

    def __init__(self, provider_name: str = "fake", model_name: str = "fake-model") -> None:
        self.provider_name = provider_name
        self.model_name = model_name
        self.calls: List[ModelRequest] = []

    def generate(self, request: ModelRequest) -> ModelResponse:
        self.calls.append(request)

        input_tokens = max(1, (len(request.prompt) + 2) // 3)
        output_text = f"FAKE_RESPONSE[{request.model_tier}]"
        output_tokens = max(1, (len(output_text) + 2) // 3)

        usage = TokenUsage(
            estimated_input=input_tokens,
            estimated_output=output_tokens,
            actual_input=input_tokens,
            actual_output=output_tokens,
            total=input_tokens + output_tokens,
        )

        return ModelResponse(
            content=output_text,
            provider=self.provider_name,
            model=self.model_name,
            model_tier=request.model_tier,
            usage=usage,
            metadata={
                "fake": True,
                "call_index": len(self.calls),
            },
        )

    def health(self) -> Dict[str, Any]:
        return {
            "status": "ok",
            "provider": self.provider_name,
            "model": self.model_name,
        }
