---
id: slot-16-consolidation
ontology_slot: consolidation
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

# Ontology Slot: Consolidation

## Question
How does experience become knowledge?

## Theoretical sources
- McClelland, McNaughton & O'Reilly (1995), Complementary Learning Systems
- Kumaran & Hassabis (2016), Weight-based plasticity and memory consolidation in artificial agents

## Validates module
- `03_IMPLEMENTATION/packages/graph/plasticity.py` (attribution-aware plasticity engine, bounded asymptotic weight updates, and telemetry journal rollback)
- `03_IMPLEMENTATION/packages/graph/synapse_store.py` (verified location defining `decay_unused()` at line 414 and `prune()` at line 426)

## Candidate concepts
| concept | source_book | confidence | status | date_added | promoted_note_id | evidence |
|---|---|---|---|---|---|---|
| Synergy | Sarfraz et al. (2022) SYNERgy - Synaptic Consolidation & Experience Replay | 0.85 | proposed | 2026-09-07 | | |
| Synaptic Consolidation | Sarfraz et al. (2022) SYNERgy - Synaptic Consolidation & Experience Replay | 0.95 | unverified_source | 2026-09-07 | | |
| Complementary Learning Systems | Sarfraz et al. (2022) SYNERgy - Synaptic Consolidation & Experience Replay | 0.95 | unverified_source | 2026-09-07 | | |
| Stochastic Weight Consolidation | Sarfraz et al. (2022) SYNERgy - Synaptic Consolidation & Experience Replay | 0.85 | unverified_source | 2026-09-07 | | |
