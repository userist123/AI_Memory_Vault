from typing import Dict, Any, List, Optional
from memory_controller.authorizer import Principal
from memory_controller.controller import MemoryController
from cognitive_core.tool_router import ToolRouter
from cognitive_core.consolidation import Consolidator
from cognitive_core.deduplication import Deduplicator
from cognitive_core.semantic import DeterministicSemanticProvider
from .base_agent import BaseWorkerAgent

class ConsolidatorAgent(BaseWorkerAgent):
    """Specialized Dedup & Consolidation Agent.
    Scans memory for duplicates and synthesizes ephemeral REVIEW lessons into canonical knowledge.
    """

    def __init__(self, controller: MemoryController, router: Optional[ToolRouter] = None):
        super().__init__(name="ConsolidatorAgent", role="consolidator", controller=controller, router=router, max_steps=4)
        self.permitted_actions = ["search", "read", "propose", "archive"]
        self.semantic = DeterministicSemanticProvider()
        self.consolidator = Consolidator(self.controller, self.router)
        self.deduplicator = Deduplicator(self.controller, self.semantic, self.router)

    def process_task(self, principal: Principal, task: Dict[str, Any]) -> Dict[str, Any]:
        task_type = task.get("type", "all")
        results = {}

        if task_type in ["dedup", "all"]:
            flagged_duplicates = self.deduplicator.scan_for_duplicates(principal)
            results["duplicates_flagged"] = len(flagged_duplicates)

        if task_type in ["consolidate", "all"]:
            consolidated_id = self.consolidator.consolidate_lessons(principal)
            results["consolidated_id"] = consolidated_id

        return {
            "status": "success",
            "task_type": task_type,
            "results": results
        }
