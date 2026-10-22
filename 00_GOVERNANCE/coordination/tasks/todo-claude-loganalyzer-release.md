# todo-claude-loganalyzer-release

- **Task:** owner order 2026-10-22: work until LogAnalyzer is ready for use. Measure = the project's own release gate
  (`02_PRODUCT/projects/workspaces/loganalyzer-dfir/docs/dfir/RELEASE_GATE.md`, `release-gate/gate-items.json`) + SECURITY_AUDIT.md.
- **Branch:** `claude/loganalyzer-release-gate`, worktree `.claude/worktrees/la-rel` (build ONLY here), base E7 head efa0a03b9 (stack on #307).
- **Updated:** 2026-10-22 (WP4 closed, register draft, notices).

## Gate state (2026-10-22, from code; the gate needs PowerShell 7, not installed locally, CI runs it)
- PASS expected: tests_passing, synthetic_corpus, evidence_integrity; audit_integrity probably PASS (AuditChainCustodyTests exists; RELEASE_GATE.md "Today" stale).
- Blocking, code work: (1) response_verification (WP9) DONE 2026-10-22, (2) ai_claim_validation (WP4, `AiClaimValidation*Tests`) DONE 2026-10-22,
  (3) critical_findings (`release-gate/findings-register.json`, format in RELEASE_GATE.md).
- Waivable, owner: real_corpus, differential (owner run or signed waiver).
- Other: QuestPDF missing from THIRD_PARTY_NOTICES.md; RELEASE_GATE.md "Today" column stale.
- Owner only: Authenticode certificate + signed SHA256SUMS (#10), legacy trees decision (#21), licence keygen key, QuestPDF Community eligibility, HG585 paperwork.

## Order
1. ~~WP9 response verification~~ (done, needs Opus vault-reviewer pass). 2. WP4 AI claim validation. 3. findings register + notices + docs. 4. CI gate run read once. 5. Nice-to-have code rows.

## Done
- 2026-10-22 WP9 response_verification (vault-worker): one contract `LogAnalyzer.Dfir.Core/Response/ResponseVerification.cs`
  (ResponseVerifier re-reads each target with timeout; throw/denied/timeout -> NOT_VERIFIED, old state -> FAILED, partial -> FAILED + list;
  `[ControlledResponse]`; process identity PID+start+image SHA-256). Tests `ResponseVerification{,Process,Registry}Tests` (Dfir.Tests, fakes;
  Registry = IL scan of production assemblies for gate/primitive callers). 2 UI.Tests assertions NOT_VERIFIED -> FAILED (stricter contract).

| Response (UI trigger) | Before | Now |
|---|---|---|
| Program containment / release (Izolare procese suspecte) | Success after COM call, no re-read | rules re-read + process re-enumerated; Contained/Released only if VERIFIED; new state NotVerified; reused PID not resumed |
| Station isolate / restore / IoC block (countermeasure modal) | APPLY/VERIFY, but re-read error read as "absent"; not in case audit | tri-state re-read (0/1/other); outcome to LIVE case audit, unaudited = no success |
| Audit-policy change (Activare audit) | exit code only | effective policy re-read (AuditQuerySystemPolicy) |
| Policy apply / rollback (Politici) | re-read already; rollback re-read error = RollbackFailed | + RollbackNotVerified; `ExecutionRecord.Verification` |
| Media sanitization (Sanitizare) | read-back already | mapped to `SanitizationResult.Verification` |
| Kill / service stop / USB block / quarantine | not present in production (kill redirects to containment) | - |

- 2026-10-22 WP9 review round 1 (Opus) + fixes (vault-worker): (1) HIGH resume only when the PID's current holder matches the
  captured PID+start+SHA-256; unknown identity / unreadable list -> not resumed, NOT_VERIFIED, operator told; (2) HIGH suspend only when
  the PID runs `programPath` (and `ContainmentOptions.ExpectedSha256` when given), else REJECTED check, rules verified separately;
  (3) pre-WP9 Contained/Released without re-check shown as "neverificat" (grid, detail, PDF: `ContainmentText.State(incident)`);
  (4) `ContainmentAutoPolicy.AlreadyHandled` skips Failed incidents with active rules, `ContainmentText.Summary` names them;
  (5) Registry IL scan: missing assembly fails, Dfir.Tests references LogAnalyzer.Infrastructure; (6) refused contain/release and
  audit-policy refusals audited; sanitization read-back with data = EȘUATĂ (UI, text+PDF certificate, hash covers flag);
  Release handler `ContainmentRelease.Run` never throws, says resume happened + audit failed; RELEASE_GATE.md audit_integrity row.
  Tests: Dfir 2175/31/0, App 637, UI 354, Edition 45. Changed contract: HostChangeRoleGateTests now expects the refusal audit line.
  Residual: VM does not pass a suspect SHA-256 (SuspectProcess has none); round 2 review = delta only.

- 2026-10-22 WP4 ai_claim_validation (R22.5): case-level validator `LogAnalyzer.Dfir.Core/AI/{AiCaseFacts,AiClaimValidator,AiValidationStore}.cs`
  (SUSTINUT/PARTIAL/NESUSTINUT/NEVERIFICABIL; deterministic, no model judge, no network; counts recomputed; injection text = NESUSTINUT; result
  `Analysis/ai_validation.json` in custody + `ai.claim_validation` audit entry + evidence binding -> stale). Gate `AiCaseAnalysis.ValidateAndStore`.
  Tests: `AiClaimValidationTests`, `AiClaimValidationReportTests` (Dfir.Tests, incl. IL registry of AI-text paths), `AiClaimValidationUiTests` (App.Tests).
  HostChangeRoleGateTests narrowed (only audit_chain.jsonl + LogAnalyzer_Audit.log change under Logs on a refusal).
  Tests: Dfir 2225/31/0, App 643, UI 354, Edition 45.

| AI text path | Before | Now |
|---|---|---|
| AI screen (AiAnalysisView grids) | raw accepted statements | validated rows (state, warning, struck NESUSTINUT) |
| Analysis/ai_reasoning.json (+ raw, catalog) | raw accepted list | accepted = validated only, NESUSTINUT moved to rejected, per-statement `Validation` |
| Analysis/verification.json AiStatements | text, verdict | + `ClaimValidation` (NEVERIFICABIL if the file has none) |
| Investigation PDF | no AI text | validated AI section (summary, warnings, struck NESUSTINUT, stale notice) |
| Vault export / other PDFs / CSV | no AI text | unchanged, listed in the registry test (must not touch raw AI text) |

- 2026-10-22 WP4 review round 1 (Opus) + design decision (main session): a deterministic validator confirms VALUES, never MEANING. Fixes (vault-worker):
  (A) top state SUSTINUT -> VALORI_CONFIRMATE ("valori confirmate"), always with caveat `ClaimStates.Caveat`; AI screen has "Clasificare" (UNPROVEN) + "Validare";
  (B) values matched against the cited event/finding only (evidence file alone -> PARTIAL "citează doar fișierul, nu evenimentul"; the file never lends other events' values);
  (C) extraction of users (domain-user, user@host, keyword), hosts (keyword, FQDN), MITRE ids (sub-technique accepted), ports, "de N ori"/"N conexiuni";
  (D) negation / verdict words -> PARTIAL "sensul nu poate fi verificat automat"; (E) `AiClaims` (verification.json) applies CheckAgainst + custody check, never
  VALORI_CONFIRMATE next to REJECTED/CONTRADICTED; (F) injection reason says when the quote is in the evidence, "act as" no longer matches alone;
  (G) `AiValidationStore.TryRead` checks custody SHA-256 (mismatch / no entry -> all NEVERIFICABIL "fișierul de validare a fost modificat");
  (H) App.Tests scan fails on ANY App type reading raw AI types besides AiAnalysisViewModel/AiClaimRow. Tests: `AiClaimValidationReview{,Report}Tests` (Dfir.Tests).
  Tests: Dfir 2260/31/0, App 644, UI 354, Edition 45 (one ResponseVerificationTests timing flake seen once, passes on rerun).
  Residuals: extraction is best effort (names in prose without keyword not compared; fixed negation word list; stoplist after "user"/"host"); statement-level state in
  ai_reasoning.json is not cross-checked claim by claim against ai_validation.json (only the validation file's custody hash + evidence binding). Round 2 = delta only.

- 2026-10-22 WP4 final round (round 2, last; vault-worker) + release items. WP4 CLOSED. Each fix has a test seen failing on eadd40c04 (`AiClaimValidationFinal{,Report}Tests`,
  `ResponseVerificationPoolSaturationTests`, Dfir.Tests):
  (1) VALORI_CONFIRMATE only when ONE cited record holds every value (`ClaimFacts.Check(text, [r], countBasis)`); spread over records -> PARTIAL
  "valorile provin din înregistrări diferite" (counts of the cited set still recomputed over the whole set);
  (2) claim NFKC-normalised, then `Lookalikes.Find`: token mixing scripts / invisible Cf char / non-ASCII digit / fixed Latin confusable table / ASCII digits next to a
  Cyrillic-Greek look-alike -> NESUSTINUT "conține caractere care imită alte litere" (Romanian diacritics not flagged, tested);
  (3) `ResponseVerifier.Observe` runs the re-read on a dedicated background thread (TCS wait), timeout still injectable, default 20 s unchanged, timeout = NOT_VERIFIED;
  test: failure modes 60 s limit, timeout mode waits on a ManualResetEventSlim released at the end; saturation test blocks 2x ProcessorCount pool threads and asserts
  the re-read never runs on a pool thread (non-parallel collection). 20x loop of the WP9 classes: 20/20 green (21 tests each);
  (4) `AiClaims` normalises the stored state with `ClaimStates.Parse` (legacy SUSTINUT / unknown -> NEVERIFICABIL);
  (5) time of day "la ora N" / "ora N:MM" compared with the record's hour (record's own offset); other residuals listed in RELEASE_GATE.md limits
  (bare small numbers, paraphrased negation, names without keyword, number words, all-Cyrillic words).
- 2026-10-22 findings register DRAFT `release-gate/findings-register.draft.json` (36 entries: 32 fixed with verified commits, 4 owner items #10, #21, licence key,
  QuestPDF), `prepared_by: agent, unverified`; schema-valid (gate function run under PS 5.1 on it: PASS); gate reads only the exact path (new assertion in
  `release-gate/tests/Test-ReleaseGate.ps1`, needs pwsh 7 = CI). `findings-register.json` NOT created (owner only). RELEASE_GATE.md §4 says the owner reviews and saves it.
- 2026-10-22 THIRD_PARTY_NOTICES.md: QuestPDF 2026.7.2 (QuestPDF Community License; eligibility owner check) + every direct PackageReference of the 17 lock files.
  FLAG for owner: QuestPDF licence v3.0 says public-sector entities / government agencies are NOT eligible for Community regardless of revenue.
  Tests: Dfir 2284/31/0 (+24), App 644, UI 354, Edition 45; solution build 0 errors.

## Next
- Main session: read the CI release-gate run once (pwsh 7 self-test incl. the new draft assertion), then nice-to-have code rows.
- Owner: review `findings-register.draft.json` and save as `findings-register.json`; QuestPDF licence decision (public-sector clause); #10, #21, licence key.
- Residuals WP9: IL scan excludes WPF assemblies and delegate-injected primitives; `NotVerified` incidents are not re-contained automatically; abandoned hung re-read
  threads stay alive (background) until the reader returns.
