"""Verified-first semantic context reduction.

Reduction is a post-verification transformation, never a trust mechanism.
The reducer removes redundancy while preserving security, provenance and
agent-to-agent continuity facts.
"""
from __future__ import annotations

import math
import re
from dataclasses import dataclass
from typing import Any, Mapping

PRESERVED_FIELDS = (
    "verification",
    "security",
    "provenance",
    "integrity",
    "requirements",
    "forbidden",
    "constraints",
    "acceptance",
    "task",
    "state",
    "current_state",
    "decisions",
    "discoveries",
    "completed",
    "failed_attempts",
    "dependencies",
    "artifacts",
    "open_questions",
    "next_actions",
)

TRUSTED_STATUSES = frozenset({
    "TRUSTED",
    "VERIFIED",
    "SAFE",
    "VERIFIED_SOURCE",
    "DERIVED_FROM_VERIFIED_SOURCE",
})
PROVENANCE_REQUIRED_STATUSES = frozenset({
    "VERIFIED_SOURCE",
    "DERIVED_FROM_VERIFIED_SOURCE",
})

#: Principals that may inspect content the model-facing route withholds (the owner
#: views). Every egress layer (context pack builder, data router) reads this one set
#: so that ADMIN is treated the same everywhere.
OWNER_PRINCIPALS = frozenset({"human", "admin"})


def is_owner_principal(principal: Any) -> bool:
    """True for the owner principals; accepts a Principal enum member or its string value."""
    value = getattr(principal, "value", principal)
    return str(value).strip().lower() in OWNER_PRINCIPALS


def content_withheld_from(principal: Any, item: Mapping[str, Any], *, unverified: bool) -> bool:
    """Whether `item`'s body must not reach a non-owner model-facing caller.

    The contract (CLAUDE.md, ``memory_get``): ACTIVE notes and REVIEW notes are readable
    by an agent, REVIEW marked unverified. The model-facing context route therefore
    withholds exactly two things:

    * a note explicitly flagged ``quarantined`` (whatever its verification), and
    * the body of an unverified REVIEW candidate. It is not hidden from the agent: it is
      handed over only by the controlled ``memory_access`` tools, explicitly marked
      unverified and as untrusted data, never inside a trusted context pack.

    An ACTIVE note that is merely not ``verified`` keeps its content: lacking a
    verification stamp is a label, not a reason to hide a note the vault serves.
    """
    if is_owner_principal(principal):
        return False
    if item.get("quarantined") is True:
        return True
    return bool(unverified) and str(item.get("lifecycle", "")).upper() == "REVIEW"


@dataclass(frozen=True)
class ReductionResult:
    allowed: bool
    reason: str
    content: str
    metadata: dict[str, Any]
    original_chars: int
    final_chars: int
    bytes_saved: int
    token_estimate_before: int
    token_estimate_after: int
    tokens_saved: int


class VerifiedReducer:
    """Reduce only content that has already passed an explicit verification gate."""

    def __init__(self, chars_per_token: float = 4.0):
        self.chars_per_token = max(1.0, float(chars_per_token))

    def estimate_tokens(self, text: str) -> int:
        if not text:
            return 0
        return max(1, math.ceil(len(text) / self.chars_per_token))

    @staticmethod
    def _dedupe_lines(text: str) -> str:
        seen: set[str] = set()
        out: list[str] = []
        for raw in text.splitlines():
            line = re.sub(r"[ \t]+", " ", raw).strip()
            if not line:
                continue
            key = line.casefold()
            if key in seen:
                continue
            seen.add(key)
            out.append(line)
        return "\n".join(out)

    @staticmethod
    def _compact_whitespace(text: str) -> str:
        text = re.sub(r"[ \t]+", " ", text)
        return re.sub(r"\n{3,}", "\n\n", text).strip()

    @staticmethod
    def _truncate_at_boundary(text: str, limit: int) -> str:
        if len(text) <= limit:
            return text
        marker = "..."
        body_limit = max(1, limit - len(marker))
        candidate = text[:body_limit].rstrip()
        boundary = max(candidate.rfind("\n"), candidate.rfind(". "), candidate.rfind("; "))
        if boundary >= max(20, body_limit // 2):
            candidate = candidate[:boundary + (1 if text[boundary] == "." else 0)].rstrip()
        return candidate + marker

    def _is_verified(self, item: Mapping[str, Any], verified: bool) -> bool:
        if not verified:
            return False
        record = item.get("verification")
        if not isinstance(record, Mapping):
            return False
        status = str(record.get("status", "")).upper()
        if status not in TRUSTED_STATUSES:
            return False
        if status in PROVENANCE_REQUIRED_STATUSES:
            return isinstance(item.get("provenance"), Mapping)
        return True

    def reduce(
        self,
        item: Mapping[str, Any],
        *,
        verified: bool,
        max_chars: int,
    ) -> ReductionResult:
        original = str(item.get("content", ""))

        if not self._is_verified(item, verified):
            return ReductionResult(
                allowed=False,
                reason="verification_required",
                content="",
                metadata={},
                original_chars=len(original),
                final_chars=0,
                bytes_saved=len(original.encode("utf-8")),
                token_estimate_before=self.estimate_tokens(original),
                token_estimate_after=0,
                tokens_saved=self.estimate_tokens(original),
            )

        if bool(item.get("do_not_compress", False)):
            return ReductionResult(
                allowed=True, reason="DO_NOT_COMPRESS", content=original, metadata={
                    field: item[field] for field in PRESERVED_FIELDS if field in item
                }, original_chars=len(original), final_chars=len(original), bytes_saved=0,
                token_estimate_before=self.estimate_tokens(original), token_estimate_after=self.estimate_tokens(original), tokens_saved=0,
            )

        limit = max(1, int(max_chars))
        compact = self._compact_whitespace(self._dedupe_lines(original))
        mandatory_patterns = re.compile(r"(?im)^.*\b(?:MUST(?: NOT)?|NEVER|SHALL|REQUIRED|FORBIDDEN|DO_NOT_COMPRESS)\b.*$")
        protected_lines = [line.strip() for line in compact.splitlines() if mandatory_patterns.match(line)]
        candidate = self._truncate_at_boundary(compact, limit)
        if any(line not in candidate for line in protected_lines):
            candidate = compact
        compact = candidate

        metadata = {
            field: item[field]
            for field in PRESERVED_FIELDS
            if field in item
        }

        before = self.estimate_tokens(original)
        after = self.estimate_tokens(compact)
        original_bytes = len(original.encode("utf-8"))
        final_bytes = len(compact.encode("utf-8"))

        return ReductionResult(
            allowed=True,
            reason="verified_and_reduced",
            content=compact,
            metadata=metadata,
            original_chars=len(original),
            final_chars=len(compact),
            bytes_saved=max(0, original_bytes - final_bytes),
            token_estimate_before=before,
            token_estimate_after=after,
            tokens_saved=max(0, before - after),
        )
