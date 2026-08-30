---
type: system
category: orchestration
status: active
version: 1.0.0
document_kind: runtime_policy
document_status: active
---

# Council Context Budget

## Purpose

The Council exists to improve decisions through selective specialist input. It must not multiply context merely because more agents or skills are available.

## Default Runtime Limits

```yaml
max_council_agents: 3
max_primary_agents: 1
max_skills_per_agent: 2
max_memory_results: 5
max_graph_hops: 1
max_specialist_output_tokens: 600
max_synthesis_input_tokens: 2500
```

These limits are hard defaults. A task may use fewer. Exceeding them requires an explicit complexity/risk reason and staged execution.

## Selection Order

The runtime MUST execute this order:

```text
USER INPUT
  -> INTENT CLASSIFICATION
  -> TASK COMPLEXITY
  -> AGENT ROUTING
  -> SKILL ROUTING
  -> MEMORY RETRIEVAL
  -> CONTEXT ASSEMBLY
  -> SPECIALIST EXECUTION
  -> COMPACT EVIDENCE
  -> LEAD SYNTHESIS
  -> VALIDATION
  -> OUTPUT
```

Never load full agent skill sets before routing.

## Agent Routing

1. Select one primary agent whenever possible.
2. Add a second agent only for a meaningful cross-domain dependency or independent verification.
3. Add a third agent only when independent expertise materially reduces uncertainty or risk.
4. Never invoke the complete council by default.
5. If more than three specialists are required, use staged rounds. Do not give every specialist the full original context.

## Skill Routing

An agent's assigned skills are capability metadata, not automatic prompt content.

For each selected agent:

```text
agent capability index
        -> rank relevant skills
        -> select top 1-2
        -> load full SKILL.md on demand
```

Do not load unused skills.

Do not load skill references, demos, corpora, or auxiliary resources unless the selected skill explicitly requires them.

## Memory Routing

Retrieve memory only after intent classification.

Default:

```text
Top-K = 5
Graph expansion = 1 hop
```

Filter by:

- semantic relevance;
- keyword relevance;
- project relevance;
- confidence;
- recency;
- lifecycle.

Stop retrieval once sufficient evidence is available or the memory budget is reached.

Never load the whole Vault.

## Shared Context

The original user task, core constraints, and selected evidence should be shared by reference in the orchestrator state.

Do not duplicate the same long task description, memory note, skill text, or evidence block into every specialist prompt.

Each specialist prompt should contain only:

```text
role
selected skill(s)
objective
relevant constraints
minimal evidence
expected output schema
```

## Specialist Output Contract

Specialists return evidence, not essays.

Preferred shape:

```yaml
decision: ""
evidence: []
risks: []
unknowns: []
confidence: 0.0
recommended_action: ""
```

Keep the response below 600 tokens by default.

The specialist must not repeat the complete user prompt, system rules, or retrieved notes.

## Lead Synthesis

Only the lead agent performs the final council synthesis.

The lead receives:

- the original objective;
- compact specialist outputs;
- only the memory evidence required to resolve conflicts.

The lead must not reload every specialist's skills.

The lead must not recursively convene the council unless explicitly required.

## Runtime-Excluded Content

The following are excluded from normal council context:

- raw imports;
- audit reports;
- progress files;
- handoff files;
- briefing files;
- dispatch files;
- generated demos;
- large reference corpora;
- Obsidian navigation links;
- unrelated project notes;
- unused skill files.

They may be loaded only when the current task explicitly targets them.

## Repetition Control

Before adding context, calculate whether the same information is already present.

If yes, reference the existing context instead of injecting another copy.

The following should normally appear once per council run:

- user objective;
- global constraints;
- core policy;
- shared memory evidence.

## Complexity Policy

Use the smallest sufficient execution mode:

```text
SIMPLE
  1 agent / 0-1 skills

MODERATE
  1-2 agents / 1-2 skills each

COMPLEX
  2-3 agents / 1-2 skills each

HIGH-RISK
  staged 2-3 agent rounds with validation
```

Complexity must never be inferred from the number of available skills.

## Token Safety Invariants

- No full-council fan-out by default.
- No automatic full-skill loading.
- No automatic graph expansion beyond one hop.
- No automatic raw/audit artifact loading.
- No specialist essay requirement.
- No duplicate shared context.
- No recursive council without explicit need.
- No retrieval after the context budget is satisfied.

## Observability

Every council run should be measurable with:

```text
agents_selected
skills_selected
memory_items_selected
input_tokens_estimate
specialist_output_tokens
synthesis_input_tokens
rejected_context_items
```

The runtime should report these counters in debug/telemetry mode so token regressions can be detected.

## Design Principle

```text
Capability is cheap.
Loaded context is expensive.

Therefore:
INDEX -> ROUTE -> LOAD -> EXECUTE -> COMPRESS -> SYNTHESIZE
```

The Council is successful when it reaches the correct result with the minimum sufficient context, not when it uses the maximum number of agents.
