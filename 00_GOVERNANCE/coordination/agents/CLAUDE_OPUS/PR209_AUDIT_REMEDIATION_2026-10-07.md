---
agent: CLAUDE_OPUS
last_updated_utc: 2026-10-07T19:30:00Z
repository: userist123/AI_Memory_Vault_CODEX_READY
working_branch: security/audit-remediation-2026-10
pull_request: 209
base: origin/main 609b01bf (merged into the branch as ba9bdc18)
project_id: AI_MEMORY_VAULT
application: AI Memory Vault / Memory Engine
working_folder: 03_IMPLEMENTATION/packages/{interfaces,lifecycle,memory,retrieval}, security/, 20_TESTS/, 02_PRODUCT/projects/workspaces/jarvis_web/, docs/security/
current_task: make PR #209 functional (it was green in CI and broke real functionality)
status: DONE on the branch, awaiting owner review; not merged
in_progress: []
claim: "Claimed and completed by this session. Nobody else was working in these files when the claim was made (00_GOVERNANCE/coordination/ checked)."
evidence_level: TEST_VERIFIED plus a real-vault probe (RUNTIME_VERIFIED for the memory read path only)
related_agents: ANTIGRAVITY, CODEX, CLAUDE_SONNET
---

# PR #209 — what was wrong and what changed (2026-10-07)

PR #209 had 35 green checks and still broke the production memory path. Everything below is on
the branch `security/audit-remediation-2026-10`; the commits are listed in the PR description.

## Done

1. **`origin/main` merged** into the branch (merge commit, no conflicts).
2. **`.gitleaks.toml`** is back in the `[[allowlists]]` form and byte-identical to the file #207, #213 and #214 produce
   (main plus one shared block), so those merges are no-ops. Gitleaks refuses a config that mixes `[allowlist]` with
   `[[allowlists]]`. The PR's extra entries were redundant: `security/tests/` is already matched by `(^|/)tests/`, the
   two test secrets occur only there, the commit `2c9c1018` is not in this branch's history, and the narrow
   `regexTarget = "match"` block covers the `private_key: Ed25519PrivateKey` annotation. Verified with gitleaks 8.30.1
   on the current tree and on the full history of every ref (2,663 commits): no leaks. `20_TESTS/test_gitleaks_config_format.py`.
3. **Memory read contract restored** (blocking regression). `memory_search` / `memory_get` serve ACTIVE and REVIEW notes to
   `AI_AGENT`, REVIEW flagged `unverified`; withheld are `quarantined` notes, ARCHIVED/RAW notes and the body of an
   unverified REVIEW candidate in a trusted context pack. One shared predicate, `security/verified_reduction.py`
   (`content_withheld_from`, `OWNER_PRINCIPALS`), is used by the pack builder and the egress gate, so ADMIN and HUMAN are
   treated alike in both. Real vault probe (10 queries x top 5): main 38/48 snippets, 38/48 `memory_get`; PR before repair
   2/48 and 6/48; after repair 48/48 and 48/48 (the extra 10 are never-stamped REVIEW notes, read as unverified).
   The test fixture is unverified again and a `seed_real_distribution()` helper writes one note per real state; the
   verified-only fixture had hidden the regression.
4. **Approve -> promote works** over REST. Three independent faults were stacked: the REST approval was not an attestation
   the gate counted; a promoted candidate was proposed as `candidate-<uuid>` (the schema requires a uuid), with the
   extractor's own `provenance` keys and `fact`/`task` as note types, none of which the canonical frontmatter schema accepts
   (this one is also broken on main: no real controller ever took a promoted candidate); and `promote_approved()` aborted the
   whole batch on one unattested record. `20_TESTS/test_rest_proposal_flow.py` runs propose -> approve -> promote over HTTP
   against a real controller.
5. **The promotion gate is an owner attestation**, not a word: `MemoryProposalQueue.mark("APPROVED")` needs a typed owner
   `Principal` (the vault's ATTEST matrix), a reviewer name and an evidence reference; the CLI has no default reviewer.
   `20_TESTS/test_proposal_queue_attestation.py`.
6. **Browser clients send the API token**: `js/vault_client.js` and `js/app.js` (from `sessionStorage`, asked once after a 401,
   never in source), and `jarvis_v2/supervisor.py` (from the environment). `test/test_vault_client_auth.js` runs in the
   jarvis-web CI job.
7. **Skills importer fails closed** (scripts, hidden paths, executables, symlinks, traversal abort with every offender listed;
   other non-imported types go to `SKIPPED_FILES.json`).
8. **Honesty**: the runtime-authority layer is labelled "hardened in library, not yet wired into production" in
   `docs/security/` and in `VAULT_STATE.md`; `20_TESTS/test_vault_state_accuracy.py` fails when it gains a production consumer.

## Not done, on purpose

- The runtime-authority layer was **not** wired into a tool-execution path. That is a separate change (est. 1-2 days).
- `PersistentNonceStore.check_and_mark` treats any `sqlite3.DatabaseError`, including `database is locked` past the 5 s
  busy timeout, as a replay. That fails closed but gives spurious denials under heavy contention.
- The web UI is served by `jarvis_web/server.cjs`, which has no `/api/v1` proxy; the page needs the gateway behind the same origin.
- `docs/security/PR209_*` is about 1,000 lines of narrative proof inside the tree; trimming it is the owner's call.

## Decisions that belong to the owner

- Whether REVIEW notes that were never stamped with a verification (10 of the 48 probe results) should be served as
  unverified (done) or stay hidden as on main.
- Whether `docs/security/PR209_*` stays in the repository.
- Whether to wire the runtime-authority layer, and where.

## Next action

Owner review of the PR; then land #214, which carries the same `.gitleaks.toml`, and merge in either order.
