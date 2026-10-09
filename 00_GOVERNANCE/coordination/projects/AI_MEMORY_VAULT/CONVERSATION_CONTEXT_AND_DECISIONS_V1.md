# AI Memory Vault — Consolidated Conversation Context and Decisions

Purpose: Durable handoff of relevant project discussions, decisions, measurements, prior work, unresolved questions, and user requirements from recent AI Memory Vault conversations. This is a synthesized context ledger, not a verbatim transcript. Source artifacts and PRs remain authoritative for their own evidence; do not replace them with this summary.

Repository: https://github.com/userist123/AI_Memory_Vault_CODEX_READY
Prepared: 2026-10-09
User-facing language: Romanian. Prompts, briefs, tool instructions, and agent handoffs: complete English.
Execution policy: MAIN_ONLY, SEQUENTIAL_HANDOFF; one active agent at a time.
Authority rule: 00_GOVERNANCE/VAULT_STATE.md describes measured reality and outranks design aspirations where they conflict.

## 1. What the user is trying to build

The user is not asking merely for a RAG search box, chatbot, or collection of prompt files. The goal is an AI agent system that accepts a natural-language request, understands it, compiles it into a complete executable brief, routes it to the right agent/model/skill/tool, retrieves only relevant authorized memory, plans a long task, executes real operations, preserves focus and state across context limits or restarts, verifies the result, and delivers a truthful final answer.

The user's central clarification: skills and tools should help an agent understand the request and route it to the appropriate skill(s)/tool(s); the skill router is not itself meant to execute the work. Routing selects capabilities; tools perform actions. The user expects each request to be transformed into a prompt and delivered to the execution runtime, as described by the Memory Vault.

RAG alone does not make an LLM an agent. The complete runtime needs explicit state, orchestration, capability/tool execution, durable checkpoints, bounded context, retry/stop rules, evidence-based verification, and a terminal-resolution contract.

For a smaller/cheaper agent to run a large task without losing focus, concentration, or memory, keep active context small and structured, use the Vault as external memory, split work into dependency-aware bounded steps, checkpoint after verified milestones, compress context at phase boundaries, and resume from evidence rather than re-reading everything. Bigger models are optional workers, not a substitute for orchestration.

## 2. User requirements and constraints captured from discussion

- Do not stop at research, audit, or plan. Continue through actionable work packages and tests until acceptance gates pass, or identify a genuine blocker with evidence.
- Do not report success based on module existence, design docs, mocks, placeholder output, or a green test that did not exercise the production path.
- Use the smallest eligible model/agent route when it can do the work safely; escalate only when capability, quality, risk, or verification requires it.
- For long tasks, use CPU + GPU hybrid inference where feasible, especially for Qwen3:30B-A3B, but do not repeatedly retry known memory-allocation failures or kill user applications.
- Prompts delivered to agents must be complete English; user-facing reports must be Romanian. The user prefers scripts without Romanian diacritics where formatting may be affected.
- Turn the full user request into a complete prompt using the Memory Vault compiler. Do not ship TODO placeholders or assume the downstream agent will infer missing acceptance criteria.
- Route skills by metadata and capability, not merely folder names or keyword coincidences. The router must abstain rather than select irrelevant skills.
- Keep agent routing, skill routing, model routing, and tool routing as separate auditable decisions.
- Preserve existing work, especially the Qdrant recovery worktree. Do not reset, clean, stash, delete, or silently discard it.
- Respect MAIN_ONLY and SEQUENTIAL_HANDOFF. Do not start multiple coding agents simultaneously.
- Do not auto-merge PRs or weaken security/test gates to make CI green.
- No production Qdrant collection reindex/write without separate explicit owner approval.
- Do not claim a model or CLI was used unless an actual response and observable trace exist.
- Memory proposals stay REVIEW/unverified until the owner attests/promotes them.
- Every completion claim must have evidence and an explicit verification level; blocked commands remain BLOCKED.

## 3. Architecture and runtime work required

### 3.1 Request intake and prompt compiler

The intended handoff entry point is 30_SCRIPTS/prompt/compile_task_prompt.py. It must compile a validated task schema containing goal, explicit intent, expected outcome, scope, forbidden actions, acceptance criteria, route, candidate skills/tools, evidence references, permissions/approval, budgets, stop conditions, terminal status, and handoff destination. Supported intents are implement, verify, measure, fix, and migrate.

Intent inference in infer_intent.py is intentionally fail-closed on weak evidence. The observed classifier score on unseen phrasings was 50%; do not disable abstention just to force compilation. For the full completion task, intent is explicitly implement. The old generic prompt template has TODO placeholders and instructions about isolated worktrees/cherry-picking that conflict with MAIN_ONLY. Correct the template and tests.

Prompt compilation, delivery to the runtime, and downstream execution should be separately logged events. The generated prompt must carry fresh measured state or explicit UNAVAILABLE markers, selected skills and data sources, permissions, acceptance tests, recovery, budgets, and stop conditions.

### 3.2 Skill, agent, tool, and model routing

03_IMPLEMENTATION/packages/interfaces/skill_router.py currently uses Jaccard overlap against top-level skill directory names, defaults to top five, and lacks robust parsing of SKILL.md metadata, a minimum relevance threshold, and abstention. It has selected irrelevant skills and missed descriptions such as agent-skill-stack and antigravity-skill-orchestrator. Historical inventory: 3661 top-level skill directories; 5446 directories and 3856 SKILL.md files in a recursive inventory.

03_IMPLEMENTATION/packages/learning/agents/router_agent.py separately routes subagents such as retrieval, verifier, critic, and consolidator. Keep this separate from skill selection. ToolRouter, MultiAgentOrchestrator, PlanComplexityAnalyzer, CouncilBudgetController, ModelTierRouter, ModelProvider, ContextPackBuilder, ActivePlan, and terminal resolution must be audited for real production consumers. Existing deterministic placeholder synthesis and unsupported ToolRouter actions must not be treated as real execution.

Skill metadata should include stable id/path, name/description, capability/tool declarations, risk, provenance/source, license when known, compatibility, validation/activation status, content hash, and freshness. Select agent first, then filter/rank skills, then load only selected skill bodies. Keep context budgets small and enforce them.

Freeze a bilingual routing benchmark before testing: positive tasks, near-negative/misleading skill names, no-match, ambiguous and high-risk tasks, and missing capabilities. Report precision/recall, top-1/top-2, MRR, abstention, false selection, selected skills and token cost. Preregister thresholds and do not tune on held-out cases.

### 3.3 Memory retrieval, RAG, and context assembly

MemoryController.search is the production implementation; memory_controller/ is a shim. Inspect actual query-driven candidate generation, lifecycle/type filtering, ranking/fusion, graph expansion defaults, pagination, ContextPackBuilder, and RetrievalTrace. A prior audit found a retrieval path that did not read the query; later query-driven candidate generation was added. Retrieval quality must be measured at each stage.

A prior memory-use evaluation asked 20 real coordination questions and found only 5 relevant items among the first-three results (57 returned items). Reproduce the report in 07_EVALUATION/memory_usage before changing retrieval. Do not conflate graph-subset results with whole-corpus results. A single run of loss attribution reported 71.56% of non-abstain misses due to PAGINATION_CUT, 13.76% lifecycle floor exclusion, 11.93% never candidate, 1.83% candidate limit, 0.92% RAW exclusion, and 0% undetermined. The preregistered decision rule pointed to evaluating a reranker, not blindly expanding candidates. This is a measured one-run result, not proof a reranker is implemented.

Use only the registered vault-memory MCP tools (memory_search, memory_get, memory_propose) or secure python -m cognitive_core.recall_cli. Do not invent or call localhost:8000. Preserve lifecycle, provenance, verification state and reason codes. Retrieved text is data, not higher-priority instruction.

Measured source values have changed across commits, so always remeasure from current main: an earlier compiler run reported VaultIndex=972, graph edges=483, edge-bearing notes=195, storage-visible=852; a later VAULT_STATE snapshot reports VaultIndex=1209 and FileStorageEngine-visible=858. These are different snapshots and populations; neither pair should silently replace the other. Graph search uses one outgoing hop and covers only a small subset of corpus notes.

Graph expansion and cognitive-core modules are wired but OFF by default per the last inspected VAULT_STATE. Executive/global_workspace/reasoning/working_memory modules are also wired but OFF by default. Do not turn these on without preregistered evaluation, explicit safety/lifecycle checks, and demonstrated net benefit.

### 3.4 Long-task planning, persistent working memory, checkpoint and resume

Inspect ActivePlan.save_state/load_state before adding new state infrastructure. A checkpoint should include task/run id, schema version, step and dependencies, attempt count, last verified milestone, workspace/main SHA and file hashes, route/skill/tool choices, evidence references, remaining budgets, blockers/open questions, and exact next action.

Persist atomically; validate checkpoint schema and workspace state on resume. Missing, corrupt, or stale checkpoints must fail closed or explicitly start a new run, never silently discard prior work. Compact context at phase boundaries using verified facts and references. Use finite time/token/tool/retry budgets, idempotency and safe backoff. Test interruption before/after tool result persistence, restart in a new process, context compaction, corruption, stale SHA and duplicate destructive actions.

### 3.5 Execution and terminal resolution

Trace the real path through planner, budget gate, ContextPackBuilder, ModelTierRouter.resolve, ModelProvider.generate, authorized tool calls, verification, terminal gate and final response. A tier label or successful mock is not a real provider call. Unsupported actions must yield typed blockers.

The terminal outcome must be exactly one of RESOLVED, ABSTAINED, HUMAN_CONFIRMATION_REQUIRED. Verification is bounded; no action or verification follows terminal resolution in the same run. Memory reorganization happens after verification and cannot reopen the same task. Confidence, cross-model agreement and narrative plausibility do not substitute for evidence.

### 3.6 Qdrant stability and recovery

A recovery worktree exists at C:\Users\Marius\Projects\AI_Memory_Vault_QdrantFix, branch fix/qdrant-stable-point-ids, based on main SHA c5f939185f05c516e1cd46e11b3763026a6137d5. It contains uncommitted Qdrant changes and untracked audit/tests/scripts. Preserve it and hash every artifact before integration. Never run destructive git commands in it.

Changes under review include stable SHA-256-derived 63-bit point IDs instead of Python randomized hash, collection vector dimension/distance validation, pagination, fail-closed reindex, correct upsert semantics, legacy point migration and reconciliation. Historical modified-worktree tests: 2620 passed, 13 skipped, 9 xfailed in 515.46s; Qdrant targeted 27 passed, SkillRouter 3 passed, strict YAML 10 passed, diff check passed. These are not a fresh clean-main baseline.

Before integration, run clean-main baseline, back up and hash the recovery changes, port only validated changes, use temporary collections, test cross-process stable IDs, dimensions/distance, upsert, pagination, idempotence, migration, lifecycle eligibility, reconciliation, errors, embedding failure retention and cleanup. Production vault_memory writes/reindex require separate explicit owner approval.

## 4. Local runtime and model findings (2026-10-09)

Observed device: Marius-PC. Codex CLI version/model must be rechecked live; a prior report showed 0.162.0 and gpt-6-luna.

Antigravity:
- CLI: %LOCALAPPDATA%\agy\bin\agy.exe, version 1.3.1.
- Read-only audit with gemini-3.8-flash-high returned in 71.7 seconds.
- Trace showed four successful view_file calls and one failed read for a nonexistent path.
- It selected writing-plans and agent-memory; the latter was a poor semantic match for skill-routing audit.

Claude Code:
- CLI: %USERPROFILE%\.local\bin\claude.exe, version 2.1.295.
- Minimal smoke test returned CLAUDE_CLI_OK.
- Full audit failed with 429 usage_limit_reached; no audit answer was produced. Do not represent this as a completed audit.

Ollama inventory observed:
- qwen3:30b-a3b, about 18 GB Q4_K_M, architecture qwen3moe, total parameters 30.5B, active MoE tag A3B, context metadata 262144, embedding length 2048.
- nomic-embed-text, mistral:7b-instruct, qwen2.5:7b-instruct, llama3.1:8b, qwen2.5-coder:3b, qwen2.5-coder:7b.
- Hardware: 31.71 GB RAM, Intel i7-14700HX (20 physical/28 logical), RTX 5060 Laptop GPU 8151 MiB VRAM.

Five Qwen 30B attempts failed before generation due to CUDA_Host allocation (one about 17.53 GB, four hybrid settings requesting about 10.34–12.55 GB with num_gpu 16/18/20/22 and reduced context/batch). No Qwen response was produced; ollama ps showed no loaded model, and no user application was terminated. Future attempts must use a small bounded profile matrix, monitor RAM/VRAM/CPU, inspect background generation after timeout, stop after predefined allocation failures, and not alter drivers/quantization/system settings or kill apps without owner approval.

## 5. Governance and evidence constraints

- Canonical measured-state file: 00_GOVERNANCE/VAULT_STATE.md.
- AGENTS.md and VAULT_STATE.md use 00_GOVERNANCE/coordination/ as the canonical coordination area. UNIVERSAL_AGENT_MEMORY_PROTOCOL_V1.md still points to 09_COORDINATION/AGENT_MEMORY/. Reconcile this conflict without destroying legacy records or creating dual sources of truth.
- Project CURRENT.md was stale relative to main as of October 9; reconcile it from measured reality and retain history.
- Context budgets observed in AGENTS.md: MAX_COUNCIL_AGENTS=3; MAX_PRIMARY_AGENTS=1; MAX_SKILLS_PER_AGENT=2; MAX_TOTAL_SELECTED_SKILLS=4; MAX_MEMORY_RESULTS=5; MAX_GRAPH_EXPANSION=1 hop; MAX_SPECIALIST_OUTPUT=600 tokens; MAX_SYNTHESIS_INPUT=2500 tokens. Revalidate before use.
- Required pipeline: CLASSIFY -> ROUTE AGENTS -> ROUTE SKILLS -> RETRIEVE MEMORY -> ASSEMBLE MINIMAL CONTEXT -> EXECUTE -> COMPRESS -> SYNTHESIZE -> VALIDATE.
- Select agents before skills; load full SKILL.md only after candidate selection.
- memory_propose creates a REVIEW/unverified candidate; only the owner can attest/promote.
- Do not claim model/tool use without observable traces and real outputs.
- Distinguish CODE_VERIFIED, TEST_VERIFIED, RUNTIME_VERIFIED, CI_VERIFIED, DOCUMENT_VERIFIED, CLAIMED_ONLY, UNVERIFIED.
- A blocked command is BLOCKED, not PASS. Unknown authority, provenance, lifecycle, evidence, capability or configuration must fail closed.
- No auto-merge for security/research PRs; preserve branch protections, do not weaken tests, and do not force-push/rewrite/delete history without owner approval.

## 6. Related PRs and parallel work — preserve separation

This ledger does not authorize merging any existing PR or combining its scope. Recheck all states and SHAs live.

- PR #204 — skill-exfiltration-scanner / workflow hardening: prior report stated CI/workflows passed and the PR was merged. Work focused on workflow/verification/registry controls and token economy. Preserve as historical baseline; do not reopen or rewrite without a concrete defect.
- PR #206 — Book-to-Memory research: separate research branch; do not merge research claims just because unit tests pass. Research phases 0–6 were discussed; phase 6 retrieval/working-memory validation had a report. Later audit notes identify open evidence-integrity work B03–B10 and other B01–B12 gates: distinguish synthetic/simulated/model-generated/human-attested/independently reproduced evidence; evaluate prompt bias and counterfactuals; multi-rater/human ground truth/calibration; benchmark leakage; reproducible run configuration; claim-to-evidence audit; test real HARD_BLOCKER-to-WARNING downgrade prevention (B12). The B03 packet had 51 tasks/204 trials as prompts only and had not yet been run in a real-model ablation; no multi-rater scoring or human-label calibration was established. Read 08_RESEARCH/BOOK_TO_MEMORY/OPEN_BLOCKERS.md and branch state before further work.
- PR #207 — owner authority guardrail: separate host-OS enforcement dependency. Repo-local hooks cannot isolate a secret from an agent running under the owner's unrestricted Windows account. Owner must perform elevated Windows/NTFS ACL and least-privilege-account setup. Never claim host-level isolation from repo tests alone.
- PR #208 — LogAnalyzer DFIR platform: prior report stated merged. Related lessons learned cover evidence-driven DFIR across Windows/AD/GPO, air-gapped networks, email, internal web platforms, file servers, NAS, removable media, classified documents, policy changes and audit logs. Keep this a distinct product scope; incorporate only shared evidence-integrity lessons into Vault, not the whole LogAnalyzer implementation.
- PR #209 — runtime authority and memory integrity: open; no auto-merge. Earlier verdicts were corrected from PASS to BLOCKED/NOT PROVEN when any required gate lacked evidence. M01/M02/M03/M07 were reported hardened in library/tests but must be checked for actual production wiring. U07 (HMAC secret inaccessible to agent process) depends on host OS ACL/account separation via PR #207 and cannot be proven by PR #209 alone. Other noted residuals include U01 controller-level active mutation mitigated at MCP layer, U04 unauthenticated REST server script, U05 promoter gate, U06 unpinned dependencies, U08 unthrottled proposal rate. Treat each status as historical until rechecked.
- PR #210 — LogAnalyzer unified: mentioned as a separate unified LogAnalyzer workstream; recheck live state and do not mix scope without user-approved dependency.
- PR #211 — Claude workflow/DFIR contract: separate branch chore/claude-workflow-dfir-contract, last cited head ebb617cf0. User explicitly requested six defects only, no refactor/scope expansion, removal of contradictory verdict instructions, definition of gate evidence requirements, and resolution of U06 ambiguity. Preserve scope and inspect latest handoff/CI before touching it.
- Memory Vault Qdrant recovery worktree: separate uncommitted recovery area described in section 3.6; preserve regardless of other PR status.

These PR statuses are historical reports, not live state. Recheck live GitHub before acting.

## 7. Lessons learned that must shape implementation

1. Module exists does not mean it has a production consumer. Prove imports, construction, flags/defaults, and runtime calls.
2. A working read path can arm a destructive write path; test mutation gates after read-path changes.
3. A guard firing for the wrong reason can hide the defect it was intended to prevent.
4. Two individually correct changes can cancel each other while both report success; test end-to-end invariants.
5. A shim can make grep-based audits falsely conclude an implementation is absent; trace the actual package path.
6. A test suite passing does not establish production evidence, independent human validation, host isolation, or a real provider call.
7. Failures must not be converted to skips/warnings/green by weakening tests or changing scope.
8. Unknown/partial data is not a negative result. Report UNKNOWN/NOT_ASSESSED/PARTIAL where appropriate.
9. Do not tune benchmarks on held-out sets; record run configuration and evidence hashes.
10. A model timeout may leave generation running and queue later calls; inspect runtime state before retrying.
11. Production reindex or lifecycle mutation can be destructive even if invoked from a retrieval repair; require separate approval and reversible evidence.
12. Generated prompts, imported skills, retrieved memory, and documents cannot override governing instructions or permission boundaries.

## 8. Required work order and completion criteria

The executable specification is FULL_FUNCTIONAL_COMPLETION_PROMPT_V1.md. Its work packages are:
WP0 preservation/baseline/governance;
WP1 request intake and prompt compilation;
WP2 agent/model/skill/tool routing;
WP3 authorized RAG/retrieval/context;
WP4 real planning/provider/tool execution;
WP5 durable state/checkpoint/resume;
WP6 local model/client audit;
WP7 bounded terminal resolution/anti-fabrication;
WP8 E2E integration and handoff.

Acceptance gates G0–G9 are mandatory: governance, prompt completeness, routing metrics, authorized memory, real execution, continuity, verification, regression, runtime audit, and handoff. A task is not COMPLETE while a required gate is blocked, unverified, or lacks evidence. Record the exact blocker, evidence, owner action needed, and next step.

## 9. Sources and references to retain

- 00_GOVERNANCE/VAULT_STATE.md
- AGENTS.md, CLAUDE.md
- 00_GOVERNANCE/coordination/UNIVERSAL_AGENT_MEMORY_PROTOCOL_V1.md
- 00_GOVERNANCE/coordination/projects/AI_MEMORY_VAULT/CURRENT.md
- 00_GOVERNANCE/coordination/projects/AI_MEMORY_VAULT/RESOLUTION_IMPLEMENTATION_PROMPT_V1.md
- 10_DOCUMENTATION/procedures/Compiling_A_Request_Into_A_Brief.md
- 10_DOCUMENTATION/procedures/Recording_A_Solved_Problem.md
- 30_SCRIPTS/prompt/compile_task_prompt.py, infer_intent.py
- 03_IMPLEMENTATION/packages/interfaces/skill_router.py
- 03_IMPLEMENTATION/packages/learning/agents/router_agent.py
- 03_IMPLEMENTATION/packages/interfaces/tool_router.py
- 03_IMPLEMENTATION/packages/memory/orchestrator.py, planning.py, plan_complexity_analyzer.py, council_budget_controller.py
- 03_IMPLEMENTATION/packages/providers/model_tier_router.py, model_provider.py
- 03_IMPLEMENTATION/packages/interfaces/memory_v6_cli.py
- 07_EVALUATION/memory_usage/
- 07_EVALUATION/heldout_retrieval_benchmark_v2/
- 07_EVALUATION/loss_funnel/LOSS_FUNNEL_REPORT.md
- 08_RESEARCH/BOOK_TO_MEMORY/OPEN_BLOCKERS.md
- 10_DOCUMENTATION/audits/2026-10-09-ai-agent-workflow-audit.md (recovery worktree)
- 10_DOCUMENTATION/audits/2026-10-09-qdrant-stability-audit.md (recovery worktree)
- 10_DOCUMENTATION/journal/2026-10-09-qdrant-stability-fix.md (recovery worktree)
- NIGHT_SHIFT_FULL_AI_MEMORY_VAULT_SECURITY_PLAN.md (historical audit plan in Library)
- LOGANALYZER_LESSONS_LEARNED_COMPLETE.md (separate product context; do not merge its implementation scope into Memory Vault)
- Text lipit(20261005-182240).txt (historical PR #209 verdict correction / U07 not-proven evidence)
- PR #248: https://github.com/userist123/AI_Memory_Vault_CODEX_READY/pull/248

## 10. Explicit next actions

1. Preserve this ledger and the master prompt in the current PR; do not merge automatically.
2. Re-fetch live main SHA, open PRs, CI statuses, and canonical coordination ownership.
3. Back up/hash all Qdrant recovery work before any source edit; run a fresh baseline against exact current main.
4. Continue WP0-WP8 sequentially on main, updating this ledger and project CURRENT/task/session records as verified state changes.
5. At each handoff record task/run id, main SHA, files changed, commands and exit codes, tests, evidence paths, blockers, approvals, and exact next action.
6. Final report in Romanian; no claim of full functionality until G0-G9 are supported by fresh evidence.

Integrity note: This document preserves the relevant known context and decisions found during retrieval. It is not a verbatim transcript of every conversation. Where a detail conflicts with current source-of-truth files or fresh runtime evidence, investigate and correct this ledger in the same commit as the measured change.


## 11. October 9 conversation audit — evidence ledger (not a rerun)

This section was added after the owner clarified that the initial Senior AI/ML Engineer RAG code-review prompt must NOT be executed again. The required task is to audit the conversation/actions already performed today, recover what was saved and tested, and preserve those facts in this PR. This is an evidence ledger, not a claim that all prior runs have been reproduced.

### 11.1 Original scope to retain

The original request was a detailed architectural/code review of the existing repository, specifically:
- Markdown/PARA ingestion and chunk boundaries; metadata use and preservation of logical context;
- vector-store update/upsert semantics and duplicate-free updates after a note changes;
- retrieval relevance, context noise, small-model context budgets, and prompt-injection boundaries;
- Ollama API robustness: timeouts, streaming, factual-task temperature and error handling;
- bottlenecks, scalability, vulnerabilities, and whether the current architecture requires foundational redesign.

Subsequent clarification changed the immediate action: do not run this prompt again. Audit today's conversation and existing work/evidence, identify what was actually saved/tested/claimed, and consolidate it in PR #248.

### 11.2 Verified repository PR records inspected during this audit

The live GitHub PR metadata was fetched on 2026-10-09. These are recorded PR states, not assertions that every runtime integration is complete.

| PR | Observed state | Evidence / limits that must remain visible |
|---|---|---|
| #204 external-content trust boundary | merged | Workflow and scanner hardening recorded. Repository settings, branch protection, and some upstream version/lockfile checks were explicitly unverified in the PR record. |
| #207 owner-authority guardrail | merged | 45 targeted tests reported in the PR description. Windows least-privilege account setup and actual host-level enforcement required owner-side execution; repo-local hooks cannot constrain an unrestricted owner account. |
| #208 LogAnalyzer DFIR platform | merged | Separate product scope; do not conflate with Memory Vault implementation. |
| #209 runtime authority and memory integrity | merged | Several security components are hardened and tested in library code but explicitly not yet wired to production execution. Its PR body documents a real memory-read regression and subsequent repair; do not describe all security gates as production-enforced merely because unit tests pass. |
| #211 central agent router/dispatcher | merged | PR body says routing was reachable through manual CLI only, not through a production consumer; real claude/codex/agy/ollama executable dispatch was not exercised in the tests described there. |
| #213 fixes for #211 | merged into #211 branch | PR body reports 49 targeted tests passed and a full suite of 2,691 passed, 13 skipped, 9 xfailed, 0 failed (19m14s) on that historical head. It also explicitly lists remaining work: verifier dispatch/verification loop, authority_gate, FeedbackStore reload, named-pipe bridge transport, and final-result persistence/goal-data policy. Re-run on current main before using as current evidence. |
| #214 universal Vault access | merged | PR body reports 2,757 passed, 13 skipped, 9 xfailed, 0 failures on its historical tree; baseline main was 2,611. It includes a real MCP stdio session/snapshot test, but also says no real Claude/Codex/Antigravity/Gemini session had yet called vault tools and the Ollama bot used a fake transport. These are historical claims, not fresh current-main results. |
| #206 Book-to-Memory research | closed without merge | Research history was split into smaller PRs; don't assume #206 itself is merged or its empirical claims validated. |
| #220, #221, #223, #224, #225, #226 | merged | Book-to-Memory audit/notes/harness/docs/security/evaluation changes landed. #226 explicitly says no model was run for B03 and no human labels were written for B06; leakage findings and comparability controls are recorded, but real ablation and human calibration remain pending. |
| #248 completion prompt/context | open, not merged | Documentation-only handoff. It does not itself implement the WP0-WP8 plan or establish that its acceptance gates pass. |

### 11.3 October 9 local-runtime and Qdrant evidence already recorded

The earlier context supplied to this audit records the following, which must not be presented as a fresh run unless reproduced:
- A local validation report was referenced as `C:\Users\Marius\Projects\AI_Memory_Vault_VALIDATION_REPORT_20261009.md`.
- A separate recovery worktree exists at `C:\Users\Marius\Projects\AI_Memory_Vault_QdrantFix`, branch `fix/qdrant-stable-point-ids`, with uncommitted tracked and untracked artifacts. Never stash, clean, reset, delete, overwrite, or checkout main inside that worktree.
- Historical modified-recovery-worktree suite: 2,620 passed, 13 skipped, 9 xfailed in 515.46s; targeted Qdrant 27 passed; SkillRouter 3 passed; strict YAML 10 passed; `git diff --check` exit 0. This was not a clean-main baseline.
- Qdrant recovery changes concern stable SHA-256-based 63-bit IDs instead of Python randomized hash, dimension/distance validation, pagination, fail-closed reindex, upsert semantics, legacy ID migration/reconciliation. Live tests must use temporary collections. Production `vault_memory` writes/reindex need separate explicit owner approval.
- Local CLI audit: Antigravity 1.3.1 read-only audit succeeded with gemini-3.8-flash-high (71.7s; four successful view_file calls and one failed read); it selected writing-plans and agent-memory, the latter being a poor fit for a skill-routing audit.
- Claude Code 2.1.295 smoke test passed, but the full audit failed with `429 usage_limit_reached`; no audit answer was produced.
- Five Qwen3:30b-a3b attempts failed before generation due to CUDA host-allocation/resource constraints; no Qwen response was produced. Do not claim that model completed the audit. No driver changes, process termination, or system-setting changes without owner approval.
- Historical memory retrieval measurement: 20 coordination questions, 5 relevant items among the first three results (57 returned items). Historical loss funnel snapshot: 71.56% PAGINATION_CUT, 13.76% lifecycle floor exclusion, 11.93% never candidate, 1.83% candidate limit, 0.92% RAW exclusion, 0% undetermined. Single-run snapshot only; reproduce before current claims and separate corpus populations.
- Historical corpus counts differ by snapshot (VaultIndex 972 / edges 483 / edge-bearing 195 / storage-visible 852 versus later VaultIndex 1209 and FileStorageEngine-visible 858). Never combine these as if measured at one time.

### 11.4 What this audit has and has not established

Established from live PR metadata and previously preserved context:
- Several prior PRs have merged, including routing/security/access-control changes; those merge states were re-fetched during this audit.
- Their own descriptions distinguish unit/code verification from production wiring and real-provider execution.
- Historical full-suite counts differ by commit and worktree. They are not interchangeable and must not be summarized as a single “current tests passed” figure.
- The initial code-review prompt is the scope reference, not a task to re-execute in this conversation.

Not established by the present metadata inspection:
- That the complete local tool-call history for the entire day has been recovered verbatim.
- That every previously claimed command/test was freshly executed now.
- That current `main` is clean, has the same SHA as any historical report, or passes the full suite.
- That the local Qdrant recovery files have been safely backed up/hashed during this audit.
- That all WP0-WP8 implementation work has been performed, all providers have been exercised, or G0-G9 pass.

### 11.5 Recovery status

The recovery actions above have been performed to the extent supported by the retained local tool history and readable artifacts. The fresh main/model results are recorded in section 12; the retained-history and artifact inventory is recorded in section 13. A missing result remains marked unverified rather than inferred. PR #248 remains open and unmerged. The separate Qdrant recovery worktree and production collection were not modified by the fresh validation.



## 12. Fresh main validation and local-model matrix — 2026-10-09

This section records a new, direct execution after the local checkout was recovered. Results here supersede neither older snapshots nor the separate Qdrant recovery worktree; every result is tied to its own revision and command.

### 12.1 Main baseline and environment

- Repository: `C:\Users\Marius\Projects\AI_Memory_Vault_CODEX_READY`.
- Branch: `main`.
- Before update, checkout was clean at `c5f939185f05c516e1cd46e11b3763026a6137d5`.
- Ran `git fetch origin main` and `git merge --ff-only origin/main`; fast-forward completed without reset. New HEAD and `origin/main`: `154dc4274b3b3d634a50c6e9f30b86a77b798590`.
- Runtime: Python 3.14.2, pytest 9.0.2, Ollama 0.40.1, NVIDIA GeForce RTX 5060 Laptop GPU with 8151 MiB VRAM.
- Final Git check after testing: `## main...origin/main`, no tracked/untracked changes reported. `git diff --check` emitted no errors.
- `python -m pip check` is **FAIL** in the system Python environment: `langchain-core 1.5.5` requires missing `langsmith`; `torch 2.11.0` requires `setuptools<82`, but installed setuptools is 83.0.0. This is an environment/dependency issue; no global package changes were made.

### 12.2 Fresh full-suite result on exact main SHA

Command:

```powershell
python -m pytest -q --tb=short
```

- Revision: `154dc4274b3b3d634a50c6e9f30b86a77b798590`.
- Exit code: **1**.
- Runtime: **638.35 seconds**.
- Result: **15 failed tests and 2 test errors**. The streaming output contained an extremely long parametrized test ID and did not yield a trustworthy aggregate passed-count; do not infer one.
- The failing tests were reproduced in a targeted run. No source fixes were made as part of this validation.

Failing test IDs and observed symptoms:

1. `20_TESTS/test_b2m_leakage_check.py::test_committed_report_matches_a_fresh_run` — committed report differs from fresh output.
2. `20_TESTS/test_book_to_memory_human_labels.py::test_committed_packet_is_a_fresh_build` — committed labelling packet is not a fresh build.
3. `20_TESTS/test_book_to_memory_real_ablation.py::test_committed_packet_baseline_prompts_are_clean_and_hashes_match` — `trials.json` SHA-256 differs from manifest.
4. `20_TESTS/test_book_to_memory_real_ablation.py::test_committed_packet_is_bound_to_the_preregistration` — preregistration SHA-256 differs from the committed packet manifest.
5. `20_TESTS/test_book_to_memory_real_ablation.py::test_committed_packet_is_reproducible_from_the_notes` — generator `--check` reports `DIFFERS: manifest.json`.
6. `20_TESTS/test_import_external_skills.py::test_an_executable_bit_aborts_the_import_even_on_an_allowed_type` — expected `SystemExit` was not raised for an executable-bit case.
7. `20_TESTS/test_owner_authority_gate.py::test_settings_command_allows_and_denies` — allowed command returned exit code 2 instead of 0.
8. `20_TESTS/test_vault_access_core.py::test_read_is_verbatim_with_hash_and_exact_lines` — exact text comparison differs at CRLF/LF line endings.
9–15. `20_TESTS/test_vault_access_ollama_telegram.py::test_reading_a_file_is_extractive_and_never_calls_the_model` — seven parameter variants fail because the test's expected CRLF-containing body does not match the LF-normalized file content.
16–17. Two setup errors in parametrized `20_TESTS/test_vault_access_core.py::test_unreadable_or_unusual_frontmatter_fails_closed`, including the long `Long.md` frontmatter input. The captured output was dominated by the full generated parameter value; exact root cause remains **unverified** and must not be guessed.

The first five failures indicate generated/committed Book-to-Memory evaluation artifacts or their manifests are not in sync. The vault-access failures demonstrate a byte/line-ending contract mismatch in this Windows run. These are observations, not a claim that a specific upstream PR caused them.

### 12.3 Real local model execution — one model per isolated run

Each run used the live test `20_TESTS/test_b3_local_provider_live.py::test_b3_live_ollama_council_execution` with `RUN_LIVE_OLLAMA_TESTS=1` and a temporary three-tier config pointing all tiers to the selected model. The temporary config was removed after each run; no report or result file was saved locally.

| Installed model | Result | Test runtime | Evidence |
|---|---|---:|---|
| `qwen2.5-coder:3b` | PASS — 1 passed | 3.57 s | Real LocalProvider health, specialist + synthesis execution, provider identity and real token-usage telemetry asserted |
| `qwen2.5-coder:7b` | PASS — 1 passed | 7.82 s | Same live council smoke test |
| `qwen2.5:7b-instruct` | PASS — 1 passed | 10.34 s | Same live council smoke test |
| `mistral:7b-instruct` | PASS — 1 passed | 8.25 s | Same live council smoke test |
| `llama3.1:8b` | PASS — 1 passed | 17.40 s | Same live council smoke test |
| `qwen3:30b-a3b` | FAIL — 1 failed | 4.38 s | Ollama HTTP 500 before generation: `failed to allocate CUDA_Host buffer of size 12897402880` (12,897,402,880 bytes) |

The installed inventory contains a 30B-total-parameter MoE model with A3B active parameters, not a 20B-A3B model. No 20B-A3B model was present in `ollama list`, so the tested larger model is recorded under its exact installed tag. The failed run did not produce an LLM response; do not treat it as a quality or reasoning evaluation.

### 12.4 Real Ollama embedding + Qdrant integration on main

A direct live integration probe used a newly generated temporary collection named `mv_audit_70fab018e69c`, not `vault_memory`:

- Ollama `nomic-embed-text:latest` returned a **768-dimensional** embedding.
- Temporary Qdrant collection creation succeeded.
- Upsert succeeded; semantic search returned `audit-note-1`.
- Temporary collection deletion returned HTTP 200.
- Final result: **PASS**; the temporary collection was deleted and the production collection was not modified.

The same probe also exposed an existing main-branch defect in `03_IMPLEMENTATION/packages/retrieval/qdrant_retrieval.py`: point IDs are calculated with `abs(hash(point_id)) % (2 ** 31)`. Two separate Python processes returned **1302104753** and **1046550395** for the same `audit-note-1`, confirming process-randomized IDs. The main implementation also attempts collection creation unconditionally and does not propagate `ensure_collection()` / `upsert()` failure from `SemanticRetrieval.reindex()`. These defects are still present on this tested SHA; fixes in the separate `fix/qdrant-stable-point-ids` worktree have not been integrated by this documentation PR.

### 12.5 Correct interpretation and next work

- **Main is not green** on `154dc4274b3b3d634a50c6e9f30b86a77b798590`: full-suite exit code 1, 15 failures, 2 errors.
- Real council execution works on the installed 3B, both 7B, and 8B models. The installed Qwen3 30B-A3B model fails before generation because the runtime cannot allocate a 12.9 GB CUDA host buffer.
- Live embeddings and semantic retrieval work against a temporary Qdrant collection. This does not validate production reindexing, repeat-run idempotency, stale-point reconciliation, or stable point IDs.
- Follow-up should first reconcile the five stale Book-to-Memory generated-artifact/hash failures; fix and test the Windows executable-bit handling and vault-access line-ending contract; diagnose the two frontmatter test setup errors; then re-run the full suite on a newly recorded SHA.
- Qdrant remediation should be integrated only through a reviewed code change and tests. Do not write/reindex the production `vault_memory` collection without separate explicit owner approval.
- No source changes, dependency upgrades, automatic merge, or production collection writes were performed in this validation.


## 13. Recovered tool history and artifact inventory — 2026-10-09

### 13.1 Retained local tool history

The Remote Desktop Commander history query returned **903 retained calls** in memory, of which **886 had timestamps dated 2026-10-09**. The day's recorded call types included 456 `start_process`, 267 `read_process_output`, 91 `read_file`, 26 `list_directory`, 17 `write_file`, 13 `read_multiple_files`, 9 `list_sessions`, 3 `list_processes`, 2 `start_search`, and 1 `get_config`. These are tool-call counts, not 886 separate substantive tasks; many are polling/read operations for long-running commands. Outputs larger than the history system's 4 KiB retention cap are stored as omission markers, so the history is not a complete byte-for-byte transcript of every large output.

The retained history confirms, among other things:
- Main was originally checked at `c5f939185f05c516e1cd46e11b3763026a6137d5`; the recovered report and tests must not be mislabeled as tests of the later `154dc4274b3b3d634a50c6e9f30b86a77b798590`.
- The separate Qdrant worktree was tested and iteratively fixed; a final full suite on that modified worktree returned 2620 passed, 13 skipped, 9 xfailed, exit 0 in 515.46 s.
- The local CLI audit attempts and Qwen allocation failures described in the AI-agent workflow audit were actually attempted, with outcomes recorded there. A CLI/model appearing in the installed inventory is not evidence that it completed the requested audit.
- Attempts to compile/write a second `FULL_FUNCTIONAL_AGENT_COMPLETION_PROMPT_V1.md` into the local main checkout stopped on validation errors (an unfilled TODO placeholder, then invalid Requirements-section bounds). These attempts are **not** a successful committed artifact. The usable master prompt is the version committed on PR #248 as `FULL_FUNCTIONAL_COMPLETION_PROMPT_V1.md`.
- A compiled prompt source was referenced at `C:\Users\Marius\AppData\Local\Temp\AI_MEMORY_VAULT_FULL_FUNCTIONALITY_IMPLEMENT_PROMPT_COMPILED.md` in history. Its current existence was not rechecked, and it is not treated as a durable result.

### 13.2 Recovered local files and their evidentiary scope

| Artifact | Path | Recovered content / status |
|---|---|---|
| Initial validation report | `C:\Users\Marius\Projects\AI_Memory_Vault_VALIDATION_REPORT_20261009.md` | Read successfully; 125 lines. Records historical main SHA `c5f9391...`, 2597 passed / 13 skipped / 9 xfailed in 779.13 s, live `qwen2.5-coder:3b` council smoke test, 768-dimension embeddings, then-unavailable Qdrant, system `pip check` problems, and a clean old main checkout. |
| Qdrant stability audit | `C:\Users\Marius\Projects\AI_Memory_Vault_QdrantFix\10_DOCUMENTATION\audits\2026-10-09-qdrant-stability-audit.md` | Read successfully; 133 lines. Records QDR-001 through QDR-007, temporary-collection live integration, full-suite result on the modified worktree, and the explicit non-use of the production collection. |
| Agent workflow audit | `C:\Users\Marius\Projects\AI_Memory_Vault_QdrantFix\10_DOCUMENTATION\audits\2026-10-09-ai-agent-workflow-audit.md` | Read successfully; 368 lines. Records CLI/model inventory, observable audit attempts, skill-routing findings, Qwen allocation failures, and limitations of the traces. |
| Qdrant technical journal | `C:\Users\Marius\Projects\AI_Memory_Vault_QdrantFix\10_DOCUMENTATION\journal\2026-10-09-qdrant-stability-fix.md` | Read successfully; 105 lines. Records RED/GREEN iterations and the worktree's test sequence. |
| Historical full-suite log (modified worktree) | `%LOCALAPPDATA%\AI_Memory_Vault_QdrantFix\pytest_full_final_after_upsert_guard.log` | Its tail and final summary were read from history: 2620 passed, 13 skipped, 9 xfailed in 515.46 s. This log is historical and not the fresh main run. |
| Historical main test log reference | `50_ARTIFACTS\local_test_run_20261009.log` relative to the old main checkout | Mentioned by the initial validation report as an ignored log. Its complete current contents were not independently re-read during this recovery; do not infer more than the report states. |
| Qdrant worktree code/test artifacts | `C:\Users\Marius\Projects\AI_Memory_Vault_QdrantFix` | The recovered worktree inventory reported modified tracked files `03_IMPLEMENTATION/packages/retrieval/qdrant_retrieval.py`, `20_TESTS/fixtures/unreadable_notes_allowlist.json`, `20_TESTS/test_qdrant_retrieval.py`; and untracked audit/journal docs, `20_TESTS/test_qdrant_live_integration.py`, `20_TESTS/test_qdrant_stable_point_ids.py`, `60_DEPLOYMENT/start_local_qdrant.ps1`. These changes were not copied, stashed, reset, or edited by this fresh main validation. |
| Master prompt and context ledger | PR #248 branch `docs/full-functional-completion-prompt-v1` | Directly committed and fetched from GitHub. The current ledger includes sections 12 and 13; PR #248 remains open and unmerged. |

### 13.3 What was temporary in the fresh validation

- The model-tier JSON used for each live model smoke test was written to the OS temporary directory and removed by the same command after the test.
- The Qdrant integration probe used temporary collection `mv_audit_70fab018e69c`; Qdrant returned HTTP 200 for deletion, and a follow-up lookup returned 404.
- No fresh test report, model output, or code change was written to the local repository. The main checkout remained clean at the tested SHA.
- The new validation evidence was published directly to this PR's context ledger and PR description. The only new durable report from this validation is in GitHub, not a local report file.


## 14. Conversation audit trail and execution contract — 2026-10-09

This section exists because the owner explicitly required that the PR preserve not only outcomes, but also what the owner asked, what the assistant answered, what was attempted, what failed, what was proposed, and what must happen next. The transcript is summarized chronologically; it does not claim to be a byte-for-byte export of the full chat UI. The retained Remote Desktop Commander history and source artifacts are separately described in section 13.

### 14.1 Owner requests and assistant responses (chronological)

1. **Owner — initial request:** asked for a senior AI/ML architecture/code review of the AI Memory Vault RAG/local-model orchestration, including ingestion/chunking, vector-store upsert, retrieval/prompt injection, Ollama timeout/streaming/temperature, bottlenecks, and scalability.
   **Assistant response/clarification:** owner subsequently clarified not to rerun this original broad prompt. The goal was to audit today's entire conversation and work, recover everything claimed/tested/saved, and include it in the existing PR.

2. **Owner:** asked whether that was all the work done today and pointed out the explicit request to test all of `main`.
   **Assistant response:** admitted it had not demonstrated a fresh full-suite run on current main; separated the historical 2,620-pass modified Qdrant worktree run from main.

3. **Owner:** directed recovery of everything, then tests with local 3B/7B/8B models and Qwen3 30B-A3B if it worked, with everything published directly to GitHub rather than left in untracked local files.
   **Assistant response:** initially reported Remote Desktop access failures and that execution had not completed. After access returned, it recovered reports/history, fast-forwarded the clean main checkout from `c5f939185f05c516e1cd46e11b3763026a6137d5` to `154dc4274b3b3d634a50c6e9f30b86a77b798590`, ran a fresh full suite, tested local model smoke tests, and a temporary Qdrant integration probe.

4. **Owner:** explicitly corrected the publication target: “În pr nu pe main direct.”
   **Binding instruction:** do not commit any audit/results/report changes directly to `main`. Use PR #248 branch `docs/full-functional-completion-prompt-v1` for every durable audit update. The main checkout is only the source/test baseline, not the publication target. Keep PR #248 open; do not merge.

5. **Owner — current request:** explained that every activity must be audited, including what the owner asked, what the assistant answered, work checked, proposals, attempts, and all outputs; requested running all tests with all local AIs one at a time, recording whether claims are confirmed/useful and what to do next, entirely in the PR.
   **Assistant commitment:** execute sequentially by model, distinguish source-code tests from model-dependent tests, publish successes and failures directly to PR #248, and avoid presenting a model as tested when the test did not actually invoke it.

### 14.2 Corrections to prior assistant reporting

- An earlier assistant statement that it had “recovered access” and completed all testing was too broad until the exact current-main run and model runs were evidenced. Section 12 now gives the exact SHA, command, exit code, failures and model-specific outcomes available at that point.
- Historical results of 2,597 passed / 13 skipped / 9 xfailed on old main SHA `c5f9391...` and 2,620 passed / 13 skipped / 9 xfailed on modified Qdrant worktree are separate runs. They must never be merged into one total or described as a current-main result.
- The previously completed individual live smoke tests for 3B, 7B and 8B models do not constitute running the entire pytest suite under every model. A sequential full-suite matrix is now being run separately; results must be added to this section only after process exit and captured output.
- The current-main full suite on SHA `154dc4274b3b3d634a50c6e9f30b86a77b798590` failed with 15 failed tests and 2 errors in 638.35 seconds. No source fixes have yet been applied by this evidence-only PR.
- The Qdrant stability changes in `AI_Memory_Vault_QdrantFix` are not part of main or PR #248; the recovery worktree must remain untouched until a separate, reviewed integration plan is approved.
- Any report about tests still running, incomplete logs, unavailable models, or lost output must say so explicitly; do not invent pass counts or treat a launch as a completed test.

### 14.3 Required run record for every local model

For every installed model, append a separate result row containing:
- exact model tag and Ollama model ID where available;
- exact tested source SHA and branch/worktree;
- exact command and relevant environment variables/config;
- test scope (full pytest suite, live provider/council smoke test, embedding/Qdrant integration, or a targeted test);
- start/end time, elapsed duration, exit code, pass/skip/xfail/fail/error counts if captured;
- relevant failing test IDs and error excerpts;
- whether the test truly invoked the model or only exercised deterministic code;
- cleanup verification (temporary config removed; temporary Qdrant collection absent);
- verdict and next action.

The required sequential inventory from the current `ollama list` is: `qwen2.5-coder:3b`, `qwen2.5-coder:7b`, `qwen2.5:7b-instruct`, `mistral:7b-instruct`, `llama3.1:8b`, and `qwen3:30b-a3b`. `nomic-embed-text:latest` is an embedding model and must be evaluated through embedding/retrieval integration rather than counted as a generative council model.

### 14.4 Publication invariant

Every durable finding from this audit must be committed to branch `docs/full-functional-completion-prompt-v1` via PR #248. Do not write audit files or commits directly to `main`. Local temporary configuration files may be created solely to execute a test and must be removed in the same command; they are not the final report. Before declaring completion, re-fetch the PR file and PR metadata from GitHub and verify the latest commit contains the audit and results.


## 15. Sequential local-AI full-suite execution log

### 15.1 Full pytest run — `qwen2.5-coder:3b`

- **Date:** 2026-10-09 (local machine, Marius-PC; exact wall-clock start/end timestamps were not captured by the process wrapper).
- **Source under test:** branch `main`, SHA `154dc4274b3b3d634a50c6e9f30b86a77b798590`. This was a test run only; no changes were committed to main.
- **Ollama model tag / ID:** `qwen2.5-coder:3b` / `f72c60cabf62`.
- **Configuration:** temporary JSON config set `light`, `standard`, and `heavy` tiers to `{"provider":"local","model":"qwen2.5-coder:3b"}`; environment `RUN_LIVE_OLLAMA_TESTS=1`, `OLLAMA_MODEL=qwen2.5-coder:3b`, `OLLAMA_MODEL_TIERS_CONFIG=<TEMP>\mv_model_tiers_test.json`.
- **Command:** `python -m pytest -q --tb=no`.
- **Captured final result:** **3,682 passed, 43 skipped, 9 xfailed, 15 failed, 2 errors** in **993.47 seconds** (16m 33s); pytest exit code **1**. Remote wrapper runtime was 997.13 seconds and confirmed process completion. The progress reached 100%; this is a completed run, not an in-progress estimate.
- **Failing test IDs (15):**
  1. `20_TESTS/test_b2m_leakage_check.py::test_committed_report_matches_a_fresh_run`
  2. `20_TESTS/test_book_to_memory_human_labels.py::test_committed_packet_is_a_fresh_build`
  3. `20_TESTS/test_book_to_memory_real_ablation.py::test_committed_packet_baseline_prompts_are_clean_and_hashes_match`
  4. `20_TESTS/test_book_to_memory_real_ablation.py::test_committed_packet_is_bound_to_the_preregistration`
  5. `20_TESTS/test_book_to_memory_real_ablation.py::test_committed_packet_is_reproducible_from_the_notes`
  6. `20_TESTS/test_import_external_skills.py::test_an_executable_bit_aborts_the_import_even_on_an_allowed_type`
  7. `20_TESTS/test_owner_authority_gate.py::test_settings_command_allows_and_denies`
  8. `20_TESTS/test_vault_access_core.py::test_read_is_verbatim_with_hash_and_exact_lines`
  9–15. Seven parameter cases of `20_TESTS/test_vault_access_ollama_telegram.py::test_reading_a_file_is_extractive_and_never_calls_the_model`: `citește VAULT_STATE`, `Citeste VAULT_STATE.md`, `ce scrie în VAULT_STATE?`, `/read VAULT_STATE`, `vault://governance/vault_state`, `deschide vault state`, and `arată-mi conținutul VAULT_STATE`.
- **Errors (2):** two setup/error cases in `20_TESTS/test_vault_access_core.py::test_unreadable_or_unusual_frontmatter_fails_closed`, including the `Long.md` parameter with repeated frontmatter fields. The captured output was too verbose to preserve a concise traceback in the process response; root cause remains unverified.
- **Observed pattern:** the Vault access failures report line-ending differences (CRLF versus LF); the Book-to-Memory tests report stale/non-reproducible generated artifacts or manifest/preregistration hash mismatches; the external-skill executable-bit guard and owner-authority command test fail their expected enforcement/exit behavior. These are failure classifications from test names and prior captured diagnostics, not yet proven root causes. Do not modify files based solely on these classifications.
- **Model invocation caveat:** the full suite ran with the live-Ollama test flag and all three model tiers configured to this model. The pytest summary does not establish that every test invoked the model; most tests are deterministic code tests. This run is a full-suite result under the model configuration, not proof that every test used generative inference.
- **Cleanup:** the wrapper contains a post-run removal of the temporary tier config. A separate post-exit filesystem check was not captured in the available output, so removal is **expected but not independently verified**. Verify before reusing the config path.
- **Verdict:** full suite completed, **FAIL** (15 failed, 2 errors). This confirms the current-main failures are reproducible in a run configured for the local 3B model, but does not prove they are model-caused.
- **Next action:** publish this result before starting the next model; then run the same full suite serially with `qwen2.5-coder:7b`. Do not repair source during the model matrix, so each run has a stable source SHA and results remain comparable. After the matrix, triage and repair the confirmed project defects on a dedicated feature branch/PR, with regression tests; keep PR #248 as the audit trail and do not commit to main.


## 16. Explicit owner instructions and conversation decisions — 2026-10-09

This section consolidates the operational instructions repeated in the conversation and the supplied transcript file. It is a durable decision record, not a verbatim transcript.

### 16.1 Non-negotiable publication and audit rules

- The user explicitly corrected the workflow: **publish to PR #248, never commit directly to `main`**. PR #248 must remain open and unmerged unless the owner separately authorizes a change.
- The PR audit must include the useful work performed, not only a final verdict: what was requested, commands attempted, failed launch attempts, corrections, configuration, checks, measured outputs, proposed interpretations, unresolved questions, and next steps.
- Clearly separate observed evidence from inference. Never invent a final test count, exit code, model response, or root cause. A smoke test is not a full suite; a model configured in the environment is not proof that every test invoked it.
- Keep each full-suite run serial. Wait for the current process to exit, capture the final pytest summary and exit code, append its record to this ledger, verify that publication succeeded, and only then start the next model.
- Do not edit application source during the model comparison matrix. After the matrix, triage confirmed defects and implement fixes with regression tests on a dedicated feature branch/PR.
- Preserve `C:\Users\Marius\Projects\AI_Memory_Vault_QdrantFix` exactly as found; do not clean, reset, stash, overwrite, or silently discard its uncommitted recovery work.
- Do not write to or reindex production Qdrant collection `vault_memory` without explicit owner approval.

### 16.2 Required order and classification of local model validation

Run the full pytest suite sequentially on the same recorded source SHA with the following generative tags where the runtime permits: `qwen2.5-coder:3b`, `qwen2.5-coder:7b`, `qwen2.5:7b-instruct`, `mistral:7b-instruct`, `llama3.1:8b`, and `qwen3:30b-a3b`. Configure `light`, `standard`, and `heavy` to the model under test, set `RUN_LIVE_OLLAMA_TESTS=1`, and record the exact command/configuration. If a model cannot allocate memory, record a blocked/failed launch with evidence and do not repeatedly retry the same known failing profile without a bounded, justified change.

Evaluate `nomic-embed-text:latest` separately using embedding/retrieval integration; do not label it a generative council model or substitute a generation smoke test for embedding validation.

For every run, preserve model tag and Ollama ID, branch/source SHA, exact command and environment, start/end time if available, elapsed time, exit code, passed/skipped/xfail/failed/error counts, failed test IDs and relevant diagnostics, evidence of actual model invocation, cleanup status, verdict, and next action. Label unavailable fields as unavailable rather than guessing.

### 16.3 Results and state already established in this conversation

- Historical clean `main` at `c5f939185f05c516e1cd46e11b3763026a6137d5`: 2,597 passed, 13 skipped, 9 xfailed. Keep this historical baseline separate from newer source.
- Modified Qdrant recovery worktree: 2,620 passed, 13 skipped, 9 xfailed in a previously reported run. This is not a clean-main result and must not be represented as one.
- Current `main` source used for the model matrix: `154dc4274b3b3d634a50c6e9f30b86a77b798590`.
- Completed full suite with `qwen2.5-coder:3b`: 3,682 passed, 43 skipped, 9 xfailed, 15 failed, 2 errors; 993.47 seconds; exit code 1. Full failing test IDs and caveats are in section 15.1. Preliminary categories are not confirmed root causes.
- `qwen2.5-coder:7b`: the first launch attempt failed due to PowerShell quoting and did not run tests. A corrected multiline here-string launch started the actual full suite as PID 30080 on Marius-PC. In the latest captured output available while preparing this entry, progress had reached 88%; failures and skips had appeared, but no final summary or exit code had yet been captured. This is an in-progress observation, not a final verdict. Update this entry only after collecting the actual terminal output.
- Previously reported smoke tests are separate from full-suite results: `qwen2.5-coder:3b` PASS 3.57 s; `qwen2.5-coder:7b` PASS 7.82 s; `qwen2.5:7b-instruct` PASS 10.34 s; `mistral:7b-instruct` PASS 8.25 s; `llama3.1:8b` PASS 17.40 s; `qwen3:30b-a3b` failed before generation in 4.38 s due to a CUDA host-buffer allocation error. These smoke tests do not satisfy the full-suite matrix.
- Current next action: continue monitoring PID 30080 until it exits; capture the real summary/exit code; verify temporary tier-config cleanup if possible; append the final `qwen2.5-coder:7b` result to section 15.2 and verify the PR update; then continue one model at a time.

### 16.4 Conversation handling and truthfulness

The user asked for the essential contents of the conversation to be preserved in the PR, including user requests, assistant commitments, checks attempted, actual results, and next actions. This ledger is the durable project record; it is not represented as a verbatim transcript. If the exact supplied transcript is needed later, use the attached source artifact rather than reconstructing quoted dialogue from memory. Correct earlier omissions explicitly and do not claim a GitHub publication until the resulting commit is verified.


## 17. Essential findings recovered from the supplied full conversation — 2026-10-09

### 17.1 Source and provenance

The user supplied `Text lipit(20261009-171153).txt` and explicitly confirmed that it contains the full conversation they wanted considered. The source was read from the uploaded attachment, not reconstructed from memory. Source file SHA-256: `82b9b99d8ad6371d8afc6022b4b1bd85bca624c64361bc9748e3dcbdcb252be2`; source size: 64,783 bytes. The file includes an earlier conversation transcript plus the more recent exchange and a prior export verification. This section extracts durable project facts and decisions; it is not a substitute for the original transcript and does not claim to reproduce every message verbatim.

### 17.2 Original architecture review — findings and limits

The conversation began with an architectural code review of `userist123/AI_Memory_Vault_CODEX_READY`. The review explicitly stated that it was a static code review, not a test of the user's local runtime. Its core verdict was **PARTIAL — architectural validation**; it did not recommend rewriting the whole repository.

Findings recorded in that review:
- **Retrieval/ranking:** candidate generation uses BM25 and entity extraction with a default candidate limit of 200. A later ranking path can rely on `RelevanceScorer` (lexical overlap plus confidence) rather than consistently preserving the fused retrieval score. This could promote a high-confidence but query-irrelevant note. Graph expansion was described as disabled/ineffective by default under the documented budget configuration.
- **Markdown/PARA:** frontmatter and metadata such as id, type, tags, lifecycle, verification and provenance were present. The review did not establish that the default MemoryController path universally uses dense embeddings or semantic Markdown chunking; the documented primary path was largely lexical. It proposed section-aware chunks carrying note ID, title, PARA, section path, tags, lifecycle, verification, provenance and content hash. This was a design proposal, not a claim that the schema already existed.
- **Qdrant IDs:** `03_IMPLEMENTATION/packages/retrieval/qdrant_retrieval.py` used `abs(hash(point_id)) % (2 ** 31)`. Python string hashes vary between processes, so the identifier is unsuitable for persistent point identity and can also collide. A deterministic UUID/hash-derived ID was recommended.
- **Reindex reconciliation:** an upsert alone does not remove stale fragments when source chunks disappear or change identity. The review recommended content hashes, incremental reindexing, deletion/reconciliation of stale points, and explicit incomplete status on failed reindex.
- **Prompt safety:** the Ollama assistant had evidence delimiters, JSON-schema validation, quote/number checks, retry and extractive fallback. These controls do not prove semantic entailment, correct entity–value attribution, or complete protection against prompt injection. Access/lifecycle filtering must precede prompt inclusion.
- **Ollama runtime:** factual-task settings included temperature 0, fixed seed, non-streaming responses, context size 8192 and timeout 180 seconds. Tests inspected at that point used a simulated transport; real runtime validation was still required.
- **Scaling:** keep Markdown as canonical source; treat lexical/vector/graph indexes as derived and rebuildable. Prioritize ranking correctness, stable IDs/reconciliation, evidence provenance, real Ollama validation and measurable retrieval benchmarks before adding complexity.

Recommended evaluation metrics included Recall@k, MRR, nDCG@k, irrelevant-result rate, latency and context-budget measurements on a frozen benchmark. Proposed security/regression scenarios included stale vector removal, idempotent reindex, distinct note identity, access filtering before prompt inclusion, prompt injection in Markdown, valid citations supporting invalid conclusions, timeout/model-unavailable cases, truncated/invalid JSON and provenance preservation.

### 17.3 Local test environment discovery and repository identity

The supplied conversation records an initial mistaken path, `C:\Users\Marius\AI_Memory_Vault_CODEX_READY`, which contained only `.git`, an invalid HEAD reference and an `index.lock`; no tests were found there. The user corrected the location context to the Codex Projects copy. The repository actually used for later tests was identified as:

- Path: `C:\Users\Marius\Projects\AI_Memory_Vault_CODEX_READY`
- Branch for historical clean baseline: `main`
- Historical SHA: `c5f939185f05c516e1cd46e11b3763026a6137d5`
- Python: 3.14.2; pytest: 9.0.2; Ollama endpoint: `http://127.0.0.1:11434`
- Generative model: `qwen2.5-coder:3b`; embedding model: `nomic-embed-text:latest` (768 dimensions).

The first historical full-suite report recorded 2,597 passed, 13 skipped and 9 xfailed, with no reported failures. It also recorded a live Ollama generation test returning `LOCAL_MODEL_OK`, 35 prompt tokens and 4 generated tokens. These are historical observations at the old SHA and must not be merged with later results.

### 17.4 Qdrant repair worktree — preserve as separate evidence

The conversation records work in a separate worktree and branch, `fix/qdrant-stable-point-ids`, at `C:\Users\Marius\Projects\AI_Memory_Vault_QdrantFix`. It must remain untouched during the current model matrix unless the owner explicitly changes this instruction.

Reported changes in that worktree:
- Replace process-randomized `hash()` point IDs with deterministic SHA-256-derived IDs.
- Make reindex fail visibly when collection preparation or upsert fails.
- Validate an existing Qdrant collection's vector size and Cosine distance.
- Add `20_TESTS/test_qdrant_stable_point_ids.py` and `20_TESTS/test_qdrant_live_integration.py`.
- Add regression coverage for process-to-process ID stability, distinct notes, reindex idempotency, upsert/collection failures, and filtering of REVIEW notes.
- Add `60_DEPLOYMENT/start_local_qdrant.ps1` and audit/journal documents in that worktree.
- Install dependencies in a dedicated venv; a recorded `pip check` in that venv reported no broken requirements.
- Reported Qdrant Server v1.19.2 on loopback only: `127.0.0.1:6333` HTTP and `127.0.0.1:6334` gRPC; not registered as a Windows service and not configured for automatic startup.
- A live temporary-collection test reportedly exercised Ollama embeddings → Qdrant upsert → semantic search and removed its temporary collection.

**Do not merge the separate result counts into one baseline:** the supplied transcript reports 2,605 passed, 14 skipped and 9 xfailed for one final Qdrant-fix full run; other project context separately reports 2,620 passed, 13 skipped and 9 xfailed for a modified Qdrant worktree run. These are separate recorded observations with potentially different worktree/test states. Neither establishes that clean `main` passed those tests. The transcript says commit, push and merge had not been performed for the Qdrant repair branch at that point.

The transcript also reports an earlier environment-level `pip check` problem outside the dedicated venv: missing `langsmith` required by `langchain-core`, and installed `setuptools 83.0.0` conflicting with a Torch requirement for `setuptools<82`. Keep this distinct from the dedicated-venv dependency check.

### 17.5 Earlier smoke tests versus full-suite evidence

The conversation reports these smoke tests, which are **not** full-suite results:
- `qwen2.5-coder:3b`: PASS, 3.57 s in one recorded live test; another simple local generation test also passed.
- `qwen2.5-coder:7b`: PASS, 7.82 s.
- `qwen2.5:7b-instruct`: PASS, 10.34 s.
- `mistral:7b-instruct`: PASS, 8.25 s.
- `llama3.1:8b`: PASS, 17.40 s.
- `qwen3:30b-a3b`: failed before generation in 4.38 s because Ollama could not allocate a CUDA host buffer of approximately 12.9 GB.
- `nomic-embed-text:latest`: embedding model, 768-dimensional vectors; evaluate through embedding/retrieval integration, not as a generative council model.

These observations motivate the sequential matrix, but must not be presented as proof that each model passes the repository suite.

### 17.6 Current-source baseline and model matrix — keep results distinct

The newer test matrix uses `main` SHA `154dc4274b3b3d634a50c6e9f30b86a77b798590`, not the historical `c5f9391...` baseline. A full baseline run on this newer source was reported to have 15 failed tests and 2 errors. The completed `qwen2.5-coder:3b` full run on the same SHA recorded 3,682 passed, 43 skipped, 9 xfailed, 15 failed and 2 errors in 993.47 seconds, exit code 1. Failure IDs and caveats are documented in section 15.1. The failure categories there are preliminary classifications, not confirmed root causes.

The `qwen2.5-coder:7b` full-suite launch initially failed because of malformed PowerShell quoting; that attempt did not execute tests. The corrected multiline launch started the actual suite as PID 30080. The supplied conversation ends with an observed progress reading around 88% and no captured final pytest summary or exit code. It must remain **in progress / final result unavailable** until the live process is polled and its actual terminal output recovered.

The agreed next order is `qwen2.5:7b-instruct`, `mistral:7b-instruct`, `llama3.1:8b`, and `qwen3:30b-a3b`, after the active 7B run completes. Run only one full suite at a time, use the same source SHA, configure `light`, `standard` and `heavy` tiers to the current model, enable `RUN_LIVE_OLLAMA_TESTS=1`, capture exact counts and exit code, and publish each completed result before starting the next model. Evaluate `nomic-embed-text:latest` separately.

### 17.7 Owner's requested deliverable and operational constraints

The user's end goal is not only a code review. The project must be tested and made usable, with a final report covering required materials/dependencies, tests, scripts, setup, limitations, and next steps. The user explicitly directed that:
1. Keep verification active and start the next model when the current run ends.
2. Record the work, commands, failed attempts, results, questions/answers that affect decisions, findings and next actions in PR #248.
3. After the matrix, repair defects that are confirmed by evidence, with regression tests and a separate implementation PR/branch.
4. Never commit directly to `main`; PR #248 is an open, unmerged audit trail.
5. Do not modify the Qdrant recovery worktree or write/reindex the production collection `vault_memory` without explicit approval.
6. Do not claim all tests passed, all tests invoked the LLM, a cleanup was verified, or a root cause was proven unless the available evidence supports that exact claim.

The conversation's earlier local Qdrant validation report is useful history, but later test runs on a different SHA showed failures. Do not treat the earlier green report as proof that the current `main` is green. The correct next action remains to finish the active model run, publish its verified result, complete the serial matrix, then triage current-main failures and implement confirmed fixes under review.

### 17.8 File integrity/export note

The supplied conversation contains a prior export step that created `conversatie_completa_din_fisier.zip` from an earlier uploaded transcript file and verified that the extracted member matched that source byte-for-byte, reporting source size 28,278 bytes and SHA-256 `88f50e1e546f6648e32e4a20d498126cfee22aec318caa5186be788e10e04313`. That SHA/size refer to the earlier source file, not the currently supplied `Text lipit(20261009-171153).txt`. Keep these provenance values distinct.
