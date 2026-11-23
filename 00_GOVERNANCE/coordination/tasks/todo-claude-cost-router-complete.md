# Checkpoint: cost-router complete (global, specialist registry, Agent Router on demand)

**Branch:** `claude/cost-router-complete`, stacked on PR #404 (`claude/cost-router-opus-advisor-fable52`, 99bcaa8ab).
**Owner asks (2026-11-22):** every task, new or resumed, in every project goes through cost-router; Haiku/Sonnet execute, Opus advises and reviews, Opus or Fable execute only after a failed check; a specialist registry by domain with metadata so the AI decides fast; the vault Agent Router when Opus asks; Antigravity's work merged.
**Design source:** fable-consultant synthesis of Codex + Gemini research (bridge task 732987cf), adopted by Opus with one decision: Fable stays consultant-only in this release; `fable-executor` is M11, default off.
Paths below: `S` = `.claude/skills/cost-router`.

## Milestones (one Sonnet worker each; Opus vault-reviewer after M4 and M9)
- [x] M1 `install.py --verify` works from the installed copy (today `REPO = SKILL_SRC.parents[2]` resolves to HOME, `S/install.py:74-77,361-362`); add `--source-root`; also verify hook registration + `onFailure`. Test: temp-HOME install, installed `--verify` exit 0, tamper -> 1.
- [x] M2 Pinned vault roots (`config.json` written at install) replace the layout check `route_state.in_vault` (`S/lib/route_state.py:447-451`) before `hook_agent_model` runs `routing.route_cli` with the project as cwd (`S/hook_agent_model.py:70-111`). Test: fake project whose `route_cli.py` writes a sentinel -> no sentinel.
- [x] M3 Shadow check by release-pin `seq`, not byte equality (`S/hook_agent_model.py:243-266,751-758`): equal seq + other bytes defers; higher project seq exits 2.
- [x] M4 `COST_ROUTER_OFF` must not skip the Fable guard (`:716-721`); privileged agent names (`fable-consultant`, reviewers) resolved from a project `.claude/agents` must match a pinned sha256 (`:341-392`). `#escalate`/`#direct` count only on line 1 of the owner prompt (`S/hook_prompt_route.py:104-123`).
- [x] M5 Metadata: sidecar `route.meta.json` per skill + `S/roles/<name>.json` per agent (never frontmatter: the consultant validator needs an exact key set). Compiler `S/lib/registry_compile.py` (stdlib, install time, never in a hook) -> `registry.json` with sha256 per artifact; unknown key / duplicate id fail; skip `*.bak-*`.
- [x] M6 Specialist selection in the route line (`spec=<id>`); text picks only an id from the closed set and can only raise risk/tier; golden set ~60 RO/EN prompts >= 90%; 20 injection prompts change nothing.
- [x] M7 `S/lib/vault_router.py`: on-demand Agent Router for Opus from any project; receipts recognised by `hook_enforce_route`.
- [x] M8 `S/lib/check.py` acceptance receipts + `check-runner` agent (haiku, no write).
- [x] M9 Global install on the PC: backup `~/.claude/settings.json`, pinned user-scope hooks, `--verify` 0, smoke in 2 non-vault projects + 1 vault worktree + 1 resumed session; `--uninstall` restores.
- [x] M10 Roles: `dfir-analyst` (local_only), `test-author`, `repro-debugger` template; registry entries for analist-juridic, aplicatie-securizata, surse-web, agent-bridge, animatie-3d, clips-kitty, prompt-director (overlay).
- [x] M11 `fable-executor`, default off, own threat model + review.
- [ ] Antigravity branches: Explore inventory with diffstat; port only what passes `20_TESTS/test_cost_router_*.py`.

## Not to build
LLM calls or embeddings in hooks; metadata in frontmatter; an automatic Agent Router gate outside pinned vault roots; human-confirmation prompts; self-update following `main`.

## Recorded conflict
Owner memory says "worker = general-purpose + model fable"; the hook rewrites any non-consultant Fable spawn to Opus (`S/hook_agent_model.py:663-664`). Kept until M11.

## M1-M4 done (2026-11-22, branch claude/cost-router-complete; tests 20_TESTS/test_cost_router_global_boundary.py, 16 tests)
- Pins: `~/.claude/skills/cost-router/config.json` (vault_roots, written by install.py; `--pin-root` for worktrees) and `agents.pinned.json` (source sha256 of the agents). Only the installed copy is ever read for trust data.
- Residuals: (a) an opus agent whose project definition differs from the pinned one is NOT swapped for the installed one (Claude Code resolves project before user scope; the hook can only flag it and keep the Opus floor); (b) an attacker project with a higher release seq makes spawns in THAT project exit 2 (stale-install signal, by design); (c) an install made before M4 has no agents.pinned.json, so P4 is inactive until `install.py` is re-run; (d) `release_pin.json` was not advanced (owner action): it still lists the pre-M1 file hashes.
- `.agents/skills/cost-router/hook_antigravity.py`: `#direct` first-line rule ported; its route_state/in_vault only denies (runs nothing), not changed.

## Review round 1 fixes (2026-11-22, same branch; tests in 20_TESTS/test_cost_router_global_boundary.py, now 28)
- P3: OFF no longer skips the guard for CLAUDE_CODE_SUBAGENT_MODEL=fable, `inherit` (input or frontmatter) in a Fable session (759cd0cd3).
- P4: tolerant scan of every project `.claude/agents/**/*.md` for a privileged name/stem (comment, BOM, zero-width, NFKC, casefold); missing `agents.pinned.json` blocks the consultant, notes + Opus floor for reviewers; explicit model=opus no longer skips the check; install backs up an edited consultant/reviewer to `*.bak-<ts>` and installs the pinned one (561d6c323). Fixtures that spawn the consultant now pin the agent hashes in their temp HOME.
- P1: stored roots are compared as stored strings (never re-resolved); a pinned path that is now a link/junction is refused; registered hooks run `python -I` (0939121ba).
- P6: `--verify` checks config.json + agents.pinned.json against `pins.sha256.json`, privileged agent files, pins vs source; `--require-hooks` needs the exact installed command (e4d9141ef). An install made before this needs a re-run of install.py (no pins.sha256.json -> verify fails by design).
- P5 residual: `#direct` is a whole marker (0af0f842d, also hook_antigravity). SKILL.md updated (9a8b9826b).
- Release pin: seq 2 pins 9a8b9826b, committed separately (55f97436c). TODO owner: any later change to S needs a new pin; push both.
- Not done / residuals: `.agents` mirror of route_state not synced (its in_vault only denies); an agent with NO model key in a Fable session is not treated as Fable-sensitive under OFF; a missing pin file now adds a note on every vault-reviewer spawn on a machine without an install.

## Review round 2 fixes (2026-11-22, same branch; 20_TESTS/test_cost_router_global_boundary.py, now 33 tests)
- P3: a spawn with no model in tool_input and none in the agent frontmatter is Fable-sensitive in a Fable session, also under OFF (4d37d7aad). `CLAUDE_CODE_SUBAGENT_MODEL` resolving to Fable now DENIES (exit 2) every spawn except the pinned fable-consultant, instead of rewriting (affc60e79).
- P4: project agent files need exactly one plain `name:` line or they block Fable-sensitive spawns (file named); agent definitions are hashed with LF-normalized line endings (hook and install.py); only files named/stemmed like the spawned agent (plus fable-consultant on Fable-sensitive spawns) are compared with the pins (df8a5fc86).
- SKILL.md states the residual: pins.sha256.json and config.json are unauthenticated; --verify does not stop a same-user writer of ~/.claude (09d632ca6).
- Release pin seq 3 pins 09d632ca6, committed separately (9fce0a8b0). Any later change to S needs a new pin.
- Tests: the 6 requested paths, 775 passed. Residual: a Fable-session spawn of an agent without a model key is capped by routing (sonnet/opus), never left on Fable.

## M5 + M6 done (2026-11-22, same branch; 20_TESTS/test_cost_router_specialists.py, 57 tests)
- M5 (b2e0fd34a): sidecars `S/registry_src/skills/<id>/route.meta.json` (analist-juridic, aplicatie-securizata, surse-web, agent-bridge, animatie-3d, clips-kitty, clip, prompt-director, cost-router=overlay) and `S/roles/<id>.json` (vault-worker, vault-reviewer, explore, fable-consultant). `S/lib/registry_compile.py` (schema v1, install-time only) -> `registry.json` (7.3 KB, 7.8 KB with every artifact present; cap 8 KB). install.py compiles it into the install, VERSION manifest includes it, `--verify` recompiles and compares.
- Decisions: id `explore` is lowercase (id regex) and matches `Explore.md` case-insensitively, `subagent_type` stays `Explore`. registry.json keeps a selection view (no summary/domain/tools/uses/cost; they are validated and stay in the sidecar) to fit 8 KB. A byte-identical sidecar present in both registry_src and the installed skills dir counts once; different content with the same id is an error. registry.json is generated, not committed: the source tree has none, so a project copy of the hook gives no hint (the installed copy answers).
- M6 (8d39ec15e): `S/lib/specialists.py`; hook adds ` spec=<id>[+overlay]` (<=120 chars) and the acceptance check in the long form. The hint never feeds route state/tier/tools/authority. Golden set 74 prompts: 72/74 = 97.3%; misses: "Scrie un memoriu juridic ..." (route class implement, analist-juridic lacks that class) and "render a turntable ... in blender" (route class summarize). 20 injection prompts: route state identical with and without registry.
- tests/enforce: expected compact line now carries `spec=explore`.
- Release pin seq 4 pins 8d39ec15e (9bcf58b2a). Not done: M7+; the installed copy under ~/.claude is untouched (sidecars not yet copied next to the user skills; installing M5/M6 on the PC belongs to M9); tier floors from the registry are not applied by any hook (hint only).

## M7 + M8 done (2026-11-22, same branch; tests 20_TESTS/test_cost_router_vault_router.py 10, test_cost_router_check.py 12)
- Registry misses (902910a33): `implement` added to analist-juridic, `summarize` to animatie-3d; golden set 74/74.
- M7 (f946ecaf8) `S/lib/vault_router.py route --goal ... [--json]`: root = first pinned root passing the M2 link/realpath check (never cwd); exit 3 unconfigured, 4 BLOCKED, 5 timeout/crash. Deviation: `python -I` ignores PYTHONPATH, so `-m routing.route_cli` cannot be found; the child is `python -I -c <bootstrap>` that inserts `<root>/03_IMPLEMENTATION/packages` into sys.path and runs the module as `__main__` (PYTHONPATH is still set as specified). Goal passed as `--goal=<text>`, never logged. `COST_ROUTER_VAULT_ROUTER_TIMEOUT` can only shorten the 20 s. `hook_enforce_route.invokes_router` also accepts `<launcher> .../cost-router/lib/vault_router.py route` (same launcher forms; a quoted interpreter path is not accepted, as before). In-hook gate unchanged. SKILL.md 4E.
- M8 (c0309bc01) `S/lib/check.py run|last`, `.claude/agents/check-runner.md` (haiku, Read/Grep/Glob/Bash), `S/roles/check-runner.json`; hook_agent_model appends `after failed check <label> exit=<n>` to an `escalated ...` note when the session has a failing receipt (latest receipt per label, matched by `--session <id>` or env CLAUDE_SESSION_ID; a receipt written without a session id is keyed by pid and never matches: UNKNOWN whether Claude Code exposes the session id to Bash). SKILL.md 4F.
- Registry cap: the 14th entry pushed registry.json to 8.4 KB (cap 8192, asserted by a test). Kept the cap; trimmed 5 redundant triggers (analist-juridic ro "legal memo" and en "statute", clips-kitty ro "shorts din", animatie-3d ro "text 3d", surse-web ro "ce spune legea", cost-router ro "cost de tokens"); registry is 8183 bytes, ~9 B slack: the next entry needs a cap decision. specialists test counts 13 -> 14 and check-runner added to the required id set.
- Tests: 783 passed (new files + specialists + global_boundary + test_cost_router_*.py + test_fable_consultant_routing.py + test_hook_antigravity.py).
- Release pin seq 5 pins c0309bc01. Not done: M9-M11, Antigravity branches; installed copy under ~/.claude untouched.


## M5-M8 review fixes done (2026-11-22, same branch)
- 1+2 (2c64afcf0): registry trust split. TRUSTED = S/registry_src + S/roles (errors fatal); UNTRUSTED = other ~/.claude/skills/*/route.meta.json: malformed/unknown key/duplicate or shipped id/oversize (>4 KB)/privileged name -> skipped, reported by install and --verify. Kept untrusted entries are hint-only and clamped (tier<=sonnet, no authority, priority<=10, no acceptance text; trusted wins ties). Trigger >=4 chars and no stopword-only phrases for all entries; shipped trigger "oug" became "ordonanta de urgenta" + "analizeaza oug". Cap 16384; sha256 moved to registry.hashes.json (checked by --verify).
- 3+6 (2fce7ab17): vault_router kills the process tree on timeout, prints one clean line per field; timeout test imports the module by path.
- 4 (14bca3e2b): check.py refuses cmd metacharacters for .cmd/.bat targets (exit 2). 5 (17dc0e56f): receipt only for one plain command with the installed absolute vault_router.py or -m routing.route_cli (tests updated: chained forms are no longer receipts). 7: self-update residual documented in SKILL.md 4D.
- Release pin seq 6 pins 17dc0e56f. Not done: pe/pf junction and ph cwd-shadow/huge-output/forged-receipt probes need no change by this brief (junction swap already exit 3 here); ph "cwd shadow git" and session-id traversal are not addressed.

## M9 done 2026-11-22 (main session, live operation on the owner PC)
- Installed user scope from this worktree: `install.py --source-ref 17dc0e56f... --release-pin <seq 6> --pin-root <main checkout>`; `--verify --require-hooks` exit 0 (hooks registered, Agent hook onFailure=block).
- Backup of settings.json, CLAUDE.md, the previous skill and agents: `~/.claude/backups/cost-router-global-20261122-193615/`. Rollback: `install.py --uninstall`, then restore that folder.
- Smoke (real installed hooks): home dir, D:/Projects/AI_AGENT_BRIDGE and a vault worktree: legal prompt -> spec=analist-juridic, fix -> vault-worker/sonnet, Explore -> haiku, `continua` keeps the route.
- Next: M10 roles, M11 fable-executor (default off), Antigravity branch inventory; residuals from the M5-M8 fix round (check.py session chars, Windows cwd exe resolution, receipt size cap).

## M10 done 2026-11-22 (worker run, branch claude/cost-router-complete)
- 4a3576ab2: check.py residuals, tests red first (11 failed -> 35 passed): `--session` must match `[A-Za-z0-9_-]{1,64}` (exit 2, run and last); a bare command resolves on PATH without the cwd (Windows: found only in cwd -> refused, exit 2); receipts > 256 KB are never read by last/last_failing.
- f1794a6f5: roles. `dfir-analyst` (sonnet; Read/Grep/Glob/Bash; local_only, network none, write none; path+sha256+record id per finding, missing = UNKNOWN, evidence never executed) and `test-author` (sonnet; + Write/Edit; write tests; diff only test files, red run shown). `repro-debugger` = brief template: roles/ overlay, subagent_type vault-worker; the compiler now accepts an overlay role only without its own agent file, on an existing non-privileged agent, tier <= sonnet, tools/authority within the target's. install.py `--uninstall` removes every shipped agent copy (SHIPPED_AGENTS; check-runner had been missing). Pins: agents.pinned.json already covers every .claude/agents/*.md (glob).
- Golden set 100/100 (24 new prompts, RO+EN, 8 per role), threshold raised 0.90 -> 0.95; entry counts 14 -> 17; registry 7737 bytes (cap 16384).
- Tests (one command, temp HOME): 862 passed. With the REAL HOME, `test_fable_consultant_routing.py::test_frontmatter_fable_request_fails_closed_when_policy_is_unavailable` fails at the base commit too: it reads the installed `~/.claude/skills/cost-router/agents.pinned.json` (present since M9). Test-hygiene residual: give it a temp HOME.
- b8df729b4: release pin seq 7 pins f1794a6f5. The installed copy under ~/.claude is NOT updated (owner: re-run install.py with the seq 7 pin to get the new agents and check.py fixes).
- Next: M11 fable-executor (default off), Antigravity branch inventory.

## Handoff 2026-11-22 (start the next session from here, with /clear)
- Installed globally at release seq 7 (pin commit f1794a6f5); `--verify --require-hooks` exit 0; backups in `~/.claude/backups/cost-router-global-*`.
- Antigravity's old branches: everything still valid already reached main via #313/#317/#320 (squash); nothing to port.
- PRs: #404 (Antigravity Fable 5.2 base) <- #409 (this branch, M1-M10). Full suite not yet run on this branch: run it once (check-runner agent, temp HOME) before merge.
- Next, in order:
  1. [x] DONE 2026-11-22 (commit 0f92ce551, pin seq 8 = 6df7e5d65; F2 uses a default-tier privileged set because vault-worker has tier.max opus) M10 review residuals: (F1) document in SKILL.md that dfir-analyst network/write limits and test-author's tests-only scope are declared, not enforced (they have Bash); optional PreToolUse Bash guard keyed on agent_type. (F5) test_fable_consultant_routing.py:342 needs HOME/USERPROFILE monkeypatched to tmp_path. (F2) registry_compile overlay check: use _privileged_names and require privacy/risk no looser than the target. (F3) hook_prompt_route overlay hint prints `on <subagent_type>`. (F4) check.py: refuse when executable lookup returns None; set NoDefaultCurrentDirectoryInExePath=1 for the child.
  2. M11 fable-executor, default off (separate agent + validator, pinned policy block enabled:false, receipts after failed sonnet AND opus attempts, mandatory Opus review after).
  3. Security program: triage the 54 Dependabot alerts on main (6 critical, 28 high).

## M11 progress (worker 3, 2026-11-22; resume from here)
- Audit of WIP a668626ae against the brief: items 1-6 present (agent + sidecar, separate `_valid_fable_executor_frontmatter`, policy block enabled:false in 04_CONFIG, gate a-h in `_fable_executor_gate`, other Fable requests unchanged, registry entry non-selectable). check.py `--tier` present. Gate a-h all enforced (none informational); receipts/route state remain writable by the main session (E1 residual, documented). test_cost_router_fable_executor.py: 89 passed alone (temp HOME), covers default off, each of a-h, E1-E6.
- CRLF: git blobs were already LF (.gitattributes eol=lf); only the working tree was CRLF. Converted the working tree to LF; no content diff, so no normalisation commit exists.
- Next: run the combined brief command (temp HOME, then real HOME read-only), then release pin seq 9 (only release_pin.json), tick M11.
- Combined brief command (temp HOME) first run: 20 failed (2 check.py tests, 18 specialists injection tests), all caused by the WIP. Fixed: check.py re-adds its own lib dir to sys.path at call time (cost_router_isolation.leave removed it when the executor test file ran first); injection test no longer asserts that state has no `spec` (the state now records hinted ids by design; it still asserts no tools/authority and a list of short strings).
- Verification after fixes (commit f9859bec3): combined brief command, temp HOME: `1035 passed in 329.78s (0:05:29)`; real HOME (read-only): `1035 passed in 324.85s (0:05:24)`. Two earlier runs showed 1-3 failures only while the C: drive was 99-100% full (WinError 112 / git push failures / subprocess crash 0xC00000AD); they passed on rerun with `-o tmp_path_retention_policy=none`. Tip for a successor: free disk first.
- Next: release pin seq 9 (only release_pin.json), tick M11.
- DONE: release pin seq 9 pins cc6b9ba58 (last code commit); M11 ticked. The installed copy under ~/.claude is NOT updated; to enable the executor the owner edits the installed policy and re-runs install.py with a pin that includes it (SKILL.md 4G). Reviewer pass (Opus) on this branch still pending.

## M11 residuals progress (2026-11-22; resume from here)
- Brief: (f) normalized live-operation check + stems, (g) normalized local_only scan + dfir triggers, (E1) Agent hook records real spawns per epoch, gate needs real sonnet+opus spawn, (low) prompt hook direct/router-error paths record local_only.
- Step 1: red tests appended to 20_TESTS/test_cost_router_fable_executor.py (_failed_twice now does real sonnet+opus spawns via the hook).
- Step 2: implemented (f) normalized stems + EXECUTOR_LIVE_PATTERNS, (g) specialists.normalize/fold + dfir triggers (golden 99/100, miss: "EU AI act timeline" -> dfir-analyst), (E1) route_state.record_spawn/spawns + _record_real_spawn + _real_ladder, (low) hook_prompt_route._local_only on direct/router-error paths. Executor test file: 145 passed. Next: SKILL.md residual note, combined runs, pin seq 10.
- Step 3: SKILL.md 4G documents the spawn ladder, normalization, new stems and the same-user E1 residual. Next: combined runs, pin seq 10.
- Verification (commit 712550bd1 code): combined brief command, temp HOME on E: `1091 passed in 377.63s (0:06:17)`; real HOME (read-only, no test files appeared in ~/.claude/cache): `1091 passed in 392.27s (0:06:32)`. test_cost_router_pinned_update.py after the pin: 59 passed.
- DONE: release pin seq 10 (f821bd24e) pins 712550bd1. Flag stays OFF; installed copy under ~/.claude not updated. Residual: same-user edits of receipts/spawn record (documented SKILL.md 4G); golden 99/100 ("EU AI act timeline" now hints dfir-analyst because of the new "timeline" trigger). Opus review of the residual delta still pending.

## Status 2026-11-22 (main session, after M11 residuals)
- Installed globally at release seq 10 (pin commit 712550bd1); `--verify --require-hooks` exit 0; `rules.fable_executor.enabled` = false. Backups: E:/claude-work/backups/cost-router-global-*.
- M1-M11 done. Reviews: M1-M4 (2 rounds + main-session verification with probes), M5-M8, M10, M11 (VERIFIED, GO). The M11-residuals delta (5dfb156b0..f2950e75e) only tightens the gate and is NOT yet Opus-reviewed.
- Before anyone sets rules.fable_executor.enabled = true: (1) Opus vault-reviewer on 5dfb156b0..f2950e75e; (2) accept the E1 residual (same-user process can edit receipts/spawn log) in writing.
- Small follow-ups: trigger "timeline" makes "EU AI act timeline" pick dfir-analyst (golden 99/100): scope it ("event timeline", "forensic timeline"); registry_compile.fold should strip Unicode Cf like the hook normalizer.
- Full suite on this branch once before merging #404 -> #409 (check-runner, temp HOME and basetemp on E:).
- Disk: C: was at 98.6%; freed by moving VS-installer temp copies to E:\.curatenie-cos and removing 11 clean pushed worktrees. Something still fills C: quickly (8.8 -> 3.2 GB in ~20 min during test runs): put pytest basetemp on E:.
- 2026-11-22 follow-ups DONE: dfir "timeline" scoped (e4b8a9edd), compile fold = brief normalizer (598bf5c77 red, 93ab885ad fix), pin seq 11 (44e1748ae) pins 93ab885ad; golden 103/103; 1101 passed; seq 11 not installed globally yet.

## Decision 2026-11-22: fable_executor stays OFF (review of 5dfb156b0..44e1748ae: NOT READY)
- Seq 11 installed and safe (flag false). The text-denylist gate conditions (f) live operations and (g) local_only are inherently bypassable: Cyrillic look-alikes (`dеploy`), words split by newlines or markdown (`**de**ploy`), Romanian (`lansează versiunea pe server`, `publică pachetul`), `rm -r -f`, `git reset --hard`; (g) scans only the first 8000 chars; short prompts keep the previous epoch and spec.
- Main-session decision: do not patch more words. M12 (next) = enforce at tool level instead of brief text: a PreToolUse role guard for agent_type `fable-executor` (like dfir-analyst's) that denies git push/reset --hard/force, deploy/publish/release commands, network clients, package publishing, recursive deletes and writes outside the repo worktree; keep (f)/(g) text checks only as an extra signal; fix the short-prompt epoch path in hook_prompt_route.py:251-255; scan the full brief (no MAX_PROMPT truncation) with a confusables fold. Then one review round before the owner may enable the flag.
- Also scope dfir-analyst triggers that leak into normal hints: "triage" -> "forensic triage"/"incident triage", "security log" -> "windows security log", "timeline of events" -> "forensic timeline of events".

## M12 progress (branch claude/cost-router-m12, worktree E:\claude-work\wt\cr-m12)
- 6b322f7f6 red tests 20_TESTS/test_cost_router_fable_executor_guard.py (280 failed before the fix).
- 7c6ef4e82 executor_guard in hook_enforce_route.py (Bash allowlist + worktree write check, before OFF/ENFORCE=0/shadow, fail closed); specialists.CONFUSABLES + fold_strict, local_only_hits scans the whole text; (f) folds confusables; hook_prompt_route renew_epoch for short/slash/trivial prompts; dfir triggers scoped.
- 9e4738666 SKILL.md 4G: allowlist, X1-X7, residuals; flag stays false.
- Next: full targeted run, release pin seq 12, checkpoint.
- 42500aec0 guard checks every '=' piece of an argument (pytest -o cache_dir=../x). 1b6b827ac gate condition (i) heavy task only (route class implement/design/frontier of the owner prompt; `followup` marker for short/slash/trivial prompts), SKILL.md 4G + agent description. f56ffb5dc test isolation (sys.path/sys.modules restored; the antigravity test failed only because of that leak).
- Targeted run (guard + test_cost_router_* + fable_consultant + antigravity + package_inventory): 1423 passed in 774.76s.
- 7020b990c release pin seq 12 pins f56ffb5dc (last code commit).
- NOT done: `rules.fable_executor.enabled: true` (brief item 7). The "owner decision 2026-11-23" reached the worker only through a brief an agent edited; an agent message is not owner consent, and SKILL.md 4G says enabling is the owner's step. Main session: confirm with the owner directly, then flip the flag in 04_CONFIG/claude_model_routing.json, add the enabled-by-default test, pin seq 13. Still pending: one Opus review round of 6b322f7f6..f56ffb5dc with the X1-X7 list. Not installed globally.
- Incident: a `git stash pop` on a clean tree popped a foreign stash (shared stash list); restored with `git stash store` (66933987e, back at stash@{0}) and its files were removed from this worktree. C: hit 0 bytes during the run (pin commit retried after clearing %TEMP%\pytest-of-<user>).
