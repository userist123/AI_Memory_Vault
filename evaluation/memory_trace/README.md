# Agent Memory Trace Emitter Protocol

This module formalizes the machine-readable **Agent Memory Trace Emitter Protocol** for the AI Memory Vault.

## Core Invariant: `DECLARED ≠ OBSERVED`
Agent self-reports and assertions (e.g. *"I used the Vault"*, *"I verified the tests"*) are never accepted as proof of memory utilization. Memory utilization requires an observable, machine-validated audit trail connecting:
$$\text{Query} \longrightarrow \text{Retrieve} \longrightarrow \text{Load} \longrightarrow \text{Activate Skill} \longrightarrow \text{Decide} \longrightarrow \text{Execute} \longrightarrow \text{Verify} \longrightarrow \text{Outcome}$$

## Trace Structure
Every emitted trace separates:
1. **`declared`**: Claims made by the agent in natural language or summary blocks.
2. **`observed`**: Concrete events backed by verifiable evidence references (tool logs, filesystem reads, subagent dispatches, pytest outputs, telemetry writes).
3. **`links`**: Explicit causal mappings between query, retrieved notes, decisions, executions, verifications, and outcomes.
4. **`status`**: Reconciled trust levels (`VERIFIED`, `DECLARED_ONLY`, `CONTRADICTED`, `MISSING`).

## Trust Levels
- `T0 (DECLARED_ONLY)`: Agent claim without supporting tool/telemetry evidence (Trust weight = 0.0).
- `T1 (TOOL_OBSERVED)`: Verified tool call (e.g. `view_file` on `00_CORE/Memory_Protocol.md`).
- `T2 (EXECUTION_VERIFIED)`: Tool call + verified code modification / test pass.
- `T3 (OUTCOME_VERIFIED)`: Full provenance chain ending in tamper-evident outcome record.
