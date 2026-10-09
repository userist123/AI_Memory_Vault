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

## WP3b spec (branch `loganalyzer/wp3b-invalidation-recheck`, from main @ `8e72b252` after WP3a #238 merged)
Production path: `LogAnalyzer.Dfir.Windows/Investigation/InvestigationPipeline.cs` (writes `Analysis/findings.json`, graph,
detections, anti_forensics, `Exports/vault_proposals.jsonl` via `LogAnalyzer.Dfir.Core/Memory/VaultExport.cs`); case open via
`CaseWorkspace.Open` (callers: `LogAnalyzer.App/Services/LiveCase.cs`, tests). Find where reports (PDF/HTML/OSCAL) for a case are
written (grep the App and Core report services) and include them.
1. **Outputs in custody:** every file the pipeline or a report/export writes into the case (Analysis/*.json, Reports/*,
   Exports/*) is registered in the custody chain with its SHA-256 (one entry per file, action `output.written`), via a single
   `CaseWorkspace.RecordOutput(relPath, producer, version, dependsOnEvidenceIds)` helper. Do not change the files' content/format.
2. **Dependency index:** `Analysis/dependencies.json` maps each FindingId (and each Vault proposal id) to the EvidenceIds it rests
   on (from SupportingEvidence/EvidenceRef, transitively through derived evidence when a parsed item names its source). Versioned
   with SchemaVersions.
3. **Re-check on open:** `CaseWorkspace.Open` (or a `Recheck()` it calls) re-hashes every stored evidence file against
   `evidence_index.jsonl`, verifies both chains (`VerifyChains`), and re-hashes registered outputs. Result object + audit entry
   `case.recheck` + `Analysis/integrity_recheck.json`. Missing file = MISSING, different hash = MODIFIED; never auto-repair.
   Large cases: hashing must stream (no whole-file reads into memory) and be cancellable.
4. **Exact invalidation:** a MODIFIED/MISSING evidence item marks exactly the findings and Vault proposals depending on it as
   `INVALIDATED` (with reason + evidence id) in an `Analysis/invalidations.json` (do not rewrite findings.json); unrelated
   findings stay untouched. Vault export of an invalidated proposal is refused (VaultExport gate) with the reason.
   Use the existing WP2 state vocabulary/Status field if one fits (check `Finding`/SchemaVersions first); otherwise add additively.
5. **External head anchor (WP3a blocker 3):** the current head hashes of both chains are written into every report/export
   manifest the case produces and shown in the re-check result, so truncation at the end is detectable by comparing with a copy
   held outside the case. Document this limit honestly in `docs/dfir/` (short section), no claims beyond it.
6. **Surface it:** where the App shows a case (LiveCase / investigation view), show the re-check verdict line (Valid / Modified n /
   Missing n / chain broken / legacy) — minimal, additive UI; no page removed.
7. **Tests (TDD):** output registration; dependency index; recheck clean / modified / missing / chain broken / legacy case;
   exact invalidation (only dependents); Vault gate refuses invalidated proposals; anchor present in manifests; streaming hash on a
   large temp file.

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

- 2026-10-09T04:25Z claude-orchestrator review:
  - HashChain re-reads the head under an exclusive file lock on every append. Two workspaces on one case used to fork the chain.
    Test `Two_open_workspaces_on_the_same_case_interleave_without_forking_the_chain` went red, then green.
  - `CaseScope.Provisional`: the LIVE placeholder scope is flagged; ScopeNote and the `case.scope_provisional` audit entry
    report it, so it is never taken as the operator-confirmed scope.
  - Merged main (WP1b #237). Allowlisted the reworded LiveSecurityMonitoringEngine explanation fragment `" / wevtutil cl) pe ["`
    (Core.dll only).

- 2026-10-09T04:35Z claude-orchestrator: #238 (WP3a) merged as `8e72b252`. WP3b spec detailed; branch created.

- 2026-10-09 claude-wp3b: WP3b items 1-7 implemented (branch `loganalyzer/wp3b-invalidation-recheck`). Build 0 errors; Dfir 48 failed = baseline, 27 new tests pass
  (`IntegrityRecheckTests` 23, `PipelineIntegrityTests` 4); UI 6 = baseline; Edition 14/14. Python suite: see commit log / final report.
  Key files (under `02_PRODUCT/projects/workspaces/loganalyzer-dfir/`): `LogAnalyzer.Dfir.Core/Case/CaseWorkspace.Integrity.cs` (RecordOutput, WriteManifest, WriteDependencies,
  Recheck, Anchor/CheckAnchor, invalidations), `Case/DependencyIndex.cs`, `Case/IntegrityRecheck.cs` (result types), `Case/HashChain.cs` (Head, Contains),
  `IO/Hashing.cs` (streaming cancellable hash), `Memory/VaultExport.cs` (Release gate), `IO/SchemaVersions.cs`; callers: `InvestigationPipeline`, `ControlReportPdf.SaveToCase`,
  `AiCaseAnalysis`, `LiveCase.IntegrityLine`, `InvestigationViewModel.IntegrityLine`/`InvestigationView.xaml`; doc `docs/dfir/INTEGRITY_RECHECK.md`.

- 2026-10-09T05:10Z claude-orchestrator review: the LIVE case (every `LiveCase.Get()` caller is a UI command) now opens with
  `recheck: false` and runs `Recheck()` on a background task; `IntegrityLine` says "Verificare de integritate în curs…" until it
  ends and reports a failed re-check as failed, never as clean. A newly created LIVE case is not re-checked (nothing to check).

## Next
- PR against main; Windows CI green; merge. Then queue item 4 (WP4, verification layer v1).

## Blockers (owner decisions; safe defaults implemented)
- LIVE case (`LiveCase.Get`) has no creation dialog: if the operator has not entered a scope, it is created with a PROVISIONAL scope
  (period now..+1 year, approver "necunoscut (de confirmat)", most restrictive category air-gapped+classified, Notes "scop provizoriu").
  Owner should decide the real policy (e.g. force the scope dialog before first LIVE use).
- DISPOSED only from ARCHIVED (strictest reading); owner may allow it from other states.
- Admin-collector warning fires when the collecting machine equals the audited host and the process token is a local Administrator
  (Windows only; false elsewhere). Written once per workspace instance.
- Chain limit: deleting lines from the END of a log is not detectable without an external anchor of the head hash (WP3b candidate).
- Read-only scan allowlists three exact detector-indicator literals in LogAnalyzer.Core.dll (RansomwareDetectionEngine patterns "wevtutil cl security/system"
  and one fragment of LiveSecurityMonitoringEngine's explanation); they are matched against events, never executed.
- WP3b: `INVALIDATED` is a separate marker in `Analysis/invalidations.json`, not a new value of `Finding.Status`/`StandardState` (no existing state fits; changing the enum would touch the frozen spec vocabulary). Owner may want a state in the contract later.
- WP3b: `CaseWorkspace.Open` now re-hashes all evidence on every open (spec). On a very large case this blocks the caller (LiveCase.Get is synchronous); `Open(root, recheck: false)` + `Recheck(ct)` is available for an async/cancellable UI path. Owner decision: should the LIVE case open defer the re-check to a background task?
- WP3b: the anchor only helps if a copy is kept outside the case (documented in docs/dfir/INTEGRITY_RECHECK.md). Owner decision: where the operator is told to keep it (printout, ticket, removable medium).
- WP3b: `integrity_recheck.json` and `invalidations.json` are rewritten at every open and deliberately not registered as outputs (they would add custody entries on each open).
- WP3b: legacy detection uses the CSV custody / plain audit log as the reference for "lines that existed before chains" (VerifyChains alone reports a missing jsonl as an empty valid chain).
- Existing Dfir tests/pipeline callers now pass a scope (CaseWorkspace.Create refuses an incomplete one); no test was weakened.
