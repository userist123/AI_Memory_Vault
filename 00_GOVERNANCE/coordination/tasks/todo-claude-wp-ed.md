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
- LogAnalyzer.App.Classified project (links App UI sources, no Connectors/Response/Ai refs); App builds
NEXT (in order):
1. Move JoinConverter/ClassificationWordingConverter out of Edition/Unclassified/ProcessContainmentView.xaml.cs into a shared file; get App.Classified to build
2. Fix tests (UI.Tests, Dfir.Tests, App.Tests: references, AuditCollectionService -> UdpSyslogReceiver, DefenseActionResult using, OperatingModeTests new semantics, AI assertions moved to AiAnalysisViewModel); add policy tests
3. LogAnalyzer.Edition.Tests: inspect P1 output (assembly refs, types, P/Invoke, strings)
4. CI workflow (build both, publish), docs/dfir/EDITIONS.md, allowlist, README CI table, tools to sign policy (ps1)
5. Compare failing-test set vs base (scratchpad wped/base-failed.txt: 56), push, open PR1, wait CI
6. WP-PKG (PR2) per task
BLOCKERS / OWNER QUESTIONS:
- Local loopback AI in P1: excluded by default; opt-in -p:ClassifiedIncludeLocalAi=true (owner approval)
- Policy public key: none committed; owner must generate and embed it (else app stays air-gapped)
KEY FILES: 02_PRODUCT/projects/workspaces/loganalyzer-dfir/{LogAnalyzer.App.Classified,LogAnalyzer.App/Edition,LogAnalyzer.Core/Services/Edition}
VERIFICATION SO FAR: base = 56 Linux-only failures (Dfir.Tests 50, UI.Tests 6); App.Tests cannot run on Linux. Nothing re-run yet after the split.
