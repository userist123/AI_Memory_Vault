"""
Milestone 3: Specialized Agent Data Models, Enums, and Capability Matrices.
"""

from enum import Enum, IntEnum
from typing import List, Dict, Any, Optional, Set, Union, Literal
import time
import uuid
from datetime import datetime, timezone
from pydantic import BaseModel, Field, field_validator, ConfigDict

from jarvis.memory.invariants import Operation, Principal
from jarvis.llm.base import CancellationToken


class AgentRole(str, Enum):
    """Specialized Agent Worker Roles."""
    ROUTER = "router"
    RETRIEVAL = "retrieval"
    VERIFIER = "verifier"
    CONSOLIDATOR = "consolidator"
    CRITIC = "critic"
    SUPERVISOR = "supervisor"


class TaskPriority(IntEnum):
    """Task Priority Tiers (Lower numerical value = Higher scheduling priority)."""
    P1_URGENT = 1        # Real-time Voice / Interactive Commands
    P2_INTERACTIVE = 2   # High-priority Queries / Pre-voice Verification & Critique
    P3_STANDARD = 3      # Standard Multi-Step Subtasks / Deep Associative Retrieval
    P4_AUDIT = 4         # Memory Compliance & Frontmatter Invariant Audits
    P5_BACKGROUND = 5    # Background Maintenance / Lesson Consolidation / Reconsolidation


class TaskStatus(str, Enum):
    """Lifecycle states of a scheduled or running agent task."""
    PENDING = "pending"
    RUNNING = "running"
    COMPLETED = "completed"
    FAILED = "failed"
    CANCELLED = "cancelled"
    TIMED_OUT = "timed_out"


# ============================================================================
# Role-Based Capability Matrix (Least-Privilege Scoping)
# ============================================================================

ROLE_PERMISSIONS: Dict[AgentRole, Set[Operation]] = {
    AgentRole.ROUTER: {Operation.READ, Operation.SEARCH},
    AgentRole.RETRIEVAL: {Operation.READ, Operation.SEARCH},
    AgentRole.VERIFIER: {Operation.READ},
    AgentRole.CONSOLIDATOR: {Operation.SEARCH, Operation.READ, Operation.PROPOSE, Operation.ARCHIVE},
    AgentRole.CRITIC: {Operation.READ, Operation.PROPOSE},
    AgentRole.SUPERVISOR: {Operation.READ, Operation.SEARCH},
}

CapabilityMatrix = ROLE_PERMISSIONS


# ============================================================================
# Core Agent Task & Result Models
# ============================================================================

class AgentTask(BaseModel):
    """Execution unit dispatched to a specialized agent worker."""
    task_id: str = Field(default_factory=lambda: str(uuid.uuid4()))
    priority: int = 3
    role: Union[AgentRole, str] = AgentRole.ROUTER
    action: str = ""
    payload: Dict[str, Any] = Field(default_factory=dict)
    created_at: float = Field(default_factory=time.time)
    timeout_seconds: float = 30.0
    max_retries: int = 0
    retry_count: int = 0
    cancellation_token: Optional[Any] = None

    model_config = ConfigDict(arbitrary_types_allowed=True)

    @field_validator("role", mode="before")
    @classmethod
    def normalize_role(cls, v: Any) -> AgentRole:
        if isinstance(v, AgentRole):
            return v
        if isinstance(v, str):
            clean = v.strip().lower()
            for r in AgentRole:
                if r.value == clean or r.name.lower() == clean:
                    return r
            # Fallback if custom string
            return AgentRole.ROUTER
        return AgentRole.ROUTER

    @property
    def timeout_s(self) -> float:
        return self.timeout_seconds

    def __lt__(self, other: "AgentTask") -> bool:
        if self.priority == other.priority:
            return self.created_at < other.created_at
        return self.priority < other.priority


class TaskResult(BaseModel):
    """Result payload produced by an agent task execution."""
    task_id: str
    role: Union[AgentRole, str]
    action: str = ""
    status: Union[TaskStatus, str] = TaskStatus.COMPLETED
    result: Optional[Any] = None
    error: Optional[str] = None
    execution_time_ms: float = 0.0

    @field_validator("role", mode="before")
    @classmethod
    def normalize_role(cls, v: Any) -> Union[AgentRole, str]:
        if isinstance(v, AgentRole):
            return v
        if isinstance(v, str):
            for r in AgentRole:
                if r.value == v.lower() or r.name.lower() == v.lower():
                    return r
        return v


AgentTaskResult = TaskResult


# ============================================================================
# Router Agent Models
# ============================================================================

class SubTaskScope(str, Enum):
    """Operational domains for decomposed subtasks."""
    IOT_CONTROL = "iot_control"
    MEMORY_RETRIEVAL = "memory_retrieval"
    MEMORY_STORE = "memory_store"
    SYSTEM_STATUS = "system_status"
    CONVERSATION = "conversation"
    REASONING = "reasoning"
    QUERY = "query"


class DecomposedSubTask(BaseModel):
    """Discrete atomic subtask extracted from compound input."""
    subtask_id: int
    raw_query: str
    scope: SubTaskScope = SubTaskScope.CONVERSATION
    priority: int = 2
    dependencies: List[int] = Field(default_factory=list)
    action: str = ""
    kwargs: Dict[str, Any] = Field(default_factory=dict)
    description: str = ""


class RouterOutput(BaseModel):
    """Complete output of query analysis and decomposition."""
    original_query: str
    subtasks: List[DecomposedSubTask] = Field(default_factory=list)
    is_composite: bool = False
    confidence: float = 1.0
    routing_latency_ms: float = 0.0

    @property
    def count(self) -> int:
        return len(self.subtasks)


# ============================================================================
# Retrieval Agent Models
# ============================================================================

class RetrievalQuery(BaseModel):
    """Parameterized associative search request."""
    query: str
    working_memory_context: Optional[List[Dict[str, Any]]] = None
    limit: int = 10
    include_superseded: bool = False
    category: Optional[str] = None
    tags: Optional[List[str]] = None
    temporal_override: Optional[str] = None
    max_depth: int = 1


class ScoredMemoryNote(BaseModel):
    """Memory note enriched with multi-signal relevance scoring."""
    note: Dict[str, Any]
    composite_score: float
    lexical_score: float = 0.0
    activation_score: float = 0.0
    confidence_score: float = 0.0
    lineage_active_successor_id: Optional[str] = None


class RetrievalResult(BaseModel):
    """Response payload containing ranked memory candidates."""
    query: str
    notes: List[ScoredMemoryNote] = Field(default_factory=list)
    matches: List[Dict[str, Any]] = Field(default_factory=list)
    total_candidates: int = 0
    top_id: Optional[str] = None
    retrieval_time_ms: float = 0.0

    @property
    def count(self) -> int:
        return len(self.matches) if self.matches else len(self.notes)


# ============================================================================
# Verifier Agent Models
# ============================================================================

class ViolationSeverity(str, Enum):
    """Severity classification of schema & invariant audit findings."""
    ERROR = "error"
    WARNING = "warning"
    INFO = "info"


class SchemaViolation(BaseModel):
    """Specific schema or invariant rule failure."""
    field: str
    rule: str
    message: str
    severity: ViolationSeverity = ViolationSeverity.ERROR


class VerificationReport(BaseModel):
    """Detailed audit report produced by VerifierAgent."""
    note_id: Optional[str] = None
    is_valid: bool
    violations: List[SchemaViolation] = Field(default_factory=list)
    missing: List[str] = Field(default_factory=list)
    audit_timestamp: str = Field(default_factory=lambda: datetime.now(timezone.utc).isoformat())
    auditor_role: str = "verifier"


# ============================================================================
# Consolidator Agent Models
# ============================================================================

class ConsolidationCluster(BaseModel):
    """Identified cluster of related ephemeral review lessons."""
    cluster_id: str
    topic: str
    source_note_ids: List[str]
    proposed_content: str
    tags: List[str] = Field(default_factory=list)


class ConsolidationSummary(BaseModel):
    """Summary of consolidation and archival pass."""
    consolidated_note_id: Optional[str] = None
    archived_source_ids: List[str] = Field(default_factory=list)
    cluster_count: int = 0
    status: str = "success"


# ============================================================================
# Critic Agent Models
# ============================================================================

class ReflexionAnalysis(BaseModel):
    """Structured 6-stage Reflexion model."""
    error: str
    root_cause: str
    fix_applied: str
    verification: str
    prevention_rule: str
    core_lesson: str

    def to_markdown(self) -> str:
        return (
            f"## Formal Reflexion Analysis\n\n"
            f"- **Error**: {self.error}\n"
            f"- **Root Cause**: {self.root_cause}\n"
            f"- **Fix Applied**: {self.fix_applied}\n"
            f"- **Verification**: {self.verification}\n"
            f"- **Prevention Rule**: {self.prevention_rule}\n"
            f"- **Core Lesson**: {self.core_lesson}\n"
        )


class CritiqueResult(BaseModel):
    """Quality and compliance assessment of candidate response or plan."""
    approved: bool
    score: float = 1.0
    critique: str
    suggested_refinement: Optional[str] = None
    flags: List[str] = Field(default_factory=list)
