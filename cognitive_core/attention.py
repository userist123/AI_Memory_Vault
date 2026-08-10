from typing import List, Dict, Any

class AttentionModel:
    """
    Computes attention scores for nodes in Working Memory.
    Attention determines which notes stay in the bounded Working Memory
    when new nodes are introduced.
    """
    def __init__(self, activation_weight: float = 0.5, confidence_weight: float = 0.3, recency_weight: float = 0.2):
        self.activation_weight = activation_weight
        self.confidence_weight = confidence_weight
        self.recency_weight = recency_weight
        
        self.confidence_scores = {
            "very_high": 1.0,
            "high": 0.8,
            "medium": 0.5,
            "low": 0.2,
            "unknown": 0.1
        }
        
    def calculate_score(self, node: Dict[str, Any], activation: float, recency_tick: int, current_tick: int) -> float:
        """
        Calculate an attention score bounded between 0.0 and 1.0.
        Recency decays as current_tick increases relative to recency_tick.
        """
        conf_val = node.get("confidence", "unknown")
        conf_score = self.confidence_scores.get(conf_val, 0.1)
        
        # Simple recency decay: newer is closer to 1.0
        age = current_tick - recency_tick
        recency_score = max(0.0, 1.0 - (age * 0.05))
        
        total_score = (
            (activation * self.activation_weight) +
            (conf_score * self.confidence_weight) +
            (recency_score * self.recency_weight)
        )
        
        return min(1.0, total_score)
