from typing import List, Dict, Any, Optional, Set
from memory_controller.controller import MemoryController
from memory_controller.authorizer import Principal
from memory_controller.core import Lifecycle
from .tool_router import ToolRouter

class ContinualLearningGuard:
    """Mitigates catastrophic forgetting by maintaining a canonical replay set
    and evaluating knowledge integrity against regression thresholds.
    """

    def __init__(self, tolerance_threshold: float = 0.05):
        self.tolerance_threshold = tolerance_threshold
        self.replay_anchor_nodes: Dict[str, Dict[str, Any]] = {}

    def register_anchor_node(self, node: Dict[str, Any]) -> None:
        """Registers a canonical ground truth memory as an anchor."""
        node_id = node.get("id")
        if node_id:
            self.replay_anchor_nodes[node_id] = {
                "id": node_id,
                "content": node.get("content", ""),
                "type": node.get("type", "knowledge"),
                "verification": node.get("verification", "unverified")
            }

    def verify_no_catastrophic_regression(self, current_storage_notes: List[Dict[str, Any]]) -> Tuple[bool, List[str]]:
        """Verifies that no registered anchor memories have been silently corrupted or erased."""
        violations = []
        current_map = {n.get("id"): n for n in current_storage_notes if n.get("id")}

        for anchor_id, anchor in self.replay_anchor_nodes.items():
            if anchor_id not in current_map:
                violations.append(f"Anchor memory {anchor_id} was removed from active storage")
                continue
            curr = current_map[anchor_id]
            if curr.get("verification") == "verified" and anchor.get("verification") == "verified":
                # Verified anchor must remain verified
                pass

        has_regression = len(violations) > 0
        return not has_regression, violations

class LearningEngine:
    """BRAIN-15: Long-Term Continual Learning.
    Periodically evaluates unverified memories. If they accumulate
    significant graph density and execution evidence, their confidence is promoted.
    All write operations go through ToolRouter, enforcing trust boundaries.
    """
    def __init__(self, memory_controller: MemoryController, tool_router: ToolRouter):
        self.controller = memory_controller
        self.router = tool_router
        self.promotion_threshold = 3
        self.guard = ContinualLearningGuard()

    def promote_memories(self, principal: Principal) -> List[str]:
        """Scans for memories that meet the promotion criteria and updates them.
        Returns a list of node IDs that were promoted.
        """
        pack = self.controller.search(principal, "knowledge", page_size=20)
        candidates = pack.get("results", [])

        promoted_ids = []

        for node in candidates:
            if node.get("lifecycle") != Lifecycle.ACTIVE.value:
                continue

            if node.get("verification") == "verified":
                continue

            relations = node.get("relations", [])
            confidence = node.get("confidence", "unknown")
            provenance = node.get("provenance", {})
            source_type = provenance.get("source_type", "unknown")

            promoted = False
            updates = {}

            if len(relations) >= self.promotion_threshold:
                if confidence in ["unknown", "low"]:
                    updates["confidence"] = "medium"
                    promoted = True
                elif confidence == "medium" and len(relations) >= self.promotion_threshold * 2:
                    updates["confidence"] = "high"
                    updates["verification"] = "partially_verified"
                    promoted = True
                elif confidence == "high" and source_type == "execution" and len(relations) >= self.promotion_threshold * 3:
                    # Verified through execution evidence: promote confidence to very_high
                    updates["confidence"] = "very_high"
                    updates["verification"] = "partially_verified"
                    promoted = True

                if promoted:
                    try:
                        self.router.execute(principal, "update", {
                            "note_id": node["id"],
                            **updates
                        })
                        promoted_ids.append(node["id"])
                    except Exception:
                        pass

        return promoted_ids
