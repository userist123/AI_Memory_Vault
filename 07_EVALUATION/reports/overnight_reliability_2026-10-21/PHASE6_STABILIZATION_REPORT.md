# Phase 6: stabilization gate report (2026-10-22)

VERDICT: SAFE FOR CONTROLLED LOCAL TESTING

This verdict is narrower than "the code is fine". It holds only under every condition in section 6. Those
conditions include running repo-root sessions with `COST_ROUTER_NO_SELF_INSTALL=1`. Section 7 explains why: the
default session start copies the dirty working tree into `~/.claude`.

This is not production readiness. TOKEN SAVINGS NOT DEMONSTRATED (unchanged). Nothing was committed, pushed,
merged, installed globally or registered as a hook. Paid spend in this phase: $0, because no benchmark or
`claude -p` run was made.

Evidence labels: SOURCE_VERIFIED, TEST_VERIFIED, RUNTIME_VERIFIED, INFERRED, UNVERIFIED.

## 1. Repository state

| | Start | End |
|---|---|---|
| Branch | `gemini/casa3d-nextgen-functionality-uiux` | unchanged |
| HEAD | `655f829be` | `655f829be` |
| `git status --porcelain` lines | 91 | 92 (+ the new test file; this report sits in an already-untracked directory) |

- The gate recorded HEAD `d0e31c2f7`. Since then three commits from another agent have landed: `8c8616e76`,
  `d503e6ab9` and `655f829be`, covering Casa3D vault checks and checkpoints.
- `git diff --stat d0e31c2f7 HEAD` shows 7 files. None of them is a mission file (SOURCE_VERIFIED).

## 2. Files changed in phase 6, and who owns them

| File | Owner | Change |
|---|---|---|
| `20_TESTS/test_cost_router_escalation_matrix.py` | mission (untracked) | D1: the f4 test snapshots and restores `sys.modules` and `sys.path` |
| `03_IMPLEMENTATION/packages/interfaces/memory_access.py` | mission hunk (MED-4 dedup) | D2: the dedup reply reports the stored lifecycle and verification |
| `30_SCRIPTS/evaluation/eval_tokenizer_experiment.py` | mission D3 hunk inside the PR #195 file | D3: `STOP_BASE` frozen; `arm_tokenizer()` patches every alias and restores the originals; the bad post-run "restore" removed |
| `20_TESTS/regression/test_phase6_stabilization.py` | phase 6 (new) | 13 regression tests |
| `20_TESTS/test_cost_router_claude_hooks.py` | earlier Claude cost-router phases plus mission (untracked) | 2 assertions pinned |
| `20_TESTS/test_cost_router_enforce.py` | W7 hunk (mission) in an earlier-phase file | 1 assertion added (compact output) |
| `00_GOVERNANCE/coordination/tasks/todo-claude-overnight-reliability.md` | mission checkpoint | phase-6 entry |

- Not touched: any Antigravity file (`.agents/**`), `.claude/settings.json`, `cost_router_isolation.py`, the
  protected cognitive core, other agents' notes, and the `07_EVALUATION/tokenizer_experiment/*` artefacts.
- One temporary repro file, `20_TESTS/test_zz_p6_repro_pre_d1_tmp.py`, was created to reproduce D1 and deleted
  straight after. Its absence was verified with `ls`.

## 3. D1, D2 and D3

All commands use the environment below. `<env>` stands for it.
- Temporary `HOME`, `USERPROFILE` and `APPDATA` (scratchpad).
- `PYTHONPATH=<user site-packages>`.
- `COST_ROUTER_NO_SELF_INSTALL=1`, with `COST_ROUTER_OFF` unset.
- `python -m pytest -p no:cacheprovider`.

### D1: `sys.modules` leak between test files

**Reproduction of the original defect** (a copy of the matrix file with no teardown, run before the Antigravity
test):

    <env> pytest 20_TESTS/<copy>::test_f4_transcript_model_wins_over_a_stale_stored_value 20_TESTS/test_hook_antigravity.py::test_send_message_resume_idempotent_on_same_step

- Result: `AttributeError: module 'transcript_usage' has no attribute 'is_parent_session'`, 1 failed, 1 passed,
  exit 1 (RUNTIME_VERIFIED).
- The independent reviewer reproduced the same mutation separately.

**What the earlier defect worker left.** It purged the names on teardown. That passed the order tests, but it
destroyed modules that other suites had already loaded. That is not a restore.
- The new probe `test_d1_f4_restores_pre_existing_bare_modules_and_leaves_absent_names_absent` failed on that
  code: `assert None is <module 'route_state'>`.

**Minimal change.**
1. Before `iso.enter`, record a snapshot `{n: sys.modules[n]}` for every `BARE_NAMES` entry that is present, and
   copy `sys.path`.
2. In `finally`, after `iso.leave`: `sys.modules.update(saved)` and `sys.path[:] = saved_path`.

The result: names that existed before come back as the same objects, and names that did not exist stay absent.

**Regression tests.**
- The in-process probe (sentinel module object, plus a name that must stay absent).
- `test_d1_matrix_and_antigravity_pass_in_both_orders`: runs pytest in a subprocess, matrix-first and
  antigravity-first.

**Final results.**

| Test files, in order | Result |
|---|---|
| matrix, then antigravity | 267 passed |
| antigravity, then matrix | 267 passed |
| claude_hooks, then antigravity | 120 passed |
| antigravity, then claude_hooks | 120 passed |
| matrix, claude_hooks, antigravity | 369 passed |
| antigravity, claude_hooks, matrix | 369 passed |

All six runs were TEST_VERIFIED in one loop. That loop does not capture a reliable exit code for each run, so the
pass counts come from pytest's summary line, which reports 0 failed. The phase-6 regression file passes with exit 0.

### D2: `controller.storage.get` against a stub storage

**Intended interface.**
- `get` is part of the StorageEngine contract. It is implemented by the base `StorageEngine` (`memory/controller.py:55`),
  by `FileStorageEngine` (`file_engine.py:191`) and by `SQLiteStorageEngine` (`sqlite_engine.py:119`).
- `MemoryController.propose` calls `self.storage.get(note_id)` itself and refuses an existing id (A-08,
  `controller.py:1580-1591`).
- Both production consumers, `memory_mcp_server.py` and `vault_cli.py`, use these engines.
- Conclusion: a storage without `get` can never reach production (SOURCE_VERIFIED; the reviewer confirmed it).

**What the earlier defect worker left.** The dedup lookup runs only when `get` is callable. That does not bypass
the lifecycle gate: every input gate, `_assert_outside_slots` and `controller.propose` still run.
- Additional defect, carried over from the B-MED-4 status: the dedup reply hard-coded `REVIEW`/`unverified`, even
  when the owner had since changed the note.
- The new probe failed on that code: `'REVIEW' == 'ARCHIVED'`.

**Minimal change.** The reply reports the stored note's actual `lifecycle` and `verification`. The comment now
documents that `get` is the contract. Gates and the write path are unchanged.

**Regression tests.** Four tests:
- A stub storage without `get`: it still passes through the controller in `REVIEW`, and the earlier gates still
  refuse `source_type=user` and an empty title.
- A `get` attribute that is not callable is treated as absent.
- `get` is the contract: checked through `inspect` on `MemoryController.propose`.
- Production `FileStorageEngine`: dedup works, the reply reports `ARCHIVED` after the change, and dedup never
  rewrites the note.

**Final results.** `test_book_to_memory_lifecycle_gates.py` 57 passed (reviewer run); redteam 39 passed and
3 xfailed; `test_memory_access.py` 26 passed. TEST_VERIFIED.

### D3: the tokenizer experiment baseline

**Reproduction.**
1. `test_d3_baseline_does_not_follow_the_production_module` failed: `STOP_BASE is hr.STOP`. The baseline still
   imported production.
2. A source inspection found a post-run "restore" that assigned `tokenize_baseline` to production, which left the
   process on the ASCII tokenizer.
3. **Review H1 (HIGH, present since c5f939185 / PR #195, not introduced in this mission).** The arms patched only
   `retrieval.hybrid_retrieval` and `retrieval.context.candidate_generation`.
   - `MemoryController` calls different module objects: `memory_controller.context.candidate_generation` and
     `memory_controller.hybrid_retrieval`. The `memory_controller/__init__.py` `__path__` shim loads the same
     files a second time under those names.
   - Probe: `sys.modules[c.generate_candidates.__module__] is cg` returned False (RUNTIME_VERIFIED).
   - A spy tokenizer was never called by `controller.search`.
   - Consequence: every arm of the experiment measured the production tokenizer.

**Minimal change.**
- `STOP_BASE` is a frozen literal, identical to the HEAD value.
- `TOKEN_RE_BASE` was already frozen.
- `arm_tokenizer(tok_fn)` is a context manager. It patches all four bindings and restores the originals in
  `finally`, including when the arm raises.
- `evaluate_arm` uses it.
- The old post-run reassignment was removed.

**Regression tests.**
- The pinned historical pattern and stop list, with the exact baseline tokens (`tiin`, `ific`, `reg`, `sire`).
- Independence: the baseline output is unchanged when `hr.TOKEN_RE`, `hr.STOP` and `hr.tokenize` are monkeypatched.
- The old and new behaviour can be told apart: diacritics, and the 1-character id "7".
- Restore across all four modules, including on an exception.
- `test_d3_the_arm_tokenizer_reaches_controller_search`:
  - The spy is called by a real search.
  - The fused ranking differs between the baseline arm and a degenerate tokenizer.
  - Mutation check: with the old `retrieval.*`-only patch, this test fails ("never called").

**Final results.**
- Phase-6 file plus `test_tokenizer_experiment_accuracy.py`: 21 passed, exit 0.
- Delta review in round 2: SAFE. No `tokenize` binding is left unpatched on the search path, and the arm changes
  the ranking.

**Not done: owner decision.** The committed artefacts in `07_EVALUATION/tokenizer_experiment/*` are a null
measurement. All three arms show an identical 21/130, and the verdict "MENȚINERE BASELINE / INFIRMATĂ" is not
evidence about tokenizers.
- `test_tokenizer_experiment_accuracy.py` only re-checks the frozen JSON.
- Rerunning and regenerating would rewrite PR #195's committed results on a vault that has changed since. The
  owner must choose: rerun and regenerate, or label the artefacts INVALID.

## 4. Tests run

| Command (with `<env>`) | Result | Exit |
|---|---|---|
| Phase-6 file, before the fixes | 4 failed, 7 passed | 1 |
| Phase-6 file + tokenizer accuracy + lifecycle gates + redteam + `test_memory_access` (after D1/D2/D3, before H1) | 142 passed, 3 xfailed | 0 |
| Hook files in 6 orders | see D1 | see D1 |
| 3 strengthened assertions | 3 passed | 0 |
| After H1 and L-new, the full set (list below) | 279 passed, 3 xfailed | 0 |
| `test_notes_strict_yaml.py` | 1 failed, 9 passed | 1 |
| `git diff --check` on the tracked files changed here; trailing-whitespace grep on the untracked ones | clean | — |

The full set after H1 and L-new: phase-6, tokenizer accuracy, lifecycle gates, redteam, `test_memory_access`, the
overnight retrieval regression, `test_cost_router_enforce`, `self_update`, `skill`, and
`test_claude_cost_router_benchmark`.

**Assertions strengthened (step 2).** Each one was checked against the policy, not only against the current output;
the reviewer confirmed this (P4).
- `test_compact_route_line_names_a_high_risk`: `risk=high or risk=critical` became the exact line
  `implement -> vault-worker subagent, sonnet risk=high`.
- `test_agent_allow_context_only_when_verbose_...`: the substring `"cost-router"` became the exact reason and the
  exact `updatedInput` (an opus session; explore with opus is capped to sonnet).
- `test_prompt_hook_advertises_the_local_tier_only_when_it_answers`: now also asserts that the default compact line
  never advertises the local LLM.

**Review of the `test_cost_router_enforce.py` diff (pre-W7 hunks).** No assertion was weakened (SOURCE_VERIFIED; the
reviewer independently agreed, P5):
- Substring checks became exact equality.
- The unknown model `gpt-9` moved from "left untouched" to fail-closed with an exact note.
- `"class=explore" in stdout` became exact JSON.
- Two things are now only covered elsewhere:
  - `COST_ROUTER_VERBOSE_ROUTE=1` moved the local-tier check to verbose mode. The default mode is now covered
    by the new assertion.
  - Pinning `model: opus` in `_agent` removes the settings-file session path from those cases. The matrix tests
    `test_w7_med_c_*` cover that path.
- Kept as is: `test_parallel_enforce_hooks_...`, which asserts `deny >= 4` with racing parallel calls. This is a
  documented best-effort race, not a weakened invariant.

## 5. Full suite

- Command: `<env> pytest 20_TESTS security/tests -q -rfE --continue-on-collection-errors`. It completed in
  1788.8 s with exit 1. Log: `phase6_pytest.txt`.
- Timing caveat: the run started after D1, D2 and the first D3 fix, but before H1 and L-new. Those two changes are
  covered by the targeted set (279 passed).

| Run | failed | passed | skipped | xfailed | errors |
|---|---|---|---|---|---|
| Baseline | 54 | 4454 | 51 | 6 | 6 |
| G1 | 34 | 4728 | 51 | 9 | 6 |
| final2 | 31 | 4755 | 51 | 9 | 6 |
| **Phase 6** | **26** | **4857** | **51** | **9** | **6** |

Comparison by unique test id, with parametrisation stripped (25 ids now, 29 in final2, 40 in the baseline):

| Change | Count | Ids and cause |
|---|---|---|
| New failures | **0** | — |
| No longer failing | 4 | `bootstrap_cold_start_paths`, `readme_references::test_every_workflow_is_named`, `vault_state_accuracy::test_edge_count_is_current`, `workflow_policy_real_files`. INFERRED to be fixed by another agent's commit `8c8616e76` ("repara verificarile vault"); not by this phase. |

The 25 remaining ids, by category:

| Category | Ids |
|---|---|
| Environment / isolation | `test_memory_mcp_server`, `vault_access_clients`, `vault_routes_quarantine_parity` and `security/tests/test_prompt_compiler` (collection errors: pywintypes/jsonschema missing with a temporary APPDATA); `vault_access_perf` ×4; `memory_consolidation_command`; `audit_remediation u01`; `vault_access_core::test_unreadable_or_unusual_frontmatter_fails_closed` (environment variable over 32767 characters); `import_external_skills` (exec bit on Windows) |
| Pre-existing at HEAD | `vault_access_ollama_telegram` (CRLF fixture); `vault_state_accuracy` ×2 (count drift); `repository_layout` (docstring in `routing/claude_usage_report.py:58`); book_to_memory / b2m artefact drift ×5; `vault_access_core::test_read_is_verbatim...` (CRLF) |
| Caused by another agent's change | `corpus_health` ×2 (Casa3D `node_modules` symlink); `notes_strict_yaml` (95 unlisted unreadable notes: 89 Casa3D workspace, 5 `CASA3D_*.md`, 1 `todo-claude-frontend-design.md`; no mission file) |
| Introduced by this mission | **none** |
| Unknown | none |

The other agents' notes were left untouched.

**Working-memory admission (path 4) and provider telemetry (path 7): UNVERIFIED.** Neither was traced in this phase,
and no claim is made from adjacent tests.

## 6. Security residuals and owner decisions

**Trust boundary.** The cost-router hooks are a cost-policy aid. They are not a security boundary.
- The route state (including `escalation_authorized`), the telemetry sidecar and the resume counter are
  user-writable files under `~/.claude/cache`.
- HMAC protects nothing against an agent that can write those files and read the execution environment that holds
  the key. The key is in that process's `os.environ`.
- The actual boundary is tool permissions: deny Write/Bash outside the project, and protect the environment.

| Item | Status | Class |
|---|---|---|
| HIGH-2: unpinned, unsigned remote installer (`hook_self_update.py` clones `main` and runs its `install.py`) | **OPEN BLOCKER**; no pinning or signing exists; proposal in `SECURITY_REDTEAM_RESULTS.md`. Inactive: no cost-router hook is registered in the user settings (0 hooks; read-only stat, RUNTIME_VERIFIED). | Owner, release process |
| Implicit global self-update via the **project** SessionStart (`install.py --session-start`) | **OPEN** (section 7) | Owner decision (product behaviour approved on 2026-10-10) |
| Forged `escalation_authorized` (MED-B) | Accepted residual; trust boundary as above | Owner (accepted) |
| `vault_read` returns raw text without a `review_required` flag | OPEN; changes the verbatim-route contract and parity tests in vault routes | Owner + route owner |
| Summarisation (RT03) and evaluator prompt injection (RT16) | OPEN; xfail tests | Design decision |
| No proposal rate limit (dedup covers exact repeats only) | OPEN | Owner (limits per principal and time window) |
| Antigravity `lib/route_state.py` resume state without a lock | OPEN | Antigravity owner; **not authorized** |
| Telemetry sidecar forgery (RT15) | OPEN; same trust boundary as MED-B | Owner (accept or authenticate) |
| `sanitize_query` deletes everything between `<` and `>` | OPEN, reproduced: `'a < b and c > d' -> 'a  d'`, `'List<String> generics' -> 'List generics'`, `'<DRIVE>:/x' -> ':/x'`. It loses query text and adds no protection, because the query is never rendered as HTML. Two copies: `security/security.py` and `security/security/utils.py`. | Owner (security-labelled code) |
| Dedup reply via `storage.get` (review L1) | Accepted: it reports the lifecycle of notes that `memory_get` would refuse, but only for an id derived from content the caller already holds. A quarantined text cannot be proposed again. | Low, documented |

**Conditions attached to the verdict.**
1. HIGH-2 stays a blocker. The self-update hook stays unregistered at user level.
2. Repo-root sessions run with `COST_ROUTER_NO_SELF_INSTALL=1`, or the owner explicitly accepts the global refresh
   from the dirty tree.
3. The accepted residuals (MED-B, R2-1..R2-3, RT03/RT15/RT16) stay recorded.
4. No result from the tokenizer experiment is used as evidence.
5. Nothing is committed, pushed or installed without the owner.

## 7. Global state

**Tested with temporary HOME directories only** (`USERPROFILE`/`HOME`/`APPDATA` temporary; the real HOME was never
touched; RUNTIME_VERIFIED):

| `install.py --session-start` (what the project SessionStart runs) | Exit | Files written | Skill installed |
|---|---|---|---|
| Default (no switch) | 0 | 21 | **YES** |
| `COST_ROUTER_NO_SELF_INSTALL=1` | 0 | 0 | no |
| `COST_ROUTER_OFF=1` | 0 | 0 | no |
| Disabled marker | 0 | 0 new | no |

- `hook_self_update.py` with the switch set: exit 0, 0 files. With the switch unset it was **not** run, because it
  would fetch remote code.

**Real global state** (read-only stat):
- `~/.claude/skills/cost-router/VERSION` was rewritten on 2026-10-22 at 06:53:00. Its content is
  `d0e31c2f7…+dirty`: a copy of the dirty shared working tree.
- The writer is UNVERIFIED. INFERRED: the SessionStart of a repo-root session after the overnight gate. It was not
  any command of this phase.
- `~/.claude/agents/*.md` are unchanged (2026-10-13).
- User settings: 0 hooks.

**Conclusion.** The default start path **can** rewrite the global skill, so the requirement fails without the
switch.
- No opt-in mechanism exists. `COST_ROUTER_NO_SELF_INSTALL=1` and the disabled marker are opt-outs.
- The owner approved this behaviour on 2026-10-10, and other sessions depend on it, so changing the default is an
  owner decision. **Not implemented.**

**Smallest safe patch proposed** (for owner approval):
- In `install.py main()`, `--session-start` returns 0 with no writes unless `COST_ROUTER_SELF_INSTALL=1` or the
  marker `~/.claude/cost-router.auto-refresh` exists.
- `hook_self_update.py` gets the same gate, in addition to HIGH-2.
- Tests: the disabled default, opt-in by environment variable, opt-in by marker, and that the opt-out keeps
  priority. All with a temporary `--home`.
- Rollback: delete the gate.

## 8. Independent review (vault-reviewer, Opus, read-only; 2 rounds)

**Round 1.** SAFE for D1, D2 and the assertions; NOT SAFE for D3.
- H1 (HIGH): the arms never reached `controller.search` (pre-existing, PR #195). **Fixed.**
- L1 (LOW): the dedup reply reads storage directly. **Accepted**, documented in section 6.
- L2 (LOW): a hard-coded name list in the D1 test. **Fixed**: the test now imports `cost_router_isolation.BARE_NAMES`.
- L3 (LOW): session start copies the dirty tree. **Documented** in section 7.
- The reviewer also checked P4 and P5, with no weakened assertion found, and reproduced the D1 mutation.

**Round 2 (delta only).** SAFE.
- No unpatched binding; the arm changes the ranking; restore works.
- L-new (LOW): the spy test checked only that the tokenizer was called. **Fixed**: it now also requires a different
  fused ranking.
- Residual: the experiment artefacts are a null measurement (owner).

No Critical finding. The only High finding is fixed.

## 9. Rollback

Copy each file before you roll it back. `git checkout` restores HEAD and also discards earlier uncommitted phases.

| Change | How to roll back |
|---|---|
| D1 | In `test_f4_...`, remove `saved_modules`/`saved_path` and the two restore lines (the file is untracked; there is no HEAD version) |
| D2 | In `memory_access.propose`, replace the `existing` block with the hard-coded `REVIEW`/`unverified` reply |
| D3 | In `eval_tokenizer_experiment.py`: `STOP_BASE = hr.STOP`; remove the two `memory_controller.*` imports and `arm_tokenizer`/`_evaluate_arm_patched` (inline the patch back into `evaluate_arm`) |
| New test file | Delete `20_TESTS/regression/test_phase6_stabilization.py` |
| Assertions | Revert the 3 assertion hunks (comments marked "phase 6") |

Runtime switches (no file change): `COST_ROUTER_OFF=1`, `COST_ROUTER_NO_SELF_INSTALL=1`.

## 10. Verified and unverified subsystems

**VERIFIED** (TEST_VERIFIED in this phase, unless noted):
- D1 test isolation, in 6 file orders plus the in-process restore.
- D2 dedup and the lifecycle gates, with the stub and with production storage.
- D3 frozen baseline, independence, and that the arm reaches the search and changes the ranking.
- Restore of production tokenizers.
- Escalation matrix and Antigravity hooks (267/369 passed).
- Claude hooks (102), enforce (23), redteam (39 passed + 3 xfail residuals), `memory_access` (26), retrieval
  regression, self-update/skill switches.
- Session-start switches under a temporary HOME (RUNTIME_VERIFIED).
- Full suite: 0 new failures.

**UNVERIFIED or not demonstrated:**
- The tokenizer experiment's conclusion: null measurement, owner decision.
- Working-memory admission (path 4); provider telemetry (path 7, protected core).
- Which session wrote the global copy at 06:53.
- Live behaviour of the hooks in a real paid session (no paid run).
- Whether MCP consumers surface the corrupt-note count.
- Token savings: **NOT DEMONSTRATED**.
- Production readiness: **not claimed**.

**OPEN BLOCKERS:**
- HIGH-2.
- Default-on global session-start refresh.
- The residuals in section 6.

## 11. Addendum: default-off global refresh (owner decision 2026-10-22, implemented)

The owner approved the opt-in item and only that one. sanitize_query, vault_read, the rate limit and the tokenizer artefacts remain open.

**Change**
- `.claude/skills/cost-router/install.py`:
  - New `OPT_IN_MARKER = "cost-router.auto-refresh"` and `auto_refresh_opted_in(home)`.
  - In `main()`, after the existing opt-outs, `--session-start` and `--self-update` return 0 silently unless the owner has opted in, via `COST_ROUTER_SELF_INSTALL=1` (exactly "1") or `<home>/.claude/cost-router.auto-refresh`.
  - `--uninstall` removes the opt-in marker.
  - The docstring and the post-install message now say refresh is off until opt-in.
- `.claude/skills/cost-router/hook_self_update.py`: the same gate in `main()`, placed before any read, git call or `update()`. The docstring states the trust model: HIGH-2 is still open, and older installs need migrating.
- `.claude/skills/cost-router/SKILL.md`: one bullet.
- Precedence: opt-outs (`COST_ROUTER_NO_SELF_INSTALL=1`, `COST_ROUTER_OFF`) win over the disabled marker, which wins over the opt-in.

**Effect**
- The project SessionStart (`.claude/settings.json`, file unchanged) no longer rewrites `~/.claude/skills/cost-router` by default.
- This was RUNTIME_VERIFIED in tests: the registered bash command, run against a temp HOME, writes 0 files without an opt-in.

**Tests**
- New, in `test_phase6_stabilization.py`:
  - The default writes nothing, for both modes.
  - Opt-in by env and by marker each install.
  - Uninstall removes the marker.
  - Each opt-out beats the opt-in.
  - The disabled marker beats the opt-in.
  - The hook's `main()` calls `update()` only when opted in, by env or marker, and the hook itself writes nothing.
  - Before the implementation, 4 of these failed.
- Changed to describe opted-in behaviour; no assertion was removed:
  - The autouse fixture in `test_cost_router_self_update.py` now sets `COST_ROUTER_SELF_INSTALL=1`.
  - `test_cost_router_skill.py::test_session_start_command_runs_quietly_and_installs` first asserts the default writes 0 files, then installs once opted in.
  - `test_uninstall_stops_the_session_start_reinstall` now runs opted in, so it proves the uninstall still wins.

**Independent review** (vault-reviewer, Opus, new agent): PARTIAL. The gate logic and precedence are correct, verified by probes. Its findings:
1. MEDIUM, migration gap. A full install made before this change keeps the old ungated `~/.claude/skills/cost-router/hook_self_update.py`. If that hook is registered, it still clones unpinned `main` every hour. The new installer is then inert, so the old copy never updates itself.
   - Not exposed on this machine: user settings register 0 hooks (read-only check by the reviewer).
   - Owner action on any machine that registered the hook: run `install.py` once by hand, or `--uninstall`. Documented in the hook's docstring.
   - Not implemented: a mechanism that lets a non-opted `--self-update` swap in the gated hook. That would itself be a global write fetched remotely.
2. LOW, stale trust-model text in install.py and hook_self_update.py. Fixed.
3. LOW, no hook test for the marker opt-in. Fixed: there is now an `opted-in-marker` parameter.
4. Order-dependent failure of `test_d1_f4_restores_...` in the combined run. Reproduced as 1 failed / 607 passed.
   - Root cause: `cost_router_isolation.enter()` inserts the `.claude` lib only when it is absent from `sys.path`, so it can sit behind the Antigravity lib.
   - Fixed in f4 by forcing the lib to the front. After the fix: 608 passed, exit 0.
   - The shared helper has the same latent weakness. It was not changed (earlier-phase file).

**Final targeted run** (temp HOME, user site, `COST_ROUTER_NO_SELF_INSTALL=1`): every `20_TESTS/test_cost_router_*.py`, plus `redteam`, `test_hook_antigravity.py`, the phase-6 file and `test_install_design_stack.py`. Result: **617 passed, 4 skipped, 3 xfailed, exit 0**. `git diff --check` reports nothing for the changed tracked files.

**Not done**
- No full-suite rerun after this addendum. The section 5 result predates it.
- No second independent review round on the reviewer-fix delta (docs, one test parameter, the f4 `sys.path` line).
- Not changed: Antigravity (`.agents`), `~/.gemini`, the real `~/.claude`.
- Reported by the reviewer, not changed: `hook_repo_map.py` writes a cache under `~/.claude/cache/repo-map/` on every session start (cache only, no code).

**Rollback**
- Remove the `auto_refresh_opted_in` gate lines and `OPT_IN_MARKER` from install.py and hook_self_update.py.
- Revert the fixture line in `test_cost_router_self_update.py` and the two hunks in `test_cost_router_skill.py`.
- Delete the opt-in section of `test_phase6_stabilization.py`.
- Without editing files, to keep refresh on: set `COST_ROUTER_SELF_INSTALL=1`, or create the marker.

**Verdict:** still SAFE FOR CONTROLLED LOCAL TESTING. Condition 2 in section 6 is now met by default: repo-root sessions no longer rewrite the global skill without opt-in. HIGH-2 remains an OPEN BLOCKER for anyone who opts in.
