# External Memory Usage Audit Laboratory

This laboratory provides deterministic, evidence-based auditing of external agent conversation transcripts, tool logs, and handoff reports to evaluate whether the `AI_Memory_Vault` was actually utilized or merely cited as a passive claim.

## Core Principle
```text
Vault ACCESS
      ≠
Vault USAGE
      ≠
VAULT-INFLUENCED DECISION
```

A memory system is not demonstrated to be useful merely because an agent has access to it. Useful memory requires an observable, unbroken provenance chain:
$$\text{Retrieve} \longrightarrow \text{Load} \longrightarrow \text{Apply} \longrightarrow \text{Verify} \longrightarrow \text{Outcome}$$

## Audit Stages (11-Stage Pipeline)
1. `MEMORY_DISCOVERY`: Evidence of searching or probing the memory vault index.
2. `MEMORY_RETRIEVAL`: Evidence that specific note IDs or memory entries were fetched.
3. `MEMORY_LOADING`: Evidence that note content was loaded into runtime context.
4. `SKILL_DISCOVERY`: Evidence of searching skill registries or directories.
5. `SKILL_ACTIVATION`: Evidence of loading and following specific `SKILL.md` instructions.
6. `SUBAGENT_ROUTING`: Evidence of delegating to specialized subagents.
7. `DECISION_INFLUENCE`: Causal link connecting retrieved memory/skill to architectural choices.
8. `EXECUTION`: Observable tool calls, code modifications, or command executions.
9. `VERIFICATION`: Empirical proof (unit tests, browser checks, visual renders, diffs).
10. `OUTCOME_CAPTURE`: Recording of success/failure in telemetry or append-only outcome logs.
11. `CONSOLIDATION`: Distilling lessons learned back into permanent memory.

## Evidence Levels
- `VERIFIED`: Concrete tool call, code artifact, test log, or telemetry proof.
- `SUPPORTED`: Indirect or contextual evidence consistent with execution.
- `UNVERIFIED`: Agent verbal assertion without verifiable tool calls or artifacts.
- `MISSING`: Stage was required but completely absent.
- `CONTRADICTED`: Agent claim directly refuted by repository state or test failures.
