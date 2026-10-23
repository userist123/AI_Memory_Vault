# Overnight reliability mission - executive report (2026-10-21/22)

VERDICT: SAFE FOR CONTROLLED LOCAL TESTING

Conditions (independent round-2 review, 2026-10-22): (1) the global self-update hook stays unregistered (user hooks = 0) and HIGH-2 (unpinned/unsigned remote installer) remains an owner-accepted residual until signing is implemented; (2) repo-root sessions run with COST_ROUTER_NO_SELF_INSTALL=1 or the owner accepts the global skill refresh; (3) accepted residuals recorded with the owner: MED-B (state-file forgery of escalation_authorized; hooks are a cost-policy aid, not a boundary against an agent with arbitrary write), R2-1 LOW-MED user-level/plugin agent defaults above the session tier are not capped, R2-2 LOW unrecognised model strings pass through on non-review briefs, R2-3 LOW settings-file model still drives the same-tier advisory (never escalation), RT03/RT15/RT16 xfail residuals; (4) the loosened pre-W7 assertions in 20_TESTS/test_cost_router_enforce.py are reviewed before any merge; (5) nothing is committed, pushed or installed globally without owner authorization. TOKEN SAVINGS NOT DEMONSTRATED (unchanged).

Labels: SOURCE_VERIFIED, TEST_VERIFIED, RUNTIME_VERIFIED, MEASURED, INFERRED, UNVERIFIED. Details and commands: GATE_LOG.md; machine-readable: evidence.json.

## Starting state
- Branch `gemini/casa3d-nextgen-functionality-uiux`, HEAD 2c72a7228 at 23:12 (`state_start.txt`); another agent later committed two Casa3D commits (now d0e31c2f7), unrelated to this work. 122 dirty/untracked entries at start, most from earlier cost-router phases and Antigravity (SOURCE_VERIFIED).
- Baseline full suite (temp HOME, user site on PYTHONPATH): 54 failed, 4454 passed, 6 errors; mostly environment isolation and other agents; classified in the checkpoint (TEST_VERIFIED).
- Phase-4 finding: a haiku session with hooks ON raised subagents to sonnet/opus (+842 % cost, one correctness failure) (SOURCE_VERIFIED from the phase reports). Token verdict at start: savings not demonstrated.

## Most consequential verified findings (audits A-D; SOURCE_VERIFIED, then fixed and TEST_VERIFIED unless noted)
1. HIGH, router: a review/high-risk brief sent to a worker subagent type escaped the Opus review floor; silent escalation had no session-model input; stored session model beat the transcript. Fixed (`hook_agent_model.py`, `hook_prompt_route.py`, `route_state.py`), 149 matrix tests.
2. HIGH-1, security: self-update ignored `COST_ROUTER_NO_SELF_INSTALL` (unauthorised global install path); `install.py --session-start` ignored `COST_ROUTER_OFF`. Fixed. Default opt-in and HIGH-2 (unpinned, unsigned `main` fetched and executed hourly) remain OPEN: proposal in SECURITY_REDTEAM_RESULTS.md.
3. MED: proposal poisoning by repetition (dedup added), unflagged injection in retrieved notes (`review_required` added; `vault_read` still unflagged), HMAC key inherited by icacls children (fixed; still in `os.environ`), unlocked resume counter (fixed in the Claude copy only).
4. Retrieval: ASCII-only tokenizer split Romanian diacritics and dropped 1-char ids; corrupt notes were skipped silently. Fixed (11 regression tests, 5 fail on the old code). The audit's "window selected before scoring" suspicion was REFUTED on the main path.
5. Telemetry: missing usage counted as 0, streams without a result were scored, failed runs entered savings pooling. Fixed with fixtures; archived raw streams reconcile (37+46+11 streams, 0 mismatches, RUNTIME_VERIFIED).

## Changes
Files and ownership: RELEASE_READINESS.md (rollback list) and evidence.json. New tests: `20_TESTS/redteam` (39 passed + 3 xfail residuals), `test_cost_router_escalation_matrix.py` (149), `regression/test_retrieval_correctness_overnight.py` (11), telemetry fixtures plus benchmark tests (68), `30_SCRIPTS/evaluation/token_accounting_check.py`. Nothing committed, pushed, installed globally; paid spend $0 (MEASURED: no benchmark command run).

## Tests (final gate)
Full suite: 34 failed, 4728 passed, 51 skipped, 9 xfailed, 6 errors (baseline 54/4454/51/6/6). Versus baseline: 25 newly passing, 5 newly failing, 34 unchanged. Newly failing: 3 are caused by the mission and are test-side or fixture-side issues (see below), 2 by another agent (Casa3D `node_modules` symlink adding a duplicate group). Isolation runs of the two hook test files in both orders: 112 passed each.

## Unresolved blockers and defects (not fixed by the gate; instruction: report only)
- D1 `20_TESTS/test_cost_router_escalation_matrix.py:322-325` leaks `transcript_usage`/`route_state` into `sys.modules`, failing `test_hook_antigravity.py::test_send_message_resume_idempotent_on_same_step` when run after it (full suite and 2-file run).
- D2 `03_IMPLEMENTATION/packages/interfaces/memory_access.py:304` (`controller.storage.get`) breaks `test_book_to_memory_lifecycle_gates.py::test_16_helper_function_bypass_rejected` (stub storage without `get`).
- D3 `30_SCRIPTS/evaluation/eval_tokenizer_experiment.py:79` baseline arm now follows the production tokenizer, breaking `test_tokenizer_experiment_accuracy.py::test_tokenizer_behaviors`.
- O1 HIGH-2 and the opt-in default for global self-update are not implemented (owner decision).
- O2 Residuals with xfail tests: summarised-document injection, forged telemetry sidecar, evaluator instructions in answers; `vault_read` unflagged; no proposal rate cap; Antigravity `route_state.py` copy unlocked; weak assertions in `test_cost_router_claude_hooks.py` not strengthened; `sanitize_query` stripping `<...>`.
- O3 `test_notes_strict_yaml.py` stays red (108 unreadable Casa3D/other notes; ours fixed). Paths 4 and 7 (working-memory admission, provider telemetry) untraced: UNVERIFIED.
- O4 Global state: the global cost-router copy was rewritten at 23:32 and the frontend-design plugin was installed at 23:37 by other sessions (RUNTIME_VERIFIED: SessionStart of session 9eb044d1 at 20:32:44Z; plugin installedAt 20:37:05Z), not by gate commands (GATE_LOG.md G5).

## Recommendation
Do not release or promote yet. First: independent review of the final diff (mandatory for risky code), fix D1-D3 (small, test-side), decide HIGH-2 and the opt-in default, and treat the token claim as TOKEN SAVINGS NOT DEMONSTRATED. The router policy and security fixes are test-verified locally and safe for controlled local testing under `COST_ROUTER_NO_SELF_INSTALL=1`, as INFERRED from the evidence; this is an input, not the verdict.
