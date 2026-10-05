# AI Memory Vault — Agent Routing Protocol V1

## Purpose

The Vault is the persistent coordination layer for multiple heterogeneous agents and model runtimes.

The router exists because a memory system contains more than stored knowledge. It must also decide:

- what the task is;
- what capabilities it requires;
- what context is safe and sufficient;
- which agent role is appropriate;
- which runtime can actually satisfy the constraints;
- which model quality tier is justified;
- which skills are necessary;
- whether external research/network access is permitted;
- whether an independent verifier is required;
- what route should be recorded for later evaluation.

A memory store without routing can retrieve context without knowing who should use it, under which policy, or at what cost/risk. A model router alone is also insufficient because agent role, tools, memory, authority and verification are separate constraints.

## Architecture

```text
                    USER / AGENT REQUEST
                            |
                            v
                    TASK NORMALIZATION
                            |
                            v
                     POLICY GATE
                 / privacy / risk /
                / authority / side effects
                            |
                            v
                 CAPABILITY CLASSIFIER
                  / explicit + inferred /
                            |
                            v
                 AGENT CANDIDATE SET
                            |
              +-------------+-------------+
              |                           |
              v                           v
      RUNTIME / MODEL                 SKILL PLAN
        SELECTION                    max + minimal
              |                           |
              +-------------+-------------+
                            |
                            v
                      MEMORY PLAN
                            |
                            v
                  VERIFIER SELECTION
                            |
                            v
                     ROUTE RECEIPT
                            |
                            v
               EXTERNAL EXECUTOR
        (Claude / Codex / Antigravity /
         Perplexity / local LLM / etc.)
                            |
                            v
                  VERIFICATION LAYER
                            |
                            v
                     FEEDBACK STORE
                            |
                            v
                    ROUTING PRIORS
```

The router is deliberately above provider execution and below repository policy.

## Separation of responsibilities

### Router

Answers: "Where should this task go, under which constraints?"

It does not execute the task.

### Planner

Answers: "How should the task be decomposed?"

The router may supply planner constraints, but does not replace the planner.

### Executor

Answers: "Perform the assigned task."

The executor owns its runtime-specific prompt and tools.

### Verifier

Answers: "Did the execution meet the acceptance criteria?"

For high/critical or review-gated tasks the verifier must be independent from the primary execution path.

### Memory controller

Answers: "Which persistent context is eligible to be supplied?"

The router creates a memory plan; the Memory Controller performs retrieval.

## Runtime registry

Runtime identities are declarative metadata only.

Current registry identifiers may include:

- `claude_code`
- `codex`
- `antigravity`
- `perplexity`
- `local_llm`

The registry describes capabilities, privacy boundary, quality ceiling, tool support, code execution, research support, latency/cost scores and independence group.

**Availability is never inferred from registry presence.**

The host must explicitly report available runtimes.

## Agent registry

Agent roles are separate from runtime identities.

An agent role describes:
- domain capabilities;
- acceptable risk;
- minimum quality;
- preferred runtimes;
- default skills;
- skill ceiling;
- independence group;
- prompt-profile ID.

The prompt itself is not stored in the route registry.

## Model quality tiers

The router uses abstract tiers:

`LIGHT -> STANDARD -> HEAVY -> FRONTIER`

The router selects the minimum sufficient tier subject to policy and quality constraints.

Concrete provider/model resolution remains delegated to the existing `ModelTierRouter`.

This prevents routing policy from becoming coupled to vendor model names.

## Hard gates before ranking

A candidate is ineligible if any required constraint fails:

- requested agent/runtime mismatch;
- runtime disabled;
- agent risk ceiling exceeded;
- model quality below minimum;
- requested quality tier unavailable;
- context window insufficient;
- cost budget exceeded;
- latency budget exceeded;
- local-only/privacy conflict;
- external egress forbidden;
- research/network/tool/code/visual/write capability missing;
- explicit capability missing;
- required side effect is not authorized.

Hard failures cannot be overridden by historical performance.

## Ranking

Only eligible candidates are ranked.

Ranking may use:

- capability coverage;
- minimum sufficient quality;
- runtime cost;
- runtime latency;
- historical success;
- historical verification rate;
- historical quality;
- project preference;
- urgency.

The ranking is advisory. Eligibility remains authoritative.

## Independence

For high/critical or review-gated tasks:

`primary.agent_group != verifier.agent_group`

AND

`primary.runtime_group != verifier.runtime_group`

The same model/provider cannot be used as its own independent verifier.

If no independent verifier is eligible, the route is `BLOCKED`.

## Memory plan

Routing produces a bounded memory plan:

- query fingerprint;
- query size;
- maximum notes;
- maximum full documents;
- graph-hop ceiling;
- REVIEW inclusion policy;
- provenance requirement;
- evidence mode.

The route receipt must not store raw sensitive task text merely because it is useful for execution.

The default policy is:
- REVIEW/unverified memory is excluded from execution context unless an explicit policy allows it;
- provenance is required;
- retrieval is bounded;
- the router never bypasses MemoryController trust rules.

## Route states

### ROUTED

Eligible and runtime availability confirmed by host inventory.

### PLANNED

Eligible route found, but availability is not confirmed.

### BLOCKED

No eligible route exists, or a mandatory independent verifier cannot be selected.

No implicit fallback is allowed after BLOCKED.

## Route receipt

Each decision must be reconstructible from:

- decision ID;
- route ID;
- task fingerprint;
- registry fingerprint;
- selected agent;
- selected runtime;
- selected model tier;
- prompt-profile ID;
- selected skills;
- memory plan;
- verifier;
- support agents;
- rejected candidate reasons;
- policy gates;
- warnings;
- execution contract.

A receipt proves what the router decided. It does not prove that the executor ran.

## Prompt separation

The central router must never become a giant universal prompt.

Instead:

```text
Vault policy
      +
router decision
      +
role profile
      +
runtime-specific prompt
      +
project prompt
      =
execution context
```

Each runtime may therefore retain a specialized prompt:
- Claude/Claude Code;
- Codex;
- Antigravity;
- Perplexity;
- local LLM;
- future runtimes.

The router passes a `prompt_profile` reference only.

## Feedback loop

```text
Route
  -> Execute
  -> Observe outcome
  -> Independent verification
  -> Record quality/cost/latency
  -> Update route prior
  -> Future ranking
```

Feedback must be append-only and auditable.

Feedback may change ranking. It may never change:
- privacy policy;
- risk ceilings;
- capability requirements;
- authority boundaries;
- independence rules.

## Research and evaluation

A routing policy must be evaluated against real task slices.

Minimum evaluation dimensions:

- correctness/quality;
- cost;
- latency;
- failure rate;
- verifier agreement;
- route stability;
- false-positive capability matching;
- false-negative route eligibility;
- privacy violations;
- blocked-task precision;
- fallback behavior;
- performance drift after model/runtime changes.

The system should eventually support shadow routing and A/B evaluation without changing the production route for the observed task.

## Security requirements

The router is a policy component.

Therefore:
- registry is schema-validated;
- unknown configuration fields are rejected;
- provider execution is out of process from routing policy;
- prompt text is not silently rewritten;
- runtime availability is host-attested;
- feedback cannot override policy;
- high-risk work requires independent verification;
- route decisions are fingerprinted;
- sensitive goal text is not copied into durable route receipts.

## Canonical implementation

- Runtime: `03_IMPLEMENTATION/packages/routing/`
- Registry: `04_CONFIG/agent_router.json`
- CLI: `python -m routing.route_cli ...`
- Tests: `20_TESTS/test_agent_router.py`

The existing lower-level components remain responsible for their original concerns:
- `ModelTierRouter` — provider/model resolution;
- `SkillRouter` — skill matching;
- `MultiAgentOrchestrator` — worker orchestration;
- `MemoryDataRouter` — safe memory-data egress.

The new Agent Router coordinates these concerns; it does not replace them with duplicate implementations.


## Dispatcher

The Router decides **where** the task should go. The Dispatcher decides **how to deliver the already-approved work packet**.

It must preserve the selected:
- agent;
- runtime;
- prompt-profile ID;
- route ID;
- acceptance criteria;
- constraints;
- bounded memory references;
- timeout and execution limits.

Supported transport classes:

### Local command

Used for runtimes exposed by a local CLI or relay. The command is passed as an argument vector, not shell-concatenated text.

### A2A

Used for remote/opaque agents that expose an A2A Agent Card and task endpoint. A2A is the horizontal agent-to-agent layer; MCP remains the tool/data layer. citeturn788616search2turn437219search1

The dispatcher MUST treat:
`submitted -> working -> input-required/auth-required -> completed/failed/canceled`
as execution state, not as a model narrative. A result is not success until the expected terminal state and acceptance evidence exist. citeturn788616search0

The dispatcher never rewrites a runtime's native prompt. It sends the router-selected `prompt_profile` reference and a bounded task packet.

