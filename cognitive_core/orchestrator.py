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
from .agents.router_agent import RouterAgent
from .agents.retrieval_agent import RetrievalAgent
from .agents.critic_agent import CriticAgent
from .agents.verifier_agent import VerifierAgent
from .agents.consolidator_agent import ConsolidatorAgent

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


class WorkerExecutionError(RuntimeError):
    """Raised when a specialized worker agent fails during pipeline execution.

    Reserved for future strict-mode callers that need a raised exception
    instead of an observability record. Not currently raised by
    _invoke_worker_agent (which intentionally degrades failures into
    non-fatal history entries -- see Phase 7/8 failure-safety rationale).
    Kept as part of the public contract; not dead code to be removed
    without a deliberate follow-up decision, since removing it would be
    a breaking change for any external caller that already imports it.
    """
    def __init__(self, role: AgentRole, original_exception: Exception):
        self.role = role
        self.original_exception = original_exception
        super().__init__(f"Worker '{role.value}' failed: {original_exception}")


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

        # Specialized worker agents from cognitive_core/agents/*, instantiated
        # against the SAME controller and ToolRouter as the orchestrator, so
        # every memory access they perform continues to go through the
        # canonical MemoryController authorization boundary. self.workers/
        # SubagentSpec is kept for backward compatibility with existing
        # callers/tests that inspect orchestrator.workers and for
        # _execute_worker_action's generic ToolRouter-action gating, which
        # remains useful for actions that have no dedicated specialized
        # worker (e.g. plain archive/propose calls outside a worker's own
        # process_task contract).
        self.worker_agents: Dict[AgentRole, Any] = {
            AgentRole.ROUTER: RouterAgent(self.controller, self.router),
            AgentRole.RETRIEVAL: RetrievalAgent(self.controller, self.router),
            AgentRole.CRITIC: CriticAgent(self.controller, self.router),
            AgentRole.VERIFIER: VerifierAgent(self.controller, self.router),
            AgentRole.CONSOLIDATOR: ConsolidatorAgent(self.controller, self.router),
        }

    def _execute_worker_action(self, role: AgentRole, principal: Principal, action: str, kwargs: Dict[str, Any]) -> Any:
        spec = self.workers.get(role)
        if not spec or action not in spec.allowed_actions:
            raise PermissionError(f"Subagent '{role.value}' is not permitted to perform action '{action}'")
        return self.router.execute(principal, action, kwargs)

    def _invoke_worker_agent(self, role: AgentRole, principal: Principal, task: Dict[str, Any]) -> Dict[str, Any]:
        """Invokes the real specialized worker agent for `role` via its public
        process_task(principal, task) contract. Any failure is caught and
        converted into an observable, attributable, non-fatal record: it is
        appended to the caller-visible history as {"executed": False, ...}
        rather than raised past the pipeline or allowed to produce a partial
        write. The worker's own permitted_actions gate (enforced inside
        BaseWorkerAgent.execute_action) is untouched by this wrapper -- it
        adds no new authority, it only surfaces the worker's real output.
        """
        agent = self.worker_agents.get(role)
        if agent is None:
            return {"agent": role.value, "executed": False, "error": f"No worker agent registered for role '{role.value}'"}
        try:
            result = agent.process_task(principal, task)
            return {"agent": role.value, "executed": True, "result": result}
        except Exception as e:
            return {"agent": role.value, "executed": False, "error": str(e)}

    def route_and_dispatch(self, principal: Principal, query: str, context: List[Dict[str, Any]]) -> Dict[str, Any]:
        """Dispatches query across the orchestrator pipeline:
        Router -> Retrieval Worker -> Verifier/Critic Worker -> Synthesis.
        """
        history = []

        # 1. Router / Triage
        lowered = query.lower()
        needs_deep_retrieval = any(k in lowered for k in ["search", "find", "all", "history", "lookup", "detail"])

        # 1b. Real RouterAgent invocation (additive, observability-only for
        # this phase; does not yet replace the `needs_deep_retrieval`
        # heuristic that drives control flow below, per the approved
        # test-gated migration plan).
        router_contribution = self._invoke_worker_agent(
            AgentRole.ROUTER, principal, {"query": query, "context": context}
        )
        history.append(router_contribution)

        # 2. Retrieval Worker -- CANONICAL PATH.
        # Previously this step called BOTH _execute_worker_action(RETRIEVAL,
        # "search", ...) (generic ToolRouter.search) AND
        # RetrievalAgent.process_task() (hybrid activation+recall scoring),
        # performing the retrieval operation twice per dispatch. RetrievalAgent
        # is strictly more capable (spreading activation + RecallEngine
        # lineage-aware scoring vs a flat ToolRouter.search call) and is now
        # the SOLE retrieval path. _execute_worker_action itself is preserved
        # (not deleted): it remains the generic SubagentSpec-gated mechanism
        # for any future worker action that has no dedicated specialized
        # agent method.
        retrieved_nodes = []
        if needs_deep_retrieval:
            retrieval_contribution = self._invoke_worker_agent(
                AgentRole.RETRIEVAL, principal, {"query": query, "context": context}
            )
            history.append(retrieval_contribution)
            if retrieval_contribution.get("executed"):
                retrieved_nodes = retrieval_contribution.get("result", {}).get("results", [])
        else:
            retrieval_contribution = None

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

        # 3b. Real VerifierAgent invocation (additive; VerifierAgent is
        # read-only per its permitted_actions=["read"], so it cannot itself
        # escalate any node's trust state -- it may only report findings).
        verifier_contribution = self._invoke_worker_agent(
            AgentRole.VERIFIER, principal, {"query": query, "context": combined_context}
        )
        history.append(verifier_contribution)

        # 3c. Real CriticAgent invocation (additive; evaluates/critiques
        # candidate results, cannot mutate verification/lifecycle itself).
        critic_contribution = self._invoke_worker_agent(
            AgentRole.CRITIC, principal, {"query": query, "context": combined_context}
        )
        history.append(critic_contribution)

        # 4. Synthesis
        synthesis_result = {
            "query": query,
            "orchestration_history": history,
            "total_context_used": len(combined_context),
            "status": "completed"
        }
        return synthesis_result

    def run_maintenance_pipeline(self, principal: Principal) -> Dict[str, Any]:
        """Runs the background maintenance pipeline via the ConsolidatorAgent
        worker, which internally wraps the same legacy Deduplicator/
        Consolidator domain services (scan_for_duplicates + consolidate_lessons)
        this method called directly before. The result shape
        ({"duplicates_flagged": int, "consolidated_id": Optional[str]}) is
        byte-identical to the previous implementation, verified against
        ConsolidatorAgent.process_task()'s confirmed source:
            if task_type in ["dedup", "all"]: ... duplicates_flagged
            if task_type in ["consolidate", "all"]: ... consolidated_id
        This migration does not change authorization, provenance, lifecycle,
        verification, audit, or atomicity semantics: both the legacy direct
        calls and the ConsolidatorAgent-mediated calls invoke the exact same
        Deduplicator.scan_for_duplicates / Consolidator.consolidate_lessons
        methods against the same MemoryController/ToolRouter.
        """
        consolidator_agent = self.worker_agents[AgentRole.CONSOLIDATOR]
        outcome = consolidator_agent.process_task(principal, {"type": "all"})
        return {
            "duplicates_flagged": outcome.get("duplicates_flagged", 0),
            "consolidated_id": outcome.get("consolidated_id"),
        }
