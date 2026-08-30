"""Provider-neutral token accounting for Council runs.

This module keeps selection/context accounting backward-compatible while
adding optional provider-reported usage fields. Concrete providers populate
actual usage through the model usage adapter; estimated counters remain useful
for local models or providers that do not report token usage.
"""
from __future__ import annotations

from dataclasses import dataclass, field
from math import ceil
from typing import Any, Dict, Iterable, List
import json


def estimate_tokens(value: Any) -> int:
    text = value if isinstance(value, str) else json.dumps(
        value, ensure_ascii=False, default=str, separators=(",", ":")
    )
    return ceil(len(text) / 3)


@dataclass
class CouncilTokenTelemetry:
    agents_selected: int = 0
    skills_selected: int = 0
    memory_items_selected: int = 0
    graph_items_selected: int = 0
    raw_context_tokens: int = 0
    deduplicated_context_tokens: int = 0
    specialist_input_tokens: int = 0
    specialist_output_tokens: int = 0
    synthesis_input_tokens: int = 0
    synthesis_output_tokens: int = 0
    actual_input_tokens: int = 0
    actual_output_tokens: int = 0
    cached_input_tokens: int = 0
    reasoning_tokens: int = 0
    actual_total_tokens: int = 0
    saved_by_deduplication: int = 0
    rejected_items: int = 0
    events: List[Dict[str, Any]] = field(default_factory=list)

    def record_context(self, items: Iterable[Any]) -> int:
        values = list(items)
        self.raw_context_tokens = sum(estimate_tokens(x) for x in values)
        seen = set()
        unique = []
        for item in values:
            key = json.dumps(
                item, ensure_ascii=False, default=str, sort_keys=True, separators=(",", ":")
            )
            if key not in seen:
                seen.add(key)
                unique.append(item)
        self.deduplicated_context_tokens = sum(estimate_tokens(x) for x in unique)
        self.saved_by_deduplication = max(
            0, self.raw_context_tokens - self.deduplicated_context_tokens
        )
        self.events.append({
            "type": "context",
            "raw_tokens": self.raw_context_tokens,
            "deduplicated_tokens": self.deduplicated_context_tokens,
        })
        return self.deduplicated_context_tokens

    def record_specialist(self, input_value: Any, output_value: Any) -> None:
        self.specialist_input_tokens += estimate_tokens(input_value)
        self.specialist_output_tokens += estimate_tokens(output_value)

    def record_synthesis(self, input_value: Any, output_value: Any) -> None:
        self.synthesis_input_tokens += estimate_tokens(input_value)
        self.synthesis_output_tokens += estimate_tokens(output_value)

    def record_actual_usage(
        self,
        *,
        kind: str,
        provider: str,
        model: str,
        model_tier: str,
        actual_input: int | None,
        actual_output: int | None,
        cached_input: int | None,
        reasoning_tokens: int | None,
        total: int | None,
    ) -> None:
        """Append provider-reported usage without disturbing estimate counters."""
        self.actual_input_tokens += int(actual_input or 0)
        self.actual_output_tokens += int(actual_output or 0)
        self.cached_input_tokens += int(cached_input or 0)
        self.reasoning_tokens += int(reasoning_tokens or 0)
        if total is not None:
            self.actual_total_tokens += int(total)
        elif actual_input is not None or actual_output is not None:
            self.actual_total_tokens += int(actual_input or 0) + int(actual_output or 0)

        self.events.append({
            "type": "model_usage",
            "kind": kind,
            "provider": provider,
            "model": model,
            "model_tier": model_tier,
            "actual_input": actual_input,
            "actual_output": actual_output,
            "cached_input": cached_input,
            "reasoning_tokens": reasoning_tokens,
            "total": total,
        })

    @property
    def estimated_total_tokens(self) -> int:
        """Estimated model-token volume across specialist and synthesis calls."""
        return (
            self.specialist_input_tokens
            + self.specialist_output_tokens
            + self.synthesis_input_tokens
            + self.synthesis_output_tokens
        )

    @property
    def estimated_context_savings(self) -> int:
        return self.saved_by_deduplication

    def as_dict(self) -> Dict[str, Any]:
        return {
            "agents_selected": self.agents_selected,
            "skills_selected": self.skills_selected,
            "memory_items_selected": self.memory_items_selected,
            "graph_items_selected": self.graph_items_selected,
            "raw_context_tokens": self.raw_context_tokens,
            "deduplicated_context_tokens": self.deduplicated_context_tokens,
            "specialist_input_tokens": self.specialist_input_tokens,
            "specialist_output_tokens": self.specialist_output_tokens,
            "synthesis_input_tokens": self.synthesis_input_tokens,
            "synthesis_output_tokens": self.synthesis_output_tokens,
            "actual_input_tokens": self.actual_input_tokens,
            "actual_output_tokens": self.actual_output_tokens,
            "cached_input_tokens": self.cached_input_tokens,
            "reasoning_tokens": self.reasoning_tokens,
            "actual_total_tokens": self.actual_total_tokens,
            "saved_by_deduplication": self.saved_by_deduplication,
            "estimated_context_savings": self.estimated_context_savings,
            "rejected_items": self.rejected_items,
            "estimated_total_tokens": self.estimated_total_tokens,
        }
