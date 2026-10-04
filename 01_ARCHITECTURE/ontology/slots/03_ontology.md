---
id: slot-03-ontology
ontology_slot: ontology
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

# Ontology Slot: Ontology

## Question
What does each memory type mean?

## Theoretical sources
- Tulving & Schacter, Memory Systems 1994

## Validates module
none (maps conceptually to note types in `03_IMPLEMENTATION/packages/lifecycle/validation/schema.py` where canonical type enum defines: knowledge, project, procedure, decision, experience, error, lesson, preference, resource, hypothesis, system, core, index)

## Candidate concepts
| concept | source_book | confidence | status | date_added | promoted_note_id | evidence | occurrences |
|---|---|---|---|---|---|---|---|
| transience | openstax_psychology_2e_ch08 | 0.95 | promoted | 2026-10-04 | 7f381c04-caa8-49aa-9904-2c88ba54506b | Let’s look at the first sin of the forgetting errors: transience, which means that memories can fade over time. | 1 | 
| anterograde amnesia | openstax_psychology_2e_ch08 | 0.95 | promoted | 2026-10-04 | d6f5ed57-4f9c-42bd-8868-e7771774291b | With anterograde amnesia, you cannot remember new information, although you can remember information and events that happened prior to your injury. | 1 | 
| retrograde amnesia | openstax_psychology_2e_ch08 | 0.95 | promoted | 2026-10-04 | d2219383-13a8-4fd8-a409-2da2c4ee1a7a | Retrograde amnesia is loss of memory for events that occurred prior to the trauma. | 1 | 
| engram | openstax_psychology_2e_ch08 | 0.95 | promoted | 2026-10-04 | caad870f-00e9-4786-821a-5511ac7ec8e4 | He was searching for evidence of the engram: the group of neurons that serve as the “physical representation of memory” (Josselyn, 2010). | 1 | 
| implicit memory | 2601.09113v1; kandel_2001_molecular_biology_of_memory; schacter_tulving_memory_systems_1994 | 0.95 | promoted | 2026-09-12 | ac5e8530-c152-4b81-97ee-549caca0fa75 | Schacter's chapter focuses on a system that, he argues, plays a crucial role in supporting priming effects on tests of so-called data-driven implicit memory: the perceptual-representation system (PRS). | 17 |
| explicit memory | 2601.09113v1; kandel_2001_molecular_biology_of_memory; schacter_tulving_memory_systems_1994 | 0.95 | promoted | 2026-09-12 | 93678180-ff9e-45f5-927e-01320aff8eb9 | The PRS is distinguished from episodic memory (which supports conscious recollection on explicit memory tests) and semantic memory (which supports the acquisition of general knowledge and conceptual priming effects). | 22 |
| parametric memory | 2601.09113v1; memory_in_the_age_of_ai_agents; sarfraz22a | 0.95 | promoted | 2026-09-12 | 519ef025-222f-485e-b7ee-1684078fe6e7 | a structured form over time. 2. Parametric Memory (Section 3.2): Memory stored within the model parameters, where information is encoded through the statistical patterns of the parameter space and accessed / ers perhaps due to the lower rate of updating the stable model in the method. On Task 5, SYNERgy provides 89% accuracy compared to | 14 |
| semantic memory | 7688_jkt_au; laird_soar_cognitive_architecture; schacter_tulving_memory_systems_1994; why_we_forget; memory_in_the_age_of_ai_agents | 0.90 | promoted | 2026-09-12 | 84ef8b0f-d457-4741-be32-582ba76e3ab3 | Thus, one role for semantic memory is as a long-term memory of an agent ’ s knowledge of the relatively stable aspects of its environment. / as additional data arrives. For user factual memory, MOOM (Chen et al., 2025d) constructs stable role profiles by integrating temporary role snapshots with historical traces using rule-based processing, embedding methods, | 34 |
| multistable system | ashby_design_for_a_brain | 0.95 | promoted | 2026-09-12 | 6dd28a01-e257-4065-a481-f2c3cb5d1659 | The suggestion now before the reader is that the system of Figure 7/5/1, when looked at more closely in the forms in which it occurs in actual organisms and environments, will be found to break up into parts more like those of Figure 16/6/1 the multistable system. | 5 | 
| state-determined system | ashby_design_for_a_brain | 0.95 | promoted | 2026-09-12 | 0d68bba6-662b-4633-b065-bf1666889af9 | The theme of the chapter can now be stated: the freeliving organism and its environment, taken together, may be represented with sufficient accuracy by a set of variables that forms a state-determined system. | 31 |
| canonical representation | ashby_intro_to_cybernetics | 0.95 | promoted | 2026-09-12 | 72cf123b-b8b3-4d22-94ba-388fd72e20f1 | discussed so far transformation is the canonical representation of the machine, change by discrete jumps. These discrete transformations are, and the machine is said to embody the transformation. however, the | 6 | 
| black box | ashby_intro_to_cybernetics | 0.95 | promoted | 2026-09-12 | cf5c873d-4569-4ed8-a3a8-bda10fc1680d | Sons, appropriate selection”. Indeed, ifa talking Black Box were to Lewin, K. Principles of topological psychologJe McGraw-Hill Book Co., show high power of appropriate selection in such matters—so New York, | 8 | 
| symbolic | comparison_cognitive_architectures; wcs_1488 | 0.95 | proposed | 2026-09-12 | | ernal state information, such as goals. They have symbolic components that are accessible to the model and subsymbolic information that moderates behavior and accessibility | 4 |
| sensitization | kandel_2001_molecular_biology_of_memory; squire_kandel_mind_to_molecules | 0.95 | promoted | 2026-09-12 | cce3323b-2701-4907-ba4c-e5854fcd2bf8 | When, as is illustrated in the panel on the left, there is no preceding activity in the sensory neurons of the CS pathway at the time the US is activated, adenylyl cyclase is activated less, and less cAMP is generated, which only leads to sensitization. | 11 |
| integrated information | quantum_consciousness_framework | 0.88 | proposed | 2026-09-12 | | ble. References 1. Tononi, G., & Koch, C. (2012). Integrated information theory of consciousness: an updated account. Archives italiennes de biologie, 150(2-3), 56–90. 2. | 4 |
| dual memory | sarfraz22a | 0.95 | proposed | 2026-09-12 | | from the results. Both synaptic consolidation and dual memory experience replay play a critical role in reducing forgetting. Mean-ER provides a significant performance improvement | 7 |
| nondeclarative memory | schacter_tulving_memory_systems_1994; squire_kandel_mind_to_molecules | 0.95 | promoted | 2026-09-12 | 5a3df1ef-20e9-4f8c-bc70-91d6d4fdb5ac | Nondeclarative memory also results from ex- perience but is expressed as a change in behavior, not as a recollection. | 23 |
| declarative memory | schacter_tulving_memory_systems_1994; squire_kandel_mind_to_molecules; wcs_1488 | 0.95 | promoted | 2026-09-12 | e045ded5-01fd-4a28-8f85-203ebae670de | Brain Systems for Declarative Memory areas is thought to signal the sensory information that is being received at any particular moment. | 45 |
| subsymbolic | wcs_1488 | 0.95 | promoted | 2026-09-12 | 07d27579-d273-4b81-b2b7-25ad820f2535 | c components that are accessible to the model and subsymbolic information that moderates behavior and accessibility of the declarative memory. Accessing them increases their | 3 | 
| chunk | wcs_1488 | 0.95 | promoted | 2026-09-12 | f3977411-5918-430f-8099-c4eb254b20db | rning occurs through rule learning and changes to chunk activation. The parameters to these equations can be used to model individuals' differences and to | 7 | 
| schema | why_we_forget | 0.95 | promoted | 2026-09-12 | 2543b0b9-8580-423f-9feb-0f38ccb87fa4 | The Impact of Missing or Incorrect Schemas - The COVID-19 pandemic created scenarios that disrupted established schemas, making familiar situations feel uncomfortable and requiring additional mental effort to navigate. | 5 | 
