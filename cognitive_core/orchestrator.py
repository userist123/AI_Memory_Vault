from typing import Dict, Any, List, Optional
from dataclasses import dataclass
from enum import Enum
from memory_controller.authorizer import Principal
from memory_controller.controller import MemoryController
from cognitive_core.tool_router import ToolRouter
from cognitive_core.global_workspace import GlobalWorkspace, WorkspaceProposal
from cognitive_core.agents import (
    BaseWorkerAgent,
    RouterAgent,
    RetrievalAgent,
    VerifierAgent,
    ConsolidatorAgent,
    CriticAgent
)

class AgentRole(str, Enum):
    ROUTER = "router"
    RETRIEVAL = "retrieval"
    VERIFIER = "verifier"
    CONSOLIDATOR = "consolidator"
    CRITIC = "critic"
    SYNTHESIZER = "synthesizer"

@dataclass
class SubagentSpec:
    role: AgentRole
    allowed_actions: List[str]
    max_steps: int = 3

class WorkerExecutionError(Exception):
    """Raised when a worker execution fails during orchestration."""
    pass

class MultiAgentOrchestrator:
    """Orchestrates specialized subagents with least-privilege tool execution scoping
    and Global Workspace competition broadcasting.
    """

    def __init__(self, controller: MemoryController, router: Optional[ToolRouter] = None, global_workspace: Optional[GlobalWorkspace] = None):
        self.controller = controller
        self.router = router or ToolRouter(self.controller)
        self.global_workspace = global_workspace or GlobalWorkspace()
        
        self.workers: Dict[AgentRole, SubagentSpec] = {
            AgentRole.ROUTER: SubagentSpec(AgentRole.ROUTER, ["search", "read"], max_steps=2),
            AgentRole.RETRIEVAL: SubagentSpec(AgentRole.RETRIEVAL, ["search", "read"], max_steps=3),
            AgentRole.VERIFIER: SubagentSpec(AgentRole.VERIFIER, ["read"], max_steps=2),
            AgentRole.CONSOLIDATOR: SubagentSpec(AgentRole.CONSOLIDATOR, ["search", "read", "propose", "archive"], max_steps=4),
            AgentRole.CRITIC: SubagentSpec(AgentRole.CRITIC, ["read", "propose"], max_steps=3),
            AgentRole.SYNTHESIZER: SubagentSpec(AgentRole.SYNTHESIZER, ["read"], max_steps=2),
        }
        self.worker_agents: Dict[AgentRole, BaseWorkerAgent] = {
            AgentRole.ROUTER: RouterAgent(self.controller, self.router),
            AgentRole.RETRIEVAL: RetrievalAgent(self.controller, self.router),
            AgentRole.VERIFIER: VerifierAgent(self.controller, self.router),
            AgentRole.CONSOLIDATOR: ConsolidatorAgent(self.controller, self.router),
            AgentRole.CRITIC: CriticAgent(self.controller, self.router)
        }

    def _execute_worker_action(self, role: AgentRole, principal: Principal, action: str, kwargs: Dict[str, Any]) -> Any:
        spec = self.workers.get(role)
        if not spec or action not in spec.allowed_actions:
            raise PermissionError(f"Subagent '{role.value}' is not permitted to perform action '{action}'")
        return self.router.execute(principal, action, kwargs)

    def _invoke_worker_agent(self, role: AgentRole, principal: Principal, task: Dict[str, Any]) -> Dict[str, Any]:
        """Invokes the real specialized worker agent for `role` via its public
        process_task(principal, task) contract. Submits workspace proposal to GlobalWorkspace.
        """
        agent = self.worker_agents.get(role)
        if agent is None:
            return {"agent": role.value, "executed": False, "error": f"No worker agent registered for role '{role.value}'"}
        try:
            result = agent.process_task(principal, task)
            
            # Submit proposal to GlobalWorkspace for competition
            coherence = 0.8 if result else 0.3
            proposal = WorkspaceProposal(
                agent_id=role.value,
                content=result,
                coherence_score=coherence,
                action_type=role.value
            )
            self.global_workspace.submit_proposal(proposal)
            
            return {"agent": role.value, "executed": True, "result": result}
        except Exception as e:
            return {"agent": role.value, "executed": False, "error": str(e)}

    def route_and_dispatch(self, principal: Principal, query: str, context: List[Dict[str, Any]]) -> Dict[str, Any]:
        """Dispatches query across the orchestrator pipeline:
        Router -> Retrieval Worker -> Verifier/Critic Worker -> Global Workspace Competition -> Synthesis.
        """
        history = []

        # 1. Router / Triage
        lowered = query.lower()
        needs_deep_retrieval = any(k in lowered for k in ["search", "find", "all", "history", "lookup", "detail"])

        router_contribution = self._invoke_worker_agent(
            AgentRole.ROUTER, principal, {"query": query, "context": context}
        )
        history.append(router_contribution)

        # 2. Retrieval Worker
        retrieved_nodes = []
        if needs_deep_retrieval:
            retrieval_contribution = self._invoke_worker_agent(
                AgentRole.RETRIEVAL, principal, {"query": query, "context": context}
            )
            history.append(retrieval_contribution)
            if retrieval_contribution.get("executed"):
                retrieved_nodes = retrieval_contribution.get("result", {}).get("results", [])

        combined_context = list(context) + retrieved_nodes

        # 3. Verifier Worker & Critic Worker
        verified_count = sum(1 for n in combined_context if n.get("verification") == "verified")
        unverified_count = len(combined_context) - verified_count
        history.append({
            "agent": AgentRole.VERIFIER.value,
            "verified_nodes": verified_count,
            "unverified_nodes": unverified_count
        })

        verifier_contribution = self._invoke_worker_agent(
            AgentRole.VERIFIER, principal, {"query": query, "context": combined_context}
        )
        history.append(verifier_contribution)

        critic_contribution = self._invoke_worker_agent(
            AgentRole.CRITIC, principal, {"query": query, "context": combined_context}
        )
        history.append(critic_contribution)

        # 4. Global Workspace Competition & Broadcast
        broadcast_result = self.global_workspace.compete_and_broadcast()

        # 5. Synthesis
        synthesis_result = {
            "query": query,
            "orchestration_history": history,
            "total_context_used": len(combined_context),
            "global_broadcast": broadcast_result,
            "status": "completed"
        }
        return synthesis_result

    def run_maintenance_pipeline(self, principal: Principal) -> Dict[str, Any]:
        """Runs the background maintenance pipeline via the ConsolidatorAgent."""
        consolidator_agent = self.worker_agents[AgentRole.CONSOLIDATOR]
        outcome = consolidator_agent.process_task(principal, {"type": "all"})
        res = outcome.get("results", {})
        return {
            "duplicates_flagged": res.get("duplicates_flagged", 0),
            "consolidated_id": res.get("consolidated_id"),
        }


class MultiAgentDispatcher:
    """Local & Distributed LLM dispatcher integrating Ollama models across
    local daemons, Google Colab, and Kaggle GPU nodes.
    """
    pass