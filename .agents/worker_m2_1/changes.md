# Changes for Milestone 2: Storage, WAL & Audit Integrity

## Summary
Verified and hardened SQLite storage engine with WAL mode, thread-safe connection pooling, explicit PRAGMAs (`busy_timeout=5000`, `foreign_keys=ON`, `synchronous=NORMAL`), atomic `BEGIN IMMEDIATE` write transactions, recursive CTE lineage traversal (`resolve_active_lineage` with depth 50 recursion bound), SHA-256 tamper-evident cryptographic audit logging, and atomic checkpoint persistence (`os.fsync` + `os.replace` via temporary files) for working memory (`wm.json`) and active plans (`plan.json`).

## Files Modified & Added Tests

### 1. `memory_controller/tests/test_sqlite_storage.py`
- Added `test_sqlite_pragmas_explicit` to verify runtime configuration of `PRAGMA journal_mode=WAL`, `PRAGMA busy_timeout=5000`, and `PRAGMA foreign_keys=ON`.
- Added `test_sqlite_recursive_lineage_cycle_and_depth_limit` to verify recursive CTE lineage behavior on circular references (terminates safely at depth 50), deep chains (>50 hops), and fallback handling for non-existent IDs.
- Added `test_sqlite_atomic_rollback_on_failure` to verify transactional integrity and complete rollback on constraint violations.

### 2. `memory_controller/tests/test_audit.py`
- Added `test_audit_empty_and_nonexistent_log` to verify integrity checking on empty and newly initialized logs.
- Added `test_audit_tamper_prev_hash_and_deletion` to verify detection of `prev_hash` modifications and deleted log entries in the middle of the SHA-256 chain.
- Added `test_audit_corrupted_json_entry` to verify detection and reporting of corrupted or non-JSON log lines.

### 3. `cognitive_core/tests/test_planning.py`
- Added `test_active_plan_save_and_load` to verify atomic state persistence and round-trip deserialization for `ActivePlan`.
- Added `test_active_plan_load_missing` to verify graceful handling of non-existent plan state files.

## Verification Results
- `pytest memory_controller/tests/test_sqlite_storage.py memory_controller/tests/test_audit.py cognitive_core/tests/test_planning.py`: 25 passed in 0.63s.
- Full test suite `pytest`: 218 passed in 7.74s across 38 test suites with 0 failures.
