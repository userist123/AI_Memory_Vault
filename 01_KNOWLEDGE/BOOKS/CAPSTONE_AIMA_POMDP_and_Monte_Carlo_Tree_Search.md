---
id: 5b9d359f-0a6c-4bb9-bcb2-ba497b835d1f
type: knowledge
lifecycle: REVIEW
category: ai/pomdp_mcts_planning
tags:
- aima
- russell-norvig
- pomdp
- mcts
- belief-state
- ucb1
- reinforcement-learning
- capstone
created: '2026-09-04'
updated: '2026-09-04'
provenance:
  source_type: ai
  source_ref: 06_INBOX/RAW_IMPORTS/BOOKS/Russell-Norvig-AIMA-Ch16-17
confidence: high
verification: unverified
relations:
- relation: references
  target: 01_KNOWLEDGE/BOOKS/EXPERT_AIMA_Game_Theory_and_Adversarial_Search.md
- relation: references
  target: 01_KNOWLEDGE/BOOKS/Caiet_Teme_Aplicatii_Practice_Carti.md
- relation: references
  target: 00_CORE/GRAPH/07 Knowledge Domains Map.md
---

# AIMA Capstone: Procese Decizionale Par?ial Observabile (POMDP) & C?utare Arboreasc? Monte Carlo (MCTS)

**Surs?**: Stuart Russell & Peter Norvig, *Artificial Intelligence: A Modern Approach* (4th ed., Capitolele 16-17)  
**Domeniu**: Planificare Stochastic?, Estimare de Stare Belief & Luare de Decizii Sub Incertitudine

---

## 1. Formalizarea POMDP
Un POMDP este definit printr-un 7-tuplu formal:
$$\langle \mathcal{S}, \mathcal{A}, \mathcal{T}, \mathcal{R}, \Omega, \mathcal{O}, \gamma \rangle$$
unde $\mathcal{S}$ este spa?iul st?rilor ascunse, $\mathcal{A}$ spa?iul ac?iunilor, $\mathcal{T}(s' \mid s, a)$ modelul de tranzi?ie, $\mathcal{R}(s, a)$ func?ia de recompens?, $\Omega$ spa?iul observa?iilor perceptuale, $\mathcal{O}(o \mid s', a)$ modelul de senzori ?i $\gamma \in [0, 1)$ factorul de discount.

Deoarece starea $s$ nu este observabil? direct, agentul men?ine o distribu?ie de probabilitate continu? numit? **Belief State** $b(s) = P(S_t = s \mid o_{1:t}, a_{1:t-1})$. Actualizarea st?rii de credin?? dup? executarea ac?iunii $a$ ?i percep?ia observa?iei $o$ urmeaz? regula Bayes recursiv?:
$$b'(s') = \eta \, \mathcal{O}(o \mid s', a) \sum_{s \in \mathcal{S}} \mathcal{T}(s' \mid s, a) \, b(s)$$
unde $\eta = 1 / P(o \mid b, a)$ este factorul de normalizare.

## 2. Algoritmul MCTS cu Selec?ie UCB1
Pentru planificare ?n spa?ii mari f?r? cunoa?terea complet? a matricilor de tranzi?ie, MCTS parcurge 4 faze:
1. **Selection**: Cobor?re ?n arbore de la r?d?cin? folosind selec?ia UCB1:
   $$\text{UCB1}(i) = \frac{Q(i)}{N(i)} + c \sqrt{\frac{2 \ln N_p}{N(i)}}$$
   unde $Q(i)$ este recompensa cumulat?, $N(i)$ num?rul de vizite ale nodului $i$, $N_p$ vizitele p?rintelui, iar $c = \sqrt{2}$ balanseaz? exploatarea ?i explorarea.
2. **Expansion**: Ad?ugarea unuia sau mai multor noduri copil dac? starea nu este terminal?.
3. **Simulation (Rollout)**: Rulare rapid? stochastic? conform unei politici euristice simple p?n? la o stare final? $s_{\text{term}}$.
4. **Backpropagation**: Propagarea valorii ob?inute $R$ ?napoi de-a lungul ramurii alese, actualiz?nd $N(i) \leftarrow N(i) + 1$ ?i $Q(i) \leftarrow Q(i) + R$.

## 3. Leg?turi Canonice & Graf de Cuno?tin?e
- [[AIMA_Rational_Agents_and_Search]]
- [[ADVANCED_AIMA_Probabilistic_Reasoning_Planning_RL]]
- [[EXPERT_AIMA_Game_Theory_and_Adversarial_Search]]
- [[Caiet_Teme_Aplicatii_Practice_Carti]]
- [[07 Knowledge Domains Map]]
