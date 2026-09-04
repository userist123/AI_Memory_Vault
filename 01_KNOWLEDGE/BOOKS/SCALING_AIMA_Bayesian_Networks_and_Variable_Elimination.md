---
id: 662e960c-1507-5065-bbb2-1a677a35f109
type: knowledge
lifecycle: REVIEW
category: ai/bayesian_networks_probabilistic_inference
tags:
- aima
- russell-norvig
- bayesian-network
- variable-elimination
- conditional-independence
- d-separation
- probabilistic-inference
created: '2026-09-04'
updated: '2026-09-04'
provenance:
  source_type: ai
  source_ref: 06_INBOX/RAW_IMPORTS/BOOKS/Russell-Norvig-AIMA4e-Ch13-Ch14
confidence: high
verification: unverified
relations:
- relation: references
  target: 01_KNOWLEDGE/BOOKS/MASTERY_AIMA_HMM_Kalman_and_Particle_Filtering.md
- relation: references
  target: 01_KNOWLEDGE/BOOKS/PRODUCTION_AIMA_Constraint_Satisfaction_and_Arc_Consistency.md
- relation: references
  target: 01_KNOWLEDGE/BOOKS/Caiet_Teme_Aplicatii_Practice_Carti.md
---

# AIMA Scaling: Rețele Bayesiene și Inferență Probabilistică

**Sursă**: Stuart Russell & Peter Norvig, *Artificial Intelligence: A Modern Approach* 4th ed. (Capitolele 13-14: Raționament Probabilistic)
**Domeniu**: Modele Grafice Probabilistice, Independență Condițională, Eliminare de Variabile

---

## 1. Rețele Bayesiene — Structură

### 1.1 Definiție Formală

O rețea bayesiană este un graf aciclic direcționat (DAG) unde:
- **Nodurile** = variabile aleatoare
- **Arcele** = dependențe directe (cauză → efect)
- **Tabelele CPT** = $P(X_i \mid \text{Parents}(X_i))$ pentru fiecare nod

### 1.2 Distribuția Comună (Chain Rule)

$$P(X_1, X_2, \ldots, X_n) = \prod_{i=1}^{n} P(X_i \mid \text{Parents}(X_i))$$

Aceasta **factorizează** distribuția comună în produsul tabelelor CPT locale — reprezentare compactă exponențială.

### 1.3 Exemplu: Rețeaua Alarmă

```
     Burglar    Earthquake
        \       /
         Alarm
        /     \
     John     Mary
     calls    calls
```

| Variabilă | Parents | CPT |
| :--- | :--- | :--- |
| Burglar | ∅ | P(B) = 0.001 |
| Earthquake | ∅ | P(E) = 0.002 |
| Alarm | {B, E} | P(A\|B,E)=0.95, P(A\|B,¬E)=0.94, P(A\|¬B,E)=0.29, P(A\|¬B,¬E)=0.001 |
| JohnCalls | {A} | P(J\|A)=0.90, P(J\|¬A)=0.05 |
| MaryCalls | {A} | P(M\|A)=0.70, P(M\|¬A)=0.01 |

---

## 2. Independență Condițională și d-Separare

### 2.1 Proprietatea Markov

Fiecare nod este **condițional independent** de non-descendenții săi, date fiind valorile părinților:

$$X_i \perp \text{NonDescendants}(X_i) \mid \text{Parents}(X_i)$$

### 2.2 d-Separare (Criteriu Grafic)

Două variabile $X$ și $Y$ sunt d-separate de un set de evidență $E$ dacă **fiecare cale** între ele este blocată. O cale este blocată dacă conține:

| Structură | Condiție de Blocare |
| :--- | :--- |
| **Chain**: $A \to B \to C$ | $B \in E$ (observat) |
| **Fork**: $A \leftarrow B \to C$ | $B \in E$ (observat) |
| **Collider**: $A \to B \leftarrow C$ | $B \notin E$ și niciun descendent al lui $B$ în $E$ |

**Coliderul** este contra-intuitiv: observarea lui $B$ **creează** dependență între $A$ și $C$ (explaining away).

---

## 3. Inferență Exactă: Eliminare de Variabile

### 3.1 Problema

Dat o rețea bayesiană și evidență $\mathbf{e}$, calculează:

$$P(X \mid \mathbf{e}) = \frac{P(X, \mathbf{e})}{P(\mathbf{e})} = \alpha \cdot P(X, \mathbf{e})$$

### 3.2 Algoritmul

```
function VARIABLE-ELIMINATION(X, e, bn):
    factors ← lista tuturor CPT-urilor din bn
    for each variable Yᵢ (hidden, in ordine de eliminare):
        factors_with_Yi ← factori care conțin Yᵢ
        new_factor ← Σ_{Yi} ∏ factors_with_Yi    // marginalizare
        factors ← (factors - factors_with_Yi) ∪ {new_factor}
    result ← ∏ factors rămași (conțin doar X și e)
    return NORMALIZE(result)
```

### 3.3 Complexitate

- **Depinde de ordinea de eliminare** — alegerea optimă este NP-hard
- **Tree-width** $w$ → complexitate $O(n \cdot d^{w+1})$ unde $d$ = dimensiunea maximă a domeniului
- Pentru **arbori**: $O(n \cdot d^2)$ (exact ca message passing)

### 3.4 Euristici pentru Ordinea de Eliminare

| Euristică | Strategie |
| :--- | :--- |
| **Min-Degree** | Elimină variabila cu cele mai puține vecini în graful de interacțiune |
| **Min-Fill** | Elimină variabila care adaugă cele mai puține muchii noi |
| **Min-Weight** | Minimizează produsul dimensiunilor domeniilor factorilor creați |

---

## 4. Inferență Aproximativă

### 4.1 Când Inferența Exactă Este Intractabilă

- Rețele cu **loops** dense → tree-width mare
- Variabile continue → integrale analitice imposibile
- Rețele cu mii de noduri

### 4.2 Sampling Methods (Rezumat)

| Metodă | Principiu | Complexitate |
| :--- | :--- | :--- |
| **Prior Sampling** | Generează din distribuția prioră | Simplu dar ineficient cu evidență rară |
| **Rejection Sampling** | Respinge eșantioane inconsistente cu evidența | Ineficient dacă evidența e rară |
| **Likelihood Weighting** | Fixează evidența, ponderează eșantioanele | Mai eficient, dar varianță mare |
| **Gibbs Sampling** (MCMC) | Eșantionează fiecare variabilă condițional | Converge, dar lent pe rețele mari |

---

## 5. Aplicabilitate în Memory Vault

- **Confidence Propagation**: O rețea bayesiană poate modela cum confidența unei note afectează notele dependente
- **Anomaly Detection**: P(nota_suspectă | context) ca scor de anomalie
- **Explaining Away**: Dacă o notă are două surse posibile, confirmarea uneia reduce probabilitatea celeilalte

---

## Referințe Obsidian

- [[MASTERY_AIMA_HMM_Kalman_and_Particle_Filtering]]
- [[PRODUCTION_AIMA_Constraint_Satisfaction_and_Arc_Consistency]]
- [[HARDENING_AIMA_Safety_Alignment_and_Reward_Hacking]]
- [[Caiet_Teme_Aplicatii_Practice_Carti]]
