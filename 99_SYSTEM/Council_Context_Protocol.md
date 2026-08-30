---
type: system
category: orchestration
status: active
version: 1.0.0
document_kind: runtime_protocol
document_status: active
---

# Council Context Protocol

This protocol is mandatory for every council invocation.

## Execution Gate

```text
1. CLASSIFY task
2. SELECT minimum sufficient agent(s)
3. QUERY Agent_Capability_Registry
4. SELECT maximum 2 skills per agent
5. RETRIEVE maximum 5 memory items
6. DEDUPLICATE shared context
7. LOAD selected SKILL.md only
8. EXECUTE specialist with compact output contract
9. COMPRESS evidence
10. SYNTHESIZE once
11. VALIDATE
```

## Hard Denials

Reject or defer any runtime plan that:

- loads all agents;
- loads all skills assigned to an agent;
- loads the whole Vault;
- injects Obsidian navigation links without task relevance;
- injects audit/progress/handoff/briefing/report artifacts by default;
- repeats the complete user prompt for every specialist;
- repeats identical memory evidence between specialists;
- recursively starts another council without an explicit escalation reason;
- exceeds the context budget without staged execution.

## Context Layers

### L0 — Identity
Agent name, role and task objective only.

### L1 — Capability
Only selected skill IDs and their loaded instruction bodies.

### L2 — Evidence
Only relevant memory and task evidence.

### L3 — Execution
Tools, files or code explicitly required by the task.

Never preload L1-L3 globally.

## Specialist Output

Use this compact contract:

```yaml
decision: ""
evidence: []
risks: []
unknowns: []
confidence: 0.0
recommended_action: ""
```

Target <= 600 output tokens.

## Lead Input

The lead receives one copy of the objective and a deduplicated set of specialist evidence. It does not reload specialist profiles or skills.

## Escalation

If the task cannot be solved within the default budget:

1. explain the missing evidence category;
2. execute a staged second round;
3. pass only new evidence to the next round;
4. never replay the full previous context.

## Token Accounting

Every implementation of the runtime should expose:

```text
agents_selected
skills_selected
memory_items_selected
input_tokens_estimate
specialist_output_tokens
synthesis_input_tokens
rejected_context_items
deduplicated_context_items
```
