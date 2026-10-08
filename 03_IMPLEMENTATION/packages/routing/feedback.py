from __future__ import annotations

import json
from collections import defaultdict
from dataclasses import dataclass
from pathlib import Path

from .models import RouteFeedback, RoutePrior


@dataclass
class _Stats:
    samples: int = 0
    success: int = 0
    verified: int = 0
    quality_sum: float = 0.0
    cost_sum: float = 0.0
    latency_sum: float = 0.0


class FeedbackStore:
    """Append-only routing feedback. Never performs policy authorization."""

    def __init__(self, path: str | Path | None = None):
        self.path = Path(path) if path else None
        self._stats = defaultdict(_Stats)

    def record(self, feedback: RouteFeedback) -> None:
        key = (feedback.agent_id, feedback.runtime_id)
        s = self._stats[key]
        s.samples += 1
        s.success += int(feedback.success)
        s.verified += int(bool(feedback.verification_passed))
        s.quality_sum += float(feedback.quality_score)
        s.cost_sum += float(feedback.cost_score)
        s.latency_sum += float(feedback.latency_score)
        if self.path:
            self.path.parent.mkdir(parents=True, exist_ok=True)
            with self.path.open("a", encoding="utf-8") as f:
                f.write(json.dumps(feedback.__dict__, ensure_ascii=False) + "\n")

    def prior(self, agent_id: str, runtime_id: str) -> RoutePrior:
        s = self._stats[(agent_id, runtime_id)]
        if not s.samples:
            return RoutePrior(0, 0.0, 0.0, 0.0, 0.0, 0.0)
        return RoutePrior(
            s.samples,
            s.success / s.samples,
            s.verified / s.samples,
            s.quality_sum / s.samples,
            s.cost_sum / s.samples,
            s.latency_sum / s.samples,
        )
