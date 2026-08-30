"""Provider-neutral token accounting for Council runs.

This module measures context composition without requiring a model SDK. Exact
provider usage should be attached by the model adapter when available.
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

    @property
    def estimated_total_tokens(self) -> int:
        """Estimated model-token volume across specialist and synthesis calls.

        The deduplicated context is an assembly metric, not an additional
        model call, so it is intentionally not added a second time here.
        """
        return (
            self.specialist_input_tokens
            + self.specialist_output_tokens
            + self.synthesis_input_tokens
            + self.synthesis_output_tokens
        )

    @property
    def estimated_context_savings(self) -> int:
        """Tokens removed by context deduplication before model invocation."""
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
            "saved_by_deduplication": self.saved_by_deduplication,
            "estimated_context_savings": self.estimated_context_savings,
            "rejected_items": self.rejected_items,
            "estimated_total_tokens": self.estimated_total_tokens,
        }
