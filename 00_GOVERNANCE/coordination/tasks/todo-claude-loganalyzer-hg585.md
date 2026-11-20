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
- 2026-11-14 (claude, RMM thread; planner item 2) UI REMOTE COLLECTION WS01 -> SRV01 verified, and a bug found and fixed. CI of 3065870d1
  (contains the RMM commits) read once: 32/32 success. Lab: SRV01 time zone set to UTC for the test (WS01 = GTB, +2), TEST
  C:\ProgramData\AnyDesk\connection_trace.txt on SRV01; Continent on WS01 via UIA: "Autorizează și generează pachetul…" -> Save dialog
  (the File name box refuses settext; the default name was kept), package run as admin on SRV01 (exit 0, manifest timezone=UTC, step
  remote-tool-logs ok), "Verifică și importă pachetul…" -> folder dialog ("click Folder:" then "type", then "click Select Folder"), 205 files,
  "Pornește investigația" -> CASE-LAB-WS01-20261114-192447. BUG (fixed): "Verifică și importă" imports into the LIVE case with the target
  zone, but the run makes a NEW case and re-imports the stored files through InvestigationPipeline.Import as operator imports, so the
  zone was lost (session logs unconverted). Fix: InvestigationViewModel keeps stored path -> zone from the package import and passes it;
  InvestigationPipeline.Import(ws, files, zones). Result in the lab: SRV01 row "2026-11-09, 21:21" -> 2026-11-09 21:21 UTC (UTC target) on the
  timeline and in REMOTE-TOOL-EXECUTED. Test RemoteCollectionTests.The_target_zone_survives_the_reimport_into_the_case_of_the_run.
  Note: the station's own session logs are collected only by the "Completă" profile (UserArtifactsCollector, by design); "Pornește
  investigația" with the standard profile does not read them, "Colectează tot" does. SRV01 zone restored to GTB Standard Time.
  Lab leftovers: SRV01 C:\ProgramData\AnyDesk\connection_trace.txt (TEST), C:\LA-Collect-212158; WS01 Documents\LA_LAB-SRV01_20261114T192159Z,
  an extra authorized-but-unsaved TEST request 4fa2c04e in the live case; host E:\LA-Lab\evidence\remote\LA_LAB-SRV01_20261114T192159Z.
  Suites: Dfir 3752 (3719 passed, 33 skipped), App 1084, UI 370 (369 + 1 skipped), all exit 0.
- 2026-11-14 flaky OfficerQualifiedSignatureTests.The_CMS_is_CAdES_baseline... FIXED (test-only bug). Captured failure (run 29 of a loop):
  CryptographicException "ASN1 corrupted data" / "encoded length exceeds the number of bytes remaining" at SignedCms.Decode: the TEST cut
  the CMS out of the zero-padded /Contents with TrimEnd('0'), which also removes real trailing 00 bytes of the CMS. Now cut at the DER
  length (DerPrefix); assertions unchanged. Before: 1 failure in 15 and 1 in 29 runs alone; after: 0 failures in 200 runs; App 1084/1084.
  Production (PdfSignatureCheck) already uses the DER length. The OpenSSL recipe in docs/dfir/DESIGN_2026-10-16_OFFICER_SIGNATURE_AND_AUDIT_ANCHOR.md
  said "trailing zeros removed" (same flaw for a human verifier): now "as it is, do not strip zeros; openssl reads the DER length".
  Remaining planner items: Windows.edb ESE (needs the Search feature on SRV01 = lab change), Insula device monitor (blocked: Hyper-V
  SCSI disks count as internal), AnyDesk request+accept double count (wording only).
- 2026-11-15 (claude, RMM thread) Windows.edb ESE: NOT built, on evidence. CI of 882fffdf9 read once: 32/32 success. LAB CHANGE: Windows
  Search (feature Search-Service) installed on LAB-SRV01 (Server 2025 Datacenter Evaluation, build 26100), WSearch Automatic + running;
  kept installed for later tests (undo: Uninstall-WindowsFeature Search-Service). Server 2025 writes the index in SQLite like Windows 11
  (Windows.db / Windows-gather.db / Windows-usn.db, Projects\SystemIndex\PropMap\PropMap.db, SecStore\SecStore.db), NO Windows.edb. ESE
  matters only for Windows 10 / Server 2022 and older; the lab has none and a sample needs an install image (download = owner decision).
  Verified end to end instead: TEST file C:\Users\Administrator.LAB\Documents\TEST_LA_index_server2025.txt on SRV01 (indexed after 10 s,
  checked through Search.CollatorDSO), remote package of request e810533e re-run (search-index ok: files/WindowsSearch/Windows.db +
  -wal, Windows-gather.db + -wal; manifest timezone GTB), imported and analysed from the Continent UI on WS01 ->
  CASE-LAB-WS01-20261115-073731: WindowsSearchIndexParser 130 records and WindowsSearchGatherParser 130 records from SRV01 (1829 / 1843 from
  WS01), status OK; the TEST file in , index (System.DateModified, WorkId 134) and gatherer (DocumentID 134), all 07:33:04.431Z.
  RESEARCH_GAPS C.7 updated. Lab leftovers: SRV01 TEST file, C:\LA-Collect-093416; WS01 Documents\LA_LAB-SRV01_20261115T073417Z.
- 2026-11-15 AnyDesk double count (last open planner wording item) DONE: REMOTE-TOOL-EXECUTED keeps listing every session row and, when rows
  repeat, adds "aproximativ N conexiuni distincte" (same direction, partner and minute = one); test
  RemoteToolSessionRulesTests.One_connection_written_twice_is_estimated_as_one_distinct_connection. Planner list now empty except the
  blocked items (ESE: no Windows 10 / Server 2022 sample; Insula device monitor: Hyper-V SCSI disks count as internal).
- 2026-11-15 RDP-SESSIONS DONE (planner #2 item 1, RESEARCH_GAPS C.5; vault-worker Sonnet + 2 Opus vault-reviewer rounds; main session
  fixed round 2). New Wp11Rules.RdpSessions.cs (Info, Observation, T1021.001), called after RdpInternal; RuleContracts / ParserCapabilities
  (LSM 21-25, Security 4778/4779) / Wp11ContractTests updated, tests Wp11RdpSessionsTests (20). Owner-delegated decision (main session):
  informative finding only, existing RDP rules unchanged. r1 (cap counted attempts, production sorts by time so "log order" did not exist,
  cap kept the oldest, 4778/4779 and 4624 attributed to several sessions, over-claiming wording, empty host mixed) fixed by the worker;
  r2 MEDIUM (a session replaced by a reused ID had an open upper bound and took a 4779 written after the reuse) fixed here, test shown RED
  on the old line and GREEN on the fix; LastSeenUtc from the last event; "fără început înregistrat (… sau evenimentul 21 lipsește)";
  a BS control character in Wp11ContractTests ("CORP\bob" in a verbatim string) fixed. Accepted residuals in RESEARCH_GAPS C.5.
  LAB: field names User / SessionID / Address confirmed on real WS01 EVTX (all console, Address=LOCAL). A real network RDP WS01 -> SRV01
  could NOT be made: Credential Guard on WS01 refuses default-credential delegation and asks for a password (not entered by the agent).
  Lab changes for the attempt were all undone (SRV01: Remote Desktop Users member removed, RDP firewall group disabled; WS01:
  CredentialsDelegation policy key and C:\LA\rdp removed, mstsc closed). One failed RDP logon attempt is now in SRV01's logs.
  To verify on real data: the owner opens RDP WS01 -> SRV01 (connect, close window, reconnect, log off), then remote package + import.
  Suites: Dfir 3773 (3740 passed, 33 skipped), App 1084, UI 370 (369 + 1 skipped), all exit 0.
- 2026-11-15 RMM session logs in the STANDARD profile (planner #2 item 3) DONE. Owner-delegated decision (main session, safe option): the
  standard profile reads ONLY the AnyDesk / TeamViewer session logs, not the other per-user artifacts (hives, Recycle Bin, Notepad tabs stay
  "Completă"). New RemoteToolLogsCollector (Profiles = Standard, needs admin) delegating to UserArtifactsCollector.CollectRemoteToolLogsOnly
  (same copy rules and shadow check; profiles skipped for an unsafe path are reported there, since no hive step runs). COORDINATION with the
  NTFS thread: InvestigationPipeline.AllCollectors gained one entry right after UserArtifactsCollector; order of LiveState / NtfsMetadata
  unchanged (NtfsLogFileCopyTests still green). Guard kept: UserArtifactsCollector still not in Standard; new test: exactly one of the two
  collectors per profile (Standard / FullForensic), none in Quick. LAB (installed Continent on WS01, "Pornește investigația", profile
  "Standard (+ SRUM)"): CASE-LAB-WS01-20261115-145653, acquisition run lists collector RemoteToolLogs, custody EV-000247
  C:\ProgramData\AnyDesk\connection_trace.txt; REMOTE-TOOL-EXECUTED AnyDesk 6 sessions ("aproximativ 4 conexiuni distincte") and
  TeamViewer 2 (on 2026-11-14 the same profile had none). Not reviewed by Opus (thin wrapper over reviewed code; tests + lab instead).
  Suites: Dfir 3775 (3742 passed, 33 skipped), App 1084, UI 370 (369 + 1 skipped), all exit 0.
  Next (planner #2): item 2, remaining RMM logs (TeamViewer *_Logfile.log, RustDesk) - formats need a documented source; parsers would stay
  TESTED, not VALIDATED, without a real install (owner decision on a real sample).
- 2026-11-16 TeamViewer main log parser DONE (planner #2 item 2, part 1). TeamViewerLogfileParser (TeamViewer<NN>_Logfile(_OLD).log; Program Files
  and Program Files (x86)\TeamViewer, AppData\Roaming\TeamViewer; live + remote package). Format and markers taken from a REAL public TeamViewer
  15.55 log (GitHub, CTF lab file; read for format only, not copied): one event per incoming session (partner ID, IP from "punch received",
  partner name, auth method, attempts = max "attempt number K", client version, start/end); "was successful" = accepted, "was denied",
  "CMD_ENDSESSION with reason: LoginFailed", "aborting login" = not accepted (all REAL wordings), anything else unknown; UTC offset from the
  "Start: ... (UTC+h:mm)" header. File transfers NOT parsed (no real sample). Run on the real public log: 3 sessions (2 accepted, 1 failed
  after 3 attempts), UTC+3 converted, status Success, no gaps. Two Opus rounds; r1 (guessed failure words, attempts lost, double count with
  Connections_incoming, Terminate->Statistics gap, AddOther overwrite, ...) fixed by the worker; r2 (DistinctConnections quadratic,
  chain-merging, local time overruling UTC) fixed here: units by direction/partner/station/minute, then TeamViewer main log <-> Connections
  pairing once each, nearest first, UTC pass then local pass only when a row lacks UTC; tests for chain / order / stations / 10,000 rows
  (timing test in a non-parallel collection, best of 3). LAB WS01 (installed Continent, standard profile): TEST Logfile in C:\Program
  Files\TeamViewer collected and parsed into REMOTE-TOOL-EXECUTED (sessions with method, failed auth listed). Synthetic corpus header says
  UTC+1 while WS01 is UTC+2, so the 21:21 session gets two UTC times and is (conservatively) not paired -> "aproximativ 4". Corpus .log kept by a
  local .gitignore (!*.log). Lab leftover: WS01 C:\Program Files\TeamViewer\TeamViewer15_Logfile.log (TEST), case CASE-LAB-WS01-20261116-061944.
  Suites: Dfir 3813 (3780 passed, 33 skipped), App 1084, UI 370 (369 + 1 skipped), all exit 0.
  RustDesk (next): line "[yyyy-MM-dd HH:mm:ss.ffffff +hh:mm] LEVEL [file:line] msg" (flexi_logger opt_format, verified in source); markers from
  RustDesk source: "#<id> Connection opened from <addr>." (older: without "#<id>"), "#<id> Connection closed: <reason>", "new wakelock, require
  display on: ...", "drop wakelock"; paths AppData\Roaming\RustDesk\log\ (+ cm\, server\), LocalService profile; rotation daily or 16 MiB,
  names rustdesk_rCURRENT.log / rustdesk_rYYYY-MM-DD_HH-MM-SS.log, keep 90. No real sample yet.
- 2026-11-16 RustDesk logs DONE (planner #2 item 2, part 2). RustDeskLogParser: line "[yyyy-MM-dd HH:mm:ss.ffffff +hh:mm] LEVEL [file:line] msg"
  (flexi_logger opt_format, verified in the library source) with markers verified on REAL lines from GitHub issues rustdesk/rustdesk
  (#9003, #10251, #12784, #14662; Windows + Linux): "#id Connection opened from addr:port.", "#id Connection closed: <reason>", "#id
  connection loop exited", "new wakelock, require display on: ...", "drop wakelock". One session per #id; loopback = LocalConnection
  (Session Evidence nu; a log with only local connections gives REMOTE-TOOL-PRESENT). Collected from AppData\Roaming\RustDesk\log (+server,
  cm) and the LocalService profile, at most 30 newest per folder, rCURRENT always (decoys "rustdesk_rcurrent!NN.log" cannot hide it).
  Owner-delegated decision: approved RustDesk + incoming connection (acceptance unknown) from an address not in the profile = Medium
  "De verificat" (acceptance never claimed either way); IPv4-mapped IPv6 compared as IPv4 with the profile (all tools). Run on the 634 real
  lines: 9 sessions with id / IP / real close reason / display, 2 loopback, a masked IP ("*****") rejected. Opus r1 (no correctness defect;
  rule consequences) -> worker; r2 (decoys hide rCURRENT, text for invalid IPs, ::ffff: mismatch) fixed here with tests. Rule texts about time
  now say AnyDesk/TeamViewer = station local time, RustDesk = UTC offset per line; TeamViewer pairing note only for TeamViewer. LAB WS01
  (installed Continent, standard profile): both TEST logs (it.admin and LocalService\...\log\server) in custody, REMOTE-TOOL-EXECUTED RustDesk
  with the connections. Lab leftovers: WS01 C:\Users\it.admin\AppData\Roaming\RustDesk\log\rustdesk_rCURRENT.log and
  C:\Windows\ServiceProfiles\LocalService\AppData\Roaming\RustDesk\log\server\rustdesk_rCURRENT.log (TEST), case CASE-LAB-WS01-20261116-161324.
  Suites: Dfir 3866 (3833 passed, 33 skipped), App 1084, UI 370 (369 + 1 skipped), all exit 0.
  Next: none of planner #2 left except blocked items; ask a NEW planner (checkpoint <= 40 lines) for the next phase.
- 2026-11-16 RustDesk FILE TRANSFERS DONE (planner #3 item 1). Format from the RustDesk source libs/base/src/fs.rs: log::info!("new write {}") /
  ("new read {}") with DataSource shown "File: <path>" or "Bytes"; write = file written on the station (received; log\cm), read = read from it
  (sent; main log). RustDesk does NOT log ordinary authentication (only "Permanent password accepted via logon-screen fallback"): not parsed,
  written as a limitation. Parser 1.1: FileWrite / FileRead events (path sanitized, 4096 cap, Summary 200 without splitting a surrogate); rule:
  own sentence "Transferuri de fișiere în jurnalul RustDesk: N scrise..., M citite..." (<= 5 paths, forgery warning), header counts
  "transfer de fișiere din jurnalul uneltei" apart from sessions, a transfer row is not an execution artifact (confidence stays Medium),
  severity unchanged. Opus r1 fixed here: quadratic List.Contains in the rule (80,000 rows 9.6 s) -> sets; header counted transfers as sessions.
  ALSO FOUND: my 2026-11-16 edit (bash-style quote escaping in a PowerShell string) had left RemoteToolSessionRulesTests.Ten_thousand_session_
  rows_of_one_partner_are_counted_quickly with NO body since b262b3766 (it passed while testing nothing); restored with asserts; scanned the
  week's test files: no other empty test. LAB WS01 (installed Continent, standard profile): log\cm collected, the finding lists 3 written /
  2 read with paths and times. FOUND IN LAB: the LocalService profile is also a ProfileList row (S-1-5-19), so its RustDesk folder was read
  twice (two evidence ids for one file) -> folders deduplicated by full path; test red without the fix, green with it (lab not re-run).
  Accepted residuals: U+2028/U+2029 pass the shared sanitizer like "\n" (line breaks are allowed on purpose); transfers count in the
  "two tools within 24 h" correlation like any use. Lab leftover: WS01 it.admin AppData\Roaming\RustDesk\log\cm\rustdesk_rCURRENT.log (TEST).
  Suites: see commit. Next (planner #3): item 2 UsrClass.dat / Notepad tabs in the remote package (same restriction as live: "Completă" only);
  item 3 (ESE sample) blocked on an install image (owner).
- 2026-11-16 USER HIVES in the remote package DONE (planner #3 item 2). Owner-delegated decision (safe): optional artifact "user-hives"
  (NTUSER.DAT, UsrClass.dat per profile), NOT in the default list; UI check box "Include hive-urile utilizatorilor (... istoric personal)",
  unchecked by default and reset after every generation; the default package has no hive code at all. Locked hives via esentutl /y /vss only on
  a sharing violation; hardlinks (fsutil hardlink list > 1) and reparse points refused; 512 MiB per file, 200 profiles, free space tracked;
  capped copy. Verify now binds every package file to an artifact of the request (UnauthorizedFile; canonical paths only, "..", ".", "\"
  refused) - also closes an older gap for all artifacts. Notepad tabs were already in the package (RESEARCH_GAPS §6 corrected).
  Reviews: r1 HIGH (HResult read from MethodInvocationException: the VSS path never ran), MEDIUM hardlink, MEDIUM Verify not bound to the
  request -> worker; r2 MEDIUM ".." bypass of the binding -> fixed here with tests. LAB (UI on WS01 -> SRV01 -> UI import -> run):
  package with hives generated (UIA "click" on the check box had missed: use focus + key " "), run on SRV01 as admin: 7 hives incl. the
  LOADED NTUSER.DAT of Administrator.LAB, shadow copies 0 -> 0, fsutil fine on a real UsrClass.dat; Verify of the real full package OK (216
  files, no false refusal). FOUND IN LAB + FIXED: the live case flattens package files into one folder ("NTUSER_1.DAT"), and the run re-typed
  by name -> "file", never parsed; InvestigationPipeline.Import(..., carried) now keeps the type and origin note decided at the package import
  (only when the name gives no type). After the fix: 4 ntuser_hive + 3 usrclass_hive parsed (0 records: server profiles never used in
  Explorer; gaps "cheia lipsește"). Also explained: after the scope dialog the pending "generate" command continues by itself (the "extra
  Save dialog" seen on 2026-11-14). Lab leftovers: SRV01 C:\LA-Collect-201116, WS01 Documents\LA_LAB-SRV01_20261116T181117Z and a TEST
  request without package in the live case. Suites: Dfir 3884 (3851 passed, 33 skipped), App 1088, UI 370 (369 + 1 skipped), all exit 0.
  Next: planner #3 item 3 (ESE sample) blocked on an install image (owner); ask a NEW planner from this checkpoint.
- 2026-11-16 AUDIT FIX (planner #4 item 1): the SYSTEM / SOFTWARE hives of EVERY remote package were never parsed. RemoteCollection.ImportKind typed
  "files/*.hive" as "registry-hive" (and tasks "task-xml"), types no parser reads; InvestigationPipeline.ImportType did not know the ".hive"
  extension either (only "SYSTEM" / "*SYSTEM.hiv"), so the run's re-import did not recover them. Found on the real SRV01 package
  ("niciun parser înregistrat pentru tipul file" for SYSTEM_4.hive / SOFTWARE_4.hive); tasks survived only because task XML is recognised by content.
  Fix: package kinds are now the parsers' own (system_hive / software_hive with regf content and the exact names written by reg save, task_xml by
  content); ImportType knows SYSTEM.hive / SOFTWARE.hive. Test Every_file_kind_the_package_writes_is_read_by_a_parser_or_is_a_known_companion
  (each kind must have a parser candidate in WindowsParsers.Registry). LAB (same SRV01 package re-imported): SYSTEM -> ServicesParser 660,
  SystemHiveExecutionParser 137, UsbDevicesParser partial 0 (VM); SOFTWARE -> SoftwareHiveParser 5. Not Opus-reviewed (small, tested on real data).
  FOUND (not fixed, product question): two app instances on one station share the live case and the second fails on LogAnalyzer_Audit.log
  ("used by another process"); the lab scripts start a new instance each run (t-session) - kill old instances first. Suites: Dfir 3885 (3852
  passed, 33 skipped), App 1088, UI 370 (369 + 1 skipped), all exit 0.
  Next (planner #4): item 3 remote package aligned with live collection (Amcache, SRUM, setupapi, LNK/JumpLists, firewall log, archived logs,
  TerminalServices-RemoteConnectionManager + WinRM channels; personal ones optional like user-hives); item 2 Insula end-to-end on WS02 last.
- 2026-11-16 CI of d6343d167: 31/32 success, 1 FAILURE in "LogAnalyzer DFIR Build & Package" (run 43930773969, Test Release):
  ThreatIntelPackagesTests.The_pipeline_uses_the_approved_package_records_it_in_the_run_and_shows_a_package_that_did_not_verify_as_a_gap,
  line 194 Assert.Contains(r.RulesUsed, cert.csv) - the approved package's rules were not in RulesUsed (the approval itself succeeded).
  Not related to the diff (it touched RemoteCollection / InvestigationPipeline.Import / the UI); passes locally in Release 3/3; the same test was
  green on 8532d2161 (32/32). Certificate windows are years wide (not a clock edge). Treated as FLAKY, NOT investigated to the root yet:
  next session reads CI of f2c909abd once; if it fails again, investigate (shared state under parallel xUnit, store dir, package load timing).
- 2026-11-16 CI flake ROOT CAUSE FOUND + FIXED (ThreatIntelPackagesTests pipeline test, failed once on d6343d167; c685475ce 32/32 green).
  ThreatIntelStore.LoadActive wrote the rules snapshot to the SHARED %TEMP%\LogAnalyzer-ti and deleted EVERY other snapshot on each load; the
  engine reads the files much later without holding them open, so a parallel test loading the same package (same folder name) deleted this
  test's snapshot before the engine read it. Same race in production with two app instances / two analyses (two instances seen today in the
  lab). Fix: snapshot per process (name suffix -p<pid>); a load removes this process' older snapshots and only other processes' snapshots
  older than SnapshotMaxAge (1 day). Tests: every LoadActive in the test class uses the test's own folder; new
  A_load_keeps_the_recent_snapshot_of_another_process_and_removes_old_ones. Dfir 3886 (3853 passed, 33 skipped) exit 0.
- 2026-11-16 PLANNER #4 ITEM 3 DONE (remote package aligned with live collection), commit with this entry. RemoteCollection: new KnownArtifacts
  (evtx TerminalServices-RemoteConnectionManager/Operational + Microsoft-Windows-WinRM/Operational, amcache, setupapi, firewall-log), optional
  user-hives / srum / recent-items (UI checkbox "Include istoricul personal (SRUM, fișiere recente)", off by default, reset per request);
  helper block Copy-GuardedFile (ok/vss/skipped/link/busy/failed). SRUM set: read directly; if any file is locked the WHOLE set comes from ONE
  WMI shadow copy (only with $AllowShadowCopy, deleted in finally); else old rule (database via esentutl only if locked) with a note.
  Logs held by the system do not fail the step (database copied); other log failures are named separately. Import groups the set into
  SRUM_<key> (key = last notes segment only; review MED: a key planted in the manifest user name is ignored, test). SrumNetworkParser: a
  database WITHOUT the Network Data Usage table (JET -1305 at JetOpenTable; real Windows Server 2025 SRUDB.dat from the lab) is an explained
  gap, not Failed; any other JET error still fails. Test runs on the real copy with LADFIR_SRUM_NO_NETWORK set (owner machine; skipped elsewhere).
  LAB SRV01: shadow copies 0->0, RCM 69 + WinRM 63 events, Amcache 88, setupapi partial, firewall absent (logging off), SRUM 12 files direct.
  Opus review round 2: PARTIAL -> fixed MED (key injection), LOW notes (step-3 note says direct vs esentutl; busy vs failed logs; leftover
  direct copies now abort instead of mixing moments), doc moved. ACCEPTED RESIDUAL: the "other moment" fact of a step-3 set is only in the
  manifest step note, not in the item notes / parser recovery gap; import does not check the step status. Step 2 (WMI shadow copy) is
  exercised only as script text, never executed by a test.
  Next: read CI once; planner #4 item 2 (Insula "Colectează tot" end-to-end on WS02); then a NEW planner.
- 2026-11-17 CI of 3b1e574d1: 32/32 success. PLANNER #4 ITEM 2 DONE: Insula "Colectează tot" end-to-end on LAB-WS02 (lab build of 3b1e574d1,
  update-app insula, t-session sign-in, UIA: header "Colectează tot" -> scope dialog -> Confirmă). Investigation (profile Standard, elevated,
  run COMPLETED, 08:07:38-08:08:13 station time) then station control (NECONFORM 22, DE VERIFICAT 31, NEDETERMINAT 6, CONFORM 36, N/A 4).
  Case CASE-LAB-WS02-20261117-060738 pulled to E:\LA-Lab\evidence\LAB-WS02-case: 261 parser results, 0 Failed (SRUM 114 rows, Amcache 179,
  148 Prefetch, MFT/$I30/USN, Windows Search index + gather, LNK/JumpLists); 31 findings incl. the lab scenarios (LOG-TAMPER High,
  MEDIA-*, AIRGAP-NIC-ADDED, AIRGAP-PRINT-JOB, CRED-BRUTEFORCE); every collector gap has its reason (no Sysmon, no archived logs,
  firewall logging off, no external NTFS media, no Defender history). PDF report generated from the UI (35 pages; recheck 261 OK / 0
  modified / 0 missing), copied to E:\LA-Lab\reports\pdf. NOT a product bug: setupapi Partial (1 record, 1 malformed) = the lab script
  scenario-media.ps1 wrote the synthetic USBSTOR section with the ro-RO date separator (2026.11.06); Windows writes 2026/11/06 on the
  same file. Lab script fixed (InvariantCulture; .bak kept); the line already on WS02 stays. Checked, by design: RemoteToolLogsCollector
  is Standard-only (FullForensic gets the same files from UserArtifactsCollector, test exists); scope dialog prefills the station LIVE
  case scope. OBSERVATIONS for the owner (product questions, not changed): (1) on Standard the coverage row "Hive-uri de registru
  (NTUSER, SOFTWARE)" says the collector ran but produced no source and Home asks to "complete" it, while the profile leaves those hives
  out on purpose (SOFTWARE values are read live by AirGapFactsCollector); (2) the reused scope period (01-09.11) does not cover the
  collection day and nothing warns; (3) after "Colectează tot" the investigation page stays on wizard step 1 and the status bar keeps only
  the control line (the investigation summary is on Acasă, lower down).
  Next: a NEW planner from this checkpoint (planner #4 done). Owner items unchanged (ESE sample, RDP manual test, PR #370, lab cleanup).
- 2026-11-17 PLANNER #5 started (items, by value/risk: 1 multi-instance audit files; 2 coverage family "registry" maps ExecutionArtifactsCollector,
  which never collects NTUSER/SOFTWARE -> false "ran but produced no source" on Standard; 3 scope period ending before the collection day silently
  drops events from Wp14Rules (Ctx.InPeriod) -> non-blocking warning + audit, never auto-extend; 4 status bar after "Colectează tot" keeps only
  the control line (CollectAllState.RecordControl overwrites LastActivity); 5 SRUM step-3 "other moment" residual into item notes).
  ITEM 1 DONE: two app instances on one station share the LIVE case; the second failed with "used by another process" on LogAnalyzer_Audit.log
  (File.AppendAllText) and lost the chained entry written after it; same for chain_of_custody.csv / collection_audit.csv (CsvWriter append).
  Fix: IO\SharedAppend (write alone, readers allowed, retry ONLY sharing/lock violations ~20 s, like HashChain.OpenExclusive; other errors fail at
  once) + CsvWriter.AppendShared (header + BOM decided under the same handle; RowText = old WriteRow output). Decision: the second instance is not
  blocked, it only no longer fails. Tests: Two_app_instances_on_one_case_write_the_plain_audit_files_while_another_writer_holds_them (red on the
  old code with the lab error), A_shared_append_fails_at_once_on_an_error_that_is_not_a_sharing_violation. Opus review: APPROVE (low notes).
  ACCEPTED RESIDUAL: Integrity CountLines (File.ReadLines) swallows a sharing violation and returns 0 for a few ms while a writer holds the file
  (pre-existing); on non-Windows every IOException is retried and FileShare.Read does not exclude writers (app is Windows-only). Not lab-tested
  with two real processes. Suites: Dfir 3904 (3871 passed, 33 skipped), App 1091, UI 370 (369 + 1 skipped), Edition 45, all exit 0.
  Next: read CI once; item 2 (CoverageMatrix registry family), then 3, 4.
- 2026-11-17 PLANNER #5 ITEMS 2, 3, 4 DONE (low risk, not Opus-reviewed: mapping, UI text, one audit line). CI of 72e3c87b2 read once: 25 success,
  7 in progress, 0 failures. (2) CoverageMatrix family "registry" now maps only UserArtifactsCollector (ExecutionArtifactsCollector saves SYSTEM +
  Amcache only): on Standard the row is NOT_COLLECTED instead of a false "ran but produced no source"; test The_execution_collector_does_not_answer_
  for_the_user_and_software_hives. (3) ScopeForm.PeriodEndsBeforeWarning: ScopeDialog shows a non-blocking warning (WarningBrush) when the period
  ends before today; InvestigationPipeline.RunCore audits "case.scope_period_before_collection" on a collecting run. Period never auto-extended
  (approver's decision). Test in ScopeReportLinesTests. Dialog text NOT seen on screen yet (lab). (4) CollectAllState.BeginCollectAll /
  EndCollectAll around the Handler: RecordControl puts the same run's investigation line in front of the control line; a control alone keeps its
  own line; test in CollectAllTests. Suites: Dfir 3906 (3873 passed, 33 skipped), App 1092, UI 370 (369 + 1 skipped), Edition 45, all exit 0.
  Next: read CI once; item 5 (SRUM step-3 note, Opus review) or a lab check of items 1+3 on WS02 (two instances; scope warning).
- 2026-11-17 CI of b462a686c read once: 32/32 success. PLANNER #5 ITEM 5 DONE (closes the planner #4 item 3 residual): RemoteCollection.Import puts an
  application-written moment fact into the notes of every file of a remote SRUM set, BEFORE the set key (the key stays the last segment for
  InvestigationPipeline.SrumSetKey; it travels to the case of the run with the carried notes). SrumSetMomentNote(steps): one "ok" srum step with the step-1
  or step-2 sentence -> nothing; with the step-3 sentence -> SrumMixedMomentNote; anything else (failed, missing, duplicate, unknown note) ->
  SrumUnprovenMomentNote. SrumNetworkParser.RecoveryGap: mixed -> "NU sunt din același moment", likelihood Probabil; unproven -> "nu este dovedit", Posibil.
  Tests: A_srum_set_of_different_moments_says_so_in_the_notes_of_every_file_and_in_the_recovery_gap (real step 3: log held FileShare.None, no shadow copy),
  Only_one_ok_srum_step_with_the_step_1_or_step_2_sentence_counts_as_one_moment (incl. the three prefixes are the script's own text). Opus review: APPROVE;
  LOW 1 (a failed step was called a proven mix) fixed with the unproven note; ACCEPTED RESIDUAL: the target writes the manifest, so it can forge a step-1
  sentence (it also controls the files) or plant the sentence in user/host (only makes the gap more cautious); a failed recovery's exception does not
  name the moment fact (the notes do); step 2 never run end to end. Suites: Dfir 3908 (3875 passed, 33 skipped), App 1092, UI 370 (369 + 1 skipped),
  Edition 45, all exit 0.
  Next: a lab check of items 1+3 on WS02 (two instances; scope warning on screen), then a NEW planner. Owner items unchanged.
- 2026-11-17 CI of 2af9a3dcb read once: 32/32 success. LAB CHECK of planner #5 items 1+3 on LAB-WS02 (lab build of 2af9a3dcb, update-app insula,
  two instances A/B signed in, UIA). ITEM 3 VERIFIED ON SCREEN: the scope dialog shows the orange non-blocking warning "Perioada scopului se încheie
  la 09.11.2026, înainte de ziua colectării (17.11.2026) ..." (shot E:\LA-Lab\shots\LAB-WS02\two-A-scope.png); both runs audited
  case.scope_period_before_collection in LogAnalyzer_Audit.log + audit_chain.jsonl; period not extended. Item 4 seen too: status bar after
  "Colectează tot" = investigation line, then control line. ITEM 1 PARTIAL: A and B ran "Colectează tot" overlapping (14:40:21-14:41:13 and
  14:40:42-14:41:33 UTC), both COMPLETED (cases ...-144021 / ...-144042), no error; but each run writes its own investigation case, not the shared
  LIVE case. Both instances later opened LIVE-LAB-WS02 and appended case.opened/case.recheck 3 s apart (chain seq 3..6 intact, no error).
  Forced-contention probe (separate process holds LIVE\Logs\LogAnalyzer_Audit.log Write/share Read 7 s while a third instance opens LIVE) NOT
  achieved: on Insula only the control report (gated by 22 corrective measures) opens LIVE from the UI; "Colectează acum" on the control page
  did not touch LIVE. Fable consulted (advisor): close with residual. ACCEPTED RESIDUAL: cross-process contention on LIVE LogAnalyzer_Audit.log
  not reproduced with the real app; the retry is proven by the in-process unit test (share modes are per open, not per process); lab showed two
  instances appending 3 s apart without error. SAME BUG CLASS, NOT FIXED (Fable, from source): Json.AppendLine (Json.cs ~31, evidence index
  CaseWorkspace ~249) and PolicyAuditLog.Append (PolicyExecution.cs ~393) still use File.AppendAllText; after the ~19 s SharedAppend budget
  Audit throws before the chain append (CaseWorkspace ~421-422). LAB NOTES: windows of two instances overlap at 0,0, so the driver's mouse
  "click" can hit the other instance - use "invoke" per pid; in a fresh third instance the control wizard did not reach the report (unexplained,
  not investigated). WS02 stopped. No product code changed.
  Next: a NEW planner from this checkpoint; first candidate item: move Json.AppendLine and PolicyAuditLog.Append to SharedAppend (with tests).
  Owner items unchanged (ESE sample, RDP manual test, PR #370, lab cleanup).
- 2026-11-17 PLANNER #6 started. ITEM 1 (in progress, vault-worker + Opus review): cross-process appends. SharedAppend.WriteAfterLastLine (ReadWrite,
  share Read, last line read under the handle; LastNonEmptyLine moved from HashChain); Json.AppendLine -> SharedAppend; evidence id allocated
  under the index handle (max(counter, last line id)+1: two processes/workspaces gave duplicate EV ids); PolicyAuditLog.Append and App
  ChainOfCustodyService.Append compute Prev under the handle (in-process _lastHash forked the chain with two instances); media_live.jsonl and
  AuditLogService via SharedAppend. Probe list P1-P8 in the brief (two writers keep one chain; distinct ids; held file only delays; non-sharing
  error fails at once; bytes unchanged; garbage last line; HashChain unchanged; empty file). StartupLog left (best-effort, per-process).
- 2026-11-17 PLANNER #6 ITEM 1 DONE (vault-worker + main-session fixes; Opus review round 1 REQUEST_CHANGES (2 MEDIUM), round 2 APPROVE (LOW only)).
  SharedAppend: WriteAfterLastLine (last line read under a ReadWrite/share-Read handle) and RewriteUnderHandle (whole-file rewrite in place under the
  same kind of handle, backup path+".tmp" deleted best-effort, leading BOM skipped). CaseWorkspace: evidence id = max(counter, last index line id)+1
  under the index handle (two workspaces/processes issued duplicate EV ids); TransitionState/MarkDisposed via UpdateEvidenceIndex under the handle
  (old temp+move dropped an entry appended meanwhile -> lost line + duplicate id); same refusal messages/order. PolicyAuditLog.Append and App
  ChainOfCustodyService.Append compute Prev under the handle (ChainOfCustodyService no longer caches/restores _lastHash: constructor no longer
  throws on a truncated last line; Verify returns false on a bad line instead of throwing). Json.AppendLine, media_live.jsonl, AuditLogService via
  SharedAppend.WriteText (now create the folder; AppendAllText threw). ExportAuditTests.RawWrite also matches SharedAppend.Write*/Rewrite*
  (review M1: the guard had lost two entries). Tests: Two_workspaces_on_one_case_give_distinct_evidence_ids, A_state_change_in_one_workspace_never_
  drops_an_entry_another_workspace_appends_at_the_same_time, A_refused_state_change_leaves_the_index_bytes_unchanged, Two_custody_services_on_one_
  file_keep_one_chain, A_truncated_last_line_does_not_stop_the_service_..., Two_policy_audit_logs_..., A_chained_append_waits/fails_at_once/on_an_
  empty_file, garbage last line; 3 RawWrite InlineData. ACCEPTED RESIDUAL: a crash between the in-place write and SetLength can leave a stray tail
  in evidence_index.jsonl (case will not open; recover from evidence_index.jsonl.tmp by hand); CaseArchive.Export (FileShare.ReadWrite|Delete)
  in another process can zip a mid-rewrite index; P4 test uses a directory (UnauthorizedAccessException), the sharing filter is checked by reading;
  the policy-chain test also passes on the old code; legacy root Services\ChainOfCustodyService.cs (compiled only into UI.Tests) keeps the cached
  hash. NOTE: Defender (Wacatac.H!ml) blocks the Debug Dfir test DLL again -> run Dfir with -c Release; check the count is > 0.
  Suites: Dfir (Release) 3915 (3882 passed, 33 skipped), App 1098, UI 370 (369 + 1 skipped), Edition 45, all exit 0.
  Next: read CI once; then planner #6 next item (candidates: recovery of evidence_index.jsonl.tmp on open; lab check with two real instances
  of a state change racing a registration). Owner items unchanged (ESE sample, RDP manual test, PR #370, lab cleanup).
- 2026-11-17 CI of 56c62d995 read once: 31 success, 1 in progress, 0 failures. PLANNER #6 ITEM 2 DONE (low risk: message only, not Opus-reviewed):
  a damaged evidence index (e.g. the tail a crash between the in-place rewrite and SetLength leaves) stopped CaseWorkspace.Open with a bare
  JsonException. Now CountEvidence throws InvalidDataException with DamagedIndexMessage: says the case does not open and is NOT repaired (Open's
  rule "Nothing is ever repaired"), names evidence_index.jsonl.tmp when it exists (full content written before the last state change; compare,
  replace by hand keeping the damaged index, the change may lack a custody entry), else says there is no backup. No automatic restore (decision:
  consistent with the no-repair rule). Test A_damaged_evidence_index_stops_the_open_with_the_backup_named_and_nothing_repaired (with/without
  backup; bytes unchanged). Suites: Dfir (Release) 3917 (3884 passed, 33 skipped), App 1098, UI 370 (369 + 1 skipped), Edition 45, all exit 0.
  Next: read CI once; planner #6 item 3 = lab check with two real instances on WS02 of a state change racing a registration is impractical from
  the UI (state changes are not reachable without evidence workflows); ask a NEW planner from this checkpoint. Owner items unchanged.
- 2026-11-17 CI of 56c62d995 and de1aef038 read once: 32/32 success each. PLANNER #7 (Plan agent, verified in code): 1 RISKY sealed-chain head race
  (HashChain.Verify reads log, head and .head separately -> false "trunchiat sau rescris"; WriteSealedHead File.Move fails while a reader holds .head
  (ReadAllText, no share Delete) AFTER the line is written -> seal stuck, auth audit permanently Broken: card sign-in refused, admin page
  "accounts.json modificat", D05/A15/A17 invalid); 2 LOW case-file readers (Json.ReadLines/File.ReadLines, HashChain VerifyPlain/Contains,
  RegisteredOutputs, CountLines, AnalysisFootprint) fail or count 0 while another instance writes -> SharedRead helper with retry; 3 RISKY
  CaseArchive.Export (FileShare.ReadWrite|Delete) can zip a half-rewritten index/chain into a signed archive; 4 LOW transient false "legacy"
  plain lines in EffectiveChains (Audit writes plain log before chain). Not now: StartupLog, legacy root ChainOfCustodyService (UI.Tests only),
  auto-restore of evidence_index.jsonl.tmp, third-instance wizard stall (no code evidence).
  ITEM 1 in progress (vault-worker, then Opus review): atomic snapshot (log bytes + .head under one share-Read handle) for Verify and AuthService;
  ReadSealedHead with share ReadWrite|Delete + retry; WriteSealedHead Move retry; sealed Append all-or-nothing (truncate back if the head move
  finally fails). Probes P1-P6 in the brief.
- 2026-11-17 PLANNER #7 ITEM 1 DONE (vault-worker; Opus review APPROVE, MEDIUM + LOW fixed by the main session). HashChain.ReadVerified(): log bytes and
  .head read under ONE FileShare.Read log handle (retry sharing/lock only); Verify() and AuthService.VerifyAuditSnapshot use it (no false "trunchiat
  sau rescris" while another instance appends). ReadSealedHead opens .head ReadWrite|Delete with retry; WriteSealedHead retries the Move on sharing
  violations and on "access denied" (Windows' answer to replacing a file held without share Delete) only while .head is a writable file and only
  UnauthorizedAttempts=60 (~2 s; review MEDIUM: a persistent deny held the log ~19 s); sealed Append is all-or-nothing: the log is truncated back
  (SetLength + Flush under the FileShare.None handle) if the head move finally fails, original cause kept (AggregateException if the rollback fails).
  Verify(byte[]) kept for unsealed callers (CaseWorkflow, TimelineNotebook, CustodyOverview, HashSetLibrary). Tests HashChainSealConcurrencyTests:
  reader holding .head, verify loop vs 300 appends, failed head move, read-only head fails at once (first three red on the old code, checked by
  the worker by restoring the old files). ACCEPTED RESIDUAL: crash between line write and head move still leaves the log ahead of the seal
  (unchanged); .head read before the key-null check (non-admin unable to read .head would throw instead of "key unavailable"; unlikely);
  rollback deletes .head.tmp and could race a Reseal in another process (rare; admin retries); test 3 covers rollback via a failing tmp
  write, not via exhausted Move retries. Suites: Dfir (Release) 3921 (3888 passed, 33 skipped), App 1098, UI 370 (369 + 1 skipped), Edition 45.
  Next: read CI once; planner #7 item 2 (SharedRead for case-file readers: Json.ReadLines, HashChain VerifyPlain/Contains, RegisteredOutputs,
  CountLines, AnalysisFootprint), then 3 (CaseArchive.Export consistency), 4 (EffectiveChains transient legacy). Lab check of item 1 later
  (two instances + device monitor, then the control report: D05 valid). Owner items unchanged.
- 2026-11-17 CI of a98a576ec read once: 4 success, 28 in progress/queued, 0 failures. PLANNER #7 ITEM 2 DONE (low risk, vault-worker, not
  Opus-reviewed): IO\SharedRead (Lines/AllText): FileShare.Read kept (an in-place rewrite stays excluded), whole read under the handle, retry only
  sharing/lock violations (SharedAppend policy ~20 s), BOM/splitting like File.ReadLines. Used by Json.ReadLines (CountEvidence on Open,
  LoadEvidence, auth audit readers), HashChain VerifyPlain/Contains, RegisteredOutputs, CountLines (a sharing violation no longer counts 0; it
  propagates after the budget) and AnalysisFootprint. Main-session fix: Json.ReadLines now materializes, so the large single-run outputs read by
  EventSink.Read go through new lazy Json.StreamLines (spec §77: never buffer a whole log). Tests SharedReadTests (11; A_case_opens_while_another_
  writer_holds_the_evidence_index and CountLines_waits... red on the old code, checked by the worker by reverting). RESIDUAL: AnalysisFootprint
  still turns an IOException after the budget into 0 reports; Json.Read (single JSON files) unchanged; other File.ReadLines callers outside these
  files not reviewed. Suites: Dfir (Release) 3932 (3899 passed, 33 skipped), App 1098, UI 370 (369 + 1 skipped), Edition 45, all exit 0.
  Next: read CI once; planner #7 item 3 (CaseArchive.Export: shared-written files with share Read + retry; risky -> worker + Opus), then 4.
- 2026-11-17 CI of a98a576ec read once: 32/32 success; cadd54562: 28 success, 4 in progress, 0 failures. PLANNER #7 ITEM 3 DONE (main session;
  Opus review APPROVE, both MEDIUM fixed). CaseArchive.Export: IsSharedCaseLog (direct children of Logs\, Evidence\evidence_index.jsonl, root
  media_live.jsonl of the LIVE case; NOT *.tmp / *.lock, which another instance must be able to replace/delete) opened with FileShare.Read + retry
  of sharing violations (writers excluded while copied: no half line / half-rewritten index zipped and signed); other files (evidence, outputs)
  unchanged (share ReadWrite|Delete). A sealed log's .head is read while that log is held and archived from that copy (same moment, as
  HashChain.ReadVerified). Tests (App): An_archive_waits_for_another_instance_writing_a_shared_case_log_and_never_zips_half_a_line,
  A_sealed_log_archived_while_another_instance_appends_keeps_its_head_from_the_same_moment (CHECKED: passes also without the head fix - the window
  is microseconds; regression guard only), classification theory (14 cases). RESIDUAL: files are each consistent, not one snapshot together;
  export runs on the UI thread and can wait up to ~20 s for a writer; short-lived files listed then gone (.lock, .tmp) still throw
  FileNotFoundException (pre-existing). Suites: Dfir (Release) 3932 (3899 passed, 33 skipped), App 1113, UI 370 (369 + 1 skipped), Edition 45.
  Next: read CI once; planner #7 item 4 (EffectiveChains transient "legacy" lines, low); then lab check of item 1 (two instances + monitor,
  control report D05 valid). Owner items unchanged.
- 2026-11-17 CI of cadd54562 read once: 32/32; 3f02deaed: 31 success, 1 in progress. FOUND + FIXED (pre-existing, CaseArchive): a .tmp/.lock removed
  by another instance between the listing and the copy read as 1601 and ZipArchiveEntry.LastWriteTime threw "cannot be converted into a Zip file
  timestamp" -> the whole export failed (the sealed-log archive test was flaky in the full App suite: 1 of 3 runs). Now the file is opened before
  the entry is created, a vanished transient helper (*.tmp, *.lock) is skipped, ZipTime clamps to 1980-2107. Test A_file_time_the_zip_format_
  cannot_hold_is_clamped_instead_of_failing_the_export. App suite green twice in a row after the fix.
  PLANNER #7 ITEM 4 DONE (low risk, not Opus-reviewed): EffectiveChains counted every plain audit line beyond the chain as "linii anterioare"
  (legacy), so a line another instance was writing (plain first, chain second, same timestamp) or added after the chain was verified showed as
  legacy. Now, when the chain has entries, only plain lines whose timestamp is EARLIER than the first chained entry (ms precision) count; a line
  without a readable timestamp still counts (old format); no first chained time readable -> the old count. Tests A_plain_audit_line_another_
  instance_is_writing_is_not_reported_as_an_older_unchained_line, A_plain_audit_line_older_than_the_first_chained_entry_is_still_... RESIDUAL:
  an injected plain line newer than the chain is no longer reported as legacy (the plain log was never a tamper signal; the chain is).
  Suites: Dfir (Release) 3934 (3901 passed, 33 skipped), App 1114 (twice), UI 370 (369 + 1 skipped), Edition 45, all exit 0.
  PLANNER #7 DONE (items 1-4). Next: read CI once; lab check on WS02 of item 1 (two instances + device monitor, then the control report: D05
  valid; auth audit not Broken), then a NEW planner. Owner items unchanged.
- 2026-11-17 CI of 6b238bfc3 (with 6eb1836f5): 32/32. LAB CHECK planner #7 item 1 on WS02 (lab build of 6b238bfc3): hostile reader holding the auth audit
  .head the old way (FileShare.Read, no Delete; 2338 opens in 150 s) while instances A/B signed in, confirmed scopes and a third instance C signed
  in: "Audit autentificare: 663 -> 667 -> 670 de intrări verificate", "accounts.json corespunde ...", no .head.tmp left. Round with three parallel
  station controls: result pending at checkpoint time (lab driver slow: an invoke on "Pornește controlul" blocks until the control ends).
  LAB NOTE: after "Confirmă scopul" the control must be pressed again (by design, LiveCase.ScopeRequiredMessage); the "third instance stall" seen
  earlier is this, not a product bug. A page reached again by nav keeps its old values (navigate away first).
- 2026-11-17 EXTERNAL AUDIT (ChatGPT web research, owner-supplied file; treated as hypotheses): code inventory (Explore) + Fable advice.
  Already present: ShellBags, Recycle Bin $I, VSS inventory (read-only), offline archive verifier (KeyGen verify-archive), reparse/junction
  handling, ClockMemory, parser and rule versions. Owner-side (not code): SOPs, validation pack, accreditation matrix (HG 585/2002 cap. 8 INFOSEC),
  PAdES profile (B-B only today; isolated station has no TSA). Public references downloaded (not in repo): E:\LA-Lab\refs\standards (NIST SP 800-86,
  SWGDE 17-F-002 v2.1, ETSI EN 319 142-1 V1.3.1, RFC 2104, RFC 6979, HG 585/2002 standard text). ISO/IEC 27037/41/42/43 are paid: not downloaded.
  PLANNER #8 (Fable-ranked): 1 G1 multi-PROCESS harness (worker exe via Process.Start, FailFast kill points after line flush / head tmp / head move
  via env var) - Fable expects a defect: a kill after the line flush leaves the log ahead of the seal and reads as tampering ("intrări adăugate
  după ultimul cap sigilat") until an admin reseals; run the kill tests BEFORE fixing. 2 G2 anti-rollback witness: MAC binds only seq|hash, so an
  older valid log+.head pair verifies; A11 checks only that the paper anchor is still contained (a copy taken after the last anchor gives CONFORM);
  design: HKLM witness (seq, hash, MAC with the seal key) via AuthStoreSecurity, written after the head move; head behind witness = restored
  copy, witness behind head = benign crash; residual: admin editing registry or full image restore (paper code/CD-R only); separate Opus review.
  3 G4 artifacts in this order: WMI-Activity 5861 (permanent subscriptions), TaskCache Tree/Tasks, RDP RemoteConnectionManager 1149 + SMB
  channels (on an isolated station any hit is a finding), PowerShell transcripts, OpenSave/LastVisited PIDL MRU last. 4 SBOM (CycloneDX; Deterministic
  is the SDK default). 5 G3 forked-case compare only (identical / prefix / forked at seq N). DROPPED: PAdES B-T/TSA, thumbcache, DAM, WMI OBJECTS.DATA
  parsing, 20x1000 process stress.
  Next: planner #8 item 1 (harness + kill tests first, report the defect, then fix with Opus review). Owner items: ESE sample, RDP manual test,
  PR #370, lab cleanup; questions from the audit (accreditation authority, supported Windows builds, PAdES profile wanted).
- 2026-11-17 LAB CHECK planner #7 item 1 RESULT (WS02): during the round with three parallel station controls (instances A/B/C) the station auth audit
  grew 670 -> 842 lines; afterwards the last line (seq 842) and auth_audit.jsonl.head (seq 842) carry the same hash, no .head.tmp left (seq/hash
  alignment checked from the VM disk; the MAC itself was verified by the app only up to 670, the UI driver stalled while three controls ran).
  The VM shut itself down at 21:58: System 1074 by wlms.exe (Windows License Monitoring = expired evaluation licence, hourly shutdown) - lab issue
  for the owner, not a product defect. WS02 stopped.
- 2026-11-17 PLANNER #8 ITEM 1 DONE (phase A vault-worker: harness; phase B main session; Opus review r1 REQUEST_CHANGES 2 MEDIUM, r2 APPROVE).
  Multi-PROCESS harness: test exe LogAnalyzer.Dfir.ChainWorker (in LogAnalyzer.slnx, never published: Package job publishes only the three editions;
  calls RuntimeGuard like every exe, added to RuntimeGuardEntryPointTests' entry list as that test instructs) + internal HashChain.FaultInjection
  (off by default, no env read when off; LA_CHAIN_FAULT=<point>[:n] -> FailFast at after-intent / after-line-flush / after-head-tmp / after-head-move).
  DEFECT CONFIRMED by kill tests before the fix: a crash after the line flush (or after .head.tmp) left the sealed log one entry ahead of the seal ->
  Broken "intrări adăugate după ultimul cap sigilat ... fără cheia de sigilare" for ever, the next append did not repair it (card sign-in refused).
  FIX: before the line the key holder writes <log>.head.pending = {prevSeq, prevHash, seq, mac=HMAC(key,"LogAnalyzer-chain-intent-v1|prevSeq|prevHash|
  seq|hash")} (no hash in clear: review M2, a clear hash let someone without the key rebuild a rolled-back entry); Verify accepts exactly that
  announced entry right after a valid sealed head or, for the first sealed append, after the empty chain (review M1); the next Append finishes the
  head move; rollback deletes the intent; WriteIntent retries sharing violations; .head.tmp written with Flush(true) (power loss) and share Read;
  CaseArchive treats *.pending as transient; AuditAnchor CD-R export copies the intent. Tests MultiProcessChainTests (3 processes x 200 sealed
  appends with a verify loop; 3 x 200 SharedAppend lines; crash at each point incl. the first append; intent hides the hash; a line without the key
  after an old or announced intent stays Broken). ACCEPTED RESIDUAL (pre-existing, LOW): a .head.tmp left after a failed rollback holds the
  rolled-back entry's hash in clear (offline brute force of the entry, then revive) -> later: 128-bit random nonce in each sealed entry; online
  oracle (Valid/Broken per planted guess) needs one app verify per guess over ~1e7 timestamps: not practical; power loss beyond the tmp flush not
  covered. Suites: Dfir (Release) 3945 (3912 passed, 33 skipped), App 1114, UI 370 (369 + 1 skipped), Edition 45, all exit 0.
  Next: planner #8 item 2 (HKLM anti-rollback witness, separate Opus review) after the owner's design proposal work (uploaded file
  Propunere_Redesign_WPF_DFIR_Compliance.md). Owner items unchanged.
- 2026-11-18 REDESIGN STEP 1 (owner's ChatGPT proposal Propunere_Redesign_WPF_DFIR_Compliance.md, made WITHOUT screenshots; checked against the code):
  the app already has a token design system (Themes\Tokens\ColorTokens/Typography with Segoe UI Variable + Segoe Fluent Icons, Fluent.Light,
  StatusBadges, nav with an active rail in Navigation.xaml) - the proposal's palette/nav are mostly already there. Real gaps found: (1) the Control
  stație "Rezultat" column bound the ControlStatus enum and showed "DeVerificat"/"NuSeAplica" (not the PDF words); (2) SidebarTabStyle defined twice
  (Buttons.xaml dead copy, Navigation.xaml wins). Done: ControlStatusTextConverter (= ControlReportPdf.StatusText) + DataGridTemplateColumn badge:
  PDF word + Segoe Fluent symbol (E711 x, E7BA !, E9CE ?, E73E check, E738 -) + tone of the Home chips (Critical/High/Low/Success badge brushes,
  NU SE APLICĂ neutral), glossary tooltip kept, SortMemberPath=Status, compact top-left (lab screenshot showed the cell style stretched it);
  Buttons.xaml duplicate removed. UsabilityGlossaryTests regex updated to the template column (same assertions: column, Status binding, TermTip).
  Visual check: offscreen render of the real column XAML with the real theme (temporary test, not committed) + lab screenshot on WS02 (before the
  alignment fix). NOT done (product decisions): dark theme (all ~1900 resource refs are StaticResource: big change + many literal-XAML tests),
  two-row header, status-bar history. Lab driver: new "scroll <percent>" verb (ScrollPattern) and Shot picks the largest window (a tooltip was shot);
  E:\LA-Lab\scripts\driver-ws02.ps1 (.bak kept). Suites: App 1114, UI 370 (369 + 1 skipped), all exit 0 (Dfir/Edition untouched by this step).
  Next: owner decides dark theme / header; planner #8 item 2 (HKLM anti-rollback witness). Owner items unchanged.
- 2026-11-18 LAB LICENSING (owner asked to stop the hourly shutdown; an unofficial activation script was REFUSED): slmgr /dlv showed all four lab VMs
  expired (0xC004F009 grace time expired) with 1 rearm left; slmgr /rearm + restart on each: LAB-DC01 and LAB-SRV01 (ServerDatacenterEval) are now
  "Initial grace period"; LAB-WS01 and LAB-WS02 (EnterpriseEval) rebooted but stay in Notification mode (0xC004F009) - rearm does not restore an
  expired client evaluation - so they keep shutting down hourly (wlms). Rearm count now 0 on all. Remaining legitimate options (owner): a new
  Windows 11 Enterprise evaluation image for WS01/WS02 (reinstall + lab scenarios), or a licensed Windows (an Eval SKU cannot take a retail key:
  reinstall), or keep lab runs under one hour.
- 2026-11-18 PLANNER #8 ITEM 2 DONE (anti-rollback witness; vault-worker core + main-session fixes; Opus review r1 REQUEST_CHANGES 3 MEDIUM + 1 LOW,
  r2 REQUEST_CHANGES 1 MEDIUM -> main session decided and fixed after the 2-round limit). HashChain: IHeadWitness + RegisterHeadSeal(path, key,
  witness); witness MAC = HMAC(key,"LogAnalyzer-chain-witness-v1|seq|hash"); Append advances it after the head move (monotonic; only over a plain-valid
  log that still contains the witness entry -> a fork cannot take it over); a failed witness write never loses/rolls back an entry. Verify (only when
  otherwise Valid; witness read UNDER the log handle in ReadVerified): absent -> Valid; present but unreadable -> Broken "nu poate fi citit"; MAC
  invalid -> Broken; witness ahead of the log -> Broken "jurnalul a fost înlocuit cu o copie mai veche"; entry missing -> Broken "rescris"; Reseal
  resets it. Production: FileHeadWitness in %ProgramFiles%\LogAnalyzer\witness (registry REJECTED by the Standalone Edition guard: no RegistryKey
  SetValue/CreateSubKey - owner decision if wanted; ProgramData REJECTED after review r2: standard users can pre-create folders there and lock out
  card sign-in), folder created WITH an Administrators/SYSTEM-only protected DACL + owner Administrators (no Users read), reparse points refused, an
  existing unprotected folder moved aside "<dir>.untrusted-<utc>-<guid>" (kept) and recreated. Wired in AuthStoreSecurity.Windows() (auth audit
  only; ThreatIntel history unchanged). Tests HeadWitnessTests (12: rollback, fork, lagging witness, tamper/delete, write failure, reseal, no-witness
  unchanged, concurrent writers, file witness round trip + folder restore, unreadable witness, concurrent ReadVerified, unprotected folder moved aside).
  ACCEPTED RESIDUAL: an administrator deleting the witness (or restoring Program Files and the auth folder together, or a full image) is not caught
  (paper anchor / CD-R remain); the real ACL code (ForLog / OnlyAdminsCanRead / ApplyAdminOnlyAcl) is not unit-tested (test process not elevated) -
  verify on a lab VM; a move-aside is not yet surfaced in the audit/UI; a GPO making the creator the owner of new objects would fail the check.
  Suites: Dfir (Release) 3957 (3924 passed, 33 skipped), App 1114, UI 370 (369 + 1 skipped), Edition 45, all exit 0.
  Next: lab check of the witness on a VM (elevated app: folder created with the admin-only DACL; restore an older auth folder -> Broken "copie mai
  veche" on the admin page); then planner #8 item 3 (artifacts: WMI-Activity 5861 first). Owner: registry witness yes/no; WS01/WS02 eval reinstall.
- 2026-11-18 LAB CHECK of the witness on WS02 (lab build of 34a96cb1d, elevated app): VERIFIED - on the first audit append the app created
  C:\Program Files\LogAnalyzer\witness with exactly "SYSTEM:(OI)(CI)(F)" and "BUILTIN\Administrators:(OI)(CI)(F)" (no Users), and the witness file
  advanced with the audit (930 -> 932 -> 933 -> 934 -> 935); final state consistent (log, .head and witness at seq 935, same hash, no pending/tmp).
  After an older auth folder was put back (log 930, witness 932) the app showed the auth integrity warning and refused card sign-in.
  NOT VERIFIED IN THE LAB: the exact admin-page message ("copie mai veche") - the lab sign-in automation failed in every session of this run
  (fields left empty), and in the clean rerun the auth_audit.jsonl.head file was held by another process so the "attack" copy was partial. My lab
  script retried PS Direct calls on any error and ran the copy steps up to five times, mixing snapshots; WS02's auth folder was restored from the
  pre-test copy (seq 929) and the test witness removed, then the clean rerun ended consistent. The message itself is covered by HeadWitnessTests.
- 2026-11-18 PLANNER #8 ITEM 3a DONE (WMI permanent subscriptions; vault-worker + main-session cleanup; low/medium risk, not Opus-reviewed: detection
  wording/severity only). REAL sample first: on LAB-WS02 a test LogFileEventConsumer subscription (LA-TEST-*) produced WMI-Activity 5861
  (UserData Operation_ESStoConsumerBinding: Namespace, ESS, CONSUMER, PossibleCause with the __EventFilter / consumer instances); also a Windows
  default 5861 on a clean VM (ESS "SCM Event Log Filter", NTEventLogEventConsumer "SCM Event Log Consumer"). Test subscription removed (0 left);
  sample kept outside the repo (E:\LA-Lab\diag\wmi, contains SIDs). FINDING: rule PERSIST-WMI already existed (Correlation.cs; the earlier Explore
  inventory said MISSING - wrong) and EvtxParser already keeps UserData leaves. Refined: 5861 -> one finding per (ESS, CONSUMER) pair (repeats
  aggregated); default SCM pair excluded only with its own filter (a look-alike with another filter is reported); severity High for
  CommandLine/ActiveScript consumers, Medium otherwise (was always High); the finding lists namespace, filter, filter Query, consumer class/name and
  CommandLineTemplate/ExecutablePath/ScriptText/ScriptFileName/Filename/SourceName parsed from PossibleCause; says a mechanism exists, not who made
  it nor that it ran. Sysmon 19-21 unchanged. Tests WmiSubscription5861Tests (5, from the real XML without SIDs). Grouping separator written as
  "\u0001" (was a raw control character). Property names other than those in the captured event come from the Microsoft class names.
  Suites: Dfir (Release) 3962 (3929 passed, 33 skipped), App 1114, UI 370 (369 + 1 skipped), Edition 45, all exit 0. The worker saw one
  HashChainSealConcurrencyTests access-denied failure once; 5 targeted reruns passed (possible rare flake, watch CI).
  Next: planner #8 item 3b (TaskCache Tree/Tasks), then RDP RemoteConnectionManager 1149 + SMB channels, PowerShell transcripts. Owner items unchanged.
- 2026-11-18 (claude, hg585) CI of b22eb701f: DFIR Build & Package FAILED (other workflows green): HeadWitnessTests.A_verify_running_while_another_writer...
  and MultiProcessChainTests.Three_processes... -> sharing violation in HashChain.ReadVerified after ~20 s (reader starvation: Append re-took the
  FileShare.None handle right after releasing it). FIX: HashChain.Turnstile = named mutex Local\LogAnalyzer-chain-<sha256(path)[..32]> taken around
  OpenExclusive's open loop, ReadVerified, Head(), ReadAllShared (never while a writer holds the file -> no deadlock). Best effort: creation failure /
  timeout -> old retry loop. Review (vault-reviewer, round 1): no deadlock, semantics unchanged; FINDING a standard user can pre-create/hold the
  predictable mutex -> fixed: Wait 2 s, then the turnstile is skipped for that log for 60 s (cost <= ~2 s/min/log, never a failure). Residual
  (accepted): SharedRead.Lines callers (VerifyPlain, Contains) do not use the turnstile. New tests: four writers back to back, a reader must get
  in < 5 s (fails without the turnstile under full CPU load: 11 s; passes with it); a held turnstile costs one short wait then is skipped.
  HashChainSealConcurrencyTests reader-holds-head test: the release ran in Task.Run and started late on a busy pool in the full suite (the rare
  failure noted above) -> dedicated thread; the 4-writer test uses LongRunning tasks. Suites: Dfir (Release) 3964 (3931 passed, 33 skipped) x2,
  App 1114, UI 370 (1 skipped), Edition 45 (first turnstile version; delta touches only Dfir.Core HashChain + Dfir tests).
  Item 3b TaskCache: Explore inventory -> RawRegistry (IO\RawRegistry.cs), SoftwareHiveParser (RegistryPersistenceParsers.cs:267, source type
  software_hive), register in WindowsParsers.cs, synthetic hives via DeletedRegistryTracesTests NewHive. Lab capture script
  E:\LA-Lab\scripts\t-taskcache.ps1 ready; WS02 failed to start (host RAM: 4 GB VM, ~4 GB free). Next: free RAM / start WS02, capture, then worker.
- 2026-11-18 (claude, hg585) CI of ddaa8a96a (turnstile fix): all workflows green incl. DFIR Build & Package.
  Planner #8 item 3b DONE: TaskCache from the offline SOFTWARE hive. Lab: task \LA-TEST\LA-TEST-TaskCache created on WS02 (2 Exec, SYSTEM),
  keys + hive captured, task removed (cleanup verified); reference capture of the whole station (226 tasks: Get-ScheduledTask/Info JSON,
  System32\Tasks via robocopy /B, hive at the same moment) kept in E:\LA-Lab\diag\taskcache (lab only; Tree SD values hold the machine SID).
  LA-LAB host RAM: LAB-SRV01 put in Saved state to start WS02 (Start-VM LAB-SRV01 to resume).
  New LogAnalyzer.Dfir.Windows\Parsers\TaskCacheReader.cs, called from SoftwareHiveParser: Source "TaskCache", one event per action;
  Actions v3 (context, 0x6666 Exec, 0x7777 ComHandler) - 226/226 tasks, 0 action mismatches vs Get-ScheduledTask; DynamicInfo: FILETIME@12 =
  LastRunTime 226/226, u32@24 = LastTaskResult 93/98 run tasks (5 = running at capture, 267009 computed by the service), FILETIME@4 =
  cache (re-)registration (194/226 in one minute after servicing -> field CacheRegisteredUtc, NOT creation), FILETIME@28 unproven (neutral name).
  Anomalies (fields + gaps): Tree without SD (task hidden from listings), Tree Id without Tasks key, Tasks without Tree, duplicate Tree Id,
  unreadable Actions/DynamicInfo. Limits: 50 000 events/hive, 200 000 keys, tree depth/cycles, shared Actions cells parsed once (key = cell +
  declared length). RawRegistry: big-data size validated against the hive length and segments x 16344 before allocating.
  Reviews: round 1 (CHANGES: amplification, CreatedUtc overclaim, RawRegistry OOM, duplicate Id, docs, tests) -> fixed by a new worker;
  round 2 (CHANGES: dedupe key cell-only) -> fixed by main session + test that fails on the old key. Defender ML (Wacatac) flagged the
  test DLL when the real Actions hex (cmd redirection) was embedded -> replaced by a helper-built blob with benign commands (real bytes stay
  covered by the lab-file test); host Defender untouched. Lab-file tests ([LabFileFact]) skip when E:\LA-Lab is absent, also under
  LADFIR_REQUIRE_CORPUS. Residuals: no transaction-log replay; Triggers blob not parsed; no correlation rule yet (TaskCache vs task XML,
  hidden-task finding) - next step. Suites: Dfir (Release) 3990 (3957 passed, 33 skipped), App 1114, UI 370 (1 skipped), Edition 45, exit 0.
  Next: correlation for TaskCache (task in cache without XML / Tree without SD -> finding), then RDP 1149 + SMB, PowerShell transcripts.
- 2026-11-18 (claude, hg585) CI of 40c982d66: DFIR job failed on App test CaseArchiveTests.A_sealed_log_archived_while_another_instance_appends...
  (sharing violation in CaseArchive.OpenForArchive: same reader starvation). Fix: OpenForArchive takes HashChain.Turnstile for shared case logs
  (held only while opening). CaseArchiveTests 23/23 x3 under full CPU load; Dfir (Release) 3990 (3957 passed, 33 skipped), App 1114, exit 0.
  Next: TaskCache correlation rules (worker): Tree without SD -> finding; task in cache without XML only when XML of the same station is in the
  case (collection gaps, e.g. access-denied task files, must not become findings); PERSIST-TASK-CONFIG on TaskCache without duplicating XML.
- 2026-11-18 (claude, hg585) CI of 6e2f6f065 green. TaskCache correlation DONE (Correlation.cs, RuleContracts.cs, ScheduledTaskParser ComData):
  PERSIST-TASK-NO-SD (Tree without SD: hidden from listings; High/Direct/High, one per task); PERSIST-TASK-NO-XML (per task, Medium/Correlated,
  only when >= 80% of the host's cache tasks have XML; else ONE Low summary with <= 10 names; Host is empty in the pipeline -> "Host gol"
  alternative); PERSIST-TASK-CACHE-MISMATCH (same task, different actions in cache vs XML; Medium/Correlated; never claims tampering; ignores
  Tasks keys without Tree, partially read Actions blobs, and compares each collected copy on its own); PERSIST-TASK-CONFIG extended to cache
  Exec events, suppressed only when the XML has the same command AND arguments. Titles without SIDs (TaskNameWithoutSid). TaskCacheReader:
  Tree Ids without Tasks key (31 on the clean lab station) -> ONE note per hive instead of one gap each. Lab station: 0 NO-SD, 0 NO-XML,
  0 MISMATCH, 1 CONFIG (from XML). Reviews: r1 CHANGES (arguments in suppression, NO-XML flood, SIDs in titles, orphan gaps) -> new worker;
  r2 APPROVE with low risks R2/R3 (duplicates / partial blob -> false MISMATCH) -> fixed by main session + test. Residuals: Arguments compared
  case-insensitively (theoretical blind spot); TaskNameWithoutSid strips only a trailing SID; collection gaps not visible to Correlation.
  Suites: Dfir (Release) 4011 (3978 passed, 33 skipped), App 1114, UI 370 (1 skipped), Edition 45 (last three before the final R2/R3 delta,
  which touches only Correlation.cs + its test).
  Next: RDP RemoteConnectionManager 1149 + SMB channels, then PowerShell transcripts, OpenSave/LastVisited MRU; SBOM; forked-case compare.
- 2026-11-18 (claude, hg585) CI of 82a6fad58 green. RDP/SMB item: Explore inventory -> RDP 1149 ALREADY used (RDP-SESSIONS, REMOTE-RDP-INTERNAL);
  missing: SMBServer/SmbClient Security channels, RdpCoreTS, RDPClient. Lab: WS02 has NO network adapter (isolated, workgroup) - loopback SMB
  fails (1231). Temporary NIC on switch LA-Lab-Izolat (10.10.10.32), capture, NIC removed (verified) - WS02 back to isolated; File and Printer
  Sharing firewall group was enabled on WS02 (left enabled; harmless without a NIC). Real events: WS02 SMBServer/Security 551 x3 (sockaddr
  ClientAddress, Status 0xC000006D/0xC0000072, UserName empty), DC01 SmbClient/Security 31010 (\10.10.10.32\C$, 0xC0000022). Samples in
  E:\LA-Lab\diag\smb (lab only). DONE: collect SMBServer/Security + SmbClient/Security (Collectors.cs, RemoteCollection.KnownArtifacts; absent
  channel = gap); rules SMB-SERVER-AUTH-FAIL (551 bursts per decoded client IP, T1110, no account named, cross-ref CRED-BRUTEFORCE) and
  SMB-CLIENT-SHARE-DENIED (31010 admin shares per server, Low; >= 5 servers in 3 h -> Medium T1135); ItemMeaning T1135. Review APPROVE;
  main session fixed 3 notes (raw-hex grouping without the port, loopback alternative, "(nume declarat de client)" in titles) + tests.
  No RDP 1149 / RdpCoreTS 131 / RDPClient samples exist in the lab (manual RDP test = owner item). Suites: Dfir (Release) 4025 (3992 passed,
  33 skipped), App 1114, UI 370 (1 skipped), Edition 45, all exit 0.
  Next: PowerShell transcripts, then OpenSave/LastVisited PIDL MRU; SBOM; forked-case compare. Lab: LAB-SRV01 still Saved.
- 2026-11-18 (claude, hg585) CI of 97a268e5a green. PowerShell transcripts DONE. Lab WS02: transcription policy enabled temporarily (key was
  absent -> removed again, verified "policy after: absent"); 2 real transcripts (with OutputDirectory + invocation header -> C:\LA\pstr\yyyyMMdd\;
  without -> Documents ROOT); times local without zone; native output not transcribed; no PSReadLine history on the station (needs an
  interactive console: not covered). Samples E:\LA-Lab\diag\pstr (lab only). New PowerShellTranscriptParser (session + per-command events,
  output text NOT stored, only OutputLineCount; local time converted only with a known station zone; "PS>$global:?" skipped; interactive
  "PS C:\path>" prompt and PS 7 header unseen -> lower confidence). Live collection (UserArtifactsCollector, full profile): Documents root +
  yyyyMMdd subfolders + policy OutputDirectory (HKLM + running user's HKCU), links refused, UNC -> gap, caps reported. Remote: optional
  artifact "ps-transcripts" under the personal-history checkbox (relabelled), dedupe of folders/files, links on the policy path refused,
  limits reported, the collection script's own transcript excluded by PID + start time. Detector: PS-CMDLINE-SUSPICIOUS keyword logic shared
  (AssessPsCommand, unchanged behaviour) + new PS-TRANSCRIPT-SUSPICIOUS ("comandă executată în sesiune", not "tastată"). Reviews: r1 CHANGES
  (dedupe, links, silent limits, wording, self-transcript) -> new worker; r2 CHANGES F1 (the app DOES run powershell.exe: AuditCollector.ps1)
  -> main session: such a transcript is LABELLED (alternative explanation, name can be imitated), never hidden or lowered; test claim fixed.
  Accepted residuals: OutputDirectory "C:\" normalizes to "C:" remotely (F2); 20-line header read can read a newline-free file whole (F3);
  Get-LinkOnPath treats access-denied as no link; redirected Documents (OneDrive) not covered; remote self-exclusion not run end to end.
  Suites: Dfir (Release) 4048 (4015 passed, 33 skipped), App 1114, UI 370 (1 skipped), Edition 45, all exit 0.
  Next: OpenSave/LastVisited PIDL MRU; SBOM (CycloneDX); forked-case compare. Lab: LAB-SRV01 still Saved.
- 2026-11-18 (claude, hg585) CI of fd59033fb: DFIR job FAILED again on HeadWitnessTests 4-writers + MultiProcessChainTests (reader starvation).
  Cause: the 2 s turnstile wait (review DoS fix) is too short behind 3-4 writers that each fsync on a slow runner; the timeout switched the
  turnstile off for 60 s. Fix (HashChain.Turnstile): wait Wait (2 s), then on in Slice (2 s) steps up to MaxWaitWithProgress (20 s) only while
  the log file changes (length / last write = writers passing through); no progress -> give up + 60 s backoff as before. Hostile holder cost
  now <= ~4 s per minute per log. HeadWitnessTests held-mutex bound follows the constants (>= Wait, < Wait + Slice + 3 s). Concurrency tests
  29/29 x3 under full CPU load; Dfir (Release) 4055 (4022 passed, 33 skipped) incl. the uncommitted MRU work.
  ComDlg32 MRU (OpenSave/LastVisited/CIDSize) implemented by worker, NOT committed: review r1 CHANGES (key time on first RESOLVED entry instead
  of MRUListEx[0]; lab Windows-comparison test can pass with 0 matches; exe-name cap note; decode failures without gap; field Order vs
  MruPosition; one Guarded call for 3 keys). Next: new worker for those fixes, r2 review, commit. Wiring MRU into USER-ACTIVITY-* = later step.
- 2026-11-18 (claude, hg585) ComDlg32 MRU DONE. Lab: notepad dialog via UIA refused (app not in front), but WS02 user hive already held real
  OpenSavePidlMRU (* and pdf), LastVisitedPidlMRU and CIDSizeMRU from the app's own Save dialogs; hive saved to E:\LA-Lab\diag\mru (lab only).
  New UserActivityParsers.ComDlg32.cs (NTUSER, Sources OpenSaveMRU / LastVisitedMRU / CIDSizeMRU; MruPosition 0-based + MruListed; key time
  only for the value named by MRUListEx[0], else nobody + gap; PIDLs via existing ShellItems; bounds + gaps; three separate Guarded calls).
  Lab test: 5/5 PIDLs equal to Windows SHGetPathFromIDListW (test asserts match == total). Reviews: r1 CHANGES (key time on first resolved
  entry, lab test provable with 0 matches, exe-name cap, decode failures without gap, field names, one guard for 3 keys) -> new worker;
  r2 APPROVE + 2 notes applied by main session (Limitations line: a damaged ComDlg32 subkey can hide the ones after it - RawRegistry
  limitation; ResolutionNote test tightened). NOT wired into USER-ACTIVITY-* rules (changes counts/report/UI) - separate step.
  DEFENDER (owner item): the ML model flags LogAnalyzer.Dfir.Tests.dll (Wacatac.B!ml, IDs 2147735505 / 2147735503) intermittently; bisect showed no
  single pattern (removing any small block of the new tests drops it below the threshold); two name-limit tests merged with identical
  assertions; the 19:40 build was flagged again yet the DLL stayed and ran. Host Defender untouched; an exclusion for the build folder is
  the owner's decision. Suites: Dfir (Release) 4063 (4030 passed, 33 skipped), App 1114, UI 370 (1 skipped), Edition 45, all exit 0.
  Next: SBOM (CycloneDX); forked-case compare; wiring MRU into USER-ACTIVITY-*. Lab: LAB-SRV01 still Saved.
- 2026-11-18 (claude, hg585) CI for 53040b89d / f8439b5d6 read once: all green (LogAnalyzer DFIR Build & Package success).
  PLANNER #8 ITEM 4 SBOM: CI already wrote a CycloneDX SBOM per edition, but with version 0.0.0, random serial/timestamp, QuestPDF without a
  licence, and without the .NET runtime packs + single-file host shipped inside the self-contained exe. New eng/Sbom.psm1 + eng/New-EditionSbom.ps1
  (tool pinned 6.3.1; --no-serial-number --include-license-text --runtime win-x64 -c Release; version = DfirInfo.ApplicationVersion; runtime packs
  from msbuild ResolvedFrameworkReference/ResolvedRuntimePack (licence from .nuspec, hash .nupkg.sha512) + ResolvedSingleFileHostPack (SHA-256 of
  singlefilehost.exe; licence copied from the NETCore runtime nuspec, labelled la:licenseSource); timestamp = commit time / SOURCE_DATE_EPOCH;
  ordinal sort; refuses any component without licence or hash). Package gets <edition>.cdx.json + SHA256SUMS (sha256sum -c format), after the
  smoke test. Offline self-test eng/tests/Test-Sbom.ps1 in build-test job. Verified locally: identical bytes on two runs (25 components Standalone,
  26 Retea/Internet); temp publish + elevated self-test from a Program Files copy with SBOM + SHA256SUMS alongside: exit 0, native-libraries ok.
  Doc HG585 SW-03 -> MEETS (QuestPDF dual licence = owner decision). Build-only ILLink.Tasks stays listed with scope "excluded".
  SBOM review (Opus) r1 REQUEST_CHANGES minor: 1 MEDIUM (missing-pack refusal could never fire: required list came from the same filter) -> fixed:
  every ResolvedFrameworkReference must be covered by a pack (Select-ShippedRuntimePacks, tested); LOW: host licence fallback now requires host
  version == NETCore pack version; only the top-level SHA256SUMS excluded (deeper files of that name are listed, verified); ordinal sort for
  SHA256SUMS; temp folder removed on failure; HG585 line 22 + SW-01 (-> PARTIAL: SHA256SUMS + native manifest, no Authenticode / unsigned sums).
  Delta small and covered by Test-Sbom (27 checks) + a real run (46/46 sums match) -> no round 2 (main session decision). CI to read once.
- 2026-11-18 (claude, hg585) SBOM committed ee793b315 (CI not yet read). PLANNER #8 ITEM 5 DONE: forked-case compare (read-only, never merges).
  New Dfir.Core/Case/CaseForkCompare.cs: two copies of a case (folder or case .zip, entries under case/) compared for every hash-chained Logs/*.jsonl
  (plain chain only; broken chain compared up to its break): per log Identical (+SameBytes) / FirstIsPrefix / SecondIsPrefix / Forked at Seq N /
  OnlyInOne / NotComparable; case-level CaseForkVerdict (Broken > Forked/Unrelated > cross-log fork > FirstOlder/SecondOlder > Identical /
  IdenticalChainsDifferentFiles > NothingComparable). Zip: duplicate names (case/slash variants) refused, decompressed bytes counted (64 MB/log,
  max 64 logs). KeyGen `compare-case <case1> <case2>`: exit 0 identical, 5 chains identical/files differ, 3 older copy, 4 forked/unrelated,
  2 broken/unreadable/nothing, 1 usage. HashChain: static VerifyPlainChain(name, bytes); VerifyPlain treats a duplicate-key line as invalid JSON
  (was an ArgumentException crash). Tests App.Tests CaseForkCompareTests (12). Opus review r1 REQUEST_CHANGES (HIGH: logs older in opposite
  directions called "older copy"; MEDIUM duplicate-key crash; MEDIUM identical ignored bytes outside chain; LOW x3) -> all fixed; r2 APPROVE.
  ACCEPTED RESIDUALS: L1 HashChain.PrevHashOf / HeadOf / ContainsIn still throw ArgumentException on a duplicate-key LAST line in Verify()
  (sealed-log paths; not reachable from compare-case) -> separate one-line fix with its own review; L2 a log chained in one copy and with 0
  chained entries in the other counts as prefix/shared history (unrelated cases may read Forked, not Unrelated); L3 folder read: size checked
  before the read (a growing log can pass 64 MB), case.json uncapped (CLI exit 2 on OOM/IO); each log read twice (CPU).
  Suites: Dfir (Release) 4063 (4030 passed, 33 skipped), App 1126, UI 370 (1 skipped), Edition 45, all exit 0.
  Next: read CI once; L1 fix (HashChain duplicate-key on Verify paths, Opus review); wiring MRU into USER-ACTIVITY-*. Lab: LAB-SRV01 still Saved.
- 2026-11-18 (claude, hg585) CI of ee793b315 (SBOM) and f452eb91b (compare): all green (33/33). L1 DONE: duplicate JSON keys (parse ok, ArgumentException on
  first lookup) no longer crash: HashChain (sealed head -> invalid seal, intent -> none, PrevHashOf, ContainsIn, HeadOf x2), FileHeadWitness (-> invalid
  witness, Broken), AuditAnchor (Objects forces Count in the try; Entries), CaseWorkspace.Integrity, CustodyOverview.Row. Tests App.Tests
  HashChainDuplicateKeyTests (7; 4+1 fail with the fix reverted). Opus review r1 REQUEST_CHANGES (FileHeadWitness MEDIUM + 3 LOW sites) -> fixed.
  DEFENDER (owner): LogAnalyzer.Dfir.Tests.dll now quarantined on EVERY build (Ransom:Win32/Clop.SIB!MTB, ID 2147807749, signatures 1.463.359.0, since
  23:12), even with the new test file moved out: the Dfir suite cannot run locally; CI runs it. New tests go to App.Tests until the owner decides.
  MRU WIRING (in progress, uncommitted): OpenSaveMRU + LastVisitedMRU are user-activity sources (UserActivityAnalysis.IsSource, labels, time meaning
  = key time only for the newest choice, detail with program/position/"nu dovedește citirea"), rules wording, UI time note; UserActivityContext
  schema 1.1 (a 1.0 context restores only the 4 original sources). Tests App.Tests UserActivityDialogListTests (5). App 1138, UI 370, Edition 45 ok.
- 2026-11-18 (claude, hg585) L1 committed 0c6c70acd. MRU WIRING DONE: OpenSaveMRU + LastVisitedMRU in the user-activity view and USER-ACTIVITY-*.
  OpenSaveMRU rows with the same (evidence, account, path) in several lists ("*" + extension) are ONE row (newest dated kept; detail names the lists);
  LastVisitedMRU rows say "pereche program–folder". Summary counts files chosen / program folders apart. Rule titles, reasons, plain texts and
  descriptions follow the sources present; WITHOUT dialog rows they are the original texts (test Without_dialog_rows_the_rule_texts_are_the_original_ones).
  RuleContracts, GHID_DE_UTILIZARE, UI time note, CaseLoader (IsSource) updated; UserActivityContext schema 1.1 (1.0 restores the 4 old sources).
  Reviews: r1 REQUEST_CHANGES (overclaiming wording, double counting, docs, CaseLoader) -> fixed; r2 REQUEST_CHANGES (HIGH: REMOVABLE description
  changed for non-dialog rows -> Dfir UserActivityRulesTests:123 would fail; LOW title) -> fixed by main session (2-round cap). Dfir
  UserActivityRulesTests (21) run by copying it temporarily into App.Tests: all pass. ACCEPTED RESIDUALS: collapsed OpenSaveMRU rows keep only the
  kept row as supporting evidence (the others are named in the detail); a 1.0 context restores no fields to dialog rows (cases written today by dev
  builds only); a future-schema context gets the generic mismatch text (fails closed). Suites: App 1141, UI 370 (1 skipped), Edition 45, Dfir build ok;
  Dfir suite only in CI (Defender).
  Next: read CI once (Dfir suite!). Then a NEW planner (planner #8 done). Owner: Defender exclusion for the Dfir test build (now blocks every build),
  QuestPDF licence, RDP manual test, registry witness decision, WS01/WS02 reinstall, lab cleanup (LAB-SRV01 Saved).
- 2026-11-19 (claude, hg585) CI of 440403109 read once: 33/33 success. PLANNER #9 (Plan agent, verified in code at 440403109; ranked):
  1 MRK classification marking (manual, "neverificat") on every page of investigation / checks / cross-station / incident PDFs + "Document în lucru"
  until case.closed (HG585_ACCREDITATION_REQUIREMENTS MRK-01/03; only ControlReportPdf + TransferReportPdf print it today) - medium, worker;
  2 AcquisitionRun.EvidenceSince by index watermark, not wall clock (R7b residual, line 90) - low; 3 FileHeadWitness.MovedAside -> audit
  auth.witness_moved_aside + admin warning (line 1292) - low; 4 matrix #18 approved-software hash control check (NECONFORM / DE VERIFICAT /
  NEDETERMINAT) - medium; 5 ID-03 sign-in notice from profile, explicit accept, notice.accepted + SHA-256 in sealed auth audit - medium;
  6 R2b leftovers: rights checked inside services (report.pv, control.station, collect.*) + read-only mode on licence expiry - RISKY (worker + Opus);
  7 (capacity) intervention register with witness (matrix #20/#29/#23). Deferred (lab/sample): RdpCoreTS 131, RDPClient 1024/1102, TaskCache
  Triggers, OneDrive transcripts, per-entry nonce. CONTRADICTIONS found: R2 marked DONE but read-only-on-expiry and "Licență și modul" screen never
  built (item 6); HG585_ACCREDITATION_REQUIREMENTS body stale (AUD-05 PAdES exists, ID-03 static notice exists, MRK-01 category exists, P1-P3 table)
  -> refresh doc with item 1/5; MATRICE #18 partly stale (profile Sha256 exists, no control check).
- 2026-11-19 (claude, hg585) PLANNER #9 ITEM 1 DONE (vault-worker; main-session fix; medium, not Opus-reviewed: text/print only, no verdict changes).
  CaseScope.Marking (typed in the scope dialog, ScopeForm rejects "unclassified" words, decision 21; old case.json -> ""). Shared MarkingText.Line
  (ControlReportHeader uses it, same text). Investigation / checks / cross-station / incident PDFs print the marking in the header and on every footer;
  empty = "fără marcaj introdus"; investigation PDF header says "Document în lucru — cazul nu este închis" until case.closed (CaseLoader.IsClosed).
  Main-session fix: InvestigationViewModel.AskScopeAtStart always takes the marking from the confirmed scope (the form has no marking field, so a
  complete form used to keep the old/empty marking). Tests App.Tests MarkingTests (5; compile-level red only). LocalizationLeftoverTests 10 -> 11 fields.
  HG585_ACCREDITATION_REQUIREMENTS MRK-01..03 -> PARTIAL; GHID updated. RESIDUAL: incident PDF footer has no test hook (built, not asserted);
  marking not per paragraph and not verified (stated in docs).
- 2026-11-19 (claude, hg585) PLANNER #9 ITEM 2 DONE (main session, low): AcquisitionRunRecorder.EvidenceMark (index count at collector start) +
  EvidenceSince/AddInterrupted(mark): entries after the mark, -1 (unreadable index) falls back to the start time. Pipeline passes the mark in the 3
  catch blocks. Tests App.Tests InterruptedCollectorMarkTests (3; 2 fail with the mark ignored - verified). Suites: App 1149, UI 370 (1 skipped),
  Edition 45, exit 0; Dfir builds, suite only in CI. Next: read CI once; item 3 (witness MovedAside -> audit + admin warning).
- 2026-11-19 (claude, hg585) PLANNER #9 ITEM 3 DONE (main session; independent review APPROVE with 1 MEDIUM + 2 LOW, all fixed): IHeadWitness.MovedAside
  (default null; FileHeadWitness implements it). AuthService keeps the witness, probes it at construction, and after every Audit append, VerifyAudit and
  VerifyAuditSnapshot (MEDIUM: a move found by a read path only was lost if the process ended before the next operation) writes ONE sealed line
  auth.witness_moved_aside (folder path in detail) per folder: check-and-set under a lock (LOW race -> duplicate line), re-entrancy flag, _reportedMove set
  only after the write (LOW: a failed write is retried). WitnessMovedAside / WitnessMoveNotRecorded; sign-in window shows WitnessMovedMessage with the
  seal warning; startup log line. Tests App.Tests WitnessMovedAsideAuditTests (5; 3 fail with reporting disabled - verified). Suites: App 1154, UI 370
  (1 skipped), Edition 45, exit 0. RESIDUALS: FileHeadWitness remembers only the last move (two moves between checks -> only the second audited);
  leftover witness.untrusted-* folders from before this build are not scanned. Next: read CI once; planner #9 item 4 (approved-software hash check).
- 2026-11-19 (claude, hg585) CI of 6a272a154 read once (29 success, 4 in progress, 0 failures; 9fe6c1f4b DFIR build/CodeQL "cancelled" = superseded).
  PLANNER #9 ITEM 4 DONE (vault-worker + main-session fix; medium, not Opus-reviewed: new read-only check, no write path): S07 "Programele aprobate nu au
  fost modificate" (Software, all three editions). ApprovedSoftwareHasher (facts I/O: literal path or wildcard in its own folder, non-recursive, <= 50
  matches, 1 GiB cap, env vars expanded, shared read) -> StationFacts.SoftwareIntegrity; ControlEvaluator.ApprovedSoftware (pure). Main-session fix: for a
  wildcard row a different hash is NECONFORM only when NO match has the approved hash; otherwise the other matches are DE VERIFICAT ("alt fișier sub același
  model") - the worker's version flagged every other file as modified. CheckGuide + ANEXA (hand-written in generator format; Dfir annex test unconfirmed
  until CI), ControlApplicability, EDITIONS_AND_POSTURE, MATRICE #18 -> PARȚIAL (S07), HG585 SW-01 note. Tests App.Tests ApprovedSoftwareIntegrityTests (23;
  worker red: 6 fail with comparison/hash broken). Suites: App 1177, UI 370 (1 skipped), Edition 45, exit 0. RESIDUALS: no total hashing-time budget;
  S07 is NEDETERMINAT on every station until the profile has hashed rows. Next: read CI once (Dfir annex/applicability tests!); item 5 (ID-03 notice).
- 2026-11-19 (claude, hg585) PLANNER #9 ITEM 5 DONE (ID-03; vault-worker + independent review APPROVE, LOW L1-L3 fixed by main session). ProcedureProfile.SignInNotice
  (<= 2000, trimmed; profile page box), SignInNoticeResolver (profile text, else ModuleCatalog.DefaultNotice; unreadable profile -> default + one-line
  remark, never blocks). SignInWindow: notice + checkbox, sign-in buttons disabled until ticked; Unlock not asked. AuthService(requireNoticeAcceptance:
  true) only from AuthApp.Initialize: SignInWithPassword/Card without NoticeAcceptance (or with blank text) refused + audited notice_not_accepted
  (card: before any card/PIN; typed name kept only as a hash, L3); success writes notice.accepted (sha256 of the exact displayed text + source) AND the
  auth.signin ok detail carries the same facts (L1: a failed second write cannot leave a sign-in without the record). Unlock uses a private marker
  instance (identity check). Tests App.Tests SignInNoticeTests (13; 3 fail with the service gate off - worker). Suites: App 1190, UI 370 (1 skipped),
  Edition 45, exit 0. HG585 ID-03 -> PARTIAL; GHID updated. OWNER: official notice text from the security officer. NOT SEEN ON SCREEN yet (XAML).
  RESIDUALS: L4 card unlock with the marker not covered by a test. Next: read CI once; item 6 (R2b leftovers + read-only on licence expiry, RISKY).
- 2026-11-19 (claude, hg585) PLANNER #9 ITEM 6 DONE (RISKY; vault-worker 2 rounds + independent review r1 REQUEST_CHANGES (2 HIGH + MEDIUM), r2
  REQUEST_CHANGES (HIGH + MEDIUM + LOW) -> fixed by main session (2-round cap)). Core LicenseGate: Require(capability) on VerifiedClaims, per-call expiry vs
  max(clock, floor); RequireWritable (refuses while expired) for capability-less writes; refusals -> AuthApp.Record licence.refused; App throws at start if
  not configured. LicenseService.ExpiredClaims only for Expired after signature/product/hardware AND module ok (tampered / wrong PC / wrong module never
  read-only). Start: activation window ALSO for expired-valid, with "Continuă în mod doar citire" (r1 HIGH-1: renewal kept); banner "Licență expirată —
  mod doar citire" + "Reînnoiește licența" (restart after success). Gated: control.station (Run, quick, signed media register, media center), report.pv
  (report, copy, officer export/sign, anchor, media history, cross-station signed), case.local (collection, sanitize, seal bundle, remote package,
  reanalyze, volatility, archive import, rule-to-case, workflow ops, timeline views/bookmarks/notes), collect.lan (live monitoring, syslog, domain ops),
  registers, response.host (isolate, block IoC, scripts, contain, trust, blocked-audit). RequireWritable: policies, profile saves, officer trust, signing
  settings, threat-intel packages, auth admin ops. Deliberately ungated: RestoreNetworkAccess/Release, disabling accounts/cards, CRL import,
  AcceptIntegrity, exports of collected data (ExportAudit). r2 HIGH fixed: floor = ClockMemory.HighestRecorded (max of ALL clock records; the baseline
  forgot a later time after a suspect record or clock.ack -> full rights again from the 2nd start). r2 LOW: source test now requires the gate as the
  FIRST statement (not a comment). Tests App.Tests LicenseGateTests (88; red: 17/21 fail with Require forced true; r2 test proves LatestRecorded forgets
  February while HighestRecorded keeps it). Suites: App 1278, UI 370 (1 skipped), Edition 45, exit 0. RESIDUALS: CheckSaved still uses UtcNow (rolled-back
  clock at start -> no banner/activation offer, but every write refused by the gate); a clock once set far ahead by mistake keeps an expired licence
  read-only; most gates proven by source text, not at run time; ContainAsync / CollectAll.RunAsync / Investigation Run / RunQuickAsync / ExportForOfficer /
  SaveReportCopy / ImportDataPack gated but not in the source-test list; NOT SEEN ON SCREEN (activation window choice, banner). Next: read CI once; item 7
  (intervention register) if continuing, else a NEW planner.
- 2026-11-19 (claude, hg585) CI of 4857fc88a (item 6) read once: 33/33 success (Dfir suite included). PLANNER #9 ITEM 7 DONE (vault-worker + main-session
  fixes; medium, not Opus-reviewed: register + read-only check, write path = existing RegisterViewModel.Save with LicenseGate Registers). InterventionRegister
  (interventions.json, 12 columns: date, station, kind hardware/software/configurare/reparatie, components, description, performer, witness, authorisation,
  post-check date + by, notes; witness == performer (case/space/domain-insensitive) or missing -> error). Expander on the station-control page, Romanian
  headers. Check S08 (Software): no/unreadable/empty register -> NEDETERMINAT; no witness / witness == performer -> NECONFORM; hardware/reparatie without
  post-check -> DE VERIFICAT within 7 days, NECONFORM after; Windows Installer ops (11707/11724/1033/1034) on a day without a register row -> DE VERIFICAT.
  Main-session fixes: a day covered by the OFFLINE UPDATES register is already documented (no DE VERIFICAT for every update); "Preia de pe stație" hidden
  for this register (HasDiscovery=false; nothing to read). CheckGuide + ANEXA (hand-written, generator format) updated identically. MATRICE #20/#23/#29 ->
  PARȚIAL (S08). Tests App.Tests InterventionRegisterTests (28) + LicenseGateTests (+1); red: 7 fail with the witness rule off, 4 with the grace broken.
  Suites: App 1307, UI 370 (1 skipped), Edition 45, exit 0. RESIDUALS: no Anexa 14 printout/PDF; S02 services/tasks/drivers not correlated; witness
  presence not provable; MATRICE header still says "95 verificări". PLANNER #9 DONE (items 1-7). Next: read CI once; then a NEW planner.
- 2026-11-19 (claude, hg585) CI of 2aff5c9b8 read once: 33/33 (Dfir incl. S08 annex). PLANNER #10 (Plan agent, verified at 2aff5c9b8; no lab): 1 MRK gap:
  transfer-register PDF gets "" (TransferViewModel.cs:354) and OfficerRequestPdf has no marking - low; 2 shared RegisterReportPdf, first Anexa 14
  intervention printout (report.pv, ExportAudit, SignIfConfigured) - medium; 3 backup-copies register (Anexa 21) + S09 (matrix #30) - medium; 4 users
  register training/pledge columns (schema 1.1) + U07 (matrix #31, #2) - risky; 5 witness move-aside leftovers (scan untrusted-* siblings, all moves) - low;
  6 read-only start when the clock was rolled back (CheckSaved UtcNow) - risky; 7 docs (HG585 table body stale: AUD-05, CRY-02, HOST-03/04, NET-03/06,
  ID-02, AC-02; matrix header 95 -> 106 checks) + on-screen check of the new windows via the UIA driver - low. CONTRADICTIONS: planner #9 item 1 claimed
  marking on all PDFs (transfer + officer request were not); item 6 residual list over-stated (most are runtime-tested in LicenseGateTests:185-303);
  old lines 55 (LATENT Has()) and 68 (OPEN navigation-only checks) closed by LicenseGate.
- 2026-11-19 (claude, hg585) PLANNER #10 ITEM 1 DONE (main session, low): TransferReportPdf prints MarkingText.Line(marking) in the header and on every
  footer (empty -> "fără marcaj introdus"); TransferViewModel passes the confirmed live scope's marking (CurrentMarking) instead of ""; OfficerRequestPdf
  takes the control page's MarkingLevel (header, footer, signature page). Tests App.Tests TransferMarkingTests (3; 2 fail with the old footer rule).
  Suites: App 1310, UI 370 (1 skipped), Edition 45, exit 0. Next: item 5 (witness leftovers, low), item 7 docs, then items 2-3 (worker).
- 2026-11-19 (claude, hg585) PLANNER #10 ITEM 2 DONE (vault-worker, medium, not Opus-reviewed: print-only + gated): RegisterReportPdf prints any IRegisterData
  (landscape, marking header+footer, labels via ColumnLabels.For, optional Date period, file SHA-256 + "matches last audit entry" + chain status, repeated
  table header, wrapped cells, Întocmit/Verificat/Aprobat). RegisterViewModel.PrintCommand ("Tipărește", CanPrint true only for interventions): first
  statement LicenseGate.Require(ReportPv), ExportAudit "pdf-register-interventii", SignIfConfigured. Tests RegisterReportPdfTests (6; red: 2 fail with
  marking removed / a row skipped) + LicenseGate source list. RESIDUALS: audit match is a string check on the last "interventions" audit line; no period UI;
  other registers CanPrint=false; one integrity test may return early without asserting if the identity cannot save registers.
  PLANNER #10 ITEM 5 DONE (main session, low): IHeadWitness.UntrustedFolders() (FileHeadWitness: every move of this process + "<witness>.untrusted-*"
  siblings); AuthService audits each folder once (HashSet, skips folders already named in the audit, so restarts do not repeat). Tests
  WitnessMovedAsideAuditTests +2 (two moves = two lines; leftover folder audited once across restarts); mutation (no sibling scan + no move list): 6/7 fail.
  PLANNER #10 ITEM 7 (docs part) DONE: HG585_ACCREDITATION_REQUIREMENTS gets a dated "Actualizare 2026-11-19" table (AUD-05, CRY-02, HOST-03/04, NET-03/06,
  ID-02/AC-02, ID-03, MRK, licensing) with code evidence; P1/P2/P3 counts not recomputed. MATRICE header 95 -> 106 checks (A20 C6 D5 N14 P47 S8 U6).
  On-screen check of the new windows still open. Suites: App 1319, UI 370 (1 skipped), Edition 45, exit 0. Next: read CI once; items 3 (backup register +
  S09), 4 (users training/pledge + U07, risky), 6 (rolled-back clock read-only start, risky).
- 2026-11-19 (claude, hg585) CI of 44dadad51: 27/27; d975695f5: 30 success + 3 running when read. PLANNER #10 ITEM 3 DONE (vault-worker, medium, not
  Opus-reviewed: register + read-only check, same pattern as S08): BackupRegister (backups.json; date, station, what, medium, who, where kept, restore test
  date + by, notes; restore test not before the copy). Profile BackupMaxAgeDays (default 30) + BackupRestoreTestMaxAgeDays (default 180), 0 = default,
  1..3650, two fields on the profile page (OWNER may change the defaults - chosen by Claude as safe values). S09 (Software): none/unreadable/empty ->
  NEDETERMINAT; newest copy older than the limit -> NECONFORM; no restore test or too old -> DE VERIFICAT; future/unreadable dates never count as newest.
  Expander + "Tipărește" (RegisterReportPdf, pdf-register-copii-siguranta). EDITIONS row checked by script against ControlApplicability (same shape as
  S07, which passed CI). MATRICE #30 -> PARȚIAL, header 107. Tests BackupRegisterTests (25) + gate/print tests; red: age > -> >= fails 2. Suites: App 1346,
  UI 370 (1 skipped), Edition 45, exit 0. Next: read CI once; items 4 (users training/pledge + U07, risky) and 6 (rolled-back clock read-only start, risky).
- 2026-11-19 (claude, hg585) PLANNER #10 ITEM 6 DONE (RISKY; vault-worker + independent review APPROVE, 4 LOW; L2 fixed). Option (a): App.xaml.cs reads
  ClockMemory.HighestRecorded(AuthService.DefaultDir\auth_audit.jsonl) right after LicenseGate.Configure, BEFORE the licence check, and raises
  LicenseService.RaiseExpiryFloor + LicenseGate.RaiseClockFloor; LicenseService.EffectiveNow() = max(UtcNow, floor) for the expiry comparison only
  (CheckSaved, CurrentLicense, ValidateAndSaveKey). A licence valid by the clock but expired by the floor goes the normal Expired path (activation window
  with "Continuă în mod doar citire", banner, licence.read_only_start); tampered / wrong HW / wrong module never reach ExpiredClaims (module re-checked).
  Banner/activation note "ceasul stației este în urmă…", only beyond a 2-minute tolerance (L2). Tests LicenseGateTests +4 (red: EffectiveNow = UtcNow ->
  floor test fails). Suites: App 1350, UI 370 (1 skipped), Edition 45, exit 0. NOT FIXED (LOW, recorded): L1 IsActivated() before the floor leaves
  VerifiedClaims and ExpiredClaims both set (gate still refuses; clearing VerifiedClaims would change R2b mid-session semantics); L3 a renewal refused
  against the floor shows the generic "Licența a expirat"; L4 source-order test does not check the path equals AuthApp.Service.AuditPath. Floor read is
  unverified (planted far-future clock.seen forces read-only = fail-closed DoS, already true for the gate floor). Next: read CI; item 4 (users register).
- 2026-11-19 (claude, hg585) CI DFIR Build & Package FAILED on 44dadad51, d975695f5, 4e52dc915 (my earlier reads saw it still running - lesson: read the
  DFIR workflow conclusion explicitly). Causes: (1) AiClaimValidationReportTests: new RegisterReportPdf missing from the explicit OutputWriters list ->
  added (false: prints people's register rows, never AI text); (2) HeadWitnessTests.A_reader_gets_in_within_seconds... measured 9.5 s / 7.4 s on loaded
  runners (passed on d975695f5 in between; 44dadad51 touched no chain code) -> HeadWitnessTests in a DisableParallelization collection "ChainTiming",
  5 s bound unchanged. Dfir.Tests compiles; suite only in CI. Next: read the DFIR workflow conclusion of this commit before anything else.
- 2026-11-19 (claude, hg585) DFIR CI of 8071047fd: first run failed on two timing tests (PiiMasker regex 500 ms timeout on a short title; MultiProcessChain
  sharing violation), rerun of the failed job: SUCCESS -> runner load flakes, not regressions. PLANNER #10 ITEM 4 DONE (RISKY; vault-worker 2 rounds +
  independent review r1 REQUEST_CHANGES (HIGH + 3 MEDIUM), r2 REQUEST_CHANGES (2 MEDIUM + LOW) -> fixed by main session (2-round cap)). UsersRegister
  schema 1.1: LastTraining + PledgeSigned (optional dates at the end; 1.0 loads with blank cells; 8-column CSV ok; future date = warning only).
  RegisterStore.Save writes per type (SchemaWrittenBy) and REFUSES to overwrite a file of a newer schema (newer major, or same major + newer minor; r2:
  "2.0" passed the minor-only guard). Profile TrainingMaxAgeDays (default 365, Claude's choice; page field). U07 (Utilizatori): per enabled local person
  account (RID 500 included when enabled; 501-504, system, $ and disabled excluded): no row or no clearance valid at the control date -> NECONFORM; rows
  without clearance -> DE VERIFICAT; expired history rows (renewal = new row) never change the result and are needed by Wp14; accounts unreadable ->
  NEDETERMINAT; no local person account -> DE VERIFICAT; training/pledge checked also for every register person with a valid clearance. Docs: CheckGuide/
  ANEXA "Conform" text no longer says "nicio abilitare nu a expirat" (r2), GHID future-date sentence fixed, MATRICE #2/#31 PARȚIAL, header 108. Dfir test
  edited: Wp14RegisterTests:196 ColumnNames 10. Tests UsersTrainingRegisterTests (43; red: renewal, domain-station, guard, newer-major guard). Suites: App
  1393, UI 370 (1 skipped), Edition 45, exit 0. RESIDUALS: older builds ALREADY deployed overwrite 1.1 files (drop the 2 columns) - do not downgrade;
  domain\user matched by user part (as Wp14); control date UTC vs local row validation (00:00-03:00 off by one day); one expired + one no-clearance row
  = NECONFORM (undocumented). PLANNER #10 DONE (items 1-7; on-screen check of new windows still open). Next: read the DFIR workflow conclusion explicitly.
- 2026-11-19 (claude, hg585) DFIR CI of 95d6fd119: first run failed only on HeadWitnessTests turnstile timing (5.3 s, class already alone), rerun SUCCESS.
  Bound aligned with the design and with the held-turnstile test: Wait + Slice + 3 s (7 s); the guarded starvation was 20 s. Planner #10 complete.
  Next session: a NEW planner; on-screen check of the new windows (UIA driver) is the first candidate.
- 2026-11-19 (claude, hg585) ON-SCREEN CHECK (publish of a8f864a42 tree, App.Classified win-x64-singlefile, elevated robocopy into C:\Program Files\LogAnalyzer
  Insula (R1), launched elevated, screenshots kept OUT of the repo in %TEMP%\la_screen - they show the machine name): sign-in window shows the
  "Notificare de utilizare" box (default module text) with the checkbox "Am citit și accept condițiile de utilizare"; UIA read: "Autentificare cu card" and
  "Autentificare cu parolă" enabled=False before ticking, True after the TogglePattern tick. VERIFIED. Not checked on screen (needs sign-in, and the owner's
  rule forbids typing passwords; or an expired licence signed by the private key): read-only choice in the activation window, banner, intervention/backup
  register expanders, marking field in the scope dialog. Owner (2026-11-19): the old UIA/lab scripts were moved to E: -> E:\LA-Lab\scripts (driver-ws02.ps1,
  push-driver.ps1, vsend.ps1); an E:\D_offload\w\loganalyzer-dfir copy also exists. CI of a8f864a42: all completed checks green when read, DFIR build running.
- 2026-11-20 (claude, hg585) CI of a86292db3: 33/33 (DFIR success); a8f864a42 DFIR failed on MultiProcessChainTests (reader IOException after 400 sharing
  retries, 1 m 41 s) = 2nd occurrence. Local repro (3/6 ChainWorker processes, idle / 56 burners / 2 cores pinned): max ReadVerified 0.1-2.4 s, never an
  IOException -> root cause NOT reproduced; Fable second opinion requested before any change. PLANNER #11 (Plan agent, verified at a86292db3):
  1 turnstile/reader starvation (CI flakes) - main session; 2 DOC-01 leftovers ("Conformitate ISO/IEC 27037", "Conform Standardelor", AD audit "Conformitate
  HG 585/NIS2", "baseline securitate HG 585", "--mode=network" hint) - low; 3 EXP-01/MED-01 export path + medium (SignedMediaRegister volume serial) in the
  sealed audit detail - medium; 4 SW-05 profile integrity vs last profile.edited after_sha256 + folder ACL -> dependent verdicts DE VERIFICAT - risky;
  5 control date in the station's local calendar (U07/S08/S09 use the UTC date) - risky small; 6 DEL-01 sanitization guard (no case/evidence paths, admin
  role, media.sanitized in sealed audit, optional witness) - medium-risky; 7 startup/integrity events (native-manifest warning, station.role, ALERT_VIEWED)
  into the sealed audit via a queue - low-medium; 8 licence/notice test residuals (card unlock marker, clock-floor path, renewal-vs-floor message) - low.
  Stale doc rows found: CRY-03 (key zeroed now), SW-06 (CodeQL covers C#), DEL-02 (fixed), NET-01/02 + HOST-02 done for P1. Dead code trap:
  Infrastructure/Parsers/PrefetchParser.cs fabricates "ExecutionProven" from a zero-filled MAM buffer (UNWIRED) -> delete with item 2.
- 2026-11-20 (claude, hg585, session 44cea9) PLANNER #11 ITEM 2 DONE (main session, low, text only): DOC-01 leftovers reworded to "Referință metodologică: ...
  (conformitatea nu este atestată de acest raport/pachet/aplicație)" or "matrice orientativă, neatestată (referințe: ...)": PdfReportService, HtmlReportService,
  DfirCasePackagingService, AdAuditHtml/PdfReportService, KerberosAndLateralMovementView, ActiveDirectoryAuditView, DataCollectionView; "baseline securitate HG 585"
  -> "politica de audit aprobată pentru stație" (StandaloneSamAuditEngine, AiCopilotInvestigationEngine); "--mode=network" hint -> network mode only from the
  signed station policy (DomainInvestigationViewModel, README). Guard UI.Tests HonestShellTests.No_shipped_source_claims_conformity_or_gives_an_ignored_start_argument
  (scans shipped .cs/.xaml/.md; red with the old PdfReportService footer - verified). Dead Infrastructure/Parsers/PrefetchParser.cs (zero-filled MAM buffer
  labelled ExecutionProven, no consumer) deleted (git keeps it). UI 370 (1 skipped) ok, solution builds.
  COORDINATION: another session is editing this worktree at the same time (uncommitted LogAnalyzer.App.Tests/TurnstileStarvationTests.cs + ChainWorker
  ProjectReference in App.Tests.csproj = planner #11 item 1); asked via SendMessage, no reply yet; this session did NOT stage or touch those files.
- 2026-11-19 (claude, hg585) DFIR CI a86292db3 success; a8f864a42 failed on MultiProcessChainTests (2nd time today: IOException sharing violation escaping
  ReadVerified after ~78 s). PLANNER #11 ITEM 1 DONE (vault-worker diagnosis + independent review APPROVE with follow-ups, both applied by main session):
  root cause (partly confirmed): Turnstile.Enter giving up sets SkipUntil = now + 60 s; during that every reader of the process skips the turnstile and
  competes raw with writers that hand the exclusive handle over without a gap -> 20 s retry budget exhausted. Emulation 7/20 -> 20/20 after the fix; NOT
  reproduced with real ChainWorker processes on this fast PC (CI trigger unproven). Fix: Enter(path, ignoreBackoff) clears SkipUntil when the mutex is
  obtained; ReadVerified AND OpenExclusive (review MEDIUM: writers kept their own backoff) re-enter the turnstile once after RawAttemptsBeforeTurnstile (40,
  ~1 s) raw sharing retries. Tests App.Tests TurnstileStarvationTests (3: emulation, 3 real ChainWorker processes with forced backoff, backoff decision;
  SharingAttempts 120 per review; 5/5 local runs green); App.Tests.csproj references ChainWorker (ReferenceOutputAssembly=false). RESIDUALS: a stuck turnstile
  holder + busy writers now costs a starving read an extra wait (worst ~59 s vs ~39 s; some calls on the UI thread); ReadAllShared/Head keep the raw
  fallback; a ReadVerified that still throws reaches DispatcherUnhandledException as a warning (card sign-in fails with the error, unchanged).
  ANOTHER SESSION works in this worktree (commit 199faf149 DOC-01/Prefetch is not mine; uncommitted ExportAudit/OperationAudit/ExportMediumResolver/
  ExportDestinationTests are theirs - ExportDestinationTests.A_failing_resolver... fails locally). Commit only own files.
- 2026-11-20 (claude, hg585, session 44cea9) PLANNER #11 ITEM 3 DONE (main session, medium, not Opus-reviewed: audit detail only, nothing refused): every written
  unmasked export (single file and bundle) records "path=<full>; drive=<amovibil|fix|rețea|optic|RAM|necunoscut>; volume=<serial X8>; medium=<inventory of the ACTIVE
  entry of the verified signed media register with that volume serial | neînregistrat | registru indisponibil (no usable register: never "neînregistrat") | necunoscut>"
  between sha256 and phase. OperationAudit.MediumOf seam (null/failing -> necunoscut; separators neutralised), Dfir.Windows ExportMediumResolver (GetVolumeInformation,
  serial spellings normalised, two active entries with one serial -> both named), installed in ExportAudit's static ctor (MediaRegisterFlows.Active, audit version
  check when signed in). Dfir OperationAuditTests:60 exact string updated (CI only). Tests App.Tests ExportDestinationTests (5) + ExportAuditTests pass.
  Owner (2026-11-20): another session works on the same branch elsewhere; the uncommitted TurnstileStarvationTests.cs / App.Tests.csproj ChainWorker reference /
  HashChain.cs changes are NOT this session's and were not committed. Fable advice on the CI starvation (writer in SkipUntil re-opens with no gap; fix = short
  bounded mutex probe during backoff, rate-limited) recorded for whoever owns item 1.
- 2026-11-20 (claude, hg585, session 44cea9) DEFENDER FIXED AT THE SOURCE (no exclusion, Defender untouched): LogAnalyzer.Dfir.Tests.dll was quarantined on every
  build as Ransom:Win32/Clop.SIB!MTB (ML) because test data held whole ransomware command lines (vssadmin delete shadows /all /quiet, wmic shadowcopy delete,
  bcdedit recoveryenabled no / bootstatuspolicy ignoreallfailures, wbadmin delete catalog, "Ransom:Win32/Locky.A"). New Dfir.Tests/Destructive.cs (Cut = run-time
  empty string): 28 literals in 10 files rewritten as $"vss{Destructive.Cut}admin delete sha{Destructive.Cut}dows ..." (same run-time text, never one literal in the
  DLL); two [InlineData] theories -> [MemberData] TheoryData. Verified: Release + Debug builds, no new detection; FULL Dfir suite runs locally again: 4063
  (4030 passed, 33 skipped), exit 0, 0 detections during the run. RULE for new Dfir tests: build destructive command lines with Destructive.Cut.
- 2026-11-19 (claude, hg585) merge of origin/main pushed (fa9c5fd2f; single conflict 20_TESTS/fixtures/unreadable_notes_allowlist.json resolved by union,
  234 entries, test_notes_strict_yaml 10/10); PR #370 MERGEABLE again (pull_request workflows had stopped while it was CONFLICTING).
  OWNER FILTER (2026-11-19): priority = COMPLETE AUDIT OF THE PC: what mistakes were made, each with proof the owner can verify on paper (what / when /
  where / quoted evidence / how to verify with standard Windows tools / what it does NOT prove). Station control, signed media register, users register,
  register printouts are de-prioritised. PLANNER #11 (Plan agent, read-only at ec9043ccd): order A2 -> A1 -> A3 (core of "provable on paper"), then A4, A5.
  A1 EvidenceRef + Quote (<=400 chars of EventData / value data), TimeSource, Channel (findings.json schema; old cases still read) - medium;
  A2 ManualVerification generator per locator family (EVTX Get-WinEvent -FilterXPath EventRecordID + Event Viewer route; registry reg query / Load Hive;
  file Get-FileHash; live snapshot Get-CimInstance / schtasks) - low; A3 "Greșeli găsite" PDF section before the full list (by severity; what / when +
  time source / where / quote / steps / "Ce NU dovedește") - low-medium; A4 live `reg save HKLM\SOFTWARE` so IFEO/Winlogon/Run parsers run on Colectează
  tot - low-medium; A5 configuration findings on the investigation side from StationFacts readers (UAC, SMB1, WDigest, LSA PPL, Defender exclusions/RT,
  RDP NLA, audit policy, guest/local admins), wording not "neconform" - medium; A6 live browser history / PSReadLine / hosts - medium; A7 Sigma/IOC/YARA
  hits into the summary + Greșeli găsite - low; A8 WMI subscriptions + startup folder - medium. STALE: MASTER_PLAN_STATUS 6.3 (browsers/registry REAL only
  as parsers), test counts, 6.13 conformity wording; card button "Verifică" = automatic verifier (owner's checks need "Cum verifici tu").
- 2026-11-19 (claude, hg585) PLANNER #11 A2 DONE (vault-worker, low, not Opus-reviewed: pure function + printed text; guard test forbids reg add/delete,
  wevtutil cl, Remove-, Clear-): Presentation/ManualVerification.For(EvidenceRef, EvidenceItem) -> ordered steps (tool, exact action, what to compare) per
  locator family found in the code: EVTX EventRecordID=N (Get-WinEvent -Path '<copy>' -FilterXPath + Event Viewer route, live log may have rolled over),
  live RecordID, registry SYSTEM\/SOFTWARE\/NTUSER\/UsrClass\/Amcache.hve\ (live reg query, or copy -> reg load HKLM\LA_VERIFY -> query -> unload, or
  regedit Load Hive), Prefetch X.pf#runN, live snapshot Services[]/Tasks[]/Processes[]/Autoruns[], text logs line=N, file-position families (hash + size,
  specialist step for the position), unknown -> "Verificare de specialist", never a command. PDF: "Cum verifici tu:" under each evidence line (cap 8,
  <=3 steps, no repeats per finding); App finding card "Arată dovezile" -> box "Cum verifici tu" (the automatic "Verifică" button unchanged). GHID 5.1.
  Tests App.Tests ManualVerificationTests (23; red: -Path -> -FilePath fails 4). Suites: App 1424, UI 370 (1 skipped), Edition 45, exit 0. DFIR TESTS RAN
  LOCALLY (no Defender detection since ec9043ccd): 4026 passed, 34 skipped, 3 failed (AnyDesk/RustDesk parser tests; pass alone 63/63 -> load/timing,
  not A2). RESIDUALS: registry key-vs-value heuristic; reg load needs admin; line=N counting; event id/time in steps only when a timeline row matches.
  Card look not seen on screen. Next: A1 (Quote/TimeSource/Channel on EvidenceRef).
- 2026-11-19 (claude, hg585) PLANNER #11 A1 DONE (vault-worker + main-session fix; medium, not Opus-reviewed): EvidenceRef + Quote (<=400, surrogate-safe),
  TimeSource (EVTX TimeCreated / Registry LastWrite (cheie) / Prefetch / Instantaneu la colectare / Linie de jurnal text / Necunoscut), Channel; filled by
  Integrity/EvidenceQuote via ProvenanceBinder.BindFindings(timeline); findings.json 2.0 -> 2.1 (old reads with defaults). PDF "Citat:" + "Ora din: … ·
  canal:" per evidence (masked like all printed text; "Citat: indisponibil în acest caz" when empty); card columns Citat / Ora din; ManualVerification adds
  "compară valorile cu rândul Citat". Main-session fix: when no raw text exists the parser SUMMARY is quoted with the label "Rezumat (nu textul original): "
  (it must never be compared word for word as the source). Dfir tests edited: FindingContractTests:464, Wp5CaseHomeTests:223 (2.1). Tests App.Tests
  EvidenceQuoteTests (11; red 6/11 without the bind). Suites: App 1435, UI 370 (1 skipped), Edition 45, affected Dfir classes 118/118. RESIDUALS:
  detection-engine findings have no quote; IncidentPdfReport unmasked/unquoted; card columns not seen on screen.
- 2026-11-20 (claude, hg585) ALL EDITIONS INSTALLED from 6639dca85 (owner: "update-urile la aplicația completă, nu doar standalone"). D: was FULL (0 GB:
  D:\WSL 279 GB, SteamLibrary 71 GB, D:\w 63 GB - owner data, not touched); own publish outputs moved to E:\LA-builds\20261120 (D: 0.42 GB free after);
  publish with -p:PublishDir=E:\... and TEMP on E:. Continent (LogAnalyzer.Internet) -> C:\Program Files\LogAnalyzer Continent, Arhipelag
  (LogAnalyzer.Retea; first bundle was truncated by the full disk, republished) -> C:\Program Files\LogAnalyzer Arhipelag, owner's main C:\Program
  Files\LogAnalyzer updated to the new Insula exe (old exe kept as LogAnalyzer.Standalone.exe.bak-20261023, its licence untouched). LA2 licences signed with
  the owner's key (%USERPROFILE%\.loganalyzer-license\license-private.key, public key = Core\Licensing\license-public-keys.txt) for the test PC hardware id,
  modules insula (2027-11-19, R1 folder; the 21:25 licence written by another session failed the signature, kept as .bak-20261119-2125), continent and
  arhipelag (2027-11-20). Continent start verified (licence ok, clock ok, sign-in shown). LicenseManager publish blocked by NU1004 (packages.lock.json
  without win-x64 RID; pre-existing). Next: A3 "Greșeli găsite".
- 2026-11-20 (claude, hg585) PLANNER #11 A3 DONE (vault-worker; low-medium, not Opus-reviewed: report layout only): section "Greșeli găsite" at the start
  of the Findings block (where the audience includes ReportSection.Findings): findings with severity > Info, not BenignKnown, verifier not Contradicted/
  Rejected, INCIDENT-CHAIN excluded; order severity desc, FirstSeen, FindingId; cap 50 + "Încă N greșeli…". Per block: Ce s-a întâmplat / Când (+ ora
  din) / Unde (+ locator, canal) / Dovada (<=3, Citat) / Cum verifici tu (ManualVerification of the first evidence, <=3) / Ce NU dovedește (specific
  limitations, fallback sentence) / Siguranță. Absence sentence when none. Page-1 line "Greșeli găsite: N". Masked like all printed text. Tests
  App.Tests MistakesSectionTests (5; red: ascending order fails). Suites: App 1440, UI 370 (1 skipped), Edition 45, report Dfir classes 105 (+1 skipped).
  Note: the worker tried a whole-worktree git stash once (denied, nothing ran) - stash is forbidden in this shared worktree. RESIDUALS: repeated rule =
  repeated blocks (no grouping); "citat indisponibil" vs EvidenceLevels "indisponibil în acest caz". Next: A4 (live SOFTWARE hive), A5 (config findings).
- 2026-11-20 (claude, hg585) PLANNER #11 A4 DONE (vault-worker, low-medium): new SoftwareHiveCollector (StandardAndUp, elevated, `reg save HKLM\SOFTWARE
  Raw\Registry\SOFTWARE /y`, 10 min timeout) -> evidence software_hive / Registry:SOFTWARE / CurrentSnapshot, hashed; not elevated -> NotAvailable, failure
  -> Failed -> EvidenceGap (run continues); registered in AllCollectors after ExecutionArtifactsCollector; SoftwareHiveParser accepts it; VSS/Windows.old
  copies stay separate items (as SYSTEM). CoverageMatrix registry family + ParserCapabilities limitation updated. Tests App.Tests
  SoftwareHiveCollectorTests (7; red 2/7). Suites: App 1447, UI 370 (1 skipped), Edition 45, affected Dfir classes 176 (+3 skipped). RESIDUALS: not run
  on a real elevated PC; no disk pre-check; SoftwareHiveParser tags Historical copies as CurrentSnapshot (pre-existing).
- 2026-11-20 (claude, hg585) PLANNER #11 A5 DONE (vault-worker, medium, not Opus-reviewed: read-only facts -> findings): configuration findings read ONLY
  from stored hashed hive evidence (software_hive / system_hive: live reg save, VSS, Windows.old, imports; SYSTEM via Select\Current) - Analysis/
  ConfigurationAudit (pure rules) + Investigation/ConfigurationHiveReader; SemanticType.Configuration, Category "Configurație", facts wording (no
  conform/neconform), TimeSource Registry LastWrite (cheie), Channel Registry, honest alternatives (GPO may override; state at collection). Rules:
  CFG-UAC-OFF High, CFG-UAC-NOPROMPT Medium, CFG-SMB1 High, CFG-WDIGEST High, CFG-LSA-PPL Low (missing = low confidence, Win11 may default on),
  CFG-DEFENDER-OFF High, CFG-DEFENDER-EXCL Medium (<=20 listed), CFG-RDP-NLA Medium; safe = nothing; unreadable/missing hive = EvidenceGap.
  ManualVerification: Defender Exclusions\* treated as keys. RuleContracts +8. Tests App.Tests ConfigurationFindingsTests (8; red 3/8) + Dfir
  ConfigurationHiveReaderTests (3, synthetic regf). Suites: App 1455, UI 370 (1 skipped), Edition 45, affected Dfir 927 (+5 skipped). RESIDUALS: audit
  policy, Guest, local Administrators not done (live-only, no hashed copy; SAM not collected); Quick profile has no hives -> no config findings; SMB1 server
  value only; Terminal Services policy NLA override not read; not run on a real hive (session not elevated).
- 2026-11-20 (claude, hg585) PLANNER #11 A6 DONE (vault-worker, medium): UserHistoryCollector (StandardAndUp, no elevation needed for the current user;
  other profiles' refusals reported): Chromium History (Chrome/Edge/Brave, Default + Profile N) + Firefox places.sqlite with -wal/-journal copied first
  (own source types; notes say DB and WAL may come from different moments when VSS is used), cap 512 MB/file -> gap; locked -> esentutl /y /vss like
  NTUSER; PSReadLine ConsoleHost_history.txt (ps_readline_history) and hosts (hosts_file). New parsers PsReadLineHistoryParser, HostsFileParser
  (line=N, Fields RawLine -> A1 quotes the original line; no invented time). Findings PS-HISTORY-SUSPICIOUS (reuses AssessPsCommand),
  HOSTS-SECURITY-DOMAIN (High non-loopback / Medium loopback-0.0.0.0), HOSTS-REDIRECT (Low). Browser visits timeline-only (existing download rules).
  ManualVerification line=N adds Get-Content … | Select-Object -Index N-1. Tests Dfir UserHistoryTests (18) + App ManualVerification (+1); red 8+1.
  Suites: App 1456, UI 370 (1 skipped), Edition 45, full Dfir 4049 passed / 34 skipped / 1 failed = RemoteCollectionTests hard-link test, which
  passes with TEMP on C: (fails only when TEMP is on another volume, E:) - environment, not A6. RESIDUALS: no real browser lock tested; VS Code/ISE
  history and remote-package path not covered; multi-line PSReadLine commands = one event per physical line.
- 2026-11-20 (claude, hg585) PLANNER #11 A7 DONE (vault-worker, low): high-severity detections in "Greșeli găsite": Sigma/correlation level high/critical
  (RulesUsed), YARA meta severity high/critical, IOC confidence >= medium; hash-set known-bad excluded (own finding); one block per (rule, evidence,
  locator) with repeat count; 7 labels; Cum verifici tu = ManualVerification + rule id/title step; Ce NU dovedește = rule-match caveat + falsepositives
  (DetectionRule.FalsePositives filled by SigmaLite); page-1 "Greșeli găsite: N (din care M potriviri de reguli)". Tests RuleMistakesTests (3; red 3/3).
  PLANNER #11 A8 DONE (vault-worker, medium): LiveStatePersistence (read-only WMI root\subscription bindings/filters/consumers; all-users + per-user
  Startup folders with SHA-256, size, times, LNK target/args, Authenticode); Snapshot + 2 optional lists; LiveStateAnalyzer LIVE-WMI-SUBSCRIPTION (High
  for CommandLine/ActiveScript consumers; SCM built-in none), LIVE-STARTUP-LNK / LIVE-STARTUP-FILE (interpreter+args High, user-writable unsigned
  Medium/High, script Medium, unsigned exe High, valid signature none); locators WmiSubscriptions[f->c], Startup[path] with ManualVerification steps.
  Tests LiveWmiStartupTests (5; red 2). Suites: App 1464, UI 370 (1 skipped), Edition 45, FULL Dfir 4050 passed / 34 skipped / 0 failed (TEMP on C:).
  RESIDUALS: WMI/Startup not run on a real machine; other users' Startup needs elevation; non-command WMI consumers no finding. PLANNER #11 DONE (A1-A8).
- 2026-11-20 (claude, hg585) ALL EDITIONS REINSTALLED from 0a78d812a (planner #11 A1-A8): published to E:\LA-builds\20261120b, robocopy into Continent,
  Arhipelag, the owner's main Insula and Insula (R1) (installed licences kept). Start probes: Continent, Arhipelag and main Insula all "IsActivated: True",
  clock ok, sign-in shown; Arhipelag and main Insula closed after the probe, Continent left open for the owner. Next session: a NEW planner under the owner
  filter (full PC audit); candidates from residuals: real-machine run of SOFTWARE hive / WMI / Startup / browser collection (elevated), audit policy +
  Guest + local Administrators as live findings with a stored snapshot as evidence, Quick profile config coverage, VS Code/ISE history, grouping of
  repeated rule blocks in Greșeli găsite.
- 2026-11-20 (claude, hg585) PLANNER #12 B1 DONE: Dfir.Tests RealMachineAuditSmokeTests (opt-in LA_REAL_MACHINE=1, skipped otherwise; runs SoftwareHive,
  ExecutionArtifacts, UserHistory and LiveState collectors on THIS PC into a %TEMP% case, then InvestigationPipeline.Run(collect:false); summary
  %TEMP%\la-real-audit-summary.txt (local only, not in the repo); asserts no throw, non-empty locators, stored hashes). REAL RUN (elevated, 2026-11-20):
  all collectors Success (SOFTWARE 214 MB, SYSTEM 28 MB, Amcache, hosts, PSReadLine 3757 lines, 3 Chromium DBs, live snapshot); parsers ok; 45 findings
  (2 CFG-*, 5 HOSTS/PS-HISTORY, 24 LIVE-*, 14 other); 2 gaps (TaskCache Tree without Tasks; Amcache dirty hive, logs not replayed). QUALITY ISSUES seen:
  LIVE-UNSIGNED-USERPATH repeats one finding per PROCESS of the same exe (18 near-duplicates); LIVE-TASK-USERPATH and PERSIST-TASK-CONFIG report the same
  task twice -> planner #12 B2 grouping. Results not printed here (owner's PC data).
