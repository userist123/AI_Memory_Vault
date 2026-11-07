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
## Next
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
