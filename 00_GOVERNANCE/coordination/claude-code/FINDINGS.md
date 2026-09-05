# Runtime Security / Lifecycle Integrity — Findings (2026-09-05)

Owner: claude-code. Scope: `memory_controller/**`, `cognitive_core/tool_router.py`.
Did not touch: `.github/workflows/**`, P0.1/P0.3 tooling,
`00_GOVERNANCE/coordination/antigravity/**`, `PROJECT_BRAIN/PROJECT_STATE.md`,
or Claude's own P1.2/P2.1/plasticity front.

---

### F-01 — HIGH — Storage aliasing broke mutation atomicity / rollback

- **File**: `memory_controller/controller.py` (`StorageEngine.get/set`,
  `MemoryController.supersede`), `memory_controller/storage/file_engine.py`
  (`FileStorageEngine.get/set`)
- **Attack/scenario**: `supersede()`'s failure-path rollback
  (`self.storage.set(old_id, old_note_orig)`) relied on `old_note_orig =
  old_note.copy()`. `.copy()` is shallow: nested objects (`relations` list,
  `provenance` dict) are shared by reference. `old_note.setdefault("relations",
  []).append({...})` therefore mutated the SAME list `old_note_orig` pointed
  to. When the second write (to `new_id`) failed and the code rolled back
  `old_id` to `old_note_orig`, the restored note still carried the phantom
  `replaced_by` relation entry — the rollback was a no-op for that field.
  Separately, both `StorageEngine.get()` (returned the internal dict by
  reference, zero copy) and `FileStorageEngine.get()`/`set()` (shallow
  `dict(x)`) let ANY caller's in-place mutation of a nested field corrupt the
  storage engine's own cached/canonical state before any `set()`/validation
  ever ran.
- **Root cause**: shallow copies (`dict(x)`, `x.copy()`) used where
  independent, deep copies were required for the get-mutate-validate-or-abort
  pattern every mutation method relies on.
- **Fix**: `copy.deepcopy()` in both storage engines' `get()`/`set()`, and in
  `supersede()`'s rollback-snapshot construction.
- **Regression test**: `memory_controller/tests/test_mutation_atomicity_storage_aliasing.py`
  (7 tests, including a reproduction of the exact broken-rollback scenario
  via a monkeypatched flaky second write).

### F-02 — HIGH — `archive()` had no lifecycle state machine

- **File**: `memory_controller/controller.py::MemoryController.archive`
- **Attack/scenario**: any HUMAN/ADMIN could archive a note in ANY lifecycle
  (RAW, CLASSIFIED, an already-SUPERSEDED note, an already-ARCHIVED note
  again with a different `reason`) with an empty `reason` string accepted.
- **Root cause**: `archive()` never checked `note['lifecycle']` at all before
  overwriting it, unlike every other mutation method.
- **Fix**: `archive()` now requires lifecycle in `{ACTIVE, REVIEW}`, requires
  a non-empty `reason`, requires ADMIN (not plain HUMAN) to archive a
  `verification: verified` ACTIVE note, and logs `previous_lifecycle`/
  `new_lifecycle` in the audit trail. Full rationale:
  `ADR_RESPONSE_lifecycle_transition.md`.
- **Regression test**: `memory_controller/tests/test_archive_state_machine.py` (13 tests).

### F-03 — HIGH — `attest()`'s `verification_state` had no explicit whitelist

- **File**: `memory_controller/controller.py::MemoryController.attest`
- **Attack/scenario**: `attest(..., verification_state="anything")` was only
  ever rejected indirectly, by the shared jsonschema validator running
  *after* the field was already written into the local `note` dict — no
  explicit, local check existed. Confirmed exploitable pattern: a
  case-variant or near-miss string (`"Verified"`, `"verifed"`) would also
  have been rejected only by that same indirect path.
- **Root cause**: security-critical whitelist enforcement delegated entirely
  to a shared, general-purpose schema validator with no local backstop.
- **Fix**: explicit `_ALLOWED_VERIFICATION_STATES` check at the top of
  `attest()`, before any mutation of the local note copy, with an exact-match
  error message (no fuzzy matching, no auto-correct).
- **Regression test**: `memory_controller/tests/test_attest_security.py` (12 tests).

### F-04 — MEDIUM — Financial search bypassed the RAW-exclusion boundary for the in-memory storage engine

- **File**: `memory_controller/financial_search.py::MultiLayeredFinancialSearchEngine._extract_all_storage_notes`
- **Attack/scenario**: when running against the in-memory `StorageEngine`,
  this method read `self.storage.store` directly (`hasattr(self.storage,
  "store")` fast path), bypassing `.query()`'s hardcoded RAW exclusion. A
  downstream lifecycle filter inside `execute_search()` still excluded RAW
  from the final *returned* results today, so this was not (yet) a live data
  leak — but it meant RAW note content was indexed into the BM25/vector/graph
  caches, and RAW exposure depended on exactly one other filter continuing to
  exist, which section 6 of the brief explicitly flags as insufficient.
- **Root cause**: a fast-path optimization reached past the storage
  abstraction boundary instead of using the engine-agnostic `.query()`
  contract that every storage engine already enforces RAW exclusion in.
- **Fix**: removed the `.store` fast path; always routes through `.query()`.
- **Regression test**: covered by existing `test_query_raw_boundary.py`
  invariant plus this fix directly closes the boundary; no new financial-
  search-specific test suite exists in this repo to extend (none was found),
  flagged as a gap for whoever owns `financial_search.py` test coverage.

### F-05 — MEDIUM — Pagination token payload had no field-level type/bounds validation

- **File**: `memory_controller/security/pagination_token.py::PaginationToken.decode`
- **Attack/scenario**: after HMAC verification (which already prevents
  forgery), the payload was trusted as "any JSON object" with no check that
  `offset` was a non-negative int, `page_size` was in a sane range, or that
  `lifecycles`/`types` were lists of strings. Not exploitable today (nothing
  in this codebase produces such a token), but a stale/rolled-back-version
  token sharing the same secret, or a future producer bug, would have passed
  straight through.
- **Root cause**: "valid JSON dict" was treated as sufficient validation.
- **Fix**: `_validate_payload_shape()` added, checking type and bounds for
  every known field, called after signature/expiration checks.
- **Regression test**: `memory_controller/tests/test_security_matrix_gaps.py::TestPaginationTokenMalformedInput` (9 tests).

### F-06 — MEDIUM — `QueryClassifier` failed to match plural target-type nouns (GAP-011)

- **File**: `memory_controller/context/query_classifier.py`
- **Attack/scenario**: not a security defect — a correctness bug. The query
  "search verified procedures" failed to classify `target_types=["procedure"]`
  because the word-boundary regex required an EXACT singular match; a query
  ending in "procedures" (plural) never matched `\bprocedure\b`.
- **Root cause**: missing optional trailing `s`.
- **Fix**: `\b{type}s?\b` — preserves the existing word-boundary discipline
  (so "unverified" still cannot match "verified") while accepting the
  natural plural.
- **Regression test**: pre-existing `test_query_classifier.py::test_verified_is_still_detected_as_whole_word`
  now passes; no new false positives on the existing
  `test_intent_and_target_keywords_do_not_match_inside_larger_words` regression test.

### F-07 — LOW (process, not code) — Five legacy tests assumed the pre-hardening `REVIEW → ACTIVE` contract

- **Files**: `test_audit.py`, `test_authorization.py` (x2),
  `test_cache.py`, `test_milestone3_empirical_challenge.py`
- **Root cause**: tests written before the `verification == 'verified'` gate
  was added to `promote()`, never updated.
- **Fix**: see `ADR_RESPONSE_lifecycle_transition.md` — updated to either
  seed `verification: verified` directly (pure authorization/audit tests) or
  call a real `attest()` first (pipeline/concurrency tests). One new test
  added (`test_human_promote_rejected_without_verification`) to explicitly
  cover the negative case the drift had stopped testing.

### F-08 — LOW (test hygiene) — `test_query_raw_boundary_holds_for_sqlite_storage` intermittently failed on Windows

- **File**: `memory_controller/tests/test_query_raw_boundary.py`
- **Root cause**: the test's own `finally` block called `os.remove()` on the
  sqlite file without first closing the `SQLiteStorageEngine`'s connection;
  Windows keeps an exclusive handle open until `close()` is called, causing
  an intermittent `PermissionError: WinError 32`.
- **Fix**: `storage.close()` added before file removal; broadened the caught
  exception to include `PermissionError`.

---

## Not a finding (verified, already correct)

- `SQLiteStorageEngine` was already immune to F-01: `get()`/`set()`
  round-trip through `json.dumps`/`json.loads`, which always produces fresh
  objects.
- `retrieval cache` key composition (`principal`, `query_fp`, `lifecycle`
  tuple, `target_types` tuple, `disclosure_level`) was already correctly
  isolating principals before this pass; F-05/gap-closure tests confirm it
  empirically rather than changing it.
- `SupersessionEnforcer` (self-supersession, cycles, human-verified
  protection) was already correct and already tested
  (`test_supersession_phase43.py`); re-verified, not modified.
- `ToolRouter`'s reconciliation boundary
  (`_check_knowledge_reconciliation_boundary`) reads `controller.storage.get()`
  directly rather than `controller.read()` specifically *because* `read()`
  hides non-ACTIVE notes — this is intentional, documented in its own
  comment, and correct; the test that appeared to fail against it earlier
  this session (`test_reconciliation_boundary.py`) was already fixed by
  another concurrent session before this front's work began (confirmed via
  `git status` — not this session's change).
