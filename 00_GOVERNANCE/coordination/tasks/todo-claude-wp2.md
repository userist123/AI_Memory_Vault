# todo-claude-wp2
STATUS: IN_PROGRESS        UPDATED: 2026-10-08 (merged origin/main; PR opening, CI pending)
TASK: LogAnalyzer stage 2 WP2 - finding contract, standard states, versioned outputs, raw time (+ lessons-learned vocabulary)
BRANCH / PR: loganalyzer/wp2-finding-contract / opened (see GitHub)    BASE: origin/main
SPEC: 02_PRODUCT/projects/workspaces/loganalyzer-dfir/docs/dfir/CONTRACT_AUDIT_STAGE1.md (s7 WP2, s8 decision 2), FINDING_CONTRACT.md
DONE:
- Core: SemanticType/StandardState/OperationState/ParserHealth/SourceAvailability, Finding + TimelineEvent fields
- FindingContract.Enrich, RuleContracts catalog (31 rules), AntiOverclaim, ParserCapabilities (16 parsers), TimeFacts, TimeReliability
- Pipeline wiring, timeline.csv raw time, schema versions + readers, run_state.json, PDF/VM/XAML bindings, VaultExport meta
- Tests: Dfir FindingContractTests (71 pass), UI LegacyVocabularyTests; FINDING_CONTRACT.md
NEXT (in order):
1. open PR (base main), wait for Windows CI loganalyzer-dfir-build.yml, fix failures
BLOCKERS / OWNER QUESTIONS:
- bare-array JSON files kept as arrays (versioned via schema_manifest.json) to not break consumers: confirm
KEY FILES:
- LogAnalyzer.Dfir.Core/Analysis/{FindingContract,RuleContracts,AntiOverclaim,TimeFacts,ParserCapabilities,OperationStates}.cs
- LogAnalyzer.Dfir.Core/Model/{Analysis,FindingContract}.cs, IO/SchemaVersions.cs
- LogAnalyzer.Dfir.Windows/Investigation/InvestigationPipeline.cs
VERIFICATION SO FAR: FindingContractTests 71/71, LegacyVocabularyTests 2/2 on Linux; full suites and CI pending
