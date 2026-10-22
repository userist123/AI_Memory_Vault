---
id: "21079039-8875-507c-aefa-16075474a46c"
title: "Checkpoint: Claude Code - overnight reliability mission"
type: "project"
lifecycle: "REVIEW"
created: "2026-10-21T19:50:00Z"
updated: "2026-10-22T06:30:00Z"
verification: "unverified"
provenance:
  source_type: "execution"
  source_ref: "00_GOVERNANCE/coordination/tasks/todo-claude-overnight-reliability.md"
  created_by: "claude-code"
tags:
  - "checkpoint"
  - "cost-router"
  - "coordination"
---

# todo-claude-overnight-reliability — AUTONOMOUS RELIABILITY ENGINEERING (overnight)

Agent: Claude Code principal engineer (orchestrator) + subagents. Started 2026-10-21 ~22:50 local.
Owner brief: the "OVERNIGHT MISSION" prompt of 2026-10-21 (workstreams A-I, deliverables A-G, verdict set).
Recorded HEAD 2c72a7228 on gemini/casa3d-nextgen-functionality-uiux — re-verify; never switch branch.
Deliverables dir: `07_EVALUATION/reports/overnight_reliability_2026-10-21/` (+ JSON evidence there).

## Hard constraints (owner)
- No commit/push/merge/release; no reset/stash/broad checkout/clean on the shared tree; preserve other agents' work.
- No global changes (~/.claude, ~/.gemini, installed skills, user hooks, credentials). Installer tests only with tmp --home.
- PAID BUDGET ≈ $1.10 remaining (phase 3+4 ≈ $28.9 of $30). NO `claude -p` / benchmark runs in this mission. Use
  existing raw evidence in `07_EVALUATION/reports/cost_router_claude_*` and `..._phase4_pilot_*`.
- Lean model use: Explore (haiku) for reads, vault-worker (sonnet) for bounded changes, ONE final vault-reviewer (opus).
- Evidence labels on every claim: SOURCE_VERIFIED / TEST_VERIFIED / RUNTIME_VERIFIED / MEASURED / INFERRED / UNVERIFIED.
- A concurrent OWNER session (`fd1a8a49…`, "Continua cu loganalyzer dfir", started 21:41 at repo root) works in this
  checkout: before editing any file, check `git status`/mtime; never touch LogAnalyzer/DFIR files or anything modified
  after this mission started that it does not own.

## Known prior state (from phase 1-4 checkpoints; re-verify before reporting as current)
- Cost router: `.claude/skills/cost-router/` hooks (prompt_route, repo_map, agent_model w/ in-hook Vault router,
  enforce_route w/ same-tier advisory + capped breaker, resume_guard, agent_usage telemetry), `lib/route_state.py`
  (`resolve_session_model`, markers, seen_event idempotency, atomic writes), `COST_ROUTER_OFF=1`,
  `COST_ROUTER_NO_SELF_INSTALL=1`. Phase-4 finding: haiku session + hooks ON → subagents raised to sonnet/opus
  (cost +842%, 1 correctness failure) — the owner now requires: low-cost sessions must not SILENTLY escalate;
  mandatory high-risk review must not be bypassed (if unavailable within policy: stop / request authorization).
- Token verdict stays TOKEN SAVINGS NOT DEMONSTRATED (prefix −3,904/request demonstrated only).
- Global copy `~/.claude/skills/cost-router` is rewritten by the repo SessionStart self-update whenever a session starts
  at repo root (owner sessions did so at 21:03 and 21:41); opt-in proposal not implemented.
- Historical retrieval concerns: candidate window selected BEFORE scoring (relevant items beyond the limit lost);
  query text/intent not propagated through all layers. Check `cognitive_core/`, `03_IMPLEMENTATION/packages/` memory
  controller / recall / ranking / working-memory admission, the MCP server (`.mcp.json`), and the P0 adversarial tests.
- Protected core (never edit): `cognitive_core/model_provider.py`, `fake_model_provider.py`, `model_tier_router.py`,
  `actual_usage_telemetry.py`, `council_model_execution.py`, `executive_model_execution_bridge.py`.

## Plan (one implementer at a time; read-only agents may run in parallel)
0. State capture (branch, HEAD, status, dirty owners, baseline test runs of memory/security/cost-router suites).
1. Explore audits in parallel (read-only, structured findings: file+symbol, evidence, impact, fix, tests, uncertainty):
   A retrieval + integrity path; B security/trust boundaries; C cost-router decision matrix + global side effects;
   D test quality + telemetry aggregation.
2. Principal decides fixes (priority: unauthorized actions/secrets/destructive > memory corruption/retrieval/governance >
   routing/failed tasks > telemetry > recovery > token overhead > docs). Assign file ownership per worker.
3. Workers sequentially: retrieval fix+tests; router escalation policy+matrix tests; red-team suite (local only);
   telemetry fixtures (missing=unknown, failed≠saving); test-quality fixes; recovery invariants.
4. Integration gate (targeted suites, cross-component, git diff --check, ownership/global checks).
5. One vault-reviewer (opus) pass on the final diff + evidence; fix Critical/High; document the rest.
6. Deliverables A-G; verdict: SAFE FOR CONTROLLED LOCAL TESTING / NOT SAFE — REMEDIATION REQUIRED /
   BLOCKED — INSUFFICIENT EVIDENCE OR OWNERSHIP.

## Done
- [ ] 0  - [ ] 1  - [ ] 2  - [ ] 3  - [ ] 4  - [ ] 5  - [ ] 6

## Notes / ledger
- Paid runs this mission: $0 (none planned).
- Step 0: state in `07_EVALUATION/reports/overnight_reliability_2026-10-21/state_start.txt` (HEAD 2c72a7228 unchanged,
  122-line status). Baseline pytest needs the real user site-packages on PYTHONPATH when HOME/APPDATA are temp
  (pytest lives in %APPDATA%\Python\Python314\site-packages); 4 collection errors (test_memory_mcp_server,
  test_vault_access_clients, test_vault_routes_quarantine_parity, security/tests/test_prompt_compiler) — full run pending.
- Audit A (retrieval): R1 window-before-scoring REFUTED on the main path (generate_candidates scores the whole pool
  then slices, candidate_generation.py:178-180); R2 query propagated (sanitized query used). Defects: M tokenizer
  ASCII-only `hybrid_retrieval.py:35/61` (diacritics split, 1-char ids dropped); L-M `security/security.py:6`
  sanitize_query strips `<...>` content; L-M `file_engine.py:284` corrupt note silently skipped (error≈empty);
  L tie order inconsistent (asc id window vs desc final); L no proposal dedup. Paths 4/7 untraced.
- Audit C (router, 24-cell runtime matrix): HIGH review bypass by subagent_type (`hook_agent_model.choose()` sets
  reviewer only from subagent_type; review/high brief to vault-worker -> sonnet); HIGH `persist()` never stores risk ->
  `_advisory_note` risk exclusion dead -> sonnet session implement/high advisory; MED silent escalation (choose() has
  no session-model input); MED stale stored session model beats transcript; MED install.py --session-start ignores
  COST_ROUTER_OFF; LOW unknown explicit model leaves floor; LOW missing usage -> 0.
- Audit D (tests/telemetry): missing usage -> 0; no `incomplete` for streams without result; failed runs enter savings
  pooling (`summarize_tokens` :1147-1157; test_summarize_stats_and_ci locks it in); cost/token inconsistency unflagged;
  weak substring/existence assertions in test_cost_router_claude_hooks.py (:314, :430, :628, :864, :372/:453/:475),
  sys.modules leak :985. Re-derivation: both-correct pairs phase3_final router −58.1% (more tokens); phase4 ctx_new
  +20.8% (n=15, sign p≈0.12) -> TOKEN SAVINGS NOT DEMONSTRATED holds.
- Audit B (security, SOURCE_VERIFIED; auditor could not run pytest): HIGH-1 `hook_self_update.py:main/update` ignores
  COST_ROUTER_NO_SELF_INSTALL and `install.py` honours it only for --session-start (unauthorized global install path);
  HIGH-2 self-update clones unpinned/unsigned `main` and runs its installer hourly (owner release-process decision:
  document + propose pin/manifest hash); MED-3 `vault_runtime.ensure_secret_in_env` exports the HMAC key to os.environ
  (inherited by children); MED-4 `memory_access.propose` no dedup/rate cap (poisoning by repetition); MED-5 MCP
  retrieval (memory_get/search/vault_read) returns raw text with a label only, no `scan_text` review flag; LOW-6
  `record_resume` read-modify-write without lock + resume guard fails open on unresolved transcript. Guarded+tested:
  governance override (no attest via MCP), path traversal (vault canonical paths, state ids), malformed payloads.
  Unguarded: summarised-doc injection (local_llm), telemetry tampering (user-writable sidecar), evaluator-instruction
  data in benchmarks. 16-row table in the auditor report (to be copied into SECURITY_REDTEAM_RESULTS.md).
- BASELINE full run (20_TESTS + security/tests, temp HOME + user site on PYTHONPATH, 20 min): 54 failed, 4454 passed,
  51 skipped, 6 xfailed, 6 errors (`baseline_pytest.txt`). CAVEAT: overlapped W1 edits -> cost-router file failures
  not conclusive (re-run at the gate). Classification (source/log evidence; ollama verified by run):
  ENV_ISOLATION: pywintypes/jsonschema not importable with temp APPDATA (test_memory_mcp_server, vault_access_clients,
  vault_routes_quarantine_parity, security/test_prompt_compiler collection; vault_access_perf x4;
  memory_consolidation_command; audit_remediation u01), env var >32767 chars (vault_access_core x2), exec bit on Windows
  (import_external_skills). PRE_EXISTING (fails at HEAD): vault_access_ollama_telegram x7 (RUN at HEAD worktree: same
  7 failures; CRLF fixture), vault_state_accuracy x3 (count drift), repository_layout (`03_IMPLEMENTATION/packages/
  routing/claude_usage_report.py:58` drive-letter example in a committed docstring — exact fix: use `<DRIVE>:` placeholder;
  file belongs to the committed Antigravity cost-router work -> residual), b2m/book_to_memory artefact drift x5,
  vault_access_core read_is_verbatim (CRLF). OTHER AGENTS (Casa3D): personal_data_guard (IBAN_RO in casa3d test
  fixtures), readme_references x2 + workflow_policy (casa3d-build.yml), bootstrap_cold_start (CASA3D/CURRENT.md).
  notes_strict_yaml: 21 unreadable files, first `todo-antigravity-casa3d.md`; check whether our `todo-claude-*`
  checkpoints are in the list at the gate (fix = valid frontmatter on OUR files only).
- QUEUE (sequential implementers): W1 router (running) -> W2 security (HIGH-1 guards, MED-4 dedup, MED-5 review flag,
  MED-3 env scrub if consumers allow, LOW-6 atomic resume counter, red-team test suite) -> W3 retrieval (tokenizer
  Unicode/NFKD, corrupt-note skip count, tests) -> W4 telemetry/harness (missing=unknown, incomplete, failed pairs
  excluded, inconsistency flags, fixtures) -> docs -> one vault-reviewer -> fixes -> verdict.
- PRINCIPAL DECISION (escalation policy, owner rule 2026-10-21): (R2) review/verify brief OR risk high/critical ->
  target ≥ opus whatever subagent_type; if session tier < opus -> DENY with explicit reason unless the owner wrote
  `#escalate` in the prompt (stored by hook_prompt_route from the USER prompt only; tool_input can never set it); with
  `#escalate` -> allow + visible additionalContext note. (R1) normal-risk work whose target > session tier -> cap the
  subagent at the session tier + visible note "not escalated; #escalate allows <t>" (no deny, no silent raise).
  (R3) explicit Agent `model` above the session tier -> capped likewise (model choice ≠ owner authorization).
  (R4) advisory enforcement only when risk is empty/normal and class ∉ {review, design, frontier}. (R5) unknown
  session model -> treated as the cheapest tier for escalation decisions (fail closed) with a note to set
  ANTHROPIC_MODEL. Stale model: transcript last assistant model wins over the stored value.
- Done (worker, 2026-10-21): router escalation policy F1-F7 in .claude/skills/cost-router (hook_agent_model apply_session_policy/needs_opus, hook_prompt_route #escalate+risk, route_state transcript-first, install --session-start honours COST_ROUTER_OFF) + 20_TESTS/test_cost_router_escalation_matrix.py (146 tests); high-risk workers now opus (R2), awaiting vault-reviewer.
- Done (W2 worker, 2026-10-22 TEST_VERIFIED): W1 correction (needs_opus/required_tier follow rules.implementer_risk_floor:
  high-risk implementer -> sonnet, review/verify brief or critical/live-op -> opus, deny below the required tier unless
  #escalate; worker_stays_on_sonnet test restored); HIGH-1 (hook_self_update main + install --self-update honour
  COST_ROUTER_NO_SELF_INSTALL); MED-4 propose dedup (uuid5 of normalised title/body/type/provenance/principal,
  `deduplicated`); MED-5 `review_required`/`review_reasons` on memory_access search/get via security/skill_exfiltration_scanner
  .scan_text (file-loaded; flag only); MED-3 icacls children get env without the HMAC key (in-process consumers keep
  os.environ: residual); LOW-6 record_resume under an O_EXCL lock + resume guard counts unresolved targets by id;
  hermetic switch handling in self_update/skill tests; red-team suite 20_TESTS/redteam (39 pass + 3 xfail residuals
  RT03/RT15/RT16). Not touched: .agents/skills/cost-router/lib/route_state.py (Antigravity copy, same LOW-6 gap).
- Done (Audit A fixes, retrieval-correctness agent): D1 tokenizer Unicode+NFKD fold, keep 1-char digits (hybrid_retrieval.py); D2 skip accounting (file_engine.skip_report + candidate_trace.skipped_corrupt/skipped_paths in memory/controller.py); tests 20_TESTS/regression/test_retrieval_correctness_overnight.py (11 pass; 5 fail on old code). Gate: 546 passed before, 557 after (+11 new), no regressions.
- Done (Audit D fixes, telemetry agent, 2026-10-22): T1 missing usage = unknown (harness attribute()/metrics_from: usage_complete, missing_fields, tokens_complete, lower-bound totals; hook_agent_usage stores null + usage_complete=false, --report shows unknown); T2 no-result stream = incomplete, never correct (run_once + recompute re-score); T3 deltas only over correct+complete (task,repeat) pairs, pairs_used/pairs_excluded reported, test_summarize_stats_and_ci corrected; T4 flags usage_mismatch/cost_without_tokens/tokens_without_cost/cost_partial + SUMMARY WARNINGs; T5 fixtures 20_TESTS/fixtures/telemetry/ (+ parametrised test); T6 30_SCRIPTS/evaluation/token_accounting_check.py. T7 re-derivation on scratchpad copies: phase3_final both-correct pairs ALL total_tokens reduction -58.1% (16 pairs, 2 excluded), phase4 ctx_new vs base_old +20.8% (15 pairs, CI incl. 0), combined -5.7% (14 pairs); TOKEN SAVINGS NOT DEMONSTRATED still holds. Archived dirs untouched. Residual: hook-test weak assertions (:314,:430,...) and sys.modules leak :985 not addressed.
- Done (gate worker, 2026-10-22 00:55 TEST_VERIFIED): integration gate + deliverables A-G in `07_EVALUATION/reports/overnight_reliability_2026-10-21/` (GATE_LOG.md, OVERNIGHT_EXECUTIVE_REPORT, ARCHITECTURE_AND_DATAFLOW, SECURITY_REDTEAM_RESULTS, TOKEN_ACCOUNTING_VALIDATION, RELIABILITY_TEST_MATRIX, RELEASE_READINESS, evidence.json, final_pytest.txt). G1 final 34 failed / 4728 passed / 9 xfailed / 6 errors vs baseline 54/4454; newly passing 25, newly failing 5: 3 mission-caused open defects (D1 escalation_matrix :322-325 sys.modules leak breaks test_hook_antigravity resume test; D2 memory_access.py:304 storage.get vs test_16 stub; D3 eval_tokenizer_experiment.py:79 baseline follows production tokenizer), 2 Casa3D (corpus_health, node_modules symlink 23:32). Also: frontmatter added to our 5 todo-claude-* files (strict-YAML; remaining 108 unreadable notes belong to others). HEAD moved to d0e31c2f7 (2 Casa3D commits). frontend-design plugin installed 23:37 by the frontend-design session. Stray 0-byte `scratch_crlf.py` at repo root (owner unknown). Verdict/decision lines left pending independent review.
- Done (defect worker, 2026-10-22 TEST_VERIFIED): gate defects D1-D3 fixed. D1 test_cost_router_escalation_matrix.py test_f4 now uses cost_router_isolation enter/leave in try/finally (all bare lib names purged at teardown); escalation_matrix+hook_antigravity 167 passed in both orders. D2 memory_access.py propose dedup looks up only via a callable storage.get (else skips dedup; all other gates unchanged); test_book_to_memory_lifecycle_gates + redteam pass. D3 eval_tokenizer_experiment.py TOKEN_RE_BASE frozen to the historical ASCII literal; test_tokenizer_experiment_accuracy passes. Combined set 751 passed/1 skipped/3 xfailed; cost-router set 531 passed.
- Done (W7 fix worker, 2026-10-22 TEST_VERIFIED): reviewer HIGH-A (unclassified explicit model/agent default capped by session policy) and MED-C (settings-only session model = unknown, resolve_session_model_ex source) fixed in hook_agent_model.py / lib/route_state.py with test_w7_* (24) in the escalation matrix; test_cost_router_enforce.py _agent pins the payload model; MED-B recorded as ACCEPTED RESIDUAL; evidence corrections applied; full suite final2_pytest.txt 31 failed/4755 passed, 0 new failures vs final_pytest.txt (GATE_LOG.md G8).

### FINAL (main session, 2026-10-22)
- Independent review round 1 (opus): NOT SAFE — HIGH-A explicit model on unclassified brief escaped the session cap;
  MED-B state-file forgery; MED-C settings guess replaced fail-closed; MED-D dedup partial. W7 fixed HIGH-A + MED-C
  (24 test_w7_* tests, 19 fail on pre-fix code), MED-B accepted residual, evidence corrected.
- Round 2 (opus, delta-only): HIGH-A fixed, MED-C fixed (probes re-run), no regressions, 437 passed / 3 xfailed;
  new residuals R2-1..R2-4 (LOW-MED/LOW/INFO) recorded. VERDICT: SAFE FOR CONTROLLED LOCAL TESTING (conditions in
  OVERNIGHT_EXECUTIVE_REPORT.md / RELEASE_READINESS.md / evidence.json).
- Final full suite (final2_pytest.txt): 31 failed, 4755 passed, 51 skipped, 9 xfailed, 6 errors; 0 new failures vs G1;
  all remaining failures env-isolation / pre-existing / other agents (classified in GATE_LOG.md).
- Paid spend this mission: $0. Global state: no mission write (23:32 rewrite + 23:37 frontend-design install by
  session 9eb044d1, RUNTIME_VERIFIED). Nothing committed. Token verdict unchanged: NOT DEMONSTRATED.

### PHASE 6 stabilization gate (claude-code main session, 2026-10-22T07:45+03:00)
- Start: HEAD 655f829be (3 Casa3D commits after d0e31c2f7, none in mission files), 91 status lines.
- D1 test_f4 now snapshots/restores sys.modules (BARE_NAMES) + sys.path (purge-only destroyed other suites'
  modules); D2 dedup reply reports the stored lifecycle/verification (was hard-coded REVIEW); D3 STOP_BASE frozen
  literal + `arm_tokenizer()` restores the ORIGINAL production tokenizers (old code left production on ASCII).
  Tests: `20_TESTS/regression/test_phase6_stabilization.py` (4 of 11 failed before the fix). Strengthened 3 weak
  assertions (claude_hooks x2, enforce local-tier compact).
- Finding: project SessionStart `install.py --session-start` rewrites ~/.claude/skills/cost-router by default
  (temp-HOME probe; real VERSION rewritten 06:53 today, writer unidentified). Owner decision; patch proposed only.
- Review (opus, 2 rounds): H1 HIGH pre-existing (PR#195): tokenizer arms never reached controller.search
  (memory_controller.* shim aliases) -> fixed in arm_tokenizer + spy/ranking test; committed experiment artefacts
  are a null measurement (owner: rerun or mark INVALID). L1 accepted, L2/L-new fixed. Round 2 SAFE.
- Full suite phase6_pytest.txt: 26 failed / 4857 passed / 51 skipped / 9 xfailed / 6 errors, 0 new failures vs
  final2 (ran before H1; targeted set after H1 279 passed). Verdict: SAFE FOR CONTROLLED LOCAL TESTING (conditions).
- Next (owner): HIGH-2, session-start opt-in patch (proposed in report section 7), tokenizer rerun, residual table.
- Report: `07_EVALUATION/reports/overnight_reliability_2026-10-21/PHASE6_STABILIZATION_REPORT.md`.
- Owner approved (2026-10-22) ONLY the opt-in item: hook-driven refresh default OFF (install.py --session-start/
  --self-update + hook_self_update main; COST_ROUTER_SELF_INSTALL=1 or ~/.claude/cost-router.auto-refresh; opt-outs and
  disabled marker win). Review: gate OK; migration gap MEDIUM (old ungated global hook copies: owner re-runs install.py
  or --uninstall per machine; this machine has 0 user hooks); docs + marker test + f4 sys.path order fix done.
  Targeted 617 passed/4 skipped/3 xfailed exit 0. No full-suite rerun, no round-2 review of that delta (report sec. 11).
- Still open (owner): HIGH-2, sanitize_query, vault_read flag + proposal rate limit, tokenizer artefacts (rerun/INVALID).
- 2026-10-22 15:16 owner authorized global install: `install.py --no-hook --no-context` refreshed ~/.claude/skills/cost-router
  to the gated version (verify OK, VERSION 655f829be+dirty; user hooks 0, settings.json hash unchanged; agents
  already current). Backup of the previous global copy: session scratchpad `global_backup_20261022_151613`.
- NEXT SESSION (owner: "fa tot ce e recomandat"): (1) tokenizer artefacts -> label NULL MEASUREMENT/INVALID + test
  (no rerun); (2) sanitize_query: strip only real HTML tags, keep `a < b`, `List<String>`, `<DRIVE>` text, both copies
  (03_IMPLEMENTATION/packages/security/security.py, security/security/utils.py) + tests incl. <script> still removed;
  (3) vault_read additive review_required/review_reasons (verbatim text + sha unchanged) and memory_propose rate
  limit per client/window (fail closed, REVIEW only). Opus vault-reviewer before DONE; full suite once at the end.
  HIGH-2 (pinning/signing) stays an owner release decision.

### PHASE 7 (claude-code main session, claimed 2026-10-22T16:00+03:00; HEAD 655f829be, 93 status lines)
- Owned files: 07_EVALUATION/tokenizer_experiment/VALIDITY_NOTICE.md (new), 20_TESTS/test_tokenizer_experiment_accuracy.py
  (append only), 03_IMPLEMENTATION/packages/security/security.py + security/security/utils.py (sanitize_query),
  03_IMPLEMENTATION/packages/interfaces/memory_mcp_server.py + memory_access.py (vault_read flag, propose rate limit),
  new tests 20_TESTS/regression/test_phase7_*.py. Bit-for-bit TOKENIZER_EXPERIMENT_REPORT.md/JSON are NOT edited.
- Sink audit (Explore + main, SOURCE_VERIFIED): no HTML render / template / LLM-prompt sink for the query after
  sanitize_query (controller path, ToT reasoner deterministic f-strings, financial_search metadata.query JSON echo,
  vault_resolve no sink). OWNER APPROVED 2026-10-22 (variant 2): sanitize_query stops stripping HTML and `{{...}}`
  (text preserved, strip + size limit kept; escape at render sites); test_security.py:29 and test_core.py:95
  rewritten to "text preserved" as a deliberate, owner-approved contract change.
- 2026-10-22 worker: P7-1 + P7-3 done (VALIDITY_NOTICE.md; vault_read flag in MCP wrapper; propose rate limit
  per client, VAULT_PROPOSE_RATE, fail closed). Main re-run TEST_VERIFIED: tokenizer+P7-3+redteam+memory_controller
  security/core 89 passed/3 xfailed exit 0; ollama_telegram 7 failed = baseline pre-existing (CRLF).
  P7-2 BLOCKED: auto-mode classifier refused the `.strip()`-only rewrite as "Security Weaken" despite owner chat
  approval; not bypassed. Tree state: both sanitize_query copies = allowlist-tag version (interim, 82 probes green);
  test_security/test_core originals; test_phase7_sanitize_query.py encodes variant 2 and FAILS until the owner applies it.
- Main session also hit the classifier ("Security Weaken") on the variant-2 rewrite after owner "ai toate permisiunile";
  not bypassed. Ready-to-run script: session scratchpad p7_sanitize.py (owner runs it or adds a permission rule);
  then rewrite test_security.py:29 / test_core.py:93-96 as the owner-approved contract change.
- Opus review round 1: SAFE. MEDIUM interim sanitize worst case ~1 s at 4096 chars (n/3 passes; accepted until
  variant 2); residual key growth of _rate_events FIXED by main (stale buckets dropped + test
  test_expired_client_buckets_are_dropped; 64 passed/3 xfailed). Residuals: client names self-declared (rotation bypasses
  the per-client limit; no global cap); slot released on exceptions after storage.set; antigravity/CURRENT.md:65
  still cites H-TOKEN-1 INFIRMATĂ without the notice (other agent's file, not edited).
- [x] P7-1 tokenizer NULL MEASUREMENT notice + test  - [x] P7-2 sanitize_query (variant 2, applied after owner left auto mode)  - [x] P7-3 vault_read flag + rate limit
- [x] opus review (SAFE)  - [x] full suite once (phase7_pytest.txt: 32F/4963P; 10 = variant-2 test; 0 phase-7-caused)
- Report: 07_EVALUATION/reports/overnight_reliability_2026-10-21/PHASE7_REPORT.md.
- 2026-10-22 ~17:50: variant 2 APPLIED after the owner left auto mode (script p7_sanitize.py, CRLF kept); tests
  test_security.py:30,33 + test_core.py:96-97 rewritten (owner-approved). Affected sets 455 passed/3 xfailed exit 0,
  P0 11 passed. Round-2 opus delta review SAFE (old vs new identical on all 252 frozen benchmark queries).
  P7-2 DONE. Desktop Commander remote started by owner request in Terminal tab c2 (LAB-PC online).
- NEXT (owner): commit decision for phase 6+7 files; HIGH-2 release decision; optional note in the 2 stale docs.

### NEXT SESSION (owner decisions 2026-10-22, AskUserQuestion)
PHASE 8 = commit/PR (owner: "Ramură nouă + PR draft"). Do NOT touch the shared tree's branch/index:
1. List mission-owned files: overnight deliverables dir, our todo-claude-overnight-reliability.md, phase-6/7 files named
   above (PHASE6_STABILIZATION_REPORT.md file table + PHASE7_REPORT.md), W1-W7 files in the "Done" ledger. For files
   ALSO dirty from other agents (e.g. .claude/skills/cost-router/*, memory_access.py, eval_tokenizer_experiment.py,
   20_TESTS/test_cost_router_*): check `git diff` hunks; include only if every hunk is ours, else list as excluded.
2. `git worktree add <scratch>/p8 -b claude/overnight-reliability-p6-p7 655f829be` (or current origin HEAD), copy the
   owned files in, run the owner-privacy pass (AGENTS.md "Owner privacy": no drive-letter user paths, emails, machine
   data in committed text — reports cite C:\Users\... paths: scrub to placeholders), run the targeted suites there.
3. Commit (attributed), push -u, draft PR (check PR template), ccd_pr get_status/bind_pr, check CI once.
PHASE 9 = HIGH-2 (owner: "Implementează pin + hash"): hook_self_update/install.py --self-update must fetch a PINNED
ref (tag or commit SHA from a committed config, never moving `main`) and verify a committed SHA-256 manifest of the
installed files BEFORE running the installer; mismatch/missing manifest -> abort, nothing installed (fail closed);
refresh stays opt-in (COST_ROUTER_SELF_INSTALL / marker). Threat model in the worker brief: tampered remote, ref
rewritten, manifest swapped together with files (manifest must come from the pinned commit, not the fetched tree's
head), partial download, path traversal in manifest entries, downgrade to an older pin. Worker sonnet + opus
vault-reviewer (2 rounds max). Global ~/.claude refresh only with owner approval; tests with tmp --home.

### PHASE 8 (claude-code main session, claimed 2026-10-22T18:30+03:00; local HEAD 655f829be, origin/main f0ee40d67)
- Base = origin/main (PR 304 squash-merged; origin/main differs from 655f829be in 10 files, 2 of ours: hygiene
  comments in test_cost_router_self_update/skill -> our diff applied on top). Worktree on a scratch path, branch
  claude/overnight-reliability-p6-p7; shared tree branch/index untouched.
- Scope = the whole Claude Code cost-router line (phases 1-4 + overnight + 6/7): hunks of phases 1-4 and of the
  mission share files and cannot be split. EXCLUDED (other owners): .agents/skills/**, test_hook_antigravity.py,
  skill_router.py, todo-antigravity-*, docs/reports/COST_ROUTER_*.md, Casa3D, prompt-compiler-executor, frontend-design
  line (creative-web-director SKILL, install_design_stack + test, frontend_design_*, todo-claude-frontend-design),
  docs/research/, scratch_crlf.py; raw benchmark evidence dirs 07_EVALUATION/reports/cost_router_claude_* (1526 files,
  transcripts -> privacy; stay local).
- Fixes made for the branch: phase-6 D1 order test now targets a COMMITTED antigravity test
  (test_hook_send_message_blocks_excessive_resumes; the old target was Antigravity uncommitted work); .gitignore
  re-includes 20_TESTS/fixtures/telemetry/ (was swallowed by `telemetry/`); 2 small raw evidence files the tests read
  are included (privacy-scanned clean). Raw pytest logs + state_start.txt NOT committed (machine paths; stay local).
- Tests in the worktree: targeted 1313 passed (9 failed -> fixed above, re-run 217 passed/2 skipped). Full suite
  (GATE_LOG command) hit C: disk full (5.5 GB free, 99%): 81F/136E mostly ENOSPC; re-run of every affected file:
  14 failed/700 passed/2 errors = 9 test ids, ALL in the phase-7 failure list (0 new). Full-suite pass count
  4717 passed. Owner: free disk space before the next full run.
- Next: PHASE 9 (HIGH-2 pin + manifest hash) in a new session, brief above.
