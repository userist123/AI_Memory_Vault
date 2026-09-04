---
id: 867972cd-8423-42af-a387-1eeda84a8d18
type: knowledge
lifecycle: REVIEW
category: deep_learning/numerical_stability_ewc
tags:
- deep-learning
- glassner
- numerical-stability
- catastrophic-forgetting
- ewc
- fisher-information
- gradient-clipping
- hardening
created: '2026-09-04'
updated: '2026-09-04'
provenance:
  source_type: ai
  source_ref: 06_INBOX/RAW_IMPORTS/BOOKS/Arthur-Glassner-Deep-Learning-Ch15
confidence: high
verification: unverified
relations:
- relation: references
  target: 01_KNOWLEDGE/BOOKS/CAPSTONE_Transformer_FlashAttention_Tiling_and_IO_Awareness.md
- relation: references
  target: 01_KNOWLEDGE/BOOKS/Caiet_Teme_Aplicatii_Practice_Carti.md
- relation: references
  target: 00_CORE/GRAPH/07 Knowledge Domains Map.md
---

# Deep Learning Hardening: Stabilitate Numeric?, T?iere de Gradient & Consolidare Elastic? (EWC)

**Surs?**: Arthur Glassner, *Learning Deep Learning* (Capitolul 15) + Kirkpatrick et al. (Overcoming Catastrophic Forgetting)  
**Domeniu**: Optimizare Numeric? Profund?, Precizie Mixt? FP16/BF16 & ?nv??are Secven?ial? F?r? Uitare

---

## 1. Instabilit??i Numerice ?n Antrenarea Re?elelor Ad?nci
?n modelele mari de transformare, apar frecvent dou? patologii de gradient:
- **Gradient Exploding / Underflow**: La antrenare ?n precizie redus? FP16 (interval dinamic restr?ns: $\approx 10^{-5} .. 65504$), produsele succesive din retropropagare cauzeaz? valori `NaN` sau `Inf`. Utilizarea formatului BF16 (care p?streaz? cei 8 bi?i de exponent ai FP32) atenueaz? underflow-ul.
- **Gradient Clipping**: Pentru prevenirea pa?ilor distructivi ?n spa?iul parametrilor c?nd norma gradientului $\|g\|$ explodeaz?, se aplic? scalarea propor?ional?:
  $$g \leftarrow g \times \min\left(1, \frac{\text{threshold}}{\|g\|}\right)$$

## 2. Uitarea Catastrofal? ?i Elastic Weight Consolidation (EWC)
C?nd o re?ea antrenat? pe sarcina $A$ este re-antrenat? direct pe sarcina $B$, parametrii $\theta$ sunt rescri?i complet, ?terg?nd cuno?tin?ele din sarcina $A$ (*Catastrophic Forgetting*).
Algoritmul **Elastic Weight Consolidation (EWC)** introduce o ancor? elastic? pe baza matricei informa?ionale Fisher diagonale $F_i$:
$$\mathcal{L}(\theta) = \mathcal{L}_B(\theta) + \sum_i \frac{\lambda}{2} F_i (\theta_i - \theta_{A, i}^*)^2$$
unde $F_i = \mathbb{E} \left[ \left( \frac{\partial \ln p(y \mid x, \theta_A^*)}{\partial \theta_i} \right)^2 \right]$ m?soar? importan?a fiec?rui parametru pentru sarcina precedent?. Parametrii critici (cu Fisher ridicat) sunt proteja?i de penalizare, for??nd ?nv??area sarcinii $B$ exclusiv ?n subspa?iile libere.

## 3. Leg?turi Canonice & Graf de Cuno?tin?e
- [[Deep_Learning_Representations_and_Attention]]
- [[CAPSTONE_Transformer_FlashAttention_Tiling_and_IO_Awareness]]
- [[Caiet_Teme_Aplicatii_Practice_Carti]]
- [[07 Knowledge Domains Map]]
