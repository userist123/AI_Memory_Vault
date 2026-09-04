# -*- coding: utf-8 -*-
"""Cache package init – expose LRUCache class with deterministic composite keys.

The Cache class extends LRUCache to provide isolation across principal,
query fingerprint, lifecycle filters, target‑type filters and disclosure level.
It also proxies the hit and miss counters.
"""

import hashlib
from .lru_cache import LRUCache

class Cache(LRUCache):
    """Public Cache interface used by MemoryController.

    The composite key is built from:
        principal.value, query_fp, lifecycle tuple, target‑type tuple, disclosure_level.
    This guarantees isolation and prevents cache leakage.
    """

    def _build_key(self, principal, query_fp, lifecycle=None, target_types=None, disclosure_level="metadata"):
        parts = [principal.value]
        parts.append(query_fp)
        parts.append(tuple(sorted(lifecycle)) if lifecycle else ())
        parts.append(tuple(sorted(target_types)) if target_types else ())
        parts.append(disclosure_level)
        raw = "|".join(str(p) for p in parts)
        return hashlib.sha256(raw.encode()).hexdigest()

    def get(self, principal, query_fp, lifecycle=None, target_types=None, disclosure_level="metadata"):
        key = self._build_key(principal, query_fp, lifecycle, target_types, disclosure_level)
        return super().get(key)

    def set(self, value, principal, query_fp, lifecycle=None, target_types=None, disclosure_level="metadata", ttl=None, events=None):
        key = self._build_key(principal, query_fp, lifecycle, target_types, disclosure_level)
        return super().set(value, key, ttl=ttl, events=events)



__all__ = ["Cache"]
