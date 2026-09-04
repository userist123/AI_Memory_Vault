---
id: b93e7460-f156-592f-bb81-528688ddc467
type: knowledge
lifecycle: REVIEW
category: ai/adversarial_search_game_theory
tags:
- aima
- russell-norvig
- game-theory
- minimax
- alpha-beta-pruning
- nash-equilibrium
- transposition-table
- adversarial-search
created: '2026-09-04'
updated: '2026-09-04'
provenance:
  source_type: ai
  source_ref: 06_INBOX/RAW_IMPORTS/BOOKS/Russell-Norvig-AIMA-Ch5-17
confidence: high
verification: unverified
relations:
- relation: references
  target: 01_KNOWLEDGE/BOOKS/AIMA_Rational_Agents_and_Search.md
- relation: references
  target: 01_KNOWLEDGE/BOOKS/MASTERY_AIMA_HMM_Kalman_and_Particle_Filtering.md
- relation: references
  target: 01_KNOWLEDGE/BOOKS/Caiet_Teme_Aplicatii_Practice_Carti.md
---

# AIMA Expert: Teoria Jocurilor, C?utare Adversarial? & T?iere Alfa-Beta

**Surs?**: Stuart Russell & Peter Norvig, *Artificial Intelligence: A Modern Approach* (4th ed., Capitolele 5 & 17)  
**Domeniu**: Sisteme Multi-Agent Competitive, Decizii Strategice & Echilibrul Nash

---

## 1. Decizii Optime ?n Jocuri Deterministe cu Informa?ie Perfect?

C?nd un agent opereaz? ?ntr-un mediu ?n care un alt agent urm?re?te un obiectiv antagonist (joc cu sum? nul? / *zero-sum game*):
- **Valoarea Minimax**: Valoarea st?rii $s$ pentru juc?torul MAX, asum?nd c? MIN joac? optim:
  $$\text{Minimax}(s) = \begin{cases} 
  \text{Utility}(s) & \text{dac? } \text{Terminal}(s) \\
  \max_{a \in \text{Actions}(s)} \text{Minimax}(\text{Result}(s, a)) & \text{dac? } \text{Player}(s) = \text{MAX} \\
  \min_{a \in \text{Actions}(s)} \text{Minimax}(\text{Result}(s, a)) & \text{dac? } \text{Player}(s) = \text{MIN}
  \end{cases}$$

---

## 2. Optimizarea prin T?iere Alfa-Beta (Alpha-Beta Pruning)

Algoritmul Minimax exhaustiv are complexitate temporal? $\mathcal{O}(b^m)$, unde $b$ este factorul de ramificare ?i $m$ este ad?ncimea maxim?.
T?ierea Alfa-Beta elimin? ramurile care sunt garantat inferioare unei op?iuni deja descoperite:

- **Parametrul $\alpha$**: Cea mai bun? valoare g?sit? p?n? acum pe calea c?tre r?d?cin? pentru **MAX** (limita inferioar? a scorului).
- **Parametrul $\beta$**: Cea mai bun? valoare g?sit? p?n? acum pe calea c?tre r?d?cin? pentru **MIN** (limita superioar? a scorului).
- **Condi?ia de t?iere (*Cutoff*)**:
  $$\text{Dac? } \alpha \ge \beta \implies \text{se abandoneaz? explorarea subarborelui curent!}$$

### Eficien?a T?ierii:
- ?n cel mai bun caz (cu ordonare optim? a mut?rilor), complexitatea scade la $\mathcal{O}(b^{m/2}) = \mathcal{O}((\sqrt{b})^m)$.
- Permite c?utarea la o ad?ncime de dou? ori mai mare cu acela?i buget de calcul.
- **Tabele de Transpozi?ie (*Transposition Tables*)**: Memorarea st?rilor vizitate prin hash Zobrist pentru a preveni re-evaluarea acelora?i configura?ii atinse pe c?i diferite.

---

## 3. Teoria Jocurilor & Echilibrul Nash ?n Sisteme Multi-Agent (Ch 17)

?n contexte generale non-zero-sum, agen?ii au utilit??i independente:
- **Echilibrul Nash**: O combina?ie de strategii $(s_1^*, \dots, s_n^*)$ ?n care niciun agent $i$ nu are un stimulent s? devieze unilateral c?tre o alt? strategie $s_i$:
  $$u_i(s_i^*, s_{-i}^*) \ge u_i(s_i, s_{-i}^*) \quad \forall s_i \in S_i$$
- **Strategii Mixte**: Dac? nu exist? un echilibru ?n strategii pure (ex: Piatr?-Foarfec?-H?rtie), teorema lui John Nash garanteaz? existen?a a cel pu?in unui echilibru ?n distribu?ii de probabilitate peste ac?iuni.

