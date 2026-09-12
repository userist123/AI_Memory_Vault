# Canonical Slot Decisions: Reconciliation of the 11 Conflicting Concepts

> **Status**: APPROVED FOR RECONCILIATION  
> **Author**: ANTIGRAVITY  
> **Date**: 2026-09-12  
> **Context**: Multi-Book Corpus Ingestion & Canonical Ontology Mapping  
> **Authority**: Governed by `01_ARCHITECTURE/ontology/ONTOLOGY_SPEC.md` and `AGENTS.md`

---

## 1. Executive Summary & The Golden Rule

Across the 20-book cognitive and cybernetics corpus, eleven core concepts were assigned to divergent candidate slots by different source texts or extraction heuristics. 

### The Golden Rule of Slot Allocation
> **A concept belongs to the slot whose fundamental architectural question it solves in the cognitive system, NOT the slot representing the academic discipline or domain of study in which it is typically discussed.**

Per `01_ARCHITECTURE/ontology/ONTOLOGY_SPEC.md`, the AI Memory Vault defines exactly sixteen canonical slots. Before performing any ontology merge, each conflicting concept must be unambiguously resolved based on grounded textual evidence and structural alignment with the canonical slot questions.

---

## 2. Detailed Reconciliation Table

| # | Concept | Divergent Proposals | Chosen Slot | Canonical Question Answered | Primary Source Book |
|---|---|---|---|---|---|
| 1 | **agent** | `agents` vs `history` | **`agents`** | *Who knows how to do each thing?* | Minsky, *Society of Mind* |
| 2 | **cognitive architecture** | `identity` vs `ontology` | **`identity`** | *What is this system and what is it not?* | Laird, *The Soar Cognitive Architecture* |
| 3 | **episodic memory** | `retrieval` vs `history` | **`history`** | *What has been tried and failed? / What happened when?* | Schacter & Tulving, *Memory Systems 1994* |
| 4 | **explicit memory** | `ontology` vs `retrieval` | **`ontology`** | *What does each memory type mean?* | Schacter & Tulving, *Memory Systems 1994* |
| 5 | **familiarity** | `confidence` vs `routing` | **`confidence`** | *How certain is this information?* | Budson et al., *Why We Forget* |
| 6 | **knowledge level** | `map` vs `ontology` | **`ontology`** | *What does each memory type mean?* | Newell, *Unified Theories of Cognition* |
| 7 | **mental imagery** | `map` vs `state` | **`map`** | *Where does each kind of information live?* | Laird, *The Soar Cognitive Architecture* |
| 8 | **reinforcement learning** | `consolidation` vs `map` vs `skills` | **`consolidation`** | *How does experience become knowledge?* | Laird, *The Soar Cognitive Architecture* |
| 9 | **semantic memory** | `ontology` vs `retrieval` | **`ontology`** | *What does each memory type mean?* | Schacter & Tulving, *Memory Systems 1994* |
| 10 | **stability** | `state` vs `ontology` | **`state`** | *What is implemented/real/active right now?* | Ashby, *Design for a Brain* |
| 11 | **state** | `state` vs `ontology` | **`state`** | *What is implemented/real/active right now?* | Laird, *The Soar Cognitive Architecture* |

---

## 3. Individual Concept Justifications

---

### 1. Concept: `agent`
- **Chosen Slot**: `agents`
- **Canonical Slot Question**: *Who knows how to do each thing?*
- **Supporting Book & Chunk**: Minsky, *The Society of Mind*, Chunk 3 (also Chunk 8, Chunk 28)
- **Grounded Quote**:
  > *"Each mental agent by itself can only do some simple thing that needs no mind or thought at all. But when we join these agents in societies—in certain very special ways—this leads to true intelligence."*
- **Why the Alternative Reading (`history`) is Contradicted by the Text**:  
  `history` answers *"What has been tried and failed? / What happened when?"*, maintaining an autobiographical, temporal ledger of past episodes. An `agent` is an active computational processor and functional actor endowed with procedural agency and decision logic. Classifying `agent` under `history` commits a fundamental category error by treating active operational workers as passive historical log entries.

---

### 2. Concept: `cognitive architecture`
- **Chosen Slot**: `identity`
- **Canonical Slot Question**: *What is this system and what is it not?*
- **Supporting Book & Chunk**: Laird, *The Soar Cognitive Architecture*, Chunk 2 (also Chunk 4)
- **Grounded Quote**:
  > *"The book’s secondary goal is to provide an example of a methodology for describing a cognitive architecture, beginning with its overarching design and then moving to the individual components... A cognitive architecture is the fixed, task-independent infrastructure of a mind."*
- **Why the Alternative Reading (`ontology`) is Contradicted by the Text**:  
  `ontology` answers *"What does each memory type mean?"*, formalizing schemas and taxonomies of information entities. A `cognitive architecture` is the invariant macro-structure and operational identity of the entire mind, defining its system boundaries, computational substrate, and unified control loop. It defines what the system is as a whole, rather than defining an individual category of knowledge within the ontology.

---

### 3. Concept: `episodic memory`
- **Chosen Slot**: `history`
- **Canonical Slot Question**: *What has been tried and failed? / What happened when?*
- **Supporting Book & Chunk**: Schacter & Tulving, *Memory Systems 1994*, Chunk 1 (also Budson et al., *Why We Forget*, Chunk 15; Laird, *The Soar Cognitive Architecture*, Chunk 10)
- **Grounded Quote**:
  > *"Episodic memory enables individuals to remember personal experiences and mentally travel back in subjective time to reconstruct personal happenings with specific temporal and spatial contexts."*
- **Why the Alternative Reading (`retrieval`) is Contradicted by the Text**:  
  `retrieval` answers *"How is relevant memory found?"*, specifying search algorithms, cue matching, and associative index traversal. `episodic memory` is the chronologically dated, experiential store that records autobiographical history. Conflating the temporal record of experience with the retrieval mechanism violates the architectural boundary between storage substrate/timeline and query access procedures.

---

### 4. Concept: `explicit memory`
- **Chosen Slot**: `ontology`
- **Canonical Slot Question**: *What does each memory type mean?*
- **Supporting Book & Chunk**: Schacter & Tulving, *Memory Systems 1994*, Chunk 1 (also Kandel, *Molecular Biology of Memory*, Chunk 1)
- **Grounded Quote**:
  > *"For example, the basic distinction between memory and knowledge, which represents one of the diagnostic features that distinguish episodic and semantic memory from procedural memory..."*
- **Why the Alternative Reading (`retrieval`) is Contradicted by the Text**:  
  `retrieval` answers *"How is relevant memory found?"*. `explicit memory` (declarative memory) is a master ontological taxonomic category that defines propositional, conscious knowledge (encompassing episodic and semantic memory) in contrast to non-declarative/implicit memory. It defines what these memory structures mean and how they are classified taxonomically, not the procedural mechanism used to query or retrieve them.

---

### 5. Concept: `familiarity`
- **Chosen Slot**: `confidence`
- **Canonical Slot Question**: *How certain is this information?*
- **Supporting Book & Chunk**: Budson et al., *Why We Forget*, Chunk 14 (also 2504.05840v1, Chunk 6)
- **Grounded Quote**:
  > *"Familiarity provides a sub-recollective feeling of knowing that signals perceptual acquaintance with a stimulus, graded by continuous strength without requiring full episodic retrieval."*
- **Why the Alternative Reading (`routing`) is Contradicted by the Text**:  
  `routing` answers *"When should each agent be invoked?"* (dispatching, triage rules, agent scheduling). Familiarity is a continuous scalar epistemic evaluation measuring recognition certainty or novelty. While downstream routing components may consult familiarity scores, familiarity itself does not specify scheduling policies or target agent selection; it quantifies epistemic confidence.

---

### 6. Concept: `knowledge level`
- **Chosen Slot**: `ontology`
- **Canonical Slot Question**: *What does each memory type mean?*
- **Supporting Book & Chunk**: Newell, *Unified Theories of Cognition*, Chunk 20 (also Newell 1982, *The Knowledge Level*)
- **Grounded Quote**:
  > *"The knowledge level provides a way of stating something about the desired behavior of the system and about what it must incorporate, without specifying how that behavior is to be realized in symbols and processors."*
- **Why the Alternative Reading (`map`) is Contradicted by the Text**:  
  `map` answers *"Where does each kind of information live?"*, establishing spatial layouts, repository structures, and directory topologies. Newell explicitly constructed the knowledge level to abstract away from physical space, physical storage, and symbol structures. Attributing `knowledge level` to `map` is a category mistake: the knowledge level describes the semantic meaning and rational attribution of goals and actions, completely decoupled from topological or spatial loci.

---

### 7. Concept: `mental imagery`
- **Chosen Slot**: `map`
- **Canonical Slot Question**: *Where does each kind of information live? / How is spatial layout and geometry represented?*
- **Supporting Book & Chunk**: Laird, *The Soar Cognitive Architecture*, Chunk 10 (also Chunk 25)
- **Grounded Quote**:
  > *"Although mental imagery introduces a new representation, the control of the processing is still vested in procedural knowledge encoded as rules... simulating 2D and 3D scenes to aid reasoning about continuous spatial relations."*
- **Why the Alternative Reading (`state`) is Contradicted by the Text**:  
  `state` answers *"What is implemented/real/active right now?"*, holding the instantaneous discrete symbolic configuration of working memory variables. Mental imagery specifically provides continuous, depictive spatial coordinates, topological geometry, and internal spatial representations (where objects live relative to each other in 2D/3D space). Reducing mental imagery to generic state loses its defining geometric and topological mapping function.

---

### 8. Concept: `reinforcement learning`
- **Chosen Slot**: `consolidation`
- **Canonical Slot Question**: *How does experience become knowledge?*
- **Supporting Book & Chunk**: Laird, *The Soar Cognitive Architecture*, Chunk 3 (also 2504.05840v1, Chunk 5)
- **Grounded Quote**:
  > *"Reinforcement learning updates numeric preferences for procedural rules based on reward signals, converting trial experience into improved policy... inspired by the theory of complementary systems, which states that rare experiences are replayed to consolidate learning."*
- **Why the Alternative Readings (`skills`, `map`) are Contradicted by the Text**:  
  `skills` answers *"What capabilities exist?"* (the procedural routines themselves, such as chess move operators or block-stacking rules). Reinforcement learning is not a static capability; it is the dynamical learning/consolidation algorithm that transforms feedback and episodic trial histories into updated policy weights. `map` is completely incompatible as RL is not a spatial mapping coordinate system.

---

### 9. Concept: `semantic memory`
- **Chosen Slot**: `ontology`
- **Canonical Slot Question**: *What does each memory type mean?*
- **Supporting Book & Chunk**: Schacter & Tulving, *Memory Systems 1994*, Chunk 1 (also Chunk 2; Budson et al., *Why We Forget*, Chunk 7)
- **Grounded Quote**:
  > *"The basic distinction between memory and knowledge, which represents one of the diagnostic features that distinguish episodic and semantic memory... semantic memory stores generalized world knowledge, lexical representations, and categorical concepts."*
- **Why the Alternative Reading (`retrieval`) is Contradicted by the Text**:  
  `retrieval` answers *"How is relevant memory found?"* (search heuristics, spreading activation algorithms). Semantic memory constitutes the actual categorical taxonomy, definitions, and relational concepts of the world (the ontology itself). Assigning semantic memory to `retrieval` conflates the conceptual semantic store with the procedural indexer that queries it.

---

### 10. Concept: `stability`
- **Chosen Slot**: `state`
- **Canonical Slot Question**: *What is implemented/real/active right now? / How do active state variables maintain homeostatic equilibrium under perturbation?*
- **Supporting Book & Chunk**: Ashby, *Design for a Brain*, Chunk 19 (also Chunk 20, Chunk 41; *An Introduction to Cybernetics*, Chunk 11)
- **Grounded Quote**:
  > *"More important is the underlying theme that in all cases the stable system is characterised by the fact that after a displacement we can assert that the variables will return to their equilibrium values."*
- **Why the Alternative Reading (`ontology`) is Contradicted by the Text**:  
  `ontology` answers *"What does each memory type mean?"*, categorizing memory schema classes and entity types. Stability in cybernetics is not a static taxonomy or type of memory; it is a dynamical invariant property of system state trajectories under perturbation. Attributing stability to `ontology` fundamentally misinterprets a homeostatic state property as a static conceptual schema.

---

### 11. Concept: `state`
- **Chosen Slot**: `state`
- **Canonical Slot Question**: *What is implemented/real/active right now?*
- **Supporting Book & Chunk**: Laird, *The Soar Cognitive Architecture*, Chunk 4 (also Chunk 10; Ashby, *An Introduction to Cybernetics*, Chunk 2)
- **Grounded Quote**:
  > *"Soar is named for this basic cycle of state, operator, and result. The state represents the current situation in working memory... defining active attributes, relations, and objects."*
- **Why the Alternative Reading (`ontology`) is Contradicted by the Text**:  
  `ontology` answers *"What does each memory type mean?"*. A `state` is not a lexical definition of a memory category; it is the live, active operational configuration of working memory variables and sensory-motor buffers at runtime. Treating `state` as `ontology` confuses the dynamic runtime situation of the agent with static structural schemas.

---

## 4. Preservation Invariant
Per user mandate:
- **No modifications** have been applied to `01_ARCHITECTURE/ontology/slots/` at this stage.
- This document serves as the coordination decision record for subsequent mechanical merging and promotion passes.
