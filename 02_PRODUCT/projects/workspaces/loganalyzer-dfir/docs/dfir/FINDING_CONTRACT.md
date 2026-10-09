# LogAnalyzer - Finding contract (stage 2, WP2)

Implements program requirements §5, §6, §19, §20 and UX contract §7, §19, §20 additively. Nothing was removed:
`Finding.Classification`, `ExecutionProven` (legacy), existing exports and consumers work as before. Code: `LogAnalyzer.Dfir.Core`
(`Model/FindingContract.cs`, `Analysis/FindingContract.cs`, `RuleContracts.cs`, `AntiOverclaim.cs`, `ParserCapabilities.cs`,
`TimeFacts.cs`, `OperationStates.cs`, `IO/SchemaVersions.cs`). Tests: `FindingContractTests`, `LegacyVocabularyTests`.

## Fields added to `Finding`

| Contract (UX §19) | Field | Filled by |
|---|---|---|
| `semantic_type` | `SemanticType` (OBSERVATION, PRESENCE, EXECUTION, CONFIGURATION, CORRELATION, INFERENCE, ATTRIBUTION) | producer (EXEC-USERPATH) or rule catalog |
| `status` | `Status` (10 states, §20) | derived from `Classification`, then `AntiOverclaim.Constrain` |
| `verification` | `Verification` (State, Reason, Verifier, AssessedUtc) | NOT_ASSESSED with reason, until the verifier (WP4) |
| `limitations` | `Limitations[]` | rule catalog, semantic type, parser `CannotProve`, "contradictions not evaluated", "verification not run" |
| `contradictions` | `ContradictingEvidence` (alias `Contradictions`) | rules that test it (below) + chain inheritance |
| `missing_evidence` | `MissingEvidence` | producer, else the rule's default |
| `recommended_actions` | `RecommendedNextSteps` | producer, else the rule's default |
| Provenance | `Provenance` (rule, version, producer, app version, evidence ids + SHA-256, parsers) | pipeline |
| `audit` | `AuditTrail[]` (rule.fired, classification.mapped, contradiction.check, verification.default, anti_overclaim.check, confidence.lowered) | pipeline |
| `title_key`, `summary_key` | `TitleKey`, `SummaryKey` = `finding.<rule_id>.title/.summary` | pipeline |
| HumanSummary / TechnicalSummary | same names | pipeline |
| - | `ContractVersion` ("" = contract not applied) | pipeline |

`TimelineEvent` gains `SemanticType`, `TimeZoneBasis`, `TimeUncertainty`. Defaults are honest: a finding the contract was not applied to
reads Status NOT_ASSESSED, Verification NOT_ASSESSED, `ContractVersion` "". Severity and Confidence stay separate (HIGH + LOW is valid).

## Classification -> Status (deterministic)

| Classification | Status | Why |
|---|---|---|
| Direct | OBSERVED | one record shows it |
| Correlated | CORRELATED | independent sources agree; never SUPPORTED/VERIFIED |
| Candidate | INFERRED | worth a look, not proven |
| Unproven | UNPROVEN | |
| BenignKnown | OBSERVED | observed; "benign" is not verified |

Mapping never yields SUPPORTED, VERIFIED, CONTRADICTED or REJECTED: those are set by the verifier (WP4). A finding qualified by a
contradiction (e.g. a disabled task) keeps its status and lists the contradiction. Legacy `EvidenceStrength` (unwired): ExecutionProven ->
EXECUTION/OBSERVED only for Prefetch, BAM, UserAssist, else PRESENCE/OBSERVED (Amcache is presence); ExecutionPossible -> INFERENCE/INFERRED;
FileExistenceOnly -> PRESENCE; ConfigurationOnly -> CONFIGURATION; ContextOnly -> OBSERVATION.

## Anti-overclaim invariants (`AntiOverclaim`, tested)

Artifact/presence never becomes execution or user action; correlation never becomes proof; nothing maps to VERIFIED; AI output never exceeds
INFERRED; no findings with gaps (or without) is UNKNOWN, never "clean"; a configured policy is NOT_ASSESSED, not "effective". An unreliable
timestamp (missing, ambiguous local time, clock change nearby) lowers Confidence one level, never Severity.

## Contradictions computed today

Disabled scheduled task (`PERSIST-TASK-CONFIG`), disabled service (`PERSIST-SERVICE-CONFIG`, `LIVE-SERVICE-USERPATH`), and incident chains inherit
their steps' contradictions. Every other rule says in `Limitations` that it does not evaluate contradicting evidence.

## Timeline and versioned outputs

`timeline.csv` appends `TimeRaw`, `TimeConversion`, `TimeZoneBasis`, `TimeUncertainty`, `SemanticType` (existing columns unchanged).
`findings.json` and `graph.json` carry `"schema_version": "2.0"`, `vault_proposals.jsonl` carries it in each proposal's JSON block
(the line itself stays exactly `memory_propose` arguments). Files that remain bare arrays (`parsers.json` 1.1, `parsing.json`, `detections.json`,
`rules.json`, `anti_forensics.json`) are versioned in `Analysis/schema_manifest.json`. `run_state.json` holds the operation state. Readers
(`FindingsFile.Read`, `SchemaManifest.Read`, `VaultExport.Read`) accept files without a version as "1.0" and refuse a newer major.

## Operation state vs verification

`InvestigationResult.State` (NOT_STARTED, RUNNING, COMPLETED, PARTIAL, FAILED, CANCELLED, BLOCKED) says how far the analysis got. COMPLETED is not
"findings verified".

## Vocabulary only (not wired yet)

`ParserHealth` (§5), `SourceAvailability` (§98), graph relation types of §94, `StateLabels` (Romanian labels of the ten states).

## Remaining

WP4: verifier sets VERIFIED/SUPPORTED/CONTRADICTED/REJECTED and fills `Verification`. WP5: wire ParserHealth/SourceAvailability into coverage.
WP6: localisation of message keys (the table here is the state-label table only). Rules that do not test contradictions yet are listed in each
finding's Limitations.
