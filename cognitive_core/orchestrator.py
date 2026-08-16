from typing import Dict, Any, List, Optional
from dataclasses import dataclass
from enum import Enum
from memory_controller.authorizer import Principal
from memory_controller.controller import MemoryController
from cognitive_core.tool_router import ToolRouter
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
    """Orchestrates specialized subagents with least-privilege tool execution scoping."""

    def __init__(self, controller: MemoryController, router: Optional[ToolRouter] = None):
        self.controller = controller
        self.router = router or ToolRouter(self.controller)
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
        process_task(principal, task) contract. Any failure is caught and
        converted into an observable, attributable, non-fatal record.
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

        # 3. Verifier Worker
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

        verifier_contribution = self._invoke_worker_agent(
            AgentRole.VERIFIER, principal, {"query": query, "context": combined_context}
        )
        history.append(verifier_contribution)

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

    def __init__(self, config_path: str = "compute_nodes.json"):
        self.config_path = config_path
        self.config = self._load_config()
        self.models = self.config.get("default_models", {
            "router": "glm-4.7-flash:latest",
            "coder": "qwen3-coder:30b",
            "memory": "gemma4:26b-64k",
            "critic": "qwen3-coder:30b"
        })

    def _load_config(self) -> Dict[str, Any]:
        import json, os
        if os.path.exists(self.config_path):
            try:
                with open(self.config_path, "r", encoding="utf-8") as f:
                    return json.load(f)
            except Exception:
                pass
        return {}

    def _is_endpoint_alive(self, url: str) -> bool:
        """Verifica rapid daca un endpoint Ollama/Cloudflare este activ si functional."""
        if not url or "placeholder" in url:
            return False
        import urllib.request
        try:
            req = urllib.request.Request(f"{url.rstrip('/')}/", headers={"User-Agent": "VaultDispatcher/1.0"})
            with urllib.request.urlopen(req, timeout=3.0) as res:
                return res.status == 200
        except Exception:
            return False

    def _get_ordered_nodes(self, role: str) -> list[dict]:
        """Returneaza lista de noduri ordonate dupa prioritate pentru rolul specificat."""
        nodes = self.config.get("nodes", {})
        default_model = self.models.get(role, "qwen2.5-coder:14b")
        candidates = []

        for name, cfg in nodes.items():
            if not cfg.get("enabled", True):
                continue
            if role in cfg.get("roles", []):
                model = cfg.get("models", {}).get(role, default_model)
                candidates.append({
                    "name": name,
                    "url": cfg.get("base_url"),
                    "model": model,
                    "priority": cfg.get("priority", 99)
                })

        candidates.sort(key=lambda x: x["priority"])
        return candidates

    def dispatch(self, agent_role: str, system_prompt: str, user_input: str) -> str:
        """Dispatches step to the best available compute node with automatic failover."""
        from langchain_core.messages import SystemMessage, HumanMessage
        from langchain_ollama import ChatOllama
        import sys

        security_guardrails = """
        MANDATORY SECURITY INVARIANTS (P0-P15):
        - You are Principal.AI_AGENT.
        - verification = "unverified" (Strict).
        - lifecycle = "REVIEW" (Strict).
        - provenance = "ai" or "inference".
        """
        full_system_prompt = f"{system_prompt}\n\n{security_guardrails}"
        messages = [
            SystemMessage(content=full_system_prompt),
            HumanMessage(content=user_input)
        ]

        candidates = self._get_ordered_nodes(agent_role)
        
        # Daca nu exista noduri configurate, folosim local
        if not candidates:
            candidates = [{"name": "local", "url": "http://localhost:11434", "model": self.models.get(agent_role, "qwen2.5-coder:3b"), "priority": 99}]

        last_error = None
        for node in candidates:
            name = node["name"]
            url = node["url"]
            model = node["model"]

            # Verificam daca nodul este online
            print(f"[*] Verificare starii nodului [{name.upper()}] ({url})...", file=sys.stderr)
            if not self._is_endpoint_alive(url):
                print(f"[!] Nodul [{name.upper()}] este offline sau inaccesibil. Trecem la urmatorul nod din cluster...", file=sys.stderr)
                continue

            print(f"[+] Conectat cu succes la [{name.upper()}] GPU! Model: {model}", file=sys.stderr)
            try:
                llm = ChatOllama(
                    model=model,
                    temperature=0.0,
                    base_url=url,
                    keep_alive="5m"
                )
                full_text = []
                for chunk in llm.stream(messages):
                    if hasattr(chunk, "content") and chunk.content:
                        full_text.append(chunk.content)
                
                result = "".join(full_text)
                if result.strip():
                    return result
            except Exception as e:
                print(f"[!] Eroare in timpul executiei pe [{name.upper()}]: {e}. Incercam failover...", file=sys.stderr)
                last_error = e

        if last_error:
            raise RuntimeError(f"Toate nodurile GPU din cluster au esuat. Ultimul mesaj de eroare: {last_error}")
        
        return f"[Simulation]: Niciun nod GPU activ gasit."