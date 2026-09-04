---
id: ba5817e8-e829-5900-9f26-d9b66e08e92b
type: knowledge
lifecycle: REVIEW
category: architecture/graph_rag
tags:
- rag
- pai
- graphrag
- community-detection
- leiden-algorithm
- hierarchical-summarization
- global-search
created: '2026-09-04'
updated: '2026-09-04'
provenance:
  source_type: ai
  source_ref: "06_INBOX/RAW_IMPORTS/BOOKS/Suhas-Pai-GraphRAG-and-Knowledge-Graphs"
confidence: high
verification: unverified
relations:
- relation: references
  target: 01_KNOWLEDGE/BOOKS/LLM_Application_Design_and_RAG_Pipelines.md
- relation: references
  target: 01_KNOWLEDGE/BOOKS/ADVANCED_RAG_Hybrid_Reranking_Vector_Indexing.md
- relation: references
  target: 01_KNOWLEDGE/BOOKS/Caiet_Teme_Aplicatii_Practice_Carti.md
- relation: references
  target: 00_CORE/System_Architecture.md
---

# RAG Specializat: GraphRAG & Rezumatul Comunităților din Graf

**Sursă**: Suhas Pai, *Designing Large Language Model Applications*  
**Domeniu**: Grafuri de Cunoștințe, Recuperare Globală & Algoritmi de Comunitate

---

## 1. Limitările RAG-ului Clasic bazat pe Vectori (Needle-in-a-Haystack)

RAG-ul clasic prin fragmente (chunks) este optimizat pentru **întrebări punctuale** (ex: „Care este parola implicită a dispozitivului X?”), dar eșuează lamentabil la **întrebări holistice/globale** (ex: „Care sunt principalele teme și vulnerabilități identificate în întregul audit?”).
- Căutarea vectorială de top-k returnează doar 5–10 fragmente izolate, ignorând conexiunile la nivel de întreg sistem.

---

## 2. Arhitectura GraphRAG (Microsoft Research / Suhas Pai)

GraphRAG extrage o rețea de entități și relații din întregul corpus și construiește o ierarhie de rezumate:

```text
[Corpus Documente] ---> [Extragere Entități & Relații (LLM / Spacy)]
                               |
                               v
                     [Graful de Cunoștințe]
                               |
         +---------------------+---------------------+
         | (Detecție Comunități - Leiden Algorithm)  |
         v                                           v
 [Comunități Nivel 0]                       [Comunități Nivel 1]
   (Sub-domenii fine)                        (Domenii globale mari)
         |                                           |
         v                                           v
[Rezumate Comunitare L0]                   [Rezumate Comunitare L1]
```

### Algoritmul Leiden pentru Detecția Comunităților
- Partiționează nodurile grafului în comunități dens conectate, maximizând modularitatea rețelei:
  $$Q = \frac{1}{2m} \sum_{ij} \left[ A_{ij} - \frac{k_i k_j}{2m} \right] \delta(c_i, c_j)$$
- Spre deosebire de algoritmul Louvain, Leiden garantează că toate comunitățile detectate sunt conectate intern (fără componente disjuncte).

### Căutare Globală vs. Căutare Locală
- **Căutare Locală (Local Search)**: Explorează entitățile direct legate de query și vecinii lor la 1–2 hop-uri.
- **Căutare Globală (Global Search)**: Interoghează în paralel rezumatele comunităților de nivel înalt (L1), permițând sinteze de ansamblu peste zeci de mii de fișiere.

---

## 3. Playbook Operațional: Ce fac când un utilizator cere o sinteză peste tot Vault-ul?

1. **Nu execut grep pe tot discul**: Aceasta încarcă mii de fișiere și depășește bugetul de jetoane.
2. **Consult Maps of Content (MOC)**: Caut în hărțile de domenii (`00_CORE/GRAPH/07 Knowledge Domains Map.md`), care funcționează exact ca rezumatele comunitare L1 din GraphRAG.
3. **Plafon de sinteză**: Folosesc rezumatele de la nivel de comunitate pentru a răspunde întrebărilor panoramice.
