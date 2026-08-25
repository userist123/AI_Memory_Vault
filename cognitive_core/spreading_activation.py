"""Spreading-activation retrieval fused across the multi-graph memory.

Activation propagates from seed nodes along graph edges with exponential
hop decay, mirroring ACT-R-style spreading activation. Results are fused
with base relevance scores; this module never mutates canonical memory.
"""
from __future__ import annotations

from typing import Dict, Iterable, List, Tuple

from .multi_graph import MultiGraphMemory

_DEFAULT_GRAPH_WEIGHTS = {"semantic": 1.0, "temporal": 0.5, "causal": 0.8, "entity": 0.6}


class SpreadingActivationEngine:
    def __init__(self, multi_graph: MultiGraphMemory, decay: float = 0.6, max_hops: int = 2,
                 graph_weights: Dict[str, float] = None):
        if not 0 < decay < 1:
            raise ValueError("decay must be in (0, 1)")
        if max_hops < 1:
            raise ValueError("max_hops must be >= 1")
        self.multi_graph = multi_graph
        self.decay = decay
        self.max_hops = max_hops
        self.graph_weights = graph_weights or dict(_DEFAULT_GRAPH_WEIGHTS)

    def _propagate_on_graph(self, graph, seeds: Dict[str, float]) -> Dict[str, float]:
        activation: Dict[str, float] = dict(seeds)
        frontier: List[Tuple[str, float, int]] = [(node, score, 0) for node, score in seeds.items()]
        while frontier:
            node_id, score, hop = frontier.pop()
            if hop >= self.max_hops:
                continue
            for neighbor, attrs in graph.neighbors(node_id):
                weight = float(attrs.get("weight", 1.0))
                propagated = score * self.decay * min(weight, 3.0) / 3.0 if weight > 3 else score * self.decay * (weight if weight <= 1 else 1.0)
                propagated = score * (self.decay ** (hop + 1))
                if propagated <= 1e-6:
                    continue
                if propagated > activation.get(neighbor, 0.0):
                    activation[neighbor] = propagated
                    frontier.append((neighbor, propagated, hop + 1))
        return activation

    def activate(self, seed_scores: Dict[str, float]) -> Dict[str, float]:
        """Propagate seed activation across all four graphs and fuse results."""
        fused: Dict[str, float] = {}
        for graph_name, weight in self.graph_weights.items():
            graph = getattr(self.multi_graph, graph_name)
            propagated = self._propagate_on_graph(graph, seed_scores)
            for node_id, score in propagated.items():
                fused[node_id] = fused.get(node_id, 0.0) + score * weight
        return fused

    def rank(self, base_scores: Dict[str, float], top_k: int = None) -> List[Tuple[str, float]]:
        """Fuse base relevance scores with spreading-activation scores, then rank."""
        activated = self.activate(base_scores)
        combined = {
            node_id: base_scores.get(node_id, 0.0) + activated.get(node_id, 0.0)
            for node_id in set(base_scores) | set(activated)
        }
        ranked = sorted(combined.items(), key=lambda item: item[1], reverse=True)
        return ranked[:top_k] if top_k else ranked
