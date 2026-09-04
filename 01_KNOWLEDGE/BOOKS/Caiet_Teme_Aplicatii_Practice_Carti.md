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
---

# Caiet de Teme & Aplicații Practice: Laboratorul Celor 6 Cărți

**Rol**: Manual operațional de laborator și exerciții rezolvate pentru transpunerea teoriei în sarcini practice de producție.  
**Metodologie**: Învățare activă $\to$ Notițe sintetice $\to$ Teme rezolvate pas cu pas $\to$ Playbook de execuție imediată.

---

## Cele 6 Teme de Laborator & Decizii Operaționale

| Nr. | Carte Sursă | Tema Aplicată | Cod / Algoritm Cheie | Decizia la primirea unei sarcini |
|---|---|---|---|---|
| **1** | **DDIA** (Kleppmann) | Crash Recovery & Append-Only WAL | `SafeAppendOnlyWAL` cu `os.fsync` și verificare SHA-256 | Nu scriu direct pe fișierul țintă; folosesc fișiere temporare + `os.replace` atomic |
| **2** | **AIMA 4e** (Russell & Norvig) | Căutare Heuristică $A^*$ Bounded | `a_star_bounded_search(max_hops=2)` | Plafonez explorarea în graful de memorie la $\le 2$ hop-uri pentru a evita explozia contextului |
| **3** | **Agent Architecture** (Zvarydchuk) | Scoping Least Privilege & Retry Triad | `execute_scoped_tool(role, tool, args)` | Izolez uneltele fiecărui rol; aplic protocolul: `retry` $\to$ `replan` $\to$ `escalate` |
| **4** | **Designing LLM Apps** (Pai) | Demarcare XML & Filtrare Injecție | Formatare `<untrusted_memory id="...">` | Tratez datele din memorie strict ca text pasiv; blochez instrucțiunile imbricate |
| **5** | **Designing ML Systems** (Huyen) | Monitor de Derivă de Date (PSI) | `calculate_population_stability_index()` | Verific deviația distribuției scorurilor de căutare; trimit eșecurile în volanta de date |
| **6** | **Learning Deep Learning** (Ekman) | Calcul Atenție & LoRA $\Delta W$ | $\text{Softmax}(QK^T / \sqrt{d_k})V$, $\Delta W = \frac{\alpha}{r}BA$ | Calibrez rangul LoRA ($r=16..64$, $\alpha=2r$) pe toate proiecțiile liniare (`all-linear`) |

Ghidul complet cu implementările în cod este documentat în:
- [`.agents/skills/learn/references/caiet_de_teme_si_aplicatii_practice.md`](file:///c:/Users/user/Documents/Codex/AI_Memory_Vault_CODEX_READY/.agents/skills/learn/references/caiet_de_teme_si_aplicatii_practice.md)

---

## 🔗 Legături de Memorie & Graf Obsidian
- [[01_KNOWLEDGE/BOOKS/DDIA_Distributed_Storage_Reliability]]
- [[01_KNOWLEDGE/BOOKS/AIMA_Rational_Agents_and_Search]]
- [[01_KNOWLEDGE/BOOKS/Agent_Architecture_and_Tool_Orchestration]]
- [[01_KNOWLEDGE/BOOKS/LLM_Application_Design_and_RAG_Pipelines]]
- [[01_KNOWLEDGE/BOOKS/Production_ML_Systems_and_Continual_Learning]]
- [[01_KNOWLEDGE/BOOKS/Deep_Learning_Representations_and_Attention]]
- [[Knowledge Graph Home]]
