---
id: 894b4369-ce94-58c6-8b10-5737fa3a7e16
type: knowledge
lifecycle: REVIEW
category: architecture/deep_learning_optimization
tags:
- deep-learning
- ekman
- adamw
- weight-decay
- cosine-annealing
- gradient-clipping
- mixed-precision
created: '2026-09-04'
updated: '2026-09-04'
provenance:
  source_type: ai
  source_ref: "06_INBOX/RAW_IMPORTS/BOOKS/Magnus-Ekman-Optimization-Dynamics"
confidence: high
verification: unverified
relations:
- relation: references
  target: 01_KNOWLEDGE/BOOKS/Deep_Learning_Representations_and_Attention.md
- relation: references
  target: 01_KNOWLEDGE/BOOKS/ADVANCED_Transformer_RoPE_Attention_Sampling.md
- relation: references
  target: 01_KNOWLEDGE/BOOKS/Caiet_Teme_Aplicatii_Practice_Carti.md
- relation: references
  target: 00_CORE/System_Architecture.md
---

# Deep Learning Specializat: Optimizatorul AdamW, Dinamica Gradienților & Planificarea Ratei de Învățare

**Sursă**: Magnus Ekman, *Learning Deep Learning*  
**Domeniu**: Optimizare Numerică, Stabilitatea Antrenării & Regularizare Decuplată

---

## 1. De ce Adam Standard Eșuează la Regularizare: Decuplarea AdamW (Loshchilov & Hutter)

În optimizatorul clasic SGD, regularizarea $L_2$ și penalizarea ponderilor (*Weight Decay*) sunt matematic echivalente. Cu toate acestea, în optimizatoarele adaptive bazate pe momente (Adam):
- **Eroarea din Adam clasic**: Gradientul regularizării $L_2$ ($\lambda \theta_t$) este adăugat la gradientul pierderii $g_t$ **înainte** de calcularea momentului al doilea $v_t$.
  $$g_t \leftarrow g_t + \lambda \theta_t$$
  Aceasta înseamnă că ponderile cu gradienți istorici mari primesc o penalizare de regularizare mult mai mică decât cele cu gradienți rari (ponderile sunt împărțite la $\sqrt{v_t}$).
- **Soluția AdamW**: Decuplează penalizarea ponderilor de actualizarea adaptivă bazată pe gradient:
  $$\theta_{t+1} \leftarrow \theta_t - \eta_t \lambda \theta_t - \eta_t \frac{m_t}{\sqrt{v_t} + \epsilon}$$
  Astfel, toate ponderile sunt penalizate proporțional cu mărimea lor, restabilind eficiența regularizării.

---

## 2. Dinamica Ratei de Învățare: Warmup & Cosine Annealing

Antrenarea transformatoarelor este instabilă în primii pași datorită varianței uriașe din estimările momentelor inițiale:
1. **Încălzire Liniară (Linear Warmup)**: Rata de învățare $\eta$ crește liniar de la $0$ la $\eta_{\max}$ pe parcursul primilor $T_{\text{warmup}}$ pași (de regulă 1–5% din totalul pașilor).
2. **Scădere Cosinusoidală (Cosine Annealing)**: După warmup, rata de învățare descrește conform funcției cosinus până la o valoare minimă $\eta_{\min}$:
   $$\eta_t = \eta_{\min} + \frac{1}{2}(\eta_{\max} - \eta_{\min})\left( 1 + \cos\left( \frac{t - T_{\text{warmup}}}{T_{\text{max}} - T_{\text{warmup}}} \pi \right) \right)$$
   Aceasta permite modelului să navigheze văile largi ale funcției de pierdere la finalul antrenării, găsind minime plate care generalizează superior.

---

## 3. Limitarea Gradienților prin Norma Globală (Gradient Clipping)

Pentru a preveni explozia gradienților cauzată de secvențe atipice lungi, se calculează norma $L_2$ pe toți parametrii modelului:

$$\|g\|_2 = \sqrt{\sum_{i} \|g_i\|_2^2}$$

Dacă $\|g\|_2 > \text{max\_norm}$, toți gradienții sunt rescalați proporțional:

$$g_i \leftarrow g_i \times \frac{\text{max\_norm}}{\max(\|g\|_2, \text{max\_norm})}$$

---

## 4. Playbook Operațional: Ce fac când antrenez sau ajustez un model în TRL?

1. **Folosesc întotdeauna AdamW**: Nu selectez niciodată Adam clasic când aplic weight decay ($\ge 0.01$).
2. **Configurez Warmup + Cosine**: Setez `warmup_ratio = 0.05` și `lr_scheduler_type = 'cosine'`.
3. **Setez Gradient Clipping**: Activez întotdeauna `max_grad_norm = 1.0` pentru a preveni prăbușirea bruscă a pierderii în timpul antrenării SFT sau DPO.
