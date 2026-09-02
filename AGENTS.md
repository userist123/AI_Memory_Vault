# AGENTS.md — AI Memory System Operating Contract

This repository is the persistent memory and knowledge base for the user's AI system. The AI must protect memory integrity and minimize runtime context.

## Source of Truth

When information conflicts, prefer: user-confirmed facts, direct execution/test evidence, official documentation, project documentation, repeated successful experience, external sources, then inference. Never silently replace stronger evidence with weaker evidence.

## Runtime Context Contract

The runtime MUST use sparse context. The machine-readable defaults are in `99_SYSTEM/Council_Runtime_Profile.yaml`. The detailed policy is `99_SYSTEM/Council_Context_Budget.md`; the mandatory execution protocol is `99_SYSTEM/Council_Context_Protocol.md`; skill loading rules are in `99_SYSTEM/Skill_Runtime_Manifest.md`.

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
3. Capability mappings are authoritative in `99_SYSTEM/Agent_Capability_Registry.md`.
4. Assigned capabilities MUST NOT be loaded wholesale.
5. Load full `SKILL.md` only after selecting the skill for the current task.
6. Select at most two skills per agent and four skills across the council unless the task is explicitly staged.
7. Retrieve memory only after intent classification and stop at the configured budget.
8. Deduplicate shared context; do not repeat the same task, memory, skill or evidence in every specialist prompt.
9. Obsidian navigation links are not runtime context unless directly required.
10. Raw imports, audit reports, progress, handoff, briefing, dispatch and generated artifacts are excluded by default.
11. Specialists return compact evidence; the lead performs one synthesis.
12. No recursive council unless explicitly required; use staged execution for exceptional cases.
13. A proposed council context SHOULD be validated against `99_SYSTEM/Council_Context_Validator.py` before execution when a runtime can execute local validation.

## Memory Rules

Do not automatically convert conversations into permanent memory. Preserve useful, reusable, verifiable knowledge and provenance. Prefer small relevant retrieval sets over broad context. Never load the whole Vault.

Memory types include `knowledge`, `project`, `procedure`, `decision`, `experience`, `error`, `lesson`, `preference`, `resource`, and `hypothesis`.

Raw imports remain under `06_INBOX/RAW_IMPORTS/` and are evidence, not canonical knowledge. Secrets must never be stored.

## Multi-Agent Development Coordination

When multiple AI development environments/agents (Antigravity, Claude Code, ChatGPT, Perplexity, etc.) operate on this repository:
1. **Single Source of Truth**: `09_COORDINATION/todo.md` and `09_COORDINATION/lessons.md` are the canonical coordination layer for active and completed work.
2. **Pre-flight Check**: Before touching any code or files, inspect `09_COORDINATION/todo.md` to ensure another AI session is not actively executing or has not already resolved the task.
3. **Execution Ownership**: When starting a task, claim it or check for an active owner. When completing a task, mark it in `09_COORDINATION/todo.md` with `owner: <tool_name>` and an ISO 8601 timestamp.
4. **Protected Core Invariant**: Never modify frozen cognitive core modules (`Planner`, `PlanComplexityAnalyzer`, `CouncilBudgetController`, `Council_Orchestrator.py`, `ContextPackBuilder`, `council_token_telemetry.py`) unless explicitly required by an audited specification. All core contracts are validated against `cognitive_core/tests/test_protected_core_boundaries.py`.
5. **No Speculation**: Never mark tasks done without attaching empirical execution proof (passing `pytest` suite output).

## Prime Directive

Better memory beats more memory. Better routing beats more agents. Capability is cheap; loaded context is expensive.

