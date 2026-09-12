---
id: slot-15-retrieval
ontology_slot: retrieval
type: ontology_definition
lifecycle: ACTIVE
provenance:
  source_type: design
  source_author: ANTIGRAVITY
confidence: 1.00
status: scaffold
tags: [ontology, cognitive-architecture]
relations: []
created: 2026-09-07
---

# Ontology Slot: Retrieval

## Question
How is relevant memory found?

## Theoretical sources
- None (grounded in real code, not primarily literature)

## Validates module
- `03_IMPLEMENTATION/packages/memory/controller.py` (verified definition of `MemoryController.search()` at line 376; distinct from the `03_IMPLEMENTATION/packages/memory_controller/` compatibility shim)

## Candidate concepts
| concept | source_book | confidence | status | date_added | promoted_note_id | evidence | occurrences |
|---|---|---|---|---|---|---|---|
| contrastive learning | 2504.05840v1 | 0.90 | proposed | 2026-09-12 | | This prioritization of samples happens through a contrastive learning-related momentum loss which enables the unsupervised discovery of longtailed data from the stream of experiences | 6 |
| retrieval | 2601.09113v1; memory_in_the_age_of_ai_agents; wcs_1488 | 0.95 | proposed | 2026-09-12 | | related concepts such as LLM memory, retrieval-augmented generation (RAG), and context engineering? ❷Forms: What architectural or representational forms can agent memory take? ❸Functions: Why is agent memory needed, and what | 31 |
| long-term memory | 2601.09113v1; memory_in_the_age_of_ai_agents | 0.95 | proposed | 2026-09-12 | | ation from long contexts, serving as a measure of long-term memory capabilities in static settings. Examples include: NarrativeQA (Kočisk`y et al., 2018), QuALITY (Pang | 18 |
| external memory | 2601.09113v1 | 0.95 | proposed | 2026-09-12 | | et al., 2018) proposed attaching an LSTM with an external memory that could store and retrieve both visual and textual content. This method allowed | 9 |
| retrieval-augmented generation | 2601.09113v1 | 0.95 | proposed | 2026-09-12 | | pi, 2005). Explicit memory systems in AI, such as Retrieval-Augmented Generation (RAG), mimic this function. They serve as an “AI Hippocampus” by providing an | 4 |
| associative memory | 2601.09113v1 | 0.95 | proposed | 2026-09-12 | | encompassing self-knowledge, facts, commonsense, associative memory, and other related elements, which collectively enable the generation of contextually relevant responses across a variety of tasks. | 3 |
| cue based retrieval | laird_soar_cognitive_architecture | 0.95 | proposed | 2026-09-12 | | To determine the correct room, the agent uses a negative cue to inhibit retrieval of the room that the square object is now in, while using a positive cue for the square object | 1 |
| k-line | minsky_society_of_mind | 0.95 | proposed | 2026-09-12 | | A K-line is a wirelike structure that attaches / itself to whichever mental agents are active when you solve a problem or have a / good idea. | 7 |
| recognition memory | newell_unified_theories_of_cognition | 0.95 | proposed | 2026-09-12 | | However, a production system can also be viewed simply as a contentaddressed memory | 1 |
| priming | schacter_tulving_memory_systems_1994; squire_kandel_mind_to_molecules | 0.95 | proposed | 2026-09-12 | | This allows for elaboration and generalization of recency and expectancy memories, perhaps resulting in the priming and episodic memories reported for humans. | 28 |
| activation | wcs_1488 | 0.95 | proposed | 2026-09-12 | | ition of subsymbolic quantities, that is, numeric activation values for each production rule (sometimes, simply “rule”) and declarative memory element. These activation values enabled | 6 |
| retrieval cue | why_we_forget | 0.95 | proposed | 2026-09-12 | | The smell serves as a retrieval cue, triggering a memory that brings back not only the aroma of onions but all associated sensations and emotions you experienced during breakfast, like seeing the newspaper headlines and hearing the song 'Imagine'. | 7 |
| recollection | why_we_forget | 0.95 | proposed | 2026-09-12 | | Answer:This often happens because recognition is based on familiarity, which is an instinctive acknowledgment that doesn't necessarily include detailed recollection. | 15 |
| Experience Replay | Sarfraz et al. (2022) SYNERgy - Synaptic Consolidation & Experience Replay | 0.90 | unverified_source | 2026-09-07 | | | |
