---
id: b216e9d2-0284-5ab8-b799-dc5b66f079da
type: knowledge
lifecycle: REVIEW
category: rag/speculative_and_caching
tags:
- rag
- huyen
- speculative-decoding
- prefix-caching
- radix-attention
- inference-acceleration
- latency-optimization
created: '2026-09-04'
updated: '2026-09-04'
provenance:
  source_type: ai
  source_ref: 06_INBOX/RAW_IMPORTS/BOOKS/Chip-Huyen-LLM-Apps-Ch7-8
confidence: high
verification: unverified
relations:
- relation: references
  target: 01_KNOWLEDGE/BOOKS/LLM_Application_Design_and_RAG_Pipelines.md
- relation: references
  target: 01_KNOWLEDGE/BOOKS/MASTERY_RAG_Triad_and_Hallucination_Diagnostics.md
- relation: references
  target: 01_KNOWLEDGE/BOOKS/Caiet_Teme_Aplicatii_Practice_Carti.md
---

# RAG Expert: Decodare Speculativ? & Partajarea KV-Cache prin Prefix Caching (RadixAttention)

**Surs?**: Chip Huyen, *Designing Large Language Model Applications* (Capitolele 7 & 8) & Arhitecturi de Servire Moderne (vLLM, SGLang)  
**Domeniu**: Optimizarea Laten?ei la Inferen??, Accelerarea Conductelor RAG & Reutilizarea Cache-ului

---

## 1. Problema Decod?rii Autoregresive Seriale

Generarea fiec?rui token necesit? ?nc?rcarea tuturor greut??ilor modelului din VRAM ?n nucleele de calcul GPU. L??imea de band? a memoriei GPU impune o barier? fizic? de laten?? (debit limitat la $\approx 50-80$ ms per token pentru modele mari).

### Decodarea Speculativ? (Speculative Decoding - Leviathan et al. / Chen et al.)
Rezolv? blocajul utiliz?nd dou? modele:
1. **Model Propun?tor Mic (*Draft Model* - ex: 1B)**: Genereaz? rapid ?i ieftin o secven?? speculativ? de $K$ tokeni poten?iali: $[x_1, \dots, x_K]$.
2. **Model ?int? Mare (*Target Model* - ex: 70B)**: Evalueaz? to?i cei $K$ tokeni ?n paralel **?ntr-un singur pas de forward pass**!
3. **Criteriul de Acceptare (Rejection Sampling)**:
   - Pentru fiecare token $i$, se accept? specula?ia dac?:
     $$r_i \le \min\left(1, \frac{P_{\text{target}}(x_i \mid x_{<i})}{P_{\text{draft}}(x_i \mid x_{<i})}\right), \quad r_i \sim \mathcal{U}(0, 1)$$
   - Dac? tokenul $j$ este respins, generarea se opre?te la $j-1$, iar modelul ?int? e?antioneaz? un nou token din distribu?ia rezidual? corectat?.
- **Rezultat**: Accelera?ie de $2\times - 3\times$ **f?r? nicio degradare matematic? a calit??ii textului** (distribu?ia final? este identic? cu cea a modelului ?int?)!

---

## 2. Prefix Caching & RadixAttention ?n Sisteme RAG

?n aplica?iile RAG, o mare parte din prompt con?ine fragmente fixe (instruc?iuni de sistem, fragmente mari de context extras din baza de cuno?tin?e, exemple *few-shot*).
Dac? fiecare cerere recalculeaz? KV-cache pentru aceste prefixe repetate, resursele GPU sunt irosite.

### Arborele Radix de Prefix Caching (SGLang / vLLM)
- Toate secven?ele de tokeni procesate anterior sunt stocate ?ntr-un arbore Radix (*Radix Tree*) ?n memoria GPU.
- C?nd sose?te o nou? interogare cu context comun:
  1. Motorul g?se?te cel mai lung prefix comun ?n arbore.
  2. Reutilizeaz? instantaneu KV-cache-ul deja calculat pentru acel prefix.
  3. Calculeaz? doar diferen?a de tokeni noi.
- **Impact ?n Produc?ie**: Reduce laten?a primului token (TTFT - *Time To First Token*) cu p?n? la $80\%$ ?n conductele RAG cu documente lungi recurente.

