---
id: 775de90b-dbcb-5951-aa80-26f9a0c92345
type: knowledge
lifecycle: REVIEW
category: architecture/ai_reasoning
tags:
- aima
- russell-norvig
- mcts
- csp
- bayesian-networks
- mdps
- reinforcement-learning
- q-learning
created: '2026-09-04'
updated: '2026-09-04'
provenance:
  source_type: ai
  source_ref: "06_INBOX/RAW_IMPORTS/BOOKS/Russell-Norvig-AIMA-4e-Part3-5"
confidence: high
verification: unverified
relations:
- relation: references
  target: 01_KNOWLEDGE/BOOKS/AIMA_Rational_Agents_and_Search.md
- relation: references
  target: 01_KNOWLEDGE/BOOKS/Caiet_Teme_Aplicatii_Practice_Carti.md
- relation: references
  target: 00_CORE/System_Architecture.md
---

# AIMA Avansat: Raționament Probabilistic, CSP, MCTS & Învățare prin Recompensă

**Sursă**: Stuart Russell & Peter Norvig, *Artificial Intelligence: A Modern Approach* (4th Ed., Capitolele 5, 6, 13–17, 21)  
**Domeniu**: Modele Cognitive, Luare de Decizii Sub Incertitudine & Planificare

---

## 1. Căutare în Arbori Monte Carlo (MCTS & UCB1) (Ch 5)

MCTS balansează explorarea nodurilor puțin vizitate cu exploatarea celor cu recompensă mare, folosind formula UCB1 (Upper Confidence Bound):

$$UCB1(s, a) = Q(s, a) + c \sqrt{\frac{\ln N(s)}{N(s, a)}}$$

- $Q(s, a)$: Valoarea medie estimată a recompensei pentru acțiunea $a$ din starea $s$.
- $N(s)$: Numărul total de vizite în starea $s$.
- $N(s, a)$: Numărul de vizite specifice pe tranziția $(s, a)$.
- $c$: Constanta de explorare (teoretic $\sqrt{2} \approx 1.414$).

### Cele 4 Etape ale MCTS
1. **Selecție (Selection)**: Coborâre recursivă din rădăcină conform UCB1 până la un nod neexpandat complet.
2. **Expansiune (Expansion)**: Crearea unui nou nod copil corespunzător unei acțiuni posibile.
3. **Simulare / Rollout**: Evaluare euristică sau parcurs aleator până la o stare terminală / adâncime maximă.
4. **Retro-propagare (Backpropagation)**: Propagarea recompensei obținute în sus pe întreg lanțul de părinți, actualizând $N$ și $Q$.

---

## 2. Probleme de Satisfacere a Constrângerilor (CSP & AC-3) (Ch 6)

Un CSP este definit prin $(X, D, C)$: variabile $X$, domenii $D$ și constrângeri $C$.
- **Consistență pe Arcuri (Arc Consistency — AC-3)**: O variabilă $X_i$ este consistentă pe arc cu $X_j$ dacă pentru fiecare valoare $x \in D_i$ există o valoare $y \in D_j$ care satisface constrângerea binară între $X_i$ și $X_j$.
- **Complexitate AC-3**: Rulează în timp $\mathcal{O}(c d^3)$, unde $c$ este numărul de constrângeri binare și $d$ este mărimea maximă a domeniului.
- **Euristica MRV (Minimum Remaining Values)**: Alegerea variabilei cu cele mai puține opțiuni valide rămase (fail-first).

---

## 3. Procese Decizionale Markov (MDPs) & Învățare prin Recompensă (Ch 17, 21)

### Ecuația de Optimalitate Bellman
Pentru un spațiu de stări $S$, acțiuni $A$, probabilități de tranziție $P(s' \mid s, a)$ și recompense $R(s, a, s')$:

$$V^*(s) = \max_{a \in A} \sum_{s'} P(s' \mid s, a) \left[ R(s, a, s') + \gamma V^*(s') \right]$$

- $\gamma \in [0, 1)$: Factorul de discount temporal.
- **Iterarea Valorilor (Value Iteration)**: Converge geometric la soluția unică a ecuației Bellman datorită proprietății de contracție a operatorului Bellman ($\|B u - B v\|_\infty \le \gamma \|u - v\|_\infty$).

### Algoritmul Q-Learning Fără Model (Model-Free TD Control)
$$Q(s, a) \leftarrow Q(s, a) + \alpha \left[ R(s, a, s') + \gamma \max_{a'} Q(s', a') - Q(s, a) \right]$$

- $\alpha$: Rata de învățare ($0 < \alpha \le 1$).
- Termenul din paranteză dreaptă reprezintă **TD Error** (eroarea de diferență temporală).

---

## 4. Playbook Operațional: Ce fac când primesc o sarcină de planificare sau bugetare?

1. **Pentru bugetarea contextului (Context Budgeting)**: Tratez alocarea de jetoane per agent ca pe o problemă CSP și aplic propagarea AC-3 pentru a asigura că suma bugetelor agenților nu depășește limita totală (`MAX_SYNTHESIS_INPUT = 2500 tokens`).
2. **Pentru selecția instrumentelor de către agenți autonomi**: Folosesc UCB1 pentru a prioritiza uneltele cu rată istorică mare de succes, menținând un factor mic de explorare ($c=0.5$) pentru instrumente nou adăugate.
3. **Pentru evaluarea performanței cognitive**: Calculez discountul temporal $\gamma$ pe traseele de execuție pentru a recompensa agenții care rezolvă sarcinile în cât mai puțini pași.
