# todo-claude-design-system-a2
STATUS: IN_REVIEW          UPDATED: 2026-10-22
TASK: A2 phase 1 - DTCG token source + Style Dictionary build, Casa3D as first consumer, zero visual change
BRANCH / PR: claude/design-system-tokens / https://github.com/userist123/AI_Memory_Vault/pull/312 (base main)
Plan: 02_PRODUCT/projects/workspaces/design-system/PLAN.md (decisions + increments 1-5)
Origin: todo-claude-frontend-design.md -> owner picked A2 (2026-10-22, chat).

## Ownership
- Mine: 02_PRODUCT/projects/workspaces/design-system/**, .github/workflows/design-system-build.yml, this file.
- Casa3D touch (consumer wiring only): app/app/layout.tsx (one import), app/app/globals.css (:root value block),
  new app/app/tokens.css (generated). Casa3D owners: todo-antigravity-casa3d.md DONE, todo-claude-casa3d-pins.md
  BLOCKED_ON_OWNER (no edits in these files pending).

## Done
- [x] Archaeology: Casa3D globals.css 1246 lines, hand-written :root tokens, 262 colour literal matches (grep).
- [x] style-dictionary latest 5.7.0 (2026-10-16, too new) -> pin 5.6.0. Node 24.13 local, CI uses 22.

- [x] Agent dispatch blocked by cost-router hook (routed to dispatcher/antigravity; external dispatch not
      authorized) -> main session implemented increments 1-5 directly.
- [x] Package (SD 5.6.0 exact, lockfile, 0 vulns), tokens core (space/size/motion Carbon) + casa3d (color OKLCH+hex,
      type, shape), brand.json aliases/copyTo, build.mjs (custom: color/vault-css exact hex, duration/vault-css,
      shadow/vault-css; SD 5.6.0 lacks `duration` and recomputes hex from components), 11 node:test tests.
      Each test fails on its mutation (hex, OKLCH, $type, hand-edited tokens.css). Test caught a real cycle bug
      (`--shadow-1: var(--shadow-1)`), fixed in the format.
- [x] Casa3D: layout.tsx imports tokens.css before globals.css; globals.css :root keeps only legacy aliases +
      color-scheme (CRLF kept). Raw colour literals outside tokens: 477 before = 477 after (phase-2 metric).
- [x] CI .github/workflows/design-system-build.yml (ubuntu+windows, Node 22, npm ci --ignore-scripts, npm test).

- [x] Casa3D gates: tsc 0, next build 0, Vitest 100/108 files then 8 crashed workers (Windows 0x80000003/
      0xC0000409, not failures) rerun --maxWorkers=1: 86/86 -> 108/108 files.
- [x] Chromium (dev server): 50/50 original props compute equal; home full-page pixel diff vs original :root put
      back via CSSOM = 0 px (control capture also checked). Screenshot sent to owner.
- [x] Finding: live look = "Visual System 02" scoped on .home (~L945) and .ws (~L1048); :root values serve
      secondary pages only. PLAN.md updated (evidence + phase 2).
- [x] vault-reviewer (opus): APPROVE WITH NOTES (14 mutation probes caught; lockfile 106 pkgs, no install scripts).
      Fixed: MEDIUM import-order/presence test + layout.tsx in workflow paths; later :root blocks may only set --hit;
      shadow/colour transform throws on non-object colour (was `undefined`); argv[1] guard. 13/13, each new test
      fails on its mutation. Accepted: INFO --size-hit has no coarse override (no consumer yet; phase 2).
      Unknown: Node 22 glob in CI (documented since 21), CI not run (push filter excludes gemini/*; runs on PR).

- [x] Owner: "fă ce este recomandat" -> commit own files on branch `claude/design-system-tokens` (base 655f829be),
      push, draft PR (base = gemini/casa3d-nextgen-functionality-uiux so the diff is only this work).
      Built in a separate local `--shared` clone (main working tree untouched). dist/ is git-ignored repo-wide -> drift gate now checks
      only committed copyTo outputs; clean-clone run without dist: 13/13.

- [x] Pushed 7f2ed30fe (rebased on remote gemini c22666479), draft PR https://github.com/userist123/AI_Memory_Vault/pull/312
      base gemini/casa3d-nextgen-functionality-uiux. Privacy check staged diff + message: 16 ids, 0 hits.
- [x] PR #304 was already MERGED (squash) -> owner OK (2026-10-22): branch rewritten onto origin/main f0ee40d67
      (47e71dd35, 13/13), PR #312 base changed to main.

- [x] CI #312 (regression-and-heldout): 3 failures, all from this PR: new workflow missing from the CI tables in
      README.md/README.en.md; checkpoint + design-system PLAN/README have no note frontmatter -> added to
      20_TESTS/fixtures/unreadable_notes_allowlist.json (repo convention for plain documents). Re-run: 12 + 10 passed.

- [x] 2026-10-28 claude: CI #312 red after 6 days (npm lockfiles critical = repo-wide audit, fixed on main by #355;
      regression-and-heldout cancelled at the old 20/30 min limit, main now 45). Merged origin/main 5dd73609f.
      The merge exposed a real guard hit: main added a 2nd :root block to Casa3D globals.css (--dur-fast/base/slow,
      --ease-out/std, UI/UX P2-13). Moved those values unchanged into tokens/brands/casa3d/motion.json + brand.json
      aliases; block removed from globals.css; new parity test pins the 5 values (fails on a 120->130 ms mutation).
      npm test 14/14; Chromium dev server: computed 0.12s/0.2s/0.32s + both cubic-beziers equal, 0 console errors.

- [x] 2026-10-28 claude: CI then red only on Casa3D check-a11y radiator state - pre-existing on main since #352.
      Root cause: on Linux `--lang=ro-RO` does not change navigator.language, app rendered in English, script looks
      for Romanian labels. Fixed in PR #353 (localStorage casa3d.lang=ro; reproduced with Chrome --lang=en-US: old
      3 FAIL like CI, new 21/21), CI 38/38, merged c27908caa. PR #310 merged a9dd6e4fa. main merged here again.

## Next
- CI on #312 once; then mark ready for review on owner ask (merge = owner).
- Phase 2 (new session): see PLAN.md "Phase 2".
