from typing import List, Dict, Any, Optional
from memory_controller.controller import MemoryController
from memory_controller.authorizer import Principal
from memory_controller.core import Lifecycle
from .tool_router import ToolRouter

class LearningEngine:
    """
    BRAIN-15: Long-Term Learning.
    Periodically evaluates unverified memories. If they have accumulated
    significant graph density, their confidence is promoted automatically.
    All write operations go through ToolRouter.
    """
    def __init__(self, memory_controller: MemoryController, tool_router: ToolRouter):
        self.controller = memory_controller
        self.router = tool_router
        self.promotion_threshold = 3

    def promote_memories(self, principal: Principal) -> List[str]:
        """
        Scans for memories that meet the promotion criteria and updates them.
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
            
            if len(relations) >= self.promotion_threshold:
                promoted = False
                updates = {}
                if confidence in ["unknown", "low"]:
                    updates["confidence"] = "medium"
                    promoted = True
                elif confidence == "medium" and len(relations) >= self.promotion_threshold * 2:
                    updates["confidence"] = "high"
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
