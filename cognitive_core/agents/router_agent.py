from typing import Dict, Any, List, Optional
from memory_controller.authorizer import Principal
from memory_controller.controller import MemoryController
from cognitive_core.tool_router import ToolRouter
from .base_agent import BaseWorkerAgent

class RouterAgent(BaseWorkerAgent):
    """Specialized Router/Triage Agent.
    Analyzes intent, decomposes query complexity, and plans which specialized subagents are needed.
    """

    def __init__(self, controller: MemoryController, router: Optional[ToolRouter] = None):
        super().__init__(name="RouterAgent", role="router", controller=controller, router=router, max_steps=2)
        self.permitted_actions = ["search", "read"]

    def process_task(self, principal: Principal, task: Dict[str, Any]) -> Dict[str, Any]:
        query = task.get("query", "")
        lowered = query.lower()

        # Determine complexity & dispatch requirements
        needs_retrieval = any(k in lowered for k in ["search", "find", "how", "what", "where", "retrieve", "lookup"]) or len(query.split()) > 2
        needs_verification = any(k in lowered for k in ["verify", "check", "provenance", "trust", "valid", "attest"])
        needs_critic = any(k in lowered for k in ["reflect", "error", "fail", "critique", "review", "why"])
        needs_consolidation = any(k in lowered for k in ["consolidate", "merge", "dedup", "clean", "maintenance"])

        dispatched_agents = []
        if needs_retrieval:
            dispatched_agents.append("retrieval")
        if needs_verification:
            dispatched_agents.append("verifier")
        if needs_critic:
            dispatched_agents.append("critic")
        if needs_consolidation:
            dispatched_agents.append("consolidator")

        if not dispatched_agents:
            dispatched_agents.append("retrieval")

        return {
            "status": "success",
            "query": query,
            "complexity": "high" if len(dispatched_agents) > 1 or len(query.split()) > 8 else "low",
            "target_agents": dispatched_agents
        }
