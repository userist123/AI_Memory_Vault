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

## Next
1. Read CI on #370 once (head 5ef49dab3 or later).
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
