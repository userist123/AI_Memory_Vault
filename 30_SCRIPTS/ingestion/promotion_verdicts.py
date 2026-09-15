"""The manifest a merge must consult before writing into the ontology.

## Why

Commit 3de7fff3e ran the merge over the reconciled corpus and wrote 201
concepts into the sixteen slot files, every one `proposed`, in one step. That
is exactly what the script was built to do. Nothing was broken.

160 of them were still there five days later, judged by nobody. When someone
finally read them, 28 were phrases rather than concepts, 8 duplicated concepts
already promoted, 1 was two different concepts sharing a word, and 6 needed an
architectural decision nobody had made. **Forty-seven percent of what recurred
often enough to reach the review floor did not belong in an ontology.**

The slot files were treated as a working area by the merge script and as
canonical ontology by everything else. Nobody wrote a gate because nobody
thought there was a boundary.

This is the boundary.

## What it is not

It is not a quality check. It cannot tell a good judgement from a bad one — a
manifest that marks a phrase `PROMOTE` will let that phrase through, and the
gate will have done its job.

What it removes is the *unexamined* row: one that entered because a script ran,
rather than because someone decided. Those were 160 of 213.

## The shape

    {
      "schema_version": "polymarket-promotion-verdicts.v1",
      "source_review": "07_EVALUATION/.../PROMOTION_REVIEW.md",
      "decided_at": "2026-09-12",
      "decided_by": "antigravity",
      "verdicts": [
        {"concept": "cryptocurrency", "verdict": "PROMOTE", "reason": "..."},
        {"concept": "external memory", "verdict": "REJECT", "reason": "..."}
      ]
    }

`reason` is required on every entry, including approvals. A verdict without one
is a vote, and the whole failure being fixed here is rows that got in without
anyone saying why.
"""
from __future__ import annotations

import json
import pathlib
from dataclasses import dataclass
from typing import Any, Iterable, Mapping, Optional

VERDICTS_SCHEMA_VERSION = "polymarket-promotion-verdicts.v1"

VERDICT_PROMOTE = "PROMOTE"
VERDICT_REJECT = "REJECT"
VERDICT_MERGE = "MERGE"
VERDICT_SPLIT = "SPLIT"
VERDICT_UNSURE = "UNSURE"

KNOWN_VERDICTS = frozenset({
    VERDICT_PROMOTE, VERDICT_REJECT, VERDICT_MERGE, VERDICT_SPLIT, VERDICT_UNSURE,
})

#: Only this one admits a concept. UNSURE is deliberately not among them: a
#: decision nobody could make is not a decision to proceed, and treating it as
#: one is how "we'll look at it later" becomes a permanent ontology row.
ADMITTING_VERDICTS = frozenset({VERDICT_PROMOTE})


class VerdictError(ValueError):
    """The manifest is malformed, or a concept has no verdict in it."""


@dataclass(frozen=True)
class Verdict:
    concept: str
    verdict: str
    reason: str
    merge_into: Optional[str] = None

    def validate(self) -> None:
        if not self.concept.strip():
            raise VerdictError("a verdict needs a concept")
        if self.verdict not in KNOWN_VERDICTS:
            raise VerdictError(
                f"{self.concept}: {self.verdict!r} is not a verdict. "
                f"Known: {sorted(KNOWN_VERDICTS)}"
            )
        if not self.reason.strip():
            raise VerdictError(
                f"{self.concept}: a verdict without a reason is a vote. The "
                "failure this gate exists for was rows entering with nobody "
                "saying why."
            )
        if self.verdict == VERDICT_MERGE and not (self.merge_into or "").strip():
            raise VerdictError(
                f"{self.concept}: MERGE must name what it merges into, or the "
                "row is removed and its occurrences land nowhere"
            )


def _normalise(name: str) -> str:
    """Fold the formatting a concept name picks up between documents.

    Names arrive as ``**`architecture`**`` in review tables and bare in slot
    files. A comparison that misses the difference reports zero overlap between
    two lists of the same concepts — a mistake made twice here in one day.
    """
    return " ".join(name.strip().strip("*").strip("`").strip().lower().split())


class VerdictManifest:
    """Verdicts by concept, with the lookup the merge performs."""

    def __init__(self, verdicts: Iterable[Verdict], *, source_review: str = "",
                 decided_by: str = "", decided_at: str = "") -> None:
        self.source_review = source_review
        self.decided_by = decided_by
        self.decided_at = decided_at
        self._by_concept: dict[str, Verdict] = {}
        for verdict in verdicts:
            verdict.validate()
            key = _normalise(verdict.concept)
            existing = self._by_concept.get(key)
            if existing is not None and existing != verdict:
                raise VerdictError(
                    f"{verdict.concept}: two different verdicts "
                    f"({existing.verdict} and {verdict.verdict}). One of them "
                    "is wrong and choosing silently is how the wrong one wins."
                )
            self._by_concept[key] = verdict

    def get(self, concept: str) -> Optional[Verdict]:
        return self._by_concept.get(_normalise(concept))

    def admits(self, concept: str) -> bool:
        verdict = self.get(concept)
        return verdict is not None and verdict.verdict in ADMITTING_VERDICTS

    def counts(self) -> dict[str, int]:
        counts: dict[str, int] = {}
        for verdict in self._by_concept.values():
            counts[verdict.verdict] = counts.get(verdict.verdict, 0) + 1
        return counts

    def __len__(self) -> int:
        return len(self._by_concept)


def load_manifest(path: pathlib.Path | str) -> VerdictManifest:
    payload = json.loads(pathlib.Path(path).read_text(encoding="utf-8"))
    if not isinstance(payload, Mapping):
        raise VerdictError("a verdict manifest is a JSON object")
    if payload.get("schema_version") != VERDICTS_SCHEMA_VERSION:
        raise VerdictError(
            f"unsupported manifest version {payload.get('schema_version')!r}; "
            f"expected {VERDICTS_SCHEMA_VERSION}"
        )
    entries = payload.get("verdicts")
    if not isinstance(entries, list) or not entries:
        raise VerdictError("a manifest with no verdicts admits nothing and is "
                           "almost certainly a mistake rather than a decision")

    return VerdictManifest(
        (
            Verdict(
                concept=str(e.get("concept", "")),
                verdict=str(e.get("verdict", "")).strip().upper(),
                reason=str(e.get("reason", "")),
                merge_into=e.get("merge_into"),
            )
            for e in entries
        ),
        source_review=str(payload.get("source_review", "")),
        decided_by=str(payload.get("decided_by", "")),
        decided_at=str(payload.get("decided_at", "")),
    )


def partition(
    records: Iterable[Mapping[str, Any]], manifest: VerdictManifest
) -> tuple[list[Mapping[str, Any]], dict[str, list[str]]]:
    """Split staging rows into admitted and withheld, with the reason.

    Withheld rows are grouped by verdict rather than discarded, and a concept
    absent from the manifest gets its own group. That group is the one that
    matters: it is the 160, and a run that silently dropped them would look
    identical to a run where they had been judged.
    """
    admitted: list[Mapping[str, Any]] = []
    withheld: dict[str, list[str]] = {}
    for record in records:
        concept = str(record.get("concept", ""))
        verdict = manifest.get(concept)
        if verdict is None:
            withheld.setdefault("NO_VERDICT", []).append(concept)
            continue
        if verdict.verdict in ADMITTING_VERDICTS:
            admitted.append(record)
        else:
            withheld.setdefault(verdict.verdict, []).append(concept)
    return admitted, withheld


__all__ = [
    "VERDICTS_SCHEMA_VERSION",
    "VERDICT_PROMOTE",
    "VERDICT_REJECT",
    "VERDICT_MERGE",
    "VERDICT_SPLIT",
    "VERDICT_UNSURE",
    "KNOWN_VERDICTS",
    "ADMITTING_VERDICTS",
    "VerdictError",
    "Verdict",
    "VerdictManifest",
    "load_manifest",
    "partition",
]
