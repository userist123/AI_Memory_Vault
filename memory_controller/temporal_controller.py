"""Temporal adapter for the canonical MemoryController.

This module preserves the existing controller API and adds explicit bitemporal
filtering, deterministic temporal ranking, and lineage-aware resolution for
callers that need an ``as_of`` or ``known_as_of`` view.
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

    Temporal queries currently start from a bounded canonical search, then apply
    deterministic bitemporal filtering/ranking and authorized lineage reads.
    Pagination remains disabled until these predicates are native to
    MemoryController.search().
    """

    def __init__(self, controller: MemoryController):
        self.controller = controller

    def _resolve_lineage(self, principal: Principal, results: List[Dict[str, Any]], *, as_of: Optional[date], known_as_of: Optional[date]) -> List[Dict[str, Any]]:
        resolved = list(results)
        seen = {str(item.get("id")) for item in resolved if item.get("id")}
        for item in list(results):
            if item.get("lifecycle") != Lifecycle.SUPERSEDED.value:
                continue
            successor_id = item.get("superseded_by")
            if not successor_id or successor_id in seen:
                continue
            try:
                pack = self.controller.cognitive_read(principal, successor_id)
                successor_items = list(pack.get("results", pack.get("items", [])))
            except Exception:
                continue
            successor = successor_items[0] if successor_items else None
            if successor and successor.get("lifecycle") == Lifecycle.ACTIVE.value and matches_temporal(
                successor, as_of=as_of, known_as_of=known_as_of
            ):
                successor = dict(successor)
                successor["_temporal_lineage_from"] = item.get("id")
                resolved.append(successor)
                seen.add(str(successor.get("id")))
        return resolved

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
            dict(item)
            for item in pack.get("results", [])
            if matches_temporal(
                item,
                as_of=temporal_as_of,
                known_as_of=temporal_known_as_of,
            )
        ]

        results = self._resolve_lineage(
            principal,
            results,
            as_of=temporal_as_of,
            known_as_of=temporal_known_as_of,
        )

        from .temporal_ranking import rank_temporal_notes

        results = [
            dict(item)
            for item in rank_temporal_notes(
                results,
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
            "filter_stage": "temporal-adapter",
            "ranking": "valid_from_then_extraction_date",
            "lineage": "authorized_cognitive_read",
            "pagination": "disabled",
        }
        return pack
