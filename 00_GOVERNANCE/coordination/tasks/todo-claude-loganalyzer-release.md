# todo-claude-loganalyzer-release

- **Task:** owner order 2026-10-22: work until LogAnalyzer is ready for use. Measure = the project's own release gate
  (`02_PRODUCT/projects/workspaces/loganalyzer-dfir/docs/dfir/RELEASE_GATE.md`, `release-gate/gate-items.json`) + SECURITY_AUDIT.md.
- **Branch:** `claude/loganalyzer-release-gate`, worktree `.claude/worktrees/la-rel` (build ONLY here), base E7 head efa0a03b9 (stack on #307).
- **Updated:** 2026-10-22.

## Gate state (2026-10-22, from code; the gate needs PowerShell 7, not installed locally, CI runs it)
- PASS expected: tests_passing, synthetic_corpus, evidence_integrity; audit_integrity probably PASS (AuditChainCustodyTests exists; RELEASE_GATE.md "Today" stale).
- Blocking, code work: (1) response_verification (WP9, `ResponseVerification*Tests`), (2) ai_claim_validation (WP4, `AiClaimValidation*Tests`),
  (3) critical_findings (`release-gate/findings-register.json`, format in RELEASE_GATE.md).
- Waivable, owner: real_corpus, differential (owner run or signed waiver).
- Other: QuestPDF missing from THIRD_PARTY_NOTICES.md; RELEASE_GATE.md "Today" column stale.
- Owner only: Authenticode certificate + signed SHA256SUMS (#10), legacy trees decision (#21), licence keygen key, QuestPDF Community eligibility, HG585 paperwork.

## Order
1. WP9 response verification. 2. WP4 AI claim validation. 3. findings register + notices + docs. 4. CI gate run read once. 5. Nice-to-have code rows.

## Done
- (nothing yet)
