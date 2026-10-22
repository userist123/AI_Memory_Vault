---
id: "c0a71e9e-9a51-5d0e-8f0b-6b1a1f0c9a09"
title: "Checkpoint: Claude Code - cost-router pinned self-update (HIGH-2)"
type: "project"
lifecycle: "REVIEW"
created: "2026-10-22T19:40:00Z"
updated: "2026-10-22T19:40:00Z"
verification: "unverified"
provenance:
  source_type: "execution"
  source_ref: "00_GOVERNANCE/coordination/tasks/todo-claude-cost-router-pinned-update.md"
  created_by: "claude-code"
tags:
  - "checkpoint"
  - "cost-router"
  - "security"
---

# todo-claude-cost-router-pinned-update — PHASE 9 of the overnight reliability mission (HIGH-2)

Agent: claude-code main session (orchestrator) + vault-worker (sonnet) + vault-reviewer (opus). Claimed 2026-10-22T19:40+03:00.
Worktree: `D:/w/costrouter-p9` (owner: work on D:, C: is 99% full), branch `claude/cost-router-pinned-self-update`,
cut from `claude/overnight-reliability-p6-p7` (PR #313, draft). PR for this phase: draft, base = that branch (stacked).
Predecessor: `todo-claude-overnight-reliability.md` (NEXT SESSION / PHASE 8). Test temp dirs on D: (`D:/w/costrouter-p9-tmp`).

## Design (main session decision)
- Trust anchor = the INSTALLED copy's `release_pin.json` (it came from the owner's local checkout or from an earlier
  verified release). The hook never reads a pin, a manifest or an installer from the network.
- `.claude/skills/cost-router/release_pin.json`: `{"schema":1,"seq":N,"commit":"<40-hex>","files":{"<repo-rel posix path>":"<sha256>"},"files_sha256":"<hash of the sorted files map>"}`.
  `files` = every blob under the SPARSE set at `commit` (git blob bytes, no eol/filter conversion), EXCLUDING the pin
  file itself (it is written in a later commit than the one it pins).
- Self-update = re-sync to exactly the pinned release (it cannot auto-advance without a signature; advancing = the owner
  updates the pin in the repo and runs install.py from a trusted checkout). Residual documented: no signing.
- Fetch by SHA only (`git init` + `fetch --depth 1 <url> <sha>`, autocrlf off), check the fetched commit id == pin,
  read blobs with `ls-tree`/`cat-file` (exact bytes) into a fresh staging dir, verify staging bytes against `files`
  (no missing, no extra, no symlink, every entry a clean relative path under SPARSE), only then run the INSTALLED
  `install.py --self-update --source-root <staging> --release-pin <trusted pin>` (fetched code never runs before
  verification and the fetched install.py is never executed by the hook).
- install.py: `--source-root` rebinds SKILL/ROUTER/POLICY/AGENTS sources; `--release-pin` writes the trusted pin into
  the staged skill; downgrade refused (new pin seq < installed pin seq) unless `--force`.
- Generator: `python .claude/skills/cost-router/lib/release_pin.py generate --commit <sha> --seq <n>` from the local repo.
- Opt-in and opt-outs unchanged. No pin installed -> hook does nothing ("no-pin", fail closed).

## Threat model / probe list (worker brief = reviewer brief)
P1 tampered remote content; P2 ref rewritten / force-push (SHA fetch, id check); P3 manifest swapped together with files
(manifest never fetched); P4 partial/interrupted download; P5 manifest entries with `..`, absolute, drive letter,
backslash, empty, outside SPARSE; P6 symlink in the fetched tree; P7 downgrade to an older pin; P8 TOCTOU between verify
and install (install from the verified staging bytes); P9 fetched code executed before verification; P10 pin file
malformed / wrong schema / non-40-hex commit; P11 eol conversion turning a good tree into a mismatch (or a bad one into a match).

## Done
- [x] worker implementation + failing-first tests  - [x] main re-run  - [x] opus review (2 rounds, SAFE)
- [x] targeted + redteam + regression suites  - [x] full suite once  - [ ] privacy scan, commit, push, draft PR, CI once

## Notes
- 2026-10-22 worker round 1 (TEST_VERIFIED by main): lib/release_pin.py (SPARSE, safe_rel, load_pin, generate CLI,
  fetch_verified), hook_self_update pinned flow (no-pin/pin-invalid/fetch-failed/verify-failed/install-failed/updated),
  install.py --source-root/--release-pin + downgrade guard, SKILL.md release section, 20_TESTS/test_cost_router_pinned_update.py
  (43 tests; 21F+21E on old code), 4 old self_update tests moved to pinned origin. Main re-run: test_cost_router_*.py +
  redteam + phase6 = 634 passed / 3 xfailed, exit 0 (D: env, basetemp D:/w/costrouter-p9-tmp/bt2).
- Real-GitHub probe (main): unfiltered `fetch --depth 1 <sha>` pulls the whole tree -> 60 s timeout (production would always
  fail). Filtered sequence measured OK in 3 s: remote add origin; fetch --depth 1 --filter=blob:none origin <sha>;
  ls-tree; one explicit fetch of the 20 blob oids; cat-file. Sent to the worker (its one allowed resume).
- NEXT: main re-run after the fetch fix; opus vault-reviewer with P1-P11 (+ P12 real-remote fetch shape); privacy scan;
  commit; push; draft PR base claude/overnight-reliability-p6-p7; CI once. Then (owner) generate the first pin for the
  merged commit and commit it separately.
- Worker round 2 (fetch fix): filtered fetch + explicit blob fetch, GIT_NO_LAZY_FETCH on reads; 635 passed/3 xfailed.
  Main real-GitHub probe of fetch_verified at 08252658b: OK 5.3 s (20 files), tampered pin refused (hash mismatch).
- Opus review round 1: SAFE for HIGH-2 integrity. F1 MEDIUM silent rollback of a newer checkout install by the hourly hook
  (decision: hook re-syncs only when installed VERSION line 1 == pin commit, else "not-pinned-install"); F2 LOW pin path
  race (pass a temp copy; installer requires pin.commit == --source-ref); F3 LOW pin read twice; INFO stale comment/wording,
  docs still list HIGH-2 open (SECURITY_REDTEAM_RESULTS.md, todo-claude-router-residuals.md:43). Residuals: unsigned pin,
  staging re-read by a separate process (same-user window), inherited GIT_* env, old installs migrate once via old path.
  Fix worker dispatched (new agent); then round-2 delta review.
- Fix worker (F1-F3): 9 new tests failed on the pre-fix code; then green. Round-2 opus delta review: SAFE; MEDIUM documented
  procedure left the hook inert (VERSION = checkout HEAD != pin commit), LOW VERSION not re-checked under the lock, INFO
  SKILL.md wording. Main decided and fixed (no round 3): install() refuses a --source-ref that differs from any pin being
  installed; --self-update re-checks installed VERSION == --source-ref under the lock; SKILL.md step 4 = install with
  `--source-ref <pinned sha>`. Tests test_documented_release_procedure_keeps_the_hook_active and
  test_self_update_rechecks_the_installed_version_under_the_lock (both FAIL with the two fixes reverted: negative control run).
- Main final combined run: test_cost_router_*.py + redteam + regression = 833 passed / 1 skipped / 3 xfailed; diff --check clean.
  HIGH-2 status notes added to SECURITY_REDTEAM_RESULTS.md, RELEASE_READINESS.md, todo-claude-router-residuals.md.
- Accepted residuals (owner): pin unsigned (trust = local pin + SHA-1 commit ids); staging re-read by a separate process
  (same-user window); inherited GIT_* env; old installs migrate once via the old unpinned path; no pin committed yet
  (hook = no-pin until the owner pins a merged commit and installs with --source-ref).
- Full suite once (D: temp, `pytest 20_TESTS security/tests`): 14 failed / 4976 passed / 56 skipped / 9 xfailed / 3 errors;
  10 failing ids, all in the phase-7 baseline list (0 new); no ENOSPC. NEXT: commit, push, draft PR (base PR #313 branch), CI once.
