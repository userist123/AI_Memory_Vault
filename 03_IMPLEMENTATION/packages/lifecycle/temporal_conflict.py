"""Bitemporal conflict detection helpers.

Conflicts are reported, never silently resolved. Explicit conflicts and
high-confidence semantic contradictions are surfaced separately.
"""

from __future__ import annotations

from dataclasses import dataclass
from datetime import date
from typing import Any, Dict, Iterable, Optional

from .semantic_conflict import detect_pair


@dataclass(frozen=True)
class TemporalConflict:
    left_id: str
    right_id: str
    reason: str
    score: float | None = None
    status: str = "conflict"

    def as_dict(self) -> dict[str, Any]:
        data: dict[str, Any] = {
            "left_id": self.left_id,
            "right_id": self.right_id,
            "reason": self.reason,
        }
        if self.score is not None:
            data["score"] = self.score
        if self.status not in {"explicit", "conflict"}:
            data["status"] = self.status
        return data


def detect_temporal_conflicts(
    notes: Iterable[Dict[str, Any]],
    *,
    as_of: Optional[date] = None,
    known_as_of: Optional[date] = None,
) -> list[TemporalConflict]:
    from .temporal_controller import matches_temporal
    eligible = [note for note in notes if matches_temporal(note, as_of=as_of, known_as_of=known_as_of)]
    by_id = {str(note.get("id")): note for note in eligible if note.get("id")}
    conflicts: set[tuple[str, str]] = set()
    result: list[TemporalConflict] = []

    for note in eligible:
        left = str(note.get("id", ""))
        target = note.get("conflicts_with")
        if not left or not target or str(target) not in by_id:
            continue
        right = str(target)
        pair = tuple(sorted((left, right)))
        if pair in conflicts:
            continue
        conflicts.add(pair)
        result.append(TemporalConflict(pair[0], pair[1], "explicit conflicts_with", status="explicit"))

    # Semantic contradiction detection is deliberately thresholded and read-only.
    for idx, left in enumerate(eligible):
        for right in eligible[idx + 1:]:
            conflict = detect_pair(left, right, as_of=as_of)
            if conflict is None:
                continue
            pair = tuple(sorted((conflict.left_id, conflict.right_id)))
            if pair in conflicts:
                continue
            conflicts.add(pair)
            result.append(
                TemporalConflict(
                    pair[0], pair[1],
                    "; ".join(conflict.reasons),
                    score=conflict.score,
                    status="potential_conflict",
                )
            )

    return sorted(result, key=lambda item: (item.left_id, item.right_id, item.status))
