---
name: agent-routing
description: Central AI Memory Vault task router. Selects agent, runtime, model tier, skills, memory plan and verifier before external execution.
capabilities:
  - routing
  - orchestration
  - policy
  - model-selection
  - memory-selection
risk: controlled
---

# Agent Routing

Use the canonical implementation in `03_IMPLEMENTATION/packages/routing/`.

## Workflow

1. Construct a `TaskRequest`.
2. Load `04_CONFIG/agent_router.json`.
3. Supply explicitly confirmed runtime availability.
4. Call `AgentRouter.route(...)`.
5. Consume the returned route receipt.
6. Execute using the selected runtime's own prompt/profile.
7. Record outcome through the feedback store.
8. Independently verify high/critical work.

## Important

The router is not an executor.

Do not put runtime-specific prompt bodies inside the registry.

Do not treat a declared runtime as available until the host confirms it.

Do not use feedback as an authorization mechanism.

Use the existing `ModelTierRouter`, `SkillRouter`, `MultiAgentOrchestrator` and `MemoryDataRouter` at their respective boundaries instead of replacing them with another copy.
