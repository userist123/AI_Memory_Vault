# todo-claude-casa3d-a11y-p3
STATUS: IN_PROGRESS   UPDATED: 2026-10-23T10:30:00+03:00
TASK: Casa3D UI/UX P3 (report rows 15-18). Owner 2026-10-23: "Da, fa recomandat si dupa continua cu ce trebuie fara sa
  te opresti" -> order 17 (statement + conformance report + axe in CI), 16 (plan DOM mirror), then 18, 15.
BRANCH / PR: claude/casa3d-a11y-p3 (worktree D:/w/uiux3, base origin/main 3e5c5192)
DONE:
- (17) axe-core 4.14.0 (devDependency, exact); scripts/lib/cdp.mjs shared CDP launcher (check-slides refactored onto
  it); scripts/check-a11y.mjs audits 18 page states (home, statement, editor stages + Ctrl+K, 7 documents, client link
  desktop/phone) with WCAG 2.0/2.1/2.2 A/AA tags; scripts/a11y-known.json (empty) for reasoned exceptions; CI step on
  ubuntu in casa3d-build.yml (server + check-a11y + check-slides). Violations found and fixed: level tablist contained
  the "+ Level" button; stage buttons / 3D export label-in-name; Orders page text nearly invisible (dark-theme ink on
  paper) and its links; Ctrl+K group names contrast. Public /accesibilitate statement RO/EN (lib/a11y-statement.ts,
  linked from home and client link, public gate); ACCESSIBILITY_CONFORMANCE_REPORT.md (VPAT-style WCAG 2.2 A/AA +
  EN 301 549 mapping, honest "not evaluated manually" where unmeasured). Result: 0 violations on all 18 states.
NOTE: drive C: filled up transiently (ENOSPC / spawn UNKNOWN); builds run with TMP/TEMP on D:/w/tmp.
NEXT: (16) plan DOM mirror; (18) Next.js / React ViewTransition + CSP decision; (15) three.js upgrade with before/after.
KEY FILES: casa3d/app/scripts/{check-a11y,check-slides}.mjs, scripts/lib/cdp.mjs, lib/a11y-statement.ts,
  app/accesibilitate/page.tsx, components/StatementView.tsx, casa3d/ACCESSIBILITY_CONFORMANCE_REPORT.md,
  .github/workflows/casa3d-build.yml
