---
id: "knw-agent-memory-trace-protocol-0001"
type: procedure
lifecycle: REVIEW
category: runtime-protocol
tags: [memory-trace, emitter-protocol, provenance, anti-fabrication, declared-vs-observed, validation]
created: 2026-09-02T00:19:00Z
updated: 2026-09-02T00:19:00Z
provenance:
  source_type: execution
  source_ref: "evaluation/memory_trace/trace_validator.py"
confidence: very_high
verification: unverified
relations:
  - type: related_to
    target_id: 330fa4bc-5b7c-4fb0-8d80-bcfa148a29c9
  - type: related_to
    target_id: 86cbfde2-e9f9-4f3d-9cb5-4dc8e8850e07
  - type: related_to
    target_id: knw-memory-usage-audit-principles-0001
  - type: related_to
    target_id: c754b481-44a2-4e2f-9cb2-0be36aebb498
---

# Agent Memory Trace Emitter Protocol Specification

## 1. Purpose & Scope
This protocol establishes the canonical, machine-readable standard for emitting and validating **Agent Memory Traces** across all AI development sessions and autonomous subagents.

The protocol ensures that claimed memory utilization is backed by verifiable execution evidence, enforcing the fundamental axiom:
$$\mathbf{DECLARED \neq OBSERVED}$$

---

## 2. Declared vs Observed Model

1. **Declared State (`declared`)**: What the agent asserts in natural language, summaries, or self-reports.
2. **Observed State (`observed`)**: Concrete events recorded by tool call hooks, filesystem access logs, database queries, subagent dispatches, pytest outputs, and append-only telemetry.
3. **Reconciliation Rule**: Any declared item without a corroborating observed event with valid `evidence_ref` evaluates strictly to `DECLARED_ONLY` (Trust Weight = 0.0).

---

## 3. Canonical Trace Schema

```yaml
trace_id: "tr-YYYYMMDD-XXXX"
task_id: "task-identifier"
session_id: "sess-identifier"
agent_id: "agent_name"
timestamp: "ISO_8601_TIMESTAMP"

query: "User task prompt or classified goal"

declared:
  retrieved_memories: ["note_path_or_id"]
  activated_skills: ["skill_name"]
  activated_subagents: ["subagent_role"]
  decisions_influenced: ["specific architectural decision"]
  verification_claims: ["empirical test claim"]
  outcome_claims: ["success | fail | partial"]

observed:
  retrieval_events:
    - event_id: "evt-ret-01"
      memory_id: "00_CORE/Storage_Architecture.md"
      source: "filesystem_read"
      evidence_ref: "tool_calls[0]: view_file(...)"
  memory_load_events:
    - event_id: "evt-load-01"
      memory_id: "00_CORE/Storage_Architecture.md"
      evidence_ref: "context_pack: bytes=2450"
  skill_load_events:
    - event_id: "evt-sk-01"
      skill_name: "sqlite-wal-optimization"
      state: "ACTIVATED"
      evidence_ref: "tool_calls[1]: view_file(.agents/skills/...)"
  subagent_events:
    - event_id: "evt-sub-01"
      subagent_role: "Database Debugger"
      dispatch_event: "tool_calls[2]: invoke_subagent(...)"
      evidence_ref: "conversation://<subagent_id>"
  decision_events:
    - event_id: "evt-dec-01"
      decision: "Configured PRAGMA busy_timeout=5000"
      governing_memory_id: "00_CORE/Storage_Architecture.md"
  tool_events:
    - event_id: "evt-tool-01"
      tool: "run_command"
      command: "python -m pytest ... -q"
  verification_events:
    - event_id: "evt-ver-01"
      type: "test_pass"
      evidence_ref: "exit_code=0, output='5 passed in 0.12s'"
  outcome_events:
    - event_id: "evt-out-01"
      outcome: "success"
      verification_method: "test_pass"
      evidence_ref: "telemetry: outcome_events.jsonl append"

links:
  query_to_memory: "VALID | BROKEN | N/A"
  memory_to_skill: "VALID | BROKEN | N/A"
  memory_to_decision: "VALID | BROKEN | N/A"
  decision_to_execution: "VALID | BROKEN | N/A"
  execution_to_verification: "VALID | BROKEN | N/A"
  verification_to_outcome: "VALID | BROKEN | N/A"

status:
  memory_usage: "VERIFIED | DECLARED_ONLY | MISSING"
  skill_usage: "VERIFIED | DECLARED_ONLY | MISSING"
  decision_influence: "MEMORY_INFLUENCE_VERIFIED | MEMORY_INFLUENCE_UNVERIFIED"
  verification: "VERIFIED | DECLARED_ONLY | MISSING"
  outcome: "VERIFIED | DECLARED_ONLY | MISSING"
  trust_level: "T0_DECLARED_ONLY | T1_TOOL_OBSERVED | T2_EXECUTION_VERIFIED | T3_OUTCOME_VERIFIED"
  completeness: "COMPLETE | PARTIAL | BROKEN"
  first_missing_link: "RETRIEVE | LOAD | DECIDE | VERIFY | OUTCOME | null"
```

---

## 4. Trust Level Hierarchy

```text
T3_OUTCOME_VERIFIED    (Weight: 1.0) — Complete provenance chain ending in tamper-evident telemetry
          ▲
T2_EXECUTION_VERIFIED  (Weight: 0.8) — Tool execution + passing verification output
          ▲
T1_TOOL_OBSERVED       (Weight: 0.5) — Verified tool call in transcript without full outcome chain
          ▲
T0_DECLARED_ONLY       (Weight: 0.0) — Agent verbal assertion lacking observable tool logs
```

---

## 5. Skill & Subagent Lifecycle States

### Skill Lifecycle States:
- `DISCOVERED`: Skill directory listed or capability registry queried.
- `LOADED`: `SKILL.md` file read via `view_file`.
- `ACTIVATED`: Skill guidelines adopted by council / agent context.
- `APPLIED`: Domain instructions from skill executed in code.
- `VERIFIED`: Output passes skill-specific verification rules.

### Subagent Lifecycle States:
- `DISPATCHED`: `invoke_subagent` tool call recorded.
- `EXECUTED`: Subagent conversation transcript populated.
- `SYNTHESIZED`: Subagent return payload ingested by primary synthesizer.

---

## 6. Runtime Observed Memory Trace Implementation

The **Actual Runtime Observed Memory Trace** is deterministically emitted by [`memory_controller/memory_trace.py`](file:///memory_controller/memory_trace.py) and passively hooked into [`memory_controller/context/pack_builder.py`](file:///memory_controller/context/pack_builder.py):

### Essential Distinction:
$$\mathbf{OBSERVED \equiv \text{Final Context Presence}}$$
$$\mathbf{OBSERVED \not\equiv \text{USED (Causal Influence)}}$$

1. **Observed Definition**: The memory note ID was physically included in the final context pack passed to the model after candidate filtering, score ranking, progressive disclosure, and budget degradation.
2. **Used Definition**: The model's generated output causally depended on the retrieved knowledge (unmeasured and outside current scope).
3. **Instrumentation Point**: In `ContextPackBuilder.build()`, right before returning the validated `pack`, `record_observed_memory_trace()` appends an immutable JSON line to `telemetry/observed_memory_traces.jsonl`.
4. **Reconciliation Statuses**:
   - `ACKNOWLEDGED_CLEAN`: Declared memory IDs match Observed memory IDs exactly.
   - `FABRICATION_DETECTED`: The agent claimed memory IDs that were never present in the final context pack.
   - `UNACKNOWLEDGED_RETRIEVAL`: The context pack contained memory IDs that the agent omitted from its report.
   - `OBSERVATION_FAILED`: Telemetry persistence was unavailable or failed to write (prevents false-positive OBSERVED claims).

5. **Post-Build Transformation Audit**:
   - An exhaustive audit of all callers (`MemoryController.read()`, `MemoryController.cognitive_read()`, `MemoryController.search()`, `FinancialSearchEngine.search_financial()`) confirmed that none mutate, prune, or reorder `pack["results"]` after `ContextPackBuilder.build()`.
   - Therefore, `PACK_OBSERVED` is structurally identical to `MODEL_CONTEXT_OBSERVED` across the entire repository.


