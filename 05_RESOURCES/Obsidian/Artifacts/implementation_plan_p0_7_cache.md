---
id: "art-44cd13c8"
type: artifact
lifecycle: ACTIVE
category: conversation-artifact
tags: [artifact, obsidian-sync, conversation-evidence]
created: 2026-08-24T21:30:00Z
updated: 2026-08-24T18:31:36.389103+00:00
provenance:
  source_type: execution
  source_ref: "implementation_plan_p0_7_cache.md"
confidence: high
verification: verified
relations: []
---

# Artifact: implementation_plan_p0_7_cache

# Implementation Plan – Cache Layer (P0‑7)

## Goal
Build a secure, deterministic cache layer that satisfies the P0‑7 contract while integrating with the existing Context Economy (P0‑6). No changes to canonical Vault documents, `06_INBOX/RAW_IMPORTS`, or existing production behavior beyond what is required.

## Current State (Read‑Only Audit)
- **Cache package** (`memory_controller/cache`):
  - `Cache` is an alias for `LRUCache` (simple LRU with TTL, event map, deterministic SHA‑256 key generation).
  - `LRUCacheEntry` stores `value`, `expiry`, `last_used`.
  - Public API: `get(*key_parts)`, `set(value, *key_parts, ttl=None, events=None)`, `invalidate_by_event(event_name)`, `invalidate(*key_parts)`, `_enforce_limits()`.
- **RetrievalEngine** builds a cache key from `[intent, tuple(sorted(lifecycle)), tuple(sorted(target_types))]` and caches raw note lists.
- **Security utilities** provide `sanitize_query` and pagination token handling; tokens already bind `agent_id`, `query_fp`, lifecycle, types, disclosure, page size, offset, and expiration.
- **Authorizer/Principal** defines principals (`AI_AGENT`, `HUMAN`, `ADMIN`).
- **Existing tests** cover core functionality but contain no cache‑specific expectations.

## Missing P0‑7 Behaviours
| Requirement | Gap | Desired Behaviour |
|---|---|---|
| HIT / MISS accounting | No counters. | Expose read‑only `hit_count` and `miss_count` on the cache instance.
| TTL enforcement | Supported internally, but no explicit test. | Expired entries must be treated as a MISS and removed on access.
| Event‑based invalidation | `invalidate_by_event` exists but not hooked to mutations. | Emit events (`note_created`, `note_updated`, `note_deleted`, `lifecycle_changed`) from `MemoryController` to invalidate relevant cache entries.
| Budget mismatch detection | Retrieval caps `max_notes` but does not re‑validate cached results against the caller's `ContextBudget`. | On a cache hit, verify the cached notes satisfy the current soft/hard budget; otherwise treat as a MISS and recompute.
| Principal isolation | Cache key lacks principal. | Include the principal identifier as the first component of the cache key.
| Query fingerprint isolation | Uses raw `intent`. | Use the canonical SHA‑256 fingerprint of the sanitized query (`sanitize_query`) as part of the key.
| Disclosure level isolation | Not part of key; different disclosure levels yield different payloads. | Append the disclosure level to the cache key.
| Target‑type isolation | Already present.
| Cache poisoning protection | No explicit safeguards. | The combined key (principal + query fingerprint + lifecycle + types + disclosure) guarantees no cross‑principal leakage.
| Compatibility with Context Economy | Cached results are raw notes; the degradation pipeline is applied only after retrieval. | After a cache hit, run the same progressive‑disclosure and budget enforcement steps as for a fresh retrieval.

## Test‑First Development Plan
1. **Create `memory_controller/tests/test_cache.py`** covering:
   - **Miss** on first identical query.
   - **Hit** on second identical query (same principal, query, lifecycle, disclosure, types).
   - **TTL**: set TTL = 0 (immediate expiry) via `cache.set(..., ttl=0)` and verify second fetch is a MISS.
   - **Counters**: assert `cache.hit_count`/`miss_count` reflect the sequence of operations.
   - **Event invalidation**: after `controller.propose` (note creation) emit `note_created` event; identical query thereafter must be a MISS.
   - **Principal isolation**: Agent A caches a result, Agent B runs the same query → should be a MISS and return its own result.
   - **Disclosure isolation**: same query with `default_disclosure='full'` vs `'metadata'` creates separate cache entries.
   - **Budget mismatch**: cache a result that would exceed a tighter soft budget; subsequent request with that tighter budget must trigger a recompute (treated as MISS).
   - **Cache poisoning**: ensure no data leakage across principals or differing filters.
   - **Pagination token interaction**: confirm that a token created for Agent A cannot be used by Agent B (existing token tests already cover this, but we verify the cache does not leak.
2. **Fixture** to build a fresh `MemoryController` with its own `Cache` and `StorageEngine` for each test.
3. **No new dependencies** – TTL test uses `ttl=0` to force immediate expiry.

## Production Changes (Minimum Required)
- **`memory_controller/cache/__init__.py`**
  - Extend `Cache` (subclass of `LRUCache`) with read‑only properties `hit_count` and `miss_count` that proxy internal counters.
- **`memory_controller/cache/lru_cache.py`**
  - Add `self.hit_count = 0` and `self.miss_count = 0` in `__init__`.
  - In `get`, increment `hit_count` on a successful non‑expired hit; increment `miss_count` on miss or expired entry.
- **`memory_controller/context/retrieval.py`**
  - Change `retrieve(self, classified_query, principal, disclosure_level)` signature.
  - Build cache key parts as:
    ```python
    cache_key_parts = [principal.value,
                       intent,
                       tuple(sorted(lifecycle)),
                       tuple(sorted(target_types)),
                       disclosure_level]
    ```
  - Pass `principal` and `disclosure_level` from the caller.
- **`memory_controller/controller.py`**
  - In `search`, after classification, obtain the current disclosure level (`self.default_disclosure` or `'metadata'`).
  - Call `self.retrieval_engine.retrieve(classified, principal, disclosure_level)`.
  - After obtaining notes (whether from cache or fresh), run the progressive‑disclosure, budget enforcement, and provenance‑retention steps exactly as currently done for fresh results. This guarantees cached results are compatible with P0‑6.
  - Emit events after mutations:
    - `self.cache.invalidate_by_event('note_created')` after `propose`.
    - `self.cache.invalidate_by_event('note_updated')` after `update`.
    - `self.cache.invalidate_by_event('note_deleted')` after `archive`.
    - `self.cache.invalidate_by_event('lifecycle_changed')` after `review` and `promote`.
  - When caching query results (`self.cache.set`), include `events=['note_created','note_updated','note_deleted','lifecycle_changed']` as appropriate (use a superset `'memory_updated'` for backward compatibility if other code relies on it).
- **`memory_controller/security.py`** (optional helper)
  - Add `def cache_key(principal, sanitized_query, lifecycles, types, disclosure):` that returns the same deterministic SHA‑256 hash used for pagination fingerprint, ensuring a single source of truth.
- **No modifications to `cache.py`** beyond the minimal counter additions and key handling; we keep the existing LRU/TTL logic.

## Risk Assessment & Mitigations
- **Breaking existing retrieval semantics** – The default disclosure level remains `'metadata'`; cache key now includes disclosure, so existing callers will continue to hit the cache when appropriate.
- **Performance impact** – Adding a principal and disclosure to the key adds negligible overhead.
- **Event naming** – Preserve the existing `'memory_updated'` event for compatibility while adding more granular events.
- **TTL testing without time‑mocking** – Using `ttl=0` guarantees immediate expiry.
- **No new dependencies** – All changes rely solely on the standard library.

## Verification Plan
1. **Automated tests** – Run `python -m pytest -q` after implementing changes. All existing 48 tests must stay green; the new `test_cache.py` must pass.
2. **Manual spot‑check** – Use the controller to run a query twice and verify the cache hit/miss counters.
3. **Git status** – Ensure no commits are created; only new test files and modifications to cache files appear as changed.
4. **RAW_IMPORTS integrity** – Verify the `06_INBOX/RAW_IMPORTS` directory is untouched before and after the test run.

---
*All actions will be performed after your explicit approval of this implementation plan.*

