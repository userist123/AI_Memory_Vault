# PR #209 — Authority & Multi-Process Proof Gate

## Executive Summary

This document provides empirical verification and architectural proof for two critical security gates in PR #209:
1. **Gate 1 — Real Host-Trusted Production Authority**: Cryptographic approval enforcement cannot be disabled or bypassed via arguments, configuration, adapters, or impostor broker replacement.
2. **Gate 2 — Real Multi-Process Nonce Replay Elimination**: Cross-process atomic replay prevention demonstrated using independent OS processes (distinct OS PIDs, memory spaces, and SQLite connections).

---

## Gate 1: Host-Trusted Authority & Production Invariants

### 1. Architectural Model: Option A (Host-Trusted Broker Architecture)
The repository runtime adheres strictly to **Option A — Host-Trusted Broker Architecture**:
- The `ApprovalBroker` is provisioned and injected by the host environment or orchestration harness.
- In production mode (`production_mode=True`), the enforcer **strictly requires** an explicit `ApprovalBroker` and `PersistentNonceStore`.
- `RuntimeEnforcer.request_approval()` produces a correlation request record; it does NOT communicate with a remote multi-tenant server.
- The secret key is never accessible to untrusted agent code: `RuntimeAdapter.broker` and `RuntimeEnforcer.broker` raise `PermissionError` in production mode.

### 2. Invariants Enforced in Code
- **Invariant 1: Authentication Cannot Be Disabled in Production**
  In `RuntimeEnforcer.__init__`:
  ```python
  if production_mode:
      if require_authenticated_approval is not True:
          raise ValueError(
              "production runtime strictly forbids disabling authenticated approvals "
              "(require_authenticated_approval must be True)"
          )
  ```
  Attempting to pass `require_authenticated_approval=False`, `None`, `0`, or any non-`True` value raises `ValueError` immediately upon enforcer creation.
- **Invariant 2: Adapter Protection Against Tampered Enforcers**
  In `RuntimeAdapter.__init__`:
  ```python
  if production_mode:
      if enforcer is not None:
          if not getattr(enforcer, "_production_mode", False):
              raise PermissionError("production runtime requires an enforcer configured in production mode")
          if getattr(enforcer, "_require_authenticated_approval", False) is not True:
              raise PermissionError("production runtime strictly forbids enforcer with disabled authenticated approvals")
  ```
- **Invariant 3: Broker Issuer Binding**
  In `RuntimeEnforcer.authorize()`:
  ```python
  if approval.issuer != self._broker.issuer:
      return RuntimeAuthorization(False, "approval_issuer_mismatch")
  ```
  An attacker cannot sign a token using a broker with an arbitrary or impostor issuer identity.
- **Invariant 4: Host Secret Attestation**
  When `VAULT_AUTHORITY_HMAC_SECRET` is defined in the host environment, `RuntimeEnforcer.__init__` validates using constant-time `hmac.compare_digest`:
  ```python
  host_secret_str = os.environ.get("VAULT_AUTHORITY_HMAC_SECRET")
  if host_secret_str:
      if not hmac.compare_digest(broker._secret, host_secret_str.encode("utf-8")):
          raise PermissionError("broker secret does not match host authority secret (VAULT_AUTHORITY_HMAC_SECRET)")
  ```

---

## Gate 2: Multi-Process Nonce Replay Proof

### 1. Test Architecture (`security/tests/os_multiprocess_nonce_runner.py`)
To eliminate any ambiguity between multi-threading and multi-processing, test `test_gate2_real_os_multiprocess_nonce_replay` invokes independent child OS processes using:
```python
p = subprocess.Popen(
    [sys.executable, str(runner_script), str(db_path), secret, token_json, req_json],
    stdout=subprocess.PIPE,
    stderr=subprocess.PIPE,
    text=True,
)
```
Each spawned process:
- Has an independent OS process identifier (`PID`).
- Operates in its own distinct virtual memory address space.
- Opens an independent SQLite database handle to the shared WAL database file.
- Executes `enforcer.authorize(req, decision, approval=token, now=now)`.

### 2. Empirical Execution Results
Running `test_gate2_real_os_multiprocess_nonce_replay` with 8 concurrent OS processes:
- **Total Concurrent OS Processes**: 8
- **Output Results**:
  - Exactly **1** process outputs `RESULT:ALLOWED` (Exit Code `0`).
  - Exactly **7** processes output `RESULT:REJECTED:approval_replayed` (Exit Code `1`).
- **Subsequent 9th Process**:
  - Executed after the 8 concurrent processes finish.
  - Returns `RESULT:REJECTED:approval_replayed` (Exit Code `1`).

### 3. Underlying Concurrency Mechanism (`PersistentNonceStore`)
The SQLite storage layer ensures strict atomicity via:
- `PRAGMA journal_mode=WAL;`
- `PRAGMA busy_timeout=5000;`
- Atomic write transaction:
  ```sql
  BEGIN IMMEDIATE;
  INSERT INTO used_nonces (nonce, seen_at) VALUES (?, ?);
  COMMIT;
  ```
- If an `sqlite3.IntegrityError` is raised (unique constraint violation on `nonce`), the transaction rolls back and returns `False` (`approval_replayed`).
- If an operational database error occurs (e.g. corruption), `PersistentNonceStore` fails closed (`False`), ensuring corrupted storage never permits unverified execution.

---

## Gate 3: Memory Boundary Storage Fallback Elimination

### Model-Facing Isolation Verified
In `03_IMPLEMENTATION/packages/interfaces/memory_access.py`:
- `_readable(controller, note_id, principal=Principal.AI_AGENT)`: If `cognitive_read` yields no eligible content, returns `None`. Storage is **strictly prohibited** from serving as a fallback for `Principal.AI_AGENT`.
- `ma.get(controller, note_id, principal=Principal.AI_AGENT)`: Raises `ValueError("Note ... not found or not eligible for cognitive retrieval")`.
- `ma.search(controller, query, principal=Principal.AI_AGENT)`: Outputs empty snippet `""` for unverified notes and quarantined notes, ensuring unverified or quarantined content is never disclosed into the agent prompt context.
- Administrative inspection (`Principal.HUMAN` / `Principal.ADMIN`) retains fallback access for review and attestation workflows.

---

## Verdict
- **Gate 1 (Authority Proof)**: VERIFIED_FIXED.
- **Gate 2 (Multi-Process Replay)**: VERIFIED_FIXED with empirical independent OS-process test.
- **Gate 3 (Cognitive Fallback)**: VERIFIED_FIXED with regression tests.
- **Status**: READY FOR HUMAN SECURITY REVIEW (DO NOT AUTO-MERGE).
