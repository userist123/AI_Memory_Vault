---
id: f7c213e1-35c2-5436-a828-4067a3d69f8d
type: knowledge
lifecycle: REVIEW
category: ai/probabilistic_temporal_reasoning
tags:
- aima
- russell-norvig
- hidden-markov-models
- kalman-filter
- particle-filter
- state-estimation
- temporal-reasoning
- monte-carlo
created: '2026-09-04'
updated: '2026-09-04'
provenance:
  source_type: ai
  source_ref: 06_INBOX/RAW_IMPORTS/BOOKS/Russell-Norvig-AIMA-Ch14
confidence: high
verification: unverified
relations:
- relation: references
  target: 01_KNOWLEDGE/BOOKS/AIMA_Rational_Agents_and_Search.md
- relation: references
  target: 01_KNOWLEDGE/BOOKS/ADVANCED_AIMA_Probabilistic_Reasoning_Planning_RL.md
- relation: references
  target: 01_KNOWLEDGE/BOOKS/SPECIALIZED_AIMA_Automated_Planning_and_HTN.md
- relation: references
  target: 01_KNOWLEDGE/BOOKS/Caiet_Teme_Aplicatii_Practice_Carti.md
---

# AIMA M?iestrie: Ra?ionament Probabilistic Temporal, HMM, Filtre Kalman & Particle Filtering

**Surs?**: Stuart Russell & Peter Norvig, *Artificial Intelligence: A Modern Approach* (4th ed., Capitolul 14: Ra?ionament Probabilistic ?n Timp)  
**Domeniu**: Estimare de Stare Temporal?, Modele Senzoriale Zgomotoase & Inferen?? Secven?ial?

---

## 1. Modele Temporale Discrete: Ipoteza Markov & Modele Senzoriale

Un agent ra?ional care opereaz? ?ntr-un mediu par?ial observabil trebuie s? men?in? o distribu?ie de probabilitate asupra st?rii lumii (stare de credin?? / *belief state*):
- **Ipoteza Markov de Ordinul 1**: Starea curent? depinde exclusiv de starea imediat precedent?:
  $$\mathbf{P}(X_t \mid X_{0:t-1}) = \mathbf{P}(X_t \mid X_{t-1})$$
- **Ipoteza Observa?iilor Senzoriale**: M?sur?toarea curent? depinde doar de starea curent?:
  $$\mathbf{P}(E_t \mid X_{0:t}, E_{1:t-1}) = \mathbf{P}(E_t \mid X_t)$$

Sarcinile fundamentale de inferen?? temporal?:
1. **Filtrare (*Filtering / Monitoring*)**: $\mathbf{P}(X_t \mid e_{1:t})$ ? calculul st?rii curente date fiind toate observa?iile trecute.
2. **Predic?ie (*Prediction*)**: $\mathbf{P}(X_{t+k} \mid e_{1:t})$ pentru $k > 0$.
3. **Netezire (*Smoothing*)**: $\mathbf{P}(X_k \mid e_{1:t})$ pentru $0 \le k < t$ ? reevaluarea st?rilor trecute ?n lumina dovezilor ulterioare (algoritmul Forward-Backward).
4. **Cea mai probabil? secven?? (*Most Likely Explanation*)**: $\operatorname{argmax}_{x_{1:t}} P(x_{1:t} \mid e_{1:t})$ rezolvat? prin **Algoritmul Viterbi** (programare dinamic?).

---

## 2. Algoritmul Viterbi pentru Modele Markov Ascunse (HMM)

Pentru variabile discrete cu st?ri ascunse $S = \{s_1, \dots, s_N\}$:
- **Trecerea ?nainte**: La fiecare moment $t$, se calculeaz? probabilitatea maxim? $\mu_t(j)$ a celei mai probabile traiectorii care ajunge ?n starea $s_j$:
  $$\mu_t(j) = P(e_t \mid s_j) \cdot \max_{i} \left( \mu_{t-1}(i) \cdot P(s_j \mid s_i) \right)$$
- **Indicatori de ?ntoarcere (*Backpointers*)**: Se re?ine indicele st?rii precedente $i^*$ care a maximizat tranzi?ia.
- **Urm?rire invers? (*Backtracking*)**: La pasul $T$, se alege starea cu $\mu_T$ maxim ?i se reconstruie?te secven?a global? optim? ?n timp liniar $\mathcal{O}(T \cdot N^2)$.

---

## 3. Filtrarea Kalman pentru Sisteme Continue Liniare

C?nd st?rile ?i senzorii sunt variabile continue guvernate de dinamici liniare cu zgomot alb Gaussian:
- **Ecua?ia de Tranzi?ie**: $x_t = F_t x_{t-1} + w_t$, unde $w_t \sim \mathcal{N}(0, Q_t)$
- **Ecua?ia de Observa?ie**: $y_t = H_t x_t + v_t$, unde $v_t \sim \mathcal{N}(0, R_t)$

Ciclul Kalman const? ?n doi pa?i alternativi:
1. **Predic?ie (*Time Update*)**:
   $$\hat{x}_{t|t-1} = F_t \hat{x}_{t-1|t-1}, \quad P_{t|t-1} = F_t P_{t-1|t-1} F_t^T + Q_t$$
2. **Actualizare prin M?sur?toare (*Measurement Update*)**:
   - Inova?ie: $\tilde{y}_t = y_t - H_t \hat{x}_{t|t-1}$
   - C??tigul Kalman (*Kalman Gain*): $K_t = P_{t|t-1} H_t^T (H_t P_{t|t-1} H_t^T + R_t)^{-1}$
   - Stare corectat?: $\hat{x}_{t|t} = \hat{x}_{t|t-1} + K_t \tilde{y}_t$
   - Covarian?? corectat?: $P_{t|t} = (I - K_t H_t) P_{t|t-1}$

---

## 4. Filtre cu Particule (Particle Filtering / Sequential Monte Carlo)

Pentru sisteme neliniare ?i distribu?ii non-Gaussiene (ex: zgomot multimodal):
1. **Reprezentare prin e?antioane**: Starea de credin?? este aproximat? printr-un nor de $M$ particule ponderate $\{ (x_t^{(i)}, w_t^{(i)}) \}_{i=1}^M$.
2. **Evolu?ie (*Propagate*)**: Fiecare particul? e e?antionat? din modelul de tranzi?ie $x_t^{(i)} \sim P(X_t \mid x_{t-1}^{(i)})$.
3. **Ponderare (*Weight*)**: Ponderea fiec?rei particule este actualizat? propor?ional cu verosimilitatea observa?iei: $w_t^{(i)} = P(e_t \mid x_t^{(i)})$.
4. **Re-e?antionare (*Resample*)**: Se extrag $M$ particule noi cu probabilit??i propor?ionale cu $w_t^{(i)}$, elimin?nd particulele cu pondere mic? ?i concentr?nd resursele de calcul ?n zonele de mare probabilitate.

