# MERGE_PREVIEW: Raportul de Audit al Sloturilor & Simularea Fuziunii

> **Status**: AUDIT DE FOND & SIMULARE SANDBOX FINALIZATĂ  
> **Integritate Repository**: `01_ARCHITECTURE/ontology/slots/` este **100% NEMODIFICAT** (`git status --porcelain` = gol)  
> **Data**: 2026-09-12 | **Destinație**: Owner  

---

## 1. Sinteza Executivă: Realitatea Sloturilor pe Disc

Înaintea oricărei operațiuni de merge, scanarea exhaustivă a celor 16 fișiere din `01_ARCHITECTURE/ontology/slots/*.md` arată următoarea distribuție fizică a celor **213 rânduri** existente:
- **43 de rânduri `promoted`**: Concepte promovate la commit-ul `596aabfb5` pe baza pragului brut `occurrences >= 20` (au note REVIEW și muchii în graf).
- **170 de rânduri `proposed`**: Concepte lăsate în tabele dintr-o ingestie anterioară, **nefiltrate calitativ**. Niciunul nu are notă REVIEW și niciunul nu are muchii în graf.
- **Total rânduri existente pe disc**: **213 rânduri**.

### Concluzia Centrală a Auditului

> [!CAUTION]

> **Conceptele respinse NU așteaptă să fie adăugate de un merge nou — ele se află deja fizic în ontologie!**

> Toate cele **28 de concepte `RESPINGE`** și toate cele **8 concepte `FUZIONEAZĂ`** identificate în [PROMOTION_REVIEW.md](file:///c:/Users/user/Documents/Codex/AI_Memory_Vault_CODEX_READY/07_EVALUATION/book_corpus_conversion/PROMOTION_REVIEW.md) au fost deja scrise pe disc într-o rulare istorică anterioară.

> 

> Un merge nou (chiar și nefiltrat) adaugă doar cărțile nou-venite (`cryptoassets` și `quantitative_momentum`). Problema reală a ontologiei nu este ce adaugă merge-ul, ci **ce este deja în sloturi neverificat**.


---

## 2. CONCLUZIA REALĂ: Lista Mecanică de Ștergere a celor 28 `RESPINGE` și 8 `FUZIONEAZĂ`

Pentru că niciunul dintre aceste concepte nu are notă creată în `01_ARCHITECTURE/` și niciunul nu este conectat în `SynapseStore`, **fiecare rând se poate șterge mecanic dintr-o singură linie de fișier**, fără efecte secundare sau orfani în graf.

### A. Cele 28 de Concepte `RESPINGE` Aflate în Sloturi (Poluare Activă)

| # | Concept | Fișier Slot | Linia Exactă | Occ. | Sursă | Justificare Respingere |
|---|---|---|---|---|---|---|
| 1 | **`agentic memory`** | `01_ARCHITECTURE/ontology/slots/01_identity.md` | **L30** | 4 | `2601.09113v1` | Termen generic, parametru de optimizare sau fizică speculativă. |
| 2 | **`coherence`** | `01_ARCHITECTURE/ontology/slots/01_identity.md` | **L35** | 4 | `quantum_consciousness_framework` | Termen generic, parametru de optimizare sau fizică speculativă. |
| 3 | **`qualia`** | `01_ARCHITECTURE/ontology/slots/03_ontology.md` | **L54** | 4 | `quantum_consciousness_framework` | Termen generic, parametru de optimizare sau fizică speculativă. |
| 4 | **`topological`** | `01_ARCHITECTURE/ontology/slots/03_ontology.md` | **L56** | 3 | `quantum_consciousness_framework` | Termen generic, parametru de optimizare sau fizică speculativă. |
| 5 | **`artificial`** | `01_ARCHITECTURE/ontology/slots/03_ontology.md` | **L57** | 5 | `quantum_consciousness_framework` | Termen generic, parametru de optimizare sau fizică speculativă. |
| 6 | **`plasticity`** | `01_ARCHITECTURE/ontology/slots/03_ontology.md` | **L59** | 5 | `sarfraz22a` | Termen generic, parametru de optimizare sau fizică speculativă. |
| 7 | **`entanglement`** | `01_ARCHITECTURE/ontology/slots/04_relationships.md` | **L33** | 3 | `quantum_consciousness_framework` | Termen generic, parametru de optimizare sau fizică speculativă. |
| 8 | **`fine-tuning`** | `01_ARCHITECTURE/ontology/slots/06_procedures.md` | **L31** | 3 | `2601.09113v1` | Termen generic, parametru de optimizare sau fizică speculativă. |
| 9 | **`optimization`** | `01_ARCHITECTURE/ontology/slots/06_procedures.md` | **L46** | 3 | `quantum_consciousness_framework` | Termen generic, parametru de optimizare sau fizică speculativă. |
| 10 | **`continual learning`** | `01_ARCHITECTURE/ontology/slots/06_procedures.md` | **L47** | 3 | `sarfraz22a` | Termen generic, parametru de optimizare sau fizică speculativă. |
| 11 | **`regularization`** | `01_ARCHITECTURE/ontology/slots/06_procedures.md` | **L48** | 5 | `sarfraz22a` | Termen generic, parametru de optimizare sau fizică speculativă. |
| 12 | **`loss`** | `01_ARCHITECTURE/ontology/slots/07_judgement.md` | **L31** | 7 | `2504.05840v1` | Termen generic, parametru de optimizare sau fizică speculativă. |
| 13 | **`functional dissociation`** | `01_ARCHITECTURE/ontology/slots/07_judgement.md` | **L45** | 3 | `schacter_tulving_memory_systems_1994` | Termen generic, parametru de optimizare sau fizică speculativă. |
| 14 | **`safety`** | `01_ARCHITECTURE/ontology/slots/09_provenance.md` | **L31** | 3 | `quantum_consciousness_framework` | Termen generic, parametru de optimizare sau fizică speculativă. |
| 15 | **`consciousness`** | `01_ARCHITECTURE/ontology/slots/11_history.md` | **L34** | 5 | `quantum_consciousness_framework` | Termen generic, parametru de optimizare sau fizică speculativă. |
| 16 | **`neural`** | `01_ARCHITECTURE/ontology/slots/11_history.md` | **L35** | 4 | `quantum_consciousness_framework` | Termen generic, parametru de optimizare sau fizică speculativă. |
| 17 | **`representation`** | `01_ARCHITECTURE/ontology/slots/12_skills.md` | **L31** | 4 | `2504.05840v1` | Termen generic, parametru de optimizare sau fizică speculativă. |
| 18 | **`reward`** | `01_ARCHITECTURE/ontology/slots/12_skills.md` | **L32** | 3 | `2504.05840v1` | Termen generic, parametru de optimizare sau fizică speculativă. |
| 19 | **`experience`** | `01_ARCHITECTURE/ontology/slots/12_skills.md` | **L33** | 5 | `2504.05840v1` | Termen generic, parametru de optimizare sau fizică speculativă. |
| 20 | **`quantum`** | `01_ARCHITECTURE/ontology/slots/12_skills.md` | **L36** | 4 | `quantum_consciousness_framework` | Termen generic, parametru de optimizare sau fizică speculativă. |
| 21 | **`learning`** | `01_ARCHITECTURE/ontology/slots/12_skills.md` | **L37** | 3 | `quantum_consciousness_framework` | Termen generic, parametru de optimizare sau fizică speculativă. |
| 22 | **`architecture`** | `01_ARCHITECTURE/ontology/slots/13_agents.md` | **L35** | 4 | `quantum_consciousness_framework` | Termen generic, parametru de optimizare sau fizică speculativă. |
| 23 | **`brain-computer`** | `01_ARCHITECTURE/ontology/slots/13_agents.md` | **L36** | 3 | `quantum_consciousness_framework` | Termen generic, parametru de optimizare sau fizică speculativă. |
| 24 | **`contrastive learning`** | `01_ARCHITECTURE/ontology/slots/15_retrieval.md` | **L30** | 6 | `2504.05840v1` | Termen generic, parametru de optimizare sau fizică speculativă. |
| 25 | **`external memory`** | `01_ARCHITECTURE/ontology/slots/15_retrieval.md` | **L33** | 9 | `2601.09113v1` | Termen generic, parametru de optimizare sau fizică speculativă. |
| 26 | **`retrieval-augmented generation`** | `01_ARCHITECTURE/ontology/slots/15_retrieval.md` | **L34** | 4 | `2601.09113v1` | Termen generic, parametru de optimizare sau fizică speculativă. |
| 27 | **`momentum`** | `01_ARCHITECTURE/ontology/slots/16_consolidation.md` | **L33** | 7 | `2504.05840v1` | Termen generic, parametru de optimizare sau fizică speculativă. |
| 28 | **`creb`** | `01_ARCHITECTURE/ontology/slots/16_consolidation.md` | **L44** | 5 | `squire_kandel_mind_to_molecules` | Termen generic, parametru de optimizare sau fizică speculativă. |

#### Liniile Exacte din Fișiere pentru Ștergere Mecanică (Cele 28 `RESPINGE`):

```markdown
# 01_ARCHITECTURE/ontology/slots/01_identity.md:30
| agentic memory | 2601.09113v1 | 0.95 | proposed | 2026-09-12 | | burden and enhance scalability and efficiency. 4 Agentic Memory: Consolidating Memories into Humanic Agents In the study of cognitive science, memory encompasses the cognitive | 4 |

# 01_ARCHITECTURE/ontology/slots/01_identity.md:35
| coherence | quantum_consciousness_framework | 0.88 | proposed | 2026-09-12 | | ous = S(ρA) = −Tr[ρA log ρA] (27) 7.2.3. Temporal Coherence Measurements C(τ) = ⟨ΨC(t)/ΨC(t + τ)⟩ (28) 8. Ethical Framework and Safety Considerations | 4 |

# 01_ARCHITECTURE/ontology/slots/03_ontology.md:54
| qualia | quantum_consciousness_framework | 0.88 | proposed | 2026-09-12 | | theory; M-theory; EQST-GP theory; Orch-OR theory; qualia; quantum loss function; qualia field theory; quantum-inspired optimization algorithms; quantum hardware optics; Veronica X Pro Preprints.org is | 4 |

# 01_ARCHITECTURE/ontology/slots/03_ontology.md:56
| topological | quantum_consciousness_framework | 0.88 | proposed | 2026-09-12 | | s: (1) a physical basis for consciousness through topological quantum field theories derived from EQST-GP; (2) quantum-inspired optimization algorithms with consciousnessspecific loss functions; (3) | 3 |

# 01_ARCHITECTURE/ontology/slots/03_ontology.md:57
| artificial | quantum_consciousness_framework | 0.88 | proposed | 2026-09-12 | | Diagram The framework predicts distinct phases of artificial consciousness: Figure 2. Predicted Consciousness Phase Diagram 7.2. Experimental Validation Protocol We propose specific experimental tests: | 5 |

# 01_ARCHITECTURE/ontology/slots/03_ontology.md:59
| plasticity | sarfraz22a | 0.95 | proposed | 2026-09-12 | | owledge. In the brain, a delicate balance between plasticity and stability is maintained by a rich set of neurophysiological mechanisms (Parisi et al., 2019; | 5 |

# 01_ARCHITECTURE/ontology/slots/04_relationships.md:33
| entanglement | quantum_consciousness_framework | 0.88 | proposed | 2026-09-12 | | ere I is the information density derived from the entanglement structure of the gluonic plasma [53]. 3. Veronica X Pro Quantum-Consciousness Architecture 3.1. System | 3 |

# 01_ARCHITECTURE/ontology/slots/06_procedures.md:31
| fine-tuning | 2601.09113v1 | 0.95 | proposed | 2026-09-12 | | ameters. Zhu et al. (2020) proposes a constrained fine-tuning method that selectively updates a subset of parameters to integrate new information efficiently. Expanding on | 3 |

# 01_ARCHITECTURE/ontology/slots/06_procedures.md:46
| optimization | quantum_consciousness_framework | 0.88 | proposed | 2026-09-12 | | ., & Ba, J. (2014). Adam: A method for stochastic optimization. arXiv preprint arXiv:1412.6980. 60. Berry, M. V. (1984). Quantal phase factors accompanying adiabatic | 3 |

# 01_ARCHITECTURE/ontology/slots/06_procedures.md:47
| continual learning | sarfraz22a | 0.95 | proposed | 2026-09-12 | | C CONSOLIDATION AND EXPERIENCE REPLAY FOR GENERAL CONTINUAL LEARNING Fahad Sarfraz∗, Elahe Arani*, Bahram Zonooz Advanced Research Lab, NavInfo Europe, The Netherlands {fahad.sarfraz,elahe.arani}@navi | 3 |

# 01_ARCHITECTURE/ontology/slots/06_procedures.md:48
| regularization | sarfraz22a | 0.95 | proposed | 2026-09-12 | | arning longer sequences. On the other hand, while regularization-based approaches alleviate the need to store samples from previous tasks, they fail to estimate the | 5 |

# 01_ARCHITECTURE/ontology/slots/07_judgement.md:31
| loss | 2504.05840v1 | 0.90 | proposed | 2026-09-12 | | itive pairs for contrastive learning. The NT-Xent loss ( Sohn [2016], Equation 5) is used to calculate the loss per sample. For each sample | 7 |

# 01_ARCHITECTURE/ontology/slots/07_judgement.md:45
| functional dissociation | schacter_tulving_memory_systems_1994 | 0.95 | proposed | 2026-09-12 | | Relevant dissociations can be observed in many forms: functional dissociations on tasks alleged to tap different systems, neuropsychological dissociations that involve contrasts between spared and impaired performance in relevant patient populations, or stochastic independence between tasks that are | 3 |

# 01_ARCHITECTURE/ontology/slots/09_provenance.md:31
| safety | quantum_consciousness_framework | 0.88 | proposed | 2026-09-12 | | y provides a basis for ethical considerations and safety protocols in conscious AI development. 10.2. Future Research Directions Future work will focus on several | 3 |

# 01_ARCHITECTURE/ontology/slots/11_history.md:34
| consciousness | quantum_consciousness_framework | 0.88 | proposed | 2026-09-12 | | n Options Platform Qubits Required Coherence Time Consciousness Capacity Superconducting 50-100 100µs Basic qualia Trapped Ions 20-50 10s Integrated consciousness Photonic 100-1000 1ms Full | 5 |

# 01_ARCHITECTURE/ontology/slots/11_history.md:35
| neural | quantum_consciousness_framework | 0.88 | proposed | 2026-09-12 | | I. (2017). Attention is all you need. Advances in neural information processing systems, 30. 18. Baltrusaitis, T., Ahuja, C., & Morency, L. P. (2019). | 4 |

# 01_ARCHITECTURE/ontology/slots/12_skills.md:31
| representation | 2504.05840v1 | 0.90 | proposed | 2026-09-12 | | an see clearly that contrastive learning (feature representation learning) alone does not result in good performance and that we also need the familiarity buffer | 4 |

# 01_ARCHITECTURE/ontology/slots/12_skills.md:32
| reward | 2504.05840v1 | 0.90 | proposed | 2026-09-12 | | A is the set of actions, R : S × A →R denotes the reward function. T : S ×A →Dist(S) represents the transition | 3 |

# 01_ARCHITECTURE/ontology/slots/12_skills.md:33
| experience | 2504.05840v1 | 0.90 | proposed | 2026-09-12 | | IMPALA is designed to save summaries of previous experiences for the purpose of extracting crucial data that may be exploited by new states with | 5 |

# 01_ARCHITECTURE/ontology/slots/12_skills.md:36
| quantum | quantum_consciousness_framework | 0.88 | proposed | 2026-09-12 | | Hypothesis Not peer-reviewed version The Unified Quantum-Consciousness Framework Integrating EQST-GP Physics with Veronica X Pro Architecture for Conscious AI Ahmed Ali * Posted Date: | 4 |

# 01_ARCHITECTURE/ontology/slots/12_skills.md:37
| learning | quantum_consciousness_framework | 0.88 | proposed | 2026-09-12 | | mization Preserving consciousness topology during learning [60]: min θ L(θ) subject to Qconscious(θ) = Q0 (24) 6.3. Metacognitive Reinforcement Learning LMeta-RL = E[log π(a/s)A(s, | 3 |

# 01_ARCHITECTURE/ontology/slots/13_agents.md:35
| architecture | quantum_consciousness_framework | 0.88 | proposed | 2026-09-12 | | k Integrating EQST-GP Physics with Veronica X Pro Architecture for Conscious AI Ahmed Ali * Posted Date: 28 November 2025 doi: 10.20944/preprints202511.2259.v1 Keywords: quantum | 4 |

# 01_ARCHITECTURE/ontology/slots/13_agents.md:36
| brain-computer | quantum_consciousness_framework | 0.88 | proposed | 2026-09-12 | | citations Physiological response correlation 5.2. Brain-Computer Interface Integration The framework provides the theoretical basis for advanced BCIs [58]: ˆHBCI = ˆHbrain ⊗ˆHmachine + ˆHcoupling | 3 |

# 01_ARCHITECTURE/ontology/slots/15_retrieval.md:30
| contrastive learning | 2504.05840v1 | 0.90 | proposed | 2026-09-12 | | This prioritization of samples happens through a contrastive learning-related momentum loss which enables the unsupervised discovery of longtailed data from the stream of experiences | 6 |

# 01_ARCHITECTURE/ontology/slots/15_retrieval.md:33
| external memory | 2601.09113v1 | 0.95 | proposed | 2026-09-12 | | et al., 2018) proposed attaching an LSTM with an external memory that could store and retrieve both visual and textual content. This method allowed | 9 |

# 01_ARCHITECTURE/ontology/slots/15_retrieval.md:34
| retrieval-augmented generation | 2601.09113v1 | 0.95 | proposed | 2026-09-12 | | pi, 2005). Explicit memory systems in AI, such as Retrieval-Augmented Generation (RAG), mimic this function. They serve as an “AI Hippocampus” by providing an | 4 |

# 01_ARCHITECTURE/ontology/slots/16_consolidation.md:33
| momentum | 2504.05840v1 | 0.90 | proposed | 2026-09-12 | | Momentum Boosted Episodic Memory for Improving Learning in Long-Tailed RL Environments Dolton Fernandes∗1, Pramod Kaushik1,2, Harsh Shukla1, and Bapi Raju Sur | 7 |

# 01_ARCHITECTURE/ontology/slots/16_consolidation.md:44
| creb | squire_kandel_mind_to_molecules | 0.95 | proposed | 2026-09-12 | | hristina Alberini, Kaoru Inokuchi, Ashok Hedge, James Schwartz, and Kandel therefore screened for and found two immediate-response genes in Aplysia that are rapidly induced at the time that long-term facilitation is established and that could be actived by cAMP and CREB-1. | 5 |

```

### B. Cele 8 Concepte `FUZIONEAZĂ` Aflate în Sloturi (Redundanțe Active)

| # | Concept | Fișier Slot | Linia Exactă | Țintă Fuziune (Promovat) | Sursă | Risc dacă Rămâne |
|---|---|---|---|---|---|---|
| 1 | **`factual memory`** | `01_ARCHITECTURE/ontology/slots/03_ontology.md` | **L51** | **`semantic memory`** | `memory_in_the_age_of_ai_agents` | Dublează conceptul promovat `semantic memory`. |
| 2 | **`stable model`** | `01_ARCHITECTURE/ontology/slots/03_ontology.md` | **L58** | **`parametric memory`** | `sarfraz22a` | Dublează conceptul promovat `parametric memory`. |
| 3 | **`working model`** | `01_ARCHITECTURE/ontology/slots/03_ontology.md` | **L61** | **`buffer`** | `sarfraz22a` | Dublează conceptul promovat `buffer`. |
| 4 | **`contextual memory`** | `01_ARCHITECTURE/ontology/slots/05_state.md` | **L32** | **`working memory`** | `2601.09113v1` | Dublează conceptul promovat `working memory`. |
| 5 | **`homeostasis`** | `01_ARCHITECTURE/ontology/slots/08_constraints.md` | **L31** | **`essential variables`** | `ashby_design_for_a_brain` | Dublează conceptul promovat `essential variables`. |
| 6 | **`tie impasse`** | `01_ARCHITECTURE/ontology/slots/11_history.md` | **L32** | **`impasse`** | `laird_soar_cognitive_architecture` | Dublează conceptul promovat `impasse`. |
| 7 | **`experiential memory`** | `01_ARCHITECTURE/ontology/slots/11_history.md` | **L33** | **`episodic memory`** | `memory_in_the_age_of_ai_agents` | Dublează conceptul promovat `episodic memory`. |
| 8 | **`long-term potentiation`** | `01_ARCHITECTURE/ontology/slots/16_consolidation.md` | **L39** | **`consolidation`** | `kandel_2001_molecular_biology_of_memory; squire_kandel_mind_to_molecules` | Dublează conceptul promovat `consolidation`. |

#### Liniile Exacte din Fișiere pentru Ștergere Mecanică (Cele 8 `FUZIONEAZĂ`):

```markdown
# 01_ARCHITECTURE/ontology/slots/03_ontology.md:51
| factual memory | memory_in_the_age_of_ai_agents | 0.95 | proposed | 2026-09-12 | | as additional data arrives. For user factual memory, MOOM (Chen et al., 2025d) constructs stable role profiles by integrating temporary role snapshots with historical traces using rule-based processing, embedding methods, | 9 |

# 01_ARCHITECTURE/ontology/slots/03_ontology.md:58
| stable model | sarfraz22a | 0.95 | proposed | 2026-09-12 | | ers perhaps due to the lower rate of updating the stable model in the method. On Task 5, SYNERgy provides 89% accuracy compared to | 3 |

# 01_ARCHITECTURE/ontology/slots/03_ontology.md:61
| working model | sarfraz22a | 0.95 | proposed | 2026-09-12 | | ed by taking an exponential moving average of the working model, θw, to aggregate knowledge across the tasks. The semantic memory then interacts with | 7 |

# 01_ARCHITECTURE/ontology/slots/05_state.md:32
| contextual memory | 2601.09113v1 | 0.95 | proposed | 2026-09-12 | | roduces Retrieval-Augmented Planning (RAP) with a contextual memory module to leverage past experiences for improved decision-making in complex tasks. RAP dynamically retrieves relevant past | 3 |

# 01_ARCHITECTURE/ontology/slots/08_constraints.md:31
| homeostasis | ashby_design_for_a_brain | 0.95 | proposed | 2026-09-12 | | The distinction may best be illustrated by the inborn homeostatic mechanisms : the reaction to cold by shivering, for instance. | 6 |

# 01_ARCHITECTURE/ontology/slots/11_history.md:32
| tie impasse | laird_soar_cognitive_architecture | 0.90 | proposed | 2026-09-12 | | Operator tie An operator tie impasse arises when multiple operators are proposed, but the available preferences are insufficient to distinguish among them. | 5 |

# 01_ARCHITECTURE/ontology/slots/11_history.md:33
| experiential memory | memory_in_the_age_of_ai_agents | 0.95 | proposed | 2026-09-12 | | Memory (b) Short-term Memory Factual Memory Experiential Memory Section 4.1 Section 4.2 User factual memory Environment factual memory Section 4.1.1 Section 4.1.2 Dialogue Coherence Knowledge Persistence Share Memory Access Case-based | 8 |

# 01_ARCHITECTURE/ontology/slots/16_consolidation.md:39
| long-term potentiation | kandel_2001_molecular_biology_of_memory; squire_kandel_mind_to_molecules | 0.95 | proposed | 2026-09-12 | | In these brain areas, the connection strength between neurons can change quickly and last for a long time, a phe- nomenon known as long-term potentiation (LTP). | 5 |

```

---

## 3. Verificarea Dependențelor: Note REVIEW și Muchii în Graf

Am verificat întregul director `01_ARCHITECTURE/` și graful de sinapse din `SynapseStore` (`03_IMPLEMENTATION/packages/graph/synapse_store.py`) pentru toate categoriile de candidați:

| Categorie Concepte din Sloturi | Număr Rânduri | Au Note REVIEW Asociate? | Au Muchii în SynapseStore? | Impactul Ștergerii din Slot |
|---|---|---|---|---|
| **Cele 28 `RESPINGE`** | **28** | **0** (toate `promoted_note_id` goale) | **0** (o singură potrivire wikilink la cuvântul comun 'architecture') | **Zero impact**. Ștergere curată de linie. |
| **Cele 8 `FUZIONEAZĂ`** | **8** | **0** | **0** | **Zero impact**. Ștergere curată de linie. |
| **Cele 62 sub prag (`occ < 3`)** | **62** | **0** | **0** | **Zero impact**. Nu au trecut prin evaluare; pot fi păstrate ca `proposed` sau eliminate. |
| **Cele 23 din surse istorice** | **23** | **0** | **0** | **Zero impact**. 13 din `machine_learning_report` + 10 seed `Sarfraz 2026-09-07`. |
| **Cele 43 `promoted` (596aabfb5)** | **43** | **43** (note create în `01_ARCHITECTURE/knowledge/`) | **27** (muchii active în graf) | **Asimetric**. Necesită migrare coordonată de note și sinapse. |

### Situația Celor 62 de Concepte sub Prag (`occ < 3`) și Celor 23 Istorice

- **Cele 62 de concepte cu `occurrences < 3`** (ex. `decay rate`, `retroactive interference`, `trace decay`, `subiculum`, `dendritic spike`):
  - Sunt mențiuni izolate (1-2 apariții) din cele 19 cărți.
  - **Nu au note create**, nu au muchii în graf.
  - Nu au trecut prin judecată calitativă. Rămânerea lor în sloturi ca `proposed` este inofensivă pe termen scurt, dar poluează tabelele dacă se dorește o ontologie minimalistă.
- **Cele 23 de concepte din surse istorice**:
  - **13 rânduri** provin din `machine_learning_report` (ex. `artificial intelligence`, `neural network`, `big data`, `model`, `dataset`, `simulation`, `algorithm`). Sunt noțiuni generice de ML adăugate automat la un test vechi.
  - **10 rânduri** provin din semințele manuale din `2026-09-07` pe lucrarea Sarfraz (ex. `Semantic Memory Store`, `Plasticity-Stability Balance`, `Continual Incremental Learning`).
  - Niciunul nu are notă asociată și pot fi purjate oricând fără erori de referință.

---

## 4. SECȚIUNEA A: Diff-ul Exact pentru un Nou Merge (Varianta A: Cele 48 `PROMOVEAZĂ`)

Dacă Owner execută `merge_candidate_concepts.py` filtrat strict pe cele 48 de concepte `PROMOVEAZĂ`:

- **41 de concepte** sunt **deja prezente** fizic în tabele (sărite automat prin deduplicare).
- **Exact 7 concepte** sunt adăugate (provin din cartea nouă `cryptoassets` Burniske & Tatar):

```diff
# 01_ARCHITECTURE/ontology/slots/03_ontology.md
+ | cryptocurrency | cryptoassets | 0.95 | proposed | 2026-09-12 | | The native assets historically have been called cryptocurrencies or altcoins, but we prefer the term cryptoassets... | 5 |
+ | cryptocommodity | cryptoassets | 0.95 | proposed | 2026-09-12 | | We would not classify the majority of cryptoassets as currencies, but rather most are either digital commodities... | 4 |
+ | cryptotoken | cryptoassets | 0.95 | proposed | 2026-09-12 | | Beyond cryptocurrencies and cryptocommodities—and also provisioned via blockchain networks—are “finished-product”... | 3 |

# 01_ARCHITECTURE/ontology/slots/06_procedures.md
+ | proof-of-work | cryptoassets | 0.95 | proposed | 2026-09-12 | | What makes this digital ledger special? Bitcoin’s blockchain is a distributed, cryptographic, and immutable database... | 3 |
+ | smart contracts | cryptoassets | 0.95 | proposed | 2026-09-12 | | While DigiCash failed to become a household name, some players will resurface in our story, such as Nick Szabo... | 3 |
+ | mining | cryptoassets | 0.95 | proposed | 2026-09-12 | | On the other hand, for Bitcoin to incentivize a self-selecting group of global volunteers, known as miners... | 5 |

# 01_ARCHITECTURE/ontology/slots/05_state.md
+ | network value | cryptoassets | 0.95 | proposed | 2026-09-12 | | As of March 2017, there were over 800 cryptoassets with a fascinating family tree, accruing to a total network value... | 4 |
```

---

## 5. SECȚIUNEA B: Ce ar Adăuga Varianta B în Plus față de Varianta A

Un merge nefiltrat pe tot staging-ul ar adăuga **6 concepte suplimentare** net-noi (toate având `occurrences < 3`):
| Concept | Slot Țintă | Occ. | Sursă | Motiv de Respingere |
|---|---|---|---|---|
| **`hard fork`** | `routing` | 2 | `cryptoassets` | Sub pragul de frecvență (`occ < 3`); zgomot de trading izolat. |
| **`volatility`** | `judgement` | 2 | `cryptoassets` | Sub pragul de frecvență (`occ < 3`); zgomot de trading izolat. |
| **`correlation`** | `judgement` | 2 | `cryptoassets` | Sub pragul de frecvență (`occ < 3`); zgomot de trading izolat. |
| **`frog-in-the-pan momentum`** | `judgement` | 1 | `quantitative_momentum` | Sub pragul de frecvență (`occ < 3`); zgomot de trading izolat. |
| **`quality of momentum`** | `judgement` | 1 | `quantitative_momentum` | Sub pragul de frecvență (`occ < 3`); zgomot de trading izolat. |
| **`12-2 momentum`** | `procedures` | 1 | `quantitative_momentum` | Sub pragul de frecvență (`occ < 3`); zgomot de trading izolat. |

```diff
+ | hard fork | cryptoassets | 0.95 | proposed | 2026-09-12 | | BITCOIN’S FIRST DIGITAL SIBLING Namecoin15 was the first significant fork away from Bitcoin. Interes... | 2 |
+ | volatility | cryptoassets | 0.95 | proposed | 2026-09-12 | | 3 We’ll tackle the specifics of quantifying risk shortly, mainly through a discussion of volatility.... | 2 |
+ | correlation | cryptoassets | 0.95 | proposed | 2026-09-12 | | Correlation of Returns and the Efficient Frontier One of the key breakthroughs of modern portfolio t... | 2 |
+ | frog-in-the-pan momentum | quantitative_momentum | 0.95 | proposed | 2026-09-12 | | To calculate “frog-in-the-pan” momentum, the authors classify each daily return as either positive o... | 1 |
+ | quality of momentum | quantitative_momentum | 0.95 | proposed | 2026-09-12 | | In Step 3 we seek to identify the quality of momentum associated with the stocks from Step 2.... | 1 |
+ | 12-2 momentum | quantitative_momentum | 0.95 | proposed | 2026-09-12 | | 12/2 momentum is a strategy that sorts stocks based on their cumulative 12 month past returns (ignor... | 1 |
```

---

## 6. SECȚIUNEA D: Planul Celor 5 Anomalii Retroactive din Cele 43 Promovate

Spre deosebire de conceptele `proposed`, cele 43 de concepte promovate la commit-ul `596aabfb5` au note `REVIEW` create în `01_ARCHITECTURE/knowledge/` și 27 de muchii în `SynapseStore`.

### Constrângerea Critică de Schemă: `ALLOWED_RELATIONS` din `SynapseStore`

> [!IMPORTANT]

> Vocabularul relațional din `SynapseStore` (`03_IMPLEMENTATION/packages/graph/synapse_store.py`) este **strict închis** la 8 relații:

> - **STRONG**: `depends_on`, `contradicts`, `supersedes`, `caused`, `verified_by`, `applies_to`

> - **WEAK**: `related_to`, `part_of`

> 

> Relația `same_as` **NU EXISTĂ** în schemă. O relație `same_as` degradează automat la `related_to` (pierzând semantica de egalitate). Prin urmare, fuziunile sinonimice (#1 și #2) nu se pot face ca muchii de graf fără extinderea schemei.

### Planul de Acțiune per Anomalie:

1. **`explicit memory` + `declarative memory`** (`03_ontology.md`):
   - **Acțiune**: `declarative memory` rămâne conceptul canonic. În nota sa markdown adăugăm `aliases: ["explicit memory"]`. Rândul `explicit memory` din tabelul slotului devine `status=fused` cu referință la ID-ul notei declarative.
   - **Ce se pierde dacă o facem**: Nota individuală `REVIEW` a lui explicit memory este absorbită.
   - **Ce se pierde dacă NU**: Două noduri paralele în ontologie pentru același concept, fragmentând regăsirea.
2. **`implicit memory` + `nondeclarative memory`** (`03_ontology.md`):
   - **Acțiune**: `nondeclarative memory` rămâne conceptul canonic. Adăugăm `aliases: ["implicit memory"]`. Rândul din tabel devine `status=fused`.
   - **Ce se pierde dacă o facem**: Nota dedicată termenului operațional.
   - **Ce se pierde dacă NU**: Poluare duplicitară a taxonomiei memoriei.
3. **`state`** (`05_state.md`):
   - **Acțiune**: Deprecăm conceptul atomic `state` (este numele slotului). Nota sa devine notă de descriere a slotului `05_state`.
   - **Ce se pierde dacă o facem**: Un nod generic fără conținut operațional.
   - **Ce se pierde dacă NU**: Tautologie: containerul este promovat ca și conținut.
4. **`retrieval`** (`15_retrieval.md`):
   - **Acțiune**: Deprecăm conceptul ca nod atomic. Procesul este operaționalizat prin mecanisme reale (`associative recall`, `priming`).
   - **Ce se pierde dacă o facem**: Scade numărul conceptelor promovate cu 1.
   - **Ce se pierde dacă NU**: Numele subsistemului maschează algoritmii concreți de interogare.
5. **`sensitization`** (`03_ontology.md` $	o$ `06_procedures.md`):
   - **Acțiune**: Relocăm rândul și nota `sensitization` în `06_procedures.md` lângă reflexul geamăn `habituation`.
   - **Ce se pierde dacă o facem**: Editare pe două fișiere de slot.
   - **Ce se pierde dacă NU**: Fractură taxonomică: două reflexe non-asociative identice stau în sloturi diferite.
