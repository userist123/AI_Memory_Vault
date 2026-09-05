r"""
ACT-R Base-Level Activation Engine for Cognitive Core.

Theoretical Foundation:
Based on John R. Anderson's ACT-R (Adaptive Control of Thought-Rational) cognitive architecture.
Base-level activation reflects the recency and frequency of access to a memory chunk:
    B_i = ln( \sum_{j=1}^{n} t_j^{-d} )
where t_j is the time elapsed since the j-th access, and d is the decay rate (typically 0.5).

Memories below DORMANT_THRESHOLD decay into dormant state, remaining retrievable
explicitly but uncompetitive for automatic working memory broadcast.
"""

import time
import math
from typing import List, Dict, Any, Tuple, Optional
from memory_controller.controller import MemoryController
from memory_controller.authorizer import Principal
from .synapse import SynapticGraph

DORMANT_THRESHOLD = -2.0
DEFAULT_DECAY_RATE = 0.5


def base_level_activation(access_times: List[float], decay: float = DEFAULT_DECAY_RATE, current_time: Optional[float] = None) -> float:
    """
    Computes ACT-R base-level activation: B_i = ln( sum_{j=1}^n (t - t_j)^(-d) )
    If access_times is empty, returns DORMANT_THRESHOLD.
    """
    if not access_times:
        return DORMANT_THRESHOLD
        
    now = current_time if current_time is not None else time.time()
    sum_decayed = 0.0
    
    for t_j in access_times:
        elapsed = now - t_j
        if elapsed <= 0.0:
            elapsed = 0.001  # Minimum time delta to prevent division by zero or negative time
        sum_decayed += math.pow(elapsed, -decay)
        
    if sum_decayed <= 0.0:
        return DORMANT_THRESHOLD
        
    return math.log(sum_decayed)


class ActivationRecord:
    """
    Tracks access history, frequency, and recency for a specific memory note.
    """
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

    def calculate_activation(self, decay: float = DEFAULT_DECAY_RATE, current_time: Optional[float] = None) -> float:
        return base_level_activation(self.access_history, decay=decay, current_time=current_time)

    def is_dormant(self, threshold: float = DORMANT_THRESHOLD, current_time: Optional[float] = None) -> bool:
        return self.calculate_activation(current_time=current_time) < threshold


class ActivationTracker:
    """
    Central registry for tracking memory note activation records.
    """
    _instance: Optional['ActivationTracker'] = None

    def __init__(self):
        self.records: Dict[str, ActivationRecord] = {}

    @classmethod
    def get_instance(cls) -> 'ActivationTracker':
        if cls._instance is None:
            cls._instance = cls()
        return cls._instance

    def record_access(self, note_id: str, timestamp: Optional[float] = None) -> float:
        if note_id not in self.records:
            self.records[note_id] = ActivationRecord(note_id)
        return self.records[note_id].record_access(timestamp)

    def get_activation(self, note_id: str, current_time: Optional[float] = None) -> float:
        if note_id not in self.records:
            return DORMANT_THRESHOLD
        return self.records[note_id].calculate_activation(current_time=current_time)

    def is_dormant(self, note_id: str, threshold: float = DORMANT_THRESHOLD, current_time: Optional[float] = None) -> bool:
        if note_id not in self.records:
            return True
        return self.records[note_id].is_dormant(threshold=threshold, current_time=current_time)

    def reset(self):
        self.records.clear()


class ActivationEngine:
    """
    Spreading activation engine for the Cognitive Core.
    Traverses the synaptic graph deterministically without bypassing MemoryController policies.
    Integrates ACT-R base-level activation decay.
    """
    def __init__(self, memory_controller: MemoryController, max_depth: int = 3, max_nodes: int = 20, decay_factor: float = DEFAULT_DECAY_RATE):
        self.controller = memory_controller
        self.max_depth = max_depth
        self.max_nodes = max_nodes
        self.decay_factor = decay_factor
        self.tracker = ActivationTracker.get_instance()

    def activate_from_query(self, principal: Principal, query: str) -> List[Tuple[Dict[str, Any], float]]:
        """
        Activates initial neurons via search and spreads activation.
        """
        search_pack = self.controller.search(principal, query, page_size=self.max_nodes)
        initial_results = search_pack.get("results", [])
        
        active_nodes = {}
        queue = []
        
        for idx, res in enumerate(initial_results):
            node_id = res.get("id")
            if node_id:
                # Record search activation hit
                self.tracker.record_access(node_id)
                act_score = self.tracker.get_activation(node_id)
                # Map log activation into normalized [0, 1] range for spreading
                normalized_act = max(0.1, min(1.0, (act_score - DORMANT_THRESHOLD) / 5.0)) * (0.9 ** idx)
                active_nodes[node_id] = {"node": res, "activation": normalized_act}
                queue.append((node_id, 0, normalized_act))
                
        return self._spread_activation(principal, queue, active_nodes)

    def activate_from_ids(self, principal: Principal, node_ids: List[str]) -> List[Tuple[Dict[str, Any], float]]:
        """
        Activates specific neurons by ID and spreads activation.
        """
        active_nodes = {}
        queue = []
        
        for node_id in node_ids:
            try:
                pack = self.controller.cognitive_read(principal, node_id)
                res = pack.get("results", [])
                if res:
                    node = res[0]
                    self.tracker.record_access(node_id)
                    act_score = self.tracker.get_activation(node_id)
                    normalized_act = max(0.1, min(1.0, (act_score - DORMANT_THRESHOLD) / 5.0))
                    active_nodes[node_id] = {"node": node, "activation": normalized_act}
                    queue.append((node_id, 0, normalized_act))
            except (ValueError, AttributeError):
                pass
                
        return self._spread_activation(principal, queue, active_nodes)

    def _spread_activation(self, principal: Principal, queue: List[Tuple[str, int, float]], active_nodes: Dict[str, Any]) -> List[Tuple[Dict[str, Any], float]]:
        """
        Breadth-first spreading activation respecting depth and node limits.
        """
        visited = set(active_nodes.keys())
        
        while queue and len(active_nodes) < self.max_nodes:
            current_id, depth, current_activation = queue.pop(0)
            
            if depth >= self.max_depth:
                continue
                
            current_node = active_nodes[current_id]["node"]
            synapses = SynapticGraph.extract_synapses(current_node)
            synapses = sorted(synapses, key=lambda s: s.target_id)
            
            for synapse in synapses:
                if len(active_nodes) >= self.max_nodes:
                    break
                    
                next_id = synapse.target_id
                next_activation = current_activation * self.decay_factor
                
                if next_activation < 0.1:
                    continue
                    
                if next_id not in visited:
                    visited.add(next_id)
                    try:
                        pack = self.controller.cognitive_read(principal, next_id)
                        res = pack.get("results", [])
                        if res:
                            node = res[0]
                            active_nodes[next_id] = {"node": node, "activation": next_activation}
                            queue.append((next_id, depth + 1, next_activation))
                    except (ValueError, AttributeError):
                        pass
                else:
                    old_act = active_nodes[next_id]["activation"]
                    active_nodes[next_id]["activation"] = min(1.0, old_act + next_activation)
                    
        sorted_nodes = sorted(
            active_nodes.items(),
            key=lambda x: (x[1]["activation"], x[0]),
            reverse=True
        )
        
        return [(v["node"], v["activation"]) for k, v in sorted_nodes]
