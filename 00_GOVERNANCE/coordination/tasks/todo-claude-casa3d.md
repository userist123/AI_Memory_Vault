# todo-claude-casa3d
STATUS: IN_PROGRESS    UPDATED: 2026-10-10T10:40:00Z
TASK: Casa3D — execute the recommended follow-ups (verify v8, real AI call, reconcile F4-F8 memory)
BRANCH / PR: codex/casa3d-memory (continues claude/casa3d-opinion-52d7cf, pushed 2026-10-10) / none    BASE: 21da5bbf2
SPEC: 02_PRODUCT/projects/Casa3D.md (section "Reconciliere F4-F8"), 02_PRODUCT/projects/Casa3D/CORE_IMPLEMENTATION_v8.md, 02_PRODUCT/projects/Casa3D/SOURCE_SNAPSHOT_v8.md
DONE:
- Owner supplied casa3d-faza0/2/4.zip; copied to D:\w\casa3d (C: was 100% full). Defender clean.
- faza0: 13/13. faza4 (cumulative F1-F4): vitest 64/64, tsc + next build pass after local rename viewer3d.js -> viewer3d-engine.js (case clash on NTFS).
- Ledger written: 02_PRODUCT/projects/Casa3D/VERIFICATION_2026-10-10.md.
- Reconciled F4-F8 state in Casa3D.md: phase table row F4 -> "code present in v8 manifest, UNVERIFIED",
  new row F5-F8 UNKNOWN, new section listing the F4/F5 artefacts from the manifest and the evidence
  still missing. Note stays REVIEW/unverified.
- Searched for the v8 source on the owner's machine (C:\Users\user, D:\, git history, names casa3d*): not found.
- 2026-10-10 (cloud session): Casa3D.md updated from the ledger, not from the patch: F4 row -> DONE (TEST MODE),
  new v8 row UNVERIFIED, F4 section 'Implementare verificata', reconciliation renamed to v8 / F5-F8, evidence
  matrix, gaps, roadmap, provenance, relation to ledger 28890ae9. Note stays REVIEW/unverified.
- 2026-10-10 10:15 UTC: PHASE4.md server verification run on LAB-PC via Remote Control session LAB-PC-toasty-raven
  (health 200, admin 401/200, /go/o 302 with UTM and no cookie, bot clicks not counted, 404 for missing offer).
  Recorded in VERIFICATION_2026-10-10.md; Casa3D.md F4 section updated. v8 archive hash search on the PC was blocked
  by the Claude Code permission classifier (both personal folders and D:\w\casa3d).
NEXT (in order):
1. Owner supplies the v8 archive (or its path), or allows the PowerShell hash scan on LAB-PC; compare SHA-256 with 7bb34bf3...9628bc.
2. npm ci, full Vitest, next build; record output in CORE_IMPLEMENTATION_v8.md.
3. node scripts/verify-f4.mjs and verify-f5.mjs; record output.
4. DEFERRED by owner on 2026-10-10 ("continua fara AI deocamdata"): real model call Brief -> DSL -> Solver. Not a blocker for 1-3 and 5.
5. Only then move F4 to DONE in the phase table.
BLOCKERS / OWNER QUESTIONS:
- 2026-10-10 cloud session: no Casa3D archive is in the Vault repo or its git history; Desktop Commander device LAB-PC
  is offline, so D:\w\casa3d (faza0/2/4 extracted, npm installed) is unreachable from here. To unblock steps 1-3 and the
  PHASE4.md server check: either bring LAB-PC online (Desktop Commander app) or upload casa3d-faza4.zip / the v8
  archive into the session. Pushed state: codex/casa3d-memory @ e628911c.
- v8 source archive not on this machine (manifest path /mnt/data/casa3d-v8/app was the Claude.ai sandbox). Where is it saved locally?
- ANTHROPIC_API_KEY not set in the environment.
KEY FILES:
- 02_PRODUCT/projects/Casa3D.md
- 00_GOVERNANCE/coordination/tasks/todo-claude-casa3d.md
VERIFICATION SO FAR: pytest 20_TESTS/test_vault_state_accuracy.py 20_TESTS/test_lifecycle_schema_parity.py security/tests/test_memory_integrity.py -> 31 passed (2026-10-10, Windows and cloud)
