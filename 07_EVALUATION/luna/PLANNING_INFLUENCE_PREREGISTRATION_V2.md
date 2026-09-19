# Preînregistrare Formală — Planning Influence V2: Căutare Ghidată de Memorie sub Control Izolat

> **Protocol Științific Preînregistrat (Preregistration V2)**  
> **Data Emiterii**: 2026-09-16  
> **Status**: PRE-REGISTRATION LOCKED — Committed BEFORE running evaluation harness  
> **Calea**: `07_EVALUATION/luna/PLANNING_INFLUENCE_PREREGISTRATION_V2.md`  
> **Continuitate**: Înlocuiește protocolul V1 (`PLANNING_INFLUENCE_MVE_V2_VALIDATED.md`) ca urmare a identificării scurgerii de oracol prin ordonare și departajare (PR #119).

---

## 1. Context și Miza Epistemică

În studiile anterioare de planificare cu memorie externă (`PLANNING_INFLUENCE_MVE_V2_VALIDATED.md`, `..._APPLICABILITY_PILOT_LOCAL_20260904.md`), s-a raportat că brațul de control (baseline) a rezolvat 30/30 de scenarii într-un singur pas (30 noduri, 0 fatale), în timp ce brațul de tratament a consumat 54 de noduri și 12 fatale.

Auditul independent din 2026-09-16 (PR #119) a dezvăluit două erori structurale în harnașament:
1. **Scurgerea de oracol prin ordinea ramurilor**: În `build_scenarios`, `optimal = order[0]` plasa ramura optimă întotdeauna pe prima poziție. În `run_planner`, la priorități uniforme, departajarea alegea `-branches.index(candidate)`, selectând mereu indexul 0. Controlul „fără informație” primea răspunsul optim prin însăși ordinea datelor.
2. **Acuratețea defectuoasă a memoriei din test**: În setul fix de 30 de scenarii, memoria recomanda ramura greșită în 23 din 30 de cazuri (rată de eroare de 77%). Un experiment cu memorie greșită în 77% din cazuri nu măsoară dacă memoria utilă ajută, ci doar măsoară dauna dezinformării.

**Întrebarea Științifică Reconstruită**:  
> *De la ce nivel de acuratețe a recomandării de memorie și sub ce politică de incertitudine reduce influența memoriei costul căutării (noduri) fără a crește vizitele fatale, comparativ cu un control legitim fără acces la răspuns?*

---

## 2. Factori Controlați și Grila Experimentală

### 2.1. Factorul 1: Acuratețea Recomandării Memoriei ($p_{acc}$)
Se evaluează 11 niveluri discrete de acuratețe a memoriei:
$$p_{acc} \in \{0.0, 0.1, 0.2, 0.3, 0.4, 0.5, 0.6, 0.7, 0.8, 0.9, 1.0\}.$$
Pentru fiecare scenariu:
- Cu probabilitatea $p_{acc}$, `memory_recommended = scenario.optimal`.
- Cu probabilitatea $1 - p_{acc}$, `memory_recommended` este selectat pseudo-aleator uniform dintre ramurile non-optime (suboptime sau fatale).

### 2.2. Factorul 2: Calitatea Etichetei de Aplicabilitate ($Q_{app}$)
Se testează două regimuri ale etichetei de aplicabilitate:
1. **Calibrat (`calibrated`)**: Eticheta reflectă corectitudinea recomandării:
   - Când memoria este corectă (`recommended == optimal`):
     - 70% `APPLICABLE` (evidence_strength $\sim U[0.70, 1.00]$);
     - 30% `APPLICABLE_WITH_VERIFICATION` (evidence_strength $\sim U[0.50, 0.80]$).
   - Când memoria este incorectă (`recommended != optimal`):
     - 20% `APPLICABLE_WITH_VERIFICATION` (evidence_strength $\sim U[0.30, 0.60]$);
     - 50% `INSUFFICIENTLY_KNOWN` (evidence_strength $\sim U[0.10, 0.30]$);
     - 30% `NOT_APPLICABLE` (evidence_strength $= 0.00$).
2. **Neinformativ (`uninformative`)**: Eticheta este distribuită uniform aleator (25% pentru fiecare stare), independent de corectitudinea recomandării, simulând o memorie fără capacitate de auto-calibrare a încrederii.

### 2.3. Factorul 3: Dimensionalitatea Mediului (Grila de Ramuri și Fatale)
Pentru a evalua robustețea topologică, se testează 4 configurații de mediu:
- **Config A (Standard V1)**: $K = 4$ ramuri, 1 optimă, 1 suboptimă, 2 fatale ($F = 2$, densitate fatală 50%).
- **Config B (Risc Scăzut)**: $K = 4$ ramuri, 1 optimă, 2 suboptime, 1 fatală ($F = 1$, densitate fatală 25%).
- **Config C (Căutare Largă)**: $K = 6$ ramuri, 1 optimă, 2 suboptime, 3 fatale ($F = 3$, densitate fatală 50%).
- **Config D (Densitate Înaltă)**: $K = 8$ ramuri, 1 optimă, 3 suboptime, 4 fatale ($F = 4$, densitate fatală 50%).

### 2.4. Factorul 4: Brațul de Memorie Contrazisă / Învechită (`stale`)
Se injectează explicit scenarii cu `contradiction_state = "CONFIRMED_CONTRADICTION"` la acuratețe $0.0$ (recomandare fatală contrazisă).
Politica trebuie să neutralizeze complet influența (`influence_strength = 0.0`), generând priorități uniforme identice cu ale bazei.

---

## 3. Politici Comparate

Se compară 4 politici deterministe pe aceleași scenarii:

1. **Arm 1: Baseline Uniform (`baseline`)**:
   - Zero memorie.
   - Prior uniform pe toate ramurile: $P(b) = 1/K, \forall b \in \text{Branches}$.
2. **Arm 2: Control Consultativ (`advisory`)**:
   - Memoria este inclusă în contextul de lucru ca metadată pasivă.
   - Priorul planificatorului rămâne strict uniform $P(b) = 1/K$.
3. **Arm 3: Politica de Incertitudine V1 (`v1_uncertainty`)**:
   - Folosește constantele înghețate din `PLANNING_INFLUENCE_UNCERTAINTY_POLICY_V1.md`:
     - $\text{base\_prior} = 1/K$ (0.25 pentru $K=4$);
     - $\text{influence\_budget} = 0.40$;
     - Ponderi de aplicabilitate:
       $$\text{APPLICABLE} = 1.00, \quad \text{APPLICABLE\_WITH\_VERIFICATION} = 0.35,$$
       $$\text{INSUFFICIENTLY\_KNOWN} = 0.15, \quad \text{NOT\_APPLICABLE} = 0.00.$$
     - Veto de contradicție: dacă $\text{contradiction} == \text{"CONFIRMED\_CONTRADICTION"}$ sau $\text{applicability} == \text{"NOT\_APPLICABLE"}$, priorul este uniform $1/K$.
     - Altfel:
       $$\text{winner\_prior} = \text{base\_prior} + \text{influence\_budget} \times \text{applicability\_strength} \times \text{evidence\_strength},$$
       $$\text{loser\_prior} = \frac{1.0 - \text{winner\_prior}}{K - 1}.$$
4. **Arm 4: Politica de Verificare Activă (`v2_verification_action`)**:
   - Când este emis `APPLICABLE_WITH_VERIFICATION`, verificarea este modelată ca o **acțiune explicită de căutare** ce consumă $C_{ver} = 1.0$ noduri.
   - Verificatorul testează dacă ramura recomandată este fatală sau validă:
     - Dacă ramura este validă: priorul este promovat la nivel `APPLICABLE` ($\text{strength} = 1.00$).
     - Dacă este detectată o contradicție sau ramură fatală: priorul este anulat (reversie la uniform), prevenind explorarea fatală.

---

## 4. Cerințe de Izolare Față de Oracol (Harnașament V3)

Înainte de orice evaluare experimentală, fișierul `planning_influence_mve_v3.py` trebuie să satisfacă următoarele 4 garanții stricte de izolare (validate automat prin suita `20_TESTS/test_planning_influence_isolation.py`):

1. **Echilibrarea Poziției Ramurii Optime**:
   Pentru un set de $N$ scenarii, ramura optimă ocupă fiecare index $0, 1, \dots, K-1$ de exact $N/K$ ori.
2. **Departajare Independentă de Poziție**:
   În `run_planner`, orice egalitate de scor PUCT este rezolvată printr-o funcție deterministă pseudo-aleatoare bazată pe hash-ul `(scenario_id, branch_name, step_idx)`, având corelație zero cu poziția ramurii sau cu `scenario.optimal`.
3. **Baza Nu Știe Răspunsul**:
   Pe un set mare de scenarii, costul mediu de căutare al bazei uniforme este egal cu valoarea așteptată teoretică a căutării aleatoare uniforme fără repetiție:
   $$\mathbb{E}[\text{noduri}] = \frac{K + 1}{2} \quad (\text{pentru } K=4, \mathbb{E} = 2.50 \text{ noduri}),$$
   iar poziția indexului optim nu prezice costul căutării ($r^2 < 0.01, p > 0.10$).
4. **Audit Static de Cod**:
   Zero acces la `scenario.optimal`, `scenario.suboptimal`, `scenario.fatal_a`, `scenario.fatal_b` din planificator sau din compilatorul de memorie. Singura interacțiune permisă este apelul `scenario.oracle(branch)`, contorizat drept consum de nod.
5. **Detectarea Scurgerii Plantate (Negative Control Test)**:
   O variantă modificată a planificatorului căreia i se oferă acces intenționat la `scenario.optimal` trebuie să fie **detectată și respinsă automat** de suita de teste.

---

## 5. Metrici și Analiza Puterii Statistice

### 5.1. Metrici Primare:
1. **Noduri până la Soluție (`node_visits`)**: Numărul total de evaluări (inclusiv nodurile consumate de verificare) până la identificarea ramurii optime.
2. **Vizite Fatale (`fatal_visits`)**: Numărul de ramuri fatale explorate pe parcursul căutării.
3. **Rată de Succes (`success_rate`)**: Proporția scenariilor rezolvate în limita bugetului de căutare (max 16 rollouts).

### 5.2. Metrici Secundare:
1. **Număr de Cereri de Verificare** și costul lor cumulativ.
2. **Influență Fals-Pozitivă**: Proporția cazurilor în care tratamentul a favorizat o ramură greșită.
3. **Prag de Rentabilitate (Accuracy Threshold $p^*$)**: Nivelul minim de acuratețe $p_{acc}$ unde tratamentul depășește statistic baza în noduri fără a crește fatalele.

### 5.3. Dimensiunea Eșantionului și Puterea Statistică:
- **Unitatea Statistică**: Scenariul individual.
- **Dimensiunea Eșantionului**: $N = 200$ scenarii independente per celulă experimentală.
- **Justificare Putere**:
  Cu abaterea standard tipică a căutării pe 4 ramuri ($\sigma \approx 1.12$ noduri), la $N = 200$, eroarea standard este:
  $$\text{SE} = \frac{1.12}{\sqrt{200}} \approx 0.079 \text{ noduri}.$$
  Testul detectează o reducere minimă relevantă de $\Delta \ge 0.25$ noduri ($10\%$ din costul bazei) la o putere statistică:
  $$\text{Power} = 1 - \beta > 99.5\% \quad (\alpha = 0.05).$$
- **Intervale de Încredere**: Toate intervalele de încredere raportate sunt calculate prin Bootstrap neparametric cu 1.000 de replici și sămânță fixată (`seed = 20260916`).

---

## 6. Criteriul de Nulitate și Condiții de Falsificare

Următoarele condiții (preluate formal din `PLANNING_INFLUENCE_UNCERTAINTY_POLICY_V1.md`, Secțiunea 7) determină respingerea politicii:

1. **Rezultat Nul**:
   Dacă la toate nivelurile de acuratețe $p_{acc} \in [0.0, 1.0]$, nicio politică de memorie nu reduce semnificativ numărul de noduri ($\Delta_{\text{nodes}} \le 0$) fără a crește numărul de vizite fatale ($\Delta_{\text{fatals}} > 0$), ipoteza că memoria externă aduce beneficiu computațional în căutare este **complet respinsă**.
2. **Inversiune de Risc (Harm under Low Accuracy)**:
   Dacă la $p_{acc} \le 0.30$, politica V1 crește vizitele fatale cu peste $0.50$ fatale/scenariu față de control, politica este declarată nesigură în regim de incertitudine.
3. **Eșecul Verificării**:
   Dacă costul verificării ($C_{ver}$) depășește economia de noduri obținută prin evitarea erorilor la toate nivelurile de acuratețe, verificarea ca acțiune este considerată ineficientă economic în spații mici de căutare.
