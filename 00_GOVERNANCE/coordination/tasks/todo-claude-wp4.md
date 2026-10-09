# Checkpoint — claude-wp4 (LogAnalyzer WP4: verification layer v1, "InspectraVeritas")

- **Task:** queue item 4 of `00_GOVERNANCE/coordination/projects/LOGANALYZER_DFIR/STAGE2_PLAN.md`.
  WP4 is in `docs/dfir/CONTRACT_AUDIT_STAGE1.md` §7 and closes R10.0-R10.9, R10.V1-V7, R9.7, R15.2 and U13; the audit's
  §5 item 1 lists the gaps.
- **Branch:** `loganalyzer/wp4-verification-layer`, from main @ `d0fbae7e` (WP3a #238 and WP3b #239 merged). PR not
  opened yet.

Paths are relative to `02_PRODUCT/projects/workspaces/loganalyzer-dfir/`.

## Already in place (reuse, do not rebuild)
- `Finding.Verification` is a `FindingVerification(State, Reason, Verifier, AssessedUtc)`, default NOT_ASSESSED.
  The `StandardState` vocabulary already has Verified / Supported / Unproven / Contradicted / Rejected / Unknown /
  NotAssessed. Both live in `LogAnalyzer.Dfir.Core/Model/FindingContract.cs`.
- `Finding.ContradictingEvidence` (alias `Contradictions`) is declared and never set. `MissingEvidence`, `SemanticType`,
  `Provenance` and `Limitations` also exist.
- Existing building blocks:
  - `Integrity/ProvenanceBinder.cs` (rejects findings with no or unknown evidence);
  - `Analysis/AntiOverclaim.cs` (`SemanticForArtifact`, `Constrain` — AI is never Verified);
  - `Graph/EvidenceGraph.cs`;
  - `AI/EvidenceReasoner.cs` (AI statement validation);
  - WP3b `CaseWorkspace.Recheck` / `LoadInvalidations` / `dependencies.json`;
  - `Memory/VaultExport.Release` (gate).
- Pipeline: `LogAnalyzer.Dfir.Windows/Investigation/InvestigationPipeline.cs`. It writes `findings.json`, `graph.json`,
  `detections.json`, `anti_forensics.json` and the Vault export.

## Spec
1. **Independent module.**
   - A new assembly, `LogAnalyzer.Verification` (net10.0, no WPF). It references `LogAnalyzer.Dfir.Core` only and is in
     the solution and in both edition builds.
   - It reads the case's outputs from disk (`CaseWorkspace` plus the Analysis/*.json files). It never calls producer code
     (Correlation, rules, parsers) and never edits `findings.json`.
   - Entry point: `CaseVerifier.Verify(CaseWorkspace ws, VerificationOptions? o = null, CancellationToken ct = default)`.
     It returns a `VerificationReport` and writes `Analysis/verification.json`, versioned with `SchemaVersions`.
   - It registers that file with `RecordOutput` and writes the audit entry `verification.run` with the verdict counts.
   - Edition.Tests must stay green: no network, AI or process start inside it.
2. **Checks per finding.** Each check is deterministic, gives a Romanian reason and has a stable check id:
   - **SUFFICIENCY (R10.1):**
     - Each `SemanticType` has the evidence kinds it needs. For example, an execution claim needs an execution artifact:
       Prefetch, Amcache, BAM, 4688 or Sysmon 1.
     - Use `AntiOverclaim.SemanticForArtifact`.
     - Not enough evidence gives UNPROVEN, with the missing kinds listed.
   - **PROVENANCE (R10.2):**
     - Every supporting EvidenceId must exist in the evidence index, with a parser recorded.
     - It must not be MODIFIED or MISSING in the latest re-check or in `invalidations.json`.
     - A failure gives REJECTED.
   - **TEMPORAL (R10.4):**
     - First ≤ Last.
     - No time later than the case's acquisition time.
     - Supporting event times must fall inside the claimed window.
     - Unknown timestamps are noted.
     - A violation gives CONTRADICTED, or UNKNOWN when the times are missing.
   - **GRAPH (R10.5):** the finding's node and its edges in `graph.json` exist and point to existing evidence nodes.
     A mismatch is reported (UNKNOWN with the reason).
   - **CONTRADICTIONS (R10.6):**
     - A small, extensible rule set (at least three rules), each with a test. Examples:
       - execution claimed, but the binary's creation or first-seen time is after the claimed execution;
       - "process ran", but another source shows the file absent at that time;
       - a log-clear finding while the cleared channel has records spanning the claimed clear time.
     - Results go to `ContradictingEvidence` in the verification output and also set CONTRADICTED.
   - **MISSING EVIDENCE (R10.7):** for each claim, the expected artifact kinds absent from the case. Merge with the
     rule's own `MissingEvidence`.
   - **UNSUPPORTED (R10.8):** no supporting evidence gives REJECTED.
   - **AI CLAIMS (R10.9):** where an AI analysis output exists, each AI statement gets a verdict. AI statements are never
     VERIFIED (`AntiOverclaim.Constrain`). A statement citing a REJECTED or CONTRADICTED finding is CONTRADICTED.
3. **Verdict per finding (R10.V1-V7), most severe wins:**
   - REJECTED;
   - CONTRADICTED;
   - UNPROVEN;
   - UNKNOWN;
   - SUPPORTED: sufficient, provenance OK and consistent, with a single source kind;
   - VERIFIED: as SUPPORTED, but at least 2 independent evidence kinds agree;
   - NOT_ASSESSED: no check applies to that semantic type.

   The report carries counts per verdict and every check result, so each one can be drilled into (U13).
4. **Gates (R15.2):**
   - `VaultExport` refuses proposals whose finding is REJECTED or CONTRADICTED and lists the reason in
     `vault_refused.json`. UNPROVEN and UNKNOWN proposals are exported marked "neverificat".
   - The pipeline runs the verifier after graph/detection/anti-forensics and before the Vault export (R9.7).
   - The verdict is written into each exported proposal.
5. **Report banner:**
   - `ControlReportPdf` and the investigation view show one verification line, for example "Verificare: 3 VERIFIED,
     5 SUPPORTED, 2 UNPROVEN, 1 CONTRADICTED".
   - If any finding is CONTRADICTED or REJECTED, it shows a warning banner text.
   - Minimal, additive UI: show the per-finding verdict as one column or label where findings are listed. No page removed.
6. **Docs:** `docs/dfir/VERIFICATION.md` covers what each check proves and does not prove, and its limits. Make no claim
   of "independent verification" beyond what the code does (same application, separate module).
7. **Tests (TDD):**
   - each check, with positive and negative cases;
   - each verdict;
   - the three or more contradiction rules;
   - AI statements never VERIFIED;
   - the Vault gate;
   - the pipeline writes `verification.json` and registers it in custody;
   - an old case without `graph.json` or `dependencies.json` gives UNKNOWN with a reason, not a crash.

## Verification required before push
- `dotnet build LogAnalyzer.slnx -p:EnableWindowsTargeting=true` → 0 errors.
- Dfir/UI/Edition tests: only the known Linux-only failures (Dfir 48, UI 6); all new tests pass; Edition green.
- `python3 -m pytest -q -p no:cacheprovider` once → 0 failed. New .md files are listed in
  `20_TESTS/fixtures/unreadable_notes_allowlist.json`.

## Done
- 2026-10-09T05:20Z claude-orchestrator: spec written, branch created.

## Next
- Implement 1-7, verify, push. The orchestrator opens the PR.

## Blockers
- None yet.
