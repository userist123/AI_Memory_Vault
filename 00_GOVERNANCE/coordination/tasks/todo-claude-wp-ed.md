# todo-claude-wp-ed
STATUS: IN_PROGRESS        UPDATED: 2026-10-08
TASK: LogAnalyzer WP-ED (edition split P1 classified / P2-P3 unclassified), then WP-PKG (self-contained, AI-independent, Windows tooling compat)
BRANCH / PR: loganalyzer/wp-ed-editions / none yet (PR2 branch: loganalyzer/wp-pkg-selfcontained)    BASE: origin/main
SPEC: origin/claude/wonderful-bohr-iifqfn: tasks/loganalyzer/CONTRACT_AUDIT_STAGE1.md (section 8, decisions 13-15), tasks/todo.md, tasks/loganalyzer/WINDOWS_TOOLING_COMPAT.md (Part 3), HG585 Part 2
DONE:
- New assemblies LogAnalyzer.Connectors (network), .Response (host-modifying), .Ai (EvidenceReasoner/AiCaseAnalysis); files moved, namespaces kept
- Core contracts: Services/Edition/{EditionProfile,EditionPolicy,UnavailableServices}.cs, Interfaces/{IHostDefense,ILiveEventSource,IAuditCollectionService}.cs
- Signed policy (ECDSA P-256, same scheme as release-gate waivers); OperatingModeResolver can no longer select Network
- UI: Edition/Unclassified (Containment, DomainInvestigation, AiAnalysis VMs/views, UnclassifiedComposition), FeaturePlaceholder, MainViewModel via DI
- LogAnalyzer.App.Classified project; Edition.Tests (5 pass); policy tests; EDITIONS.md; CI step; Sign-EditionPolicy.ps1; local failing set == base (56)
NEXT (in order):
1. PR1 opened? wait Windows CI, fix failures (use get_job_logs)
2. WP-PKG on branch loganalyzer/wp-pkg-selfcontained (stacked on PR1): pubxml for both editions, --self-test CLI, CI publish+smoke, AI-independence test, AuditCollector.ps1 shipped (5.1, no wmic), drop Owner path, full-path powershell, netsh->INetFwPolicy2, auditpol->API, wevtutil->ExportLog, coverage "indisponibil pe acest sistem", docs SUPPORTED_WINDOWS.md + lab checklist
BLOCKERS / OWNER QUESTIONS:
- Local loopback AI in P1: excluded by default; opt-in -p:ClassifiedIncludeLocalAi=true (owner approval)
- Policy public key: none committed; owner must generate and embed it (else app stays air-gapped)
KEY FILES: 02_PRODUCT/projects/workspaces/loganalyzer-dfir/{LogAnalyzer.App.Classified,LogAnalyzer.App/Edition,LogAnalyzer.Core/Services/Edition}
VERIFICATION SO FAR: base = 56 Linux-only failures (Dfir.Tests 50, UI.Tests 6); App.Tests cannot run on Linux. After split: failing set identical to base; Edition.Tests 5/5 (also with -p:ClassifiedIncludeLocalAi=true).
