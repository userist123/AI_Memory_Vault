import hashlib
import time
from typing import Any, Dict, Tuple, List
from memory_controller.security.utils import detect_cache_poisoning

class LRUCacheEntry:
    def __init__(self, value: Any, ttl_seconds: int):
        self.value = value
        self.expiry = time.time() + ttl_seconds
        self.last_used = time.time()

    def is_expired(self) -> bool:
        return time.time() > self.expiry

class LRUCache:
    """Deterministic LRU cache with TTL fallback.

    - max_items: hard limit for number of cache entries.
    - default_ttl: seconds after which an entry is considered stale.
    - event_map: maps event names to sets of cache keys for invalidation.
    """

    def __init__(self, max_items: int = 256, default_ttl: int = 300):
        self.max_items = max_items
        self.default_ttl = default_ttl
        self.store: Dict[str, LRUCacheEntry] = {}
        self.event_map: Dict[str, set] = {}
        self.hit_count: int = 0
        self.miss_count: int = 0

    def _make_key(self, *parts: Any) -> str:
        """Create a deterministic hash key from arbitrary serializable parts."""
        raw = "|".join(str(p) for p in parts)
        return hashlib.sha256(raw.encode()).hexdigest()

    def get(self, *key_parts: Any) -> Any:
        key = self._make_key(*key_parts)
        entry = self.store.get(key)
        if entry is None:
            self.miss_count += 1
            return None
        if entry.is_expired():
            # Remove stale entry
            del self.store[key]
            self.miss_count += 1
            return None
            
        try:
            detect_cache_poisoning(key, entry.value)
        except ValueError:
            # Poisoned cache entry, invalidate and treat as miss
            self.invalidate(*key_parts) # using invalidate handles event_map cleanup
            self.miss_count += 1
            return None
        # Update LRU order
        entry.last_used = time.time()
        self.hit_count += 1
        return entry.value

    def set(self, value: Any, *key_parts: Any, ttl: int = None, events: list = None) -> None:
        key = self._make_key(*key_parts)
        ttl_seconds = ttl if ttl is not None else self.default_ttl
        entry = LRUCacheEntry(value, ttl_seconds)
        self.store[key] = entry
        self._enforce_limits()
        if events:
            for ev in events:
                self.event_map.setdefault(ev, set()).add(key)

    def invalidate_by_event(self, event_name: str) -> None:
        keys = self.event_map.pop(event_name, set())
        for k in keys:
            self.store.pop(k, None)
    def invalidate(self, *key_parts: Any) -> None:
        """Invalidate cache entry for the given key parts.

        Removes the entry from the store and cleans up any event mappings.
        """
        key = self._make_key(*key_parts)
        self.store.pop(key, None)
        # Remove key from all event sets to avoid stale references
        for ev_keys in self.event_map.values():
            ev_keys.discard(key)

    def _enforce_limits(self) -> None:
        # Enforce max_items using LRU eviction
        if len(self.store) <= self.max_items:
            return
        # Sort by last_used ascending (oldest first)
        sorted_items: List[Tuple[str, LRUCacheEntry]] = sorted(
            self.store.items(), key=lambda item: item[1].last_used
        )
        for key, _ in sorted_items[: len(self.store) - self.max_items]:
            del self.store[key]
