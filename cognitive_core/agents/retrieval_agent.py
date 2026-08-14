from typing import Dict, Any, List, Optional
from memory_controller.authorizer import Principal
from memory_controller.controller import MemoryController
from cognitive_core.tool_router import ToolRouter
from cognitive_core.semantic import DeterministicSemanticProvider
from cognitive_core.recall import RecallEngine
from cognitive_core.activation import ActivationEngine
from cognitive_core.working_memory import WorkingMemory
from .base_agent import BaseWorkerAgent

class RetrievalAgent(BaseWorkerAgent):
    """Specialized Memory Retrieval Agent.
    Executes hybrid associative activation, semantic scoring, and supersession lineage resolution.
    """

    def __init__(self, controller: MemoryController, router: Optional[ToolRouter] = None):
        super().__init__(name="RetrievalAgent", role="retrieval", controller=controller, router=router, max_steps=3)
        self.permitted_actions = ["search", "read"]
        self.semantic = DeterministicSemanticProvider()
        self.activation_engine = ActivationEngine(self.controller)
        self.recall_engine = RecallEngine(self.controller, self.semantic)

    def process_task(self, principal: Principal, task: Dict[str, Any]) -> Dict[str, Any]:
        query = task.get("query", "")
        wm = task.get("working_memory") or WorkingMemory()

        # 1. Activate from graph
        activated = self.activation_engine.activate_from_query(principal, query)

        # 2. Score via RecallEngine with lineage resolution
        recalled = self.recall_engine.recall(principal, query, activated, wm)

        return {
            "status": "success",
            "query": query,
            "results": [node for node, score in recalled],
            "scored_results": recalled,
            "total_found": len(recalled)
        }
