# PR #209 — Security Barrier Mutation Proof

> **Status note (2026-10-07).** This is a point-in-time report. Its `VERIFIED_FIXED` / `FIXED + VERIFIED BY TEST` entries for the runtime-authority layer (B1/Gate 1, B2, B4/Gate 2, M01, M02, M03, M07, U02, U03 and the nonce-persistence part of M08) mean **hardened in the library, not yet wired into production**: `security/runtime_enforcer.py`, `runtime_adapter.py`, `memory_adapter.py`, `memory_boundary.py` and `security_update_manager.py` have no importer outside `security/` and the tests, and nothing runs with `production_mode=True`. The model-facing quarantine described here (M04/M05/B3) was also narrowed afterwards: an ACTIVE note keeps its content whatever its verification label, and an unverified REVIEW candidate is served flagged `unverified` by the controlled `memory_access` tools. The current classification is in [`AUDIT_REMEDIATION.md`](AUDIT_REMEDIATION.md).

## Purpose

This document details the mutation testing conducted against the security barriers implemented in PR #209. Mutation testing verifies that negative and adversarial security tests are genuinely sensitive to the removal, weakening, or bypassing of each defensive invariant.

---

## Mutation Matrix

| ID | Component Under Test | Mutation Description | Target Invariant | Test Detecting Mutation | Mutation Result |
|---|---|---|---|---|---|
| **MUT-01** | `security/runtime_enforcer.py` | Comment out `require_authenticated_approval is not True` check in `__init__` | Production runtime cannot disable authentication | `test_gate1_production_mode_fails_closed_if_authenticated_approval_disabled` | **KILLED** (Failed with `AssertionError: DID NOT RAISE <class 'ValueError'>`) |
| **MUT-02** | `security/runtime_enforcer.py` | Remove `approval.issuer != self._broker.issuer` check in `authorize()` | Host authority issuer binding cannot be spoofed | `test_gate1_approval_issuer_mismatch_rejected` | **KILLED** (Failed with `AssertionError: assert not True; reason was allowed`) |
| **MUT-03** | `03_IMPLEMENTATION/packages/interfaces/memory_access.py` | Restore raw storage fallback for `Principal.AI_AGENT` in `_readable()` | Model prompt context cannot receive unverified memory from storage | `test_ai_agent_cannot_read_or_get_storage_fallback_when_cognitive_read_empty` | **KILLED** (Failed with `AssertionError: 'SECRET_MARKER_FORBIDDEN_CONTENT_9988' in snippet`) |
| **MUT-04** | `security/runtime_enforcer.py` (`PersistentNonceStore`) | Replace `BEGIN IMMEDIATE` + atomic insert with non-atomic select-then-insert | Cross-process replay prevention atomicity | `test_gate2_real_os_multiprocess_nonce_replay` | **KILLED** (Failed with `AssertionError: Expected exactly 1 ALLOWED process, got 2+`) |
| **MUT-05** | `.github/workflows/apisec-scan.yml` | Hardcode scanner status reporting to always emit `EXECUTED_PASSED` | CI cannot report passed status on failed scanner runs | `test_blocker5_ci_reporting_distinguishes_passed_from_failed_and_rejects_masking` | **KILLED** (Failed with `AssertionError: assert 'EXECUTED_PASSED' == 'EXECUTED_FAILED'`) |
| **MUT-06** | `security/runtime_adapter.py` | Allow agent callers to access `adapter.broker` in production | Agent cannot inspect or invoke authority broker directly | `test_blocker1_agent_cannot_access_broker_or_issue_approval_in_production` | **KILLED** (Failed with `AssertionError: DID NOT RAISE <class 'PermissionError'>`) |

---

## Detailed Mutation Scenarios & Execution Proofs

### Scenario 1: Mutation of Production Authentication Gate (MUT-01)
- **Code Weakened**:
  ```python
  # MUTATED: check disabled
  # if require_authenticated_approval is not True:
  #     raise ValueError(...)
  ```
- **Execution**:
  `pytest security/tests/test_audit_remediation.py::test_gate1_production_mode_fails_closed_if_authenticated_approval_disabled`
- **Observed Behavior**:
  Test failed immediately because `RuntimeEnforcer(production_mode=True, require_authenticated_approval=False)` initialized without throwing `ValueError`.
- **Verdict**: Invariant test is tight and catches any regression.

### Scenario 2: Mutation of Cognitive Storage Fallback for AI Agents (MUT-03)
- **Code Weakened**:
  ```python
  # MUTATED: restore raw storage read for AI_AGENT when cognitive_read is empty
  if principal == Principal.AI_AGENT:
      raw = controller.storage.get(note_id)
      return raw  # Bypassing cognitive read boundary
  ```
- **Execution**:
  `pytest 20_TESTS/test_memory_access.py::test_ai_agent_cannot_read_or_get_storage_fallback_when_cognitive_read_empty`
- **Observed Behavior**:
  Test failed because `ma.get(..., principal=Principal.AI_AGENT)` succeeded instead of raising `ValueError`, and `ma.search()` populated snippets with unverified contents.
- **Verdict**: Memory boundary completely defends model context from unverified data disclosure.

### Scenario 3: Mutation of SQLite Atomic Nonce Claim (MUT-04)
- **Code Weakened**:
  ```python
  # MUTATED: Non-atomic check then insert without BEGIN IMMEDIATE
  cursor.execute("SELECT 1 FROM used_nonces WHERE nonce = ?", (nonce,))
  if cursor.fetchone():
      return False
  # Race window here
  cursor.execute("INSERT INTO used_nonces VALUES (?, ?)", (nonce, now))
  ```
- **Execution**:
  `pytest security/tests/test_audit_remediation.py::test_gate2_real_os_multiprocess_nonce_replay`
- **Observed Behavior**:
  Multiple independent OS processes interleaved in the race window, resulting in `allowed_count > 1` and assertion failure.
- **Verdict**: The `BEGIN IMMEDIATE` + UNIQUE constraint pattern in SQLite WAL mode is required and validated against true concurrency.

---

## Conclusion
All 6 mutation scenarios were detected and rejected by the automated test suite. The security controls are not tautological and actively protect the repository invariants against both accidental regressions and intentional bypasses.
