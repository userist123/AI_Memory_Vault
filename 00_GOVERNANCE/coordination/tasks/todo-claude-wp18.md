# todo-claude-wp18
STATUS: IN_PROGRESS        UPDATED: 2026-10-10T04:10Z
TASK: WP18 — station roles (CONTROL / CSIRT chosen by the PC through the signed policy) and non-technical UI, per
`02_PRODUCT/projects/workspaces/loganalyzer-dfir/docs/dfir/PROMPT_WP18_ROLURI_STATIE_UI_SIMPLA.md` (owner decisions D1–D8 in §11).
BRANCH / PR: `claude/loganalyzer-dfir-roles-ui-0a71fb` (worktree). One commit per step; PR at the end of the package.
DONE:
- S1 station role: `LogAnalyzer.Core/Services/Edition/StationRole.cs` (enum, decision, resolver, context, StartupDecision);
  `role` as optional last signed line of `LogAnalyzer.policy` (`EditionPolicy.cs`); both `EditionComposition.DecideStartup`;
  startup log + `station.role` audit + auth audit `session.context`; `CaseInfo.StationRole` written by `NewCase` and the LIVE case;
  header badge `ROL:` with the decision as tooltip (`MainViewModel`, `MainWindow.xaml`); `Sign-EditionPolicy.ps1 -Role`.
  Tests: `LogAnalyzer.UI.Tests/StationRoleTests.cs` (9). Docs: `docs/dfir/EDITIONS.md` "Station role (WP18)".
- S2 Home + navigation per role: `LogAnalyzer.Core/Services/Edition/RoleProfile.cs` (HomeIntent, IntentAvailability, NavigationEntry,
  RoleProfile, RoleProfiles.For — data, ≤5 primary intents, unavailable ones listed with the reason); `HomeViewModel` renders the profile
  (`PrimaryIntents`, `MoreIntents`, `RunIntentCommand`); `HomeView.xaml` big buttons + "Mai multe"; sidebar section of the role +
  "Avansat (toate paginile)" expander with every legacy page (decision 5); the app opens on Home in every mode (Live SOC jump removed).
  Tests: `LogAnalyzer.UI.Tests/RoleProfileTests.cs` (8), `HomeViewModelTests` (+2, one updated). App.Tests 30/30, UI.Tests 187/187.
- S3 language levels, terms, glossary: `LogAnalyzer.Dfir.Core/Language/LanguageLevel.cs` (UiLanguageLevel Simple/Expert, LanguageLevelContext,
  LanguagePreferences per account under %LOCALAPPDATA%\LogAnalyzer\preferences, default Simple for EVERY account); 11 control-line terms added
  to the single `Glossary` (Hardware ID, NetworkList, USBSTOR, auditpol, RecordID, CRL, CA, SHA-256, SRUM, RDP, GPO); `TermExtension` now binds
  to the level (Simple = human phrase, Expert = technical name beside it) and recomputes on change; header switch "LIMBAJ: Simplu/Expert" +
  "Ce înseamnă?" opening `GlossaryWindow` (searchable); role pages reworded so technical names appear only in parentheses (StationControl,
  Investigation, ProcedureProfile, Auth, FindingCard); administration buttons in user words ("Adaugă persoana", "Înregistrează cardul",
  "Adu lista certificatelor revocate (CRL)…"). Lint test over the role pages: `LogAnalyzer.App.Tests/Wp18LanguageTests.cs` (15 tests incl. theory).
  App.Tests 45/45, UI.Tests 187/187; Dfir.Tests Wp6a glossary/XAML tests green after the SRUM entry (example family → shimcache).
- S4 guided flows: `LogAnalyzer.Dfir.Core/Flow/GuidedFlow.cs` (steps, Next with validation, Back, Stop/Resume keep the place, Restart);
  `LogAnalyzer.Dfir.Windows/Audit/ControlGuide.cs` (period choices incl. "de la ultimul control", ControlArchive reading Control/CONTROL_*/control_report.json,
  ControlComparison = difference worse/better/new/removed, ProfileSummary sections defined/nedefinit, ControlResultScreen = the five answers,
  "nimic neconform găsit" always with the coverage, ≤5 next steps); `LogAnalyzer.Dfir.Windows/Investigation/IncomingEvidence.cs` (scan of a folder
  brought from another PC: families present/missing, files not evidence; nothing copied or hashed before import). `StationControlViewModel` +
  `StationControlView.xaml`: three-step "Verifică această stație" (station → period → procedures/inspector → run), single result card, "Compară cu
  controlul anterior", raw parameters under "Avansat". `InvestigationViewModel` + `InvestigationView.xaml`: three-step "Primește probe de la o stație"
  (folder → what was found/what is missing → scope → run). Tests: `LogAnalyzer.App.Tests/Wp18FlowTests.cs` (7). App.Tests 52/52; Wp6a XAML tests green.
VERIFICATION (S1, local, Windows):
- `dotnet build LogAnalyzer.slnx -c Release`: Build succeeded (TEST_VERIFIED).
- App.Tests 28/28, Edition.Tests 15/15, UI.Tests 179/179 passed.
- Dfir.Tests 1045 passed, 4 skipped, 2 FAILED on the real local corpus: `AntiForensicsTests.Real_corpus_traces_match_wevtutil…` (28 vs 64)
  and `InvestigationTests.NanAgent_corpus_yields_the_known_incident_chain` (chain text lacks NanAgent32.exe). Neither test touches WP18 code
  (parsers / AntiForensics.Evaluate / incident chain); treated as PRE-EXISTING local corpus drift, NOT fixed here. CI never runs them (no corpus).
- Windows Defender flags `LogAnalyzer.Dfir.Tests.dll` as `Ransom:Win32/Clop.SIB!MTB` on every rebuild (test data contains the literal
  `vssadmin delete shadows /all /quiet` lines of WP11 tests). False positive on the test assembly; owner restored the file from quarantine.
- `Sign-EditionPolicy.ps1` change: parsed OK with Windows PowerShell; end-to-end run UNVERIFIED here (needs pwsh 7, not installed).
NEXT: S2 Home + navigation per role → S3 language levels/terms/glossary → S4 guided flows → S5 reports → S6 accessibility → S7 docs.
BLOCKERS: none.
KEY FILES: see DONE; prompt §2 reading list.
