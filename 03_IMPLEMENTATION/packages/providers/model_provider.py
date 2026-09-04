"""model_provider.py — A1: Provider-neutral model execution contract.

This module defines the boundary between orchestration (Planner, Analyzer,
CouncilBudgetController, Orchestrator, Executive) and model execution
(local models, OpenAI, or any future provider).

Invariants this module MUST preserve:
  1. deterministic          — no hidden randomness in the contract itself
  2. provider-neutral        — no OpenAI/Ollama/vendor names anywhere here
  3. no network              — this file makes zero network calls
  4. no secrets               — no API keys, tokens, or credentials here
  5. no SDK dependency        — no openai/ollama/anthropic imports
  6. no Planner dependency    — does not import cognitive_core.planning
  7. no Council dependency    — does not import council_budget_controller
  8. no MemoryController dep. — does not import memory_controller

Concrete providers (LocalProvider, OpenAIProvider, ...) implement this
Protocol in separate modules. The orchestration core never imports a
concrete provider directly — it only depends on ModelProvider.
"""
from __future__ import annotations

from dataclasses import dataclass, field
from typing import Any, Dict, Optional, Protocol, runtime_checkable


@dataclass(frozen=True)
class TokenUsage:
    """Three-tier token accounting: estimated -> assembled -> actual.

    actual_* fields are Optional by design: a local model may only ever
    populate estimated_*, while a provider that reports real usage can
    populate actual_*. The same contract must work in both worlds.
    """
    estimated_input: int = 0
    estimated_output: int = 0

    actual_input: Optional[int] = None
    actual_output: Optional[int] = None
    cached_input: Optional[int] = None
    reasoning_tokens: Optional[int] = None
    total: Optional[int] = None

    @property
    def effective_total(self) -> int:
        if self.total is not None:
            return self.total

        actual_in = self.actual_input if self.actual_input is not None else self.estimated_input
        actual_out = self.actual_output if self.actual_output is not None else self.estimated_output
        return actual_in + actual_out


@dataclass(frozen=True)
class ModelRequest:
    prompt: str
    model_tier: str
    system_prompt: Optional[str] = None
    tools: tuple[Dict[str, Any], ...] = ()
    metadata: Dict[str, Any] = field(default_factory=dict)


@dataclass(frozen=True)
class ModelResponse:
    content: str
    provider: str
    model: str
    model_tier: str
    usage: TokenUsage
    metadata: Dict[str, Any] = field(default_factory=dict)


@runtime_checkable
class ModelProvider(Protocol):
    """Minimal contract every concrete provider must satisfy.

    Orchestration code depends ONLY on this Protocol — never on a
    concrete provider class. This is what keeps model selection a
    config-level decision instead of a code-level one.
    """

    def generate(self, request: ModelRequest) -> ModelResponse:
        ...

    def health(self) -> Dict[str, Any]:
        ...
