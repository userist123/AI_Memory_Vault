# PR #209: Audit Remediation (Runtime Authority, Memory Integrity & 5 Concrete Blockers)

> **Branch**: `security/audit-remediation-2026-10`  
> **Target**: `main`  
> **Status**: **OPEN — NEVER AUTO-MERGE — REQUIRES HUMAN SECURITY REVIEW**  
> **Commit**: the tip of this branch (this file ships in it; the PR page shows the SHA) (updated 2026-10-07; `origin/main` merged in, 0 conflicts)  

---

## 1. Executive Summary

This Pull Request contains the security remediation for audit findings across runtime authority, memory trust boundaries, unverified content quarantine, supply chain policies, and CI workflows.

**Read this first: what is enforced and what is not yet wired.** The independent review of this PR on 2026-10-07 found two things the earlier text of this description did not say.

1. **The runtime-authority work is hardened in the library, not yet wired into production.** The HMAC `ApprovalBroker`, the SQLite `PersistentNonceStore`, `production_mode`, revision/content binding (`security/runtime_enforcer.py`, `security/runtime_adapter.py`), the write-boundary rollback (`security/memory_adapter.py`, `memory_boundary.py`, `memory_integrity.py`) and the update provenance gate (`security/security_update_manager.py`) are real code with tests, but no module outside `security/` and the tests imports them, and nothing builds `RuntimeAdapter`, `RuntimeEnforcer` or `ApprovalBroker` with `production_mode=True`. Under the Global Production-Consumer Rule of `CLAUDE.md` they harden nothing at runtime until a tool-execution path calls them. Wiring them is a separate change and was deliberately not done here. Findings B1, B2, B4, M01, M02, M03, M07, U02, U03 and the nonce-persistence part of M08 are classified **HARDENED IN LIBRARY, NOT YET WIRED INTO PRODUCTION**. `20_TESTS/test_vault_state_accuracy.py` now fails the day one of those modules gains a production consumer.
2. **The first version of the memory quarantine broke the production memory path, with CI green.** It withheld the body of every note that was not `verified`. On the real vault, 10 queries x top 5 through `memory_access` as `AI_AGENT`: non-empty snippets fell from 38/48 (main) to 2/48, and `memory_get` on a REVIEW note failed, against the documented contract (`CLAUDE.md`: `memory_get` serves ACTIVE and REVIEW notes, REVIEW marked unverified). The test fixture had been changed to seed `verified` notes only, which is why nothing failed. Repaired below; after the repair the probe gives 48/48 and 48/48.

## 2. What this update changes (on top of the 27 original commits)

| # | Problem found in review | Fix | Guard |
|---|---|---|---|
| 1 | `.gitleaks.toml` rewritten to the legacy `[allowlist]` table; gitleaks refuses it next to the `[[allowlists]]` block that #207, #213 and #214 add, so whichever landed second turned the Gitleaks and R001 history scans red | back to `[[allowlists]]`, byte-identical to the file those PRs produce (they merge as no-ops). The extra entries were redundant (`security/tests/` already matches `(^|/)tests/`; the two test secrets occur only there; commit `2c9c1018` is not in this history; the shared `regexTarget = "match"` block covers the `private_key: Ed25519PrivateKey` annotation). gitleaks 8.30.1: no leaks on the tree and on the history of all refs | `20_TESTS/test_gitleaks_config_format.py` |
| 2 | memory read path hid every non-verified note (above) | one shared predicate (`security/verified_reduction.py`, `content_withheld_from`): withheld are `quarantined` notes, ARCHIVED/RAW notes and the body of an unverified REVIEW candidate inside a trusted context pack; ACTIVE notes keep their content whatever their label; `memory_get` serves a REVIEW note flagged `unverified` | `20_TESTS/test_memory_access.py` with a fixture holding the real mix of states; `20_TESTS/test_memory_mcp_server.py`; `security/tests/test_audit_remediation.py::test_m04_*` |
| 3 | REST approve -> promote never worked: the approval was not counted by the gate, and a promoted candidate failed schema validation on every path (`candidate-<uuid>` id, extractor keys in `provenance`, `fact`/`task` as note types; broken on main too) | candidates are proposed as schema-valid notes; the REST decision attests as `Principal.HUMAN` on the bearer-authenticated request; one unattested legacy approval is reported in `skipped` instead of aborting the batch | `20_TESTS/test_rest_proposal_flow.py` (propose -> approve -> promote over HTTP, real controller) |
| 4 | the promotion gate trusted a self-declared reviewer string and the CLI defaulted `--reviewer human` | an approval needs a typed owner `Principal` (the vault's `ATTEST` matrix), a reviewer name and an evidence reference; no defaults; `approve` takes `--principal {human,admin} --reviewer --evidence` | `20_TESTS/test_proposal_queue_attestation.py` |
| 5 | `vault_client.js` (and `app.js`, `jarvis_v2/supervisor.py`) sent no token: 401 on every route but `/status` | token from `sessionStorage` (asked once after a 401) or the environment; never in source; not sent to `/status` or other origins | `jarvis_web/test/test_vault_client_auth.js` (now a CI step), `20_TESTS/test_rest_proposal_flow.py` |
| 6 | `import_external_skills.py` silently skipped scripts and hidden files | fails closed: scripts, hidden paths (except the checkout's own `.git`), executable bits, symlinks and traversal abort with every offender listed; other non-imported types are written to `SKIPPED_FILES.json` | `20_TESTS/test_import_external_skills.py`, `test_m06_*` |
| 7 | the pack builder tested `!= "human"` (ADMIN lost content) while the data router used `{human, admin}` | one owner set, `OWNER_PRINCIPALS` | `test_m04_admin_and_human_are_treated_alike_*` |
| 8 | the findings were all labelled `VERIFIED_FIXED`; no coordination entry | relabelled in `docs/security/` and `00_GOVERNANCE/VAULT_STATE.md` (new rows, one of them tested); claim entry in `00_GOVERNANCE/coordination/agents/CLAUDE_OPUS/` | `20_TESTS/test_vault_state_accuracy.py` |

Behaviour changes of the original PR that the state card now records: an `AI_AGENT` cannot update an ACTIVE note except `relations`/`confidence`/`verification`/`valid_until`, and never to `verified` (U01); a note body over 20,000 characters is refused by the controller (U08).

Not done, on purpose: wiring the runtime-authority layer into a tool-execution path (separate change); trimming the ~1,000 lines of narrative `docs/security/PR209_*` proof documents (owner's call). Known and documented: `PersistentNonceStore.check_and_mark` treats any `DatabaseError`, including `database is locked` past the 5 s busy timeout, as a replay (fails closed, spurious denials under heavy contention); the web page is served by `server.cjs`, which has no `/api/v1` proxy.

---

## 3. 5 Concrete Blockers & Authority Gates Matrix

Detailed proofs and evidence are documented in:
- [`docs/security/PR209_REGRESSION_PROOF.md`](docs/security/PR209_REGRESSION_PROOF.md)
- [`docs/security/PR209_AUTHORITY_MULTIPROCESS_PROOF.md`](docs/security/PR209_AUTHORITY_MULTIPROCESS_PROOF.md)
- [`docs/security/PR209_SECURITY_BARRIER_MUTATION_PROOF.md`](docs/security/PR209_SECURITY_BARRIER_MUTATION_PROOF.md)
- [`docs/security/PR209_CI_FAILURE_ROOT_CAUSE.md`](docs/security/PR209_CI_FAILURE_ROOT_CAUSE.md)

| Gate / Blocker ID | Description | Affected Surface | Status | Enforcement & Verification |
|---|---|---|---|---|
| **Gate 1 / B1** | In-process fallback for `ApprovalBroker` and authority separation | `security/runtime_enforcer.py`, `security/runtime_adapter.py` | **HARDENED IN LIBRARY, NOT YET WIRED INTO PRODUCTION** | In `production_mode=True`, requires an injected `ApprovalBroker` (secret >= 32 bytes, rejects `test-`/`dev-`) and an injected `PersistentNonceStore`; `require_authenticated_approval` cannot be disabled; issuer mismatch rejected. Validated by `test_gate1_*` and `test_blocker1_*`. Nothing sets `production_mode=True` outside tests. |
| **Blocker 2** | Revision and content bindings omissible on mutating operations | `security/runtime_enforcer.py` | **HARDENED IN LIBRARY, NOT YET WIRED INTO PRODUCTION** | Mandatory `revision_id` / `content_sha256` for mutating tools; `approval_binding_required_for_tool`. Validated by `test_blocker2_mandatory_binding_for_mutating_tools`. |
| **Gate 3 / B3** | `memory_access.search()` and `get()` storage fallback | `03_IMPLEMENTATION/packages/interfaces/memory_access.py` | **FIXED + VERIFIED BY TEST** (scope corrected) | No content is rebuilt from storage for quarantined, ARCHIVED or RAW notes; a `quarantined` note gives an empty snippet and a `get()` error. ACTIVE notes and unverified REVIEW candidates are served, REVIEW flagged `unverified`. Real vault: 48/48 snippets, 48/48 `memory_get` (main 38/48, 38/48). |
| **Gate 2 / B4** | Persistent nonce store multi-process atomicity and corruption handling | `security/runtime_enforcer.py` (`PersistentNonceStore`) | **HARDENED IN LIBRARY, NOT YET WIRED INTO PRODUCTION** | SQLite WAL, `busy_timeout = 5000`, `BEGIN IMMEDIATE`; 8 concurrent OS processes (`os_multiprocess_nonce_runner.py`); fails closed on corruption. Nothing constructs a `PersistentNonceStore` outside tests. |
| **Blocker 5** | CI success reporting masks scanner failure | `.github/workflows/apisec-scan.yml`, `fortify.yml` | **FIXED + VERIFIED BY TEST** | `EXECUTED_PASSED` only when the scanner step's `outcome == 'success'`. Enforced in CI. |

---

## 4. Findings Matrix & Current Classification

| ID | Title | Severity | Status | Notes |
|---|---|---|---|---|
| **M01** | Cryptographic Approval Broker | P0 | **HARDENED IN LIBRARY, NOT YET WIRED** | HMAC-SHA256, persistent nonces, tamper-proof tokens; no production consumer |
| **M02** | Trust State Fail-Closed Gate | P1 | **HARDENED IN LIBRARY, NOT YET WIRED** | `memory_boundary` / `memory_adapter`; no production consumer |
| **M03** | Rollback & Integrity Failures | P1 | **HARDENED IN LIBRARY, NOT YET WIRED** | `INTEGRITY_FAILURE` if rollback fails; `MemoryAdapter` has no production consumer |
| **M04** | Context Pack Sanitization | P1 | **FIXED + VERIFIED BY TEST** (scope corrected) | non-owner callers get no body for `quarantined` notes and unverified REVIEW candidates; ACTIVE notes keep content |
| **M05** | Memory Disclosure Invariant | P1 | **FIXED + VERIFIED BY TEST** (scope corrected) | same rule at every disclosure level, in `memory_access`, `activation`, `working_memory` and the egress gate |
| **M06** | Skill Importer Filtering | P1 | **FIXED + VERIFIED BY TEST** | fails closed on scripts, hidden paths, executables, symlinks, traversal; reports what it leaves out |
| **M07** | Mandatory Production Controls | P1 | **HARDENED IN LIBRARY, NOT YET WIRED** | `production_mode=True` blocks bypasses, but nothing enables it |
| **M08** | CI status masking / replay persistence | P2 | **FIXED + VERIFIED BY TEST** (CI reporting); nonce persistence **HARDENED IN LIBRARY, NOT YET WIRED** | distinct `EXECUTED_PASSED` / `SKIPPED_UNCONFIGURED` / `ADVISORY_FAILED` outputs |
| **B01** | Positive evaluation results can be synthetic | P0 | **PARTIALLY FIXED** | Promotion of synthetic evidence to ACTIVE/verified blocked in the write boundary (library). PR #206 isolated. |
| **B02** | HMAC token revision & content binding | P0 | **HARDENED IN LIBRARY, NOT YET WIRED** | strict matching + mandatory binding for mutating operations/tools |
| **B03–B10** | Empirical evaluation gaps on research branch | P1 | **REMAINS OPEN** | Isolated on PR #206 (`research/book-to-memory`); not present on `main` |
| **B11** | Graph cleanup rewrites note body prose | P1 | **FIXED + VERIFIED BY TEST** | Preserves note body bytes identically |
| **B12** | Severity downgrade from HARD_BLOCKER | P1 | **PARTIALLY FIXED** | `validate_severity_transition()` in the library |
| **U01** | AI Agent Direct Active Mutation | P0 | **FIXED + VERIFIED BY TEST** | `MemoryController.update()`; `test_u01_ai_agent_cannot_mutate_active_note` |
| **U02** | Omitted Hash Check in Enforcer | P1 | **HARDENED IN LIBRARY, NOT YET WIRED** | bound in `RuntimeEnforcer.authorize()` |
| **U03** | `issue_approval` on `RuntimeAdapter` | P1 | **HARDENED IN LIBRARY, NOT YET WIRED** | blocked in production mode, which nothing enables |
| **U04** | Unauthenticated REST API Server | P0 | **FIXED + VERIFIED BY TEST** | bearer token on every route but `/status`, fail-closed when unset; the gateway's clients (browser, supervisor) now send it |
| **U05** | Promotion Verification Gate Flag | P1 | **FIXED + VERIFIED BY TEST** | owner attestation (typed owner principal + reviewer + evidence, no defaults); approve -> promote works over REST |
| **U06** | Unpinned Python Dependencies | P2 | **FIXED + VERIFIED BY TEST** | exact pins in `requirements.txt` and `requirements-memory-v6.txt`; `test_u06_*` |
| **U07** | Windows Host NTFS DENY ACLs | P1 | **DEPENDENT — NOT PROVEN** | depends on PR #207, which remains OPEN |
| **U08** | Unbounded Note Size DoS | P2 | **FIXED + VERIFIED BY TEST** | `MAX_NOTE_CONTENT = 20_000` in `MemoryController._validate_note()` |

---

## 5. Files Modified in this PR

```text
.github/workflows/ai-security-update-watch.yml
.github/workflows/apisec-scan.yml
.github/workflows/context-token-economy-gate.yml
.github/workflows/fortify.yml
.github/workflows/jarvis-command-center.yml
.github/workflows/polymarket-phase1-tests.yml
.github/workflows/polymarket-phase2-tests.yml
.github/workflows/polymarket-phase3-tests.yml
.github/workflows/polymarket-phase4-tests.yml
.github/workflows/r001-enforcement.yml
.github/workflows/repository-hygiene.yml
.github/workflows/security-boundary.yml
00_GOVERNANCE/VAULT_STATE.md
00_GOVERNANCE/coordination/agents/CLAUDE_OPUS/PR209_AUDIT_REMEDIATION_2026-10-07.md
02_PRODUCT/projects/workspaces/jarvis_web/README.md
02_PRODUCT/projects/workspaces/jarvis_web/jarvis_v2/supervisor.py
02_PRODUCT/projects/workspaces/jarvis_web/js/app.js
02_PRODUCT/projects/workspaces/jarvis_web/js/vault_client.js
02_PRODUCT/projects/workspaces/jarvis_web/package.json
02_PRODUCT/projects/workspaces/jarvis_web/test/api_smoke_test.py
02_PRODUCT/projects/workspaces/jarvis_web/test/test_vault_client_auth.js
03_IMPLEMENTATION/packages/graph/activation.py
03_IMPLEMENTATION/packages/interfaces/api_server.py
03_IMPLEMENTATION/packages/interfaces/memory_access.py
03_IMPLEMENTATION/packages/interfaces/memory_mcp_server.py
03_IMPLEMENTATION/packages/interfaces/memory_v6_cli.py
03_IMPLEMENTATION/packages/lifecycle/proposal_queue.py
03_IMPLEMENTATION/packages/lifecycle/queue_promoter.py
03_IMPLEMENTATION/packages/memory/controller.py
03_IMPLEMENTATION/packages/memory/data_router.py
03_IMPLEMENTATION/packages/memory/working_memory.py
03_IMPLEMENTATION/packages/retrieval/context/pack_builder.py
03_IMPLEMENTATION/packages/retrieval/context/progressive_disclosure.py
20_TESTS/fixtures/ontology_slot_writers.json
20_TESTS/memory_controller/test_api_server.py
20_TESTS/memory_controller/test_milestone3_empirical_challenge.py
20_TESTS/memory_vault_fixture.py
20_TESTS/regression/test_workflow_security_audit.py
20_TESTS/test_gitleaks_config_format.py
20_TESTS/test_import_external_skills.py
20_TESTS/test_memory_access.py
20_TESTS/test_memory_mcp_server.py
20_TESTS/test_memory_v6_promotion.py
20_TESTS/test_proposal_queue_attestation.py
20_TESTS/test_rest_proposal_flow.py
20_TESTS/test_vault_state_accuracy.py
30_SCRIPTS/knowledge/clean_source_frontmatters.py
30_SCRIPTS/verification/import_external_skills.py
README.en.md
README.md
docs/security/AUDIT_REMEDIATION.md
docs/security/NIGHT_SHIFT_BASELINE.md
docs/security/NIGHT_SHIFT_SECURITY_AUDIT.md
docs/security/PR209_AUTHORITY_MULTIPROCESS_PROOF.md
docs/security/PR209_CI_FAILURE_ROOT_CAUSE.md
docs/security/PR209_DESCRIPTION.md
docs/security/PR209_REGRESSION_PROOF.md
docs/security/PR209_SECURITY_BARRIER_MUTATION_PROOF.md
docs/security/RED_TEAM_ATTACK_SURFACE.md
docs/security/RED_TEAM_FINAL_REPORT.md
docs/security/RED_TEAM_MUTATION_RESULTS.md
docs/security/RED_TEAM_UNKNOWN_FINDINGS.md
docs/security/TOOL_EXECUTION_BOUNDARY_AUDIT.md
requirements-memory-v6.txt
requirements.txt
security/memory_adapter.py
security/memory_boundary.py
security/memory_integrity.py
security/runtime_adapter.py
security/runtime_enforcer.py
security/security_update_manager.py
security/tests/os_multiprocess_nonce_runner.py
security/tests/test_audit_remediation.py
security/tests/test_pr209_final_security_gates.py
security/tests/test_runtime_adapter.py
security/tests/test_runtime_enforcer.py
security/tests/test_security_update_manager_provenance.py
security/trust_gate.py
security/verified_reduction.py
```

79 files changed against `main` (the original description listed 27).

---

## 6. Empirical Test Execution Proof

Measured on Linux, Python 3.13.16, pytest 9.1.1, on the branch merged with `origin/main` @ `66ca490f` (PR #203 included):

```text
pytest -q                      (testpaths = 20_TESTS)
2678 passed, 13 skipped, 9 xfailed in 766s            (main @ 609b01bf, before #203: 2611 passed, 13 skipped, 9 xfailed)

pytest -q security/tests
204 passed

node --test test/test_jarvis.js test/test_vault_client_auth.js     (jarvis_web)
234 tests, 234 pass          node test/smoke_test.cjs: 24 passed
python test/api_smoke_test.py (gateway, with token): 7/7 PASS

gitleaks 8.30.1  detect --no-git (tree)                no leaks found
gitleaks 8.30.1  detect (history, all refs, 2,663 commits)   no leaks found

real-vault probe, 10 queries x top 5 through memory_access as AI_AGENT
                     non-empty snippets   memory_get ok
  main @ 66ca490f          38/48               38/48
  branch before repair      2/48                6/48
  branch after repair      48/48               48/48
```

The 10 results main cannot read are REVIEW notes that were never stamped with a verification; they are served flagged `unverified`.
The CI matrix (Python 3.10, 3.11, 3.12) runs on the pushed head.

---

## 7. Limitations & Architectural Boundaries

1. **Option A (Host-Trusted Broker Architecture)**: the authority broker is host-injected and trusted within the deployment runtime. `RuntimeEnforcer.request_approval()` generates a correlation request placeholder; it does not communicate with a remote server. None of it runs in production yet (section 1).
2. **Owner authority key storage**: in a production deployment `ApprovalBroker` would need a secure HMAC secret (>= 32 bytes) injected via `VAULT_AUTHORITY_HMAC_SECRET` or a hardware-backed keystore, inaccessible to the untrusted agent.
3. **Persistent nonce store**: the SQLite WAL database must live on local persistent storage reachable by the local worker processes, with restricted OS permissions. Any `DatabaseError` is treated as a replay (fail closed).
4. **Owner attestation is a typed principal, not a signature**: the CLI and the REST gateway name the owner principal themselves (the gateway only after the bearer token checks out). Anyone who can run the CLI as the vault owner can approve. A cryptographic owner approval is what the unwired broker would provide.
5. **CI external secrets**: `Fortify` and `APIsec` need repository secrets. When they are not configured the workflows report `SKIPPED_UNCONFIGURED` / `NOT_CONFIGURED_OPTIONAL` without failing CI or falsifying a pass.
6. **PR isolation**: PRs `#204`, `#206`, `#207` and `#208` are untouched. This update makes `.gitleaks.toml` identical to what #207, #213 and #214 produce, so they merge with it as no-ops.

---

## 8. Protected PRs Status

- **PR #204**: `MERGED` on `main` (`codex/skill-exfiltration-scanner`).
- **PR #206**: `OPEN` (`research/book-to-memory`) — isolated from `main`.
- **PR #207**: `OPEN` (`codex/owner-authority-guardrail`).
- **PR #208**: `MERGED` on `main` (`claude/loganalyzer-dfir-platform`).
- **PR #209**: `OPEN` (`security/audit-remediation-2026-10`) — updated, open for human review. **DO NOT AUTO-MERGE.**
