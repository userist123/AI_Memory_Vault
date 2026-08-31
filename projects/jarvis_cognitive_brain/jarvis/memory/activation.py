"""
ACT-R Base-Level Activation Engine and Synaptic Spreading Activation.
"""

import time
import math
import re
import threading
from typing import List, Dict, Any, Tuple, Optional

DORMANT_THRESHOLD: float = -2.0
DEFAULT_DECAY_RATE: float = 0.5


def base_level_activation(
    access_times: List[float],
    decay: float = DEFAULT_DECAY_RATE,
    current_time: Optional[float] = None,
) -> float:
    """
    Computes ACT-R base-level activation:
    B_i = ln( sum_{j=1}^n (t - t_j)^(-d) )
    """
    if not access_times:
        return DORMANT_THRESHOLD

    now = current_time if current_time is not None else time.time()
    sum_decayed = 0.0

    for t_j in access_times:
        elapsed = now - t_j
        if elapsed <= 0.0:
            elapsed = 0.001
        sum_decayed += math.pow(elapsed, -decay)

    if sum_decayed <= 0.0:
        return DORMANT_THRESHOLD

    return math.log(sum_decayed)


class ActivationRecord:
    """Tracks access history, frequency, and recency for a specific memory chunk."""

    def __init__(self, note_id: str, access_history: Optional[List[float]] = None):
        self.note_id = note_id
        self.access_history: List[float] = access_history if access_history is not None else []
        self.last_accessed: float = self.access_history[-1] if self.access_history else time.time()
        self.access_count: int = len(self.access_history)

    def record_access(self, timestamp: Optional[float] = None) -> float:
        ts = timestamp if timestamp is not None else time.time()
        self.access_history.append(ts)
        self.last_accessed = ts
        self.access_count = len(self.access_history)
        return ts

    def calculate_activation(
        self, decay: float = DEFAULT_DECAY_RATE, current_time: Optional[float] = None
    ) -> float:
        return base_level_activation(self.access_history, decay=decay, current_time=current_time)

    def is_dormant(
        self, threshold: float = DORMANT_THRESHOLD, current_time: Optional[float] = None
    ) -> bool:
        return self.calculate_activation(current_time=current_time) < threshold


class ActivationTracker:
    """Thread-safe central registry tracking memory note activation records."""

    _instance: Optional["ActivationTracker"] = None
    _lock = threading.Lock()

    def __init__(self):
        self.records: Dict[str, ActivationRecord] = {}
        self._record_lock = threading.Lock()

    @classmethod
    def get_instance(cls) -> "ActivationTracker":
        with cls._lock:
            if cls._instance is None:
                cls._instance = cls()
            return cls._instance

    def record_access(self, note_id: str, timestamp: Optional[float] = None) -> float:
        with self._record_lock:
            if note_id not in self.records:
                self.records[note_id] = ActivationRecord(note_id)
            return self.records[note_id].record_access(timestamp)

    def get_activation(self, note_id: str, current_time: Optional[float] = None) -> float:
        with self._record_lock:
            if note_id not in self.records:
                return DORMANT_THRESHOLD
            return self.records[note_id].calculate_activation(current_time=current_time)

    def is_dormant(
        self, note_id: str, threshold: float = DORMANT_THRESHOLD, current_time: Optional[float] = None
    ) -> bool:
        with self._record_lock:
            if note_id not in self.records:
                return True
            return self.records[note_id].is_dormant(threshold=threshold, current_time=current_time)

    def reset(self) -> None:
        with self._record_lock:
            self.records.clear()


class SpreadingActivationEngine:
    """Spreads activation across memory note relationships and wikilinks."""

    def __init__(
        self,
        max_depth: int = 3,
        max_nodes: int = 20,
        decay_factor: float = DEFAULT_DECAY_RATE,
    ):
        self.max_depth = max_depth
        self.max_nodes = max_nodes
        self.decay_factor = decay_factor
        self.tracker = ActivationTracker.get_instance()

    def extract_links(self, note: Dict[str, Any]) -> List[str]:
        """Extract explicit target_ids and [[wikilinks]] from note relations and content."""
        links = []
        relations = note.get("relations", [])
        if isinstance(relations, list):
            for rel in relations:
                if isinstance(rel, dict):
                    target_id = rel.get("target_id")
                    if target_id:
                        links.append(target_id)
                    target = rel.get("target", "")
                    # Check wikilink format [[Target]]
                    match = re.search(r"\[\[(.*?)\]\]", target)
                    if match:
                        links.append(match.group(1))

        content = note.get("content", "")
        for match in re.finditer(r"\[\[(.*?)\]\]", content):
            links.append(match.group(1))

        return list(set(links))

    def spread_activation(
        self,
        initial_nodes: List[Dict[str, Any]],
        storage_fetch_func: Optional[Any] = None,
    ) -> List[Tuple[Dict[str, Any], float]]:
        """
        Takes seed notes, computes ACT-R base activation, and spreads activation
        across related links up to max_depth and max_nodes.
        """
        active_nodes: Dict[str, Dict[str, Any]] = {}
        queue: List[Tuple[str, int, float]] = []

        for idx, node in enumerate(initial_nodes):
            node_id = node.get("id")
            if not node_id:
                continue

            self.tracker.record_access(node_id)
            act_score = self.tracker.get_activation(node_id)
            # Map log activation into normalized [0.1, 1.0] range
            norm_act = max(0.1, min(1.0, (act_score - DORMANT_THRESHOLD) / 5.0)) * (0.9 ** idx)
            active_nodes[node_id] = {"node": node, "activation": norm_act}
            queue.append((node_id, 0, norm_act))

        visited = set(active_nodes.keys())

        while queue and len(active_nodes) < self.max_nodes:
            curr_id, depth, curr_act = queue.pop(0)
            if depth >= self.max_depth:
                continue

            curr_node = active_nodes[curr_id]["node"]
            links = self.extract_links(curr_node)

            for target_id in links:
                if len(active_nodes) >= self.max_nodes:
                    break

                next_act = curr_act * self.decay_factor
                if next_act < 0.05:
                    continue

                if target_id not in visited:
                    visited.add(target_id)
                    target_node = None
                    if storage_fetch_func:
                        try:
                            target_node = storage_fetch_func(target_id)
                        except Exception:
                            target_node = None

                    if target_node:
                        active_nodes[target_id] = {"node": target_node, "activation": next_act}
                        queue.append((target_id, depth + 1, next_act))
                else:
                    if target_id in active_nodes:
                        old_act = active_nodes[target_id]["activation"]
                        active_nodes[target_id]["activation"] = min(1.0, old_act + next_act)

        sorted_results = sorted(
            active_nodes.values(),
            key=lambda x: (x["activation"], x["node"].get("id", "")),
            reverse=True,
        )
        return [(item["node"], item["activation"]) for item in sorted_results]
