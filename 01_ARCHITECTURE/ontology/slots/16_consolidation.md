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
| concept | source_book | confidence | status | date_added | promoted_note_id | evidence | occurrences |
|---|---|---|---|---|---|---|---|
| reinforcement learning | 2504.05840v1; 7688_jkt_au; laird_soar_cognitive_architecture | 0.90 | promoted | 2026-09-12 | 10664ba8-39ac-4498-8823-f47014a73efc | (Eligibility traces, which can speed learning, are used in some of the demonstrations in section 7.3.) 7.1.5 Reinforcement Learning in Substates Soar supports reinforcement learning in substates. | 13 |
| forgetting | 2601.09113v1; memory_in_the_age_of_ai_agents; sarfraz22a | 0.95 | promoted | 2026-09-12 | 39a37abc-5f60-4fd0-aa2c-9338a922dc43 | plan-execute memory architectures, or incorporating human-like forgetting and reflection mechanisms (Tang et al., 2025c,d; Ouyang et al., 2025; Ye et al., 2025b; Zhao et al., 2024; Liang et al., 2025). | 24 |
| consolidation | 2601.09113v1; memory_in_the_age_of_ai_agents; sarfraz22a; squire_kandel_mind_to_molecules; why_we_forget; kandel_2001_molecular_biology_of_memory | 0.95 | promoted | 2026-09-12 | b2888875-8546-4d42-b262-2c7f779e3cc9 | exclusive online updating by incorporating offline consolidation mechanisms analogous to biological sleep. Drawing from the Complementary Learning Systems (CLS) theory (Kumaran et al., 2016; McClelland et al., 1995), future architectures / In these brain areas, the connection strength between neurons can change quickly and last for a long time, a phe- nomenon known as long-term potentiation (LTP). | 34 |
| synaptic plasticity | kandel_2001_molecular_biology_of_memory; squire_kandel_mind_to_molecules | 0.95 | proposed | 2026-09-12 | | A SIMPLE CASE OF SYNAPTIC PLASTICITY The first attempt at the neural analysis of habitu- ation was undertaken as early as 1908, in the course of studies focusing on the isolated spinal cord of the cat. | 7 |
| chunking | laird_soar_cognitive_architecture; newell_unified_theories_of_cognition | 0.90 | promoted | 2026-09-12 | a1df3d1c-201c-4217-aa62-f36292b01aa4 | 68 Chapter 3 search to knowledge search, by compiling problem solving using other types of knowledge into procedural knowledge, which is done in Soar via chunking, a learning mechanism described in chapter 6. | 13 |
| papert principle | minsky_society_of_mind | 0.95 | proposed | 2026-09-12 | | Papert�s Principle: Some of the most crucial steps in mental growth are based not i simply on acquiring new skills, but on acquiring new administrative ways to use what one already knows. | 3 |
| experience replay | sarfraz22a | 0.95 | proposed | 2026-09-12 | | , 2022 SYNERGY BETWEEN SYNAPTIC CONSOLIDATION AND EXPERIENCE REPLAY FOR GENERAL CONTINUAL LEARNING Fahad Sarfraz∗, Elahe Arani*, Bahram Zonooz Advanced Research Lab, NavInfo Europe, | 7 |
| Synaptic Consolidation | Sarfraz et al. (2022) SYNERgy - Synaptic Consolidation & Experience Replay | 0.95 | unverified_source | 2026-09-07 | | | |
| Complementary Learning Systems | Sarfraz et al. (2022) SYNERgy - Synaptic Consolidation & Experience Replay | 0.95 | unverified_source | 2026-09-07 | | | |
