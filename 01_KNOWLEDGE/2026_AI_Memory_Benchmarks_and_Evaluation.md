---
id: "knw-benchmarks-2026-0001"
type: knowledge
lifecycle: ACTIVE
category: evaluation-benchmarks
tags: [benchmarks, locomo, longmemeval, beam, ir-metrics, graphrag, precision-k]
created: 2026-08-24T23:31:00Z
updated: 2026-08-24T23:31:00Z
provenance:
  source_type: official
  source_ref: "ai-memory-benchmarks-2026"
confidence: very_high
verification: verified
relations:
  - "00_CORE/System_Architecture.md"
  - "00_CORE/Confidence_Model.md"
  - "00_CORE/GRAPH/02 Memory Knowledge Map.md"
---

# 📊 Ghid Canonic: Benchmark-uri & Metrici de Evaluare Memorie AI (2026 Standards)

Acest ghid specifică metricele și benchmark-urile standard de evaluare a performanței memoriei agentice pentru `AI_Memory_Vault_CODEX_READY`.

---

## 🏆 1. Cele 3 Benchmark-uri Industriale Principale (2026)

### A. Benchmark-ul LoCoMo (Long-Context Dynamic Memory)
- **Scop**: Evaluează capacitatea agentului de a păstra, actualiza și recupera memorii pe traiectorii conversaționale cu orizont lung (peste 100k tokeni).
- **Metrici Cheie**:
  - **Dynamic Retention Rate (DRR)**: Procentul de fapte actualizate corect reținute fără degradarea memoriilor vechi.
  - **Context Decay Half-Life**: Numărul de pași operaționali până când precizia de recuperare scade sub 90%.

### B. Benchmark-ul LongMemEval (Evaluare Bi-Temporală & Contradicții)
- **Scop**: Măsoară raționamentul temporal, gestiunea contradicțiilor și rezoluția notelor înlocuite (`RECONSOLIDATING` / `SUPERSEDED`).
- **Capabilități Evaluat**:
  - **Fact Invalidation Speed**: Timpul necesar marcării unei memori vechi ca fiind invalidată când apare o memorie nouă explicită.
  - **Bi-Temporal Accuracy**: Corectitudinea răspunsurilor la întrebări temporale (*"Care era valoarea parametrului X înainte de actualizarea din martie?"*).

### C. Benchmark-ul BEAM (Benchmark for Procedural & Adaptation Memory)
- **Scop**: Evaluează adaptarea procedurală a agenților, reutilizarea rețetelor din `03_PROCEDURES/` și prevenirea repetării erorilor documentate în `04_MEMORY/Lessons/`.

---

## 📈 2. Metrici de Information Retrieval (IR) pentru MemoryController

| Metrică IR | Formulă / Definiție | Prag Țintă Vault v4.5.0 |
|---|---|---|
| **Precision@K** | $\frac{|\text{Note Relevante în Top K}|}{K}$ | $\ge 0.85$ (pentru $K=5$) |
| **Recall@K** | $\frac{|\text{Note Relevante în Top K}|}{|\text{Total Note Relevante}|}$ | $\ge 0.90$ (pentru $K=10$) |
| **MRR (Mean Reciprocal Rank)** | $\frac{1}{|Q|} \sum_{i=1}^{|Q|} \frac{1}{\text{rank}_i}$ | $\ge 0.88$ |
| **NDCG@K (Normalized Discounted Cumulative Gain)** | $\frac{\text{DCG}_K}{\text{IDCG}_K}$ | $\ge 0.92$ |

---

## 🔗 Legături de Memorie & Graf Obsidian
- [[02 Memory Knowledge Map]]
- [[Knowledge Graph Home]]
- [[System_Architecture]]
