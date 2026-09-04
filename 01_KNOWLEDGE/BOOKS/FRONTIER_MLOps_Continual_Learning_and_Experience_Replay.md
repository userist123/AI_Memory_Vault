---
id: 86250002-ec4f-5608-aa92-14556b05d2a1
type: knowledge
lifecycle: REVIEW
category: mlops/continual_learning_experience_replay
tags:
- ml-systems
- huyen
- continual-learning
- experience-replay
- reservoir-sampling
- catastrophic-forgetting
- online-evaluation
- streaming-ml
created: '2026-09-04'
updated: '2026-09-04'
provenance:
  source_type: ai
  source_ref: 06_INBOX/RAW_IMPORTS/BOOKS/Huyen-Designing-ML-Systems-Ch9-Ch11
confidence: high
verification: unverified
relations:
- relation: references
  target: 01_KNOWLEDGE/BOOKS/SCALING_MLOps_Distribution_Shift_and_Monitoring.md
- relation: references
  target: 01_KNOWLEDGE/BOOKS/HARDENING_Transformer_Numerical_Stability_and_Catastrophic_Forgetting.md
- relation: references
  target: 01_KNOWLEDGE/BOOKS/Caiet_Teme_Aplicatii_Practice_Carti.md
---

# MLOps Frontier: Învățare Continuă, Buffere de Replay și Evaluare Online în Streaming

**Sursă**: Chip Huyen, *Designing Machine Learning Systems* (Capitolele 9 și 11) & literatura MLOps privind *Online Continual Learning*  
**Domeniu**: Arhitecturi de Învățare în Timp Real, Combaterea Uitării Catastrofale, Evaluare Progresivă

---

## 1. De ce Antrenamentul Batch Tradițional Este Insuficient

În sistemele clasice de ML, modelele sunt reantrenate periodic (zilnic sau săptămânal) pe seturi masive de date statice:

```
[Date Ziua 1] ──→ [Antrenament Offline (12h)] ──→ [Model v1 Deployed]
                      ... 24h latență ...
[Date Ziua 2] ──→ [Antrenament Offline (12h)] ──→ [Model v2 Deployed]
```

**Dezavantaje majore**:
1. **Latență de Adaptare Ridicată**: Schimbările bruște de comportament al utilizatorilor sau pieței nu sunt reflectate decât după următoarea fereastră de antrenament.
2. **Cost Computațional Masiv**: Rescrierea și reprocesarea întregului istoric de date la fiecare iterație.
3. **Pauze în Funcționare**: Modelele pot produce inferențe degradate pe durata tranziției.

---

## 2. Paradigma Învățării Continue (Continual / Streaming ML)

Modelul își actualizează parametrii incremental la fiecare flux de date noi:

$$\theta_{t+1} = \theta_t - \eta \nabla_\theta \mathcal{L}(f_{\theta_t}(x_t), y_t)$$

Provocarea fundamentală: **Uitarea Catastrofală (*Catastrophic Forgetting*)** — adaptarea la distribuția recentă distruge performanța pe sarcinile sau distribuțiile istorice.

---

## 3. Mecanisme de Protecție a Memoriei Modelului

### 3.1 Experience Replay Buffer și Reservoir Sampling
Pentru a păstra un eșantion reprezentativ din datele trecute fără a cunoaște dimensiunea totală a fluxului $N$:
- **Algoritmul R (Reservoir Sampling)**:
  - Primele $K$ elemente umplu bufferul.
  - Pentru al $i$-lea element ($i > K$), se alege un index aleatoriu $j \in [0, i-1]$.
  - Dacă $j < K$, elementul din buffer de la poziția $j$ este înlocuit cu noul element.
  - *Garanție*: La orice moment, fiecare element văzut în flux are probabilitatea egală de $K/i$ de a se afla în buffer.

### 3.2 Gradient Episodic Memory (GEM)
Când se calculează gradientul pe noul batch de streaming $g$, modelul verifică proiecția sa pe gradientele calculate pe memoria de replay $g_{\text{mem}}$:

$$\langle g, g_{\text{mem}} \rangle \ge 0$$

Dacă produsul scalar este negativ (gradientul actual ar crește pierderea pe datele vechi), $g$ este proiectat ortogonal pe spațiul admisibil prin programare pătratică (QP).

---

## 4. Evaluare Progresivă în Streaming (Prequential Evaluation)

Într-un flux continuu, nu există un set separat fix de test. Se utilizează tehnica **Test-Then-Train**:

```
Pentru fiecare eșantion nou (x_t, y_t):
  1. Pasul 1 (Test / Predict):  ŷ_t = Model_t(x_t)
  2. Pasul 2 (Evaluate Metric): Log Metric(y_t, ŷ_t)
  3. Pasul 3 (Train / Update):  Model_{t+1} = Update(Model_t, x_t, y_t + ReplayBuffer)
  4. Pasul 4 (Buffer Update):   ReservoirSample(x_t, y_t)
```

Această procedură garantează că fiecare măsurătoare de performanță este calculată strict pe date nemaivăzute de model (*unseen test data*), reflectând fidel comportamentul din producție.

---

## 5. Comparativ: Paradigme de Antrenament ML

| Criteriu | Batch Offline | Micro-Batch Online | Streaming Continual + Replay |
| :--- | :--- | :--- | :--- |
| **Frecvență Update** | Zile / Săptămâni | Ore / Minute | Secunde / Milisecunde |
| **Utilizare Resurse** | Vârfuri mari periodice | Nivel mediu constant | Consum redus, continuu |
| **Risc de Degenerare** | Scăzut | Mediu | Ridicat (necesită guardrails stricte) |
| **Adaptabilitate la Drift** | Extrem de Lentă | Medie | Instantanee |

---

## 6. Aplicabilitate în AI Memory Vault

- **Consolidarea Interactivă a Preferințelor Utilizatorului**: Modelele de ranking și reranking își ajustează ponderile pe baza feedback-ului imediat din conversație.
- **Replay Buffer de Lecții Critice**: Păstrarea automată în memoria de lungă durată a interacțiunilor clasificate cu erori pentru a preveni repetarea acelorași greșeli de planificare.
- **Prequential Monitoring pe Calitatea Memoriei**: Măsurarea continuă a acurateței regăsirii fără a necesita seturi de date etichetate manual.

---

## Referințe Obsidian

- [[SCALING_MLOps_Distribution_Shift_and_Monitoring]]
- [[HARDENING_Transformer_Numerical_Stability_and_Catastrophic_Forgetting]]
- [[PRODUCTION_MLOps_AB_Testing_and_Statistical_Significance]]
- [[Caiet_Teme_Aplicatii_Practice_Carti]]
