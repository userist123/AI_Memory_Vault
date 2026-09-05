---
id: "knw-benchmarks-2026-0001"
type: knowledge
lifecycle: ACTIVE
category: evaluation-benchmarks
tags: [benchmarks, locomo, longmemeval, beam, ir-metrics, graphrag, precision-k, bi-temporal]
created: 2026-08-24T23:31:00Z
updated: 2026-08-24T23:33:00Z
provenance:
  source_type: official
  source_ref: "ai-memory-benchmarks-2026-deep-research"
confidence: very_high
verification: verified
relations:
  - "01_ARCHITECTURE/System_Architecture.md"
  - "00_GOVERNANCE/protocols/Confidence_Model.md"
  - "01_ARCHITECTURE/graphs/02 Memory Knowledge Map.md"
---

# 📊 Ghid Canonic: Benchmark-uri & Metrici de Evaluare Memorie AI (2026 Standards)

Acest ghid specifică metricele matematice și benchmark-urile standard de evaluare a performanței memoriei agentice pentru `AI_Memory_Vault_CODEX_READY`.

---

## 🏆 1. Cele 3 Benchmark-uri Industriale Principale (2026)

```text
       +-----------------------------------------------------------------------+
       |               AI AGENT MEMORY EVALUATION LANDSCAPE (2026)             |
       +-----------------------------------------------------------------------+
       |                                                                       |
       |  LoCoMo (ACL 2024/2026)       LongMemEval / LME-V2 (2026)    BEAM (ICLR 2026) |
       |  ----------------------       --------------------------    ----------------  |
       |  * Conversational Memory      * Chat & Web Agent            * Scale Frontier  |
       |  * Up to 35 sessions          * Trajectories up to 115M tok * 128K - 10M tok   |
       |  * 4 Reasoning Categories     * Abstention & Workflow       * 10 Capabilities |
       |                                                                               |
       +-----------------------------------------------------------------------+
```

### A. Benchmark-ul LoCoMo (Long-Context Dynamic Memory)
- **Scop**: Evaluează capacitatea agentului de a păstra, actualiza și recupera memorii pe traiectorii conversaționale cu orizont lung (peste 35 sesiuni conversaționale).
- **Categorii de Raționament**:
  1. **Single-Hop Factual Recall**: Extragerea de fapte specifice din sesiuni anterioare.
  2. **Multi-Hop Reasoning**: Lănțuirea de fapte disparate din sesiuni non-adiacente.
  3. **Temporal Reasoning**: Urmărirea cronologiei evenimentelor și ordonării secvențiale.
  4. **Open-Domain Synthesis**: Sintetizarea evidențelor contextualizate pe întreaga memorie.

### B. Benchmark-ul LongMemEval (LME & LME-V2)
- **Scop**: Măsoară raționamentul temporal, gestiunea contradicțiilor, abținerea de la halucinații (*Abstention Check*) și consolidarea traiectoriilor lungi (până la 115 milioane de tokeni).

### C. Benchmark-ul BEAM (Benchmark for Evaluating Agent Memory — ICLR 2026)
- **Scop**: Evaluează sistemul de memorie pe 4 niveluri extreme de scală: **128K, 500K, 1M și 10M tokeni**, acoperind 10 capabilități agentice (inclusiv urmărirea preferințelor, rezoluția contradicțiilor și persistența stării procedurale).

---

## 📈 2. Metrici de Information Retrieval (IR) & Bi-Temporal GraphRAG

În memoriile bi-temporale, fiecare fapt este delimitat de două dimensiuni de timp:
- **Valid Time ($T_v$)**: Intervalul real în care faptul este adevărat.
- **Transaction Time ($T_t$)**: Intervalul în care faptul a fost înregistrat în ledger-ul imutabil.

### Formule Matematice de Evaluare IR

#### 1. Precision@K ($\text{P}@K$)
$$\text{Precision}@K = \frac{\sum_{i=1}^{K} \text{rel}(r_i) \cdot \mathbb{I}(\text{Valid}(r_i, T_q))}{\min(K, |R|)}$$

#### 2. Recall@K ($\text{R}@K$)
$$\text{Recall}@K = \frac{\sum_{i=1}^{K} \text{rel}(r_i) \cdot \mathbb{I}(\text{Valid}(r_i, T_q))}{|\text{Gold\_Facts}(T_q)|}$$

#### 3. Normalized Discounted Cumulative Gain at K ($\text{NDCG}@K$)
$$\text{DCG}@K = \sum_{i=1}^{K} \frac{2^{\text{gain}(r_i, T_q)} - 1}{\log_2(i + 1)}, \quad \text{NDCG}@K = \frac{\text{DCG}@K}{\text{IDCG}@K}$$

#### 4. Mean Reciprocal Rank (MRR)
$$\text{MRR} = \frac{1}{|Q|} \sum_{j=1}^{|Q|} \frac{1}{\text{rank}_j (\text{First Valid \& Relevant Node})}$$

---

## ⚙️ 3. Matrice de Evaluare pentru AI Memory Vault

| Metrică | Prag Țintă | Modul de Validare în Vault |
|---|---|---|
| **Precision@5** | $\ge 0.85$ | Verifică relevanța temporală a primelor 5 note returnate |
| **Recall@10** | $\ge 0.92$ | Garantează că nicio notă de cunoștințe validă nu este omisă |
| **NDCG@5** | $\ge 0.88$ | Penalizează afișarea notelor învechite (`SUPERSEDED`) înaintea celor `ACTIVE` |
| **MRR** | $\ge 0.90$ | Se asigură că primul rezultat este principala sursă validă |
| **Graph Path Consistency** | $100\%$ | Validare strictă că lanțul temporal al grafului nu conține paradoxuri |

---

## 🔗 Legături de Memorie & Graf Obsidian
- [[02 Memory Knowledge Map]]
- [[Knowledge Graph Home]]
- [[System_Architecture]]
