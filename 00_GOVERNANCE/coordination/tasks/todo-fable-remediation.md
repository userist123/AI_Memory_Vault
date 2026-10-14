# todo-fable-remediation
STATUS: DONE (awaiting owner review; not pushed)        UPDATED: 2026-10-14T21:45:00Z
TASK: controlled remediation of the 2026-10-14 external audit (access control, write paths, scanners, cold-start, storage/retrieval, routing, CI)
BRANCH / PR: fable/remediation-2026-10-14 / none    BASE: 0b967fe88195b3df76072cb178b6d7388119751c
SPEC: docs/security/FABLE_REMEDIATION_REPORT_2026-10-14.md (findings matrix, verdicts, approvals needed)
DONE:
- Revalidated every audit finding on HEAD; 8 of 12 audit IDs reproduced, rest refined (see report).
- Closed: vault_* quarantine bypass (A-01); controller slot retire path (A-05); propose() overwrite (A-08);
  purge --apply partial manifest (A-09); write_path_audit classify + blocking CI step (A-10);
  personal_data_guard formats + --history mode (B-03/B-06); stale bootstrap paths (C-01);
  snapshot markers on unreachable-SHA state cards (C-02); homonym fallbacks in path tests (C-04/C-05);
  storage skip accounting (D-01); graph no-op status (D-02); cache-hit ranking (D-04);
  README cognitive-core claim + curriculum hash sidecar (D-06/D-07); route_cli exit codes (E-02);
  suite no longer writes telemetry (E-08); workflow audit on workflow-only PRs + persist-credentials (F-01/F-03).
- VAULT_STATE.md rows updated in the same change; WRITE_PATHS.md regenerated from the fixture.
NEXT (in order):
1. Owner review of the report; merge decision (nothing pushed).
2. Owner decisions listed under BLOCKERS.
BLOCKERS / OWNER QUESTIONS:
- History rewrite + force-push to purge 4 personal documents (B-01) — requires explicit approval.
- Unify memory_* / vault_* / REST policy (A-02/A-03/A-04); load 04_CONFIG/agent_budgets.json (D-03);
  dispatch the selected verifier (E-01); 21 agent.md files citing 99_SYSTEM (C-03); CLAUDE.md:109 path
  pinned by test_claude_contract; retroactive_update_openstax_provenance.py re-proposes existing ids.
KEY FILES:
- 03_IMPLEMENTATION/packages/memory/controller.py, storage/file_engine.py, retrieval/context/retrieval.py
- 03_IMPLEMENTATION/packages/interfaces/{vault_cli,memory_mcp_server}.py, routing/route_cli.py
- 30_SCRIPTS/verification/{personal_data_guard,write_path_audit}.py, 30_SCRIPTS/ingestion/purge_rejected_rows.py
- 00_GOVERNANCE/VAULT_STATE.md, 00_GOVERNANCE/coordination/BOOTSTRAP_ALL_AGENTS_V1.md
VERIFICATION SO FAR: baseline 3830 passed/22 skipped/9 xfailed; final full suite 4072 passed/22 skipped/6 xfailed (exit 0); independent read-only review APPROVE WITH CHANGES, all 9 points applied; git diff --check clean; CI not run (no push).
