"""Verified-first context reduction.

This module deliberately separates two operations:

1. verification establishes that an artifact may enter the context pipeline;
2. reduction removes redundant payload while preserving security-critical facts.

Reduction is never a trust mechanism. A caller cannot make unverified content
safe by asking for a shorter representation.
"""
from __future__ import annotations

import re
from dataclasses import dataclass
from typing import Any, Mapping

# These fields are policy/evidence, not disposable prose. They survive reduction
# byte-for-byte (as Python values) so the compact representation cannot silently
# change the security decision.
PRESERVED_FIELDS = (
    "verification",
    "security",
    "provenance",
    "integrity",
    "requirements",
    "forbidden",
    "constraints",
    "acceptance",
)

TRUSTED_STATUSES = frozenset({"TRUSTED", "VERIFIED", "SAFE"})


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
        return max(1, (len(text) + int(self.chars_per_token) - 1) // int(self.chars_per_token))

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
        text = re.sub(r"\n{3,}", "\n\n", text)
        return text.strip()

    def _is_verified(self, item: Mapping[str, Any], verified: bool) -> bool:
        if not verified:
            return False
        record = item.get("verification")
        if not isinstance(record, Mapping):
            return False
        status = str(record.get("status", "")).upper()
        return status in TRUSTED_STATUSES

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
                token_estimate_before=self.estimate_tokens(original) if original else 0,
                token_estimate_after=0,
                tokens_saved=self.estimate_tokens(original) if original else 0,
            )

        limit = max(1, int(max_chars))
        compact = self._compact_whitespace(self._dedupe_lines(original))

        # Keep the beginning of the verified content as the compact body. The
        # security/policy fields remain separately preserved below, so truncation
        # cannot erase the decision evidence or mandatory constraints.
        if len(compact) > limit:
            compact = compact[:limit].rstrip()
            if len(compact) >= 3:
                compact = compact[:-3].rstrip() + "..."

        metadata = {
            field: item[field]
            for field in PRESERVED_FIELDS
            if field in item
        }

        before = self.estimate_tokens(original) if original else 0
        after = self.estimate_tokens(compact) if compact else 0
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
