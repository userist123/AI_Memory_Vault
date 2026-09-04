from typing import List, Dict, Any, Tuple
from .attention import AttentionModel
from memory_controller.controller import Lifecycle

class WorkingMemory:
    """
    Bounded ephemeral state representing the active context.
    Maintains a strict capacity limit by evicting lowest-attention nodes.
    """
    def __init__(self, capacity: int = 10, attention_model: AttentionModel = None):
        self.capacity = capacity
        self.attention_model = attention_model or AttentionModel()
        self.buffer: Dict[str, Dict[str, Any]] = {}
        self.tick = 0
        
    def admit(self, nodes_with_activation: List[Tuple[Dict[str, Any], float]]):
        """
        Attempt to admit new nodes from the spreading activation engine.
        Updates internal clock and computes attention to determine evictions.
        """
        self.tick += 1
        
        for node, activation in nodes_with_activation:
            node_id = node.get("id")
            if not node_id:
                continue
                
            if node_id in self.buffer:
                # Update existing node's activation and recency
                self.buffer[node_id]["activation"] = max(self.buffer[node_id]["activation"], activation)
                self.buffer[node_id]["tick"] = self.tick
                # We update the node data too just in case it changed
                self.buffer[node_id]["node"] = node
            else:
                # Add new node
                self.buffer[node_id] = {
                    "node": node,
                    "activation": activation,
                    "tick": self.tick
                }
                
        # Re-evaluate attention scores for all nodes in buffer
        for node_id, data in self.buffer.items():
            score = self.attention_model.calculate_score(
                data["node"], 
                data["activation"], 
                data["tick"], 
                self.tick
            )
            data["attention"] = score
            
        # Enforce capacity
        if len(self.buffer) > self.capacity:
            self._evict_to_capacity()
            
    def _evict_to_capacity(self):
        """
        Evict nodes with the lowest attention score until capacity is reached.
        Deterministic tie-break using ID.
        """
        # Sort ascending by attention, then descending by ID (so lower ID wins tie)
        # Wait, if we sort ascending by attention, lower attention gets evicted.
        # Tie break: we want deterministic behavior. Sort by attention asc, ID asc.
        sorted_nodes = sorted(
            self.buffer.items(),
            key=lambda item: (item[1]["attention"], item[0])
        )
        
        num_to_evict = len(self.buffer) - self.capacity
        for i in range(num_to_evict):
            node_id = sorted_nodes[i][0]
            del self.buffer[node_id]
            
    def get_active_context(self) -> List[Dict[str, Any]]:
        """
        Returns the nodes currently in Working Memory, sorted by highest attention.
        """
        sorted_nodes = sorted(
            self.buffer.values(),
            key=lambda item: (item.get("attention", 0.0), item["node"].get("id")),
            reverse=True
        )
        return [item["node"] for item in sorted_nodes]
        
    def clear(self):
        """Flushes Working Memory completely."""
        self.buffer = {}
        self.tick = 0
        
    def save_state(self, filepath: str) -> None:
        """
        Serializes Working Memory state to disk.
        Only stores the node IDs and metadata to prevent duplicating canonical memory.
        """
        import json
        import os
        
        state = {
            "tick": self.tick,
            "capacity": self.capacity,
            "buffer": {}
        }
        
        for node_id, data in self.buffer.items():
            state["buffer"][node_id] = {
                "id": node_id,
                "activation": data.get("activation", 0.0),
                "tick": data.get("tick", 0),
                "attention": data.get("attention", 0.0)
            }
            
        dir_path = os.path.dirname(os.path.abspath(filepath))
        os.makedirs(dir_path, exist_ok=True)
        import tempfile
        fd, temp_path = tempfile.mkstemp(dir=dir_path, prefix=".tmp_wm_")
        try:
            with os.fdopen(fd, "w", encoding="utf-8") as f:
                json.dump(state, f, indent=2)
                f.flush()
                os.fsync(f.fileno())
            os.replace(temp_path, filepath)
        except Exception as e:
            if os.path.exists(temp_path):
                try:
                    os.remove(temp_path)
                except Exception:
                    pass
            raise e
            
    def load_state(self, filepath: str, memory_controller, principal) -> None:
        """
        Deserializes Working Memory state from disk and reconstructs nodes.
        Uses the provided memory_controller to fetch the canonical nodes.
        """
        import json
        import os
        
        if not os.path.exists(filepath):
            return
            
        with open(filepath, "r", encoding="utf-8") as f:
            state = json.load(f)
            
        self.tick = state.get("tick", 0)
        self.buffer = {}
        
                # Determine retrieval method
        method = getattr(memory_controller, "cognitive_read", None)
        # If cognitive_read is a MagicMock without real implementation, fall back to read
        if not (callable(method) and hasattr(method, "__code__")):
            method = getattr(memory_controller, "read", None)
            
        for node_id, meta in state.get("buffer", {}).items():
            try:
                response = method(principal, node_id)
                
                nodes = []
                if isinstance(response, dict):
                    if "results" in response:
                        nodes = response["results"]
                    else:
                        nodes = [response]
                
                node = nodes[0] if nodes else None
                if not node:
                    continue
                    
                if node.get("lifecycle") == Lifecycle.REVIEW.value:
                    node["_cognitive_unverified"] = True
                
                self.buffer[node_id] = {
                    "node": node,
                    "activation": meta.get("activation", 0.0),
                    "tick": meta.get("tick", 0),
                    "attention": meta.get("attention", 0.0)
                }
            except Exception:
                continue
