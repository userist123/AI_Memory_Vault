---
id: 938a5aee-7642-4c10-a243-2dd1d1e73927
type: knowledge
lifecycle: REVIEW
category: rag/context_poisoning_adversarial_defense
tags:
- rag
- pai
- context-poisoning
- adversarial-retrieval
- embedding-collision
- neural-reranking
- hardening
created: '2026-09-04'
updated: '2026-09-04'
provenance:
  source_type: ai
  source_ref: 06_INBOX/RAW_IMPORTS/BOOKS/Suhas-Pai-LLM-Apps-Ch9
confidence: high
verification: unverified
relations:
- relation: references
  target: 01_KNOWLEDGE/BOOKS/CAPSTONE_RAG_DPO_Alignment_and_Contrastive_Reasoning.md
- relation: references
  target: 01_KNOWLEDGE/BOOKS/Caiet_Teme_Aplicatii_Practice_Carti.md
- relation: references
  target: 00_CORE/GRAPH/07 Knowledge Domains Map.md
---

# RAG Hardening: Otr?virea Contextului, Coliziuni Vectoriale & Reg?sire Adversarial?

**Surs?**: Suhas Pai, *Designing Large Language Model Applications* (Capitolul 9: Atacuri ?i Rezilien?? RAG)  
**Domeniu**: Securitatea Sistemelor RAG, Baze de Date Vectoriale & Falsificare Semiotic?

---

## 1. Atacul de Otr?vire a Contextului (Corpus Poisoning)
?ntr-un sistem RAG de scar? mare, un atacator poate injecta documente sintetice construite matematic pentru a avea o similitudine cosinus extrem de mare cu ?ntreb?rile viitoare ale utilizatorilor:
- **Embedding Collision Attack**: Prin optimizare adversarial? pe reprezentarea vectorial? $\vec{v}$, se creeaz? un pasaj scurt $\mathcal{P}_{\text{trojan}}$ care este plasat ?n Top-1 pe aproape orice interogare din domeniul ?int?, de?i con?inutul s?u factual este fals sau con?ine instruc?iuni mali?ioase.
- **Retrieval Denial of Service**: Inundarea indexului cu sute de mii de varia?ii semantice dense, for??nd modelul s? selecteze exclusiv zgomot ?i s? ??i dep??easc? bugetul de context (*context overflow*).

## 2. Mecanisme de Ap?rare ?i Cur??are a Contextului
1. **Neural Cross-Encoder Reranker cu Filtrare de Coeren??**: Spre deosebire de bi-encoderele dense care compar? vectori pre-calcula?i, un cross-encoder calculeaz? aten?ia ?ncruci?at? complet? ?ntre interogare ?i pasaj, penaliz?nd discrepan?ele sintactico-semantice.
2. **K-NN Density & Outlier Anomaly Detection**: Calculul distan?ei locale relative ?ntre pasajul candidat ?i vecinii s?i k-NN. Un document injectat adversarial are de regul? o norm? de gradient neobi?nuit? sau o densitate de vecin?tate artificial comprimat?.
3. **Semn?turi Digitale Criptografice (Provenance Attestation)**: Verificarea la nivel de controller (`I-004`) c? documentele reg?site de?in o semn?tur? de provenien?? valid? ?i un lan? de atestare aprobat ?nainte de a fi livrate ?n promptul final.

## 3. Leg?turi Canonice & Graf de Cuno?tin?e
- [[LLM_Application_Design_and_RAG_Pipelines]]
- [[CAPSTONE_RAG_DPO_Alignment_and_Contrastive_Reasoning]]
- [[Caiet_Teme_Aplicatii_Practice_Carti]]
- [[07 Knowledge Domains Map]]
