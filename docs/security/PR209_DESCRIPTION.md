# PR #209: Audit Remediation (Runtime Authority, Memory Integrity & 5 Concrete Blockers)

> **Branch**: `security/audit-remediation-2026-10`  
> **Target**: `main`  
> **Status**: **OPEN — NEVER AUTO-MERGE — REQUIRES HUMAN SECURITY REVIEW**  
> **Commit**: `124ef7347`  

---

## 1. Executive Summary

This Pull Request contains the comprehensive security remediation for audit findings across runtime authority, memory trust boundaries, unverified content quarantine, supply chain policies, and CI workflows.

In addition to audit findings **M01–M08** and the independent Red Team and Zero-Trust analysis artifacts (**U01–U08**), this revision specifically resolves the **5 concrete blockers** identified during independent code inspection:
1. **Blocker 1 (B1 — External Broker & Authority Separation)**: Forbid in-process broker creation, test/dev secrets, and agent self-approval in production mode; require explicitly injected `ApprovalBroker` ($\ge 32$ char secret) and `PersistentNonceStore`.
2. **Blocker 2 (B2 — Revision & Content Binding)**: Mandatory `revision_id` and `content_sha256` binding for state-mutating operations and tools (`patch`, `update`, `state_mutate`, `delete`, `memory_patch`, `file_writer`, etc.).
3. **Blocker 3 (B3 — Memory Access Quarantine & Snippet Hardening)**: Made `readable` strictly authoritative for model disclosure under `Principal.AI_AGENT`; eliminated fallback to `stored["content"]` in `memory_access.search()`; enforced empty snippet and get denial for quarantined/unverified notes.
4. **Blocker 4 (B4 — Multi-Process Persistent Nonce Store Atomicity)**: Upgraded `PersistentNonceStore` to SQLite WAL mode with `PRAGMA busy_timeout = 5000` and database-level `PRIMARY KEY` uniqueness, preventing parallel replay races across workers and failing closed on corruption.
5. **Blocker 5 (B5 — CI Scanner Failure Masking)**: Fixed `.github/workflows/apisec-scan.yml` and `fortify.yml` to inspect scanner step `outcome`, preventing failed or crashed scans from reporting `EXECUTED_PASSED`.

---

## 2. 5 Concrete Blockers Remediation Matrix

Detailed regression proofs with adversarial reproduction and negative test results are documented in [`docs/security/PR209_REGRESSION_PROOF.md`](docs/security/PR209_REGRESSION_PROOF.md).

| Blocker ID | Description | Affected Surface | Status | Enforcement & Verification |
|---|---|---|---|---|
| **Blocker 1** | In-process fallback for `ApprovalBroker` and missing external authority separation | `security/runtime_enforcer.py`, `security/runtime_adapter.py` | **VERIFIED_FIXED** | In `production_mode=True`, strictly requires injected `ApprovalBroker` (secret $\ge 32$ chars, rejects `test-`/`dev-`) and injected `PersistentNonceStore`. `.broker` and `issue_approval` raise `PermissionError` in production mode. Added agent-safe `request_approval(request)`. Validated by 4 adversarial tests (`test_blocker1_*`). |
| **Blocker 2** | Revision and content bindings omissible on mutating operations | `security/runtime_enforcer.py` | **VERIFIED_FIXED** | When token specifies `revision_id` or `content_sha256`, request must specify and match them. Mandatory binding enforced for all mutating operations (`patch`, `update`, `state_mutate`, `delete`) and tools (`memory_patch`, `state_mutate`, `file_writer`, `code_patch`). Missing bindings return `approval_binding_required_for_tool`. Validated by `test_blocker2_mandatory_binding_for_mutating_tools`. |
| **Blocker 3** | `memory_access.search()` reintroduces quarantined / unverified content | `03_IMPLEMENTATION/packages/interfaces/memory_access.py` | **VERIFIED_FIXED** | `readable` (from `cognitive_read`) is strictly authoritative for model disclosure under `Principal.AI_AGENT`. For unverified or quarantined notes, snippet is strictly `""`. Zero fallback to `stored["content"]` for `AI_AGENT`. `ma.get()` rejects quarantined notes. Validated by `test_quarantined_note_*`, `test_unverified_redacted_*`, and `test_blocker3_*`. |
| **Blocker 4** | Persistent nonce store multi-process atomicity and corruption handling | `security/runtime_enforcer.py` (`PersistentNonceStore`) | **VERIFIED_FIXED** | Upgraded to SQLite WAL mode with `PRAGMA busy_timeout = 5000` and atomic `INSERT INTO consumed_identifiers` guarded by `PRIMARY KEY`. Deduplication is enforced by the database engine across all operating system processes. Fails closed (returns `False`) on corrupted DB. Validated by `test_blocker4_sqlite_nonce_store_multi_instance_atomicity` (8-thread race across 2 enforcers) and `test_blocker4_corrupted_nonce_store_fails_closed`. |
| **Blocker 5** | CI success reporting masks scanner failure | `.github/workflows/apisec-scan.yml`, `fortify.yml` | **VERIFIED_FIXED** | Added explicit step IDs (`id: apisec_scan`, `id: fortify_scan`). Final reporting step inspects step `outcome`: `EXECUTED_PASSED` is emitted ONLY if scanner succeeded (`outcome == 'success'`). Failed scanner emits `EXECUTED_FAILED`. Unconfigured emits `NOT_CONFIGURED_OPTIONAL`. Validated by `test_apisec_and_fortify_scan_outcome_checked_before_reporting_passed` and `test_blocker5_*`. |

---

## 3. Findings Matrix & Current Classification

| ID | Title | Severity | Status | Notes |
|---|---|---|---|---|
| **M01** | Cryptographic Approval Broker | P0 | **VERIFIED_FIXED** | HMAC-SHA256, persistent SQLite WAL nonces, tamper-proof tokens |
| **M02** | Trust State Fail-Closed Gate | P1 | **VERIFIED_FIXED** | Quarantines unverified/unknown notes fail-closed |
| **M03** | Rollback & Integrity Failures | P1 | **VERIFIED_FIXED** | Emits `INTEGRITY_FAILURE` if atomic rollback fails |
| **M04** | Context Pack Sanitization | P1 | **VERIFIED_FIXED** | Models receive `content=""` for unverified notes |
| **M05** | Memory Disclosure Invariant | P1 | **VERIFIED_FIXED** | Blank content enforced across all disclosure levels & search snippets |
| **M06** | Skill Exfiltration Scanner | P1 | **VERIFIED_FIXED** | Detects outbound exfiltration regexes in imported skills |
| **M07** | Mandatory Production Controls | P1 | **VERIFIED_FIXED** | `production_mode=True` blocks bypasses fail-closed |
| **M08** | In-Memory Replay Persistence | P2 | **VERIFIED_FIXED** | `PersistentNonceStore` tracks nonces across restarts and processes |
| **B01** | Positive evaluation results can be synthetic | P0 | **PARTIALLY FIXED** | Promotion of synthetic evidence to ACTIVE/verified blocked on `main`. PR #206 isolated. |
| **B02** | HMAC token revision & content binding | P0 | **VERIFIED_FIXED** | Strict matching + mandatory binding for mutating operations/tools |
| **B03–B10** | Empirical evaluation gaps on research branch | P1 | **REMAINS OPEN** | Isolated on PR #206 (`research/book-to-memory`); not present on `main` |
| **B11** | Graph cleanup rewrites note body prose | P1 | **VERIFIED_FIXED** | Preserves note body bytes identically; zero semantic injections |
| **B12** | Severity downgrade from HARD_BLOCKER | P1 | **PARTIALLY FIXED** | `validate_severity_transition()` prevents downgrade on `main` |
| **U01** | AI Agent Direct Active Mutation | P0 | **VULNERABLE** | Downstream architectural question under review by vault owner |
| **U02** | Omitted Hash Check in Enforcer | P1 | **VERIFIED_FIXED** | Bound in `RuntimeEnforcer.authorize()` |
| **U03** | `issue_approval` on `RuntimeAdapter` | P1 | **VERIFIED_FIXED** | Blocked in production mode; requires external authority |
| **U04** | Unauthenticated REST API Server | P0 | **VULNERABLE** | Unauthenticated port 8000 route marked as retired/unsupported |
| **U05** | Promotion Verification Gate Flag | P1 | **VULNERABLE** | Architectural flag awaiting owner review |
| **U06** | Unpinned Python Dependencies | P2 | **VULNERABLE** | Requirements lockfile cryptographic hash pinning open |
| **U07** | Windows Host NTFS DENY ACLs | P1 | **UNVERIFIED** | Host-level ACL enforcement pending PR #207 merge |
| **U08** | Unbounded Note Size DoS | P2 | **VULNERABLE** | Input size guard implemented in `memory_access`, core pending |

---

## 4. Files Modified in this PR

```text
.github/workflows/apisec-scan.yml
.github/workflows/fortify.yml
03_IMPLEMENTATION/packages/interfaces/memory_access.py
20_TESTS/memory_vault_fixture.py
20_TESTS/regression/test_workflow_security_audit.py
20_TESTS/test_memory_access.py
docs/security/AUDIT_REMEDIATION.md
docs/security/PR209_REGRESSION_PROOF.md
security/runtime_adapter.py
security/runtime_enforcer.py
security/tests/test_audit_remediation.py
```

---

## 5. Empirical Test Execution Proof

Automated tests executed directly on Windows with Python 3.14.2:

```text
pytest security/tests 20_TESTS/test_import_external_skills.py 20_TESTS/test_memory_access.py 20_TESTS/test_cognitive_core_search_wiring.py 20_TESTS/test_end_to_end_workflow.py 20_TESTS/regression/test_workflow_security_audit.py
============================= 221 passed in 2.33s =============================

pytest 20_TESTS/memory_controller/
============================= 328 passed in 7.56s =============================

Total Verified Test Suite:
============================= 549 passed, 0 failed, 0 skipped, 0 errors =============================
```

---

## 6. Limitations & Required External Configurations

1. **Owner Authority Key Storage**: In production deployments, `ApprovalBroker` requires a secure HMAC secret ($\ge 32$ chars) injected via an environment variable (`VAULT_APPROVAL_SECRET`) or hardware-backed keystore, inaccessible to the agent.
2. **Persistent Nonce Store Storage**: The SQLite WAL database must reside on local persistent storage accessible across local worker processes, protected with restricted OS permissions (`chmod 600` / NTFS Owner-only ACLs).
3. **CI External Secrets**: GitHub Actions scanners (`Fortify`, `APIsec`) require credentials configured in repository secrets. When credentials are not configured, workflows report `SKIPPED_UNCONFIGURED` / `NOT_CONFIGURED_OPTIONAL` without failing CI or falsifying passed status.
4. **PR Isolation**: PRs `#204`, `#206`, `#207`, and `#208` remain completely untouched and open. PR #206 (`research/book-to-memory`) is isolated from `main`.

---

## 7. Protected PRs Status

- **PR #204**: Intact, unchanged, open.
- **PR #206**: Intact, unchanged, open.
- **PR #207**: Intact, unchanged, open.
- **PR #208**: Intact, unchanged, open.
- **PR #209**: Remediated, updated, open for human review. **DO NOT AUTO-MERGE.**
