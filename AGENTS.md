# AGENTS.md — AI Memory System Operating Contract

This repository is the persistent memory and knowledge base for the user's AI system. The AI must protect memory integrity and minimize runtime context.

## Source of Truth

When information conflicts, prefer: user-confirmed facts, direct execution/test evidence, official documentation, project documentation, repeated successful experience, external sources, then inference. Never silently replace stronger evidence with weaker evidence.

## Runtime Context Contract

The runtime MUST use sparse context. Current configuration and governance are organized under the numbered repository spine:
- `00_GOVERNANCE/` — operating rules, agent coordination, review and protocol policy;
- `01_ARCHITECTURE/` — architecture, durable knowledge, graphs and memory design;
- `02_PRODUCT/` — product/project specifications and requirements;
- `04_CONFIG/` — machine-readable runtime configuration;
- `06_INBOX/RAW_IMPORTS/` — untrusted external input.

The previous `99_SYSTEM/` paths are retired and must not be used as canonical locations.

```text
MAX_COUNCIL_AGENTS = 3
MAX_PRIMARY_AGENTS = 1
MAX_SKILLS_PER_AGENT = 2
MAX_TOTAL_SELECTED_SKILLS = 4
MAX_MEMORY_RESULTS = 5
MAX_GRAPH_EXPANSION = 1 hop
MAX_SPECIALIST_OUTPUT = 600 tokens
MAX_SYNTHESIS_INPUT = 2500 tokens
```

### Mandatory routing

```text
CLASSIFY
 -> ROUTE AGENTS
 -> ROUTE SKILLS
 -> RETRIEVE MEMORY
 -> ASSEMBLE MINIMAL CONTEXT
 -> EXECUTE
 -> COMPRESS
 -> SYNTHESIZE
 -> VALIDATE
```

1. Select agents before loading skills.
2. Agent profiles are identity/persona manifests only.
3. Capability mappings are authoritative in the current governance/agent configuration; do not rely on retired `99_SYSTEM/*` paths.
4. Assigned capabilities MUST NOT be loaded wholesale.
5. Load full `SKILL.md` only after selecting the skill for the current task.
6. Select at most two skills per agent and four skills across the council unless the task is explicitly staged.
7. Retrieve memory only after intent classification and stop at the configured budget.
8. Deduplicate shared context; do not repeat the same task, memory, skill or evidence in every specialist prompt.
9. Obsidian navigation links are not runtime context unless directly required.
10. Raw imports, audit reports, progress, handoff, briefing, dispatch and generated artifacts are excluded by default.
11. Specialists return compact evidence; the lead performs one synthesis.
12. No recursive council unless explicitly required; use staged execution for exceptional cases.
13. A proposed council context SHOULD be validated against the current repository validation tooling before execution when a runtime can execute local validation.

## Memory Rules

Do not automatically convert conversations into permanent memory. Preserve useful, reusable, verifiable knowledge and provenance. Prefer small relevant retrieval sets over broad context. Never load the whole Vault.

Memory types include `knowledge`, `project`, `procedure`, `decision`, `experience`, `error`, `lesson`, `preference`, `resource`, and `hypothesis`.

Raw imports remain under `06_INBOX/RAW_IMPORTS/` and are evidence, not canonical knowledge. Secrets must never be stored.

### Unified Secure Retrieval Policy

All agents (Claude Code, Antigravity, Codex, etc.) must access vault memory exclusively through authorized interfaces:
- Primary: REST API `http://localhost:8000/memory/search?query=...`
- Offline fallback: `python -m cognitive_core.recall_cli --query "..."` (versiune securizată, delegată la `MemoryController.search()` și supusă acelorași verificări ale invariantelor `I-001..I-012` și `I-RETRIEVAL` validate prin testele adversariale `P0-001..P0-015`)

Direct unauthenticated filesystem scans, raw `os.walk` traversals, or any attempts to bypass memory trust boundaries (`I-001..I-012`, `I-RETRIEVAL`) are strictly prohibited across all runtimes.

> **Nomenclatură Model de Securitate**:
> - `P0` = Prioritatea Phase 4.3 P0 Security Hardening.
> - `P0-001..P0-015` = Contractele de testare adversarială.
> - `I-001..I-012` = Invariantele canonice de securitate a memoriei.
> - `I-RETRIEVAL` = Invarianta de regăsire unificată securizată.
> - `P1/P2/P3` = Niveluri de prioritate criminalistică (corectitudine, arhitectură, mentenanță).
> - `P16/P17/P18` = Invariante separate de telemetrie hardware și criminalistică.
> - `P0-P18` = Termen umbrelă sintetic; nu reprezintă 19 invariante secvențiale de memorie.

## Multi-Agent Development Coordination

When multiple AI development environments/agents (Antigravity, Claude Code, ChatGPT, Perplexity, etc.) operate on this repository:
1. **Single Source of Truth**: `00_GOVERNANCE/coordination/` is the canonical coordination layer for active and completed work.
2. **Pre-flight Check**: Before touching any code or files, inspect the current coordination state to ensure another AI session is not actively executing or has not already resolved the task.
3. **Execution Ownership**: When starting a task, claim it or check for an active owner. When completing a task, record ownership and timestamp in the current coordination state.
4. **Protected Core Invariant**: Never modify frozen cognitive core modules (`Planner`, `PlanComplexityAnalyzer`, `CouncilBudgetController`, `Council_Orchestrator.py`, `ContextPackBuilder`, `council_token_telemetry.py`) unless explicitly required by an audited specification. All core contracts are validated against the cognitive-core protected-boundary tests.
5. **No Speculation**: Never mark tasks done without attaching empirical execution proof (passing `pytest` suite output).

## Global Production-Consumer Rule

Before constructing a new layer over a component, verify who consumes that component in the production path:

    grep -rl "<module>" --include='*.py' . | grep -v "/tests/\|test_\|benchmarks"

If the result is empty, the component is not integrated. Do not build another layer over it. Cable it into production first, or work on another front.

## Prime Directive

Better memory beats more memory. Better routing beats more agents. Capability is cheap; loaded context is expensive.

