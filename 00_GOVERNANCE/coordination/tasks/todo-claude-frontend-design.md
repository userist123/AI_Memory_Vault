# todo-claude-frontend-design

Agent: Claude Code (main session) + subagents (vault-worker/sonnet, vault-reviewer/opus)
Task: frontend-design plugin: verify real integration, close gaps, A/B benchmark with real `claude -p` runs, review, report
(same method as todo-claude-cost-router-native.md).
Branch: gemini/casa3d-nextgen-functionality-uiux (shared; touch only owned files)
Head at start: 2c72a7228. Started: 2026-10-21.

## Owner decisions (2026-10-21, chat)
- Benchmark BOTH variants: official plugin `frontend-design@anthropic-plugin-directory` (install at user scope approved)
  and the Vault community copy `.agents/skills/frontend-design`; activate permanently only the proven winner.
- Budget ~$6, Sonnet runs, 3 UI tasks x 3 conditions x 1 run + Opus blind judge.

## Ownership
- Mine: `30_SCRIPTS/evaluation/frontend_design_benchmark.py`, `20_TESTS/test_frontend_design_benchmark.py`,
  `07_EVALUATION/reports/frontend_design_ab_benchmark_2026-10-21/`, `docs/reports/FRONTEND_DESIGN_PLUGIN_REPORT.md`, this file.
- install_design_stack.py / its test: uncommitted edits by another session; touch only the false docstring claim, if at all.

## Baseline findings (verified 2026-10-21)
- `claude plugin list` (2.1.304): frontend-design NOT installed; cache has only superpowers; no ~/.claude/design-stack.installed.
- install_design_stack.py docstring claims "already installed" -> false.
- creative-web-director SKILL.md:32 says "Use the `frontend-design` skill" -> unloadable in every session (broken consumer path).
- Vault copy `.agents/skills/frontend-design` (community, 282 lines, risk: critical) not exposed in `.claude/skills`.

## Done
- [x] Archaeology + owner questions.
- [x] Installed official plugin (user scope): cache `~/.claude/plugins/cache/anthropic-plugin-directory/frontend-design/
      d1adba47161a-65ba16f3`, SKILL.md 71 lines, sha256 b8009ae690df..., text-only (read in full, no scripts).
- [x] Isolation probe (haiku, empty dir, `--setting-sources project --strict-mcp-config`): init skills differ only by
      `frontend-design:frontend-design` (official) / `frontend-design-vault:frontend-design` (vault wrapper in scratchpad);
      built-in `design`, `design-sync` present in all arms (equal confounder).
- [x] Smoke test of measure (real Chromium) found 2 bugs: mobile overflow not detected (layout viewport widened by
      emulation), console.error not captured. Fixed by worker round 2 (overflow vs requested width, event buffering);
      26 tests pass; mobile PNG verified visually by main session (600px bar visible, overflow_px 210).
- [x] Live observation: cost-router F-DEADLOCK breaker works (Bash denied "delegate", Agent denied "local_llm/do directly",
      next Bash allowed).
- [x] Round 1 (scratchpad/bench, 9 runs, sonnet, $1.488 total, all exit 0, index.html in 9/9): skill AUTO-INVOKED 0/6
      (init lists it; model does one Write and stops). Arms therefore equal in execution; metric diffs = noise.
      measure + judge-pack done (seed 20261021).
- [x] Harness: *_forced arms (prompt prefix "Use the frontend-design skill for this task.") + judge-pack letter bug
      (zip "ABC" silently dropped arms 4-5; caught by test) fixed. 27 tests pass.
- [x] Judge round 1 (blind Opus): means baseline 6.33 / official 6.50 / vault 6.67 = noise (skill never loaded).
      T3 official page had empty programme+tickets, matching 2 console errors. Caveat: baseline was letter A in 3/3.
- [x] Round 2 ($1.549): forced arms invoked skill 6/6. Judge means baseline 5.17 / official_forced 7.17 /
      vault_forced 8.17; baseline last in 3/3. Objective: mobile overflow baseline 3/3, official 0/3, vault 2/3;
      uppercase-tracked labels official 0 in 3/3; vault T2 median contrast 4.37 (<4.5 AA). Cost +30%/run.
      Contact sheets: 07_EVALUATION/reports/frontend_design_ab_benchmark_2026-10-21/round2_T*_hero.png.
- [x] Round 3 ($0.617, director_official = official + creative-web-director, no forcing): director fired 2/3,
      frontend-design 0/3 -> the production consumer path still does NOT load frontend-design. Pages wrote
      PRODUCT/DESIGN/PLAN.md, used cream+terracotta (official tell #1), mobile overflow 3/3. Harness limits found:
      oklch() colours -> contrast None (silent); scroll-reveal opacity:0 -> blank full-page shots.
- [x] Cost-router findings (live): subagent hand-back messages are classified as user prompts and re-route the
      main session (explore -> blocked Bash); F-DEADLOCK breaker worked once.
- [x] vault-reviewer: APPROVE WITH NOTES; HIGH overflow metric (refuted "baseline 3/3 overflow") + HIGH oklch contrast,
      judge rotation, budget, chrome leak, weak test -> fixed by new vault-worker; 32 tests pass (verified by main).
      Re-measured all rounds; final blind rotated judge: vault_forced 8.33 / official_forced 8.00 / director 7.00 /
      baseline 5.67.
- [x] Evidence copied (redacted, grep home path = 0) to 07_EVALUATION/reports/frontend_design_ab_benchmark_2026-10-21/.
- [x] Report docs/reports/FRONTEND_DESIGN_PLUGIN_REPORT.md. Verdict PARTIALLY COMPLETE.

- [x] Round 4 (owner "Continua"): creative-web-director §2 now requires invoking `frontend-design:frontend-design`
      via the Skill tool (diff verified by main, 4+/2-). Re-test 3 unforced runs $0.731: frontend-design loaded 2/3
      (was 0/3); T2 director did not fire at all. min contrast 1.03/1.25 on T1/T3 (uninspected), T1 mobile overflow.
      Evidence 07_EVALUATION/.../bench4/ (copied; arm wrapper plugins in arm_plugins/). Total $4.385.

- [x] creative-web-director long line re-wrapped (no wording change).
- [x] Low-contrast inspection (scratchpad CDP script, same canvas colour normalisation as harness):
      T3 min 1.25 = `.venues` paragraphs, text oklch(0.86 .02 140) on `.venues{background:var(--ink)}` = DARK bg ->
      real contrast is high; 1.25 is a MEASUREMENT BUG (bg resolved as light). T1: real min 4.59, harness 1.03
      unexplained (same bug class likely). T1 mobile overflow = BUTTON "Ample" right 411 > 390 (real, 21 px).
      => min/median_text_contrast on oklch pages are UNTRUSTED; vault T2 median 4.37 (report §3, §6) must be re-checked.

## Next (new session; read only this file + the report)
- FIRST: reproduce in Chromium on bench4 T3: getComputedStyle('.venues').backgroundColor vs canvas rgba() result;
  fix METRICS_JS colour resolution (CSS Color 4 oklch->sRGB in JS, or computed `color(srgb ...)`), add a real-Chromium
  smoke page (dark oklch bg + light oklch text, expect ratio > 10); re-measure all rounds; correct report §3/§6.
- Inspect the low-contrast
  elements on T1/T3 (decorative vs real defect); re-wrap the long line in creative-web-director SKILL.md.
- Director trigger miss (T2): consider description/trigger wording of creative-web-director; re-test only with owner ok.
- [x] Fix false docstring in install_design_stack.py (done 2026-10-22, see Done).
- [x] cost-router: hook_prompt_route must not classify subagent hand-backs as user prompts (done 2026-10-22, see Done).
- Commit owned files on owner ask (harness, test, report, evidence, this checkpoint).
- Total spend $3.654 of ~$6 approved.
- [x] vault-worker: harness `30_SCRIPTS/evaluation/frontend_design_benchmark.py` (run / measure via Chromium CDP / judge-pack)
      + `20_TESTS/test_frontend_design_benchmark.py`. Note: static tell detector is derived from the official skill's own
      list of tells -> biased toward the official arm; blind Opus judge is the primary quality signal.

## Next
- Install official plugin, read its SKILL.md; build harness; run; judge; review; report.

## Protected (not authorized)
- push, commit without owner ask, editing other sessions' files.

- [x] 2026-10-22 claude (vault-worker): contrast measurement bug FIXED. Root causes (reproduced in Chromium): translucent
      backgrounds (alpha 0.08) treated as opaque (T1 1.03 -> real 4.59) and headless Chromium following the OS dark theme
      (T3 `.venues` 1.25 is real in dark mode, 2.86 min in light). Canvas was exact. METRICS_JS now: canvas-free CSS Color 4
      parser (oklch/oklab/lab/lch/srgb/rgb; others -> `unparsed_color:<fmt>`), alpha compositing, light scheme pinned,
      `dark_scheme` min/median recorded. Re-measured bench..bench4, metrics.json + SUMMARY.md copied to 07_EVALUATION; report
      §3/§5b/§6 corrected (vault_forced T2 median 6.36, not 4.37). Open: official_forced T2 min 1.06 (sibling-bar overlay,
      likely false low); sibling/pseudo-element backgrounds are not detectable by the ancestor walk.
- [x] 2026-10-22 claude (vault-worker): `.claude/skills/cost-router/hook_prompt_route.py` now detects synthetic turns
      (prompt STARTING with `[Subagent hand-back]`, `[SYSTEM NOTIFICATION - NOT USER INPUT]`, `<agent-message`,
      `<task-notification`, `<side-sessions-event>`, `<ci-monitor-event>`) and keeps route state untouched (only the note
      "route unchanged"). 8 new tests in 20_TESTS/test_cost_router_claude_hooks.py. Run of the 7 cost-router test files:
      281 passed (111.9s).
- [x] 2026-10-22 claude (vault-worker): install_design_stack.py docstring corrected (only that phrase).
      `pytest 20_TESTS/test_install_design_stack.py -q`: 8 passed, 4 skipped.
- [x] 2026-10-22 vault-reviewer (opus) on synthetic-turn fix: APPROVE WITH NOTES; 182 passed (hooks/enforce/enforcement),
      8 passed 4 skipped (design stack); live evidence: hook emitted "route unchanged" on real hand-back turns.
      Synthetic turn never grants more than the prior state (no allowance re-arm, own text cannot set flags).
      RESIDUAL (owner decision): an OWNER prompt that starts with `[Subagent hand-back]` keeps the previous task's
      escalation/#direct/delegated state (owner-only trigger; web/tool content cannot reach the prompt). Options: accept,
      or clear escalation_authorized on synthetic turns / require the closing envelope tag.
      Unknown: exact harness text of <agent-message>/side-session/CI envelopes (only hand-back marker confirmed live).
- [x] Docstring de-personalised by main session (no machine-state fact; review note): "`claude plugin install` is
      idempotent; never assume they are already present". Tests 8 passed, 4 skipped.

## Before any commit (owner ask)
- [x] 2026-10-22 owner-privacy pass (AGENTS.md "Owner privacy"): 97 owned text files (harness, tests, report, this file,
  creative-web-director, install_design_stack, hook_prompt_route + test, all 07_EVALUATION evidence) checked against
  the local private_identifiers list (16 entries) + profile-path/user-name regex: 0 hits. Plugin install state is not
  on the AGENTS.md list (reviewer note only). Re-run the same check on the STAGED diff + commit message at commit time.
- Pending owner: (1) accept or close the synthetic-turn residual; (2) ask to commit (dedicated branch suggested; files
  of other sessions in install_design_stack.py / hook_prompt_route.py are mixed with mine -> stage hunks carefully).

## 2026-10-28 claude: CI #310
- [x] enforce + regression-and-heldout red for one reason: this checkpoint has no note frontmatter
      (test_notes_strict_yaml::test_no_unlisted_unreadable_file) -> added to 20_TESTS/fixtures/unreadable_notes_allowlist.json
      (repo convention for checkpoints). Merged origin/main 5dd73609f (branch was 6 days behind).
- Next: CI once; ready for review / merge on owner ask.
