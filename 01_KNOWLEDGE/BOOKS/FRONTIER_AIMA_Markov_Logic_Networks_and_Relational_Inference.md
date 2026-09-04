---
id: 04f98e18-9e4a-5995-8475-d78a59bbde01
type: knowledge
lifecycle: REVIEW
category: ai/markov_logic_networks_relational_reasoning
tags:
- aima
- russell-norvig
- markov-logic
- relational-reasoning
- first-order-logic
- probabilistic-logic
- mcmc-inference
created: '2026-09-04'
updated: '2026-09-04'
provenance:
  source_type: ai
  source_ref: 06_INBOX/RAW_IMPORTS/BOOKS/Russell-Norvig-AIMA4e-Ch14-Ch15
confidence: high
verification: unverified
relations:
- relation: references
  target: 01_KNOWLEDGE/BOOKS/SCALING_AIMA_Bayesian_Networks_and_Variable_Elimination.md
- relation: references
  target: 01_KNOWLEDGE/BOOKS/PRODUCTION_AIMA_Constraint_Satisfaction_and_Arc_Consistency.md
- relation: references
  target: 01_KNOWLEDGE/BOOKS/Caiet_Teme_Aplicatii_Practice_Carti.md
---

# AIMA Frontier: Rețele Markov Logic și Raționament Relațional Probabilistic

**Sursă**: Stuart Russell & Peter Norvig, *Artificial Intelligence: A Modern Approach* 4th ed. (Capitolele 14 și 15) & fundamentele formulate de Richardson & Domingos (2006)  
**Domeniu**: Fuziune Simbolic-Statistică, Logica de Ordinul I Ponderată, Rețele Markov Neorientate

---

## 1. Dihotomia Simbolic vs Probabilistic

În inteligența artificială clasică:
- **Logica de Ordinul I (FOL)**: Expresivă, capabilă să reprezinte relații complexe între obiecte, dar fragilă la zgomot și excepții (o singură contradicție face teoria inconsistentă).
- **Modelele Grafice Probabilistice (PGM)**: Robuste la incertitudine, dar limitate la vectori cu dimensiuni fixe (propoziționale), incapabile să generalizeze relații între mulțimi arbitrare de entități.

**Rețelele Markov Logic (MLN)** unifică ambele lumi: o bază de cunoștințe FOL unde formulele nu sunt constrângeri rigide, ci au asociate **ponderi reale** ($w_i \in \mathbb{R}$).

---

## 2. Formalismul Matematic al MLN

O bază de cunoștințe MLN $L$ este o mulțime de perechi $(F_i, w_i)$, unde $F_i$ este o formulă în Logica de Ordinul I, iar $w_i \in \mathbb{R}$ este ponderea sa.

### 2.1 Instanțierea într-o Rețea Markov (Grounding)
Dată o mulțime de constante $C = \{c_1, c_2, \dots, c_{|C|}\}$, MLN definește o Rețea Markov $M_{L, C}$ prin *grounding*:
1. **Noduri**: Un nod binar pentru fiecare atom instanțiat (ground atom).
2. **Feature-uri / Clici**: O funcție de potențial binară pentru fiecare formulă instanțiată (ground formula). Valoarea este $1$ dacă formula instanțiată este adevărată într-o lume $x$, și $0$ altfel.

### 2.2 Distribuția de Probabilitate peste Lumi Posibile
Probabilitatea unei lumi posibile $x \in \{0, 1\}^n$ este dată de distribuția Gibbs:

$$P(X = x) = \frac{1}{Z} \exp\left( \sum_{i} w_i n_i(x) \right)$$

Unde:
- $n_i(x)$ = numărul de instanțieri adevărate ale formulei $F_i$ în lumea $x$.
- $Z = \sum_{x' \in \mathcal{X}} \exp\left( \sum_i w_i n_i(x') \right)$ = funcția de partiție (normalizare).

```
Dacă w_i → +∞: Formula devine o constrângere strictă de ordinul I (Logică pură).
Dacă w_i = 0: Formula nu exercită nicio influență asupra probabilității.
Dacă w_i < 0: Lumea în care formula este adevărată este penalizată.
```

---

## 3. Inferență în Rețele Markov Logic

### 3.1 Căutarea MAP (Maximum A Posteriori)
Găsirea celei mai probabile lumi posibile devine o problemă de optimizare combinatorie echivalentă cu **Weighted Max-SAT**:

$$\arg\max_x P(X = x) = \arg\max_x \sum_{i} w_i n_i(x)$$

Algoritmi folosiți: **MaxWalkSAT** (căutare stocastică locală cu reporniri).

### 3.2 Inferență Marginală: MC-SAT
Pentru a calcula $P(\text{Query} \mid \text{Evidență})$, samplingul MCMC standard (Gibbs) eșuează când există formule cu ponderi foarte mari (creează bariere de potențial insurmontabile).  
**MC-SAT** combină:
1. *SampleSAT* (un solver satisfiability stocastic).
2. Eșantionarea feliilor (*Slice Sampling*) pentru a traversa spațiul de stări fără a fi captiv în minime locale.

---

## 4. Comparativ: FOL vs Bayesian Networks vs MLN

| Dimensiune | Logică FOL Pură | Rețea Bayesiană | Markov Logic Network (MLN) |
| :--- | :--- | :--- | :--- |
| **Tratarea Excepțiilor** | Inconsistentă | Probabilități locale | Ponderi continue |
| **Expresivitate Relațională** | Universală ($\forall, \exists$) | Nulă (variabile fixe) | Universală ($\forall, \exists$ peste obiecte) |
| **Dependențe Ciclice** | Suportate | Interzise (DAG strict) | Suportate (graf neorientat) |
| **Inferență la Scară** | Rezoluție / Teoreme | Variable Elimination / MCMC | MaxWalkSAT / MC-SAT |

---

## 5. Aplicabilitate în AI Memory Vault

- **Consolidarea Cunoștințelor Probabilistice**: Notele relaționate din `01_KNOWLEDGE/` pot conține reguli soft (e.g., $w=2.5$: $\forall x, y \, \text{References}(x, y) \implies \text{RelatedDomain}(x, y)$).
- **Detecția Contradicțiilor între Note**: Rezolvarea conflictelor între surse cu credibilități diferite prin ponderarea certitudinii sursei.
- **Rationament Relațional peste Graful Obsidian**: Inferență de proprietăți noi peste legăturile `[[...]]` fără a necesita reantrenare de rețele neurale.

---

## Referințe Obsidian

- [[SCALING_AIMA_Bayesian_Networks_and_Variable_Elimination]]
- [[PRODUCTION_AIMA_Constraint_Satisfaction_and_Arc_Consistency]]
- [[MASTERY_AIMA_HMM_Kalman_and_Particle_Filtering]]
- [[Caiet_Teme_Aplicatii_Practice_Carti]]
