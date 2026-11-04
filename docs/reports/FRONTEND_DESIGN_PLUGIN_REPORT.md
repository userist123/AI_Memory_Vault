# frontend-design plugin — integration check and A/B benchmark (2026-10-21/22)

Verdict: **PARTIALLY COMPLETE.** The skill measurably improves UI output **when it is loaded**, but in normal use it is
**not loaded** (0/9 unforced runs), including on the production path through `creative-web-director`.

Evidence: `07_EVALUATION/reports/frontend_design_ab_benchmark_2026-10-21/` (RUNS.json, SUMMARY.md, per-run run.json /
metrics.json / index.html, judge keys and scores, visual comparison sheets `skill_compare_T1..T3.webp`: baseline / official / vault / director path, real
Chromium screenshots). The raw per-run PNG screenshots are not committed (size); re-create them by copying each run's
`index.html` and `run.json` into `<out>/runs/<task>_<arm>/` (page under `work/index.html`) and running
`frontend_design_benchmark.py measure --out <out>`. Harness:
`30_SCRIPTS/evaluation/frontend_design_benchmark.py`, tests `20_TESTS/test_frontend_design_benchmark.py` (32 passed).

## 1. Integration state (verified on the machine)
- Before: `frontend-design` was **not installed** (`claude plugin list`, Claude Code 2.1.304). The docstring of
  `30_SCRIPTS/setup/install_design_stack.py` claimed "already installed"; no `~/.claude/design-stack.installed` marker.
  `creative-web-director/SKILL.md:32` tells agents to use a skill that could not load in any session.
- Now: official plugin installed at user scope with owner approval
  (`frontend-design@anthropic-plugin-directory`, cache `d1adba47161a-65ba16f3`, SKILL.md 71 lines,
  sha256 `b8009ae690df…`, text-only, read in full).
- The Vault copy `.agents/skills/frontend-design` is a community variant (282 lines, `risk: critical`,
  `agentic-awesome-skills`). It was tested only through a temporary `--plugin-dir` wrapper and **is not activated**.

## 2. Method
- `claude -p --model sonnet`, empty work dir outside git, `--setting-sources project --strict-mcp-config`,
  tools `Write,Edit,Read,Skill`. The arms differ only in `--plugin-dir`, plus a prompt prefix on the `*_forced` arms.
  The isolation probe confirmed that the init skill lists differ only in the tested skill.
- Tasks: T1 bike-repair co-op landing page, T2 hospital-pharmacy stock dashboard, T3 dance-festival event page.
  Each task produces a single `index.html`.
- `measure`: headless Chromium over CDP at 1440×900 and 390×844, with a scroll pass to trigger reveals and full-page
  screenshots. It records page-level horizontal overflow, WCAG text contrast (colours parsed in JS with CSS Color 4 math, translucent backgrounds composited, colour scheme pinned
  to light with the dark-scheme result reported separately), tracked-uppercase labels, console errors, and static tells.
- Blind judging by an Opus subagent that saw only the PNGs and the briefs. The final pass used 4 arms per task with
  Latin-square rotated letters.

## 3. Results
| round | arms | skill invoked | cost |
|---|---|---|---|
| 1 | baseline / official / vault (no forcing) | **0/6** | $1.488 |
| 2 | baseline / official_forced / vault_forced | **6/6** forced arms | $1.549 |
| 3 | director_official (official plugin + creative-web-director, no forcing) | director 2/3, **frontend-design 0/3** | $0.617 |

Total: **$3.654** of the ~$6 approved.

Blind judge, overall score 1-10, mean of 3 tasks:
- Round 1: baseline 6.33, official 6.50, vault 6.67. The skill was never loaded, so this spread is run-to-run noise.
- Final rotated pass (rounds 2 and 3): **vault_forced 8.33 · official_forced 8.00 · director_official 7.00 ·
  baseline 5.67**. Baseline ranked last or second-to-last in every task. vault vs official (0.33) is within noise
  (n=1 per cell).

Objective metrics after the reviewer fixes (round 2):
- Tracked-uppercase labels (desktop): official_forced **0/0/0**; vault_forced 20/35/58; baseline 6/0/49.
- **Correction (2026-10-22): the contrast numbers originally published here (T2 median: official 9.55, vault 4.37,
  baseline 13.98) and the 1.03 / 1.25 minima in 5b were a measurement bug, not a property of the pages.** Two causes,
  both reproduced in headless Chromium: (a) the harness took the first ancestor background with alpha > 0.01 as if opaque,
  so `oklch(.. / 0.08)` panels counted as a light background (bench4 T1: 1.03, real 4.59); (b) headless Chromium followed
  the OS dark theme, so pages with `@media (prefers-color-scheme: dark)` were measured in dark mode on this machine
  (bench4 T3 `.venues`: in dark mode `--ink` is light, so light text on it is genuinely 1.24; in light mode 2.86 min).
  The canvas colour normalisation itself was exact (it matched the new JS parser to +-1/255 on oklch, oklab, lab, lch,
  srgb, rgb). The harness now composites translucent layers, uses a canvas-free parser, pins `prefers-color-scheme: light`
  and records dark-scheme contrast separately. Re-measured, light scheme, desktop (min / median text contrast):

  | round | task | baseline | official | vault | director_official |
  |---|---|---|---|---|---|
  | 1 | T1 | 4.29 / 8.66 | 3.85 / 12.94 | 3.75 / 6.23 | - |
  | 1 | T2 | 4.68 / 16.29 | 4.66 / 16.37 | 4.46 / 15.75 | - |
  | 1 | T3 | 4.09 / 15.97 | 3.57 / 15.88 | 3.70 / 5.32 | - |
  | 2 (forced) | T1 | 4.22 / 14.60 | 4.63 / 15.82 | 4.70 / 7.91 | - |
  | 2 (forced) | T2 | 4.64 / 16.15 | 1.06 / 14.58 | 3.17 / 6.36 | - |
  | 2 (forced) | T3 | 3.89 / 15.91 | 5.45 / 13.50 | 2.87 / 15.13 | - |
  | 3 | T1/T2/T3 | - | - | - | 5.14 / 14.28, 5.95 / 7.19, 4.10 / 8.30 |
  | 4 | T1/T2/T3 | - | - | - | 4.59 / 13.66, 3.45 / 7.07, 2.86 / 12.52 |

  What this changes: vault_forced T2 median is **6.36, not 4.37**, so "below WCAG AA" no longer holds as a median claim.
  Vault copies still have the lowest median in 5 of 6 round-1/2 cells (all but round-2 T3), but the
  gap is smaller and n=1 per cell, so contrast is no longer evidence against the Vault copy. The 1.06 minimum on
  official_forced T2 is white text on a `.tag` inside a bar whose background belongs to a sibling element, which an
  ancestor walk cannot see: probably a false low, not verified. Pages with a dark-mode block can differ materially
  under the dark scheme (see `dark_min`/`dark_median` in each SUMMARY.md).
- Page-level mobile overflow: none in rounds 1-2; director T2 overflowed by 366 px.
- Cost per run: forced arms $0.17-0.20 against $0.13-0.16 for the round-2 baseline. The round-1 baseline averaged
  $0.19, so there is no reliable cost difference.

Qualitative: without the skill, Sonnet converged on near-black with a single orange accent on all three subjects. The
director path produced cream with a terracotta accent, which is tell #1 in the official skill's list.

## 4. Independent review (vault-reviewer, Opus): APPROVE WITH NOTES
Findings and status:
1. **HIGH, fixed.** The overflow metric counted content clipped inside its own container. The earlier claim
   "baseline 3/3 overflow" was **refuted and withdrawn**.
2. **HIGH, fixed.** Contrast silently returned None on oklch pages. It now uses canvas normalisation and records the
   reason when a value is missing.
3. **MEDIUM, open by design.** The static tell detector mirrors the official skill's own list, which biases it toward
   the official arm. It is not used for the verdict.
4. **MEDIUM, fixed.** Judge letters were not rotated (baseline was letter A in all three round-1 tasks). Fixed with a
   Latin square and the judging was re-run.
5. **LOW, fixed.** The budget overshoot on timed-out runs.
6. **LOW, fixed.** The Chromium profile leak.
7. **LOW, fixed.** A test that checked nothing real.

Residual limits: one judge, three tasks, one run per cell, a single model (Sonnet).

## 5. Side findings for cost-router (live, this session)
- The F-DEADLOCK breaker worked: Bash was denied with "delegate", the Agent call was denied with "do directly", and
  the next Bash call was allowed.
- **New:** subagent hand-back messages are classified as user prompts by `hook_prompt_route`. They re-route the main
  session; for example "explore" then blocked a plain Bash command.

## 5b. Round 4: explicit invocation in creative-web-director (owner said "Continua", 2026-10-22)
- Edit `.claude/skills/creative-web-director/SKILL.md` §2: "Before writing any UI code, invoke the `frontend-design`
  skill with the Skill tool (`frontend-design:frontend-design`) … naming it is not enough, it must be loaded."
- Re-test with 3 unforced runs, director_official, $0.731. frontend-design is now **loaded in 2/3 tasks** (T1, T3),
  up from 0/3. In T2 the director itself did not fire: the model wrote the file directly, a skill-triggering miss
  upstream of the edit.
- Quality is not fixed by loading the skill alone. The earlier minimum contrast of 1.03 (T1) and 1.25 (T3) was a measurement bug
  (see section 3 correction). Corrected light-scheme minima are T1 4.59 and T3 2.86; under the dark scheme T3 really is
  1.24 (light text on a light `.venues` panel, a genuine dark-mode defect of the generated page). T1 overflows on mobile
  (a 21 px button).
- Running total: **$4.385** of the ~$6 approved.

## 6. Recommendations (owner decisions)
1. Keep the official plugin; it is already installed. **Do not activate the Vault copy:** its visual score is equal
   within noise, it has more tracked-uppercase template labels (20/35/58 vs 0/0/0), and it carries `risk: critical`
   community provenance. The earlier contrast argument (T2 median 4.37) was a measurement bug and is withdrawn; vault
   medians remain the lowest in most cells (T2 6.36 vs 14.58) but are not below AA.
2. Fix the real gap, which is auto-invocation. Change `creative-web-director` to say
   "invoke the `frontend-design:frontend-design` skill before writing UI code", then re-test with 3 unforced runs
   (~$0.6). It is an owner standing directive, so it needs the owner's ok.
3. Correct the false "already installed" docstring in `install_design_stack.py`. That file holds another session's
   uncommitted edits, so coordinate first.
4. Port the subagent-message classification fix to cost-router (`hook_prompt_route`).
