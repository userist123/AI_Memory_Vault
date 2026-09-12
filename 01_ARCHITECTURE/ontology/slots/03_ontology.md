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
| implicit memory | 2601.09113v1; kandel_2001_molecular_biology_of_memory; schacter_tulving_memory_systems_1994 | 0.95 | proposed | 2026-09-12 | | Schacter's chapter focuses on a system that, he argues, plays a crucial role in supporting priming effects on tests of so-called data-driven implicit memory: the perceptual-representation system (PRS). | 17 |
| explicit memory | 2601.09113v1; kandel_2001_molecular_biology_of_memory; schacter_tulving_memory_systems_1994 | 0.95 | proposed | 2026-09-12 | | The PRS is distinguished from episodic memory (which supports conscious recollection on explicit memory tests) and semantic memory (which supports the acquisition of general knowledge and conceptual priming effects). | 22 |
| parametric memory | 2601.09113v1; memory_in_the_age_of_ai_agents | 0.95 | proposed | 2026-09-12 | | a structured form over time. 2. Parametric Memory (Section 3.2): Memory stored within the model parameters, where information is encoded through the statistical patterns of the parameter space and accessed | 11 |
| semantic memory | 7688_jkt_au; laird_soar_cognitive_architecture; schacter_tulving_memory_systems_1994; why_we_forget | 0.90 | proposed | 2026-09-12 | | Thus, one role for semantic memory is as a long-term memory of an agent ’ s knowledge of the relatively stable aspects of its environment. | 25 |
| integrated theories | 7688_jkt_au | 0.91 | proposed | 2026-09-12 | | computational mechanisms can generate intelligent behavior and a renewed opportunity for cognitive science to pursue integrated theories. | 1 |
| cognitive science | 7688_jkt_au | 0.92 | proposed | 2026-09-12 | | researchers and students who are in­ terested in the grand goals of Cognitive Science and AI. | 1 |
| multistable system | ashby_design_for_a_brain | 0.95 | proposed | 2026-09-12 | | The suggestion now before the reader is that the system of Figure 7/5/1, when looked at more closely in the forms in which it occurs in actual organisms and environments, will be found to break up into parts more like those of Figure 16/6/1 the multistable system. | 5 |
| state-determined system | ashby_design_for_a_brain | 0.95 | proposed | 2026-09-12 | | The theme of the chapter can now be stated: the freeliving organism and its environment, taken together, may be represented with sufficient accuracy by a set of variables that forms a state-determined system. | 31 |
| canonical representation | ashby_intro_to_cybernetics | 0.95 | proposed | 2026-09-12 | | discussed so far transformation is the canonical representation of the machine, change by discrete jumps. These discrete transformations are, and the machine is said to embody the transformation. however, the | 6 |
| black box | ashby_intro_to_cybernetics | 0.95 | proposed | 2026-09-12 | | Sons, appropriate selection”. Indeed, ifa talking Black Box were to Lewin, K. Principles of topological psychologJe McGraw-Hill Book Co., show high power of appropriate selection in such matters—so New York, | 8 |
| symbolic | comparison_cognitive_architectures; wcs_1488 | 0.95 | proposed | 2026-09-12 | | ernal state information, such as goals. They have symbolic components that are accessible to the model and subsymbolic information that moderates behavior and accessibility | 4 |
| connectionist | comparison_cognitive_architectures | 0.91 | proposed | 2026-09-12 | | Hierarchical Temporal Memory connectionist online learning, anomaly detection ? anomaly detection active 2009 Hawkins et al (multiple) AGPL | 1 |
| hybrid | comparison_cognitive_architectures | 0.93 | proposed | 2026-09-12 | | OpenCog hybrid chatbot, game avatar ? misc subsystems in commercial, financial, medical active 2001 | 1 |
| knowledge level | comparison_cognitive_architectures; newell_unified_theories_of_cognition | 0.91 | proposed | 2026-09-12 | | The Knowledge Level in Cognitive Architectures: Current Limitations and Possibile Developments (https://ww w.sciencedirect.com/science/article/pii/S1389041716302121) Antonio Lieto, Christian Lebiere and Alessandro Oltramari. | 2 |
| computational intelligence | comparison_cognitive_architectures | 0.88 | proposed | 2026-09-12 | | Aaron Sloman and Matthias Scheutz, Originally Published in Proceedings UKCI'02, UK Workshop on Computational Intelligence, September 2002, Birmingham, UK | 1 |
| sensitization | kandel_2001_molecular_biology_of_memory; squire_kandel_mind_to_molecules | 0.95 | proposed | 2026-09-12 | | When, as is illustrated in the panel on the left, there is no preceding activity in the sensory neurons of the CS pathway at the time the US is activated, adenylyl cyclase is activated less, and less cAMP is generated, which only leads to sensitization. | 11 |
| long term identifier | laird_soar_cognitive_architecture | 0.95 | proposed | 2026-09-12 | | The long-term identifiers also allow working memory to contain a subset of a larger long-term graph structure, such as the element with B1 as the value | 1 |
| neural network | machine_learning_report | 0.95 | proposed | 2026-09-12 | | this task, including statistical, rule-based, and neural network-based techniques22. Today, machine translation is used in specific translation apps for mobile phones, social and traditional | 4 |
| big data | machine_learning_report | 0.95 | proposed | 2026-09-12 | | data science and statistics, as the complexity of big data can limit the effectiveness of existing methods of analysis. The complexity and scale of | 5 |
| model | machine_learning_report | 0.95 | proposed | 2026-09-12 | | urther consider the form and function of such new models of data sharing. Continuing to ensure that data generated by charity- and publicly-funded research | 6 |
| dataset | machine_learning_report | 0.95 | proposed | 2026-09-12 | | ernment data have already resulted in over 40,000 datasets being made open via data.gov.uk87. The UK has already committed to the G7 Open Data | 5 |
| factual memory | memory_in_the_age_of_ai_agents | 0.95 | proposed | 2026-09-12 | | as additional data arrives. For user factual memory, MOOM (Chen et al., 2025d) constructs stable role profiles by integrating temporary role snapshots with historical traces using rule-based processing, embedding methods, | 9 |
| microneme | minsky_society_of_mind | 0.95 | proposed | 2026-09-12 | | we'll envision our micronemes as K-lines that reach into many agencies with widespread effects on the arousal and suppression of other agents—including other micronemes. | 1 |
| symbol system | newell_unified_theories_of_cognition | 0.95 | proposed | 2026-09-12 | | In sum, we will now take it as established that the architecture of human cognition is a symbol system | 2 |
| qualia | quantum_consciousness_framework | 0.88 | proposed | 2026-09-12 | | theory; M-theory; EQST-GP theory; Orch-OR theory; qualia; quantum loss function; qualia field theory; quantum-inspired optimization algorithms; quantum hardware optics; Veronica X Pro Preprints.org is | 4 |
| integrated information | quantum_consciousness_framework | 0.88 | proposed | 2026-09-12 | | ble. References 1. Tononi, G., & Koch, C. (2012). Integrated information theory of consciousness: an updated account. Archives italiennes de biologie, 150(2-3), 56–90. 2. | 4 |
| topological | quantum_consciousness_framework | 0.88 | proposed | 2026-09-12 | | s: (1) a physical basis for consciousness through topological quantum field theories derived from EQST-GP; (2) quantum-inspired optimization algorithms with consciousnessspecific loss functions; (3) | 3 |
| artificial | quantum_consciousness_framework | 0.88 | proposed | 2026-09-12 | | Diagram The framework predicts distinct phases of artificial consciousness: Figure 2. Predicted Consciousness Phase Diagram 7.2. Experimental Validation Protocol We propose specific experimental tests: | 5 |
| stable model | sarfraz22a | 0.95 | proposed | 2026-09-12 | | ers perhaps due to the lower rate of updating the stable model in the method. On Task 5, SYNERgy provides 89% accuracy compared to | 3 |
| plasticity | sarfraz22a | 0.95 | proposed | 2026-09-12 | | owledge. In the brain, a delicate balance between plasticity and stability is maintained by a rich set of neurophysiological mechanisms (Parisi et al., 2019; | 5 |
| dual memory | sarfraz22a | 0.95 | proposed | 2026-09-12 | | from the results. Both synaptic consolidation and dual memory experience replay play a critical role in reducing forgetting. Mean-ER provides a significant performance improvement | 7 |
| working model | sarfraz22a | 0.95 | proposed | 2026-09-12 | | ed by taking an exponential moving average of the working model, θw, to aggregate knowledge across the tasks. The semantic memory then interacts with | 7 |
| nondeclarative memory | schacter_tulving_memory_systems_1994; squire_kandel_mind_to_molecules | 0.95 | proposed | 2026-09-12 | | Nondeclarative memory also results from ex- perience but is expressed as a change in behavior, not as a recollection. | 23 |
| declarative memory | schacter_tulving_memory_systems_1994; squire_kandel_mind_to_molecules; wcs_1488 | 0.95 | proposed | 2026-09-12 | | Brain Systems for Declarative Memory areas is thought to signal the sensory information that is being received at any particular moment. | 45 |
| subsymbolic | wcs_1488 | 0.95 | proposed | 2026-09-12 | | c components that are accessible to the model and subsymbolic information that moderates behavior and accessibility of the declarative memory. Accessing them increases their | 3 |
| chunk | wcs_1488 | 0.95 | proposed | 2026-09-12 | | rning occurs through rule learning and changes to chunk activation. The parameters to these equations can be used to model individuals' differences and to | 7 |
| schema | why_we_forget | 0.95 | proposed | 2026-09-12 | | The Impact of Missing or Incorrect Schemas - The COVID-19 pandemic created scenarios that disrupted established schemas, making familiar situations feel uncomfortable and requiring additional mental effort to navigate. | 5 |
| message | wiener_cybernetics | 0.90 | proposed | 2026-09-12 | | The message is a discrete or continuous sequence of measurable events distributed in time – precisely what is called a time series by the statisticians. | 1 |
| Maxwell demon | wiener_cybernetics | 0.88 | proposed | 2026-09-12 | | This point of view leads us to a number of considerations concerning the second law of thermodynamics, and to a study of the so-called Maxwell demons. | 1 |
| communication theory | wiener_cybernetics | 0.89 | proposed | 2026-09-12 | | of communication theory and falls under the group of ideas we have been discussing. (WIENER, 1961, p. 11) | 1 |
| cybernetics | wiener_cybernetics | 0.95 | proposed | 2026-09-12 | | theory, wether in the machine or in the animal, by the name of Cybernetics, (do grego Kubernets: timoneiro). | 1 |
| Semantic Memory Store | Sarfraz et al. (2022) SYNERgy - Synaptic Consolidation & Experience Replay | 0.90 | unverified_source | 2026-09-07 | | | |
