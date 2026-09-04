"""
Cognitive Executive Daemon coordinating the OODA loop with atomic checkpointing and error recovery.
"""

import os
from pathlib import Path
from typing import Optional, Dict, Any, List, Union, Callable
import asyncio
from jarvis.llm.base import BaseLLMProvider
from jarvis.memory.sqlite_engine import SQLiteStorageEngine
from jarvis.memory.invariants import Principal
from jarvis.core.models import (
    PerceptionEvent,
    WorkingMemory,
    ActivePlan,
    OODACycleResult,
)
from jarvis.core.ooda import OODACognitiveEngine


class CognitiveExecutive:
    """Cognitive Daemon coordinating OODA execution, checkpoints, and recovery."""

    def __init__(
        self,
        llm_provider: BaseLLMProvider,
        storage_engine: SQLiteStorageEngine,
        checkpoint_dir: Union[str, Path] = ".checkpoints",
        working_memory_capacity: int = 10,
        max_retries: int = 2,
    ):
        self.checkpoint_dir = Path(checkpoint_dir)
        os.makedirs(self.checkpoint_dir, exist_ok=True)
        self.max_retries = max_retries

        self.storage = storage_engine
        self.engine = OODACognitiveEngine(
            llm_provider=llm_provider,
            storage_engine=storage_engine,
            working_memory_capacity=working_memory_capacity,
        )
        self.working_memory: WorkingMemory = self.engine.working_memory
        self.active_plan: Optional[ActivePlan] = None
        self._state_callbacks: List[Callable[[dict], None]] = []
    def save_checkpoint(self) -> None:
        """Atomic checkpointing of working memory and active plan."""
        wm_file = self.checkpoint_dir / "wm.json"
        self.working_memory.save_state(wm_file)

        if self.active_plan:
            plan_file = self.checkpoint_dir / "plan.json"
            self.active_plan.save_state(plan_file)

    def load_checkpoint(self) -> bool:
        """Recover state from previous checkpoint after restart or crash."""
        loaded = False
        wm_file = self.checkpoint_dir / "wm.json"
        if wm_file.exists():
            try:
                self.working_memory.load_state(wm_file)
                loaded = True
            except Exception:
                pass

        plan_file = self.checkpoint_dir / "plan.json"
        if plan_file.exists():
            try:
                self.active_plan = ActivePlan.load_state(plan_file)
                loaded = True
            except Exception:
                pass

        return loaded

    async def process_utterance(
        self,
        text: str,
        source: str = "voice",
        principal: Principal = Principal.AI_AGENT,
    ) -> OODACycleResult:
        """
        Main cognitive dispatch entry point for voice transcription or text query.
        Executes complete OODA cycle with automatic checkpointing and co-activation synapses.
        """
        perception = PerceptionEvent(
            channel=source,
            raw_data=text,
            metadata={"principal": principal.value},
        )

        result = await self.engine.execute_cycle(
            perception=perception,
            principal=principal,
            auto_checkpoint_callback=self.save_checkpoint,
        )

        self.active_plan = result.active_plan
        self.save_checkpoint()

        # Fire dynamic synapses between co-activated context nodes
        if len(result.context_used) >= 2:
            self._fire_synapses(result.context_used, principal)
        # Emit state to registered callbacks
        state_snapshot = {
            "active_plan_id": self.active_plan.id if self.active_plan else None,
            "memory_len": len(self.working_memory.entries),
            "principal": principal.value,
        }
        await self._emit_state(state_snapshot)
        return result

    def _fire_synapses(
        self, context: List[Dict[str, Any]], principal: Principal = Principal.AI_AGENT
    ) -> None:
        """Create reciprocal relations between co-activated working memory nodes."""
        for i in range(min(3, len(context) - 1)):
            node_a = context[i]
            node_b = context[i + 1]
            id_a = node_a.get("id")
            id_b = node_b.get("id")
            if not id_a or not id_b or id_a == id_b:
                continue

            # Update node_a relations if not already present
            try:
                rels = node_a.get("relations", [])
                if not any(r.get("target_id") == id_b for r in rels if isinstance(r, dict)):
                    rels.append({
                        "relation": "co_activated",
                        "target": node_b.get("type", "knowledge"),
                        "target_id": id_b,
                    })
                    self.storage.update(principal, id_a, {"relations": rels})
            except Exception:
                pass
    def register_state_callback(self, callback: Callable[[dict], None]) -> None:
        """Register a callback to receive executive state updates.

        The callback may be a regular function or an async coroutine.
        """
        self._state_callbacks.append(callback)

    async def _emit_state(self, state: dict) -> None:
        """Invoke all registered callbacks with the given state.

        Callbacks that return a coroutine are awaited. Errors in callbacks are ignored
        to prevent disruption of the executive loop.
        """
        for cb in list(self._state_callbacks):
            try:
                result = cb(state)
                if asyncio.iscoroutine(result):
                    await result
            except Exception:
                pass

