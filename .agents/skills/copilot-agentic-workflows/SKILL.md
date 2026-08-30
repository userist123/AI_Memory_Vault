---
name: copilot-agentic-workflows
description: Low-context patterns for hooks, lifecycle workflows and sparse multi-agent orchestration.
---

# Agentic Workflows & Hook Architecture

## Context-Efficient Workflow

Use explicit stages:

```text
CLASSIFY -> ROUTE -> LOAD -> EXECUTE -> VERIFY -> COMPRESS
```

Do not expose every workflow, skill or agent to every model call.

## Pre-Tool Hooks

Validate parameters, permissions and target before a tool call. Keep hook output limited to the information required for the decision.

## Post-Tool Verification

Verify the actual result after execution. Return only the relevant evidence and failures.

## Progressive Disclosure

Progressive disclosure is mandatory:

1. Keep capability metadata small.
2. Select the relevant agent before loading its skills.
3. Select the relevant skill before loading its full content.
4. Load references/examples only when required.
5. Do not duplicate shared context between specialists.

## Multi-Agent Orchestration

Follow `99_SYSTEM/Council_Context_Budget.md`.

Default:

```text
1 agent for simple tasks
2 agents for cross-domain tasks
3 agents for complex/high-risk tasks
```

Never fan out the complete council by default.

## Specialist Output

Specialists should return compact evidence:

```yaml
decision: ""
evidence: []
risks: []
unknowns: []
confidence: 0.0
recommended_action: ""
```

Avoid repeating the user request, system instructions, memory notes or skill text.

## Context Rule

The amount of available knowledge must not determine the amount of loaded context.

Use the minimum sufficient context needed to complete the current step.
