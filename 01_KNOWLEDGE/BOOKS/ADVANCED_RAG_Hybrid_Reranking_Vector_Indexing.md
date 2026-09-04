---
id: d4c0d850-553e-5d6b-b619-a159e0b34bb9
type: knowledge
lifecycle: REVIEW
category: architecture/rag_systems
tags:
- rag
- pai
- hybrid-search
- bm25
- dense-retrieval
- reciprocal-rank-fusion
- colbert
- hnsw
created: '2026-09-04'
updated: '2026-09-04'
provenance:
  source_type: ai
  source_ref: "06_INBOX/RAW_IMPORTS/BOOKS/Suhas-Pai-Designing-LLM-Apps"
confidence: high
verification: unverified
relations:
- relation: references
  target: 01_KNOWLEDGE/BOOKS/LLM_Application_Design_and_RAG_Pipelines.md
- relation: references
  target: 01_KNOWLEDGE/BOOKS/Caiet_Teme_Aplicatii_Practice_Carti.md
- relation: references
  target: 00_CORE/System_Architecture.md
---

# RAG Avansat: Căutare Hibridă, Re-ranking, Fuziune RRF & Indexare Vectorială

**Sursă**: Suhas Pai, *Designing Large Language Model Applications*  
**Domeniu**: Recuperare de Informație (IR), Pipeline-uri RAG & Indexare Eficientă

---

## 1. Căutare Hibridă & Fuziunea Rangurilor Reciproce (RRF)

Combinarea căutării lexicale (BM25 / Keyword Overlap) cu cea semantică (Dense Embeddings) neutralizează punctele slabe ale fiecăreia:
- **BM25**: Excelent la identificatori exacți (coduri de eroare, nume de fișiere, variabile), dar orb la sinonime.
- **Dense Embeddings**: Excelent la semnificație semantică și parafrazare, dar vulnerabil la potriviri lexicale exacte pe termeni rari.

### Formula Reciprocal Rank Fusion (RRF)
Pentru un set de documente regăsite din $M$ sisteme de clasificare diferite:

$$RRF(d) = \sum_{m=1}^{M} \frac{1}{k + r_m(d)}$$

- $r_m(d)$: Rangul documentului $d$ în sistemul de căutare $m$ (1-indexed).
- $k$: Constantă de amortizare a clasamentului (în general $k = 60$).
- RRF nu depinde de scalarea absolută a scorurilor (care diferă între BM25 și cosine similarity), fiind robust împotriva scorurilor necalibrate.

---

## 2. Interacțiune Târzie (Late Interaction — ColBERT)

În modelele dense clasice (Bi-encoders), un document întreg este comprimat într-un singur vector de 768 sau 1536 de dimensiuni, pierzând detalii fine.
- **Mecanismul ColBERT**: Păstrează câte un vector per jeton atât pentru interogare cât și pentru document.
- **Operatorul $MaxSim$**: Calculează similaritatea maximă pentru fiecare jeton din interogare $q_i$ cu toți vectorii de jetoane ai documentului $d_j$:

$$\text{Score}(Q, D) = \sum_{i \in Q} \max_{j \in D} \left( E_{q_i} \cdot E_{d_j}^T \right)$$

Aceasta permite o rezoluție semantică extrem de înaltă la cost de indexare moderat.

---

## 3. Indexare Vectorială Scalabilă: Grafuri HNSW & Quantizare IVF-PQ

Căutarea liniară (Brute-force k-NN) are complexitate $\mathcal{O}(N \cdot D)$, devenind inaccesibilă pentru baze de date cu milioane de memorii.
- **HNSW (Hierarchical Navigable Small World)**:
  - Construiește un graf multistrat unde straturile superioare au legături cu pas lung (expres, de tip skip-list), iar straturile inferioare au densitate mare.
  - Complexitate de căutare: $\mathcal{O}(\log N)$.
- **IVF-PQ (Inverted File with Product Quantization)**:
  - *Inverted File (IVF)*: Partiționează spațiul în clustere Voronoi via k-means. La căutare, se explorează doar cele mai apropiate $n_{\text{probe}}$ clustere.
  - *Product Quantization (PQ)*: Comprimă vectorii de floating-point (32 biți) în coduri scurte de 8 biți prin descompunere în subvectori, reducând memoria RAM cu 90–95%.

---

## 4. Evaluarea Calității Regăsirii: Metrici Standard

1. **Precision@K & Recall@K**: Raportul documentelor relevante returnate în primele $K$ poziții.
2. **Mean Reciprocal Rank (MRR)**: Măsoară poziția primului document relevant:
   $$MRR = \frac{1}{|Q|} \sum_{i=1}^{|Q|} \frac{1}{\text{rank}_i}$$
3. **Normalized Discounted Cumulative Gain (NDCG@K)**: Evaluează ordonarea relativă cu penalizare logaritmică a documentelor relevante plasate pe poziții inferioare.

---

## 5. Playbook Operațional: Ce fac când construiesc un pipeline RAG?

1. **Folosesc întotdeauna căutare hibridă cu RRF**: Nu mă bazez exclusiv pe vectori de embedding, deoarece termenii exacți de cod sau hash-urile SHA-256 necesită BM25.
2. **Aplic Re-ranking pe primele 20 de rezultate**: Folosesc un model Cross-Encoder ușor pentru a cerne top 5 documente finale.
3. **Măsor constant MRR și Precision@3**: Trasez performanța cu `trace_cli.py` pentru a detecta scăderile de calitate înainte ca utilizatorul să observe halucinații.
