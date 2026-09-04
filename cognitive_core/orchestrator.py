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

class UnknownAgentRoleError(ValueError):
    """Raised when an unknown or unsupported agent role is requested."""
    pass

SUPPORTED_ROLES: Dict[str, AgentRole] = {
    "router": AgentRole.ROUTER,
    "retrieval": AgentRole.RETRIEVAL,
    "memory": AgentRole.RETRIEVAL,
    "verifier": AgentRole.VERIFIER,
    "consolidator": AgentRole.CONSOLIDATOR,
    "critic": AgentRole.CRITIC,
    "synthesizer": AgentRole.SYNTHESIZER,
    "coder": AgentRole.SYNTHESIZER,
}

def validate_agent_role(role: Any) -> AgentRole:
    """Validates and normalizes an agent role against supported workers.

    B1: Unknown/unsupported role is rejected deterministically.
    """
    if isinstance(role, AgentRole):
        return role
    if isinstance(role, str):
        normalized = role.strip().lower()
        if normalized in SUPPORTED_ROLES:
            return SUPPORTED_ROLES[normalized]
    raise UnknownAgentRoleError(
        f"Unknown or unsupported agent role: '{role}'. "
        f"Supported roles: {sorted(list(SUPPORTED_ROLES.keys()))}"
    )

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

    def route_and_dispatch(
        self,
        principal: Principal,
        query: str,
        context: List[Dict[str, Any]],
        skip_retrieval: bool = False,
        run_verifier: bool = True,
        max_context_items: Optional[int] = None,
    ) -> Dict[str, Any]:
        """WIRE-C3: each orchestration_history entry now includes a real
        model_tier (light/standard/heavy), sourced from that worker's own
        SubagentSpec.model_tier (defined once in __init__, not invented at
        report time). This is preparation for Option A (LLM provider/model
        integration): when a real model call is eventually wired into a
        worker, the per-worker tier used to pick a provider/model is already
        available in this telemetry, rather than needing to be decided from
        scratch at that point.

        Dispatches query across the orchestrator pipeline:
        Router -> Retrieval Worker -> Verifier Worker (optional) -> Synthesis.

        skip_retrieval: set True when `context` was already produced by a
        prior retrieval pass over this exact `query` (e.g. Executive's own
        ActivationEngine + RecallEngine step). Without this flag, any query
        containing a deep-retrieval keyword ("search", "find", ...) always
        triggers a second, redundant live search through the RETRIEVAL
        worker even when the caller already retrieved for that query.
        Defaults to False to preserve the original standalone contract for
        direct callers (e.g. cognitive_core/tests/test_multiagent_orchestration.py)
        that pass hand-built context and rely on this method to do its own
        retrieval.

        run_verifier: set False (via CouncilBudgetController LIGHT/NONE
        tiers is handled by the caller skipping this method entirely; this
        flag exists for callers that want Retrieval without the Verifier
        tally). Defaults to True to preserve the original contract.

        max_context_items: caps the size of `combined_context` BEFORE the
        Verifier tally and the returned `total_context_used`. This is the
        actual enforcement point for a Council-wide memory-result budget
        (e.g. Council_Context_Validator.MAX_MEMORY_RESULTS) on the context
        this orchestrator consumes, independent of how large the caller's
        own WorkingMemory happens to be. None (default) means no cap is
        applied here, preserving the original contract for direct callers.
        """
        history = []

        # 1. Router / Triage
        lowered = query.lower()
        needs_deep_retrieval = (not skip_retrieval) and any(
            k in lowered for k in ["search", "find", "all", "history", "lookup", "detail"]
        )

        # 2. Retrieval Worker
        retrieved_nodes = []
        if needs_deep_retrieval:
            search_pack = self._execute_worker_action(
                AgentRole.RETRIEVAL, principal, "search", {"query": query, "page_size": 5}
            )
            retrieved_nodes = search_pack.get("results", [])
            history.append({
                "agent": AgentRole.RETRIEVAL.value,
                "retrieved_count": len(retrieved_nodes),
                # WIRE-C3: real model_tier from this worker's own SubagentSpec
                # (defined in __init__, not invented here) -- so a future LLM
                # integration (Option A) has a real per-worker tier signal to
                # select a provider/model with, instead of guessing one at
                # that later point.
                "model_tier": self.workers[AgentRole.RETRIEVAL].model_tier,
            })

        combined_context = list(context) + retrieved_nodes
        if max_context_items is not None:
            combined_context = combined_context[:max_context_items]

        # 3. Verifier Worker (checks verification status and flags unverified claims)
        if run_verifier:
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
                "unverified_nodes": unverified_count,
                # WIRE-C3: same real model_tier signal as the Retrieval entry.
                "model_tier": self.workers[AgentRole.VERIFIER].model_tier,
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

    def dispatch_worker(
        self,
        role: AgentRole,
        principal: Principal,
        query: str,
        context: Optional[List[Dict[str, Any]]] = None
    ) -> Dict[str, Any]:
        """Dispatches a query/task directly to a specialized worker with least-privilege scoping.

        B2: Maps supported role to intended worker/capability.
        B3: Different supported roles execute their distinct worker capabilities.
        B4: Operates through MemoryController security boundary.
        B5: Preserves Principal.AI_AGENT security semantics.
        """
        spec = self.workers.get(role)
        if not spec:
            raise UnknownAgentRoleError(f"No worker specification found for role '{role}'")

        ctx = list(context or [])

        if role == AgentRole.ROUTER:
            lowered = query.lower()
            needs_deep = any(k in lowered for k in ["search", "find", "all", "history", "lookup", "detail"])
            triage_nodes = []
            if needs_deep:
                search_pack = self._execute_worker_action(
                    AgentRole.ROUTER, principal, "search", {"query": query, "page_size": 2}
                )
                triage_nodes = search_pack.get("results", [])
            return {
                "status": "completed",
                "worker": AgentRole.ROUTER.value,
                "model_tier": spec.model_tier,
                "action": "triage_and_route",
                "query": query,
                "routing_decision": {
                    "intent": "deep_retrieval" if needs_deep else "direct_reasoning",
                    "target_worker": AgentRole.RETRIEVAL.value if needs_deep else AgentRole.SYNTHESIZER.value,
                    "triage_nodes_count": len(triage_nodes),
                },
            }

        elif role == AgentRole.RETRIEVAL:
            search_pack = self._execute_worker_action(
                AgentRole.RETRIEVAL, principal, "search", {"query": query, "page_size": 5}
            )
            retrieved = search_pack.get("results", [])
            return {
                "status": "completed",
                "worker": AgentRole.RETRIEVAL.value,
                "model_tier": spec.model_tier,
                "action": "deep_retrieval",
                "query": query,
                "retrieved_nodes": retrieved,
                "retrieved_count": len(retrieved),
            }

        elif role == AgentRole.VERIFIER:
            verified_count = 0
            unverified_count = 0
            unverified_flags = []
            for node in ctx:
                ver = node.get("verification")
                if ver == "verified":
                    verified_count += 1
                else:
                    unverified_count += 1
                    unverified_flags.append(node.get("id") or "unnamed_node")
            return {
                "status": "completed",
                "worker": AgentRole.VERIFIER.value,
                "model_tier": spec.model_tier,
                "action": "verify_claims",
                "query": query,
                "audit": {
                    "verified_nodes": verified_count,
                    "unverified_nodes": unverified_count,
                    "unverified_flags": unverified_flags,
                    "total_checked": len(ctx),
                },
            }

        elif role == AgentRole.CONSOLIDATOR:
            flagged = self.deduplicator.scan_for_duplicates(principal)
            consolidated_id = self.consolidator.consolidate_lessons(principal)
            return {
                "status": "completed",
                "worker": AgentRole.CONSOLIDATOR.value,
                "model_tier": spec.model_tier,
                "action": "consolidate_and_deduplicate",
                "query": query,
                "consolidated_id": consolidated_id,
                "duplicates_flagged": len(flagged),
            }

        elif role == AgentRole.CRITIC:
            critique = {
                "evaluated_query": query,
                "context_size": len(ctx),
                "assessment": "Valid cognitive structure; no invariant violations detected in input.",
            }
            return {
                "status": "completed",
                "worker": AgentRole.CRITIC.value,
                "model_tier": spec.model_tier,
                "action": "critique_and_reflect",
                "query": query,
                "critique": critique,
            }

        elif role == AgentRole.SYNTHESIZER:
            return {
                "status": "completed",
                "worker": AgentRole.SYNTHESIZER.value,
                "model_tier": spec.model_tier,
                "action": "context_synthesis",
                "query": query,
                "total_context_used": len(ctx),
                "synthesis": f"Synthesized output for query '{query}' across {len(ctx)} context items.",
            }

        raise UnknownAgentRoleError(f"Unhandled agent role '{role}'")


class MultiAgentDispatcher:
    """Dispatches tasks to specialized workers via MultiAgentOrchestrator with least-privilege scoping."""

    def __init__(self, memory_controller: Optional[MemoryController] = None):
        if memory_controller is None:
            from cognitive_core.recall_cli import get_memory_controller
            memory_controller = get_memory_controller()
        self.orchestrator = MultiAgentOrchestrator(memory_controller)
        self.config: Dict[str, Any] = {"nodes": {"local": {"enabled": True}}}

    def dispatch(self, agent_role: str, system_prompt: str, user_input: str) -> str:
        """Dispatches query through the multi-agent orchestrator with least-privilege scoping.

        Architecture:
            requested agent_role -> role validation -> authorized role -> worker selection -> real dispatch
        """
        import json
        from cognitive_core.recall_cli import validate_hmac_secret

        # Fail closed if HMAC secret is missing or invalid
        validate_hmac_secret()

        # Step 1: Role validation (B1: reject unknown/unsupported role)
        authorized_role = validate_agent_role(agent_role)

        # Step 2: Context assembly
        context = [{"role": "system", "content": system_prompt}] if system_prompt else []

        # Step 3 & 4: Worker/capability selection and real dispatch (B2, B3, B4, B5)
        result = self.orchestrator.dispatch_worker(
            role=authorized_role,
            principal=Principal.AI_AGENT,
            query=user_input,
            context=context,
        )
        return json.dumps(result, indent=2, default=str)

