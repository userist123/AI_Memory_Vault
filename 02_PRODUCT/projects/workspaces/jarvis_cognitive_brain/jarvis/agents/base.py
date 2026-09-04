"""
Milestone 3: Base Agent and Scoped Storage Proxy Enforcing Least Privilege (P0-P18).
"""

from abc import ABC, abstractmethod
from typing import Dict, Any, Optional, List, Union
import logging

from jarvis.memory.invariants import (
    Principal,
    Operation,
    Lifecycle,
    NoteType,
    validate_propose_invariants,
    validate_update_invariants,
    validate_attest_invariants,
    validate_promote_invariants,
    validate_supersession_invariants,
    validate_hardware_telemetry_invariants,
)
from jarvis.memory.sqlite_engine import SQLiteStorageEngine
from jarvis.llm.base import BaseLLMProvider, CancellationToken
from jarvis.agents.models import AgentRole, ROLE_PERMISSIONS

logger = logging.getLogger(__name__)


class ScopedStorageProxy:
    """
    Security boundary wrapping SQLiteStorageEngine.
    Enforces agent role-based capability boundaries and P0-P18 invariants at runtime.
    """

    def __init__(
        self,
        storage: SQLiteStorageEngine,
        role: AgentRole,
        principal: Principal = Principal.AI_AGENT,
    ):
        self._storage = storage
        self._role = role
        self._principal = principal

    @property
    def role(self) -> AgentRole:
        return self._role

    @property
    def principal(self) -> Principal:
        return self._principal

    @property
    def underlying_storage(self) -> SQLiteStorageEngine:
        return self._storage

    def _assert_op(self, op: Operation) -> None:
        """Check if the operation is permitted for this agent's role."""
        allowed = ROLE_PERMISSIONS.get(self._role, set())
        if op not in allowed:
            raise PermissionError(
                f"Agent with role '{self._role.value}' is not permitted to perform operation '{op.value}' (RBAC Violation)."
            )

    def get(self, note_id: str) -> Optional[Dict[str, Any]]:
        self._assert_op(Operation.READ)
        return self._storage.get(note_id)

    def query(
        self,
        lifecycle: Optional[Union[str, List[str]]] = None,
        note_type: Optional[Union[str, List[str]]] = None,
        category: Optional[str] = None,
        limit: int = 100,
    ) -> List[Dict[str, Any]]:
        self._assert_op(Operation.READ)
        return self._storage.query(
            lifecycle=lifecycle,
            note_type=note_type,
            category=category,
            limit=limit,
        )

    def search_bm25(self, query: str, limit: int = 20) -> List[Dict[str, Any]]:
        self._assert_op(Operation.SEARCH)
        return self._storage.search_bm25(query, limit=limit)

    def get_lineage(self, note_id: str, max_depth: int = 50) -> List[Dict[str, Any]]:
        self._assert_op(Operation.READ)
        return self._storage.get_lineage(note_id, max_depth=max_depth)

    def resolve_active_lineage(self, note_id: str) -> Optional[Dict[str, Any]]:
        self._assert_op(Operation.READ)
        return self._storage.resolve_active_lineage(note_id)

    def count(self) -> int:
        self._assert_op(Operation.READ)
        return self._storage.count()

    def propose(self, note_or_principal: Any, note: Optional[Dict[str, Any]] = None) -> Dict[str, Any]:
        self._assert_op(Operation.PROPOSE)
        # Handle both propose(note) and propose(principal, note)
        if note is None and isinstance(note_or_principal, dict):
            target_note = note_or_principal
            target_principal = self._principal
        else:
            target_principal = note_or_principal
            target_note = note if note is not None else {}

        # Enforce propose invariants P0-001..P0-005, P16-P18
        validate_propose_invariants(target_principal, target_note)
        return self._storage.propose(target_principal, target_note)

    def update(
        self,
        principal_or_note_id: Any,
        note_id_or_updates: Any,
        updates: Optional[Dict[str, Any]] = None,
    ) -> Dict[str, Any]:
        self._assert_op(Operation.UPDATE)
        if updates is None:
            target_principal = self._principal
            target_note_id = principal_or_note_id
            target_updates = note_id_or_updates
        else:
            target_principal = principal_or_note_id
            target_note_id = note_id_or_updates
            target_updates = updates

        current = self._storage.get(target_note_id)
        if not current:
            raise ValueError(f"Note with ID '{target_note_id}' does not exist.")
        validate_update_invariants(target_principal, current, target_updates)
        return self._storage.update(target_principal, target_note_id, target_updates)

    def attest(
        self,
        principal_or_note_id: Any,
        note_id_or_reason: str = "",
        reason: str = "",
        evidence_ref: str = "",
    ) -> Dict[str, Any]:
        self._assert_op(Operation.ATTEST)
        if isinstance(principal_or_note_id, Principal):
            target_principal = principal_or_note_id
            target_note_id = note_id_or_reason
            target_reason = reason
            target_evidence = evidence_ref
        else:
            target_principal = self._principal
            target_note_id = principal_or_note_id
            target_reason = note_id_or_reason
            target_evidence = reason

        validate_attest_invariants(target_principal, target_note_id)
        return self._storage.attest(target_principal, target_note_id, target_reason, target_evidence)

    def promote(self, principal_or_note_id: Any, note_id: Optional[str] = None) -> Dict[str, Any]:
        self._assert_op(Operation.PROMOTE)
        if isinstance(principal_or_note_id, Principal) and note_id is not None:
            target_principal = principal_or_note_id
            target_note_id = note_id
        else:
            target_principal = self._principal
            target_note_id = principal_or_note_id

        current = self._storage.get(target_note_id)
        if not current:
            raise ValueError(f"Note with ID '{target_note_id}' does not exist.")
        validate_promote_invariants(target_principal, current)
        return self._storage.promote(target_principal, target_note_id)

    def archive(
        self,
        principal_or_note_id: Any,
        note_id_or_reason: str = "",
        reason: str = "",
    ) -> Dict[str, Any]:
        self._assert_op(Operation.ARCHIVE)
        if isinstance(principal_or_note_id, Principal):
            target_principal = principal_or_note_id
            target_note_id = note_id_or_reason
            target_reason = reason
        else:
            target_principal = self._principal
            target_note_id = principal_or_note_id
            target_reason = note_id_or_reason

        return self._storage.archive(target_principal, target_note_id, reason=target_reason)

    def supersede(
        self,
        principal_or_old_id: Any,
        old_id_or_new_id: str,
        new_id: Optional[str] = None,
    ) -> None:
        self._assert_op(Operation.SUPERSEDE)
        if new_id is not None:
            target_principal = principal_or_old_id
            target_old_id = old_id_or_new_id
            target_new_id = new_id
        else:
            target_principal = self._principal
            target_old_id = principal_or_old_id
            target_new_id = old_id_or_new_id

        self._storage.supersede(target_principal, target_old_id, target_new_id)

    def delete(self, note_id: str) -> bool:
        self._assert_op(Operation.DELETE)
        return self._storage.delete(note_id)


class BaseAgent(ABC):
    """
    Abstract Base Class for all specialized agent workers.
    Ensures least-privilege storage scoping and standard execution contracts.
    """

    role: AgentRole = AgentRole.ROUTER
    principal: Principal = Principal.AI_AGENT

    def __init__(
        self,
        storage: Optional[Union[ScopedStorageProxy, SQLiteStorageEngine]] = None,
        llm: Optional[BaseLLMProvider] = None,
        principal: Principal = Principal.AI_AGENT,
    ):
        self.principal = principal
        self.llm = llm
        if storage is not None:
            if isinstance(storage, ScopedStorageProxy):
                self.storage: Optional[ScopedStorageProxy] = storage
            else:
                self.storage = ScopedStorageProxy(storage, self.role, self.principal)
        else:
            self.storage = None

    @abstractmethod
    async def execute(
        self,
        payload: Dict[str, Any],
        cancellation_token: Optional[CancellationToken] = None,
    ) -> Any:
        """Execute role-specific task workload."""
        pass
