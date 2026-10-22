# todo-claude-loganalyzer-release

- **Task:** owner order 2026-10-22: work until LogAnalyzer is ready for use. Measure = the project's own release gate
  (`02_PRODUCT/projects/workspaces/loganalyzer-dfir/docs/dfir/RELEASE_GATE.md`, `release-gate/gate-items.json`) + SECURITY_AUDIT.md.
- **Branch:** `claude/loganalyzer-release-gate`, worktree `.claude/worktrees/la-rel` (build ONLY here), base E7 head efa0a03b9 (stack on #307).
- **Updated:** 2026-10-22 (WP4 closed, register draft, notices; monitor review findings 1-4 + round 3 + owner decisions done, needs Opus review).

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

## Release state 2026-10-22 (main session, verified)
- Gate (pwsh 7.6.7 installed with owner OK): `Invoke-ReleaseGate -Mode Enforce` on fresh TRX = RELEASE READY for 0.1.0-dfir, PASS=7 WAIVED=2, exit 0;
  gate self-tests 35/35. real_corpus + differential WAIVED by a waiver signed by Claude with the owner's delegation (key pair in
  %USERPROFILE%\.loganalyzer-waiver, public key committed); findings register finalized with the same delegation (36: 32 resolved, 4 owner items).
  The corpus is not on this station (D:\LAB_CORPUS missing); the lab needs it too.
- Licence: key pair made with LogAnalyzer.KeyGen (private key DPAPI in %USERPROFILE%\.loganalyzer-license); public key in
  LogAnalyzer.Core/Licensing/license-public-keys.txt, kept LOCAL (owner's .git/info/exclude); demo licence for this station until 2027-04-22,
  verified by `OwnerLicenseLiveTests` (LADFIR_LICENSE_FILE). A CI build has no key, so it refuses every licence (fail closed) by design.
- SDK 10.0.301 -> 10.0.402 (runtime 10.0.13, fixes the SignedCms CVE flagged by RuntimeGuard); lock files refreshed, locked restore clean.
- Demo installed: %ProgramFiles%\LogAnalyzerDemo\LogAnalyzer.Standalone (Release single-file, self-contained, runtime 10.0.13+4099f8, license.lic next to
  it); `--self-test` PASSED; started elevated: sign-in window (licence accepted; primary admin account already exists on the station).
- Owner: sign in with the primary admin password; QuestPDF licence before any public institution; Authenticode; #21 legacy trees.
- Next (optional, owner-independent): unknown shell item types, Zone.Identifier/MOTW, ControlReport.UserActivity caller, users register (B9),
  NU1510 warnings (redundant PackageReference ProtectedData/EventLog after the SDK update).

## Real-PC UI walkthrough 2026-10-22 (installed Standalone demo, signed in with the owner's test account; screenshots in the session scratchpad only)
Findings to fix (UI only, no behaviour change):
1. Every page: the "no data" banner shows the same text as title and subtitle ("Nu există încă date — Colectează acum" twice).
2. Status bar: "Analiză AI finalizată: Risc 35/100 | 0 Kerberos …" with no data collected (misleading: no analysis ran); and "Izolare automată: nu este disponibilă în ediția clasificată" (edition is now "Standalone").
3. Home card "Verifică un suport" says "cu driverul" (driver columns were removed from the monitor).
4. Device monitor grid: Nume/Tip/Producător/Model columns collapse to ~2 characters; the Alertă column wraps one character per line so one row fills the screen; header text lists "controlere și componente interne" (contradicts the scope text below); two different counts ("Conectate acum: 3 · cunoscute de stație: 6" vs "Conectate acum: 7 · cunoscute de monitor: 45") without saying what each counts; large empty gap above the scope text.
5. DatePicker watermark "Select a date" in English (transfer register, investigation); dates shown "01-Sep-26" (scope dialog) instead of Romanian dd.MM.yyyy.
6. Transfer register: grid headers truncated ("Clasi", "Destina", "Medi", "Mart"); text refers to page „Medii de stocare” (now „Monitor dispozitive”); an external SSD is called "stick".
7. English help text: users register (Person; Accounts; Sids; Clearance; ClearanceValidFrom/To; NeedToKnow; ZonesAllowed), procedure profile ("Day: Mon..Sun … Start/End"), policies (DRAFT → VALIDATED → TESTED → APPROVED → DEPLOYED → VERIFIED).
8. "Autentificare și conturi": the three password-change boxes have no labels (current / new / repeat).
9. Station-control wizard: the hint "Apăsați „Înainte” ca să alegeți perioada" stays on steps 2 and 3.
10. Window title in English marketing text ("Threat Triage & Incident Response Command Center").
Not exercised: running the station control by automated click was refused by the session's permission classifier.

### Fix session 2026-10-22 (items 1-10 DONE in code; UI only; not committed to main)
Build: `dotnet build LogAnalyzer.slnx` 0 errors. Tests: Dfir 2284 passed / 31 skipped / 0 failed; App 656/0 (644 + 12 new); UI 354 / 1 skipped; Edition 45/0.
1. Banner title/subtitle: `LogAnalyzer.App/Services/CollectAll.cs` (EmptyTitle, EmptySubtitle, Status default), `Views/Controls/NoDataPanel.xaml`.
2. Status bar: `ViewModels/MainViewModel.cs` RunAiAnalysis (neutral "Nicio analiză încă." without events; AI label only if Edition.Has(AiAnalysis));
   AI tab hidden in Standalone: `Views/InvestigationView.xaml` + `ViewModels/InvestigationViewModel.cs` (HasAiAnalysis); "ediția Standalone" in
   `MainWindow.xaml`, `Views/DashboardView.xaml`, `Views/AuthView.xaml`, `Core/Services/Edition/EditionProfile.cs|StationRole.cs|RoleProfile.cs`,
   `Dfir.Windows/Policy/SettingProviders.cs`; `Views/RiskAnalysisView.xaml` button renamed (no "AI" in Standalone).
3. Home card: `Core/Services/Edition/RoleProfile.cs` ("cu driverul" removed).
4. Device monitor: `Views/MediaCenterView.xaml` (scope header, MinWidth on Nume/Tip/Producător/Model/Alertă, OptionalLine style collapses empty lines),
   `ViewModels/DeviceMonitorViewModel.cs` and `ViewModels/MediaCenterViewModel.cs` (count labels). Empty-gap cause NOT reproduced visually.
5. Dates/watermark: `App.xaml.cs` (FrameworkElement.LanguageProperty ro-RO, static ctor), `App.xaml` (DatePickerTextBox template, "Alegeți data").
   Thread culture deliberately NOT changed (report formatting would change). Watermark template NOT verified visually.
6. Transfer register: `Views/TransferRegisterView.xaml` (MinWidth headers, "Monitor dispozitive"), `ViewModels/TransferViewModel.cs`,
   `Dfir.Core/Registers/TransferReconciliation.cs` (message "mediu de stocare ...").
7. Help texts: `ViewModels/RegisterViewModel.cs` (Hint), `ViewModels/ProcedureProfileViewModel.cs`, `Views/PolicyView.xaml`, `ViewModels/PolicyViewModel.cs`.
8. Password labels: `Views/AuthView.xaml` (Parola curentă / Parola nouă / Repetați parola nouă + AutomationProperties.Name).
9. Wizard hints: `ViewModels/StationControlViewModel.cs` (FlowHints, SetFlowHint on Next/Back/Restart).
10. Window title: `MainWindow.xaml` Title="LogAnalyzer" (suffix " — Ediția: Standalone" from MainWindow.xaml.cs).
Tests: new `LogAnalyzer.App.Tests/UiWalkthrough20261022Tests.cs`. Pinned old strings updated: `CollectAllTests.cs` (Status default),
`UI.Tests/TransferReconciliationTests.cs` (message), `UI.Tests/StationRoleTests.cs` ("clasificat" -> "Standalone"), `UI.Tests/EditionPolicyTests.cs`.
Open: SplashWindow.xaml still has the English tagline; live click-through of the banner/watermark/monitor layout not done (refused earlier).

## Real-PC walkthrough round 2 (2026-10-22, vault-worker; branch claude/loganalyzer-release-gate)
A. HIGH clone false positive (every medium "serie duplicată – posibilă clonă"): cause = the duplicate-serial rule compared raw manufacturers/capacities;
   one stick appears as USB node + USBSTOR disk (+ UAS node) with placeholders ("(Standard disk drives)", "Compatible USB storage device", vendor padded with a space)
   and capacity 0. Fix in `Dfir.Core/Devices/DeviceJournalModel.cs`: `KnownManufacturer` (placeholders = unknown, trimmed, case-insensitive, containment),
   capacity compared only per LUN (`LunKey`), `SerialConflict` (other VID; other PID only for storage media - a phone changes PID by USB mode; other known
   capacity of the same LUN; other real manufacturer under the same serial+VID/PID), live cross-identity check raised once for a new identity,
   `RecomputeDuplicateSerials` on every `DeviceJournalStore.Load` (stored flag not trusted; simultaneous-clone rows and port-generated clone + twin keep
   their stored mark, these need simultaneous presence). Placeholder never overwrites a real manufacturer. Old sealed alerts are untouched.
   Tests: `Dfir.Tests/DeviceMonitorCloneNodesTests.cs` (SSD/stick/peripheral/phone no clone; two capacities, other VID/PID still clones; recompute on load).
B. `Views/MediaCenterView.xaml`: MinWidth on every monitor column, HorizontalScrollBarVisibility=Auto, FrozenColumnCount=2, scope text once (top);
   phone row named "F:\" -> `DeviceMonitorRules.FriendlyName` (core merge + `DeviceMonitorViewModel` for old rows).
C. Status bar: the 35 came from the anomaly/YARA heuristics (baseline 5) over events stored by an EARLIER session, re-read at start-up. Start-up now runs
   `RunAnalysisAsync(announce: false)` -> "Nicio analiză încă."; import/collect/button announce. No events -> score 0, level NEDETERMINAT.
D. Home: `HomeView.xaml` binds `QuickControl.HomeStatus` (wizard hints replaced by `StationControlViewModel.HomeHint`).
Tests: Dfir 2294/31/0, App 661, UI 354/1, Edition 45 (build 0 errors). New `App.Tests/UiWalkthroughRound2Tests.cs`.
Open: needs Opus vault-reviewer (clone detection is security-relevant); live re-check on the station not done; VM row HomeStatus not UI-verified.

## Real-PC walkthrough round 3 (2026-10-22, build 8802d7d92 installed, hash-checked)
Verified fixed on screen: Romanian window title; banner title != subtitle; home hint; status bar "Nicio analiză încă."; "ediția Standalone";
"Alegeți data" watermark; transfer register points to „Monitor dispozitive” and calls a disk "mediu de stocare"; monitor columns readable,
horizontal scroll, scope text once.
Still wrong (real data, owner's station):
1. One physical device -> several rows: the Razer keyboard (VID 1532 PID 009C) has 3 rows, a (Standard keyboards) 258A/013B keyboard 2 rows
   (multi-interface HID collections); Kingston, Samsung SSD and the Razer keyboard each have a "Conectat" and a "Deconectat" row.
2. The Samsung SSD's USB-SATA bridge (VID 0BDA PID 9201, Realtek; "USB Mass Storage Device", same serial as the SSD) is shown as
   "Telefon / tabletă" and as a separate device.
3. "Integrated Camera" (Realtek 04F2/B83E, internal laptop camera) is listed although the scope says integrated devices are not shown.
4. Transfer register grid: after the width change the first columns (Nr., Data, Direcție) collapse ("TR", "20", "ieș").
5. Not yet checked: whether the Alertă column still shows "posibilă clonă" on those rows (the grid could not be scrolled right by automation).
Status 2026-10-22 (vault-worker, after round 3): items 1-4 DONE below; item 5 not re-checked on screen (the old clone rows are folded, see 5 below).

## Review findings on the clone change + round 3 + owner decisions (2026-10-22, vault-worker; DONE, needs Opus vault-reviewer)
All in `02_PRODUCT/projects/workspaces/loganalyzer-dfir/`; each fix has a test seen failing on e7819c71d (17 Dfir cases run against the HEAD sources; the rest need the new API).
- F1 HIGH LUN/capacity: `Dfir.Core/Devices/DeviceJournalModel.cs` `SameDevice`/`CapacityDiffers`: under ONE identity any two known capacities must agree
  (LUN number/type is device-controlled); `SerialConflict` uses the LUN only between identities; a known capacity is never overwritten (kept, row flagged).
- F2 HIGH manufacturer: `ManufacturerWords` + `SameManufacturer`: equal after normalisation (case, punctuation, company suffixes Inc/Ltd/Corp/Co/LLC/GmbH/
  Electronics/Technology/...) or whole-word prefix of >= 4 chars („Razer Inc” = „Razer USA Ltd.”; „S” != „Samsung”, „WD” != „Western Digital”); a real
  manufacturer is never overwritten (only a placeholder or empty field is filled).
- F3 MEDIUM null rows: `Dfir.Core/Devices/DeviceMonitorJournal.cs` `Load`: rows/alerts/runs/radios/pending with null fields dropped, problem
  „parțial deteriorat … (cauza: câmp null)”, rest loaded (not reset); NRE/ArgumentNull = damaged. `Dfir.Windows/Audit/StationFacts.cs` reads it inside `Try`.
- F4 LOW risk page: `App/ViewModels/MainViewModel.cs`: no score, no factors, neutral colour, „Nu există date analizate.” without events, and nothing scored at
  start-up (announce=false) so the page agrees with „Nicio analiză încă.”.
- R3.1 one device = one row: cause on the station = rows of an OLDER version (per-HID-node „ID|HID\...” rows of 2026-10-16 and „|DUP|1” rows of the old clone
  rule of 2026-10-17), not the live grouping. `DeviceJournalMerge.MarkSuperseded` (on every Load and after Apply, deterministic, never stored) FOLDS them
  for the page (`DeviceScope.IsShown`, `ShownView` earliest first-seen/latest connection, `FoldedIdentities` + `DeviceHistory.Build(identities)` for Istoric).
  Rows are NOT deleted or re-keyed (D05 still compares every row with the sealed audit); rows with two different registration numbers are not folded.
  Live: `DeviceScope.GroupKey` follows Parent to the USB device when no container id.
- Owner decision 2026-10-22 (a), mouse vs keyboard: `DeviceScope.PrimaryInputKind`: the kind of the FIRST interface (lowest MI_xx, then COLxx; mouse wins on a tie). Deviation from
  the literal wording „mouse if any mouse interface”: the station's 258A/013B keyboard has a mouse collection on MI_01 and would become a mouse.
- R3.2 bridge: `DeviceScope.Classify` drops the WPD volume view (SWD\WPDBUSENUM) as phone/camera in a group sharing container/serial with a drive or with a
  mass-storage interface (USBSTOR/UASPStor/Class_08/„Mass Storage”); old bridge rows folded into the disk with the same serial.
- R3.3 integrated camera: the station's row is an old per-node row; `IsInternalRow` (integrated name, or non-external bus ACPI/PCI/HID\ELAN) hides such rows;
  `Classify` also drops Camera/Keyboard/Mouse groups with an „Integrated/Built-in” name.
- R3.4 `App/Views/TransferRegisterView.xaml`: MinWidth on every column, Data 140, horizontal scroll, first column frozen.
- Owner decision 2026-10-22 (b) "default filter Conectate acum" is SUPERSEDED (see the follow-up below): the default is again `FilterAll` (connected + disconnected external devices).
Tests: new `Dfir.Tests/DeviceMonitorRound3Tests.cs` (38), `App.Tests/UiWalkthroughRound3Tests.cs` (6). Dfir 2332/31/0, App 667, UI 354/1, Edition 45, build 0 errors.
Open (decide with owner / Opus): (1) D05 compares a row's class with its FIRST sealed class: a device whose kind changes with this version (the station's
mouse row Tastatură -> Mouse) gives a D05 „alt tip” finding until a sealed reclassification record is designed (not done: D05 is a gate). (2)
an all-zero serial („000000000000”) is treated as a real serial. (3) report 3c / D05 still list folded and old internal rows (by design). (4) live re-check on the station.

## Follow-up 2026-10-22 (vault-worker): owner decision on the list, D05 reclassification, placeholder serial (DONE, needs Opus vault-reviewer: D05 is a gate)
1. OWNER DECISION 2026-10-22 (supersedes "connected only"): the list shows external devices that were plugged into the station (storage media and external devices),
   connected OR disconnected, one row per physical device; internal/integrated/virtual/system nodes and every HID interface on its own do not appear. Default filter
   back to `FilterAll` (the filter list is kept), page text `Views/MediaCenterView.xaml` and guide §monitor updated; the two App tests no longer set the filter.
   Test: `UiWalkthroughRound3Tests.The_list_opens_on_the_external_devices_connected_or_disconnected_and_hides_internal_nodes` (seen failing with the old default).
2. D05 reclassification: `DeviceScope.ClassifierVersion = 2` (1 = before round 3), stored in `DeviceJournalEntry.ClassifierVersion` (0 = v1) and in "device.first_seen"
   (`classifier=`; absent = 1). `DeviceJournalMerge.Apply(..., reclassified)`: same identity, kind changed, row version < current -> `DeviceReclassification`; the VM
   seals one "device.reclassified" (identity, class_old/new, classifier_old/new, operator=application). `DeviceMonitorAuditCheck.KindChain` follows first-seen kind
   + notes (note must continue the chain and raise the version); `DeviceMismatches` accepts the row/alert kinds of a valid chain; `Reclassifications` lists the
   informational lines (D05 evidence + section 3c + Istoric "Tip reclasificat"). Same classifier version, no note, a note for another identity/kind = today's finding.
   Tests: `Dfir.Tests/DeviceMonitorReclassifyTests.cs` (3 D05 cases, chain edge cases, round trip through the real audit), App test for the sealed note.
3. Placeholder serial (all one character, e.g. 000000000000, 111111): `DeviceMonitorRules.Serial` returns "" (identity = instance id, `GenericSerial` flag),
   no clone comparison (also not the "two capacities" rule), old stored rows are not flagged on Load; shown "fără serie unică (serie generică)".
   Annex D05 regenerated (`LA_UPDATE_DOCS=1`), `CheckGuide` text and sources list updated.
Tests: Dfir 2347/31/0, App 668, UI 354/1, Edition 45, build 0 errors.
Open: (1) two different sticks of one model sharing a placeholder serial share the instance id, hence one row (capacity of the first kept); (2) old rows keyed "SN|..|000000000000"
stay as rows of their own until folded; (3) live re-check on the station; (4) Opus review of KindChain trust rules.

## Final review fixes 2026-10-22 (vault-worker, after the last review round; device monitor CLOSED pending the real-PC check)
Probes of the reviewer (p4 a1-a4, c2, c3; p5) are tests in `Dfir.Tests/DeviceMonitorFinalReviewTests.cs` (13) + `App.Tests/UiWalkthroughRound3Tests` (1 new VM test).
1. HIGH integrated/built-in by NAME removed from `DeviceScope.Classify`: a group is internal ONLY when `IsAttestedInternal` (every node `PnpSnapshotEntry.Removable == false`,
   read by `PnpEnumerator.Removable` = DEVPKEY_Device_Capabilities CM_DEVCAP_REMOVABLE, i.e. the hub/port/ACPI data; null = unknown = external). The local machine
   container was already out of scope. A node with any external or unknown indicator is journaled. `IsInternalRow` (display of OLD disconnected rows) still hides by
   name: display only, D05/report still list the row (residual, recorded). Test: p4 c3 + `A_group_is_internal_only_when_every_node_is_attested_not_removable`.
2. HIGH laundering: `DeviceJournalMerge.Apply(..., sealedKinds)`: old kind and old classifier version come ONLY from the SEALED audit (`DeviceMonitorAuditCheck.SealedKinds`
   = first-seen + valid notes; the VM reads it fresh, chain must verify; none = no reclassification); the change must be in `DeviceScope.KnownReclassifications`
   (v1->v2: Keyboard->Mouse only, also enforced in `KindChain`); and the v1 rule re-run on the live snapshot (`ScopedDevice.KindAtVersion`, v1 = keyboard when a
   keyboard and a mouse node are both there) must give the sealed old kind. Anything else stays the D05 „alt tip” finding. The journal's ClassifierVersion is no longer read for the trigger.
   Existing reclassify/App tests moved to the keyboard+mouse pair and a sealed first-seen record (their old fixtures were a mouse-only device and journal-only version).
3. MEDIUM generic serial, OWNER-CHANGEABLE DECISION (main session): a registration number on a medium without a unique serial (placeholder/no serial, not a card) is bound to
   the capacity + volume serial + disk signature recorded when the number was set (`DeviceJournalEntry.RegistrationBound*`). A later medium on that row whose KNOWN
   capacity, volume serial or signature differs does not inherit it: `RegistrationMediumMismatch`, shown „neînregistrat”, critical alert „mediu fără serie unică: nu
   corespunde mediului înregistrat” (also when swapped between two readings), `DeviceMonitorRules.EffectiveNumber` is "" for D04/D05 counts, report, page, alert decision.
   Same capacity and volume serial = registered; the registered stick back clears it. Setting a number on the mismatched row binds it to the new medium (one number per row).
   Residuals: the binding is journal-resident (the number and the alert are sealed, the binding is not); a registered legacy row is bound to the first medium seen after upgrade; one row = one number.
Not verified: `Removable` on a real internal webcam/keyboard (CM_DEVCAP_REMOVABLE of the interface children) - if Windows reports them removable the internal camera is listed (fail closed) - real-PC check pending.

## State at end of session (2026-10-22, main session)
- Branch `claude/loganalyzer-release-gate` head 5b4ded8a6, draft PR #308 (on #307 on #306 on #303). Installed demo = this head
  (%ProgramFiles%\LogAnalyzerDemo\LogAnalyzer.Standalone, hash = publish, --self-test PASSED); suites Dfir 2372/31/0, App 670, UI 354/1, Edition 45.
- Real-PC verified with screenshots (owner rule, memory `verify-on-real-pc-with-screenshots`): home/status bar, transfer register, device
  monitor (Razer = one "Mouse extern" row; the same mouse over Bluetooth = "Dispozitiv Bluetooth" with alert; bridge/camera rows gone).
- Device-monitor review: 2 Opus rounds used (clone detection); later small changes (fold by same instance id, Bluetooth kind v3, stored BT rows)
  were verified by tests + real PC only, not by an Opus review: run one `vault-reviewer` pass on 55e0a4a00..5b4ded8a6 before merge.
- Not exercised on the real PC: station control run (automated click refused by the permission classifier), investigation with user activity, PDF reports.
- Disk: C: had 0 GB; old worktrees' bin/obj/publish deleted (owner rule, memory `disk-full-move-to-d-and-clean`), 5.4 GB free.
- Next: Opus review of the delta above; owner starts "Fă controlul stației" (or allows the automated click) so the control, PDF and
  investigation pages can be checked with screenshots; then merge order #303 -> #306 -> #307 -> #308.

## Opus review of 55e0a4a00..5b4ded8a6 (2026-10-22, next session)
- vault-reviewer round 1: CHANGES_REQUIRED. MEDIUM: `FoldSameInstance` (step 1c) folded a real serial clone row (SerialConflict) into the original, or the
  disconnected original under a connected clone (display only; D05/report and the "serie duplicată" alert were intact). FIXED: the same guard as step 1
  (`rows.Any(o => SerialConflict(r, o))` -> no fold) + test `The_same_instance_fold_never_folds_a_real_serial_clone_in_either_direction` (red before the fix).
  Dfir 2373/31/0, App 670/0.
- LOW residual (pre-existing, not a regression, NEEDS OWNER ACCEPTANCE): `FoldLegacyIntoPresent` can fold a disconnected legacy row of another class
  (e.g. v0 NetworkAdapter `&MI_02`) or another unit of the same model (HID\ id, no USB parent) under a connected row: the page shows the connected row's
  decision. Display only (D05/report use IsInScope). Possible fix: cross-class fold only for pairs in `KnownReclassifications`.
- Next: round-2 review of the fix delta only; then owner: station control run, merge #303 -> #306 -> #307 -> #308.
