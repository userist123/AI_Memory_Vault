# Checkpoint — claude-wp3 (LogAnalyzer WP3: integrity and audit hardening)

- **Task:** queue item 3 of `00_GOVERNANCE/coordination/projects/LOGANALYZER_DFIR/STAGE2_PLAN.md`; WP3 in
  `docs/dfir/CONTRACT_AUDIT_STAGE1.md` §7 (closes R3.4, R3.5, R3.7, R3.8, R16, R22.7) plus owner decisions 11 and 23 (§8).
- **Split:** WP3a (this branch) then WP3b (separate PR, after WP3a merges).
- **WP3a branch:** `loganalyzer/wp3a-audit-chain-custody` (from main @ `fff6b0c6`). PR: not opened yet.

Paths below are relative to `02_PRODUCT/projects/workspaces/loganalyzer-dfir/`. The core type is
`LogAnalyzer.Dfir.Core/Case/CaseWorkspace.cs` (Create/Open, `Custody`, `Audit`, `RecordCollection`). Its production
callers are `LogAnalyzer.Dfir.Windows/Investigation/InvestigationPipeline.cs` and `LogAnalyzer.App/Services/LiveCase.cs`.

## WP3a spec
1. **Hash-chained audit and custody** (R3.4, R3.5):
   - Each new line of `Logs/chain_of_custody.jsonl` and of a new `Logs/audit_chain.jsonl` carries `Seq`, `PrevHash`
     and `Hash`, where `Hash = SHA-256(canonical JSON of the entry without Hash)`. The first `PrevHash` is 64 zeros.
   - Keep writing the existing `chain_of_custody.csv` and `LogAnalyzer_Audit.log` unchanged (backward compatible).
   - **Reader stays backward compatible:** cases created before WP3a have no chain fields. Report them as
     "lanț neverificabil (caz creat înainte de WP3)", never as an error and never as "valid".
   - Add `CaseWorkspace.VerifyChains()` → result per log: Valid / Broken(at Seq, reason) / Legacy.
2. **Custody records who, from where, on which medium** (STAGE2_PLAN WP3 bullet):
   - Collection entries carry collector account, collecting machine, source system and removable-medium id when known
     (empty = "necunoscut", not invented).
   - If the collecting account is a local administrator of the audited system itself, write a warning entry ("colectorul
     este administrator al sistemului auditat"). This is a warning, not a block.
3. **Evidence lifecycle states (decision 11)**, states only:
   - `ACQUIRED → VERIFIED → IN_ANALYSIS → ARCHIVED`, plus `DISPOSED`, which is set manually.
   - Every transition is written to the custody chain. Invalid transitions are refused with a reason.
   - No automatic deletion anywhere. `DISPOSED` is only a mark: it requires two distinct approvers and is refused while
     the case has `LegalHold = true`.
   - Retention is a per-case field, empty by default. The app only warns when retention is exceeded.
   - The state lives on `EvidenceItem` (additive property, default ACQUIRED when absent in old JSON).
4. **Case scope fields (decision 23), mandatory at case opening:**
   - Fields: purpose, period (from/to), systems in scope, approver, legal basis (Incident / Audit / Control).
   - Also the system category (STAGE2_PLAN WP3 bullet): air-gapped network / standalone PC / connected, combined with
     classified / unclassified.
   - All fields are additive on `CaseInfo`. `CaseWorkspace.Create` validates them and refuses an incomplete scope with
     the list of missing fields.
   - Old `case.json` files load with the fields empty and a "scop incomplet (caz vechi)" note.
   - Production callers must pass a scope:
     - `InvestigationPipeline`: add a parameter; the UI collects the values.
     - `LiveCase`: same, with the values taken from the existing case-creation UI. If no UI field exists yet, add the
       minimal fields to the existing case dialog. Do not remove anything.
5. **Read-only rule (STAGE2_PLAN WP3 bullet):** add a test that scans the production assemblies (via the
   Edition.Tests-style metadata reader) for calls that clear or delete source logs (`EventLog.Clear`, `EvtClearLog`,
   `wevtutil cl`, `Clear-EventLog`). There must be none.
6. **Tests (TDD):** chain append + verify, tamper detection (edit, delete, reorder), legacy case, every lifecycle
   transition (valid and refused), dual approval, legal hold, retention warning, scope validation, admin-collector
   warning.

## WP3b spec (next PR, do not start before WP3a merges)
- Register findings, report and exports in custody, with hashes.
- Track finding → EvidenceId dependencies. A changed evidence hash invalidates exactly the affected findings and any
  Vault proposals built on them.
- Re-check hashes and chains on case open, and show the result.

## Verification required before push (each PR)
- `dotnet build LogAnalyzer.slnx -p:EnableWindowsTargeting=true` → 0 errors.
- Dfir/UI/Edition tests: only the known Linux-only failures (Dfir 48, UI 6); all new tests pass.
- `python3 -m pytest -q -p no:cacheprovider` once: 0 failed.
- This file listed in `20_TESTS/fixtures/unreadable_notes_allowlist.json`.

## Done
- 2026-10-09T03:35Z claude-orchestrator: spec written, branch created.
- 2026-10-09 claude-wp3a: WP3a items 1-6 implemented and verified locally (build 0 errors; Dfir 48 failed = baseline, 32 new tests pass; UI 6 = baseline; Edition 14/14 incl. original 5).
  Key files (under `02_PRODUCT/projects/workspaces/loganalyzer-dfir/`): `LogAnalyzer.Dfir.Core/Case/HashChain.cs` (chain + verify),
  `Case/CaseWorkspace.cs` (VerifyChains, lifecycle, legal hold, retention, collection context, admin warning),
  `Model/CaseInfo.cs` (CaseScope, LegalHold, RetentionUntilUtc), `Model/EvidenceItem.cs` (State),
  `LogAnalyzer.Dfir.Tests/AuditChainCustodyTests.cs`, `LogAnalyzer.Edition.Tests/ReadOnlyCollectionTests.cs`,
  callers: `InvestigationPipeline.NewCase(..., scope)`, `LiveCase.Configure`, `InvestigationViewModel`/`InvestigationView.xaml` scope fields.

## Next
- Python suite once, push; the orchestrator opens the PR. WP3b starts only after WP3a merges.

## Blockers (owner decisions; safe defaults implemented)
- LIVE case (`LiveCase.Get`) has no creation dialog: if the operator has not entered a scope, it is created with a PROVISIONAL scope
  (period now..+1 year, approver "necunoscut (de confirmat)", most restrictive category air-gapped+classified, Notes "scop provizoriu").
  Owner should decide the real policy (e.g. force the scope dialog before first LIVE use).
- DISPOSED only from ARCHIVED (strictest reading); owner may allow it from other states.
- Admin-collector warning fires when the collecting machine equals the audited host and the process token is a local Administrator
  (Windows only; false elsewhere). Written once per workspace instance.
- Chain limit: deleting lines from the END of a log is not detectable without an external anchor of the head hash (WP3b candidate).
- Read-only scan allowlists three detector-indicator literals in LogAnalyzer.Core.dll (RansomwareDetectionEngine patterns "wevtutil cl security/system"
  and an explanation text); they are matched against events, never executed.
- Existing Dfir tests/pipeline callers now pass a scope (CaseWorkspace.Create refuses an incomplete one); no test was weakened.
