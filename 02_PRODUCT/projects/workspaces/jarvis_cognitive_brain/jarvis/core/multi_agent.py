"""
Backwards-compatible integration and re-exports for Multi-Agent subsystem.
"""

from jarvis.agents.models import (
    AgentRole,
    TaskPriority,
    TaskStatus,
    AgentTask,
    TaskResult,
    AgentTaskResult,
    CapabilityMatrix,
    ROLE_PERMISSIONS,
)
from jarvis.agents.base import BaseAgent, ScopedStorageProxy
from jarvis.agents.router import RouterAgent
from jarvis.agents.retrieval import RetrievalAgent
from jarvis.agents.verifier import VerifierAgent
from jarvis.agents.consolidator import ConsolidatorAgent
from jarvis.agents.critic import CriticAgent
from jarvis.agents.supervisor import MultiAgentSupervisor, SupervisorCoordinator

__all__ = [
    "AgentRole",
    "TaskPriority",
    "TaskStatus",
    "AgentTask",
    "TaskResult",
    "AgentTaskResult",
    "CapabilityMatrix",
    "ROLE_PERMISSIONS",
    "ScopedStorageProxy",
    "BaseAgent",
    "RouterAgent",
    "RetrievalAgent",
    "VerifierAgent",
    "ConsolidatorAgent",
    "CriticAgent",
    "MultiAgentSupervisor",
    "SupervisorCoordinator",
]
