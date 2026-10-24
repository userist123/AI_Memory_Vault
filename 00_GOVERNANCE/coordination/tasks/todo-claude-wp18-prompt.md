# todo-claude-wp18-prompt
STATUS: DONE (document only)        UPDATED: 2026-10-10T01:00Z
TASK: Write the WP18 work-package prompt (two station roles chosen by the machine — CONTROL for PIC checks on air-gapped
stations, CSIRT for the incident-response centre — plus a non-technical UI layer) for the owner to hand to an agent.
BRANCH / PR: `claude/loganalyzer-dfir-roles-ui-0a71fb` (worktree); no PR yet, no code changed.
DONE:
- `02_PRODUCT/projects/workspaces/loganalyzer-dfir/docs/dfir/PROMPT_WP18_ROLURI_STATIE_UI_SIMPLA.md` — the prompt
  (vocabulary of the four axes edition/mode/account role/station role, the two roles, role decision via the signed policy
  `role` field, non-technical UI rules, data contracts, steps S1–S7 with tests, invariants, acceptance scenarios, owner questions Q1–Q7).
- Allowlist entries in `20_TESTS/fixtures/unreadable_notes_allowlist.json` for the prompt and this checkpoint (plain documents).
- Owner decisions D1–D8 recorded in the prompt §11 (2026-10-10): role in the existing signed policy; control export = imported
  evidence at CSIRT; configurable report header; manual marking until WP13; **Simple language level default for every account,
  administrators included (administrators may be non-IT people)**; **no new account roles in WP18**; real-person usability run; station = PC.
NEXT:
- An agent starts S1 on `loganalyzer/wp18-station-roles`.
BLOCKERS: none. Facts about existing code are DOCUMENT_VERIFIED on docs/dfir + README at main @ 21da5bbf2; paths listed in the
prompt §2 were checked to exist (CODE_VERIFIED, existence only).
KEY FILES: the prompt above; `docs/dfir/EDITIONS.md`, `AUTHENTICATION.md`, `CASE_HOME.md`, `LOGANALYZER_PRODUCT_UX_CONTRACT.md`,
`LESSONS_LEARNED_MAPPING.md` §3; `STAGE2_PLAN.md` execution queue.
