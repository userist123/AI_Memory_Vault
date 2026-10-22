# todo-claude-casa3d-pins
STATUS: BLOCKED_ON_OWNER   UPDATED: 2026-10-22T09:00:00+03:00
TASK: Casa3D — CI green on PR #304 + PIN-gated client pins on /share
BRANCH / PR: gemini/casa3d-nextgen-functionality-uiux / PR #304    REMOTE HEAD: c22666479
DONE (all pushed):
- a8c614b03 IBAN fixtures: personal_data_guard PASS (guard and allowlist untouched).
- d0e31c2f7 PIN-gated pins (design + threat model P1-P11: 00_GOVERNANCE/coordination/projects/CASA3D/SHARE_PINS_DESIGN.md);
  Opus review APPROVE, L1/L2 fixed; vitest 108/650, tsc 0; browser: client flow + Editor PIN panel and "pin pe imagine" verified.
- 8c8616e76 (pushed from clean worktree D:/w/ci304) vault checks: casa3d-build.yml persist-credentials false, README CI tables,
  CASA3D/CURRENT.md, allowlist (Casa3D docs + agent checkpoints as plain_document), VAULT_STATE 1328 notes / 533 edges (177/178/178).
- Full 20_TESTS on D:/w/ci304: only extra failure vs main = test_repository_layout (cost-router absolute path);
  14 failures + 2 errors in b2m/vault_access/import_external_skills are Windows-only and identical on clean main (D:/w/cimain).
NEXT (in order):
1. DONE 2026-10-22: main checkout fast-forwarded to 8c8616e76; worktrees D:/w/ci304 and D:/w/cimain removed (both clean),
   D:/w/ci304_pytest.txt deleted.
2. DONE 2026-10-22 (head d503e6ab9): CI checked once. All Casa3D jobs green (app typecheck/test/build ubuntu+windows,
   twin-core, CodeQL js/ts/python, Gitleaks, personal data). Red = only item 3 below: 2x "Structural and path hygiene"
   (3 TEST_ABSOLUTE_PATH) + "enforce" (1 IMPLEMENTATION_ABSOLUTE_PATH claude_usage_report.py). Pending at check time:
   regression-and-heldout, Analyze (csharp) — not re-polled.
3. DONE 2026-10-22 c22666479 (pushed from worktree D:/w/fix304): cost-router red fixed — test lines got the official
   marker "# hygiene: intentional-absolute-path"; claude_usage_report.project_slug slugs drive paths as written (Linux CI).
   hygiene FAILURES=0, layout PASS, 71 tests pass; PR #304 CI ALL GREEN, mergeStateStatus CLEAN.
   NOTE: main checkout still has the other agent's uncommitted edits in test_cost_router_{self_update,skill}.py and
   local commit 655f829be (= 3444380c9 on remote); expect a small conflict on the marker lines when it pulls.
   Was red:
   - repository hygiene TEST_ABSOLUTE_PATH: 20_TESTS/test_claude_model_router.py, test_cost_router_self_update.py, test_cost_router_skill.py
   - layout IMPLEMENTATION_ABSOLUTE_PATH: 03_IMPLEMENTATION/packages/routing/claude_usage_report.py
   - test_claude_model_router.py::test_project_slug_matches_claude_code_layout (hardcoded Windows slug; fails on Linux CI)
4. BLOCKED: `gh pr merge 304 --squash` denied by the auto-mode permission classifier — owner merges #304 (squash, head
   c22666479), then close #305 (verified: every #305 commit is an ancestor of #304 head).
   Recommendations: #257 close as superseded (2026-10-10 reconciliation, add/add conflict on VERIFICATION_2026-10-10.md
   with #304); #258 do not merge (218 commits/510 files, 202 patches not in #304, CONFLICTING) — salvage in its own session.
UNVERIFIED: cloudflared tunnel sends x-forwarded-proto https (cookie Secure flag).
NOTES:
- Antigravity's uncommitted files (CASA3D_*_AUDIT/INVENTORY/STATUS/EVIDENCE*.md, edits in todo-antigravity-casa3d.md) left untouched.
- .claude/launch.json has a local "casa3d" dev-server entry (not committed).
KEY FILES: SHARE_PINS_DESIGN.md; casa3d/app/{lib/share-pin.ts,lib/feedback.ts,components/PinLayer.tsx,tests/share-pins.test.ts}
