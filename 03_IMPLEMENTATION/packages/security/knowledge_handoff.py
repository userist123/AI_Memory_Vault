"""Agent-to-agent knowledge handoff built on verified reduction."""

from __future__ import annotations

from dataclasses import dataclass
from typing import Any, Mapping

from .verified_reduction import ReductionResult, VerifiedReducer


KNOWLEDGE_FIELDS = (
    "task",
    "state",
    "current_state",
    "decisions",
    "discoveries",
    "completed",
    "failed_attempts",
    "constraints",
    "dependencies",
    "artifacts",
    "open_questions",
    "next_actions",
)

SECURITY_FIELDS = (
    "verification",
    "security",
    "provenance",
    "integrity",
    "requirements",
    "forbidden",
    "acceptance",
)


@dataclass(frozen=True)
class KnowledgeCapsule:
    content: str
    knowledge: dict[str, Any]
    security: dict[str, Any]
    original_chars: int
    final_chars: int
    tokens_saved: int
    recoverable_ref: str | None


class VerifiedKnowledgeHandoff:
    """Compile verified agent work into minimal, recoverable handoff state."""

    def __init__(self, reducer: VerifiedReducer | None = None):
        self.reducer = reducer or VerifiedReducer()

    def reduce(self, item: Mapping[str, Any], *, max_chars: int) -> ReductionResult:
        # The caller must explicitly attest that verification was completed.
        # The reducer still requires the embedded trusted status, so an
        # unverified caller cannot manufacture a trusted result.
        return self.reducer.reduce(item, verified=True, max_chars=max_chars)

    def compile(
        self,
        item: Mapping[str, Any],
        *,
        max_chars: int,
        recoverable_ref: str | None = None,
    ) -> KnowledgeCapsule:
        result = self.reduce(item, max_chars=max_chars)
        if not result.allowed:
            raise PermissionError(result.reason)

        knowledge = {
            key: result.metadata[key]
            for key in KNOWLEDGE_FIELDS
            if key in result.metadata
        }
        security = {
            key: result.metadata[key]
            for key in SECURITY_FIELDS
            if key in result.metadata
        }
        return KnowledgeCapsule(
            content=result.content,
            knowledge=knowledge,
            security=security,
            original_chars=result.original_chars,
            final_chars=result.final_chars,
            tokens_saved=result.tokens_saved,
            recoverable_ref=recoverable_ref,
        )
