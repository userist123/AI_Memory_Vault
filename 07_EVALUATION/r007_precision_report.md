# Precision Report: Reviewed Edge Promotion (Task r007)

**Branch**: `r007/reviewed-edge-promotion`  
**Base Commit**: `98ca28ead`  
**Evaluator**: ANTIGRAVITY  
**Date**: 2026-09-06  
**Status**: **STOP CONDITION TRIGGERED — NO-GO FOR BULK PROMOTION**

---

## 1. Executive Summary & Decision

In accordance with Task r007 contract requirements:
> *"Before any bulk promotion, hand-verify a random sample of at least 50 proposals and report precision: for each, would a human agree the two notes are genuinely related in a way useful at query time? ... If sampled precision is below 70%, do NOT bulk-promote. Report the measured precision, the failure patterns you found, and stop."*

A random sample of **50 proposals** from `06_INBOX/edge_proposals.json` (1,000 proposals total) was selected (seed = 42) and hand-verified against query-time semantic utility.

- **Sample Size**: 50 proposals
- **True Positives (Useful at query time)**: 9 (18.0%)
- **False Positives (Spurious / Noise)**: 41 (82.0%)
- **Measured Precision**: **18.0%**
- **Threshold Required for Bulk Promotion**: **70.0%**
- **Decision**: **NO-GO**. The STOP CONDITION is triggered. Bulk promotion of the existing 1,000 proposals in `06_INBOX/edge_proposals.json` is halted to protect the runtime memory graph from vocabulary pollution.

---

## 2. Hand-Verified Sample Judgements (Seed 42)

Each proposal was judged on whether a human user/agent querying the source note would find the target note a genuinely relevant semantic neighbor in memory retrieval.

| # | Source Path | Target Path | Shared Entities | Judgement | Failure Category |
|---|---|---|---|:---:|---|
| 01 | `.../Artifacts/PERPLEXITY_TAKEOVER_01_DOCUMENTATION.md` | `.../Artifacts/PERPLEXITY_TAKEOVER_PACKAGE.md` | `['1.0.0', '3.14.2', '9.0.2', '_cognitive_unverified', ...]` | FALSE | Generic version numbers & artifact boilerplate |
| 02 | `.../Artifacts/PERPLEXITY_TAKEOVER_04_TESTS.md` | `.../Artifacts/PERPLEXITY_TAKEOVER_05_EVIDENCE.md` | `['__init__', 'artifact', 'cognitive_core', ...]` | FALSE | Dunder method & artifact boilerplate collision |
| 03 | `.../Artifacts/PERPLEXITY_TAKEOVER_01_DOCUMENTATION.md` | `.../Artifacts/PERPLEXITY_TAKEOVER_03_MEMORY_CONTROLLER.md` | `['_cognitive_unverified', 'api', 'applies_to', ...]` | FALSE | Takeover chapter dump collision |
| 04 | `.../Artifacts/PERPLEXITY_CURRENT_AUDIT_SOURCE.md` | `.../Artifacts/AGENTS.md` | `['action', 'agents', 'api', 'archived', ...]` | FALSE | Generic prompt artifact token overlap |
| 05 | `.../Artifacts/implementation_plan_p0_7_cache.md` | `.../Artifacts/PERPLEXITY_CURRENT_AUDIT_SOURCE.md` | `['__init__', '_enforce_limits', 'admin', 'api']` | FALSE | Generic API/admin tokens |
| 06 | `.../Artifacts/cognitive_core_audit.md` | `.../Artifacts/PERPLEXITY_TAKEOVER_PACKAGE.md` | `['api', 'approvalrequirederror', 'artifact', ...]` | FALSE | Audit log to package summary coupling |
| 07 | `00_GOVERNANCE/.../UNIVERSAL_AGENT_MEMORY_PROTOCOL_V1.md` | `00_GOVERNANCE/.../agents/PERPLEXITY/CURRENT.md` | `['antigravity', 'base_main_sha', 'codex', ...]` | FALSE | Transient scratchpad target (CURRENT.md) |
| 08 | `00_GOVERNANCE/.../UNIVERSAL_AGENT_MEMORY_PROTOCOL_V1.md` | `00_GOVERNANCE/.../agents/ANTIGRAVITY/CURRENT.md` | `['antigravity', 'base_main_sha', 'blocked', ...]` | FALSE | Transient scratchpad target (CURRENT.md) |
| 09 | `01_ARCHITECTURE/.../Registru_Multi_Agent_Contracts.md` | `01_ARCHITECTURE/.../Master_Skills_Catalog_251.md` | `['127.0.0', 'aa', 'agents', 'claude', 'cli']` | FALSE | Localhost IP & WCAG acronym collision |
| 10 | `02_PRODUCT/projects/Continuity_Handoff.md` | `01_ARCHITECTURE/.../Master_Skills_Catalog_251.md` | `['cause', 'crud', 'for', 'github', 'id', 'it']` | FALSE | English stopword collision ('for', 'id', 'it') |
| 11 | `01_ARCHITECTURE/.../Registru_Transferuri_Development_Standards.md` | `02_PRODUCT/projects/workspaces/registru-transferuri/README.md` | `['127.0.0', 'aa', 'ac', 'euci', 'hg', 'infosec']` | **TRUE** | High domain alignment (Registru Militar & EUCI) |
| 12 | `.../Artifacts/PERPLEXITY_TAKEOVER_04_TESTS.md` | `.../Artifacts/cognitive_core_architecture.md` | `['ai_agent', 'artifact', 'filestorageengine', ...]` | FALSE | Artifact boilerplate tokens |
| 13 | `.../Artifacts/PERPLEXITY_TAKEOVER_04_TESTS.md` | `01_ARCHITECTURE/.../Retrieval_Bottleneck_P0_Empirical_Findings.md` | `['cognitive_core', 'id', 'memory_controller', ...]` | FALSE | Architecture tokens in test dump |
| 14 | `.../Artifacts/PERPLEXITY_TAKEOVER_04_TESTS.md` | `01_ARCHITECTURE/.../Master_Skills_Catalog_251.md` | `['can', 'head', 'id', 'must', 'net', 'not']` | FALSE | English stopword collision ('can', 'must', 'not') |
| 15 | `.../Artifacts/PERPLEXITY_TAKEOVER_PACKAGE.md` | `00_GOVERNANCE/.../AI_Memory_Vault_Multi_Agent_Execution_Protocol_V1.md` | `['_cognitive_unverified', 'approval', 'github', ...]` | FALSE | Generic workflow token overlap |
| 16 | `02_PRODUCT/projects/JARVIS_COGNITIVE_BRAIN.md` | `02_PRODUCT/.../workspaces/jarvis_cognitive_brain/README.md` | `['hud', 'jarviscontrols', 'onnx', 'rest', 'stt', 'tts']` | **TRUE** | Direct project specification to workspace README |
| 17 | `00_GOVERNANCE/.../agents/ANTIGRAVITY/CURRENT.md` | `00_GOVERNANCE/.../STATUS_SNAPSHOT_20260904_LUNA.md` | `['antigravity', 'codex', 'current', 'luna', ...]` | FALSE | Ephemeral agent coordination scratchpad |
| 18 | `.../Artifacts/PERPLEXITY_TAKEOVER_04_TESTS.md` | `01_ARCHITECTURE/.../Multi_Agent_Pipeline_Architecture.md` | `['activationengine', 'admin', 'cognitive_core', ...]` | FALSE | Generic cognitive_core component names |
| 19 | `.../Artifacts/walkthrough.md` | `.../Artifacts/implementation_plan.md` | `['agents', 'archived', 'artifact', 'obsidian-sync']` | FALSE | Generic session sync artifact pair |
| 20 | `02_PRODUCT/projects/GPO_Baseline_Deployment.md` | `.../Artifacts/02_PROJECTS__GPO_Baseline_Deployment.md` | `['gpo', 'ise', 'lgpo', 'mycommand', ...]` | FALSE | Duplicate sync artifact copy, not a knowledge synapse |
| 21 | `00_GOVERNANCE/.../agents/ANTIGRAVITY/CURRENT.md` | `00_GOVERNANCE/.../projects/AI_MEMORY_VAULT/CURRENT.md` | `['ai_memory_vault', 'cognitive_core', 'current', ...]` | FALSE | Ephemeral agent coordination scratchpad |
| 22 | `.../Artifacts/Continuity_Handoff.md` | `01_ARCHITECTURE/.../Temporal_Memory_P2_Empirical_Findings.md` | `['audit_log', 'json', 'must', 'not', 'review', 'rfc']` | FALSE | Stopword collision ('must', 'not', 'json') |
| 23 | `01_ARCHITECTURE/.../UI_UX_Resources_Directory_Reference.md` | `01_ARCHITECTURE/.../External_Repository_References_Aug2026.md` | `['css', 'github', 'html', 'js', 'mcp', 'ui-design']` | **TRUE** | Curated catalog pairing of external UI/code resources |
| 24 | `01_ARCHITECTURE/.../Master_Skills_Catalog_251.md` | `01_ARCHITECTURE/.../Temporal_Memory_P2_Empirical_Findings.md` | `['confirmed', 'json', 'must', 'not', 'rfc', 'unknown']` | FALSE | Stopword collision ('must', 'not', 'confirmed') |
| 25 | `.../Artifacts/PERPLEXITY_TAKEOVER_02_COGNITIVE_CORE.md` | `.../Artifacts/implementation_plan_p0_7_cache.md` | `['__init__', 'admin', 'api', 'artifact', ...]` | FALSE | Artifact boilerplate tokens |
| 26 | `02_PRODUCT/projects/Continuity_Handoff.md` | `00_GOVERNANCE/.../AI_Memory_Vault_Multi_Agent_Execution_Protocol_V1.md` | `['_cognitive_unverified', 'approval', 'human', ...]` | FALSE | Ephemeral handoff note to protocol |
| 27 | `01_ARCHITECTURE/.../Retrieval_Bottleneck_P0_Empirical_Findings.md` | `00_GOVERNANCE/.../RESOLUTION_IMPLEMENTATION_PROMPT_V1.md` | `['candidate', 'cognitive_core', 'execution', 'llm']` | FALSE | Architecture finding linked to prompt text |
| 28 | `10_DOCUMENTATION/procedures/Import_Sanitization.md` | `.../Artifacts/Memory_Protocol.md` | `['archived', 'classified', 'normalized', 'raw', 'review']` | **TRUE** | Core memory lifecycle sanitization & protocol pairing |
| 29 | `.../Artifacts/03_PROCEDURES__README.md` | `.../Artifacts/04_MEMORY__Lessons__README.md` | `['desc', 'from', 'readme', 'sort', 'table', 'where']` | FALSE | Egregious Dataview query keywords collision |
| 30 | `.../Artifacts/implementation_plan.md` | `.../Artifacts/PERPLEXITY_TAKEOVER_05_EVIDENCE.md` | `['agents', 'artifact', 'claude_original', ...]` | FALSE | Ephemeral session artifact collision |
| 31 | `.../Artifacts/PERPLEXITY_TAKEOVER_PACKAGE.md` | `02_PRODUCT/projects/JARVIS_COGNITIVE_BRAIN.md` | `['__init__', 'activeplan', 'api', 'llm', 'ooda']` | FALSE | Unrelated projects linked by generic '__init__', 'api' |
| 32 | `.../Artifacts/prompt_draft.md` | `02_PRODUCT/projects/JARVIS_COGNITIVE_BRAIN.md` | `['begin', 'immediate', 'ooda', 'sha-256', 'wal']` | FALSE | Draft prompt file linked by generic storage keywords |
| 33 | `01_ARCHITECTURE/.../Design_System_Foundation.md` | `10_DOCUMENTATION/procedures/UI_UX_Heuristic_Review.md` | `['aa', 'accessibility', 'dr', 'tl', 'wcag']` | **TRUE** | Design system foundation linked to UX accessibility review |
| 34 | `.../Artifacts/02_PROJECTS___Projects_Index.md` | `.../Artifacts/04_MEMORY__Lessons__README.md` | `['desc', 'from', 'sort', 'table', 'where']` | FALSE | Dataview SQL query keywords collision |
| 35 | `.../Artifacts/walkthrough.md` | `.../Artifacts/AGENTS.md` | `['agents', 'archived', 'artifact', 'review']` | FALSE | Generic documentation boilerplate words |
| 36 | `10_DOCUMENTATION/.../Autonomous_Program_Construction_Protocol.md` | `10_DOCUMENTATION/.../Enterprise_Large_Scale_Project_Integration.md` | `['127.0.0', 'dfir', 'pragma', 'sha-256', 'wal']` | FALSE | System tokens ('127.0.0', 'wal') across loose protocols |
| 37 | `.../Artifacts/PERPLEXITY_TAKEOVER_03_MEMORY_CONTROLLER.md` | `10_DOCUMENTATION/.../Enterprise_Large_Scale_Project_Integration.md` | `['api', 'controller', 'memory', 'memory_controller']` | FALSE | Artifact to procedure link based on generic words |
| 38 | `01_ARCHITECTURE/.../MT5_Python_Tkinter_Stack_For_Trading_App.md` | `01_ARCHITECTURE/.../Trading_Bot_Prompt_Language_English.md` | `['mt5', 'python', 'sl', 'tp']` | **TRUE** | High-precision domain pairing (MT5 trading bot app) |
| 39 | `00_GOVERNANCE/agents/memory-skill-router.md` | `00_GOVERNANCE/skills/ai-memory-vault/SKILL.md` | `['raw_external', 'sha-256', 'skill', 'skill_ingestion']` | **TRUE** | Router manifest directly governs vault skill ingestion |
| 40 | `00_GOVERNANCE/.../agents/LUNA/CURRENT.md` | `00_GOVERNANCE/.../RESOLUTION_IMPLEMENTATION_PROMPT_V1.md` | `['ci', 'current', 'mve', 'next']` | FALSE | Ephemeral agent coordination scratchpad |
| 41 | `.../Artifacts/cognitive_core_audit.md` | `02_PRODUCT/ORIGINAL_REQUEST.md` | `['api', 'llm', 'ooda', 'review', 'toolrouter']` | FALSE | Audit artifact to original user prompt dump |
| 42 | `.../Artifacts/cognitive_core_architecture.md` | `.../Artifacts/implementation_plan.md` | `['artifact', 'conversation-evidence', 'obsidian-sync']` | FALSE | Ephemeral session artifact pair |
| 43 | `.../Artifacts/implementation_plan.md` | `.../Artifacts/phase3_readiness.md` | `['artifact', 'conversation-evidence', 'obsidian-sync']` | FALSE | Ephemeral session artifact pair |
| 44 | `01_ARCHITECTURE/.../Design_System_Foundation.md` | `01_ARCHITECTURE/.../Motion_Design_Principles.md` | `['accessibility', 'dr', 'related_to', 'tl']` | **TRUE** | Design system foundation linked to motion principles |
| 45 | `00_GOVERNANCE/.../UNIVERSAL_AGENT_MEMORY_PROTOCOL_V1.md` | `00_GOVERNANCE/.../RESOLUTION_IMPLEMENTATION_PROMPT_V1.md` | `['current', 'next', 'sha', 'task']` | FALSE | Protocol to prompt text file via generic words |
| 46 | `.../Artifacts/Provenance_and_Redaction.md` | `.../Artifacts/PERPLEXITY_CURRENT_AUDIT_SOURCE.md` | `['original_path', 'security', 'source_ref', ...]` | FALSE | Procedure to audit dump via boilerplate fields |
| 47 | `01_ARCHITECTURE/.../UI_UX_Resources_Directory_Reference.md` | `01_ARCHITECTURE/.../Deep_Visual_Web_Engineering_Master_Report.md` | `['aa', 'css', 'github', 'wcag']` | **TRUE** | UI resource catalog complements web engineering report |
| 48 | `01_ARCHITECTURE/.../Registru_Multi_Agent_Contracts.md` | `.../Artifacts/PERPLEXITY_TAKEOVER_PACKAGE.md` | `['agents', 'depends_on', 'net', 'related_to']` | FALSE | High-level contracts linked to takeover package by graph keys |
| 49 | `.../Artifacts/PERPLEXITY_TAKEOVER_01_DOCUMENTATION.md` | `10_DOCUMENTATION/.../Skill_Agent_Memory_MOC.md` | `['agents', 'memory', 'raw', 'verification']` | FALSE | Target is a Navigation Hub (MOC); forbidden by rule #1 |
| 50 | `.../Artifacts/PERPLEXITY_CURRENT_AUDIT_SOURCE.md` | `10_DOCUMENTATION/procedures/Perplexity_Space_Setup_Registru.md` | `['agents', 'net', 'perplexity', 'readme']` | FALSE | Generic tokens 'net', 'readme', 'agents' |

---

## 3. Failure Patterns Analysis

The 41 false positives fell into six distinct failure modes:

1. **Dataview Query Collision**: Obsidian index files with Dataview code blocks (`FROM ... WHERE ... SORT ... DESC`) share query syntax keywords (`table`, `sort`, `where`, `from`, `desc`). This caused multiple folder indexes to link spuriously (e.g. Proposal #29, #34).
2. **Generic Technical Scaffolding**: Python dunder methods (`__init__`, `__file__`, `__code__`) and generic versions (`1.0.0`, `3.14.2`, `9.0.2`) co-occur in code-related notes without semantic topical relationship (e.g. Proposals #01, #02, #05).
3. **Stopwords and Short Acronyms**: Common tokens like `must`, `can`, `not`, `for`, `id`, `it`, `head`, `net` passed the entity extractor as acronyms or nouns and caused high TF-IDF scores between completely disjoint notes (e.g. Proposals #10, #14, #22, #24).
4. **Transient Multi-Agent Scratchpads**: Notes under `00_GOVERNANCE/coordination/agents/*/CURRENT.md` are transient agent working memory, not permanent vault knowledge. Linking static protocols to them introduces temporal instability (e.g. Proposals #07, #08, #17, #21, #40).
5. **Session Artifact Dumps**: Notes under `10_DOCUMENTATION/resources/Obsidian/Artifacts/` (takeover packages, sync logs, implementation plans) contain high volumes of internal system tokens that pair densely with one another, skewing the graph toward session noise rather than durable architecture.
6. **Navigation Hub Targets**: Proposal #49 linked directly to `Skill Agent Memory MOC`, violating Hard Requirement 1 ("No hub linking").

---

## 4. Proposer Hardening & Fixes Applied

In response to the empirical findings, `30_SCRIPTS/knowledge/edge_proposer.py` was updated:

1. **Import Alignment**: Switched from legacy `cognitive_core` paths to canonical `03_IMPLEMENTATION/packages` (`retrieval.hybrid_retrieval`, `graph.synapse_store`, `retrieval.vault_index`) with fallback support.
2. **Entity Noise Blacklist (`SPURIOUS_ENTITIES`)**: Explicitly filters dunder methods (`__init__`, `__file__`), generic semantic versions (`1.0.0`, `3.14.2`), Dataview keywords (`table`, `where`, `sort`, `desc`), and English stopwords (`can`, `must`, `not`, `id`, `for`).
3. **Hub Exclusion (`FORBIDDEN_HUBS`)**: Rejects candidate pairs that link to canonical hubs (`Knowledge Graph Home`, `08 Memory Subsystems Map`, `00 Core Map`, `02 Memory Knowledge Map`, `Skill Agent Memory MOC`) or notes exceeding `HUB_IN_DEGREE_THRESHOLD` (50).

---

## 5. Runtime Graph Baseline Measurement

Measured exclusively via `SynapseStore.from_index(ix)` (with navigation hubs excluded):

- **Total Notes in Index**: 936
- **Total Runtime Edges**: 301
- **Origin Breakdown**:
  - `declared`: 69
  - `inferred`: 69
  - `wikilink`: 163
- **Nodes Touched (Distinct source or target)**: 132
- **Nodes with Outgoing Edges**: 120
- **Mean Out-Degree**: 2.51
- **Hub Concentration (Top-8 In-Degree Share)**: 81 / 301 = **26.91%**
- **Top 8 In-Degree Targets**:
  1. `System Architecture` (`330fa4bc-5b7...`): 17
  2. `Canonical Frontmatter` (`ab6867cb-1ac...`): 14
  3. `Promotion and Human Review` (`27f72d97-a5f...`): 12
  4. `Memory Lifecycle` (`89105d0b-9fd...`): 9
  5. `Storage Conventions` (`5a663b4a-287...`): 8
  6. `Integrity Check` (`00b606ec-9dd...`): 8
  7. `Artifact: AGENTS` (`c754b481-44a...`): 7
  8. `Retrieval Bottleneck — P0 Empirical Findings` (`knw-retrieva...`): 6

---

## 6. Regression Testing

Regression test suite created in `20_TESTS/test_reviewed_edge_promotion.py`:

- `test_ai_agent_cannot_create_or_promote_directly_to_active`: Verifies review gating cannot be bypassed (AI_AGENT transitions to ACTIVE are denied by `lifecycle/policy.py`).
- `test_review_state_is_authorized_for_promoted_proposals`: Verifies promoted proposals enter as `REVIEW`.
- `test_hub_links_refused_when_in_degree_exceeds_threshold`: Verifies notes with in-degree >= 50 are dropped by `SynapseStore.from_index()`.
- `test_forbidden_canonical_hubs_excluded_by_proposer`: Verifies named navigation hubs are excluded.
- `test_deterministic_candidate_generation`: Verifies proposal generator produces identical outputs and ordering across repeated runs.
- `test_promoted_edge_provenance_schema`: Verifies all mandatory provenance fields (`source_id`, `target_id`, `relation`, `confidence`, `weight`, `origin`, `evidence_entities`, `extraction_run_id`) are present.

**Pytest Execution**:
```text
1180 passed, 3 skipped in 20.46s (baseline 1174 + 6 new regression tests)
```
**Repository Layout**:
```text
LAYOUT_STATUS=PASS
TRACKED_FILE_COUNT=19103
```

---

## 7. Remaining Gaps & Island Analysis

- **Current Runtime Graph Coverage**: 132 / 936 notes connected (14.1%).
- **Islands Remaining**: 804 notes (85.9%) remain without runtime semantic edges.
- **Root Cause**: The uncalibrated proposal queue in `06_INBOX/edge_proposals.json` has only 18% query-time precision. To satisfy Hard Requirement 1 ("An honest island beats a fake edge"), bulk promotion was halted.
- **Path Forward**: Future promotions must use the hardened `edge_proposer.py` with domain-specific entity filtering or human attestation before edges are written into note frontmatter.
