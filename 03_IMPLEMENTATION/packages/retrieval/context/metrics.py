# -*- coding: utf-8 -*-
"""Metrics utilities for the Context Economy layer.

Provides simple counters for retrieval operations, cache hits/misses, and
budget usage. In a full implementation these would be exported to a
monitoring system; here we keep an in‑memory dict.
"""

from collections import defaultdict
from typing import Dict

class Metrics:
    def __init__(self):
        self.counters: Dict[str, int] = defaultdict(int)

    def inc(self, metric_name: str, amount: int = 1) -> None:
        self.counters[metric_name] += amount

    def get(self, metric_name: str) -> int:
        return self.counters.get(metric_name, 0)

    def snapshot(self) -> Dict[str, int]:
        """Return a shallow copy of all counters."""
        return dict(self.counters)
