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

## Next
1. UI (vault-worker): profile level picker; control VM sets facts.StationLevel, builds the plan, editable grid, GenerateReport refuses with
   Missing() lines, passes actions + comparison to SaveToCase. Then Opus vault-reviewer.
2. Full suites (TEMP/TMP on D:\w\tmp\la-f1f3), push, NEW draft PR (replaces #329), CI read once.
3. Real-PC check of F1-F3 + the new section (needs the owner's sign-in: the agent does not type the password).
4. Later rows: #1/#2 logged account vs clearance, #31 training dates, #20/#23/#29 intervention register, #34 accreditation expiry.

## Open owner questions (unchanged from 2026-10-23)
- Unit thresholds from CSS/PrOpSec; PV form used in the unit; olographic vs electronic signature.
