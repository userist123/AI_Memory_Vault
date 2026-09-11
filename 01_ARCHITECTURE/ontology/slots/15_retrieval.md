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
| Experience Replay | Sarfraz et al. (2022) SYNERgy - Synaptic Consolidation & Experience Replay | 0.90 | unverified_source | 2026-09-07 | | | |
