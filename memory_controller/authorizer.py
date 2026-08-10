# authorizer.py
"""Pluggable authorizer for Memory Controller.
Defines an abstract interface and a default implementation based on the
policy matrix required by the specification.
"""

from enum import Enum
from typing import Protocol, Set

class Principal(Enum):
    HUMAN = "human"
    AI_AGENT = "ai_agent"
    ADMIN = "admin"

class Operation(Enum):
    READ = "read"
    SEARCH = "search"
    PROPOSE = "propose"
    REVIEW = "review"
    PROMOTE = "promote"
    ARCHIVE = "archive"
    UPDATE = "update"
    SUPERSEDE = "supersede"

class Authorizer(Protocol):
    """Authorizer protocol – objects must implement `is_allowed`.
    """
    def is_allowed(self, principal: Principal, operation: Operation) -> bool:
        ...

class DefaultAuthorizer:
    """Default policy implementation.

    The matrix follows the specification:
    * READ / SEARCH – Human, AI Agent
    * PROPOSE – Human, AI Agent
    * REVIEW – Human only
    * PROMOTE – Human only
    * ARCHIVE – Human (Admin may override later)
    * UPDATE – depends on lifecycle – handled in core, but permission
      is granted to Human and Admin for ACTIVE notes; AI can update
      non‑ACTIVE drafts.
    """

    _policy = {
        Operation.READ: {Principal.HUMAN, Principal.AI_AGENT, Principal.ADMIN},
        Operation.SEARCH: {Principal.HUMAN, Principal.AI_AGENT, Principal.ADMIN},
        Operation.PROPOSE: {Principal.HUMAN, Principal.AI_AGENT, Principal.ADMIN},
        Operation.REVIEW: {Principal.HUMAN, Principal.ADMIN},
        Operation.PROMOTE: {Principal.HUMAN, Principal.ADMIN},
        Operation.ARCHIVE: {Principal.HUMAN, Principal.ADMIN},
        Operation.UPDATE: {Principal.HUMAN, Principal.ADMIN, Principal.AI_AGENT},
        Operation.SUPERSEDE: {Principal.HUMAN, Principal.ADMIN, Principal.AI_AGENT},
    }

    def is_allowed(self, principal: Principal, operation: Operation) -> bool:
        allowed: Set[Principal] = self._policy.get(operation, set())
        return principal in allowed
