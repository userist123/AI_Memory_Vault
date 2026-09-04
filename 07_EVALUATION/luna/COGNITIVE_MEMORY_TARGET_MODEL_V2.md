# AI Memory Vault — Cognitive Memory Target Model V2

Status: PROPOSED TARGET MODEL — NOT IMPLEMENTED
Authoring agent: LUNA
Project: AI_MEMORY_VAULT
Round: R001
Evidence level: UNVERIFIED / DESIGN TARGET

## 1. Definition

A cognitive memory is a persistent external epistemic substrate that can causally influence future agent computation through explicit runtime interfaces, not merely add retrieved context.

The Vault persists experience, evidence, reusable patterns and applicability conditions. Runtime components consume compiled memory influence to alter representation, search, epistemic behavior or execution.

Core distinction:

```text
retrieval != influence
context != cognition
memory repository != runtime influence mechanism
```

## 2. Architectural Boundary

The target model explicitly separates two layers:

```text
PASSIVE EPISTEMIC SUBSTRATE
AI Memory Vault
  - experience
  - evidence
  - pattern
  - applicability
  - provenance
  - temporal state

ACTIVE RUNTIME INTERFACES
  - representation/frame compiler
  - planning/search harness
  - epistemic gate
  - execution gateway
```

The Vault may persist and compile influence signals. It cannot claim direct control over an LLM's hidden states, greedy decoding, planner or tool executor unless the corresponding runtime interface exists.

Therefore:

- Planning Influence is inactive in a plain single-pass ReAct/LLM pipeline unless an explicit search harness consumes it.
- Execution Influence is authoritative only at a deterministic execution boundary.
- Representation Influence is observable through changes in the explicit hypothesis/frame state, not hidden neural state access.

## 3. Cognitive Memory Unit

The persistent unit keeps five semantic layers:

```text
EXPERIENCE
  what happened

EVIDENCE
  what supports the record

PATTERN
  reusable transition / relation / lesson

APPLICABILITY
  conditions under which transfer is valid

INFLUENCE
  how applicable memory should alter current computation
```

Full evidence stays external. Compact influence artifacts are compiled on demand.

## 4. MemoryInfluenceState

For a current task, memory may compile:

```json
{
  "situation": {},
  "candidate_patterns": [],
  "applicability": [],
  "counterexamples": [],
  "uncertainty": {},
  "representation_influence": {},
  "planning_influence": {},
  "epistemic_influence": {},
  "execution_constraints": {},
  "evidence_refs": []
}
```

This remains a conceptual contract until implementation and runtime tests define exact schemas.

## 5. Four Influence Channels

### 5.1 Representation / Recall

Memory may alter the explicit frame of the problem by surfacing structural invariants, relevant variables, analogous situations and known failure modes.

Measurable target:

```text
memory-off hypothesis/frame set
!=
memory-on hypothesis/frame set
```

This is a claim about observable agent state, not hidden activations.

### 5.2 Planning

Memory may alter search when a runtime search harness consumes memory-derived priors, penalties, preferences or expected outcomes.

Abstract contract:

```text
search(s)
+ memory influence
-> different branch ordering / visitation / values
```

MCTS/PUCT, beam search, candidate reranking and other mechanisms are valid implementations. No single planner is required.

Precondition:

```text
NO SEARCH HARNESS => NO DIRECT PLANNING INFLUENCE
```

In a linear zero-shot pipeline, the same signal becomes advisory context and must be classified as Representation Influence instead.

### 5.3 Epistemic / Uncertainty

Memory may change whether the agent acts, verifies, explores or abstains.

Relevant dimensions include evidence strength, applicability, temporal validity, contradiction state and outcome support. A single scalar confidence is insufficient as the canonical epistemic state.

Target decisions:

```text
ACT
VERIFY
EXPLORE
ABSTAIN
```

Thresholds remain experimental parameters, not design assumptions.

### 5.4 Execution

Memory-derived constraints may affect tool dispatch through an explicit gateway:

```text
agent request
   -> constraint evaluation
   -> ALLOW / DENY / REQUIRE_CONFIRMATION
   -> executor
```

The deterministic gateway, not the LLM's compliance, is authoritative for high-risk actions.

Logit masking and constrained decoding are optional techniques, not universal requirements.

## 6. Applicability Contract

Semantic similarity is candidate generation, not proof of transfer.

An applicability decision should be based on explicit conditions such as:

```text
Environment predicates
Relational topology
Boundary contraindications
Expected post-condition
Temporal validity
Evidence support
```

A memory can therefore be relevant but not applicable.

## 7. Reorganization / Learning

The target loop is:

```text
OBSERVATION / TASK
  -> EXPERIENCE
  -> EVIDENCE
  -> PATTERN / EXPECTATION
  -> APPLICABILITY
  -> MEMORY INFLUENCE
  -> AGENT DECISION
  -> ACTION / EXECUTION
  -> OUTCOME
  -> PREDICTION ERROR / FEEDBACK
  -> REORGANIZATION
  -> updated cues / pattern / applicability
  -> MEMORY
```

Reorganization must preserve provenance and avoid promoting coincidence into causal knowledge without sufficient evidence.

Supported transformations may include:

- cue/link reweighting;
- conditional boundary splitting;
- applicability narrowing or expansion;
- value-prior updates when a planner exists;
- quarantine of weak or contradictory lessons.

## 8. Token Economy

The objective is lower token transport with preserved or increased cognitive capability.

```text
FULL MEMORY RECORD
   -> offline/low-frequency extraction
   -> compact influence representation
   -> task-specific recall card
   -> optional evidence expansion
```

No capability is removed merely to save tokens. Compression must preserve the operators, conditions and negations that determine applicability.

## 9. Experimental Proof Standard

A causal claim requires:

1. matched task/model/runtime conditions;
2. one defined memory intervention;
3. measurable computational or behavioral change;
4. outcome/efficiency/safety effect where applicable;
5. traceable memory -> influence -> behavior linkage;
6. reproducible execution evidence.

A single successful run is not sufficient evidence of causal influence.

## 10. Planning Influence Gate Experiment

The first experiment isolates Planning Influence in a real search harness.

Control:

```text
same task
same model
same planner
memory content may be visible as advisory context
planner priors remain uniform
```

Treatment:

```text
same task
same model
same planner
same informational content
memory is additionally compiled into planner priors/penalties
```

The critical contrast is not "memory text versus no text". It is:

```text
ADVISORY INFORMATION
vs.
COMPUTATION-LEVEL SEARCH INFLUENCE
```

## 11. Failure Guards

The model explicitly rejects:

- assuming a planner exists when it does not;
- treating vector similarity as causal applicability;
- converting one accidental success into a causal invariant;
- globally blocking an action from a single context-specific failure;
- inflating prompts with verbose schemas as a substitute for cognition;
- claiming influence from model self-report alone.

## 12. Acceptance Criterion

The target crosses from "memory as retrieved context" toward "memory as active cognitive substrate" only when a controlled runtime experiment demonstrates that a defined memory intervention changes a computation channel and that the change is traceable and reproducible.

The first acceptance target is Planning Influence. Subsequent targets are Epistemic Influence, Representation Influence, Execution Influence and the full Outcome -> Reorganization loop.

Until runtime evidence exists, this file remains a design hypothesis.
