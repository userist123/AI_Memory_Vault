"""
Milestone 3: Router Agent (Query Analysis, Intent Decomposition, Least-Privilege Scoping).
"""

import time
import re
from typing import Dict, Any, List, Optional
from jarvis.llm.base import BaseLLMProvider, CancellationToken
from jarvis.agents.base import BaseAgent
from jarvis.agents.models import (
    AgentRole,
    SubTaskScope,
    DecomposedSubTask,
    RouterOutput,
)


class RouterAgent(BaseAgent):
    """
    Analyzes incoming multi-intent requests and decomposes them into
    independent atomic sub-tasks. Enforces read-only least-privilege scoping.
    """

    role: AgentRole = AgentRole.ROUTER

    def __init__(
        self,
        storage: Optional[Any] = None,
        llm: Optional[BaseLLMProvider] = None,
    ):
        super().__init__(storage=storage, llm=llm)

    async def decompose(
        self,
        query: str,
        context: Optional[Dict[str, Any]] = None,
        cancellation_token: Optional[CancellationToken] = None,
    ) -> RouterOutput:
        """
        Decomposes a user query into atomic subtasks using high-speed heuristics
        with support for FastMCP IoT, Memory, Status, and Conversational actions.
        """
        t0 = time.time()
        if cancellation_token:
            cancellation_token.raise_if_cancelled()

        cleaned_query = (query or "").strip()
        if not cleaned_query:
            return RouterOutput(
                original_query=query,
                subtasks=[],
                is_composite=False,
                confidence=1.0,
                routing_latency_ms=(time.time() - t0) * 1000.0,
            )

        # Split on conjunctions
        # Regex splits on: and, then, after that, & surrounded by word boundaries/punctuation
        split_pattern = r"(?:\s*,\s*and\s+|\s*,\s*then\s+|\s*,\s*after that\s+|\s+and\s+|\s+then\s+|\s+after that\s+|\s*&\s*)"
        raw_clauses = re.split(split_pattern, cleaned_query, flags=re.IGNORECASE)

        # Filter out empty or meaningless clauses (e.g. repeated conjunctions, punctuation)
        valid_clauses: List[str] = []
        for c in raw_clauses:
            clause = c.strip(" ,.;:!?\n\t")
            # Remove leading/trailing conjunction remnants
            clause = re.sub(r"^(?:and|then|after that|please|also)\s+", "", clause, flags=re.IGNORECASE).strip()
            clause = re.sub(r"\s+(?:and|then|after that|please)$", "", clause, flags=re.IGNORECASE).strip()
            if clause and len(clause) > 1 and not re.match(r"^(?:and|then|or|also|please)$", clause, re.I):
                valid_clauses.append(clause)

        if not valid_clauses:
            return RouterOutput(
                original_query=query,
                subtasks=[],
                is_composite=False,
                confidence=1.0,
                routing_latency_ms=(time.time() - t0) * 1000.0,
            )

        subtasks: List[DecomposedSubTask] = []
        for idx, clause in enumerate(valid_clauses, start=1):
            scope, action, kwargs, priority = self._classify_clause(clause)
            subtasks.append(
                DecomposedSubTask(
                    subtask_id=idx,
                    raw_query=clause,
                    scope=scope,
                    priority=priority,
                    action=action,
                    kwargs=kwargs,
                    description=f"Subtask {idx}: {action} ({scope.value})",
                )
            )

        is_composite = len(subtasks) > 1
        elapsed_ms = (time.time() - t0) * 1000.0

        return RouterOutput(
            original_query=query,
            subtasks=subtasks,
            is_composite=is_composite,
            confidence=0.95 if is_composite else 1.0,
            routing_latency_ms=elapsed_ms,
        )

    def _classify_clause(self, clause: str) -> tuple[SubTaskScope, str, Dict[str, Any], int]:
        """Classify clause into (scope, action, kwargs, priority)."""
        lower = clause.lower()

        # IoT control detection
        if any(kw in lower for kw in ["turn on", "turn off", "switch on", "switch off", "set temperature", "set thermostat", "climate", "thermostat", "dim", "brighten", "lock", "unlock"]):
            action = "iot_control"
            kwargs: Dict[str, Any] = {}
            if "turn on" in lower or "switch on" in lower:
                service = "turn_on"
            elif "turn off" in lower or "switch off" in lower:
                service = "turn_off"
            elif "set temperature" in lower or "set thermostat" in lower or "climate" in lower or "thermostat" in lower or "degrees" in lower:
                service = "set_temperature"
                temp_match = re.search(r"(\d+(?:\.\d+)?)\s*(?:degrees|deg|c|f)?", lower)
                if temp_match:
                    kwargs["temperature"] = float(temp_match.group(1))
            elif "lock" in lower:
                service = "lock"
            elif "unlock" in lower:
                service = "unlock"
            else:
                service = "control"

            # Detect entity/domain
            domain = "light"
            if "thermostat" in lower or "climate" in lower or "temperature" in lower:
                domain = "climate"
            elif "lock" in lower:
                domain = "lock"
            elif "switch" in lower:
                domain = "switch"

            kwargs["domain"] = domain
            kwargs["service"] = service

            # Entity hint
            if "kitchen" in lower:
                kwargs["entity_id"] = f"{domain}.kitchen"
            elif "living room" in lower:
                kwargs["entity_id"] = f"{domain}.living_room"
            elif "bedroom" in lower:
                kwargs["entity_id"] = f"{domain}.bedroom"

            return SubTaskScope.IOT_CONTROL, service, kwargs, 1

        # Memory store detection
        if any(kw in lower for kw in ["remember", "store memory", "save note", "record note", "note that", "write down"]):
            content = re.sub(r"^(?:remember\s+that|remember|store\s+memory|save\s+note|record\s+note|note\s+that|write\s+down)\s*", "", clause, flags=re.IGNORECASE).strip()
            return SubTaskScope.MEMORY_STORE, "store_memory", {"content": content}, 2

        # System status / diagnostics detection
        if any(kw in lower for kw in ["system status", "status", "diagnostics", "health check", "cpu usage", "system health"]):
            return SubTaskScope.SYSTEM_STATUS, "check_system_status", {}, 3

        # Memory retrieval / Knowledge Query detection
        if any(kw in lower for kw in ["what is", "who is", "where is", "tell me about", "search memory", "check memory", "check if", "retrieve", "recall", "find note", "architecture", "how does"]):
            return SubTaskScope.QUERY, "query_memory", {"query": clause}, 2

        # Conversational / Reasoning fallback
        return SubTaskScope.CONVERSATION, "respond_conversation", {"text": clause}, 2

    async def execute(
        self,
        payload: Dict[str, Any],
        cancellation_token: Optional[CancellationToken] = None,
    ) -> Dict[str, Any]:
        """
        Execute router decomposition on payload.
        Returns dictionary containing both string subtasks and structured subtask objects.
        """
        query = payload.get("query", "")
        context = payload.get("context")
        output = await self.decompose(query, context=context, cancellation_token=cancellation_token)

        subtask_strings = [s.raw_query for s in output.subtasks]
        plan_hints = [{"scope": s.scope.value, "action": s.action, "kwargs": s.kwargs} for s in output.subtasks]

        return {
            "original_query": output.original_query,
            "subtasks": subtask_strings,
            "subtask_objects": [s.model_dump() for s in output.subtasks],
            "count": len(output.subtasks),
            "is_composite": output.is_composite,
            "confidence": output.confidence,
            "plan_hints": plan_hints,
            "routing_latency_ms": output.routing_latency_ms,
        }
