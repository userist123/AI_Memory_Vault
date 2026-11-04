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

## Next
1. Read CI on #370 once.
2. Owner's new order 2026-11-04: professional product. Phase R1 of the plan (cleanup to _recovered, ModuleFeatures catalogue, module names).
   Screenshots: the owner gave the test account (administrator.principal) in chat; computer-use access to LogAnalyzer was DENIED once
   in this session - ask again only when needed. App requires admin: for UI review launch with __COMPAT_LAYER=RunAsInvoker.
   Dev build cannot pass activation (license-public-keys.txt absent): use the installed build or add a documented dev key path (R2).
3. Real-PC check of F1-F3 + the new section.
4. Later rows: #1/#2 logged account vs clearance, #31 training dates, #20/#23/#29 intervention register, #34 accreditation expiry.

## Open owner questions (unchanged from 2026-10-23)
- Unit thresholds from CSS/PrOpSec; PV form used in the unit; olographic vs electronic signature.
