"""Temporal adapter for the canonical MemoryController.

Adds explicit bitemporal filtering, deterministic ranking, lineage-aware
resolution and signed pagination for temporal queries.
"""

from __future__ import annotations

import hashlib
import json
import os
from datetime import date, datetime, timedelta, timezone
from typing import Any, Dict, List, Optional

from .authorizer import Principal
from .controller import Lifecycle, MemoryController
from .security.pagination_token import PaginationToken, InvalidPaginationTokenError, MissingHMACSecretError


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


def _query_fingerprint(query: str, *, as_of: Optional[date], known_as_of: Optional[date],
                       lifecycles: Optional[List[Lifecycle]], types: Optional[List[str]],
                       page_size: int) -> str:
    payload = {
        "query": query,
        "as_of": as_of.isoformat() if as_of else None,
        "known_as_of": known_as_of.isoformat() if known_as_of else None,
        "lifecycles": [x.value if isinstance(x, Lifecycle) else x for x in (lifecycles or [])],
        "types": types or [],
        "page_size": page_size,
    }
    return hashlib.sha256(json.dumps(payload, sort_keys=True, separators=(",", ":")).encode()).hexdigest()


class TemporalMemoryController:
    """Compatibility wrapper around the canonical MemoryController.

    Temporal queries begin from the canonical controller search, then apply
    deterministic bitemporal filtering/ranking and authorized lineage reads.
    Pagination uses an HMAC-signed cursor bound to the full temporal query.
    """

    def __init__(self, controller: MemoryController):
        self.controller = controller

    def _resolve_lineage(self, principal: Principal, results: List[Dict[str, Any]], *,
                         as_of: Optional[date], known_as_of: Optional[date]) -> List[Dict[str, Any]]:
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

    def search(self, principal: Principal, query: str, *, page_size: int = 10,
               page_token: Optional[str] = None, lifecycles: Optional[List[Lifecycle]] = None,
               types: Optional[List[str]] = None, as_of: Any = None, known_as_of: Any = None) -> Dict[str, Any]:
        temporal_as_of = _as_date(as_of)
        temporal_known_as_of = _as_date(known_as_of)
        temporal_query = temporal_as_of is not None or temporal_known_as_of is not None

        if not temporal_query:
            return self.controller.search(principal, query, page_size=page_size, page_token=page_token,
                                          lifecycles=lifecycles, types=types)

        bounded_page_size = max(1, min(page_size, 100))
        fingerprint = _query_fingerprint(query, as_of=temporal_as_of, known_as_of=temporal_known_as_of,
                                         lifecycles=lifecycles, types=types, page_size=bounded_page_size)
        offset = 0
        if page_token is not None:
            secret = os.getenv("MEMORY_CONTROLLER_HMAC_SECRET")
            if not secret:
                raise MissingHMACSecretError("HMAC secret not configured in MEMORY_CONTROLLER_HMAC_SECRET")
            payload = PaginationToken.decode(page_token)
            if payload.get("temporal_query") is not True:
                raise InvalidPaginationTokenError("Token is not a temporal pagination token")
            if payload.get("principal") != principal.value:
                raise InvalidPaginationTokenError("Token principal does not match current request")
            if payload.get("fingerprint") != fingerprint:
                raise InvalidPaginationTokenError("Token temporal query does not match current request")
            offset = int(payload.get("offset", 0))

        pack = self.controller.search(principal, query, page_size=100, page_token=None,
                                      lifecycles=lifecycles, types=types)
        results = [dict(item) for item in pack.get("results", [])
                   if matches_temporal(item, as_of=temporal_as_of, known_as_of=temporal_known_as_of)]
        results = self._resolve_lineage(principal, results, as_of=temporal_as_of, known_as_of=temporal_known_as_of)

        from .temporal_ranking import rank_temporal_notes
        results = [dict(item) for item in rank_temporal_notes(results, as_of=temporal_as_of,
                                                               known_as_of=temporal_known_as_of)]

        total = len(results)
        end = min(offset + bounded_page_size, total)
        page_results = results[offset:end]
        next_token = None
        if end < total:
            secret = os.getenv("MEMORY_CONTROLLER_HMAC_SECRET")
            if not secret:
                raise MissingHMACSecretError("HMAC secret not configured in MEMORY_CONTROLLER_HMAC_SECRET")
            payload = {
                "temporal_query": True,
                "principal": principal.value,
                "fingerprint": fingerprint,
                "offset": end,
                "page_size": bounded_page_size,
                "expiration": int((datetime.now(timezone.utc) + timedelta(minutes=10)).timestamp()),
            }
            next_token = PaginationToken(payload, secret.encode()).encode()

        pack = dict(pack)
        pack["results"] = page_results
        pack["next_page_token"] = next_token
        pack["temporal"] = {
            "as_of": temporal_as_of.isoformat() if temporal_as_of else None,
            "known_as_of": temporal_known_as_of.isoformat() if temporal_known_as_of else None,
            "filter_stage": "temporal-adapter",
            "ranking": "valid_from_then_extraction_date",
            "lineage": "authorized_cognitive_read",
            "pagination": "signed_temporal_cursor",
        }
        return pack
