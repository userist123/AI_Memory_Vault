import enum
from typing import Dict, Any, List, Optional, Callable
from memory_controller.controller import MemoryController
from memory_controller.authorizer import Principal
from .tool_router import ToolRouter, RiskLevel, ApprovalRequiredError
from .recall import RecallEngine
from .reflection import ReflectionPipeline, SelfRefine
from .deduplication import Deduplicator
from .consolidation import Consolidator
from .semantic import DeterministicSemanticProvider

class AgentRole(str, enum.Enum):
    ROUTER = "router"
    RETRIEVAL = "retrieval"
    VERIFIER = "verifier"
    CONSOLIDATOR = "consolidator"
    CRITIC = "critic"
    SYNTHESIZER = "synthesizer"

class SubagentSpec:
    """Specifies role, allowed tool actions, model tier, and step limits."""
    def __init__(self, role: AgentRole, allowed_actions: List[str], model_tier: str = "light", max_steps: int = 3):
        self.role = role
        self.allowed_actions = set(allowed_actions)
        self.model_tier = model_tier
        self.max_steps = max_steps

class MultiAgentOrchestrator:
    """Orchestrator-Worker Multi-Agent System.
    Dispatches specialized tasks to bounded worker subagents with strict privilege scoping.
    """

    def __init__(self, memory_controller: MemoryController, tool_router: Optional[ToolRouter] = None):
        self.controller = memory_controller
        self.router = tool_router or ToolRouter(self.controller)
        self.semantic = DeterministicSemanticProvider()
        self.recall_engine = RecallEngine(self.controller, self.semantic)
        self.reflection = ReflectionPipeline(self.controller)
        self.deduplicator = Deduplicator(self.controller, self.semantic, self.router)
        self.consolidator = Consolidator(self.controller, self.router)

        # Worker specifications with least privilege
        self.workers: Dict[AgentRole, SubagentSpec] = {
            AgentRole.ROUTER: SubagentSpec(AgentRole.ROUTER, ["search"], model_tier="light", max_steps=2),
            AgentRole.RETRIEVAL: SubagentSpec(AgentRole.RETRIEVAL, ["search", "read"], model_tier="light", max_steps=3),
            AgentRole.VERIFIER: SubagentSpec(AgentRole.VERIFIER, ["read"], model_tier="light", max_steps=2),
            AgentRole.CONSOLIDATOR: SubagentSpec(AgentRole.CONSOLIDATOR, ["search", "propose", "archive"], model_tier="standard", max_steps=4),
            AgentRole.CRITIC: SubagentSpec(AgentRole.CRITIC, ["read", "propose"], model_tier="standard", max_steps=3),
            AgentRole.SYNTHESIZER: SubagentSpec(AgentRole.SYNTHESIZER, ["read"], model_tier="heavy", max_steps=2),
        }

    def _execute_worker_action(self, role: AgentRole, principal: Principal, action: str, kwargs: Dict[str, Any]) -> Any:
        spec = self.workers.get(role)
        if not spec or action not in spec.allowed_actions:
            raise PermissionError(f"Subagent '{role.value}' is not permitted to perform action '{action}'")
        return self.router.execute(principal, action, kwargs)

    def route_and_dispatch(self, principal: Principal, query: str, context: List[Dict[str, Any]]) -> Dict[str, Any]:
        """Dispatches query across the orchestrator pipeline:
        Router -> Retrieval Worker -> Verifier/Critic Worker -> Synthesis.
        """
        history = []

        # 1. Router / Triage
        lowered = query.lower()
        needs_deep_retrieval = any(k in lowered for k in ["search", "find", "all", "history", "lookup", "detail"])

        # 2. Retrieval Worker
        retrieved_nodes = []
        if needs_deep_retrieval:
            search_pack = self._execute_worker_action(
                AgentRole.RETRIEVAL, principal, "search", {"query": query, "page_size": 5}
            )
            retrieved_nodes = search_pack.get("results", [])
            history.append({"agent": AgentRole.RETRIEVAL.value, "retrieved_count": len(retrieved_nodes)})

        combined_context = list(context) + retrieved_nodes

        # 3. Verifier Worker (checks verification status and flags unverified claims)
        verified_count = 0
        unverified_count = 0
        for node in combined_context:
            if node.get("verification") == "verified":
                verified_count += 1
            else:
                unverified_count += 1
        history.append({
            "agent": AgentRole.VERIFIER.value,
            "verified_nodes": verified_count,
            "unverified_nodes": unverified_count
        })

        # 4. Synthesis
        synthesis_result = {
            "query": query,
            "orchestration_history": history,
            "total_context_used": len(combined_context),
            "status": "completed"
        }
        return synthesis_result

    def run_maintenance_pipeline(self, principal: Principal) -> Dict[str, Any]:
        """Runs the background maintenance pipeline via specialized worker agents."""
        results = {}
        # Deduplication worker
        flagged = self.deduplicator.scan_for_duplicates(principal)
        results["duplicates_flagged"] = len(flagged)

        # Consolidation worker
        consolidated_id = self.consolidator.consolidate_lessons(principal)
        results["consolidated_id"] = consolidated_id

        return results
