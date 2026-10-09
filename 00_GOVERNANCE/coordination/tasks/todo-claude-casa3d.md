# todo-claude-casa3d
STATUS: IN_PROGRESS    UPDATED: 2026-10-10T16:00:00Z
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
NEXT (in order):
1. Owner supplies the v8 archive (or its path); compare SHA-256 with 7bb34bf3...9628bc.
2. npm ci, full Vitest, next build; record output in CORE_IMPLEMENTATION_v8.md.
3. node scripts/verify-f4.mjs and verify-f5.mjs; record output.
4. One real model call Brief -> DSL -> Solver with ANTHROPIC_API_KEY (owner sets the key; agent never types it).
5. Only then move F4 to DONE in the phase table.
BLOCKERS / OWNER QUESTIONS:
- v8 source archive not on this machine (manifest path /mnt/data/casa3d-v8/app was the Claude.ai sandbox). Where is it saved locally?
- ANTHROPIC_API_KEY not set in the environment.
KEY FILES:
- 02_PRODUCT/projects/Casa3D.md
- 00_GOVERNANCE/coordination/tasks/todo-claude-casa3d.md
VERIFICATION SO FAR: pytest 20_TESTS/test_vault_state_accuracy.py 20_TESTS/test_lifecycle_schema_parity.py security/tests/test_memory_integrity.py -> 31 passed (2026-10-10, Windows and cloud)
