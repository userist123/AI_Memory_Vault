# todo-claude-wp-ed
STATUS: IN_PROGRESS        UPDATED: 2026-10-08
TASK: LogAnalyzer WP-ED (edition split P1 classified / P2-P3 unclassified), then WP-PKG (self-contained, AI-independent, Windows tooling compat)
BRANCH / PR: PR1 #234 loganalyzer/wp-ed-editions -> main (open, CI running). PR2 loganalyzer/wp-pkg-selfcontained (stacked on PR1) / not opened yet.
SPEC: origin/claude/wonderful-bohr-iifqfn: 02_PRODUCT/projects/workspaces/loganalyzer-dfir/docs/dfir/{CONTRACT_AUDIT_STAGE1.md (decisions 13-26),WINDOWS_TOOLING_COMPAT.md (Part 3)}
DONE:
- PR1 (WP-ED): assemblies Connectors/Response/Ai, Core Edition contracts, signed policy, P1 app project, Edition.Tests, EDITIONS.md, CI step. Merged main into it (d040e7e16).
- PR2 (WP-PKG): pubxml for both editions; --self-test (Dfir.Windows/Investigation/SelfTest.cs, wired in App.xaml.cs, shared by P1); CI package matrix + smoke test with no .NET and outbound blocked; AiIndependenceTests (3 pass); AuditCollector.ps1 (5.1, wmic-free) shipped via Response csproj; Owner path dropped and PowerShell started by full path; netsh -> COM (ComFirewallRunner, netsh kept as explicit fallback); auditpol read -> AuditQuerySystemPolicy; wevtutil -> EvtExportLog P/Invoke (not EventLogSession: banned in P1); absent channels / Prefetch reported "indisponibil pe acest sistem"; docs/dfir/SUPPORTED_WINDOWS.md with lab checklist.
NEXT (in order):
1. Open PR2 (base wp-ed-editions), wait CI (Windows build-test + package matrix), fix failures
2. Not done (list in PR2 body): esentutl/reg save/MpCmdRun replacement (P2), C# port of audit collector modules (P2), WMI->Win32 (P3), Exchange UI marker (P3), ActivitiesCache/Timeline coverage row, auditpol /set still a process (unclassified only), lab runs on real Windows versions
BLOCKERS / OWNER QUESTIONS:
- Local loopback AI in P1: excluded by default; opt-in -p:ClassifiedIncludeLocalAi=true (owner approval)
- Policy public key: none committed; owner must generate and embed it (else app stays air-gapped)
- Supported Windows list in SUPPORTED_WINDOWS.md is a proposal (owner decides)
KEY FILES: LogAnalyzer.Dfir.Windows/{Investigation/SelfTest.cs,Native/EventLogExport.cs,Acquisition/Collectors.cs}, LogAnalyzer.Response/{Defense/ComFirewallRunner.cs,Collection/PowerShellAuditCollector.cs,Scripts/AuditCollector.ps1}, .github/workflows/loganalyzer-dfir-build.yml
VERIFICATION SO FAR: Linux (EnableWindowsTargeting): UI.Tests DefenseActionVerification 16/16, Dfir.Tests AiIndependence 3/3; builds OK. Full local suites pending before final push.
