import uuid
from typing import List, Dict, Any, Optional
from memory_controller.controller import MemoryController
from memory_controller.authorizer import Principal
from memory_controller.core import Lifecycle
from .tool_router import ToolRouter

class Consolidator:
    """
    BRAIN-10: Memory Consolidation Routine.
    Periodically scans ephemeral REVIEW lessons and synthesizes them into concrete knowledge.
    All write operations go through ToolRouter to enforce autonomy/reconciliation boundaries.
    """
    def __init__(self, memory_controller: MemoryController, tool_router: ToolRouter):
        self.controller = memory_controller
        self.router = tool_router

    def consolidate_lessons(self, principal: Principal) -> Optional[str]:
        """
        Finds multiple 'lesson' nodes in REVIEW lifecycle and attempts to consolidate them.
        Returns the ID of the new consolidated knowledge node, if any.
        """
        pack = self.controller.search(principal, "lesson", page_size=20)
        results = pack.get("results", [])
        
        lessons_to_consolidate = []
        for node in results:
            if node.get("type") == "lesson" and node.get("lifecycle") == Lifecycle.REVIEW.value:
                lessons_to_consolidate.append(node)
                
        if len(lessons_to_consolidate) < 2:
            return None
            
        combined_content = "Consolidated Knowledge:\n"
        source_refs = []
        relations = []
        
        for lesson in lessons_to_consolidate:
            combined_content += f"- {lesson.get('content', '')[:100]}...\n"
            source_refs.append(lesson.get("id"))
            relations.append({
                "target_id": lesson.get("id"),
                "type": "derived_from",
                "confidence": "high"
            })
            
        new_id = str(uuid.uuid4())
        
        consolidated_node = {
            "id": new_id,
            "type": "knowledge",
            "lifecycle": Lifecycle.REVIEW.value,
            "confidence": "medium",
            "verification": "unverified",
            "provenance": {
                "source_type": "inference",
                "source_refs": source_refs
            },
            "content": combined_content,
            "relations": relations
        }
        
        from .reflection import SelfRefine
        passed, refined_node = SelfRefine.refine_memory(consolidated_node)
        if not passed:
            return None

        # Propose through ToolRouter
        self.router.execute(principal, "propose", {"note_data": refined_node})
        
        # Archive old lessons through ToolRouter
        for lesson in lessons_to_consolidate:
            try:
                self.router.execute(principal, "archive", {
                    "note_id": lesson["id"],
                    "reason": "Consolidated into knowledge node"
                })
            except Exception:
                pass
                    
        return new_id
