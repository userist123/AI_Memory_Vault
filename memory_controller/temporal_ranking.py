"""Deterministic temporal ranking and lineage helpers."""

from __future__ import annotations

from datetime import date
from typing import Any, Mapping, Optional

from .temporal_controller import _as_date, matches_temporal


def temporal_score(
    note: Mapping[str, Any],
    *,
    as_of: Optional[date],
    known_as_of: Optional[date],
) -> float:
    """Score a temporally eligible note without inventing missing dates.

    Later valid_from dates are preferred for an as_of snapshot because they are
    the most recent applicable version at that point in time. Earlier knowledge
    (extraction) is preferred for equal valid_from dates.
    """
    if not matches_temporal(dict(note), as_of=as_of, known_as_of=known_as_of):
        return float("-inf")

    valid_from = _as_date(note.get("valid_from"))
    extraction = _as_date((note.get("provenance") or {}).get("extraction_date"))

    valid_rank = valid_from.toordinal() if valid_from else 0
    extraction_rank = extraction.toordinal() if extraction else 0

    # Keep this score intentionally bounded and deterministic.
    return float(valid_rank) + (extraction_rank / 1_000_000.0)


def rank_temporal_notes(
    notes: list[Mapping[str, Any]],
    *,
    as_of: Optional[date],
    known_as_of: Optional[date],
) -> list[Mapping[str, Any]]:
    """Filter and rank notes for a bitemporal snapshot."""
    eligible = [
        note
        for note in notes
        if matches_temporal(note, as_of=as_of, known_as_of=known_as_of)
    ]
    return sorted(
        eligible,
        key=lambda note: (
            temporal_score(note, as_of=as_of, known_as_of=known_as_of),
            str(note.get("id", "")),
        ),
        reverse=True,
    )
