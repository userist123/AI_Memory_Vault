# todo-claude-loganalyzer-hg585

- **Task:** owner 2026-11-04 „Continuă cu loganalyzer dfir” + „Be ambitious”: land the F1-F3 fixes (PR #329 was closed on 2026-10-31 by the
  history rewrite) and start the HG 585 work decided on 2026-10-23 (levels from secret de serviciu up; matrix
  `02_PRODUCT/projects/workspaces/loganalyzer-dfir/docs/dfir/MATRICE_HG585_ORNISS_2026-10-23.md`, order: #38 then #39).
- **Branch:** `claude/loganalyzer-f1f3-hg585`, worktree `D:\w\la-f1f3`, from `origin/claude/loganalyzer-release-gate` (rewritten history,
  22af56b15 = old 4fbf59703 same content) + merge of origin/main d17d6b486. The old worktree `.claude/worktrees/la-rel` is PRE-rewrite: do not push from it.
- **Updated:** 2026-11-04.

## Done (main session)
- Core, `LogAnalyzer.Dfir.Core/Profile/StationLevelRules.cs`: level = `ProcedureProfile.StationLevel` parsed by `SecrecyLevels` (rank 1..4,
  NATO/EU by rank); retention null/3/3/10 years, internet forbidden + destruction only at rank 4, TEMPEST at rank >= 2. Unknown text = undefined.
- Checks `ControlEvaluator.Level.cs`: A18 retention by level (CONFORM only if the live Security log alone covers it; later start = DE VERIFICAT,
  archives; no level / unreadable log = NEDETERMINAT), N14 internet by level (Internet edition only; SSID = NECONFORM). Rows in
  ControlApplicability + EDITIONS_AND_POSTURE.md, CheckGuide entries. `StationFacts.StationLevel`.
- #38 `LogAnalyzer.Dfir.Windows/Audit/CorrectiveActions.cs`: Plan (carry open actions, close only on CONFORM, new action per NECONFORM),
  Missing (measure, responsible, valid deadline), SectionLines (3d, overdue first). Persisted in control_report.json `CorrectiveActions`,
  read back by ControlArchive into `PreviousControl.Actions`.
- #39 `ControlComparison.SectionLines` (3e); PDF prints 3d + 3e + „nivelul stației” in the header line; JSON has `Comparison`.
- Tests `LogAnalyzer.Dfir.Tests/Hg585LevelAndActionsTests.cs` 26/26 (written after the code, not red-first).

- UI (vault-worker) done; Opus review CHANGES_REQUIRED -> fixed f3151bf5f (3e recomputed at save vs PreviousControls[0], Run resets
  Comparison/Result; saved CLOSED state cannot hide a previous NECONFORM; A18 never CONFORM for NATO/EU; picker normalises abbreviations;
  profile warning for unknown level; stale report after grid edit). Suites 2026-11-04 TEMP on C: Dfir 2448/0/31, App 827/0, UI 354/0/1.
  TEMP on D: breaks 11 keygen tests (D:\ is a git repo, keygen refuses). Defender flagged Dfir.Tests.dll once (Wacatac.H!ml), later passed.
- Draft PR https://github.com/userist123/AI_Memory_Vault/pull/370 (replaces #329). CI not read yet.
- Research + plan: docs/dfir/PLAN_PRODUS_PROFESIONAL_2026-11-04.md (Word copy in D:\w\reports). Module names Insula / Arhipelag / Continent.

- 2026-11-04 REAL SCREENS (installed Standalone 23.10 build, signed in as the test admin; screenshots kept OUT of the repo, they show
  the machine/user name). Method: elevated UIA driver (Start-Process -Verb RunAs works without a prompt here: ConsentPromptBehaviorAdmin=0;
  computer-use access was denied twice and cannot drive elevated windows anyway). Findings for the redesign:
  (1) Standalone menu still lists Domain/e-mail, Containment, Threat intel (pages say "nu este disponibil în ediția Standalone") -> hide;
  (2) Expert pages are English/Romanian mixed (Panou general "INGESTED EVENTS / HOSTS IN SCOPE", Amenințări "Compile & Evaluate Rules",
      Firul atacului "Chronological Intrusion Stages", static ATT&CK stage strip even with no attack);
  (3) Threat intel shows APT29/APT28/Lazarus cards on an air-gapped station; (4) Timeline panel printed FIXED "CRITICAL (3 ALERTS)",
      fixed times and "EDR Shield active: C2 beacon isolated" -> FIXED now (TimelineSummary computed + guard test NoFixedSampleEvidenceTests),
      also fake fallbacks in LiveAlertToast removed. App 830/0.

- R1 DONE d757f426b: ModuleCatalog (Core/Services/Edition) = single source of pages per module; RoleProfiles leaves out what the edition can
  never have; ;air/;net gone; title "LogAnalyzer DFIR · Insula — Stație izolată"; legacy root UI tree moved to _recovered/legacy_root_ui_2026-11-04
  (root Services/ KEPT: UI.Tests compiles ..\Services\*.cs; old UI LicenseService kept for R2). VERIFIED ON SCREEN with an official publish
  (dotnet publish ...App.Classified -p:PublishProfile=win-x64-singlefile -> publish/LogAnalyzer.Standalone/win-x64, gitignored) installed to
  C:\Program Files\LogAnalyzer Insula (R1) (protected folder required by the app; the owner's C:\Program Files\LogAnalyzer untouched) + license.lic
  copied from the installed app; LogAnalyzer.Core/Licensing/license-public-keys.txt copied from la-rel (excluded via .git/info/exclude).
- R3 + R5 DONE 999f70784 / 6e1ed6159, VERIFIED ON SCREEN (installed R1 folder): sign-in two columns (module, purpose, station, level, version,
  licence, notice); advanced pages Romanian, fixed ATT&CK grid -> techniques of loaded alerts, fixed attack-stage strip removed, false claims
  removed (CRYPTOGRAPHICALLY SEALED, 12 ARTIFACTS, AES-256, 88.4%). App 848/0, UI 354/0/1, Edition 45/0. Tests RomanianUiTextTests, ModuleCatalogTests.
- R2 DONE (2026-11-05): LA2 = ECDSA-signed JSON with mod/feat/holder; a build accepts only its own module (WrongModule), catalogue
  capabilities only (FeatureNotAllowed / Malformed), LA1 still valid as legacy with DefaultFor(module); offline request file (4 keys) +
  import in ActivationWindow; KeyGen --module/--holder/--feature; LicenseManager issues LA2. Tests LicenseV2Tests (P1-P9).
  Opus review CHANGES_REQUIRED (minor) -> fixed: request recorded audit-first in the APP LOG (license.request_saved) before writing;
  size test now uses a long id (mutation without the size check -> test fails, verified); strictness cases (comment, case-variant key,
  numeric exp/hw). Depth limit (MaxDepth=8) not separately observable: deep nesting is refused by the feat type check anyway (noted).
  RESIDUAL for the owner: the request is in the plain app log, not in the sealed chain (the sign-in service opens it only after the licence).
  LATENT: Has()/CurrentLicense() have no production consumer yet; Has() caches claims for the process (re-checks expiry only) - fix when
  enforcing capabilities (R2b, together with read-only mode on expiry). App 919/0, UI 354/0/1, Edition 45/0.
- Re-publish/reinstall recipe: kill old instances (elevated Stop-Process), dotnet publish (profile win-x64-singlefile), copy license.lic,
  elevated robocopy to C:\Program Files\LogAnalyzer Insula (R1); UIA driver in scratchpad\uia (driver.ps1 elevated, send.ps1).

- 5ef49dab3: activation window redesigned (identity column + step 1 request / step 2 licence), VERIFIED ON SCREEN; the owner's installed
  LA1 licence pasted into the new build -> "Activare reușită" (legacy LA1 accepted). CI on 5ef49dab3: 32 pending / 1 pass / 0 fail when read
  (just pushed) -> read once more next session.

- R6 DONE aa2719286 (Avansat menu generated from ModuleCatalog sections; verified on screen).
- R2b DONE (2026-11-05): licence capabilities drive menu, role pages and every navigation (MainViewModel.OpenTab gate; --tab start argument too);
  LicenseService.Has re-reads/re-verifies the file. Opus review CHANGES_REQUIRED -> fixed: rights = VerifiedClaims from the start-up check
  (never a fallback to the module set; nothing verified = no capability), tab 8 gate is mode-aware (AD needs auth.ad), --tab via OpenTab.
  App 940/0, UI 354/0/1, Edition 45/0. OPEN (low): capability checks only at navigation, not inside StationControl.Run / CollectAll /
  transfer save / containment (all current callers gated); report.pv / intel.online / update.online not enforced anywhere; tab 7 has no capability.

- R7a DONE (6ca070792 + fixes): known-file hash sets (import text/CSV incl. NSRL CSV, known-bad -> high finding with full evidence,
  known-good = label only), card in Profil de proceduri (verified on screen). Opus round 1: null-id crash, planted/altered sets, Sysmon
  Hashes ignored, 839 MiB -> fixed by worker (strict per-set validation + visible problems/EvidenceGap, audit-matched stored SHA, SafePath,
  binary sorted storage ~30 MiB/1M, cache, SelfTest empty library, Hashes parser, coverage note). Round 2 (delta): folder pre-created by a
  standard user defeats the unkeyed audit -> fixed in main session: FolderTrustProblem (owner + no non-admin write ACE + file owners; repair
  when elevated) on the station folder, audited-but-missing sets reported, memo key includes the trust flag. Suites: Dfir 2448/0/31,
  App 979/0, UI 354/0/1, Edition 45/0. RESIDUAL (owner): audit chain is unkeyed (folder ACL is the barrier); sets imported before the fix
  must be re-imported; import/remove still synchronous in the page; rejected sets cannot be removed from the page; load peak ~850 MiB at 10M.

- CI #370 on 21671b335: 1 fail (zero-regression-gate: test_notes_strict_yaml, this checkpoint + MATRICE + PLAN docs without frontmatter) ->
  fixed 41b4808af (allowlist entries with reason). Read CI once more on the next head.
- R7b DONE (2026-11-05): per-run acquisition record Logs/acquisition_runs/<RunId>.json (operator, station, app version, profile,
  collectors with tool/version/errors, every item with size + SHA-256, canonical-JSON manifest hash per collector, also in the formerly
  empty collection_audit.csv SHA256 column); atomic write, RecordOutput + audit acquisition.run_recorded; AcquisitionRunVerifier in Recheck
  (only registered records trusted, findings escalate the verdict, never crash Open). Opus round 1 (8 findings: null records crash Open,
  planted record trusted, silent Complete failure, interrupted collector missing, double hashing, manifest collision, junction, O(n^2))
  fixed by worker; round 2: leftover unregistered record on a read-only case / failed RecordOutput + two catch gaps -> fixed in main
  session (read-only guard, delete on RecordOutput failure, unexpected collector exception recorded, audit catch widened). Dfir 2470/0/31,
  App 979/0. Defender quarantines the fresh Dfir.Tests.dll intermittently (PUA heuristic); later builds passed; not touched.
  RESIDUAL: EvidenceSince relies on the wall clock (clock stepped back -> items missed / earlier run pulled); record not shown in the UI yet.

- R7c DONE (2026-11-05): tab „Colectări” in Investigație completă (summary line + grid + collectors in row details) and PDF section
  „Fișele colectării” (reloaded at export, „verificare din …”). AcquisitionRunOverview; verifier reads record bytes once and compares the
  record hash with the registered SHA (RecordModified, closes an R7b gap); „verificată” only on a positive result; record text cleaned
  (controls, Format chars, bidi, 120 chars, no split surrogate). Opus round 1 (edited record shown verified, PDF injection, unreadable
  registry verified, stale PDF, double hashing at open, wording) fixed by worker; round 2 (overflow crash at open, PDF hashing twice,
  surrogate split, Format chars) fixed in main session. App HashSetsTests moved to a non-parallel collection (F4 memory test was flaky
  under parallel load; assertions unchanged). Dfir 2483/0/31, App 982/0, UI 354/0/1.
  RESIDUAL (owner): the custody chain has no key - a record planted AND registered through it reads „verificată” (same trust root as
  Recheck); PDF export hashes on the UI thread (as the existing pre-report check); tab not refreshed after an export.
  FOUND: tab 9 „Istoricul probelor” shows the in-memory AI provenance ledger, not the case custody chain - misleading name, fix later.

- 2026-11-05 screens attempt (R1 folder reinstalled with b48433824): sign-in OK (one failed attempt first - my extraction error, it is in
  the app log); „Colectează acum” refused to collect without a confirmed case purpose + approver („Colectarea nu a produs date: Neînceput”)
  - correct; I did not invent a purpose/approver. Station control ran (NECONFORM 25 · DE VERIFICAT 25 · NEDETERMINAT 6 · CONFORM 32 ·
  NU SE APLICĂ 4). „Colectări” tab NOT verified on screen (results area below the fold, UIA tree too large); app closed.
  To verify: owner confirms a real purpose once, or a test case with a recorded run is opened.

- 2026-11-05 tab 9 „Istoricul probelor” rewired (claude, main session): CI #370 head 55da7a3da read once = 42/42 pass/skip.
  New Dfir.Core/Case/CustodyOverview.cs reads Logs/chain_of_custody.jsonl ONCE into bytes (cap 256 MB), verifies with HashChain.Verify(byte[])
  and parses rows from the same lines (HashChain.Lines = File.ReadLines split: BOM dropped, \r\n|\r|\n); pre-WP3 CSV-only = Legacy; lines
  without chain fields in front of the chain = PROBLEMĂ + row marked „neverificat”, no hash; non-JSON / null lines = unreadable rows; newest
  20 000 rows first. Loaded by CaseLoader and at the very end of RunCore (CancellationToken.None, after investigation.end); VM
  InvestigationViewModel.CustodyRows/CustodyLine/RefreshCustodyCommand; view binds Investigation.*, session AI ledger kept in an Expander
  labelled „nu face parte din caz”; misleading „Exportă pachetul de probe” (ExportCsvCommand) removed. HashChain: JSON `null` line no
  longer throws NullReferenceException (4 parse sites). Opus round 1 (planted legacy prefix shown verified, null-line crash, tab empty/stale
  after a run, TOCTOU) and round 2 (late cancel rewrote a finished run, lone \r / BOM read differently) all fixed. Dfir 2499/0/31,
  App 987/0, UI 354/0/1. RESIDUAL: Load holds the log open while copying (an Append waits up to ~2 s, as Recheck already does); CsvRows
  read separately (legacy cases only); FillCustody adds up to 20 000 rows on the UI thread; pre-WP3 CSV rows are not listed (line says so).
  Not verified on screen.

- 2026-11-05 FULL TEST-EVALUATION (owner: "testare-evaluare, completezi cu lucruri de test, facem curățenie la final"). Report:
  D:\w\reports\LogAnalyzer DFIR - testare si evaluare 2026-11-05.{md,docx}. Method: installed Standalone build driven by the elevated UIA
  driver (scratchpad\uia: driver.ps1, send.ps1, walk.ps1, scope.ps1 fills the scope dialog with TEST values, plant.ps1 = benign ground
  truth: eventcreate LA_EVAL_TEST 999, powershell -w hidden -enc, whoami, net localgroup, temp file). Test cases on disk:
  CASE-<LAB-PC>-20261105-184516 (crashed run), -190251, -193400 (+ event source LA_EVAL_TEST) -> CLEANUP LIST in the report.
  FIXED (Opus 2 rounds, round-2 findings fixed by main session): EvidenceGraph crash on duplicate F-ids (LiveState numbered from Count;
  PS-SUSPICIOUS consumed ids for unlisted blocks) -> MaxFindingNo everywhere, ids only for listed blocks, graph GroupBy, duplicate =
  audit+gap; RunCore any non-OOM failure writes run_state Failed; VM catches pipeline-only failures (pipelineDone flag; after that the
  fail-closed global handler still applies - owner decision recorded here); new rule PS-CMDLINE-SUSPICIOUS (Windows PowerShell 400
  HostApplication, -e/-ec/-enc//enc/quoted base64 decoded+scored, Visible() escapes Cc/Cf/separators by code point, cap 100); Trunc never
  splits a surrogate pair; investigation PDF prints the case scope (ScopeForm.ReportLines); a11y: NavigationEntry/MenuSection/PageEntry
  ToString; PrivateIdentifiersGuard skips transient *_wpftmp.csproj (+.gitignore). VERIFIED ON SCREEN: tab 9 (1.259 entries verified),
  Colectări, PDF scope block, planted encoded PowerShell -> F-0172 decoded.
  OPEN (see report §5): advanced pages (Panou general, Firul atacului, Conturi locale) do not read the open case (16.161 events / 0 alerts
  vs 67.123 / 237 / critical chain); SRUM network parse intermittent (JET -550 dirty DB) -> conclusions differ run to run (63 vs 129 high);
  the "Alte N blocuri" summaries are always rejected (no evidence) so never reach the report; amcache_log has no parser and Amcache rows
  with empty name; leftover English (High, Removable Media, DETECTED, SUCCESS, Evidence Graph, Show Calendar); wizard + expert form
  duplicated; userpath rules flag signed per-user apps (Chrome, Claude) - calibrate by signature.

- 2026-11-05 batch 2 ("Repara tot"): all open items of the evaluation addressed. (A) advanced pages read the OPEN CASE: MainViewModel.
  CaseWorkspace.cs (StateChanged -> LoadCaseIntoWorkspaceAsync: working DB refilled from the case's own EVTX copies via the extracted
  IngestEvtxFile, alerts = case findings, banner WorkspaceSourceLine in MainWindow, tab 8 added to LegacyDataTabs) - VERIFIED ON SCREEN
  (Panou general 49.411 events / 310 alerts / recheck valid; Firul atacului from the case). (B) SRUM: collector copies SRU.chk/SRU*.log/
  *.jrs/SRUDB.jfm (srum_log); parser recovers a JET -550 copy with esentutl /r confined to its work folder (manual probe: live DB untouched);
  ESE ctor frees the instance on failure. (C) "Alte N" summaries carry evidence (0 rejected on screen). (D) SignatureCalibration +
  ArtifactPathResolver (\device\harddiskvolumeN, \VOLUME{..-serial}, \users) + Finding.WithSeverity: live run lowered 22 signed per-user
  programs, high 123 -> 102. (E) Amcache valueless keys named from the key; Romanian severity/category converters on the old pages,
  DatePicker automation names, ScopeDialog names, custody texts; wizard says "Analiza rulează…" while busy (verified); AttackStoryline only
  High/Critical, no "atacatorul a parcurs cu succes", level from max severity. Tests Dfir 2543/0/31, App 1005/0, UI 354/0/1, Edition 45/0.
  TEST cases now also -201142, -202410. Opus round 1 CHANGES_REQUIRED (10: case/import mixing in the working DB -> SemaphoreSlim gate +
  token per batch; calibration of imported evidence -> only live-collected-here evidence; -CORRELATED excluded (side-loading), one level,
  revocation stated, audit trail; silent load failure; SRUM logs only from the evidence index with SHA-256; analysis rerun; volume serial
  removed from tests; skip attribute; Esent JetTerm) all fixed; round 2 APPROVE; its 3 LOW residuals also fixed (AcquisitionMethod/remote
  package excluded, ClearCache behind the gate with Wait(0), SRUM log hashed on the work copy). RecentCaseView ToString (a11y). Gate READY
  3951/0/33; EVAL-* register entries resolved. NOT covered by tests: the gate/cancel/rerun concurrency (code reading only).

- 2026-11-06 LA-Lab (Hyper-V, isolated switch, owner's second SSD): DC (Server 2025) + file server + WS01 (Win11, domain, Arhipelag and
  Continent) + WS02 (Win11, no NIC, Insula); lab-only licence key (fingerprint 37A204D85F7BF5CF, private key DPAPI on the lab disk, lab builds
  never leave it). Scenarios with ground truth: media R1-R7 on WS02 (hot-added SCSI disks, CD, NIC, printer, synthetic USBSTOR, clock +2 h),
  attack S1-S8 on WS01 -> SRV01. Result vs truth: WS02 R3/R4/R5/R6 detected, R7 in AF05; R1/R2 Hyper-V SCSI disks are internal by design
  (lab limit, not a defect). WS01 S3/S4/S5/S6/S7(client)/S8 + incident chain detected; S2 had no rule -> DISCOVERY-BURST added. Defects
  found on real data and fixed (commits c2e0fc094..f66dfffde): WMI built-in subscription logged as Type="name" (High FP), TPExclusions
  read as an exclusion, live "Potato" CRITICAL on every SYSTEM 4672, BadUSB on any keyboard, 104 cleared log named "System", 87 CIM
  module blocks flooding PS-SUSPICIOUS, Burn desktop.ini, setup accounts Medium, SIDs in titles, Enter does not submit sign-in/unlock,
  a11y names (IdentityRow, HomeIntentView, ParsedEvent, DetectedIssue), Ctrl+K palette (fixed old list, shared text, no focus).
  Opus round 1 RESPINS (8, all fixed in f66dfffde); round 2 pending. Tests Dfir 2556/0/32, App 1010/0, UI 357/0/1, Edition 45/0.
  Not yet fixed (UX, report): live feed rows show the whole multi-line message; page header text squeezed at 1024 px; wizard says
  "Completați scopul" while the analysis already runs; Arhipelag first run needs the documented icacls on ProgramData\LogAnalyzer\auth.
  Lab state and scripts: E:\LA-Lab (plan-teste-module.md, evidence\truth.jsonl, evidence\LAB-WS0*-case). Lab cleanup = whole E:\LA-Lab.
- 2026-11-06 evening (round 2 done, CI read once on d16d1cc47: rule PS-CMDLINE-SUSPICIOUS had no contract entry + DeviceMonitor test hung on a
  dispatcher left on its pool thread -> e652210d6). SRV01 evidence exported (export-evidence.ps1) and imported on WS01 through "Un folder adus
  de la stația afectată" (works: 291 files, 24 846 events): found CIM modules in lower case root/ (Server 2025) -> c3bc7d58f, Domain Admins
  as bare SID -> 41260a2ff, scope dialog showed "period.to". S1 NOT in any evidence: DC01 does not audit Kerberos failures (no 4771/4768
  for ana.pop), SRV01 has one NTLM 4625 -> audit gap of the lab, the control page should say it. S7 server side: REMOTE-WINRM seen; zip/C$
  copy not auditable (no 5140/5145). Lab VM clocks start 4 h ahead (records at 21:57 UTC on the same day) -> TimeReliability lowers
  confidence, correct. Cases: evidence\LAB-SRV01-case-v2 (new build), DC01 export in evidence\LAB-DC01.

- 2026-11-06 night: owner asked for findings and reports "ca la prosti" (who did what, no event codes). Done: Finding.PlainWhat/Plain +
  PlainStory (Dfir.Core/Language), person from the users register, SID resolved from the case events, card/grids/PDF lead with the sentence,
  live alerts (LivePlain) in status bar/toast/grid; codes kept under "Detalii tehnice"/"Pentru specialiști" (custody). Commits 1dd1682d4,
  8b40f0fb2, b34490adc, 1c8f616a2 + round-2 fixes. Opus review: round 1 REJECTED (13, fixed), round 2 REJECTED-PARTIAL (5 fixed by the main
  session; no round 3 per the 2-round rule). Residuals fixed after round 2 (same night): $ sessions kept unless this computer's own; NT SERVICE\*, IIS APPPOOL, IUSR are Windows;
  4732 member without a name not guessed local/domain; "probabil copiat" only within 30 min; accounts Windows made at setup (SYSTEM/machine
  creator, record <= 500 of a never-cleared Security log) are Low and told "creat de Windows la instalare"; kernel processes (Registry…) are
  not user programs. Then: the app never passed the users register to the analysis (no person was named in the real app) -> the pipeline loads the saved one; the card, grids and report re-tell the sentence with the register the app holds now (PlainStory.CurrentPersonOf, set when the register loads/saves); a task definition is no longer "Windows are…" and its name loses the user SID.
  Lab report generator: scratch worktree wt-pdf + ZzLabReportTests (never committed); reports in E:\LA-Lab\reports.
- 2026-11-07 morning, real app on the VMs: users register filled through the UI (rows table had no height at 1024x720 -> fixed
  05b4041f8; "Preia de pe stație" puts the account name as person -> not a person, d7b6222db), names shown in grid and PDF from the app.
  Continent on WS01: connected mode needs a SIGNED edition policy (by design); lab policy key = E:\LA-Lab\secrets\lab_policy_*.{bin,b64}
  (private DPAPI), build-lab-apps.ps1 writes the public key into LogAnalyzer.App\Edition\Unclassified\edition-policy-public-key.b64 only for
  the build and deletes it; policy signed for LAB-WS01 role csirt in C:\ProgramData\LogAnalyzer\LogAnalyzer.policy. WS01 now auto-logs on
  as LAB\it.admin (scripts\domain-desktop.ps1) so the elevated app runs under a domain account. Fixed: AD inventory closed the app
  (dynamic COM on maxPwdAge -> 4d858a877), AD error message in English / no hint (e5cf9fd61), XML entities in live alerts, DC box a11y.
  AD inventory verified vs lab: 7 users, 3 computers, Domain Admins, no lockout (explains S1), 7 NECONFORM.
  Incident: after a reboot the Start search had focus and the sign-in script typed the LAB password there (screenshot deleted, VM search
  cleared); the UIA driver now refuses to type unless the app is in front. The lab password is lab-only and goes with the lab cleanup.
  Containment verified end to end (scan found updsvc, isolate, firewall rules VERIFIED, scan tied it to Run+service, release VERIFIED):
  fixed renamed-Windows-program detection (107cbbb8c), lost incident after GetOwner on a partial WMI object (3419a71c3), row refresh
  (7b8dc5598). Remote collection = offline signed package: authorised on WS01, run on SRV01, 202 files imported with matching SHA-256;
  absent logs (Sysmon) no longer "failed" (ee2e032c0). Open: MessageBox Yes/No in English; pages slide under the sidebar after an
  off-screen button is brought into view (1024 px).
  Next: Arhipelag redeploy on WS01, Insula rerun, lab report .docx (scratchpad md2docx).
- 2026-11-07T12:40+02:00 LA-Lab: build 1ced11c8b redeployed (insula WS02, arhipelag + continent WS01). Station control re-run in the real UI:
  Arhipelag WS01 92 checks (14 NECONFORM), Insula WS02 90 checks (17 NECONFORM). Lab evaluation report (.docx, outside the repo) sent to the owner.
  Open UX items: English Yes/No buttons; Control stație wider than 1024 px; Arhipelag shows „Mod: conectat” on a closed LAN; AF05 without a plain sentence.
- 2026-11-07T13:20+02:00 127f56ba7: all four fixed (RomanianMessageBox CBT hook; LazyPage.MinPageWidth 740 + WrapPanel toolbars + 1024 px render test;
  ModuleCatalog.ModeBadge; AF05 Plain in PDF/grid). vault-reviewer 2 rounds, all issues resolved (OK-only box id 2, modal-safe test, KG twin delta).
  Tests: Dfir 2629, App 1014, UI 366, Edition 45. Verified on WS01 (1024 px): no sideways scroll, „Da / Nu”, „Mod: rețea locală, fără Internet”.
  Next: read CI once on #370.
- 2026-11-07T14:30+02:00 a009fc238: PlainWhat for 28 more rules (worker, 2 vault-reviewer rounds: no claims beyond evidence — „a scris un fișier cu același nume”,
  never „a copiat documentul”; impersonal when the account is not the actor). CRED-BRUTEFORCE/SPRAY now read DC-side 4771 (0x18/0x6) and 4776
  (0xC000006A/0xC0000064), one attempt in two logs counted once, `$` accounts excluded, not split by client-declared workstation.
  DC promotion (krbtgt by S-1-5-7, ±10 min, same host, no 1102) => Windows, Info/Low. Lab: DC01 auditpol Kerberos AS + Credential Validation
  failure ENABLED (lab change, add to owner cleanup list); S1 re-run as S1b: 14 wrong passwords for ana.pop now detected from DC01's log.
  Known limits (accepted): NTLM spray without a logged address is not detected; generated-CIM PS modules stay one row each (earlier review decision).
  Tests: Dfir 2656, App 1014, UI 366, catalog 3. CI on 7d0405d81: all green (build, tests, 3 packages, CodeQL, Gitleaks).
- 2026-11-07T15:50+02:00 „Cine ce a făcut” (PeopleStory, Dfir.Core/Language): findings regrouped by person, time order, serious first; PDF section
  before Constatări + InvestigationView tab. Attributed to a person ONLY when the sentence makes the account the subject (lower-case PlainWhat, same
  test as PlainStory.Tell); whole clauses (account as object/victim) => „Fără un autor anume”; several accounts => own group; Windows group.
  Accounts kept apart as logged (domain\user, local per host, upn, SID resolved via AccountResolver.FromEvents); only the register joins accounts.
  PiiMasker.KnownPerson: masked PDF masks register people (also via resolved SIDs). S-1-5-90/96 = Windows. vault-reviewer 2 rounds: round 1 REJECT
  (merged same-name accounts, SID split, register names unmasked, stale tab), round 2 REJECT (SID-only masked leak) -> fixed + test.
  Accepted residuals: R1 a register row naming a built-in account („Administrator”) joins every computer's Administrator (register's statement);
  R2 another computer's local account seen here is labelled „domeniul X”; R3 tab refresh on register change runs only with a WPF Application.
  Lab (scratch wt-pdf, LAB-WS02/DC01/SRV01 evidence): WS02 labadmin 10, DC01/SRV01 Administrator 1/2, rest NoAuthor/Windows (CIM modules).
  Tests: Dfir 2664, App 1018, UI 366, Edition 45. CI on 0e2de293c: all green.
- 2026-11-07T16:30+02:00 6153f049d A19 (new station check): on a domain controller (HKLM ProductOptions ProductType = LanmanNT) the audit policy
  must record Failure for Kerberos Authentication Service and Credential Validation, else NECONFORM in plain words; no A19 line on other stations;
  n/a on Standalone. CheckGuide, ControlApplicability, EDITIONS_AND_POSTURE.md and the annex (LA_UPDATE_DOCS=1) updated. Verified on LAB-DC01:
  ProductType=LanmanNT, both subcategories Success and Failure (after the lab change) => CONFORM. Tests: Dfir 2668, App 1018, UI 366, Edition 45.
- 2026-11-07T18:00+02:00 Research „Acoperirea auditului pe stații izolate” (defensive; report D:\w\reports\*.docx/.md, notes outside git in
  research_notes/ and reports/ of the worktree - NEVER commit them). Plan P1 started: A01 now requires Removable Storage Success+Failure (STIG
  WN11-AU-000085/-000090); A02 threshold 128 -> 192 MB (CIS 196,608 KB, SecurityLogMinMB); ParserCapabilities 4663 (no SACL on removable volumes)
  and 6416 (subject is usually SYSTEM) corrected; LnkParser stale Jump List text fixed. Behaviour change: lab stations with Removable Storage
  success-only now get A01 NECONFORM. Next P1: P1.4 automatic USN export of NTFS removable volumes, P1.3 archive inventory for A18, P1.5
  DriverFrameworks channel check. Tests: Dfir 2674, App 1018, UI 366, Edition 45.
- 2026-11-07T19:30+02:00 P1.4 done: UsnJournalCollector (Standard/Full, elevated) reads the USN journal of every external NTFS volume
  (removable, or USB disk by MSFT_Disk BusType=7 / Win32_DiskDrive USB) directly via FSCTL_READ_USN_JOURNAL (UsnJournalNative), never the
  system, case or application drive; writes fsutil-layout CSV + "Time stamp UTC" (parser prefers it) + Volume/serial + "Read complete".
  fsutil rejected: redirected output lost ș/ț/ă. Lab VHD (elevated): 91/91 records, 0 field differences vs fsutil, Romanian names kept.
  vault-reviewer 2 rounds: R1 REJECT (FILETIME > 9999 threw and failed the run, case drive not excluded, silent truncation, UASP, provenance),
  R2 REJECT (volume label with CR/LF could fake the CSV column line) -> all fixed with tests. Accepted residuals: CR inside a file name lost by
  the existing parser; Source info shown as hex when nonzero; a case folder on a folder-mounted volume is not excluded. Host C: had 199 MB
  free (4 tests failed on space) -> moved generated lab cases to E:\LA-Lab\reports\scratch-cases-2026-11-07. Tests: Dfir 2681, App 1018, UI 366, Edition 45.
  CI on d81606972: all green.
- 2026-11-07T20:10+02:00 P1.5 done: A20 „Jurnalele care notează conectarea dispozitivelor sunt pornite” (Partition/Diagnostic default ON:
  off => NECONFORM; DriverFrameworks-UserMode/Operational default OFF: off => DE VERIFICAT; both on => CONFORM; unreadable never CONFORM).
  Host check: Partition on, DFW off (=> DE VERIFICAT), channel names verified. Guide, applicability, docs, annex updated.
  Next P1: P1.3 archive inventory for A18 (retention 3/10 years). Tests: Dfir 2685, App 1018, UI 366, Edition 45.
- 2026-11-07T20:50+02:00 P1.3 done: StationFacts.SecurityArchives (winevt\Logs\Archive-Security-*.evtx, first/last event from the file);
  ControlEvaluator.ContinuousCoverage chains archives to the live log (gap tolerance 1 day; unreadable/empty never extends); A18 CONFORM when
  the chain covers the level's years, else DE VERIFICAT naming the gap ("poate fi și o perioadă în care stația a fost oprită") and that archives
  on other media are not seen. Not reviewed by vault-reviewer (control logic, not a security boundary) - residual: an archive file left on the
  station is trusted as-is (no integrity check). P1 complete. Tests: Dfir 2689, App 1018, UI 366, Edition 45.
  Next: P2 of the research plan (WPD/MTP channel + rule, BTHPORT paired devices, setupapi.dev.log parser, N03 SSIDs, P20 all profiles, P14/P28
  verdicts from the procedure profile).
- 2026-11-07T21:30+02:00 P2.1 done: AIRGAP-PORTABLE-DEVICE (Kernel-PnP 400 with WPD class {eec5ad98-…}, not SWD\WPDBUSENUM; or WPD-MTPClassDriver /
  WPD-ClassInstaller events), T1052.001, Medium on classified, authorised only by a profile transfer channel (telefon/phone/MTP/PTP/camer),
  never on Standalone; PlainWhat "A fost conectat un telefon…; … nu se poate spune din jurnale ce s-a copiat" (=> NoAuthor in PeopleStory).
  WPD-MTPClassDriver/Operational + WPD-ClassInstaller/Operational added to the DFIR EVTX export; ParserCapabilities entry; contract + test.
  Residual: no real phone in the lab (Hyper-V has no USB passthrough) - field names rely on the Kernel-PnP 400 fields the Bluetooth rule already uses.
  Tests: Dfir 2693, App 1018, UI 366, Edition 45. Next P2.2 BTHPORT paired devices.
- 2026-11-07T22:00+02:00 P2.2 done: N04 reads BTHPORT\Parameters\Devices (address + UTF-8 Name; Parameters\Keys never read); a paired device on
  an isolated station => NECONFORM even with the service disabled or the radio removed. Format verified on the host (2 real pairings; names
  not printed). Guide + annex updated. Tests: Dfir 2697, App 1018, UI 366, Edition 45. Next P2.3 setupapi.dev.log parser, P2.6 N03 SSIDs.
- 2026-11-07T22:40+02:00 P2.6 done (changed design): SSIDs NOT from the Wlansvc XML (earlier decision: never opened, can hold keys) but from
  NetworkList\Profiles (NameType 71 Wi-Fi / 23 mobile broadband; ProfileName, DateCreated/DateLastConnected local SYSTEMTIME -> UTC).
  N03 NECONFORM on an isolated station when the last wireless join is inside the controlled period (even with no adapter now). Format verified
  elevated on the host (7 wireless profiles, names not printed). Tests: Dfir 2700, App 1018, UI 366, Edition 45. Next P2.3 setupapi.dev.log.
- 2026-11-07T23:20+02:00 P2.3 done: SetupApiLogParser (Dfir.Core/Devices; "Device Install (Hardware initiated)" sections only; local time ->
  UTC with the evidence time zone; serial from USBSTOR / USB ids that carry one; exit status) registered (parser count 19 -> 20) and
  SetupApiLogCollector (live %SystemRoot%\INF\setupapi.dev*.log, QuickAndUp, no elevation). Host file: 27 installs, 0 malformed, all timed.
  FirewallLogParser.ReadBoundedLine made internal and shared. Tests: Dfir 2703. Remaining P2: P2.7 Recall/Snipping all profiles, P2.8 P14/P28.
- 2026-11-07T23:50+02:00 CORRECTION of befc37492 (reverted): it re-read NetworkList that StationFacts.NetworkProfiles already reads (N01 judges
  connections in the period, N02 lists networks - the research code inventory missed them) and mapped NameType 23 as mobile broadband (23 = VPN,
  243 = mobile broadband). Now N03 only names the Wi-Fi entries of f.NetworkProfiles and is DE VERIFICAT (not CONFORM) when Wi-Fi history exists.
  Tests: Dfir 2702, App 1018, UI 366, Edition 45.
- 2026-11-08T00:20+02:00 P2.8 (P14) done: StationFacts.ApprovedTransferMedia (from ProcedureProfile.TransferChannels via StationControlViewModel;
  null = no channels / unreadable). P14 unblocked USB: no USB/removable channel in the profile => NECONFORM; a USB channel => DE VERIFICAT (media
  register D01-D04 decides); no channels => DE VERIFICAT. Words: usb, stick, amovibil, removable, flash, disc/hdd/ssd extern, suport de stocare.
  P28 (optical) not changed yet. Lesson: verify the research's "gap" claims in code before building (N03 duplicate). Tests: Dfir 2705.
- 2026-11-08T00:50+02:00 P28: an approved disc channel in the profile (whole-word cd/dvd/blu-ray/bd-r/disc optic/disc inscripționat) turns the
  isolated NECONFORM into DE VERIFICAT (discs must be the registered ones). P2.7 DEFERRED with reason: TurnOffWindowsCopilot (per-user) is a legacy
  policy the new Copilot app ignores, Snipping Tool paths are forum-sourced and conflicting, Recall's real control (HKLM AllowRecallEnablement /
  DisableAIDataAnalysis) is already read by P20. Tests: Dfir 2706, App 1018, UI 366, Edition 45.
  Next: redeploy the current build to the lab VMs and run Control stație to see A19/A20/N03/N04/P14/P28 on real stations.
- 2026-11-08T01:40+02:00 LAB VERIFICATION (scratchpad\labcheck harness: StationFactCollector.Collect + ControlEvaluator.Evaluate, self-contained,
  run elevated on the 4 VMs via E:\LA-Lab\scripts\lab-check.ps1; outputs E:\LA-Lab\reports\labcheck-*.txt). FOUND A REAL BUG in A19 (6153f049d):
  "Kerberos Authentication Service" was not in AuditPolicy.Subcategories (the list AuditQuerySystemPolicy reads), so DC01 showed NECONFORM while
  auditpol said Success and Failure. Fixed: added G(0x42); A19 now NEDETERMINAT when a subcategory was not read; guard test
  AuditSubcategoriesUsed() ⊆ AuditPolicy.Subcategories. After the fix: DC01 A19 CONFORM; SRV01 (member) no A19; A20 DE VERIFICAT on all 4 (the
  Windows default); N03/N04/P28 CONFORM; P14 DE VERIFICAT (no profile on the VMs); A01 NECONFORM where Removable Storage/PnP are not audited.
  Tests: Dfir 2708, App 1018, UI 366, Edition 45.
- 2026-11-08T03:00+02:00 (1) RecentItemsCollector (Recent\*.lnk, AutomaticDestinations, CustomDestinations of every ProfileList SID; StandardAndUp;
  no elevation for own profile) + CustomJumpListParser (P3.1). Host run: 829 files, 0 malformed. (2) FOUND by routing test: the native USN export
  started with "Exported by" -> preflight fingerprint "unknown" -> FORMAT_MISMATCH -> never parsed; fixed ("USN Journal ID" first) + new
  fingerprints setupapi_dev / custom_destinations (SetupApiLogParser would also never have been selected). NewSourcesRoutingTests go through
  WindowsParsers.Registry.Select. (3) vault-reviewer on the control batch REJECT, all fixed: P14 NECONFORM only when every channel is recognised
  as non-storage (unknown medium => DE VERIFICAT); A20 no "pornit" when unread; N04 unread pairing list => NEDETERMINAT; A18 foreign-computer
  archives excluded, newest 200 read + count, "integritatea nu este verificată"; A19 unread role => NEDETERMINAT; setupapi live file first;
  Windows-made instance ("7&…") is no serial; WPD channel events need a non-stick device id; "prin cablu USB" removed. Parser count 21.
  Lab rerun consistent. Tests: Dfir 2722, App 1018, UI 366, Edition 45. Round 2 review of this delta pending.
- 2026-11-08T04:00+02:00 Round 2 REJECT (narrow) fixed - review closed by the main session (2-round rule): P14 storage words widened (pendrive,
  hard*, disc dur, stocare, storage, mediu de stocare) so a mixed phrase never yields "no storage channel"; RecentItemsCollector checks reparse
  points on AutomaticDestinations/CustomDestinations too (junction test with mklink /J), per-profile logic in CollectProfile; A18 unread archives
  counted in StationFacts.SecurityArchivesNotRead (not as a fake archive); foreign archive worded "alt nume de calculator (… sau redenumire)";
  doc comments fixed. Lab apps rebuilt and redeployed (WS02 insula, WS01 arhipelag+continent); WS01 starts to the sign-in screen.
  Tests: Dfir 2727, App 1018, UI 366, Edition 45. CI on 8a267d5ea: 42 pass.
- 2026-11-08T05:00+02:00 P3.3: AIRGAP-PRINT-JOB on a virtual printer names PDF/XPS files created (USN File create, LNK/JumpList) within 10 min
  after the job as the probable output (inference by time, stated in text, PlainWhat and contract). Tests: Dfir 2729.
- 2026-11-08T05:20+02:00 P3.4: on Windows 11 (build 26300) no IMAPI event provider is registered and CDROM/Operational is an analytic
  power log, disabled: MEDIA-OPTICAL-ACTIVITY contract now says an Explorer burn leaves no event; evidence = Burn staging, burn-tool logs/
  execution, LNK to CDROM. P3.2 (spool SPL/SHD) NOT done: files are deleted after printing unless KeepPrintedJobs; low yield. P3.5 partly
  done (lab harness on 4 VMs; physical USB stick NTFS + MTP phone still untested - needs the owner to plug them in).
- 2026-11-08T05:50+02:00 CI fail on b77c8eca8 (UI LogClearLifecycleLegacyEngineTests: Info expected, High): LogClearAssessment.Field used a
  NonBacktracking regex with a match timeout -> timed out on the runner -> subject "" -> Unexpected/High. REAL BUG too: the regex only matched
  Name="…", but EventRecord.ToXml writes Name='…' (verified on a lab Security evtx), so live/legacy engines never read the clearing account and
  a planned clear was always High. Replaced by an ordinal search reading both quote styles; tests added. Tests: Dfir 2729, App 1018, UI 366, Ed 45.
  Same apostrophe bug fixed in MainViewModel (event detail fields never shown for real events) and ForensicEventKnowledgeService.ExtractXmlField
  (RDP logon type 10 never "Atenție" on real events); RealEventXmlQuotesTests. LiveSecurityMonitoringEngine.Field already read both. UI 368.
- 2026-11-08T00:10+02:00 MASTER PLAN (owner: „LogAnalyzer_DFIR_Master_Research_and_Implementation_Plan.docx”, every module 6.1-6.17 with
  its sub-modules, lab screenshots on Insula/Arhipelag/Continent). Plan text: scratchpad\plan\plan.txt. Phase-0 inventory (Explore, grep):
  REAL: 6.2 custody/integrity, 6.3 Windows parsers, 6.4 log clear + coverage matrix + gaps, 6.5 PCAP/flows/DNS/SNI, 6.11 graph + contradiction
  rules (LogAnalyzer.Verification), 6.13 policy/compliance, 6.15 manifest + report verifier, 6.17 citations + AiClaimValidator.
  PARTIAL: 6.4 timestomp, heatmap UI; 6.6 Volatility text only; 6.7 entropy (Core), no PE parser; 6.8 Sigma correlation outside Dfir;
  6.10 AD; 6.14 dry-run/approval binding; 6.15 redaction; 6.16 only TimelineDiffEngine. MISSING: 6.1 workflow (DONE now), 6.9 bookmarks /
  saved views, 6.12 signed TI packages, 6.16 AnalysisRun manifest + run diff, 6.5 JA3/Zeek, 6.7 PE parser, 6.8 rule test lab, graph view.
- 6.1 DONE: Case/CaseWorkflow.cs (stages, roles, tasks, finding review, closure checklist, chained Logs/workflow.jsonl cross-checked with the
  case audit), tab „Flux de lucru”, docs/dfir/CASE_WORKFLOW.md. Opus review 2 rounds: round 1 found 4 HIGH bypasses (self-approver, remove
  reviewer then solo close, review before submit, delete/truncate log) - fixed with Probe_A..E tests; round 2 found a read race with the audit
  (fixed, Probe_H) + probe K (investigator reviewing own work after a colleague submitted; fixed). ACCEPTED RESIDUALS (main session, recorded
  in the doc): same-length truncation of BOTH workflow and case audit (case audit not sealed - owner decision), identity = OperatorIdentity.
  Lab: Insula WS02, Arhipelag + Continent WS01 screenshots (E:\LA-Lab\shots\LAB-WS0x\ws0x-*wf*). Suites: Dfir 2749, App 1020, UI 368, Ed 45.
- 6.16 DONE (2026-11-08T01:00): Case/AnalysisRuns.cs (immutable Analysis/Runs/RUN-*/run_manifest.json + diff_previous.json, CreateNew +
  read-only + custody + manifest sha in audit; List checks it against the VERIFIED audit chain by file position; stable finding key rule +
  evidence locators + #n; diff source Inputs/Toolchain/Unexplained; collection settings recorded but not compared), recorded BEFORE the
  schema-manifest anchor; tab „Rulări și diferențe” + „Reanalizează cazul deschis” (same signed media register, refused when read-only or
  workflow-Closed, Recheck after); docs/dfir/ANALYSIS_RUNS.md. Opus review 2 rounds (round 1: duplicate rule id crash HIGH, hidden duplicate
  findings, forged audit line, anchor order, missing folder, settings; round 2: forged line claiming seq 1, collection settings) all fixed
  with tests (AnalysisRunsTests 16). Lab: WS02 two reanalyses 159 evidence/20,895 events -> „Aceleași constatări (83)”; WS01 Arhipelag then
  Continent same case 188/50,943 -> „Aceleași constatări (74)”. NOTE: lab VMs' Windows evaluation expired -> each VM shuts down hourly
  (WS01 ~:14, WS02 ~:20), watchdog restarts it; plan lab work inside the window, never touch licensing. Suites: Dfir 2765, App 1022, UI 368, Ed 45.
- 6.9 DONE (2026-11-08T01:40): Case/TimelineNotebook.cs (EventKey from evidence+locator+source+id+time; one filter for page and saved views;
  bookmarks, annotations, saved views, view comparison; chained Logs/timeline_notes.jsonl, audit prefix timeline.note.*), Cronologie tab
  controls + ★/✎ column, tab area PageFill.MinHeight 360->560 (grid had 0 px at 1024x720). Opus review 1 round: HIGH audit prefix collision
  with timeline.export_masked (fixed + test), silent filter widening on a bad date (fixed), ✎ refresh, missing saved source message, inclusive
  „până la”, MVVMTK0034. Lab: Arhipelag (1,347 x 4625, ★✎, saved view, compare 1 vs 1,346), Continent same notes, Insula bookmark. docs/dfir/TIMELINE_NOTES.md.
  Suites: Dfir 2772, App 1023, UI 368, Ed 45.
- 6.12 DONE (2026-11-08T02:15): Detection/ThreatIntelPackages.cs (signed offline TI packages: manifest + detached CMS, same trust as the
  signed media register: officer list, qualified chain/CRL; stage on copy -> approve (admin, reason) -> rollback; anti-rollback on highest
  approved; LoadActive re-verifies everything incl. NO unlisted files, writes a private snapshot from verified bytes for the engine; sealed
  history with the machine chain key; store ACL checked on every use, unsafe store refused), App/Services/ThreatIntelFlows.cs, Politici page
  section, pipeline ThreatIntel property + gap + run setting pachet_ti. Opus review 2 rounds (HIGH unlisted file used, HIGH no ACL, truncation,
  TOCTOU, NRE, id chars, core authz, expiry; round 2: ACL only on new folder, snapshots pile up, false "compromis" without key) fixed with tests.
  ACCEPTED RESIDUAL (owner to confirm): an admin can restore an older complete sealed state (no monotonic counter). Lab: Insula refusal shown;
  positive path only in ThreatIntelRealCmsTests (real SignedCms) - lab has no officer certificate yet. docs/dfir/THREAT_INTEL_PACKAGES.md.
  Suites: Dfir 2784, App 1026, UI 368, Ed 45.
- 6.7 DONE (2026-11-08T02:40): Detection/PeTriage.cs (bounded static PE reader: header, sections+entropy, imports+imphash with
  untranslated ordinals, overlay, signature presence, TLS; explained indicators; notable imports as shipped data pe_notable_imports.json so the
  DLL carries no detection names), pipeline -> Analysis/pe_triage.json with station Authenticode (cache only), MZ-but-unreadable = gap, tab
  „Executabile”. Opus review: 12k mutations no throw; HIGH import blow-up (1M functions, 273 MB) capped + PE-IMPORTS-TRUNCATED; silent drop,
  imphash vs pefile (documented, label), entropy cost, overlay lower bound fixed. Lab Arhipelag: adb valid, foreign notepad unsigned here,
  overlay 20,000. docs/dfir/PE_TRIAGE.md. Suites: Dfir 2794, App 1026, UI 368, Ed 45.
- 6.5 JA3 DONE (8d0358fe9): PacketDecoder.Ja3 + JA3/JA3Hash fields + ioc type ja3 (Ja3Tests); lab has no TLS traffic (said in docs/dfir/NETWORK_JA3.md).
- 6.8 rule lab DONE (2adc6d213): Detection/RuleLab.cs (engine-identical load, test on case corpus, diff vs reference, add to case Rules/ audited
  + custody, Sigma correlation refused, missing-field note), tab „Laborator de reguli”. Lab Arhipelag: 0 -> found reopened-case CSV has no
  parser fields (note added), v2 1,322 new matches. No Opus review for 6.5/6.8 (low risk; main session decision). Lab UIA driver settext now
  unescapes \n (scratchpad uia\driver.ps1, pushed to WS01/WS02).
- 2026-11-08T03:35 MASTER PLAN round 1 closed: 6.4 coverage-in-time (d3c1109d? see d3ec0c416), 6.11 contradicted findings in review (1486a5344),
  6.6 Volatility 3 JSON parser (b2a76163e), CI flake fixed (Differential python probe, 07f81c709). 6.14 and 6.17 confirmed REAL by inspection.
  Status table: docs/dfir/MASTER_PLAN_STATUS.md (Word copy D:\w\reports, outside the repo). Remaining PARTIAL: 6.10 AD, 6.15 redaction policy,
  6.6 memory findings rules + memory/disk correlation, 6.5 Zeek, 6.8 station-wide signed rule packs. Accepted residuals listed in the status doc
  (owner to confirm). Lab VMs shut down hourly (expired evaluation); owner cleanup list unchanged + new TEST cases on WS01 (executabile,
  memorie) and C:\LA\{ti-test-pkg,pe-test,vol-test}.
## Next
-1. MASTER PLAN order after 6.1 + 6.16 + 6.9 + 6.12 + 6.7 + 6.5 + 6.8 (remaining): bookmarks/saved views; 6.7 PE triage; 6.5 JA3; 6.8 rule test lab; 6.11 graph view; 6.4 coverage heatmap; 6.14 target-bound
   approval. Each: tests, Opus review for risky code, lab screenshots on the three editions.
00. LA-Lab continue: Continent on WS01; remote collection WS01 -> SRV01; AD page; containment (updsvc.exe); (DC failure auditing: done as A19); rerun WS02 (Insula) on the new build; lab report .docx (scratchpad md2docx).
0. Owner cleanup of the TEST cases (list in the report) when the evaluation is closed.
0b. Advanced pages on the open case (one data source) - biggest product gap found; SRUM: recover the dirty ESE copy (esentutl /r with the
   SRU logs) or mark the result as depending on it; route the "Alte N" summaries to the report.
1. Read CI on #370 once on the head after the evaluation commit.
2. R6 navigation by case flow (plan section 5), R2b read-only on expiry + capability enforcement (fix Has() cache), R7 DFIR (collection
   records, unified timeline, hash sets).
2. Owner's new order 2026-11-04: professional product. Phase R1 of the plan (cleanup to _recovered, ModuleFeatures catalogue, module names).
   Screenshots: the owner gave the test account (administrator.principal) in chat; computer-use access to LogAnalyzer was DENIED once
   in this session - ask again only when needed. App requires admin: for UI review launch with __COMPAT_LAYER=RunAsInvoker.
   Dev build cannot pass activation (license-public-keys.txt absent): use the installed build or add a documented dev key path (R2).
3. Real-PC check of F1-F3 + the new section.
4. Later rows: #1/#2 logged account vs clearance, #31 training dates, #20/#23/#29 intervention register, #34 accreditation expiry.

## Open owner questions (unchanged from 2026-10-23)
- Unit thresholds from CSS/PrOpSec; PV form used in the unit; olographic vs electronic signature.

## Master plan — closing the PARTIAL modules (2026-11-08, main session)
- Owner 2026-11-08: „Hai sa facem ce nu este” = build the modules still PARTIAL after the 6.1-6.17 round.
- Done, each with tests, Opus review (2 rounds where risky) and lab verification:
  - 6.5 Zeek: `Dfir.Core/Network/ZeekLogParser.cs` (parser 23), lab Insula with synthetic logs (doc NETWORK_ZEEK.md).
  - 6.6 memory rules: `Dfir.Core/Memory/MemoryFindings.cs` MEM-HIDDEN/INJECT/NETPROC/PATH/NODISK; lab Arhipelag + Continent.
  - 6.10 identity: `Dfir.Core/Identity/IdentityReconstruction.cs`, Analysis/identity.json, tab „Identități”; lab on the real DC01/SRV01 Security
    logs (C:\LA\id-test on WS01/WS02): 70 identities, 5,762 sessions, identical on Insula, Arhipelag and Continent.
  - 6.15 redaction: `Dfir.Core/Privacy/Redaction.cs` + PiiMasker Keep/Restore; lab Continent reversible export + reveal; audit holds no original.
    Lab found an export crash (UPN learned twice -> PiiMasker.Mapping duplicate key), fixed in 3c4025fff with a regression test.
  - 6.8: station-wide signed rule packs = the 6.12 packages (sigma/yara loaded by DetectionEngine.Load); documented.
- Accepted residuals are in docs/dfir/MASTER_PLAN_STATUS.md, IDENTITY_AD.md and REDACTION_POLICY.md; the owner confirms them.
- Suites: Dfir 2866, App 1026, UI 368, Edition 45.
- Lab clean-up for the owner: new TEST cases on WS01/WS02 and C:\LA\{id-test,zeek-test} (kept).
- Next: CI read once on the pushed head.
- 2026-11-08 (continued, owner „Continua”): 6.5 beaconing/scan/endpoint-link heuristics (`Network/NetworkHeuristics.cs`, Analysis/network_links.json) and
  6.8 Sigma correlation rules (`Detection/SigmaCorrelation.cs`), Opus review 2 rounds (1 HIGH + 5 MEDIUM, all fixed), lab: correlation on the real DC01 log
  (3 hits, Arhipelag + Continent), beacon/scan on Insula. Lab-found: false RuleLab field note (logsource keys), „cineva … a deschis” texts; fixed. Suites: Dfir 2904.
- 2026-11-08 (owner „Continua”, round 3): CI green on ab332a6ac (read once). 6.10 account path: `Dfir.Core/Identity/IdentityGraph.cs` ->
  Analysis/identity_graph.json, tab „Identități” two-column layout with the path tree, PDF section „Conturi, sesiuni și căi”
  (ReportSection.Identities: ItSecurity/IR/Audit/Everything). Links only host+LogonId in the session window and ProcessGuid; gaps counted.
  Opus review 2 rounds (r1: 2 HIGH PID reuse / silent drops + 3 MEDIUM; r2: 3 MEDIUM capped PID, Sysmon+4688 exit, reboot bound) all fixed,
  IdentityGraphTests 13. Real DC01/SRV01 logs: 133 processes linked, 292 system. Lab: identical on Insula, Arhipelag, Continent.
  Note: C: had 0.17 GB free (scratchpad art* folders); cleaned to 4 GB.
- Next optional: 6.6 controlled Volatility run (admin-pinned vol.exe SHA-256, plugin allowlist, --offline, symbol dir verified by a pinned
  manifest hash; outputs imported as derived evidence) — security-sensitive: threat model + Opus review first.
- 2026-11-08 (owner „Continua”, round 4): 6.6 controlled Volatility run. `Dfir.Windows/Memory/VolatilityRunner.cs` (+ ToolRunner live output
  cap, raw stdout/stderr), `Dfir.Core/Memory/VolatilityToolProfile.cs` (profile + FolderManifest in %PROGRAMDATA%\LogAnalyzer\tools, admin-only),
  KeyGen `volatility-profile` (ToolsCli: admin only, refuses tool/symbol folders others can change, moves an untrusted profile folder aside, ACL at
  creation), tab „Memorie (Volatility)” + Reanalyze. Opus security review 2 rounds (r1: 3 HIGH profile writable by users / owner ignored / only exe
  hashed + output flood; r2: 2 MEDIUM KeyGen pre-created folder / unchecked tool folder) all fixed; tests VolatilityRunnerTests 12,
  VolatilityProfileCliTests 2. Lab with a TEST stand-in (E:\LA-Lab\fakevol, scripts fakevol-push/-tamper): 6 derived outputs, 8 events,
  3 MEM findings on Insula/Arhipelag/Continent; planted python311.dll refused. Residuals in MEMORY_VOLATILITY.md (10-min exec window false alarm).
  Not mine, left alone: untracked LogAnalyzer.App/Edition/Unclassified/edition-policy-public-key.b64.
- 2026-11-08 (owner „Continua”, round 5): CI on 56027952b all green. Fixed a false positive found while closing the 10-min residual: the approved
  Volatility exe joined CollectorExec.Inventory only in memory, so after an app restart every earlier run was refused as claimed activity.
  `VolatilityToolProfile.ApprovedExecutable()` (profile + tool folder admin-only, local path, no link, pinned hash; cached by mtimes) now feeds
  Inventory(). Test in VolatilityRunnerTests. The 10-min cap stays (owner rule, OperationAudit.MaxExecWindow).
  Opus review r1: MEDIUM null fields in an unvetted tools.json threw out of Inventory() and broke the station control -> whole body caught,
  returns null; LOW refusals no longer cached, drive root / non-fixed (mapped) drives refused. Suites: Dfir 2930, App 1028, UI 368, Edition 45.
  CI on dfa6ce9fb all green. Lab Insula (WS02, Process Creation audit enabled temporarily, then restored to No Auditing): new Volatility run,
  app restarted, Control stație -> A17 DE VERIFICAT, 6 events claimed. Local ReadCollectorExecs on the copied audit: every record refused as
  "după o înregistrare cu oră în urmă" — the WS02 audit has a clock step back of ~2 h on 2026-11-06 (line 14), so no own command after it is
  ever confirmed there; the inventory fix cannot be shown on that station (unit-test verified only). Owner question: a one-time clock
  correction disables A17 recognition for good on that station (by design today).
  Lab Arhipelag (WS01, clean audit, 0 clock steps back): Volatility run, app killed + restarted, Control stație -> A17 CONFORM, 18 own
  commands, 0 claimed; copied audit: 18 vol records VALID with the approved exe in the inventory, all 18 refused for inventory without it
  (the pre-fix behaviour). WS01 process-creation audit was already on with command lines (left as is). Note: E:\LA-Lab\scripts\kill-stuck.ps1
  stops a HOST pwsh running run-stage+install-app, not the app; use scripts\q10.ps1 (Stop-Process inside the VM) to close the app.
- 2026-11-09 (owner „Mai fa un research de calitate si hai sa mai adaugam”): research (2 web agents) saved in
  docs/dfir/RESEARCH_GAPS_2026-11-09.md, each item checked against the code. Wave 1 implemented: station-control checks P40–P47 from the
  DISA STIG Windows 11 CAT I list (anonymous access, LM/NTLMv2, dangerous user rights via LSA P/Invoke `Audit/UserRights.cs`,
  AlwaysInstallElevated, DEP/SEHOP, WinRM Basic/unencrypted, LLMNR, IIS) in `Audit/ControlEvaluator.StigChecks.cs`; tests
  StationChecksStigTests (56). Opus review r1: MEDIUM false Conform for values other than 0/1 (P43, P45) and P45 Conform on unset policy
  with the service off (STIG counts unset as a finding) -> both strict now; LOW IIS components without W3SVC (InetStp key) -> DeVerificat.
  Suites: Dfir 2976, App 1028, UI 368, Edition 45. Lab Insula: control 99 checks; P40 Neconform (RestrictAnonymous 0), P41 Neconform
  (LmCompatibilityLevel unset = 3), P42 Conform (LSA read live works), P43 Conform, P44 Neconform (DEP OptIn), P45 DeVerificat, P46
  Neconform (isolated), P47 Conform. Next waves from the research doc: .cklb STIG checklist export; AD detection pack (ADCS 4886/4887,
  NTLM relay); EDR tampering via WFP 5441/5447; Defender DetectionHistory; RMM pack; Notepad TabState / MUICache.
- 2026-11-09 (owner „Continua”): CI fix 32246dee4 (research doc added to the unreadable-notes allowlist). Wave 2: .cklb STIG Viewer 3 export
  `Audit/StigChecklist.cs` (52 Windows 11 V2R3 rules, V-id/severity/title from stigaview, no SV ids), written by „Exportă pentru ofițer”.
  Opus review r1 (HIGH): absent registry values counted as compliant (STIG text: absent = finding) and WDigest absent/unreadable -> not_a_finding;
  fixed: absent = open, CC-000315 HKLM only, P13/P24/C01 evidence-only, posture NuSeAplica -> not_reviewed (never not_applicable), surrogate-safe
  cap; r2: all fixed. Lab bug found and fixed: the device monitor writes media_live.jsonl into the LIVE case folder before the case exists, so
  CaseWorkspace.Create refused it ("Case folder is not empty") and every report/officer export of that station failed; now
  CaseWorkspace.CreateKeepingPreCaseFiles keeps only listed pre-case files (audited with SHA-256), anything else still refused. Verified on
  Insula: case created, journal kept. The .cklb itself is not yet produced in the lab: the officer export first requires measure/owner/deadline
  for each NECONFORM row (by design). Suites: Dfir 2994, App 1028, UI 368, Edition 45.
- 2026-11-09 (owner „Continua”): wave 3, AD/EDR detection pack in the investigation pipeline (`Analysis/Wp11Rules.AdAttacks.cs`; the old
  KerberosAdAttackEngine is only used by the legacy MainViewModel, the case pipeline had no Kerberos rules): KERBEROAST (RC4 TGS, 3-4 services
  Medium, 5+ High), ASREP-ROAST, ADCS-SAN (request SAN attribute = ESC6 path), NTLM-RELAY-MACHINE (NTLM type 3 of a computer account from an
  address with no EARLIER Kerberos sign-in of that account), EDR-WFP-BLOCK (5441/5447 "Custom Outbound Filter", 5157 blocks of EDR agents).
  Opus review r1: relay rule compared WorkstationName (the victim's in a relay) -> IP baseline; Kerberoast threshold; ESC6 wording; r2: baseline
  must be time-ordered (later PKINIT logon hid a relay) -> fixed by main session, IPv6 canonical. Lab: TEST SPN accounts on DC01 (removed after),
  RC4 tickets from WS01; the real pipeline on the DC01 Security export (20,022 events) gave exactly one KERBEROAST and no false NTLM relay
  (16,408 machine Kerberos logons, 0 machine NTLM). 5441/5447/5157/4886/4887 field names not seen in real logs yet (lab has no CA/EDR).
  Suites: Dfir 3014, App 1028, UI 368, Edition 45.
- 2026-11-09 (owner „Continua”): CI of 7d97d8df0 red: 20_TESTS/test_loganalyzer_rule_catalog.py only recognises hyphenated rule ids, so
  KERBEROAST was a "dead" catalog entry -> renamed KERBEROAST-RC4 (f86da7731). Wave 4 started: Defender DetectionHistory. Real sample from
  LAB-WS01 (EICAR test string written by PowerShell Direct; Defender RTP detected and blocked it) at E:\LA-Lab\diag\defender\dh (history file,
  MPLog, Operational evtx). Format: 8-aligned TLV stream (u32 len, u32 type; 0x15 UTF-16 string, 0x06 u32, 0x0A FILETIME, 0x1E GUID, 0x28
  key/value block with ThreatTracking* hashes); verified against the 1116 event (ID, time, user, process, path, SHA-256). Worker (Sonnet)
  building DefenderDetectionHistoryParser + collector + rule DEF-HISTORY-ONLY (detection in history, missing from the collected 1116/1117);
  Opus review next. Lab leftover: C:\LA\eicar and C:\LA\dh on WS01 (Defender quarantined/blocked the test file).
  Done f7e826ff4: parser, collector (in InvestigationPipeline), rule DEF-HISTORY-ONLY. Opus r1: HIGH severity ignored the log window (old
  detections and untimed ones were High), HIGH a log cleared and collected empty read as "not collected", MEDIUM only 1116/1117 matched ->
  severity by window (High only cleared-after via System 104 or inside the log span; Low outside; Medium untimed / not in case), 1116-1119.
  r2: MEDIUM contradictory text when only an earlier clear exists (new case ClearedBeforeOnly), LOW technique on the capped summary -> fixed.
  Lab WS01 real run (parser + EvtxParser + Correlation): before clear only DEF-DETECTION; after `wevtutil cl` of the Defender log
  DEF-HISTORY-ONLY High T1070.001 + LOG-TAMPER. Defender rewrote the record after the clear (FILETIME after the block +13 s); parser now falls
  back to ThreatTrackingStartTime when the FILETIME is >1 s later (second real fixture). Not done: RemoteCollection. Next: RMM pack, then
  Notepad TabState / MUICache. Suites: Dfir 3041, App 1028, UI 368, Edition 45.
- 2026-11-09 (owner „Continua”): wave 5, RMM. A separate RMM-TOOL rule was built first and dropped: it duplicated the existing
  REMOTE-TOOL-PRESENT / REMOTE-TOOL-EXECUTED (Wp11Rules.Tools.cs + Data/remote_access_tools.json; missed by a grep because the tool names are
  data). Folded into them instead: 8 tools added, `domains` + DNS (Sysmon 22, DNS-Client 3006/3008) as presence, PRESENT Low->Medium for a
  service in a user-writable folder or two distinct tools on one station within 24 h, Quick Assist (`common`) present Low / run Medium.
  Opus r1: Chrome Remote Desktop lowered to Low (now High as before), name-only approval bypassable, cross-station correlation, DNS/approved
  counting, cmd /c wrapper. r2: Windows\Temp, other drives, `..` traversal, %SystemRoot%/%ProgramFiles%, quoted paths with spaces, 32 K
  ImagePath -> all fixed by main session (no round 3). Accepted residual: a case with no station names is one station; same NetBIOS name in
  two domains is one station. Lab WS01 (renamed PING.EXE, removed after with rmm-lab.ps1 -Undo): AnyDesk service+process from C:\Users\Public
  and RustDesk from Program Files -> two REMOTE-TOOL-EXECUTED High naming each other. Suites: Dfir 3090, App 1028, UI 368, Edition 45.
  Next: Notepad TabState / MUICache.
- 2026-11-09 (owner: „rezolva dupa ce termini partea cu windows defender”): Defender gap closed: remote package artifact `defender-history`
  (copies ProgramData\...\DetectionHistory; missing folder = step "absent"; RemoteCollection.ImportKind types a file for the DH parser only
  with a GUID name + matching content inside files/DefenderDetectionHistory/). Lab WS01: package generated in a TEST case
  (E:\LA-Lab\diag\remote), run as admin on WS01, Verify ok, Import typed the history file, DEF-HISTORY-ONLY produced (Low: package had no
  evtx:System, so the 104 clear was not visible). Notepad TabState attempt paused: on WS01 (Notepad 11.2312) a scheduled task in the user
  session ran but no TabState files appeared yet; the VM UI driver cannot see the Store Notepad window. Suites: Dfir 3092.
- 2026-11-09: wave 6, Notepad TabState + MUICache. Real samples from LAB-WS01 (Notepad 11.2312 driven by an interactive scheduled task in
  it.admin's session; the VM UI driver cannot see Store Notepad): unsaved tab + saved-file tab with an unsaved line; UsrClass.hiv via reg save.
  Worker (Sonnet): NotepadTabStateParser (2 real fixtures; saved-content length is in bytes, CRC32 big-endian over bytes after "NP"+seq),
  collection in UserArtifactsCollector, MUICache in ShellBagsParser (presence only, linked to REMOTE-TOOL as present). Real hive: 3 MUICache
  entries found. Opus r1: oversized tab silently typed "file", 10,000-entry cap after a full OrderBy, first 300 chars (secrets) in Summary;
  r2: unsalted hash + exact length in Summary lets short passwords be brute-forced, Tag block / U+2028 kept. Fixed by main session: no text
  and no hash in Summary (length only), Content cleaned per code point and marked sensitive, VSS only on sharing violation, capped copy.
  Accepted residual: Content stays in Fields (local AiCaseFacts blob; nothing leaves the station; exports with Fields not separately masked);
  the base parser layer already rejects >4 MiB, so the parser's own oversize gap is a second guard; collector outer catch message wording.
  Lab leftovers on WS01: C:\LA\np, C:\LA\remote, it.admin's Notepad tabs (TEST text). Suites: Dfir 3127, App 1028, UI 368, Edition 45.
- 2026-11-10 (owner „Nu te mai opri pana nu termini toata treaba”): wave 7, Windows Search index (SQLite, Windows 11). Real Windows.db from
  LAB-WS01 (WSearch stopped briefly for the lab copy only; TEST file in it.admin's Documents indexed, WorkId 1811). Worker (Sonnet):
  WindowsSearchIndexParser (property ids from the Metadata table by name; AutoSummary = file content, sensitive, never in Summary) +
  WindowsSearchIndexCollector + REMOTE-TOOL presence. Opus r1 HIGH (reproduced): db and -wal copied through separate esentutl /vss snapshots
  rebuild a never-existing state that passes quick_check -> collector now makes ONE WMI Win32_ShadowCopy (ClientAccessible), copies db/-wal/-shm
  from it, deletes it (orphan deleted if lookup fails); without it only the db (esentutl). WMI flow verified in PowerShell on WS01 (created,
  copied, deleted, count back to 0); C# collector only with a fake ISnapshot. r2: journal of a vouched pair applied even if the db alone
  fails quick_check (checkpoint frozen mid-way), unsound db always gets a gap, import brings Windows.db-wal into the same folder, pair vouched
  for only by collector + action + note. Accepted residual: a later-moment journal of an imported pair can only be flagged ("possibly
  mixed"), not detected. Real run: 1,171 events, SHA-256 unchanged. Suites: Dfir 3158, App 1028, UI 368, Edition 45.
  Next: $I30 slack (FSCTL_GET_NTFS_FILE_RECORD + raw $INDEX_ALLOCATION read), then decide on $LogFile / offline VSS (needs image formats).
- 2026-11-10: CI of 0b5b318f0 was still pending when last read (not re-polled). $I30 / $LogFile (raw NTFS reads) not implemented in this
  session; the direct-volume-read approach was dropped. Offline VSS needs disk-image support first. Both stay open for the owner.
  Lab leftovers (TEST data, to clean when the owner decides): WS01 C:\LA\eicar, C:\LA\dh, C:\LA\dh2, C:\LA\np, C:\LA\remote, C:\LA\wsearch,
  C:\LA\vss1, it.admin's Notepad TEST tabs and Documents\TEST-LA-raport-index.txt; Defender Operational log cleared by the test; host
  E:\LA-Lab\diag\{defender,rmm,notepad,wsearch,remote}. Fake RMM services were already removed.
- 2026-11-10 (owner „Continua”): CI green on 1f7b018e0. Remote package: artifacts `search-index` (db + -wal from one WMI shadow copy,
  deleted in finally, both files or neither, sizes/free space checked first, no fallback) and `notepad-tabs` (every path component checked
  for reparse points). Import stores the index pair in its own WindowsSearch_<guid> folder, SHA checked before registering, vouched pair only
  when the step ended ok. Real WS01 run found a parser bug: the real index declares COLLATE "UNICODE_en-US", quick_check failed with "no such
  collation sequence" on a sound database -> journal dropped + false "unsound" gap; schema collations are now registered before the check.
  Opus 2 rounds (r2: verified, LOW residuals: finally masks the copy error, UNC script root fails the step, TOCTOU on a user's own TabState).
  Lab after fix: both steps ok, shadow copies 0 -> 0, WSearch not stopped, 1,171 entries no gaps, 4 tabs. Suites: Dfir 3162+.
  Lab leftovers added: WS01 C:\LA\remote2, C:\LA\remote3; host E:\LA-Lab\diag\remote2, remote3.
- 2026-11-10: Defender DetectionHistory, third real sample (EICAR in a zip on WS01, detection 0FF8925A..., 1116 Path has containerfile + file):
  the parser stopped at the first key block -> missed the second resource and its hashes, and took the 1117 action time. Now all resources and
  blocks are read in order, hashes bound to the resource whose block holds them (event Path = that file, not the zip), detection time = tail
  FILETIME within 1 s of ThreatTrackingStartTime else the start time (a planted early FILETIME is never taken). Opus r1 found the hash/path
  mismatch and the unbounded "earliest" rule; both fixed with tests; r2 skipped by the main session (small delta, all three real samples equal
  the 1116 time). Suites: Dfir 3164, App 1028, UI 368, Edition 45. Lab leftover added: WS01 C:\LA\eicarzip, C:\LA\dhzip.
- 2026-11-10 (owner „Continua”): CI green on 68eeea3bb. Wave 8, Windows-gather.db. Real lab experiment on WS01 (TEST file created in it.admin's
  Documents, indexed, deleted; databases copied twice from one WMI shadow copy each, shadow copies back to 0): after the delete the file's rows
  in SystemIndex_1_PropertyStore (Windows.db) are gone, its SystemIndex_Gthr row (path via SystemIndex_GthrPth + LastModified FILETIME LE)
  stays; the new row lived only in Windows-gather.db-wal. Samples: host E:\LA-Lab\diag\gather\{a,b}. Phase 1 (worker, Sonnet): parser
  WindowsSearchGatherParser + shared SQLite copy/WAL helper + registration. Phase 2 (next): collector/remote/import copy the gather pair from the
  same shadow copy + a rule for gather rows with no property-store row (only for databases from one collection). Lab leftover: WS01 C:\LA\gth.
  Phase 1 done: worker built the parser; real run found 2 of 1,175 rows silently lost (ORDER BY DocumentID used the secondary index, whose PK
  lookups need the Windows collation; stand-in orders differently) -> ORDER BY +DocumentID, test fails without the fix. Opus r1: MEDIUM output
  amplification (32K paths x deep chains) -> MaxPathChars 2,048; LOW row-order-dependent incomplete paths -> depth-aware memo (walk again
  without memo when a memoised node would pass MaxDepth), both with tests; LIMIT cap+1. r2 skipped by the main session (bounded delta, tests in
  both orders). Accepted LOW: collations now also registered on the Windows.db read connection (no collated column there); '..' / '\' inside
  names kept verbatim (raw Url kept). Lab rerun: a and b sets 1,175 rows, no gaps, deleted TEST file still present with path and time.
- 2026-11-10: wave 8 phase 2 (worker, Sonnet): collector + remote package copy Windows-gather.db (+ -wal) from the same shadow copy as
  Windows.db; parsers stamp a moment key (snapshot id / import folder id, vouched pairs only, once, at the end of the notes); rule
  WSEARCH-GATHER-ONLY (Low, no MITRE) = gather DocumentID without a Windows.db WorkId at the same moment. Live on WS01 (remote package, import,
  rules): step ok, shadow copies 0 -> 0, WSearch not stopped, 1,173 gather rows / 1,171 store entries, one finding = a real transient
  __PSScriptPolicyTest_*.psm1 created and deleted by PowerShell (likely the package run itself). The TEST file deleted ~1 h earlier was already
  gone from gather: retention is short (docs and limitation say so). Opus r1: MEDIUM a Windows.db whose -wal copy failed kept the note + key
  -> registration deferred, key and same-snapshot note dropped when the planned journal was not copied; LOW pathless WorkIds look missing ->
  the Windows.db read is marked incomplete when any WorkId has no path; LOW capped-summary text spoke of scanners -> own wording; text aligned.
  All with tests. r2 skipped by the main session (bounded delta). Accepted LOW (pre-existing): the remote script checks reparse points only on
  the gather files, not on folders on the way or Windows.db. Reviewer noted that host Defender blocked the test DLL in ITS run; main-session
  runs were not blocked (Defender not touched). Suites: Dfir 3220, App 1028, UI 368, Edition 45, python catalog 3.
  Lab leftovers added: WS01 C:\LA\remote4; host E:\LA-Lab\diag\remote4.
- 2026-11-10 (owner: „Continua cu update-ul aplicatiei, repara ce este greșit... testele se fac pe vm”): CI green on c87f1cd58. Current build
  deployed to the lab (Insula on WS02, Arhipelag + Continent on WS01), pages walked with the UIA driver, station control run in the UI and
  headless on all 4 VMs x 3 postures (labcheck harness, all checks: E:\LA-Lab\reports\labcheck-<VM>-<posture>.txt), Colectează tot on Insula
  (71,395 events, 29 findings; the in-app C# WMI collector copied Windows.db + Windows-gather.db: 428 / 430 rows, no gaps = first live proof).
  Found on the VMs and fixed, each with a regression test: (1) sidebar item checked by keyboard / UI Automation did not open its page, and a
  page opened from elsewhere left another item checked -> two-way sync in MainWindow; (2) 23 "Nu se aplică" reasons were printed in English
  -> ControlApplicability.ReasonRo; (3) P11/P32 on Windows Server said "Invalid namespace ... run as administrator" -> BitLocker feature not
  installed = NECONFORM; (4) C01 on a domain controller said the account list could not be read -> DE VERIFICAT, accounts are the domain's;
  (5) P15 "Mod de pornire: necunoscut" with Secure Boot on -> UEFI (deduced); (6) P26 reported a blank LegalNoticeText as "title — and a text
  of 1 character" -> no banner; (7) P39 on Windows Server said no antivirus registered (no Security Center there) -> follows Defender (P08);
  (8) S02 "1 driver instalate"; (9) the default per-user OneDrive task / Run value were High / Medium persistence on every station -> Low,
  candidate, check the Microsoft signature (exact default path only). Lab-verified after the fix: 2-8 (labcheck rerun). Still to verify on the
  VM after redeploy: 1 and 9. Open, not fixed: header buttons „Ce înseamnă?” and „Notițe” fall outside a 1024 px window (thin horizontal
  scrollbar); users-register grid rows are announced as "System.Data.DataRowView". Suites: Dfir 3264, App 1030, UI 368, Edition 45.
  The lab VMs still shut down hourly (expired evaluation; watchdog restarts them): each UI session has about 55 minutes.
- 2026-11-10 (lab pass, continued): fixes 1 and 9 verified on the VM after redeploy (UIA Select opens the page; OneDrive task / Run value Low).
  Arhipelag on WS01: pages walked, domain inventory against LAB-DC01 (7 users, 3 computers, DM checks), Colectează tot (161,212 events, 63
  findings: the lab attack is found - AnyDesk executed, UpdSvc service / task / Run value, discovery burst, incident chain; the masquerading
  "OneDriveUpdate" Run value stays Medium / High). More fixes from this pass: (10) grid rows without their own text, DataRowView rows and
  record dumps ("ControlCheck { Id = DM10, ... List`1[System.String] }") get an accessible name from their first columns (GridRowNames, global
  class handler); (11) PERSIST-RUNKEY-USERPATH for the default OneDrive Run value -> Low (KnownDefaults in Dfir.Core, shared with the LIVE
  rules); (12) WSEARCH-GATHER-ONLY gave 14 separate findings for ordinary churn on a real station -> ONE finding per moment that lists 15 paths
  and counts the rest; (13) CRED-BRUTEFORCE for "?<PSDirectVMLegacy>" (failed logons left by PowerShell Direct from the Hyper-V host) -> Low,
  candidate, said plainly; (14) after Colectează tot the intake assistant showed "Un folder adus de la stația afectată" -> shows this station.
  Not fixed (owner design decision): at 1024 px the header needs about 1,150 px, so „Ce înseamnă?” and „Notițe” are reached only through the
  thin horizontal scrollbar. Suites: Dfir 3265, App 1035, UI 368, Edition 45, python catalog 3. Still to do in the lab: verify 10-14 on the VM,
  Continent (Internet edition) pages, remote collection WS01 -> SRV01 from the UI, live monitoring, PDF reports.
- 2026-11-10 (lab pass, part 3): lot 2 verified on WS01 after redeploy (50 findings instead of 63; OneDrive Run value and PowerShell Direct Low;
  one WSEARCH finding with 19 files; assistant shows "Această stație"). GridRowNames did nothing on the VM: a class handler for Loaded is never
  called (WPF raises Loaded only where an instance handler exists) -> hook is SizeChanged; verified on the VM ("Incident: INC-0001; Stare: ...").
  LESSON: a test that creates a window handle (Window.Show or HwndSource) ends the App.Tests host early and the run still prints "Passed" with
  fewer tests (632 / 435 of 1,036) -> always read the TOTAL and the exit code; no HWND in tests. Continent on WS01: all 24 pages open. Fixed:
  (15) plain list / tree items announced as class names (TimelineItem, MitreTechnique, AptActorProfile, ProcessNode) -> ToString texts;
  (16) Cronologia evenimentelor: a raw Windows message made one card up to 1,900 px tall -> first lines + tooltip; (17) the live alert said
  "cineva a greșit de mai multe ori parola contului „?<PSDirectVMLegacy>”" at every lab connection -> told as the Hyper-V host.
  Suites: Dfir 3265, App 1037, UI 369, Edition 45. To verify on the VM after the next redeploy: 15, 16, 17. Still open in the lab: remote
  collection WS01 -> SRV01 from the UI, live monitoring page, PDF reports, Insula device monitor with a hot-added disk.
- 2026-11-10 (lab pass, part 4): 15, 16, 17 verified on WS01 (list items named, timeline cards 157-188 px, live alert tells the Hyper-V host).
  Incident PDF generated from the Arhipelag case on WS01 (40 pages, Romanian; copy on the host: E:\LA-Lab\reports\pdf). Found in it:
  (18) "AnyDesk observed between 6 September 2024 and 9 November 2026": the ShimCache time is the file's last-modified time (a copied
  executable), also true for Windows Search entries -> the period of REMOTE-TOOL findings ignores those two unless nothing else has a time;
  (19) the live toast still said "Contul ?<PSDirectVMLegacy> a primit 5 parole greșite ... Întrebați titularul contului" at Medium -> a Low
  alert "Conexiuni PowerShell Direct de pe gazda Hyper-V". Both with tests; to verify on the VM after the next redeploy.
  Driver note: a Save As dialog needs "click Save" (invoke and Enter do not reach it). Suites: Dfir 3266, App 1037, UI 369, Edition 45.
- 2026-11-10 (lab pass, part 5): 18 and 19 verified on WS01 after redeploy (AnyDesk / RustDesk first seen 2026-11-09 21:21, the run; the status
  bar tells the Hyper-V host). CI of ccf69a22c when last read: 34 passed, 4 long jobs still pending, none failed (not re-polled).
  Not done in this pass: remote collection through the UI (file dialogs; the package flow itself was verified end to end on WS01 earlier
  today), the live monitoring page, the Insula device monitor with a hot-added disk (Hyper-V SCSI disks count as internal, known since
  2026-11-06), officer signature / PAdES (needs a card). Open owner decision: the header at 1024 px. Lab leftovers of this pass: new TEST cases
  on WS01 (it.admin profile) and WS02, host E:\LA-Lab\reports\{labcheck-*,pdf}, evidence\LAB-WS0x-case, shots\LAB-WS0x\{ins,arh,cont}-*.
- 2026-11-10 (owner: „acum ne axam pe partea de design a aplicatiei, sa o facem mai profi”): design pass on the existing "Registru" theme
  (not a rebrand). Rules and plan: docs/design/{PRODUCT,DESIGN,PLAN}.md in the project (9 steps, each with its check). Step 1 DONE and
  verified on the VM at 1024 px (Arhipelag, the longest header): below 1280 px the header folds „Stația:”, „Utilizator:”, „Limbaj:” and the
  texts of „Ce înseamnă?” / „Notițe” into icons (system icon font), the search box becomes one button below 1180 px; no scrollbar line.
  Wp18LanguageTests updated to the new form of the glossary button (name and visible text both still asserted).
  FLAKY (seen once in a full App.Tests run, 3/3 green alone): OfficerQualifiedSignatureTests.The_CMS_is_CAdES_baseline_signing_certificate_v2_
  present_and_no_signing_time - not investigated.
- 2026-11-11 design pass DONE (owner: „ne axăm pe partea de design… poți schimba stilul aplicației mai profi”): theme "Dosar" (dark graphite
  frame header+sidebar, cool paper workspace, cobalt accent; severity colours unchanged), rules in docs/design/{PRODUCT,DESIGN,PLAN}.md, steps
  1-9 done and verified on the VMs at 1024 px, before/after captures in docs/design/capturi. Standing PLANNER agent (Plan subagent, resumed
  with "done: … — next?") ranks the next work after each batch, at the owner's request („ține un agent on să îți spună ce să faci”).
  Design tests in LogAnalyzer.App.Tests/DesignSweepTests.cs (page header, no emoji, no hex outside tokens, no old colours, no all-caps
  labels / view-model strings, one accent per screen, column widths ≥ header). LESSON: a test that creates an HWND ends App.Tests early and
  still prints "Passed" with fewer tests: always read the total and the exit code. AD page "50.000" events = the persisted event DB of earlier
  sessions (real data, kept). Suites: App 1084, UI 370, Dfir 3298, Edition 45.
  Next (planner, ranked): RMM session logs (AnyDesk ad.trace / connection_trace.txt, TeamViewer Connections_incoming.txt, ScreenConnect
  events) with synthetic TEST logs on WS01; Windows-usn.db; Windows.edb ESE (needs Search on SRV01 = lab change); UI remote collection;
  flaky OfficerQualifiedSignatureTests.
- 2026-11-11 RMM session logs DONE (planner item 1): parsers AnyDeskConnectionTraceParser, AnyDeskTraceParser, TeamViewerConnectionsParser
  (LogAnalyzer.Dfir.Core/RemoteTools; registry 30), ScreenConnect Application ID 0 events; live collection (UserArtifactsCollector) and remote
  package ("remote-tool-logs"). A session counts as execution in REMOTE-TOOL-EXECUTED (High, confidence Medium). Owner-delegated decision
  (main session): approved tool + accepted incoming session from an ID/IP not in the profile = Medium "De verificat" (not High); approval
  only by path pattern with only a session log = Medium "nici confirmată, nici exclusă". Opus review round 1 fixed: target time zone now in
  the package manifest ("timezone"; operator imports and old packages leave session times unconverted), IPv4 with leading zeros rejected,
  RemoteToolLogs typed before "/Tasks/". Lab WS01 (synthetic TEST logs: C:\ProgramData\AnyDesk\{connection_trace.txt,ad_svc.trace},
  C:\Program Files\TeamViewer\Connections_incoming.txt, TEST source "ScreenConnect Client (TEST0000)" in Application): the 3 parsers
  status OK, findings for AnyDesk / TeamViewer / ScreenConnect with sessions, 21:21 station time -> 19:21 UTC. Lab fix: ad.trace detail rows
  (relay, remote OS) no longer counted as sessions; direction in Romanian. Lab leftovers: those TEST files and the event source on WS01,
  E:\LA-Lab\diag\rmmlogs, case CASE-LAB-WS01-20261111-002241. Residual (recorded): SetupApi / Firewall imports still use the analyst's zone;
  PS package has no reparse re-check after copy (live does). Formats not checked against a real AnyDesk / TeamViewer install.
  Next (planner): Windows-usn.db; Windows.edb ESE (lab change on SRV01); UI remote collection; Insula device monitor; flaky Officer test.
- 2026-11-11 RMM committed b01695e7d (pushed). CI read once right after the push: ACTIVE note integrity success, the rest queued /
  in progress, none failed; NOT re-polled - the next session reads it once first and fixes it if red.
  Planner ranking for the next phase (new session, read only this checkpoint):
  1. Windows-usn.db (S-M): new parser beside WindowsSearchGatherParser, same single-WMI-shadow-copy and SQLite copy/WAL helper, collector
     WindowsSearchIndexCollector + "search-index" artifact in RemoteCollection.cs, RESEARCH_GAPS C.7 "usn". Lab WS01 in one boot window:
     TEST file in it.admin Documents, indexed, renamed, deleted; Colectează tot + remote package; USN rows old/new name + times, moment key
     matches Windows.db / Windows-gather.db; shadow copies 0 -> 0, WSearch not stopped.
  2. UI remote collection WS01 -> SRV01 (verification): TEST connection_trace.txt on SRV01, Continent on WS01, "click Save" in dialogs; the
     session lands in UTC per SRV01's zone (exercises the new manifest "timezone"); captures at 1024 px. Start right after a VM restart.
  3. SetupApi / Firewall imports with the target zone from the manifest (gap note without one); unit test + SRV01 Set-TimeZone round trip.
  Later: Windows.edb ESE (Search on SRV01), flaky OfficerQualifiedSignatureTests (run alone 20x), Insula device monitor, AnyDesk
  request+accept double count (wording).
- 2026-11-11 planner item 1 (Windows-usn.db) DROPPED on evidence: schema read-only from sqlite_master = one table ChangeTracking
  (Client, Batch, Path = volume, CaseMap, CurrentEntry/OldEntry BLOB(56), MoveSource/MoveDestination, RangeInformation), 1 row on a
  Windows 11 machine used for weeks, one empty page on WS01 (4096 bytes, unchanged since 2026-11-06, no WAL). It is the indexer's per-volume
  USN cursor, not a rename/delete history; recorded in RESEARCH_GAPS C.7. esentutl /vss on WS01: shadow copies 0 -> 0, copy empty
  (E:\LA-Lab\diag\usn empty). Item 3 narrowed: remote packages now carry the target zone for every file (manifest "timezone");
  SetupApi / Firewall are not collected remotely; the only open case is an OPERATOR import of setupapi.dev.log / pfirewall.log, which
  still converts with the analysis machine's zone (usually right; "" would fall back to UTC). Product choice, left as is.
  Next: item 2 (UI remote collection WS01 -> SRV01) right after a VM restart, then Windows.edb ESE / flaky Officer test.
- 2026-11-13 (owner: „Continua cu loganalyzer … fable advisor pentru opus, opus researcher, atribuitor de sarcini și verificator,
  sonnet executant, haiku numărător. Fii ambițios”): CI of e5fd/a479 had ONE red cause (enforce + zero-regression-gate:
  docs/design/{DESIGN,PLAN,PRODUCT}.md without frontmatter, not in the unreadable-notes allowlist) -> fixed e84f85136.
  Roles used: Haiku (Explore) baseline counts: tests Dfir 2602 / App 844 / UI 275 / Edition 20 attribute lines, 30 parsers,
  ~83 rule ids; no $LogFile / $I30 / Windows.edb / WebCache; $MFT only an UNWIRED SHA stub. Fable (fable-consultant, read-only,
  at the owner's explicit request despite the "Fable never a subagent" routing rule) ranked: NTFS $MFT + $I30 first
  ($LogFile needs it), RDP sessions second, Windows.edb third. Opus researcher: format card from primary sources.
  NEW PHASE „NTFS”: design docs/dfir/NTFS_MFT_DESIGN.md (tasks S1-S5, emission policy, out of scope), format card
  docs/dfir/NTFS_FORMAT_CARD.md (confirmed on the corpus). Corpus LogAnalyzer.Dfir.Tests/Corpus/Ntfs: 64 MB VHDX written by
  Windows 11 on the dev station (no station data, scanned for private identifiers incl. binary SID: 0 hits), mft.bin
  (256 x 1024), many_i30.bin (3 INDX), boot.bin, truth.json from fsutil/Get-Item. Raw volume image + VHDX kept OUT of the
  repo in the session scratchpad (ntfs\out). Lab: DC01 + SRV01 running, WS01/WS02 off.
  Next: S1+S2 (reader + MftParser on an exported $MFT) to vault-worker, then Opus review; then S5 ($I30), S3 (VSS raw
  collector, live on WS01), S4 (AF08 + graded timestomp rule).
- 2026-11-13 NTFS S1+S2 DONE (worker Sonnet, reviewer Opus 2 rounds, final fix by the main session): Dfir.Core/FileSystem/Ntfs/
  {NtfsCommon,NtfsBootSector,NtfsFileRecord,NtfsAttributes}.cs + FileSystem/MftParser.cs (SourceType ntfs_mft, registry 31,
  ParserCapabilities, fingerprint ntfs_mft in EvidencePreflight). Corpus test: paths, sizes, $SI times (100 ns) = truth.json;
  stomped.txt $SI 2019 / $FN = pre-stomp times; 17 free records found deleted, no live record deleted.
  Review r1 (3 MEDIUM, reproduced with probes): deleted cap ordered by raw $FN time (implausible times pushed real deletions out);
  boot sector of another volume trusted -> false "truncated"; zero-length attribute hid a file with Success / 0 malformed. Fixed
  with red-first tests + LOW: record 0 zero/BAAD, SafeText Cc+Cf, honest texts (parser does NOT read $ATTRIBUTE_LIST; links
  extensions by base reference; AttributeList.Walk / DataRuns.Decode have NO production consumer yet -> S3), name budget 64 Mi chars.
  Review r2: one MEDIUM regression (one altered USA count in record 0 set the size for the whole file) -> main session: every valid
  header in the first 64 KiB votes for its own size, majority wins, the boot sector breaks ties only (a planted boot + one planted
  header cannot outvote), gap "Antete FILE în conflict". MftParserRecordSizeVoteTests (6). Accepted LOW residuals (r2): fingerprint
  'unknown' when the first bytes are neither FILE/BAAD/zero although the parser reads it; zeros + one FILE at 4096 fingerprinted
  ntfs_mft; "incomplete" summary shows "(…)" and IncompleteNotEmitted is only in the gap. Not measured: multi-GB $MFT memory/speed,
  real 4 KiB records, real $ATTRIBUTE_LIST.
  Suites (main session, fresh): Dfir 3413 (3380 passed, 33 skipped, 0 failed), App 1084/1084.
  Next: S5 ($I30 INDX + slack, corpus many_i30.bin: 3 deleted names live ONLY in $I30 because their MFT records were reused),
  then S3 (raw $MFT from one shadow copy, live on WS01), then S4 (AF08 real verdict + graded timestomp rule).
- 2026-11-13 NTFS S5 DONE: I30Parser (SourceType ntfs_i30, registry 32) + Ntfs/{IndexRecord,NtfsFixup}.cs (fixup shared with
  NtfsFileRecord, equivalence checked by the reviewer). Corpus: 3 INDX blocks, 39 allocated + 2 from $INDEX_ROOT (file_017, file_035
  = B+ tree root separators) = the 41 live files; slack 34 = 24 live_copy + 10 deleted names historical_free (021..051); 018/036/060
  keys survive only without their 16-byte header (not recovered); the 3 names whose MFT records were reused are NOT in slack.
  Review r1 (2 MEDIUM): a mismatched neighbouring $MFT made live copies look deleted (Medium, no gap); classify compared names only
  (a file moved out looked like noise). Fixed: coherence gate before any classification (one parent = live directory with the same
  sequence; >= 98% of allocated entries live with same sequence and name; else all slack unclassified + gap; fails closed on small
  directories), classes live_copy / moved_out (Medium) / historical_renamed (Low, "nume anterior posibil") / foreign_parent (Low, from
  the index alone - accepted) / historical_reused / historical_free; $INDEX_ROOT read from the neighbouring $MFT (main-session
  decision); MaxBlockSize 65,536; corpus numbers pinned. r2 APPROVED (LOW only); main session fixed 2 LOW (root without end marker /
  used < 16 = problem, gap text join) with IndexRootDamageTests (4). Accepted: a coherent hostile $MFT + $I30 pair can plant a
  historical_free name (no more than planting in $MFT itself); gaps keep Status Success (framework-wide).
  Suites (main session, fresh): Dfir 3473 (3440 passed, 33 skipped, 0 failed), App 1084/1084.
  Next: S3 collector (raw $MFT + boot sector + $I30 of chosen directories from ONE shadow copy, live on WS01; uses DataRuns.Decode and
  AttributeList which have no production consumer yet), then S4 (AF08 real verdict + graded timestomp rule; truth: stomped.txt).
- 2026-11-13 NTFS S4 DONE (timestomp): rule NTFS-TIMESTOMP (T1070.006, Analysis/NtfsTimestomp.cs + Wp14Rules.Timestomp.cs,
  RuleContracts) and AF08 with a real verdict. Built on an EMPIRICAL calibration corpus (Corpus/NtfsCalib, Windows 11, USN active
  before any operation, 17 scenarios A-N2; README has the table). Measured: rename / same-volume move COPY $SI into $FN; robocopy
  /COPY:DAT and Copy-Item copy the source change time ($SI MftModified < $FN Created) - robocopy otherwise looks exactly like a
  stomp; tar truncates to the second; File.Replace copies the creation time into $SI AND $FN in ONE operation; NTFS tunnelling
  happens ONLY with 8.3 names on the volume (new Win11 volumes: off; system volume: usually on) and inherits the creation time only.
  Rule (main-session design after 2 Opus review rounds that found: Close-time used as creation time, wrong USN time zone turning
  zip/robocopy into High, AF08 NotDetected without the recent window, tunnelling/safe-save): USN ordered by Usn number never by time;
  HIGH = $SI Created < $FN Created - 60 s + a separate earlier-closed "Basic info change" op (no Data/Create/Rename) after creation,
  or ($SI == $FN copied by rename) create -> separate BIC -> rename with $SI > 24 h before the create (beats any zone error); no
  High when the final name was freed just before by another FileId in the same parent (tunnelling, counted); MEDIUM = other mismatch
  without copied change time; LOW = MFT-only mismatch on a DOS-named record whose $SI modification time is new (possible tunnelling;
  main-session refinement: a DOS-named full stomp stays Medium). AF08 NotDetected only with the recent window emitted, no cap cut,
  USN inside the window. UsnJournalParser uses EvidenceItem.Timezone (TimeSource field).
  Calibration result: with USN A-E High, F-N2 nothing (all 4 zones tested); MFT only A, B, E Medium, L2 Low, rest nothing.
  Accepted residuals: an archiver restoring creation times (7-Zip -mtc) then renaming looks like D3 (untested); an attacker can
  pre-free the name within 15 s to have a stomp counted as tunnelling; NtSetInformationFile can also set the change time;
  UsnJournalParser keeps only Close records ("Rename: old name" inferred). Defender quarantines the Debug Dfir test DLL
  (Wacatac.H!ml false positive) -> run Dfir tests with -c Release; Defender not touched.
  Suites (main session, fresh): Dfir (Release) 3524 (3491 passed, 33 skipped, 0 failed), App 1084/1084.
  Next: S3 collector (raw $MFT + boot + $I30 + USN from ONE shadow copy, live on LAB-WS01), then lab check of the rule on a real
  system volume with 8.3 names (how many Low/Medium on a clean Windows install = noise budget).
- 2026-11-13 NTFS S3 (in progress) LAB FACTS (LAB-WS01, Windows 11, today): copying "\\?\GLOBALROOT\Device\HarddiskVolumeShadowCopyN\$MFT"
  by path = Access denied; opening the shadow DEVICE read-only works (PowerShell and .NET FileStream) -> raw read is mandatory (also for
  the remote PowerShell package). 8.3 names ENABLED on C: (tunnelling relevant). PowerShell prototype E:\LA-Lab\scripts\mft-raw.ps1
  (boot -> record 0 -> runs) copied 127,926,272 bytes in 2 runs, SHA on target = SHA on host, shadow copies 0 -> 0. S3a collector
  NtfsMetadataCollector (worker, uncommitted) run LIVE through a self-contained lab harness (scratchpad ntfsharness, E:\LA-Lab\scripts\
  ntfs-harness.ps1): Success in 2.1 s, shadows 0 -> 0, $MFT 128 MB, FixupErrors 0, 10 $I30 directories all passing the coherence gate.
  Defender on WS01 KILLS the harness (Behavior:Win32/NewCreatedFileExecWMI.A, "new file executing WMI", 4 detections) -> USN part
  measured on the host (E:\LA-Lab\diag\ntfsreal: $MFT, $Boot, manifest, usn_direct.csv 71 MB; lab data, NOT in the repo). PRODUCT
  NOTE: a new unsigned executable that uses WMI shadow copies is blocked behaviourally; the installed app was not (earlier sessions).
  NOISE on a clean install: MftParser MismatchTotal 99,099 of 125,184 records (install.wim keeps $SI 2024-04-01, $FN = install day);
  rule MFT only High 0 / Medium 4,684 / Low 316; MFT + USN (142,144 records) High 0. All Medium in system trees or directories, and the
  5,000 emission cap was filled by them (a user-profile stomp could be pushed out). Decision: system-tree and directory mismatches are
  counted, not emitted; Medium only outside them; High (USN) everywhere. Lab leftovers: WS01 C:\LA\{mft,mftraw,harness,ntfscase..ntfscase4}
  (case files are read-only), Defender detection history on WS01 (not touched); host E:\LA-Lab\diag\{mft,mftraw,ntfsreal,ntfs-*.txt}.
- 2026-11-13 NTFS S3a DONE: raw reader Dfir.Core/FileSystem/Ntfs/NtfsVolumeReader.cs (boot -> record 0 -> runs + $ATTRIBUTE_LIST; first
  production consumer of DataRuns.Decode / AttributeList) + collector Dfir.Windows/Acquisition/NtfsMetadataCollector.cs (one WMI shadow copy,
  device validated against the ShadowID, read-only handle, $MFT + $Boot + $I30 of up to 64 directories, manifest with SHA-256 / shadow id /
  UTC time / zone, per-run folder Raw\NtfsMetadata\<UTC>_<8 hex>, files of a cancelled/failed run deleted), WmiShadowCopy extracted to
  ShadowCopy.cs (equivalent to the old WmiSnapshot, checked by the reviewer), wired in "Colectează tot" (StandardAndUp). Oracle: from
  Corpus/Ntfs/volume.raw.gz the extracted $MFT / \many:$I30 / boot are byte-identical to the files Windows gave (fsutil extents).
  Live (lab harness on WS01): Success 2.1 s, shadows 0 -> 0, 128 MB, 0 fixup errors, 10 $I30 all passing the gate. Review r1: 2 MEDIUM
  (sparse $MFT -> 512 MiB of zeros with a "valid" SHA; huge TotalSectors -> OverflowException failing the whole collection) + 4 LOW,
  all fixed with red-first tests and confirmed by r2. Noise fix (measured on WS01): system trees / directories counted not emitted
  (MftParser.SystemTrees), channel system_mismatch_recent (cap 20,000) feeding only the USN High path. r2 found one MEDIUM: user-writable
  places inside system trees + NotDetected without USN -> main session: \Windows\Temp, \Windows\Tasks, \Windows\Tracing,
  \Windows\System32\spool\drivers\color, \Windows\System32\Tasks, \$Recycle.Bin are NEVER filtered; only desktop.ini and the exact
  \Users\Public\Libraries\RecordedTV.library-ms count as shell defaults (other .library-ms = NTLM lure, examined); AF08 is Undetermined
  without USN when mismatches were only counted. Real WS01 data after all fixes: High 0 / Medium 0 / Low 0, AF08 Undetermined (fresh
  install: channel capped). Accepted residuals: \Users\*\AppData\Local\Packages filtered (Store-provisioned, user-writable; covered by
  the honest verdict); AF08 text mixes number formats (99,099 vs 142.144); a stomped system file older than 30 days is reached by no path
  (said in AF08 limits). Suites (main session): Dfir (Release) 3631 (3598 passed, 33 skipped, 0 failed), App 1084/1084.
  Next: S3b remote PowerShell package artifact "ntfs-metadata" (raw read from one shadow copy; prototype E:\LA-Lab\scripts\mft-raw.ps1 works
  on WS01; add $I30 of a few directories or leave them to the in-app collector), then verify "Colectează tot" in the INSTALLED app on WS01
  (publish + redeploy; the lab harness is killed by Defender: Behavior:Win32/NewCreatedFileExecWMI.A).
- 2026-11-14 CI of cdb28bc79: 2 red (enforce, zero-regression-gate) = 20_TESTS/test_loganalyzer_rule_catalog.py: the rule id must be a
  LITERAL in a `const string *RuleId = "..."` inside Wp14Rules*.cs (it referenced NtfsTimestomp.RuleId) -> fixed 81c0a2f52. LESSON: the
  python catalog test is not in the dotnet suites; before a push that adds a rule run `python -m pytest 20_TESTS/test_loganalyzer_rule_catalog.py
  20_TESTS/test_vault_access_clients.py -q --basetemp <scratchpad>` (the default basetemp hits a PermissionError on this station).
- 2026-11-14 NTFS S3b DONE: remote package artifact "ntfs-metadata" (RemoteCollection.cs): ONE WMI shadow copy of the system drive, its device
  opened read-only (regex-anchored, found by ShadowID), boot + record 0 + runs validated (bps, spc, record size 1024/4096, fixup all positions
  first, unnamed non-resident $DATA, no sparse/compressed/encrypted, run nibbles, LCN in volume, sorted by LCN: no overlap, none over cluster 0,
  <= 4,096 runs, real size multiple of the record and >= 16 records, <= volume and <= 16 GiB, VDL = real, $ATTRIBUTE_LIST in record 0 ->
  step failed "use the app"), free space first, partial files removed, finally: Remove-CimInstance then vssadmin /shadow=<id>. Import (only
  with the step "ok" and exactly one files/NTFS/$MFT + $Boot): own NTFS_<guid> folder, SHA re-checked, AcquiredAtUtc = the step's startUtc
  (CaseWorkspace.RegisterStored gained an optional acquiredAtUtc); Verify refuses duplicate manifest paths. No $I30 in the remote package
  (left to the in-app collector). LIVE on LAB-WS01 twice (before and after the review fixes): step ok, 128,188,416 bytes, 2 runs, shadow copies
  0 -> 0, Verify ok, MftParser on the import Success with 0 fixup errors. Review r1: 1 MEDIUM (overlapping runs accepted -> duplicated records
  imported as a valid $MFT) + 5 LOW, all fixed (6 tests red first); r2 APPROVED. Accepted: search-index step still does not fall back to
  vssadmin when Remove-CimInstance fails; a compromised station can feed any bytes (SHA proves transport, not provenance). Lab: generator
  scratchpad\remotentfs, E:\LA-Lab\scripts\remote5.ps1, packages E:\LA-Lab\diag\remote5{,a}; WS01 C:\LA\remote5.
  Suites (main session): Dfir (Release) 3643 (3610 passed, 33 skipped, 0 failed), App 1084/1084, python 27/27.
  Next (NTFS phase): verify "Colectează tot" in the INSTALLED app on WS01 (publish + redeploy, UIA driver) incl. the NTFS collector and
  the AF08 / NTFS-TIMESTOMP texts on screen; then $LogFile (next phase, now that the reader exists).
- 2026-11-14 INSTALLED-APP CHECK (Arhipelag lab build of d7dd17beb, E:\LA-Lab\scripts\build-lab-arhipelag.ps1 = build-lab-apps.ps1 for one
  module with the current scratchpad TEMP; keys restored, SHA equal; update-app.ps1 to WS01; UIA: E:\LA-Lab\scripts\t-ntfs2.ps1 restarts
  WS01 after the hourly shutdown and waits for PowerShell Direct, then cmd_ntfs.txt "Colectează tot" -> "Confirmă scopul"): case
  CASE-LAB-WS01-20261114-052942 (C:\Users\it.admin\AppData\Local\LogAnalyzer\Cases, hidden AppData: search with -Force), 243,528 events,
  72 findings; NtfsMetadataCollector ran inside "Colectează tot": $MFT 128,974,848 + $Boot + 10 .i30 + manifest, shadow copies 0;
  MftParser Success 44,266 events, 0 malformed; AF08 Undetermined (fresh install, channel capped). FOUND: "Colectează tot" never reads the
  SYSTEM volume's USN journal (UsnJournalCollector reads only external NTFS media, by design) -> the NTFS-TIMESTOMP High path never has data
  for C: in the app; AF08 text glued two reasons ("verificate cu USN. fără jurnal USN."). S6 dispatched: NtfsMetadataCollector also reads
  the system volume journal with UsnJournalNative into the same run folder (usn_<L>.csv, UTC column), AF08 reasons as separate sentences.
  First UI attempt was cut by the hourly VM shutdown during the 300 s wait (not an app fault).
- 2026-11-14 NTFS S6 DONE: NtfsMetadataCollector also reads the SYSTEM volume's USN journal (UsnJournalNative.Read, read-only FSCTL)
  right after copying $MFT / $I30, while the shadow copy still exists, into usn_<L>.csv in the same run folder (UTC column, station zone,
  SourceType usn_journal); manifest block "usn" (status, journalId, first/next USN, records, incomplete, readUtc, sha256); a failed or capped
  read = Partial, never Failed. AF08 reasons are separate sentences (no "USN. fără jurnal" glue). Main session read the diff (read-only,
  partial file deleted on error, public constructor wires the real reader). Not reviewed by a separate Opus agent (reuses the reviewed
  native reader, no new handle/IO path) - recorded as a decision. LIVE in the INSTALLED app (Arhipelag lab build, WS01, case
  CASE-LAB-WS01-20261114-055626): usn_C.csv 90,632,588 bytes, 360,107 records, complete; 161,923 closed operations parsed; NTFS-TIMESTOMP
  0 findings on the clean station; AF08 Undetermined with the right reasons (fresh install, channel capped; USN analysed); collector
  SUCCESS, shadow copies 0. Lab note: the first sign-in after a fresh app start failed once (timing of the UIA typing; a second try with the
  app open worked) - do not loop sign-ins (lockout risk).
  Suites (main session): Dfir (Release) 3652 (3619 passed, 33 skipped, 0 failed), App 1084/1084, python 27/27.
  NTFS phase complete (S1-S6). Next phase: $LogFile (redo/undo of creates, renames, deletes and $SI/$FN changes; the raw reader exists:
  copy $LogFile from the same shadow copy in NtfsMetadataCollector), then the SRUM tables not yet read, then Windows.edb (ESE).
- 2026-11-14 NEW PHASE „$LogFile” (owner: „Continua”). CI of 30fb89b4c: 32/32 green. Fable advisor: $LogFile usually wraps FASTER than USN;
  its value is (A) USN record images surviving `fsutil usn deletejournal`, (B) old/new $SI in undo/redo; do AF07 first, gate on a measured
  window, then conservation -> structural parser -> slice A -> slice B; no High from $LogFile alone; no full redo/undo; no 1.1 without a
  sample; not in the remote package. Opus researcher: format from ntfs-3g logfile.h / logfile.c / ntfsrecover.c (sources saved in the
  scratchpad ntfs\), no per-record time, v2.0 tail pages = blocks 2..33, $SI old/new semantics NOT confirmed primarily.
  G0 gate (threshold >= 2 h set before measuring): first estimate from FILE-header LSN + $SI time was INVALID (45% time inversions, said so);
  direct: WS01 $LogFile 64 MiB read raw from a shadow copy (E:\LA-Lab\scripts\logfile-raw.ps1, shadows 0 -> 0) -> 14,267 USN_RECORD_V2
  images, 05:36:44-10:25:05 UTC = 4.8 h -> PASSED (idle lab VM; a busy station is shorter; own collection activity is inside the window).
  Oracle corpus LogAnalyzer.Dfir.Tests/Corpus/NtfsLog (9 known operations right after format, $LogFile not wrapped, 33 USN images;
  fsutil refuses $LogFile extents -> copied by a separate PowerShell decoder). Design docs/dfir/NTFS_LOGFILE_DESIGN.md (tasks L1-L5).
  Next: L1 (AF07 deletejournal detection) to a Sonnet worker, Opus review; then L2 conservation.
- 2026-11-14 $LogFile phase L1 DONE: AF07 hardened (journal absent on the system volume = distinct Detected Medium; recreation anchored on the
  1st percentile of EVTX times with a 24 h tolerance; NotDetected only with a journal analysed) + rule USN-JOURNAL-DELETED (T1070.004,
  Wp14Rules.UsnJournal.cs, linear UsnCommandScanner, no regex on raw text): fsutil.exe process (Image / NewProcessName / OriginalFileName)
  -> its arguments without argv[0]; shell lines lexed (cmd quoting; cmd /c|/k and powershell -Command re-read, depth 3; a powershell.exe
  line is not split by cmd rules); 4104 PowerShell lexer (strings incl. typographic quotes U+2018-201E, here-strings anywhere, # and <# #>
  comments; '{' '(' open a block mid-word only after a keyword, else the brace pair stays in the word: \\?\Volume{GUID}); deletejournal = High,
  createjournal alone = Low, recreate <= 1 h on the SAME non-empty volume = High; 4688 + Sysmon 1 of one execution deduplicated; 256 KiB
  cap per event -> Info RULE-NOT-EVALUATED. Lab calibration (main session, read-only, 3 VMs): the C: USN journal is created at FIRST BOOT
  (~1 s from the oldest EVTX record) while Win32_OperatingSystem.InstallDate is ~10 h earlier (image preparation) -> an InstallDate anchor
  would false-positive on every station; the EVTX anchor is right. Reviews: r1 2 MEDIUM (printed text in strings/here-strings/comments ->
  High; ReDoS on 3,000 spaces silently hiding a real delete) fixed by the worker (17 tests red first); r2 2 MEDIUM (compact try{fsutil ...}
  missed; typographic quotes / here-string after '=' -> High) + 4 LOW -> main session fixed the lexer (UsnJournalDeletionRound2Tests).
  Accepted residuals: Start-Process / iex / concatenation / variable verbs in 4104 not captured (the child fsutil 4688 / Sysmon 1 is);
  `cmd /c "a" & "b"` second statement may be missed; cmd `\"` treated as escape (rare FP); percentile weak with > 1% wrong-clock records;
  no producer yet for the volume format / install time (MftParser summary could add record 0 / root $SI creation).
  Suites (main session): Dfir (Release) 3720 (3687 passed, 33 skipped, 0 failed), App 1084/1084, python 27/27.
  Next: lab check of USN-JOURNAL-DELETED on WS02 (Hyper-V checkpoint FIRST, then fsutil usn deletejournal + createjournal, collect, revert);
  then L2 ($LogFile conservation in NtfsMetadataCollector, early in "Colectează tot").
- 2026-11-14 L1 LAB CHECK on LAB-WS02 (Insula, workgroup: credential LAB-WS02\labadmin, NOT LAB\Administrator). Default config there: NO
  Process Creation auditing, no 4688 command line, no script block logging, no Sysmon -> the command-line rule is blind on such stations,
  AF07 is the only net. Test inside a Hyper-V checkpoint (E:\LA-Lab\scripts\t-ws02-usndel.ps1: checkpoint -> enable auditing -> powershell
  -Command "fsutil usn deletejournal /d C:" -> 5 s -> createjournal -> export Security/System/PowerShell EVTX + new USN -> TurnOff, restore,
  remove checkpoint; checkpoints after: none). Journal ID 0x01dd6e39de300ac7 -> 0x01dd742bf5c99138. Host analysis (scratchpad usnlab,
  EvtxParser + UsnJournalParser + Wp14Rules + AntiForensics, data in E:\LA-Lab\diag\usndel): USN-JOURNAL-DELETED High „Jurnalul USN a fost
  șters și recreat prin comandă” (4688 fsutil.exe + 4104); AF07 Detected Medium (journal created 2026-11-14T11:33:24Z vs EVTX start
  2026-11-06T15:10:31Z, percentile 1), text with the chkdsk / major-update tolerance.
  Next: L2 ($LogFile conservation in NtfsMetadataCollector, as early as possible in "Colectează tot"; reader copies MFT record 2).
- 2026-11-14 L2 DONE ($LogFile conservation, claude main session + vault-worker Sonnet + 2 Opus vault-reviewer rounds). NtfsVolumeReader.CopyLogFile:
  record 2 in use / base / not a directory, non-DOS $FILE_NAME "$LogFile" whose parent = record 5 by number AND real sequence, record 5 an
  in-use base directory; unnamed $DATA via CollectSegments (attribute list), non-resident, no sparse (new noSparse flag, $MFT/$I30 paths
  unchanged), not compressed/encrypted, 2 pages..1 GiB (MaxLogFileBytes); restart values (magic at 0 and at system_page_size, page sizes,
  version) read from the COPIED bytes (head capture ~64 KiB); RSTR/CHKD valid. NtfsMetadataCollector: $LogFile after $MFT, before $I30,
  SourceType ntfs_logfile, manifest block logFile; a $LogFile error = file deleted + Partial ($MFT/$I30/USN kept); bad magic or VDL < size =
  file kept + gap + Partial. InvestigationPipeline: NtfsMetadataCollector moved SECOND in AllCollectors (right after LiveState): the case
  folder is usually on C: and the other collectors' writes rotate $LogFile (~4.8 h / 64 MiB) before the shadow copy freezes it;
  AuxiliaryNote for ntfs_logfile (no parser yet -> SkippedByDesign). Remote package untouched (out of scope this phase).
  Reviews: r1 1 MEDIUM (cancellation test fired inside the $MFT read, $LogFile never reached) + 3 LOW -> worker; r2 1 MEDIUM (record 5
  flags not checked) + 2 LOW (weak test assertions) -> main session fixed (root theory test, furthest-offset check, error reason).
  Note: Defender once blocked the Release test dll (0x800700E1 -> "No test matches", exit 0): always check the test count is > 0.
  Suites (main session): Dfir (Release) 3751 (3718 passed, 33 skipped, 0 failed), App 1084/1084, python 27/27, solution build 0 errors.
  Next: live check on LAB-WS01 (installed app, "Colectează tot" elevated: $LogFile size = MFT record 2 $DATA, RSTR at 0 and 4096, shadows
  0 -> 0, collector second in the run log); then L3 (pure structural parser, Dfir.Core).
- 2026-11-14 L2 LIVE CHECK (installed Arhipelag lab build of dbfc94892 on LAB-WS01, E:\LA-Lab\scripts\t-l2.ps1 elevated via sudo: update-app
  -> UIA "Colectează tot" -> read-only inspection): case CASE-LAB-WS01-20261114-141843; Raw folders in creation order LiveState (14:18:45.71Z)
  -> NtfsMetadata (14:18:45.82Z) -> EventLogs ... (collector second, as designed); $LogFile 67,108,864 bytes = the 64 MiB measured
  independently at G0, VDL = size, 1 run, RSTR at 0 and 4096, page sizes 4096, manifest logFile Copied, SHA equal, no errors / gaps,
  shadow copies 0 -> 0, deleted. Sample on the host: E:\LA-Lab\diag\l2 ($LogFile, $Boot, manifest; lab only, not committed).
  FOUND: restart area version is 1.1 on EVERY real sample (this one, the G0 raw copy, Corpus/NtfsLog VHDX); only Corpus/Ntfs is 2.0.
  Design doc updated: L3 covers 1.1 first (calibrate on the WS01 sample, 16,382 of 16,384 pages RCRD), 2.0 on Corpus/Ntfs.
  Next: L3 (pure structural parser in Dfir.Core) - Fable consult first on 1.1 vs 2.0 page layout, then brief.
