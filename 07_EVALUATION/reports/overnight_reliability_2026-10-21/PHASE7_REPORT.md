# Phase 7: owner-recommended follow-ups (2026-10-22)

Branch `gemini/casa3d-nextgen-functionality-uiux`, HEAD 655f829be. Nothing was committed. Checkpoint:
`00_GOVERNANCE/coordination/tasks/todo-claude-overnight-reliability.md` (PHASE 7).

## Results

| Item | State | Evidence |
|---|---|---|
| P7-1 tokenizer artefacts | DONE: `07_EVALUATION/tokenizer_experiment/VALIDITY_NOTICE.md` labels the experiment INVALID / NULL MEASUREMENT. The report and JSON are unchanged. A guard test requires the notice while the arms are identical. | TEST_VERIFIED |
| P7-2 `sanitize_query` | DONE (variant 2, owner-approved). Both copies now `return query.strip()`. The auto-mode classifier first blocked the change; it was applied after the owner left auto mode, not by bypassing the classifier. `test_security.py:30,33` and `test_core.py:96-97` were rewritten as the owner-approved contract change. Round-2 review (opus, delta only): SAFE. The old regexes give the same output as the new code on all 252 frozen benchmark queries, so no artefact drifts. | TEST_VERIFIED: 455 passed / 3 xfailed + P0 11 passed; reviewer 402 + 98 passed |
| P7-3a `vault_read` | DONE: additive `review_required` / `review_reasons`. The text, sha256, `cite_as` and error envelopes are unchanged, and a scanner failure sets the flag (fail closed). | TEST_VERIFIED, review probes |
| P7-3b `memory_propose` rate limit | DONE: per client, sliding window, default 20/3600 s, `VAULT_PROPOSE_RATE`. Invalid config refuses proposals (fail closed); duplicate replies do not count; proposals stay REVIEW. Stale client buckets are dropped. | TEST_VERIFIED, 300-thread probe |

## Sink audit (basis for variant 2, SOURCE_VERIFIED)
After `sanitize_query`, the query reaches only these places: tokenizer, BM25, planner, classifier and scorers; sha256
fingerprints; the deterministic f-strings in `memory/reasoning.py` (no LLM); and a JSON echo at `financial_search.py:1478`.
None of them renders HTML, interprets `{{...}}` or builds an LLM prompt. `vault_resolve` was never sanitized. The correct
defence is escaping at whichever site renders the query.

## Review (opus, round 1): SAFE
- MEDIUM: the interim sanitizer has a worst case of about 1 s on a 4096-character input (n/3 passes). Accepted until variant 2 lands.
- Residuals:
  - Client names are self-declared, so rotating them bypasses the per-client limit; there is no global cap.
  - A rate-limit slot is released on exceptions raised after `storage.set`.
  - `00_GOVERNANCE/coordination/antigravity/CURRENT.md:65` still cites H-TOKEN-1 without the notice. That file belongs to another agent.
- Fixed after review: the per-client counters no longer grow without bound (`test_expired_client_buckets_are_dropped`).

## Full suite (`phase7_pytest.txt`, run once)
32 failed / 4963 passed / 51 skipped / 9 xfailed / 3 errors, compared with phase 6 at 26 failed / 4857 passed.
- 10 failures are `test_phase7_sanitize_query.py`, which encodes variant 2 and is expected to fail until it is applied.
- The other 22 failures are classified as follows:
  - 20 were already present in phase 6.
  - `test_vault_access_clients::test_the_real_registry_passes_its_ci_check` was a collection error in phase 6 and is now collected. It fails on a local, git-ignored `04_MEMORY/` directory (ENV).
  - `test_owner_authority_gate::test_settings_command_allows_and_denies`: `bash -c` reports "no working python interpreter". The test file and its settings example are unchanged since 2026-10-08. Classified as ENV (bash/PATH); the root cause is UNVERIFIED.
- `security/tests/test_prompt_compiler.py` cannot be collected because `20_TESTS/test_prompt_compiler.py` has the same basename (another agent's file).
- Run with `--continue-on-collection-errors`.

## Variant 2 applied (2026-10-22, after the full-suite run)
- The 10 `test_phase7_sanitize_query.py` failures listed above are now green. The full suite was not re-run after
  this delta; the affected sets were run instead (see the table).
- LOW, docs only: two docs still describe stripping: `07_EVALUATION/security_invariant_reconciliation_2026-09.md:130`
  (already inaccurate before this change) and `10_DOCUMENTATION/resources/Obsidian/Artifacts/implementation_plan_p0_8_security.md:42`.
  Neither is a code path. Both predate this change.
- Risk: no upstream escaping remains. Any future HTML, template or LLM-prompt sink must escape the query where it renders it.
