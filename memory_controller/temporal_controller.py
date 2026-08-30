"""Temporal adapter for the canonical MemoryController.

This module preserves the existing controller API and adds explicit bitemporal
filtering for callers that need an ``as_of`` or ``known_as_of`` view. It does
not alter lifecycle, authorization, or mutation semantics of MemoryController.
"""

from __future__ import annotations

from datetime import date
from typing import Any, Dict, List, Optional

from .controller import Lifecycle, MemoryController
from .authorizer import Principal


def _as_date(value: Any) -> Optional[date]:
    if value is None or value == "":
        return None
    if isinstance(value, date):
        return value
    try:
        return date.fromisoformat(str(value)[:10])
    except (TypeError, ValueError):
        raise ValueError(f"Invalid ISO date: {value!r}")


def matches_temporal(note: Dict[str, Any], *, as_of: Optional[date], known_as_of: Optional[date]) -> bool:
    if as_of is not None:
        valid_from = _as_date(note.get("valid_from"))
        valid_until = _as_date(note.get("valid_until"))
        if valid_from is not None and as_of < valid_from:
            return False
        if valid_until is not None and as_of > valid_until:
            return False

    if known_as_of is not None:
        extraction = _as_date((note.get("provenance") or {}).get("extraction_date"))
        if extraction is not None and extraction > known_as_of:
            return False

    return True


class TemporalMemoryController:
    """Compatibility wrapper around the canonical MemoryController.

    Temporal queries currently run as a bounded second-stage filter over a
    canonical controller search. Until temporal constraints are pushed into the
    canonical retrieval pipeline, pagination tokens are rejected for temporal
    queries instead of being silently ignored.
    """

    def __init__(self, controller: MemoryController):
        self.controller = controller

    def search(
        self,
        principal: Principal,
        query: str,
        *,
        page_size: int = 10,
        page_token: Optional[str] = None,
        lifecycles: Optional[List[Lifecycle]] = None,
        types: Optional[List[str]] = None,
        as_of: Any = None,
        known_as_of: Any = None,
    ) -> Dict[str, Any]:
        temporal_as_of = _as_date(as_of)
        temporal_known_as_of = _as_date(known_as_of)
        temporal_query = temporal_as_of is not None or temporal_known_as_of is not None

        if not temporal_query:
            return self.controller.search(
                principal,
                query,
                page_size=page_size,
                page_token=page_token,
                lifecycles=lifecycles,
                types=types,
            )

        if page_token is not None:
            raise ValueError(
                "Pagination tokens are not supported for temporal queries until "
                "temporal predicates are applied inside the canonical retrieval pipeline."
            )

        bounded_page_size = max(1, min(page_size, 100))
        pack = self.controller.search(
            principal,
            query,
            page_size=100,
            page_token=None,
            lifecycles=lifecycles,
            types=types,
        )
        results = [
            item for item in pack.get("results", [])
            if matches_temporal(
                item,
                as_of=temporal_as_of,
                known_as_of=temporal_known_as_of,
            )
        ]

        pack = dict(pack)
        pack["results"] = results[:bounded_page_size]
        pack["next_page_token"] = None
        pack["temporal"] = {
            "as_of": temporal_as_of.isoformat() if temporal_as_of else None,
            "known_as_of": temporal_known_as_of.isoformat() if temporal_known_as_of else None,
            "filter_stage": "post-controller-search",
            "pagination": "disabled",
        }
        return pack
