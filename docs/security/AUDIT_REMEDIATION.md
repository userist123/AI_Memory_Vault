# Audit Remediation Report — Runtime Authority & Memory Integrity

**Branch**: `security/audit-remediation-2026-10`  
**Base Commit**: `69afe312a8cf9f780ff18ecc293c3c0ff099fb80` (`main`)  
**Scope**: In-depth remediation of static and dynamic audit findings across runtime authority, memory trust boundaries, unverified content quarantine, supply chain policies, and CI workflows.  
**PR Policy Adherence**: In accordance with explicit governance directives, PRs `#204`, `#206`, `#207`, and `#208` **were NOT modified or closed**. Findings on PR `#206`'s branch (`origin/research/book-to-memory`) remain isolated and are tracked as `REMAINS OPEN` on PR #206, while `main` has been hardened fail-closed against admitting unverified or synthetic claims.

---

## 1. Executive Summary & Adversarial Assessment

The audit revealed that prior security claims ("fail-closed", "unforgeable approvals", "verified empirical learning") exceeded the actual enforcement provided by the implementation. While structural defenses were present, concrete bypasses existed:
1. **Unauthenticated Approvals**: Runtime approvals lacked cryptographic signature verification and could be constructed by an agent if exposed.
2. **Missing Deny-by-Default on Trust States**: Unknown trust states (e.g., `"UNKNOWN"`, `None`, `""`) bypassed negative checks in memory boundaries.
3. **Non-Transactional Writes**: Memory persistence preceded ledger commit confirmation without rollback, leaving orphaned data on commit failures.
4. **Quarantine Leaks to Models**: Unverified items retained raw content strings when returned from progressive disclosure and data routing.
5. **Supply Chain Gaps & Incomplete CI Reporting**: Missing provenance could be ignored when policies were configured, while CI jobs masked unconfigured scans as successful.
6. **Synthetic Metrics & Semantic Body Rewriting**: B01–B10/B12 flaws on PR #206 branches relied on synthetic evaluation fallbacks; B11 on `main` rewrote note body prose to avoid graph islands.

All issues present on `main` have been remediated with fail-closed cryptographic and transactional boundaries, backed by **208 passing security/regression tests** (including 20 dedicated regression and adversarial self-audit tests) and **536 passing tests** across the wider vault test suite.

> **Correction (2026-10-07, review of this branch).** Two statements above were stronger than the code, and the tables below are corrected accordingly.
>
> 1. **Hardened in library, not yet wired into production.** The runtime-authority layer (`security/runtime_enforcer.py` with the HMAC `ApprovalBroker` and the SQLite `PersistentNonceStore`, `security/runtime_adapter.py` and its `production_mode`, `security/memory_adapter.py`, `memory_boundary.py`, `memory_integrity.py`, `security/security_update_manager.py`) is real code with tests, but a repo-wide search finds no importer outside `security/` and the tests, and nothing constructs `RuntimeAdapter`, `RuntimeEnforcer` or `ApprovalBroker` with `production_mode=True`. By the Global Production-Consumer Rule in `CLAUDE.md` it hardens nothing at runtime until a tool-execution path calls it. Findings that live only there carry the label **HARDENED IN LIBRARY, NOT YET WIRED INTO PRODUCTION** (B1/Gate 1, B2/Blocker 2, B4/Gate 2, M01, M02, M03, M07, U02, U03, and the nonce-persistence part of M08). Wiring it is a separate change; it was deliberately not done here. `20_TESTS/test_vault_state_accuracy.py::test_runtime_authority_layer_has_no_production_consumer` fails the day one of those modules gains a production consumer, so this note gets corrected rather than going stale. The findings that are enforced where the production code runs (U01, U04, U05, U06, U08, B11, the memory read path M04/M05/B3, the importer M06, the CI reporting of M08/B5) keep their status.
> 2. **The quarantine of model-facing content was too broad.** "Unverified" is a label, not a reason to hide an ACTIVE note. The first version of M04/M05/B3 withheld the body of every note that was not `verified`, which cut the non-empty snippets an agent gets from the real vault from 38/48 to 2/48 and made `memory_get` fail on every REVIEW note, against the documented contract (`CLAUDE.md`: `memory_get` serves ACTIVE and REVIEW notes, REVIEW marked unverified). The rule is now: withheld are a note flagged `quarantined`, ARCHIVED/RAW notes, and the body of an unverified REVIEW candidate inside a trusted context pack; the controlled `memory_access` tools hand the REVIEW candidate over, flagged `unverified`. Real vault, 10 queries x top 5: 48/48 snippets and 48/48 `memory_get` (main 38/48 and 38/48).

---

## 2. Findings Matrix & Exact Classification

Classification tiers:
- `FIXED`: Remediated in code.
- `FIXED + VERIFIED BY TEST`: Remediated in code and validated with passing unit/regression tests.
- `PARTIALLY FIXED`: Remediated on `main` / core boundary, but upstream experimental branches retain open items.
- `UNVERIFIED`: Proposed or theoretical fix not yet validated by automated tests.
- `REMAINS OPEN`: Open on unmerged branches (e.g., PR #206).

| Finding ID | Finding Title | Priority | Target Surface | Status | Summary of Fix & Verification |
|---|---|---|---|---|---|
| **M01** | Runtime approvals do not authenticate the owner | P0 | `main` | **HARDENED IN LIBRARY, NOT YET WIRED INTO PRODUCTION** | Library only: no production consumer (see the correction note above). Added `ApprovalBroker` HMAC-SHA256 signing; `ApprovalToken` requires valid signature; `RuntimeEnforcer.authorize` verifies signature, parameter SHA256, expiration, and nonces. Added `PersistentNonceStore` preventing replay across process restarts. Verified by `test_m01_b02_*` and `test_m01_persistent_nonce_store_replay_across_restarts`. |
| **M02** | Unknown trust states accepted at write boundary | P1 | `main` | **HARDENED IN LIBRARY, NOT YET WIRED INTO PRODUCTION** | Library only: no production consumer (see the correction note above). Implemented `normalize_trust_state()`; explicit allowlist check in `MemoryWriteBoundary.prepare()` & `MemoryLedger.commit_proposal()`. Unknown, None, empty string rejected fail-closed with zero backend persistence. Verified by `test_m02_unknown_trust_state_denied_fail_closed`. |
| **M03** | Persistence precedes integrity commit (no rollback) | P1 | `main` | **HARDENED IN LIBRARY, NOT YET WIRED INTO PRODUCTION** | Library only: no production consumer (see the correction note above). Added `rollback` callable to `MemoryAdapter`. If ledger commit fails or rejects, persistence is automatically rolled back, emitting `MEMORY_ROLLBACK`. If rollback fails or is missing when dirty writes occurred, `MemoryIntegrityError` (`INTEGRITY_FAILURE`) is raised and audited, preventing normal DENY masking. Verified by `test_m03_*`. |
| **M04** | Quarantine is labeled, not completely isolated from models | P1 | `main` | **FIXED + VERIFIED BY TEST** (scope corrected, see the note above) | `ContextPackBuilder._verify_and_reduce` and `MemoryDataEgressGate` share one predicate, `security.verified_reduction.content_withheld_from`: for a non-owner caller they blank `content`/`snippet` and set `model_egress = False` for a note flagged `quarantined` and for an unverified REVIEW candidate. An ACTIVE note that is merely not `verified` keeps its content (`model_egress = True`) and keeps its `UNVERIFIED_QUARANTINED` label. Owners are `human` and `admin` in both layers (`OWNER_PRINCIPALS`); the builder used to test `!= "human"` while the router used `{human, admin}`. Verified by `test_m04_*` in `security/tests/test_audit_remediation.py`. |
| **M05** | Alternative getters bypass egress boundary | P1 | `main` | **FIXED + VERIFIED BY TEST** (scope corrected, see the note above) | `graph/activation.py` and `memory/working_memory.py` blank the body of an unverified REVIEW node for `Principal.AI_AGENT`. `interfaces/memory_access.py` serves ACTIVE and REVIEW notes (REVIEW flagged `unverified`) and refuses a `quarantined` note; it reads storage only for a REVIEW candidate that is not quarantined, never for ARCHIVED/RAW or quarantined notes. Verified by `20_TESTS/test_memory_access.py` (fixture with the real mix of states) and `test_blocker3_*`. |
| **M06** | Importer has structural incompatibility with `.git` and extensions | P1 | `main` | **FIXED + VERIFIED BY TEST** | `import_external_skills.py` prunes the checkout's own top-level `.git` and fails closed on everything else that is dangerous: a script or binary, a hidden path, an executable bit, a symlink or a traversal aborts the import, every offending path is listed in one error and nothing is copied. A file that is none of those but not an imported type (image, `LICENSE`) is left out and listed in `SKIPPED_FILES.json`; nothing is dropped silently. (The first version of this fix skipped scripts and hidden files silently.) Verified by `test_import_external_skills.py` and `test_m06_*`. |
| **M07** | Supply-chain policy can be bypassed if provenance is omitted | P1 | `main` | **HARDENED IN LIBRARY, NOT YET WIRED INTO PRODUCTION** | Library only: no production consumer (see the correction note above). `SecurityUpdateManager.evaluate()` fails closed with `SECURITY_UPDATE_REJECTED` if `provenance_policy` is active but `provenance` is None. `RuntimeAdapter(production_mode=True)` enforces mandatory broker and supply chain policy. Verified by `test_security_update_manager_provenance.py` and `test_m07_*`. |
| **M08** | CI green status can mask unexecuted scans | P2 / P1 | `main` | **FIXED + VERIFIED BY TEST** (CI reporting; the nonce-persistence part is **HARDENED IN LIBRARY, NOT YET WIRED INTO PRODUCTION**) | Updated `ai-security-update-watch.yml`, `apisec-scan.yml`, and `fortify.yml` to emit explicit distinct status outputs: `EXECUTED_PASSED`, `SKIPPED_UNCONFIGURED`, or `ADVISORY_FAILED`. Fixed path filters in `jarvis-command-center.yml` to include `03_IMPLEMENTATION/packages/memory/**`. Pinned all test runners to `pytest==9.1.1`. |
| **B01** | Positive evaluation results can be synthetic | P0 | PR #206 / `main` | **PARTIALLY FIXED** | On `main`, `MemoryWriteBoundary.prepare()` strictly detects and blocks promotion of synthetic evidence to `ACTIVE` or `verified` (`synthetic_evidence_promotion_blocked`). PR #206 branch remains unmerged and isolated from `main`. Verified by `test_b01_synthetic_evidence_promotion_blocked`. |
| **B02** | HMAC token does not protect note revision or content | P0 | `main` / PR #206 | **HARDENED IN LIBRARY, NOT YET WIRED INTO PRODUCTION** | Library only: no production consumer (see the correction note above). Added `revision_id` and `content_sha256` binding to `ApprovalToken` and `ExecutionRequest`. Any mismatch between approved revision and execution target is rejected fail-closed. Verified by `test_m01_b02_revision_binding_mismatch_rejected`. |
| **B03** | Lack of ablation on synthetic vs human evaluation | P1 | PR #206 | **REMAINS OPEN** | PR #206 branch remains unmerged. The synthetic ablation study has not been executed against real models. Main repository does not ingest PR #206 data. |
| **B04** | Prompt bias and leading questions in evaluation harness | P1 | PR #206 | **REMAINS OPEN** | PR #206 branch remains unmerged. Evaluator prompts in PR #206 must be redesigned with counter-factual baselines before any merge consideration. |
| **B05** | Single-rater evaluation vulnerability | P1 | PR #206 | **REMAINS OPEN** | PR #206 branch remains unmerged. Multi-model consensus voting is not yet cabled into PR #206 research runners. |
| **B06** | Metric calibration on synthetic corpus | P1 | PR #206 | **REMAINS OPEN** | PR #206 branch remains unmerged. Scores have not been calibrated on human-attested ground truth. |
| **B07** | Data leakage between evaluation sets | P1 | PR #206 | **REMAINS OPEN** | PR #206 branch remains unmerged. Overlap between training scenarios and benchmark questions remains unverified on PR #206. |
| **B08** | Non-comparable baseline models | P1 | PR #206 | **REMAINS OPEN** | PR #206 branch remains unmerged. Comparative evaluation tables lack identical parameter temperature and seed controls. |
| **B09** | Calibration of claims vs empirical evidence in reports | P1 | PR #206 | **REMAINS OPEN** | PR #206 branch remains unmerged. Research reports claim empirical verification where only unit mock simulation took place. |
| **B10** | External validity of simulated research pipeline | P1 | PR #206 | **REMAINS OPEN** | PR #206 branch remains unmerged. Real-world memory recall speed and accuracy improvements under production load remain unmeasured. |
| **B11** | Graph cleanup rewrites note body prose to prevent island notes | P1 | `main` | **FIXED + VERIFIED BY TEST** | Removed lines 81-91 semantic text injection in `30_SCRIPTS/knowledge/clean_source_frontmatters.py`. Frontmatter edge cleaning now strictly modifies YAML relations and preserves markdown body bytes identically. Verified by `test_b11_clean_frontmatter_preserves_body_bytes`. |
| **B12** | Severity downgrade from HARD_BLOCKER to WARNING | P1 | `main` / PR #206 | **PARTIALLY FIXED** | On `main`, `validate_severity_transition()` in `security/trust_gate.py` prevents downgrading blocker findings (`HARD_BLOCKER`, `BLOCKER`, `CRITICAL`, `P0`) to `WARNING` or `INFO`. PR #206 branch reports remain unmerged. Verified by `test_b12_severity_downgrade_blocked`. |

---

### 2.2 Independent Red-Team / Zero-Trust Findings (U01–U08)

| Finding ID | Finding Title | Priority | Target Surface | Status | Summary of Fix & Verification |
|---|---|---|---|---|---|
| **U01** | AI Agent Direct Active Mutation | P0 | `main` | **FIXED + VERIFIED BY TEST** | Enforced in `MemoryController.update()` fail-closed: `AI_AGENT` cannot update `ACTIVE` notes (`PermissionError`). Verified by `test_u01_ai_agent_cannot_mutate_active_note`. |
| **U02** | Omitted Hash Check in Enforcer | P1 | `main` | **HARDENED IN LIBRARY, NOT YET WIRED INTO PRODUCTION** | Library only: no production consumer (see the correction note above). Parameter hash bound and verified in `RuntimeEnforcer.authorize()`. Verified by `test_m01_b02_*`. |
| **U03** | `issue_approval` on `RuntimeAdapter` | P1 | `main` | **HARDENED IN LIBRARY, NOT YET WIRED INTO PRODUCTION** | Library only: no production consumer (see the correction note above). Blocked in production mode; requires out-of-process authority. Verified by `test_u03_runtime_adapter_issue_approval_blocked_in_production`. |
| **U04** | Unauthenticated REST API Server | P0 | `main` | **FIXED + VERIFIED BY TEST** | Bearer authentication enforced via `AI_MEMORY_VAULT_API_TOKEN` in `api_server.py`: every `/api/v1/*` route except `/status` answers 401 fail-closed, and all of them while the variable is unset. The gateway's clients send the token (the first version broke them): `jarvis_web/js/vault_client.js` and `js/app.js` read it at run time from `sessionStorage` (asked once after a 401; never in source), `jarvis_v2/supervisor.py` from the environment. Verified by `test_u04_api_mutation_routes_require_authentication`, `api_smoke_test.py`, `jarvis_web/test/test_vault_client_auth.js` and `20_TESTS/test_rest_proposal_flow.py`. |
| **U05** | Promotion Verification Gate Flag | P1 | `main` | **FIXED + VERIFIED BY TEST** | An approval is an owner attestation: `MemoryProposalQueue.mark("APPROVED", ...)` needs a typed owner `Principal` (the vault's `ATTEST` matrix: HUMAN, ADMIN), a reviewer name and an evidence reference, none defaulted (the CLI `approve` takes `--principal`, `--reviewer`, `--evidence`; the REST decision endpoint attests with the bearer-authenticated request as `Principal.HUMAN`). A reviewer string such as `human` satisfies nothing. `QueuePromoter.promote_approved()` promotes only attested approvals; an unattested legacy one is reported in `skipped` and no longer blocks the rest (it is still a hard error when nothing else could be promoted). Approve then promote now works over REST: a promoted candidate used to fail schema validation. Verified by `20_TESTS/test_proposal_queue_attestation.py`, `20_TESTS/test_rest_proposal_flow.py` and `test_u05_queue_promoter_requires_verified_evidence`. |
| **U06** | Unpinned Python Dependencies | P2 | `main` | **FIXED + VERIFIED BY TEST** | Pinned exact versions in `requirements.txt` and `requirements-memory-v6.txt` (Python 3.10-3.12 compatible). Verified by `test_u06_direct_dependencies_are_exactly_version_pinned` (hash-pinning documented as limitation). |
| **U07** | Windows Host NTFS DENY ACLs | P1 | host OS | **DEPENDENT — NOT PROVEN** | Host-level ACL enforcement depends on PR #207 (`codex/owner-authority-guardrail`), which remains OPEN. |
| **U08** | Unbounded Note Size DoS | P2 | `main` | **FIXED + VERIFIED BY TEST** | `MAX_NOTE_CONTENT = 20_000` enforced centrally in `MemoryController._validate_note()`. Verified by `test_u08_unbounded_note_size_rejection_at_controller_boundary`. |

---

### 2.3 Targeted Remediation of 5 Concrete Blockers

Detailed regression and adversarial proofs are documented in [`docs/security/PR209_REGRESSION_PROOF.md`](file:///c:/Users/user/Documents/Codex/AI_Memory_Vault_CODEX_READY/docs/security/PR209_REGRESSION_PROOF.md).

| Blocker ID | Description | Surface | Status | Enforcement & Verification |
|---|---|---|---|---|
| **Blocker 1 / Gate 1** | In-process fallback for `ApprovalBroker` and missing external authority separation | `security/runtime_enforcer.py`, `security/runtime_adapter.py` | **HARDENED IN LIBRARY, NOT YET WIRED INTO PRODUCTION** | In `production_mode=True`, strictly requires injected `ApprovalBroker` (secret $\ge 32$ chars, rejects `test-`/`dev-`) and injected `PersistentNonceStore`. `require_authenticated_approval` must be `True` (cannot be disabled). Validates secret against host env `VAULT_AUTHORITY_HMAC_SECRET`. Rejects issuer mismatch (`approval_issuer_mismatch`). `.broker` and `issue_approval` raise `PermissionError` in production mode. Added agent-safe `request_approval(request)`. Validated by `test_gate1_*` and `test_blocker1_*`. |
| **Blocker 2** | Revision and content bindings omissible on mutating operations | `security/runtime_enforcer.py` | **HARDENED IN LIBRARY, NOT YET WIRED INTO PRODUCTION** | When token specifies `revision_id` or `content_sha256`, request must specify and match them. Mandatory binding enforced for all mutating operations (`patch`, `update`, `state_mutate`, `delete`) and tools (`memory_patch`, `state_mutate`, `file_writer`, `code_patch`). Missing bindings return `approval_binding_required_for_tool`. Validated by `test_blocker2_mandatory_binding_for_mutating_tools`. |
| **Blocker 3 / Gate 3** | `memory_access.search()` and `get()` storage fallback for AI Agent | `03_IMPLEMENTATION/packages/interfaces/memory_access.py` | **FIXED + VERIFIED BY TEST** (scope corrected, see the note above) | `memory_access` no longer reconstructs content from storage for quarantined, ARCHIVED or RAW notes, and a `quarantined` note yields an empty snippet and a `get()` error. It does serve an ACTIVE note with its content whatever its verification label, and an unverified REVIEW candidate flagged `unverified` (the documented `memory_get` contract). The first version hid every non-verified note (2/48 snippets on the real vault). Verified by `20_TESTS/test_memory_access.py` and `test_blocker3_*`. |
| **Blocker 4 / Gate 2** | Persistent nonce store multi-process atomicity and corruption handling | `security/runtime_enforcer.py` (`PersistentNonceStore`) | **HARDENED IN LIBRARY, NOT YET WIRED INTO PRODUCTION** | Upgraded to SQLite WAL mode with `PRAGMA busy_timeout = 5000` and `BEGIN IMMEDIATE` atomic insert guarded by `PRIMARY KEY`. Deduplication proven across 8 concurrent OS child processes (`os_multiprocess_nonce_runner.py`). Fails closed on corruption. Validated by `test_gate2_real_os_multiprocess_nonce_replay` and `test_blocker4_*`. |
| **Blocker 5** | CI success reporting masks scanner failure | `.github/workflows/apisec-scan.yml`, `fortify.yml` | **VERIFIED_FIXED** | Added explicit step IDs (`id: apisec_scan`, `id: fortify_scan`). Final reporting step inspects step `outcome`: `EXECUTED_PASSED` is emitted ONLY if scanner succeeded (`outcome == 'success'`). Failed scanner emits `EXECUTED_FAILED`. Unconfigured emits `NOT_CONFIGURED_OPTIONAL`. Validated by `test_apisec_and_fortify_scan_outcome_checked_before_reporting_passed` and `test_blocker5_*`. |

### 2.2 Proof Artifacts & Empirical Verification
- **Multi-Process Nonce & Authority Proof**: [`docs/security/PR209_AUTHORITY_MULTIPROCESS_PROOF.md`](file:///c:/Users/user/Documents/Codex/AI_Memory_Vault_CODEX_READY/docs/security/PR209_AUTHORITY_MULTIPROCESS_PROOF.md)
- **Security Barrier Mutation Proof**: [`docs/security/PR209_SECURITY_BARRIER_MUTATION_PROOF.md`](file:///c:/Users/user/Documents/Codex/AI_Memory_Vault_CODEX_READY/docs/security/PR209_SECURITY_BARRIER_MUTATION_PROOF.md)
- **CI Failure Root Cause Analysis**: [`docs/security/PR209_CI_FAILURE_ROOT_CAUSE.md`](file:///c:/Users/user/Documents/Codex/AI_Memory_Vault_CODEX_READY/docs/security/PR209_CI_FAILURE_ROOT_CAUSE.md)

---

## 3. Adversarial Self-Audit Proof

### M01 — Approval Broker & Secret Isolation
1. **Secret Location**: The HMAC secret is passed to `ApprovalBroker(secret)` at system bootstrap from an environment variable or secure vault outside the repository (e.g. `VAULT_APPROVAL_SECRET`).
2. **Read Permissions**: The secret is accessible exclusively to the host/human broker process; neither the LLM nor agent workers have read access to the broker private attribute (`_secret`).
3. **Agent Import / Instantiation**: While an agent process can import the class `ApprovalBroker`, it cannot forge approvals because it lacks the valid secret. Any token signed with an arbitrary or forged key fails constant-time verification (`hmac.compare_digest`) in `RuntimeEnforcer.authorize()`.
4. **Approval Replay Across Process Restarts**: `RuntimeEnforcer` now supports `PersistentNonceStore(nonce_store_path)`. When configured, consumed token IDs and nonces are atomically persisted to disk. A token consumed in process A cannot be replayed in process B after restart.
5. **Absence of Broker**: If an approval is missing or unauthenticated, `RuntimeEnforcer.authorize()` returns `RuntimeAuthorization(False, "approval_unauthenticated")` or `"approval_required"`.

### M03 — Transactional Rollback & Integrity Failure Handling
- **Normal Rollback**: If `prepare()` succeeds, `_persist()` succeeds, but `boundary.commit()` fails, `MemoryAdapter` invokes `rollback(namespace, payload)` and records `MEMORY_ROLLBACK` in the audit log. The write returns `MemoryWriteDecision(False, reason)`.
- **Integrity Failure (`INTEGRITY_FAILURE`)**: If persistence succeeded, commit failed, and rollback either raises an error (e.g. disk I/O error) or no rollback handler was configured, `MemoryAdapter` audits `outcome="INTEGRITY_FAILURE"` and raises `MemoryIntegrityError`. This ensures unrecoverable states are never masked as a clean deny.

### M04 & M05 — Complete Model Read Path Audit
Every path capable of reaching LLM context was audited. The rule they share is `security.verified_reduction.content_withheld_from(principal, item, unverified=...)`: for a non-owner caller, withhold the body of a note flagged `quarantined` and of an unverified REVIEW candidate; never withhold an ACTIVE note for lack of a `verified` stamp.
- `ContextPackBuilder`: for such items `item["content"] = ""`, `item["snippet"] = ""` and `item["model_egress"] = False`; every other item has `model_egress = True`. Owners are `human` and `admin`.
- `ProgressiveDisclosure`: the egress flags (and `quarantined`) are preserved across `metadata_only`, `snippet`, `sections` and `full_document`, and withheld content stays blank at every level.
- `MemoryDataEgressGate`: re-applies the same predicate before egress, so a pack built for one principal cannot be routed to another with a body the second may not see.
- `interfaces/memory_access.py`: serves ACTIVE and REVIEW notes (REVIEW flagged `unverified`); a `quarantined` note gives an empty snippet and a `get()` error for `Principal.AI_AGENT`; ARCHIVED and RAW notes are not served. The unverified REVIEW candidate is handed over by this controlled tool, as untrusted data, never inside a trusted context pack.
- `working_memory.py` & `graph/activation.py`: blank the body of an unverified REVIEW node for `Principal.AI_AGENT` (both are off by default).

### M07 — Production Mode Bypass Verification
*Library only: `RuntimeAdapter` is not constructed anywhere in production code today, so `production_mode=True` is never in effect.* In `RuntimeAdapter`:
- Direct instantiation with `production_mode=True` requires:
  1. Authenticated `ApprovalBroker`.
  2. Active `SupplyChainPolicy` and registered tool hashes.
  3. Active `CatalogProvenancePolicy`.
- Missing any requirement raises `PermissionError` at initialization time, preventing stealth bypasses via default parameters.

### B01 — Synthetic Evidence Ingestion Guard
`MemoryWriteBoundary.prepare()` inspects incoming payloads:
- If `synthetic: True`, `source_type: "synthetic"`, or `provenance: "synthetic"` is set, and the payload attempts to assert `verification: "verified"`, `lifecycle: "ACTIVE"`, or `empirically_confirmed: True`, the boundary returns `MemoryWriteDecision(False, "synthetic_evidence_promotion_blocked")` with `record = None`. Zero persistence occurs.

### Hard Token Cap Enforcement
In `ContextPackBuilder`:
- If individual items or total pack exceed `hard_token_budget`, the builder performs adaptive compression. If content still exceeds budget, it progressively evicts non-protected items.
- If even an empty envelope exceeds `hard_token_budget`, it raises `BudgetExceededError` fail-closed. Under all circumstances: `final_context_tokens <= hard_token_budget`.

### B11 — Graph Frontmatter Rollback Byte-for-Byte Check
`30_SCRIPTS/knowledge/clean_source_frontmatters.py` was stripped of body-injection logic. The script only modifies YAML relation lines between `---` boundaries. A byte-for-byte check confirms:
```text
note_body_before == note_body_after
```
No semantic phrases (e.g. `[[state-determined system]]`) are injected.

---

## 4. Development Mode vs. Production Mode Matrix

*This describes what `production_mode=True` enforces inside the `security/` library. Nothing in the running system sets it yet (see the correction note in section 1), so today every deployment behaves as the left-hand column for these rows.*

| Feature | Development / Test Default | Production Mode (`production_mode=True`) |
|---|---|---|
| **Approval Broker** | In-process broker with generated test secret | Mandatory out-of-process broker with external secret |
| **Nonce Tracking** | In-memory `_used_approvals` set | Mandatory `PersistentNonceStore` tracking nonces across restarts |
| **Supply Chain Policy** | Local tools can execute for rapid testing | Mandatory `SupplyChainPolicy`; unregistered tool execution raises `PermissionError` |
| **Memory Write Rollback** | Logs warning if rollback callback omitted | Rollback handler required; rollback failure raises `MemoryIntegrityError` (`INTEGRITY_FAILURE`) |
| **Quarantined Content View** | Local diagnostic scripts can inspect unverified text | Model route returns `content = ""`; only authenticated human owner can inspect |
| **Security Catalog Watch** | Missing remote URL reports advisory | Mandatory signed catalog; invalid signatures halt deployment |

---

## 5. Residual Limitations & Scope Boundaries

1. **In-Memory Ledger Concurrency**: While SQLite WAL transactions are thread-safe and atomic within a single host, multi-node clustering requires distributed database synchronization.
2. **PR #206 Isolation**: PR #206 (`research/book-to-memory`) remains unmerged and isolated. Its 176 files and research benchmarks contain synthetic simulation artifacts that must not be merged into `main` without real multi-rater empirical evaluation.
3. **CI External Secrets**: Scanners (`Fortify`, `APIsec`) require repository-level secrets configured in GitHub Actions settings. When unconfigured, workflows report `SKIPPED_UNCONFIGURED`.

---

## 6. Empirical Test Execution Proof

Re-measured on 2026-10-07 on Linux (Python 3.13.16, pytest 9.1.1), on the branch merged with `origin/main` @ `66ca490f`. The Windows numbers this section used to quote predate the corrections in section 1.
```text
pytest -q                          (testpaths = 20_TESTS)   2678 passed, 13 skipped, 9 xfailed   (main @ 609b01bf, before #203: 2611 / 13 / 9)
pytest -q security/tests                                     204 passed
node --test test/test_jarvis.js test/test_vault_client_auth.js (jarvis_web)   234 passed
real-vault probe, 10 queries x top 5 through memory_access as AI_AGENT:
    non-empty snippets / memory_get ok:  main 38/48 and 38/48; branch before repair 2/48 and 6/48; after repair 48/48 and 48/48
gitleaks 8.30.1: no leaks on the tree, none on the history of all refs
```
`20_TESTS` and `security/tests` are run separately, as CI does: the two directories each hold a `test_prompt_compiler.py`, which collides when they are collected in one invocation (pre-existing).
