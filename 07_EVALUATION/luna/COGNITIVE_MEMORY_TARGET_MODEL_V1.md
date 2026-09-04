# AI Memory Vault — Cognitive Memory Target Model V1

Status: PROPOSED TARGET MODEL — NOT IMPLEMENTED
Authoring agent: LUNA
Project: AI_MEMORY_VAULT
Round: R001
Evidence level: UNVERIFIED / DESIGN TARGET

## 1. Definition

A cognitive memory is a persistent external memory system that can alter the agent's future cognition, not merely provide additional context.

Its defining property is causal influence on future computation:

```text
past experience
    ↓
memory representation
    ↓
current cognitive influence
    ↓
agent decision / action
    ↓
outcome
    ↓
memory reorganization
```

Retrieval is therefore a capability of memory, not its definition.

Core distinction:

```text
retrieval ≠ influence
context ≠ cognition
memory item ≠ memory influence
```

## 2. Cognitive Memory Unit

The canonical long-lived unit should preserve four layers:

```text
EXPERIENCE
  what actually happened

EVIDENCE
  what supports the record and how strongly

PATTERN
  what reusable transition / relation / lesson was abstracted

APPLICABILITY
  when, where and under which conditions the pattern should influence cognition
```

A fifth operational layer exposes the effect on current cognition:

```text
INFLUENCE
  how the applicable memory changes current reasoning, search,
  epistemic behavior or execution
```

This target deliberately keeps full historical evidence separate from compact cognitive recall artifacts.

## 3. MemoryInfluenceState

For a current situation, memory should compile an explicit influence state rather than returning an unstructured list of passages.

Conceptual shape:

```json
{
  "situation": {},
  "relevant_patterns": [],
  "applicability": [],
  "counterexamples": [],
  "uncertainty": {},
  "planning_influence": {},
  "execution_constraints": {},
  "evidence": []
}
```

This object is a design abstraction, not yet a repository implementation contract.

Minimum semantic responsibilities:

- represent the current situation in terms useful for transfer;
- identify reusable patterns rather than only matching text;
- preserve applicability conditions and counterexamples;
- expose uncertainty as structured state;
- describe which cognitive channel(s) may be influenced;
- retain evidence lineage sufficient for later verification.

## 4. Four Cognitive Influence Channels

### 4.1 Representation / Recall Influence

Memory changes the explicit problem representation presented to the agent.

It may:

- surface relevant invariants;
- suppress irrelevant hypotheses;
- expose known failure modes;
- propose structurally analogous situations;
- identify variables and constraints that historically mattered.

The target claim is NOT that external memory directly controls hidden model states. The measurable claim is that memory changes the explicit hypothesis/frame space used by the agent.

### 4.2 Planning Influence

Memory changes the search over candidate strategies or actions.

Conceptually:

```text
planner(s)
   + memory-derived priors / penalties / preferences
   → different search trajectory
```

Possible mechanisms include:

- branch priors;
- strategy preferences;
- historical failure penalties;
- expected-outcome estimates;
- exploration priorities.

The abstraction MUST remain planner-independent. MCTS/PUCT is one possible implementation, not a required dependency.

### 4.3 Epistemic / Uncertainty Influence

Memory changes whether the agent acts, verifies, explores or abstains.

Uncertainty must not collapse into one generic confidence number.

Relevant dimensions may include:

```text
evidence strength
retrieval relevance
applicability confidence
temporal validity
contradiction state
outcome support
```

The design target is an explicit epistemic decision such as:

```text
ACT
VERIFY
EXPLORE
ABSTAIN
```

The system must never invent a numerical threshold merely to create an appearance of rigor; thresholds belong to later experiments.

### 4.4 Execution Influence

Memory-derived invariants may affect whether an external action can be dispatched.

Conceptual path:

```text
agent tool request
      ↓
memory-derived invariant
      ↓
ALLOW / DENY / REQUIRE_CONFIRMATION
      ↓
execution gateway
```

This channel must be deterministic at the execution boundary for high-risk actions. It must not rely on the LLM remembering or obeying a textual warning.

Logit masking / constrained decoding is an optional implementation technique, not a universal architecture requirement.

## 5. Cognitive Loop

The target lifecycle is:

```text
OBSERVATION / TASK
        ↓
EXPERIENCE
        ↓
EVIDENCE
        ↓
PATTERN / EXPECTATION
        ↓
APPLICABILITY
        ↓
MEMORY INFLUENCE
   ┌────┼─────────────┐
   ↓    ↓             ↓
FRAME PLAN       EPISTEMICS
   └────┼─────────────┘
        ↓
AGENT DECISION
        ↓
ACTION / EXECUTION
        ↓
OUTCOME
        ↓
PREDICTION ERROR / FEEDBACK
        ↓
REORGANIZATION
        ↓
UPDATED PATTERN / APPLICABILITY / CUES
        ↓
MEMORY
```

A system is not considered cognitively complete merely because it can retrieve old memories. The outcome-to-reorganization link is essential to the target model.

## 6. Token Economy

Cognitive capability must not be reduced in order to save tokens.

Instead:

```text
FULL MEMORY RECORD
      ↓
COGNITIVE COMPILATION
      ↓
COMPACT INFLUENCE STATE
      ↓
TASK-SPECIFIC RECALL CARD
```

The full record remains persistent and expandable on demand. The agent receives only the compact information that is expected to alter current cognition.

Progressive disclosure and reference-based expansion are preferred over deletion of useful knowledge.

## 7. Experimental Contract

The first experiments must distinguish memory influence from context enrichment.

### Control

```text
same task
same agent/model configuration
memory influence disabled
```

### Treatment

```text
same task
same agent/model configuration
memory influence enabled
```

The treatment must differ in a specific, traceable influence channel.

### Required measurements

1. Decision Delta — did the chosen strategy/action change?
2. Search Delta — did explored alternatives or search order change?
3. Token Delta — did redundant exploration decrease or useful exploration increase?
4. Outcome Delta — did task performance improve or become safer?
5. Attribution — can the difference be traced to a specific MemoryInfluenceState and evidence lineage?

## 8. What Counts as Proof

Strong evidence requires all of the following:

- the same or controlled-equivalent task and agent conditions;
- a clearly defined memory intervention;
- a measurable change in cognition or behavior;
- an outcome difference consistent with that intervention, where applicable;
- traceability from memory record → influence state → agent behavior;
- reproducible execution evidence.

Preferred proof progression:

```text
structural proof
  → behavioral proof
  → runtime proof
  → repeated comparative proof
```

## 9. What Does NOT Count as Proof

The following are insufficient by themselves:

- higher top-k retrieval recall;
- more text inserted into the prompt;
- a benchmark improvement without causal attribution;
- a claimed semantic similarity score;
- a planner that happens to select a better action once;
- model self-report that it "used memory";
- design documents presented as runtime evidence.

## 10. Non-Goals / Guardrails

This target model does NOT require:

- replacing the LLM with a symbolic planner;
- direct access to model hidden states;
- MCTS as the only planning mechanism;
- active-inference theory as an implementation dependency;
- a second monolithic controller replacing the existing architecture;
- weakening the existing memory lifecycle, provenance or security rules.

The goal is to add measurable channels of cognitive influence while preserving the existing Vault capabilities and evidence discipline.

## 11. First Validation Sequence

The first research/implementation sequence is intentionally narrow:

```text
1. Planning Influence
2. Epistemic / Uncertainty Influence
3. Representation / Recall Influence
4. Execution Influence
5. Full outcome → reorganization loop
```

Planning Influence is the first target because its behavioral effect is easiest to isolate without requiring direct access to model internals.

## 12. Acceptance Question

The target model is validated only if we can demonstrate:

```text
WITHOUT MEMORY
    ↓
agent follows one computational path

WITH MEMORY
    ↓
memory changes a defined influence channel
    ↓
agent follows a measurably different path
    ↓
outcome / efficiency / safety changes
    ↓
trace proves the memory intervention caused the difference
```

Until that experiment exists, "cognitive memory" remains a design hypothesis, not an established repository capability.
