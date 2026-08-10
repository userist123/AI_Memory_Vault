from typing import List, Dict, Any, Tuple
from memory_controller.controller import MemoryController
from memory_controller.authorizer import Principal
from .synapse import SynapticGraph

class ActivationEngine:
    """
    Spreading activation engine for the Cognitive Core.
    Traverses the synaptic graph deterministically without bypassing MemoryController policies.
    """
    def __init__(self, memory_controller: MemoryController, max_depth: int = 3, max_nodes: int = 20, decay_factor: float = 0.5):
        self.controller = memory_controller
        self.max_depth = max_depth
        self.max_nodes = max_nodes
        self.decay_factor = decay_factor

    def activate_from_query(self, principal: Principal, query: str) -> List[Tuple[Dict[str, Any], float]]:
        """
        Activates initial neurons via search and spreads activation.
        """
        # Initial retrieval via public API
        search_pack = self.controller.search(principal, query, page_size=self.max_nodes)
        initial_results = search_pack.get("results", [])
        
        active_nodes = {}
        queue = []
        
        # Assign deterministic initial activation
        for idx, res in enumerate(initial_results):
            # Base activation decays slightly by rank
            activation = 1.0 * (0.9 ** idx)
            node_id = res.get("id")
            if node_id:
                active_nodes[node_id] = {"node": res, "activation": activation}
                queue.append((node_id, 0, activation))
                
        return self._spread_activation(principal, queue, active_nodes)

    def activate_from_ids(self, principal: Principal, node_ids: List[str]) -> List[Tuple[Dict[str, Any], float]]:
        """
        Activates specific neurons by ID and spreads activation.
        """
        active_nodes = {}
        queue = []
        
        for node_id in node_ids:
            try:
                # Read requires ACTIVE lifecycle via public API unless principal is ADMIN
                pack = self.controller.cognitive_read(principal, node_id)
                res = pack.get("results", [])
                if res:
                    node = res[0]
                    active_nodes[node_id] = {"node": node, "activation": 1.0}
                    queue.append((node_id, 0, 1.0))
            except (ValueError, AttributeError):
                # If unauthorized or non-ACTIVE, just skip
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
            
            # Sort synapses deterministically by target_id to ensure consistent ordering
            synapses = sorted(synapses, key=lambda s: s.target_id)
            
            for synapse in synapses:
                if len(active_nodes) >= self.max_nodes:
                    break
                    
                next_id = synapse.target_id
                next_activation = current_activation * self.decay_factor
                
                # Minimum activation threshold to prune weak paths
                if next_activation < 0.1:
                    continue
                    
                if next_id not in visited:
                    visited.add(next_id)
                    try:
                        # Retrieve neighbor strictly through MemoryController
                        pack = self.controller.cognitive_read(principal, next_id)
                        res = pack.get("results", [])
                        if res:
                            node = res[0]
                            active_nodes[next_id] = {"node": node, "activation": next_activation}
                            queue.append((next_id, depth + 1, next_activation))
                    except (ValueError, AttributeError):
                        # Skip if blocked by security, audit, or lifecycle rules
                        pass
                else:
                    # If already visited, boost activation bounded by 1.0
                    old_act = active_nodes[next_id]["activation"]
                    active_nodes[next_id]["activation"] = min(1.0, old_act + next_activation)
                    
        # Sort by activation descending, deterministic tie-break by ID ascending
        sorted_nodes = sorted(
            active_nodes.items(),
            key=lambda x: (x[1]["activation"], x[0]),
            reverse=True
        )
        
        # Return sorted list of (node_dict, activation_score)
        # Note: Provenance is preserved because we return the original node dictionary retrieved from MemoryController
        return [(v["node"], v["activation"]) for k, v in sorted_nodes]
