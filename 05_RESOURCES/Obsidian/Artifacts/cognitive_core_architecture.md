---
id: "7424d260-4f88-4114-b117-67af0795b8f4"
type: artifact
lifecycle: ACTIVE
category: conversation-artifact
tags: [artifact, obsidian-sync, conversation-evidence]
created: 2026-08-24T21:30:00Z
updated: 2026-08-24T18:31:36.389103+00:00
provenance:
  source_type: execution
  source_ref: "cognitive_core_architecture.md"
confidence: high
verification: verified
relations: []
---

# Artifact: cognitive_core_architecture

# COGNITIVE CORE – ARCHITECTURAL DESIGN (READ-ONLY)

## 1. Current Architecture & State
The system is currently stabilized with **MemoryController** acting as the robust "hippocampal" layer. It interfaces directly with the Vault via **FileStorageEngine**, providing a highly secure, audited, and strictly bounded I/O layer.

## 2. What Already Exists
- **Memory Objects**: Defined by canonical schema (UUID, type, lifecycle, confidence, verification, provenance, tags, content).
- **Relations**: Explicit `relations` array in YAML frontmatter representing static links.
- **Lifecycles & Provenance**: Strict promotion paths (`RAW` -> `REVIEW` -> `ACTIVE`) and source-tracking.
- **Retrieval Engine & Context Economy**: BM25 scoring (`RelevanceScorer`), `ProgressiveDisclosure`, and strict token budget enforcement based on `Principal`.
- **Security & Audit**: Robust RBAC (`Authorizer`) and atomic JSONL audit trails for every operation.

## 3. What is Missing for Autonomous Cognition
- **Working Memory**: A stateful, ephemeral layer to hold currently relevant context during a task.
- **Graph Traversal (Activation Spreading)**: The ability to dynamically traverse the `relations` network to pull in secondary context.
- **Attention Mechanism**: Dynamic scoring of memories based on cognitive context (goals, tasks) rather than just text overlap.
- **Executive & Planning Logic**: A structured layer to formulate goals, decompose tasks, route to tools, and execute autonomously.
- **Reflection & Learning**: Mechanisms to synthesize observations into new long-term memories.

---

## 4. Cognitive Core Architecture
The Cognitive Core represents the "Prefrontal Cortex". It sits strictly **above** the `MemoryController`. It possesses **zero direct filesystem I/O capabilities for the Vault**. Its entire existence is virtual, stateful, and ephemeral, acting as the orchestrator of reasoning, planning, and tool execution.

## 5. Neuron Model
A **Neuron** in this architecture is perfectly mapped 1:1 to a canonical **Memory Object**.
- **No Shadow Models**: The Cognitive Core will NOT invent a parallel data structure. When a neuron is activated, it is simply a retrieved Memory Object dictionary.
- **Representations**: Concepts (`knowledge`), rules (`core`), workflows (`procedure`), outcomes (`experience`), etc.

## 6. Synapse Model
A **Synapse** is the directed relationship between two Neurons, derived from the `relations` array.
- **Semantics**: `related_to`, `supports`, `contradicts`, `derived_from`, `depends_on`, `caused_by`, `implements`.
- **Properties**: 
  - Source UUID, Target UUID
  - Type (semantic meaning)
  - *Future Dynamic Properties (held in Working Memory)*: Activation strength, traversal frequency.

## 7. Activation Model
Activation represents how memories enter active cognition:
1. **Input**: User prompt or internal goal.
2. **Retrieval**: Queries `MemoryController.search()`.
3. **Activation**: Retrieved neurons are loaded into Working Memory with an initial activation energy.
4. **Spreading**: The Core inspects the synapses (relations) of active neurons and conditionally retrieves neighbors via `MemoryController.read()` if activation energy permits.

## 8. Attention Model
Attention determines which neurons in Working Memory deserve cognitive priority.
Factors influencing attention score:
- **Task Relevance**: Similarity to current Executive Goal.
- **Confidence**: High confidence boosts reliability weight.
- **Recency**: Recent observations score higher for immediate tasks.
- **Contradiction**: Conflicting neurons receive an attention spike to force resolution.
- **Decay**: Attention decreases over time unless refreshed by reasoning.

## 9. Working Memory Model
An ephemeral, bounded space isolating active cognition from Long-Term Memory (LTM).
- **Capacity**: Strictly bounded by the Agent's `ContextEconomy` budget.
- **Item Structure**: Active Neurons + Ephemeral task context (observations, tool outputs).
- **Eviction**: Neurons with decaying attention are gracefully evicted (downgraded in progressive disclosure, then dropped) to free capacity.
- **Separation**: Mutations in Working Memory NEVER affect LTM directly.

## 10. Reasoning Boundary
Reasoning is the application of logic over the Working Memory state.
- **Boundary Rule**: Reasoning logic has **NO** access to `MemoryController.set()`, `FileStorageEngine`, or `os`. 
- **Output**: Reasoning produces local conclusions, action decisions, or *proposals*. It can only alter long-term memory by submitting a formal proposal to `MemoryController.propose()`.

## 11. Planning Model
The Planner generates and tracks execution graphs dynamically:
`Goal → Subgoals → Actions → Expected Outcomes → Observations → Adaptation`
- Plans are ephemeral objects in Working Memory.
- Reusable successful plans may be reflected upon and proposed as `procedure` or `project` neurons via LTM consolidation.

## 12. Executive / Orchestrator Model
The central decision loop (OODA loop equivalent):
- **Observe**: Read User Input + Tool Observations + Working Memory.
- **Orient**: Apply Attention and Spreading Activation.
- **Decide**: Plan next action (Retrieve more context, Reason, Use Tool, Terminate).
- **Act**: Execute decision.
**Autonomy Policy Enforcement**: The Executive checks the predefined risk level of the chosen action. Low/Medium risk = auto-execute + audit. High/Critical risk = yield to user for approval.

## 13. Tool / Nervous-System Interface
The boundary for external side-effects (e.g., executing shell commands, calling APIs).
`Executive → Tool Router → Tool Execution → Observation → Working Memory`
- Tools must be registered with predefined capabilities and risk levels.
- Tool outputs are injected as ephemeral observations into Working Memory, NOT immediately into LTM.

## 14. Learning & Reflection Model
After a task concludes, the Executive triggers a Reflection phase:
- Compares *Expected Outcomes* vs *Actual Observations*.
- Generates "What did I learn?", "What failed?".
- Translates insights into candidate Memory Objects (`lesson`, `experience`, `error`).
- Submits candidates via `MemoryController.propose()` with lifecycle `REVIEW`. **NO auto-promotion**.

## 15. Contradiction Handling
If Working Memory detects conflicting Neurons (e.g., A says X, B says NOT X):
- **Detection**: Triggered by reasoning or overlapping constraint violations.
- **Resolution**: The system does NOT silently delete. It creates a new `decision` or `hypothesis` neuron explaining the conflict, and establishes a `contradicts` synapse between A and B.
- If it blocks the critical path, the Executive escalates to the User.

## 16. Memory Consolidation
A future, offline/background process.
- Evaluates raw or dense clusters of `experiences` and `lessons`.
- Synthesizes them into high-level, abstract `knowledge` or `procedures`.
- Ensures provenance traces back to the original cluster of experiences.

---

## 17. Safety Boundaries
The Cognitive Core **MUST NOT**:
1. Bypass `MemoryController` or directly instantiate `FileStorageEngine`.
2. Directly modify, delete, or create `.md` files in the Vault or `RAW_IMPORTS`.
3. Silently overwrite or delete conflicting canonical memories.
4. Bypass `ContextEconomy` bounds when loading Working Memory.
5. Bypass the `Authorizer` (Core operates strictly under `Principal.AI_AGENT`).
6. Execute high-risk external tools without human approval.
7. Auto-promote its own learnings to `ACTIVE` lifecycle.

---

## 18. Architecture Diagram

```mermaid
flowchart TD
    User([User]) -->|Input| Exec
    
    subgraph CognitiveCore [Cognitive Core / Brain]
        Exec[Executive Orchestrator]
        WM[Working Memory]
        Attn[Attention & Activation]
        Plan[Planner]
        Reason[Reasoning Engine]
        Reflect[Reflection & Learning]
        ToolR[Tool Router]
        
        Exec <--> WM
        WM <--> Attn
        Exec <--> Plan
        Exec <--> Reason
        Exec <--> Reflect
        Exec --> ToolR
    end
    
    ToolR -->|Execute| ExternalTools([External Tools])
    ExternalTools -->|Observation| WM
    
    subgraph Hippocampus [MemoryController]
        Ret[Retrieval & Search]
        CE[Context Economy]
        Sec[Security & Audit]
        Cache[Cache]
    end
    
    Attn -->|Query / Read| Hippocampus
    Reflect -->|Propose (REVIEW)| Hippocampus
    
    Hippocampus -->|Validate & Filter| FSE[FileStorageEngine]
    FSE <--> Vault[(Canonical Vault)]
```

---

## 19. Implementation Roadmap

Based on the existing repository state, I propose the following phased implementation:

- **BRAIN-1: Synaptic Graph & Activation**
  - Implement Graph traversal logic that reads `relations` and fetches connected nodes via `MemoryController.read()`.
- **BRAIN-2: Working Memory & Attention**
  - Implement the ephemeral state manager, bounded strictly by `ContextEconomy`.
  - Implement the Attention scoring algorithm.
- **BRAIN-3: Tool Router & Executive Loop**
  - Implement tool registration and risk-based autonomy evaluation.
  - Implement the main OODA loop.
- **BRAIN-4: Reflection Pipeline**
  - Implement the post-task synthesis logic that calls `propose()` with `REVIEW` status.
- **BRAIN-5: Planner & Reasoning Integration**
  - Connect dynamic task breakdown capabilities to the Executive.

---

## 20. Risks
- **Context Bloat**: Spreading activation can quickly exhaust the `AI_AGENT` hard budget if synapses are dense. Mitigation: strict Attention decay and depth limits.
- **Latency**: Extensive graph traversals through `MemoryController.read()` (which currently hits the filesystem layer sequentially) may incur high I/O latency. Mitigation: `Cache` layer optimization.

## 21. Open Architectural Questions
1. **Graph Backend**: Will the volume of relationships eventually necessitate migrating `relations` into a dedicated graph index, rather than parsing YAML arrays per file? (For now, FileStorageEngine suffices, but scale is a concern).
2. **Attention Weights**: How should the baseline weights for Attention (recency vs confidence vs task relevance) be calibrated without machine learning models?

