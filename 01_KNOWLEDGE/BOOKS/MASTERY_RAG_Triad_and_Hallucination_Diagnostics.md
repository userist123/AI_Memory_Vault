---
id: 5299840c-839a-5194-bf48-432d9e290c29
type: knowledge
lifecycle: REVIEW
category: rag/triad_and_diagnostics
tags:
- rag
- huyen
- rag-triad
- context-relevance
- faithfulness
- answer-relevance
- semantic-entropy
- hallucination-detection
created: '2026-09-04'
updated: '2026-09-04'
provenance:
  source_type: ai
  source_ref: 06_INBOX/RAW_IMPORTS/BOOKS/Chip-Huyen-LLM-Apps-Ch6
confidence: high
verification: unverified
relations:
- relation: references
  target: 01_KNOWLEDGE/BOOKS/LLM_Application_Design_and_RAG_Pipelines.md
- relation: references
  target: 01_KNOWLEDGE/BOOKS/ADVANCED_RAG_Hybrid_Reranking_Vector_Indexing.md
- relation: references
  target: 01_KNOWLEDGE/BOOKS/SPECIALIZED_RAG_GraphRAG_and_Community_Summaries.md
- relation: references
  target: 01_KNOWLEDGE/BOOKS/Caiet_Teme_Aplicatii_Practice_Carti.md
---

# RAG M?iestrie: Triada RAG, Diagnosticarea Halucina?iilor & Entropie Semantic?

**Surs?**: Chip Huyen, *Designing Large Language Model Applications* (Capitolul 6: Evaluare & RAG) & TruLens / Ragas  
**Domeniu**: Evaluare RAG Automat?, Auditul Calit??ii R?spunsurilor & Detec?ia Halucina?iilor

---

## 1. Triada RAG (The RAG Triad Architecture)

Pentru a diagnostica cu precizie chirurgical? degradarea calit??ii ?ntr-o conduct? RAG, evaluarea nu se face global, ci separat pe cele trei laturi ale triunghiului Query-Context-Response:

```text
         [ Interogare (Query) ]
               /        \
              /          \
Context      /            \  Answer
Relevance   /              \  Relevance
           /                \
          v                  v
    [ Context ] --------> [ R?spuns (Response) ]
              Groundedness / Faithfulness
```

### 1. Relevan?a Contextului (*Context Relevance*)
- **?ntrebare**: Con?ine contextul extras exclusiv informa?ii necesare rezolv?rii interog?rii, f?r? zgomot inutil?
- **Formul? / Metric?**: Raportul dintre propozi?iile din context care sunt direct utile pentru r?spuns ?i num?rul total de propozi?ii extrase.
- **E?ec specific**: *Context Stuffing* ? injectarea a zeci de fragmente irelevante care dilueaz? aten?ia modelului.

### 2. ?mp?m?ntarea / Fidelitatea (*Groundedness / Faithfulness*)
- **?ntrebare**: Fiecare afirma?ie din r?spunsul generat poate fi dedus? logic strict din contextul extras?
- **Formul? / Metric?**: $\frac{\text{Num?r afirma?ii verificate ?n context}}{\text{Num?r total afirma?ii ?n r?spuns}}$.
- **E?ec specific**: *Hallucination / Confabulation* ? modelul inventeaz? detalii nefondate ?n sursele furnizate.

### 3. Relevan?a R?spunsului (*Answer Relevance*)
- **?ntrebare**: R?spunsul abordeaz? direct problema formulat? de utilizator, f?r? divaga?ii sau refuzuri nejustificate?
- **Formul? / Metric?**: Similaritatea semantic? ?ntre ?ntrebarea original? ?i o ?ntrebare invers generat? din r?spuns (*reverse query generation*).
- **E?ec specific**: *Evasive / Circular Response* ? modelul genereaz? un text factologic corect din context, dar ocole?te cerin?a utilizatorului.

---

## 2. Diagnosticarea Halucina?iilor prin Entropie Semantic? (Farquhar et al., Nature 2024)

Modelele lingvistice pot produce formul?ri superficial diferite pentru aceea?i idee semantic?. Verificarea certitudinii se bazeaz? pe cuantificarea entropiei la nivel de sens:
1. **E?antionare Stocastic?**: Se genereaz? $K$ r?spunsuri independente la aceea?i interogare folosind o temperatur? moderat? ($T \approx 0.7$).
2. **Clustering Semantic Bi-Direc?ional**: R?spunsurile $y_i$ ?i $y_j$ sunt plasate ?n aceea?i clas? de echivalen?? $C_k$ dac? se implic? reciproc logic ($y_i \models y_j$ ?i $y_j \models y_i$) verificat printr-un model NLI (*Natural Language Inference*).
3. **Calculul Entropiei Semantice**:
   $$H_{\text{semantic}} = - \sum_{C_k} P(C_k) \log P(C_k)$$
- **Interpretare**: Dac? $H_{\text{semantic}} \approx 0$, toate formul?rile converg c?tre aceea?i semnifica?ie (?ncredere ridicat?). Dac? $H_{\text{semantic}}$ este ridicat, modelul confabuleaz? idei divergente (halucina?ie confirmat?).

---

## 3. Izolarea Prompturilor ?i Invariantul Kleppmann-Pai

?n conformitate cu principiul de izolare a instruc?iunilor de date (*Prompt Data/Instruction Isolation*):
- Orice context extras din Vault este strict demarcat ?n etichete XML:
  `<untrusted_memory id="..." lifecycle="..."> ... con?inut ... </untrusted_memory>`
- Meta-instruc?iunile injectate mali?ios ?n interiorul fragmentelor extrase sunt tratate ca date pasive, prevenind atacurile de tip *Indirect Prompt Injection*.

