# todo-claude-casa3d-next-security
STATUS: IN_REVIEW (PR draft)        UPDATED: 2026-11-17
TASK: Casa3D, `npm run build` ieșea cu SecurityFatalError pe Next.js 16.4.3 (după compilarea tuturor paginilor).
BRANCH: claude/casa3d-next-16-4-6, worktree D:/w/nextsec (bază origin/main). Launch config casa3d-nextsec (start, port 3222).
DONE:
  - Cauză (npm audit 2026-11-17): GHSA-mfr8-8qww-v76h (>=16.3.0 <=16.4.4) și GHSA-vc7v-qv4w-wrv8 (>=16.2.0 <=16.4.4),
    ambele moderate (scurgeri de cache între valori de root params la `use cache`). Prima versiune reparată: 16.4.5.
  - next ^16.4.3 → ^16.4.6 (ultima din 16.4.x), lockfile regenerat de npm.
  - Verificat: vitest 142 de fișiere / 972 de teste (main), tsc exit 0, `npm run build` exit 0; aplicația construită pornită
    (start) și editorul Plan + 3D încărcat pe un proiect demo, consola fără erori.
NEXT: CI pe PR; după merge, PR #386 (renovare) se rebazează pe main pentru build verde.
BLOCKERS: niciunul.
KEY FILES: 02_PRODUCT/projects/workspaces/casa3d/app/{package.json,package-lock.json}.
