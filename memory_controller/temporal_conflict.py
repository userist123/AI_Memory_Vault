"""Bitemporal conflict detection helpers.

Conflicts are reported, never silently resolved. Two notes are conflicting when
both are temporally eligible for the requested snapshot and explicitly identify
one another via ``conflicts_with`` or share a caller-supplied conflict key.
"""

from __future__ import annotations

from dataclasses import dataclass
from datetime import date
from typing import Any, Dict, Iterable, Optional

from .temporal_controller import matches_temporal


@dataclass(frozen=True)
class TemporalConflict:
    left_id: str
    right_id: str
    reason: str

    def as_dict(self) -> dict[str, str]:
        return {
            "left_id": self.left_id,
            "right_id": self.right_id,
            "reason": self.reason,
        }


def detect_temporal_conflicts(
    notes: Iterable[Dict[str, Any]],
    *,
    as_of: Optional[date] = None,
    known_as_of: Optional[date] = None,
) -> list[TemporalConflict]:
    eligible = [
        note for note in notes
        if matches_temporal(note, as_of=as_of, known_as_of=known_as_of)
    ]
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
        result.append(TemporalConflict(pair[0], pair[1], "explicit conflicts_with"))

    return sorted(result, key=lambda item: (item.left_id, item.right_id))
