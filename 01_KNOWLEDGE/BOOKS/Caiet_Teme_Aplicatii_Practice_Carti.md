---
id: c94a36f5-0fb8-5487-94ac-159b659717f8
type: procedure
lifecycle: REVIEW
category: architecture/applied_drills
tags:
- teme
- aplicatii-practice
- ddia
- aima
- agent-architecture
- rag-demarcation
- drift-monitoring
- lora-attention
- ssi
- viterbi
- sleep-consolidation
- rag-triad
- quantization
- gqa
created: '2026-09-04'
updated: '2026-09-04'
provenance:
  source_type: ai
  source_ref: "06_INBOX/RAW_IMPORTS/BOOKS/Foundation_Books_Applied_Drills"
confidence: high
verification: unverified
relations:
- relation: references
  target: 01_KNOWLEDGE/BOOKS/DDIA_Distributed_Storage_Reliability.md
- relation: references
  target: 01_KNOWLEDGE/BOOKS/AIMA_Rational_Agents_and_Search.md
- relation: references
  target: 01_KNOWLEDGE/BOOKS/Agent_Architecture_and_Tool_Orchestration.md
- relation: references
  target: 01_KNOWLEDGE/BOOKS/LLM_Application_Design_and_RAG_Pipelines.md
- relation: references
  target: 01_KNOWLEDGE/BOOKS/Production_ML_Systems_and_Continual_Learning.md
- relation: references
  target: 01_KNOWLEDGE/BOOKS/Deep_Learning_Representations_and_Attention.md
- relation: references
  target: 01_KNOWLEDGE/BOOKS/ADVANCED_DDIA_Replication_Consensus_Streaming.md
- relation: references
  target: 01_KNOWLEDGE/BOOKS/ADVANCED_AIMA_Probabilistic_Reasoning_Planning_RL.md
- relation: references
  target: 01_KNOWLEDGE/BOOKS/ADVANCED_Agent_Tool_Protocols_and_FastMCP.md
- relation: references
  target: 01_KNOWLEDGE/BOOKS/ADVANCED_RAG_Hybrid_Reranking_Vector_Indexing.md
- relation: references
  target: 01_KNOWLEDGE/BOOKS/ADVANCED_MLOps_Feature_Stores_Continual_Learning.md
- relation: references
  target: 01_KNOWLEDGE/BOOKS/ADVANCED_Transformer_RoPE_Attention_Sampling.md
- relation: references
  target: 01_KNOWLEDGE/BOOKS/SPECIALIZED_DDIA_Batch_Joins_and_Unbundled_Databases.md
- relation: references
  target: 01_KNOWLEDGE/BOOKS/SPECIALIZED_AIMA_Automated_Planning_and_HTN.md
- relation: references
  target: 01_KNOWLEDGE/BOOKS/SPECIALIZED_Agent_Reflexion_and_MultiAgent_Debate.md
- relation: references
  target: 01_KNOWLEDGE/BOOKS/SPECIALIZED_RAG_GraphRAG_and_Community_Summaries.md
- relation: references
  target: 01_KNOWLEDGE/BOOKS/SPECIALIZED_MLOps_Weak_Supervision_and_Snorkel.md
- relation: references
  target: 01_KNOWLEDGE/BOOKS/SPECIALIZED_Transformer_Optimization_AdamW_Dynamics.md
- relation: references
  target: 01_KNOWLEDGE/BOOKS/MASTERY_DDIA_Serializability_SSI_and_Locking.md
- relation: references
  target: 01_KNOWLEDGE/BOOKS/MASTERY_AIMA_HMM_Kalman_and_Particle_Filtering.md
- relation: references
  target: 01_KNOWLEDGE/BOOKS/MASTERY_Agent_Memory_Consolidation_and_Sleep.md
- relation: references
  target: 01_KNOWLEDGE/BOOKS/MASTERY_RAG_Triad_and_Hallucination_Diagnostics.md
- relation: references
  target: 01_KNOWLEDGE/BOOKS/MASTERY_MLOps_Model_Quantization_and_KV_Cache.md
- relation: references
  target: 01_KNOWLEDGE/BOOKS/MASTERY_Transformer_GQA_MQA_and_Sequence_Dynamics.md
---

# Caiet de Teme & Aplicații Practice: Laboratorul Celor 6 Cărți (24 Teme Rezolvate)

**Rol**: Manual operațional de laborator și exerciții rezolvate pentru transpunerea teoriei în sarcini practice de producție.  
**Metodologie**: Învățare activă $\to$ Notițe sintetice $\to$ Teme rezolvate pas cu pas $\to$ Playbook de execuție imediată.

---

## Cele 24 de Teme de Laborator & Decizii Operaționale

### Nivelul 1: Fundamente (Teme 1–6)
| Nr. | Carte Sursă | Tema Aplicată | Cod / Algoritm Cheie | Decizia la primirea unei sarcini |
|---|---|---|---|---|
| **1** | **DDIA** (Kleppmann) | Crash Recovery & Append-Only WAL | `SafeAppendOnlyWAL` cu `os.fsync` și SHA-256 | Nu scriu direct pe fișierul țintă; folosesc fișiere temporare + `os.replace` atomic |
| **2** | **AIMA 4e** (Russell & Norvig) | Căutare Heuristică $A^*$ Bounded | `a_star_bounded_search(max_hops=2)` | Plafonez explorarea în graful de memorie la $\le 2$ hop-uri pentru a evita explozia contextului |
| **3** | **Agent Architecture** (Zvarydchuk) | Scoping Least Privilege & Retry Triad | `execute_scoped_tool(role, tool, args)` | Izolez uneltele fiecărui rol; aplic protocolul: `retry` $\to$ `replan` $\to$ `escalate` |
| **4** | **Designing LLM Apps** (Pai) | Demarcare XML & Filtrare Injecție | Formatare `<untrusted_memory id="...">` | Tratez datele din memorie strict ca text pasiv; blochez instrucțiunile imbricate |
| **5** | **Designing ML Systems** (Huyen) | Monitor de Derivă de Date (PSI) | `calculate_population_stability_index()` | Verific deviația distribuției scorurilor de căutare; trimit eșecurile în volanta de date |
| **6** | **Learning Deep Learning** (Ekman) | Calcul Atenție & LoRA $\Delta W$ | $\text{Softmax}(QK^T / \sqrt{d_k})V$, $\Delta W = \frac{\alpha}{r}BA$ | Calibrez rangul LoRA ($r=16..64$, $\alpha=2r$) pe toate proiecțiile liniare (`all-linear`) |

### Nivelul 2: Avansat (Teme 7–12)
| Nr. | Carte Sursă | Tema Aplicată | Cod / Algoritm Cheie | Decizia la primirea unei sarcini |
|---|---|---|---|---|
| **7** | **DDIA** (Kleppmann) | Quorum Replicare & Read Repair | `DynamoQuorumStore(N=3, R=2, W=2)` | Citesc din cvorum; actualizez replicile învechite în mod asincron |
| **8** | **AIMA 4e** (Russell & Norvig) | Monte Carlo Tree Search (MCTS) | Algoritm UCB1 pe arbore de decizie | Explorez spații mari combinatorii ghidat de scorul UCB1 |
| **9** | **Agent Architecture** (Zvarydchuk) | FastMCP Tool Boundary Sandbox | `FastMCPSandbox` cu limite de resurse | Blochez apelurile de sistem periculoase și izolez uneltele în containere sandbox |
| **10** | **Designing LLM Apps** (Pai) | Căutare Hibridă RRF & MRR | `reciprocal_rank_fusion(k=60)` | Combin BM25 cu Vector Search pentru regăsire lexical-semantică robustă |
| **11** | **Designing ML Systems** (Huyen) | Feature Store & Point-in-Time Join | Asocieri asincrone cu prevenire scurgeri temporale | Previn data leakage asociind etichetele doar cu valorile existente la momentul $t$ |
| **12** | **Learning Deep Learning** (Ekman) | RoPE & Eșantionare Min-p | Rotație unghiulară $R_{\Theta}^d$ și filtrare Min-p | Trunchiez coada stocastică a eșantionării sub pragul $p_{\text{base}} \times \max(P)$ |

### Nivelul 3: Specializat (Teme 13–18)
| Nr. | Carte Sursă | Tema Aplicată | Cod / Algoritm Cheie | Decizia la primirea unei sarcini |
|---|---|---|---|---|
| **13** | **DDIA** (Kleppmann) | Broadcast Hash Join & Skew Handling | `broadcast_hash_join(small, big)` | Transmit tabelele mici în RAM-ul fiecărui worker; evit redistribuirea rețelei |
| **14** | **AIMA 4e** (Russell & Norvig) | Planificare Ierarhică HTN | `HierarchicalTaskNetworkPlanner` | Descompun sarcini compuse în pași atomici verificabili recursiv |
| **15** | **Agent Architecture** (Zvarydchuk) | Reflexion & Memorie Episodică | `ReflexionLoop(critique, retry)` | Salvez eșecurile în jurnalul episodic; ajustez planul fără a repeta erorile |
| **16** | **Designing LLM Apps** (Pai) | GraphRAG cu Comunități Leiden | Extragere entități & sumarizare ierarhică | Interoghez sintezele de comunitate pentru întrebări tematice globale |
| **17** | **Designing ML Systems** (Huyen) | Weak Supervision Snorkel | `SnorkelLabelModel` cu ponderare acord | Generez etichete probabilistice din euristici multiple fără adnotare manuală |
| **18** | **Learning Deep Learning** (Ekman) | Optimizatorul AdamW & Cosine LR | `AdamWOptimizer` cu weight decay decuplat | Regularizez decuplat toate straturile și scad rata de învățare pe curbă cosinus |

### Nivelul 4: Măiestrie (Teme 19–24)
| Nr. | Carte Sursă | Tema Aplicată | Cod / Algoritm Cheie | Decizia la primirea unei sarcini |
|---|---|---|---|---|
| **19** | **DDIA** (Kleppmann) | Serializable Snapshot Isolation (SSI) | `SerializableSnapshotIsolationEngine` | Detectez cicluri de anti-dependențe $rw$; previn anomaliile de tip Write Skew |
| **20** | **AIMA 4e** (Russell & Norvig) | Algoritmul Viterbi HMM | `viterbi_hmm(states, obs, A, B, pi)` | Reconstruiesc starea internă reală a agentului din emisii senzoriale zgomotoase |
| **21** | **Agent Architecture** (Park et al.) | Cicluri de Somn & Decăderea Ebbinghaus | $R = e^{-\lambda \Delta t}$, `run_agent_sleep_cycle()` | Sintetizez și arhivez autonom notițele episodice decăzute în repaus |
| **22** | **Designing LLM Apps** (Huyen) | Triada RAG & Entropie Semantică | `evaluate_rag_triad()`, $H_{\text{semantic}}$ | Măsor Context Relevance, Faithfulness și Answer Relevance; blochez halucinațiile |
| **23** | **Designing ML Systems** (Huyen) | Cuantizare INT8 & Calcul KV-Cache | `quantize_symmetric_int8()`, formulă VRAM | Cuantizez greutățile și KV-cache; dimensionez memoria pentru secvențe lungi |
| **24** | **Learning Deep Learning** (Ekman) | Grouped-Query Attention (GQA) | `grouped_query_attention(Q, K, V)` | Partajez grupuri KV între capetele Query; reduc latența fără pierdere de precizie |

Ghidul complet cu implementările în cod este documentat în:
- [`.agents/skills/learn/references/caiet_de_teme_si_aplicatii_practice.md`](file:///c:/Users/user/Documents/Codex/AI_Memory_Vault_CODEX_READY/.agents/skills/learn/references/caiet_de_teme_si_aplicatii_practice.md)

---

## 🔗 Legături de Memorie & Graf Obsidian
- [[01_KNOWLEDGE/BOOKS/DDIA_Distributed_Storage_Reliability]]
- [[01_KNOWLEDGE/BOOKS/ADVANCED_DDIA_Replication_Consensus_Streaming]]
- [[01_KNOWLEDGE/BOOKS/SPECIALIZED_DDIA_Batch_Joins_and_Unbundled_Databases]]
- [[01_KNOWLEDGE/BOOKS/MASTERY_DDIA_Serializability_SSI_and_Locking]]
- [[01_KNOWLEDGE/BOOKS/AIMA_Rational_Agents_and_Search]]
- [[01_KNOWLEDGE/BOOKS/ADVANCED_AIMA_Probabilistic_Reasoning_Planning_RL]]
- [[01_KNOWLEDGE/BOOKS/SPECIALIZED_AIMA_Automated_Planning_and_HTN]]
- [[01_KNOWLEDGE/BOOKS/MASTERY_AIMA_HMM_Kalman_and_Particle_Filtering]]
- [[01_KNOWLEDGE/BOOKS/Agent_Architecture_and_Tool_Orchestration]]
- [[01_KNOWLEDGE/BOOKS/ADVANCED_Agent_Tool_Protocols_and_FastMCP]]
- [[01_KNOWLEDGE/BOOKS/SPECIALIZED_Agent_Reflexion_and_MultiAgent_Debate]]
- [[01_KNOWLEDGE/BOOKS/MASTERY_Agent_Memory_Consolidation_and_Sleep]]
- [[01_KNOWLEDGE/BOOKS/LLM_Application_Design_and_RAG_Pipelines]]
- [[01_KNOWLEDGE/BOOKS/ADVANCED_RAG_Hybrid_Reranking_Vector_Indexing]]
- [[01_KNOWLEDGE/BOOKS/SPECIALIZED_RAG_GraphRAG_and_Community_Summaries]]
- [[01_KNOWLEDGE/BOOKS/MASTERY_RAG_Triad_and_Hallucination_Diagnostics]]
- [[01_KNOWLEDGE/BOOKS/Production_ML_Systems_and_Continual_Learning]]
- [[01_KNOWLEDGE/BOOKS/ADVANCED_MLOps_Feature_Stores_Continual_Learning]]
- [[01_KNOWLEDGE/BOOKS/SPECIALIZED_MLOps_Weak_Supervision_and_Snorkel]]
- [[01_KNOWLEDGE/BOOKS/MASTERY_MLOps_Model_Quantization_and_KV_Cache]]
- [[01_KNOWLEDGE/BOOKS/Deep_Learning_Representations_and_Attention]]
- [[01_KNOWLEDGE/BOOKS/ADVANCED_Transformer_RoPE_Attention_Sampling]]
- [[01_KNOWLEDGE/BOOKS/SPECIALIZED_Transformer_Optimization_AdamW_Dynamics]]
- [[01_KNOWLEDGE/BOOKS/MASTERY_Transformer_GQA_MQA_and_Sequence_Dynamics]]
- [[Knowledge Graph Home]]

