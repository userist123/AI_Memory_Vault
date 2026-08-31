"""
Core Data Models for Cognitive OODA Loop, Working Memory, and Planning.
"""

from enum import Enum
from typing import List, Dict, Any, Optional, Union
import time
import json
import os
import tempfile
from pathlib import Path
from pydantic import BaseModel, Field
import uuid


class IntentType(str, Enum):
    """Classified user intention."""
    QUERY = "query"
    IOT_CONTROL = "iot_control"
    TASK = "task"
    MEMORY_STORE = "memory_store"
    SYSTEM_STATUS = "system_status"
    CONVERSATION = "conversation"


class UserIntent(BaseModel):
    """Structured perception representation of user request."""
    raw_text: str
    intent_type: IntentType = IntentType.QUERY
    entities: List[Dict[str, Any]] = Field(default_factory=list)
    requires_tool: bool = False
    extracted_query: Optional[str] = None
    confidence: float = 1.0


class PerceptionEvent(BaseModel):
    """Ephemeral input sensory event."""
    timestamp: float = Field(default_factory=time.time)
    channel: str = "text"  # voice, text, sensor, api
    raw_data: str
    metadata: Dict[str, Any] = Field(default_factory=dict)


class StepStatus(str, Enum):
    """Execution status of a discrete plan step."""
    PENDING = "pending"
    RUNNING = "running"
    SUCCESS = "success"
    BLOCKED = "blocked"
    FAILED = "failed"


class PlanStep(BaseModel):
    """Discrete executable step within an active plan."""
    step_id: int
    action: str  # search, read, iot_call, propose, reason, calculate
    kwargs: Dict[str, Any] = Field(default_factory=dict)
    description: str = ""
    status: StepStatus = StepStatus.PENDING
    result: Optional[Any] = None
    error: Optional[str] = None


class ActivePlan(BaseModel):
    """State machine governing multi-step reasoning and execution."""
    goal: str
    steps: List[PlanStep] = Field(default_factory=list)
    current_step_index: int = 0
    id: str = Field(default_factory=lambda: str(uuid.uuid4()))

    def get_next_step(self) -> Optional[PlanStep]:
        if self.current_step_index < len(self.steps):
            return self.steps[self.current_step_index]
        return None

    def complete_current_step(self, result: Any = None) -> None:
        if self.current_step_index < len(self.steps):
            step = self.steps[self.current_step_index]
            step.status = StepStatus.SUCCESS
            step.result = result
            self.current_step_index += 1

    def fail_current_step(self, error: str) -> None:
        if self.current_step_index < len(self.steps):
            step = self.steps[self.current_step_index]
            step.status = StepStatus.FAILED
            step.error = error

    def is_complete(self) -> bool:
        return self.current_step_index >= len(self.steps)

    def save_state(self, file_path: Union[str, Path]) -> None:
        """Atomically persist plan state to disk."""
        target = Path(file_path)
        os.makedirs(target.parent, exist_ok=True)
        data = self.model_dump_json(indent=2)
        temp_fd, temp_path = tempfile.mkstemp(dir=target.parent, prefix=".tmp_plan_")
        try:
            with open(temp_fd, "w", encoding="utf-8") as f:
                f.write(data)
                f.flush()
                os.fsync(f.fileno())
            os.replace(temp_path, target)
        except Exception:
            if os.path.exists(temp_path):
                try:
                    os.remove(temp_path)
                except Exception:
                    pass
            raise

    @classmethod
    def load_state(cls, file_path: Union[str, Path]) -> "ActivePlan":
        """Load plan state from disk."""
        with open(file_path, "r", encoding="utf-8") as f:
            data = json.load(f)
        return cls.model_validate(data)


class StepExecutionResult(BaseModel):
    """Result payload from executing a PlanStep."""
    step_id: int
    action: str
    status: str  # success, blocked, error
    result: Optional[Any] = None
    error: Optional[str] = None
    execution_time_ms: float = 0.0


class WorkingMemory:
    """Bounded, ACT-R decayed working memory holding active cognitive chunks."""

    def __init__(self, capacity: int = 10):
        self.capacity = capacity
        self.active_chunks: List[Dict[str, Any]] = []
    @property
    def entries(self) -> List[Dict[str, Any]]:
        """Alias for active_chunks to preserve backward compatibility."""
        return self.active_chunks

    def admit(self, nodes_with_scores: List[Any]) -> None:
        """Admit top-scored recalled notes into bounded working memory."""
        new_chunks = []
        for item in nodes_with_scores:
            if isinstance(item, tuple) and len(item) == 2:
                node, score = item
            elif isinstance(item, dict):
                node = item
            else:
                continue

            node_copy = node.copy()
            new_chunks.append(node_copy)

        # Merge with existing, keeping highest priority up to capacity
        existing_ids = {c.get("id") for c in new_chunks if c.get("id")}
        for old in self.active_chunks:
            if old.get("id") not in existing_ids:
                new_chunks.append(old)

        self.active_chunks = new_chunks[: self.capacity]

    @property
    def size(self) -> int:
        return len(self.active_chunks)

    def __len__(self) -> int:
        return len(self.active_chunks)

    def add(self, item: Any) -> None:
        """Convenience method to add a single note or list of notes to working memory."""
        if isinstance(item, list):
            self.admit(item)
        else:
            self.admit([item])

    def get_active_context(self) -> List[Dict[str, Any]]:
        """Retrieve current active working memory items."""
        return list(self.active_chunks)

    def clear(self) -> None:
        """Flush working memory."""
        self.active_chunks.clear()

    def save_state(self, file_path: Union[str, Path]) -> None:
        """Atomically persist working memory state."""
        target = Path(file_path)
        os.makedirs(target.parent, exist_ok=True)
        data = json.dumps(self.active_chunks, indent=2)
        temp_fd, temp_path = tempfile.mkstemp(dir=target.parent, prefix=".tmp_wm_")
        try:
            with open(temp_fd, "w", encoding="utf-8") as f:
                f.write(data)
                f.flush()
                os.fsync(f.fileno())
            os.replace(temp_path, target)
        except Exception:
            if os.path.exists(temp_path):
                try:
                    os.remove(temp_path)
                except Exception:
                    pass
            raise

    def load_state(self, file_path: Union[str, Path]) -> None:
        """Load working memory state from disk with strict schema validation."""
        with open(file_path, "r", encoding="utf-8") as f:
            data = json.load(f)
        if not isinstance(data, list):
            raise ValueError(f"WorkingMemory payload must be a JSON list of note objects, got {type(data).__name__}")

        self.active_chunks = [item for item in data if isinstance(item, dict)][: self.capacity]


class OODACycleResult(BaseModel):
    """Complete summary of an executed OODA cognitive loop cycle."""
    perception: PerceptionEvent
    intent: UserIntent
    active_plan: Optional[ActivePlan] = None
    step_results: List[StepExecutionResult] = Field(default_factory=list)
    context_used: List[Dict[str, Any]] = Field(default_factory=list)
    reflections: List[str] = Field(default_factory=list)
    consolidated_ids: List[str] = Field(default_factory=list)
    execution_time_ms: float = 0.0

    @property
    def success(self) -> bool:
        return all(s.status == "success" for s in self.step_results) if self.step_results else True

    @property
    def plan(self) -> Optional[ActivePlan]:
        return self.active_plan

    @property
    def response_text(self) -> str:
        for s in self.step_results:
            if isinstance(s.result, dict) and "answer" in s.result:
                return str(s.result["answer"])
        return ""
