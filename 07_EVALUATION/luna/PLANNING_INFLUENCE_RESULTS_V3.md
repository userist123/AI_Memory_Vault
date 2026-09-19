# Raport Științific: Planning Influence V3 — Reconstruit pe un Control Fără Scurgere de Oracol

**Data emiterii:** 2026-09-19  
**Autor:** Antigravity (Sistem Autonom de Cercetare Cognitivă)  
**Referință Preînregistrare:** `07_EVALUATION/luna/PLANNING_INFLUENCE_PREREGISTRATION_V2.md`  
**Addendum Preînregistrare:** `07_EVALUATION/luna/PLANNING_INFLUENCE_PREREGISTRATION_V2_1.md`  
**Stare Reproducere:** `TOATE CELE 5 TABELE REPRODUSE IDENTIC LA OCTET (N=200/celulă)`  

---

## 1. Ce nu se poate sau nu există

Cercetarea empirică riguroasă impune trasarea barierelor fundamentale înainte de a prezenta rezultatele pozitive:

1. **Nu există beneficiu al memoriei la acuratețe scăzută ($p < p^*$):**  
   Memoria cu acuratețe slabă ($p < 0.4$ în mod calibrat, respectiv $p < 0.5$ în mod neinformativ pentru politica `v1_uncertainty`) nu oferă nicio economie de căutare; dimpotrivă, poate induce o penalizare prin devierea priorităților inițiale către acțiuni sub-optime. Nicio politică euristică nu poate extrage informație utilă dintr-un semnal zgomotos sub pragul critic.
2. **Verificarea ca acțiune NU este gratuită:**  
   Fiecare acțiune de verificare costă exact $1.0$ nod de căutare efectivă. În spații de decizie mici ($K=4$), costul fix de $1.0$ nod depășește economia potențială de căutare, conducând la o economie netă negativă a verificării ($-0.235$ până la $-0.305$ noduri) față de atenuarea pasivă prin incertitudine (`v1_uncertainty`). Verificarea ca acțiune activă este justificată economic exclusiv în spații mari ($K \ge 6$) sau în regimuri de risc critic.
3. **Limita superioară teoretică a câștigului pe spațiu restrâns ($K=4$):**  
   Căutarea neinformativă de bază pe $K=4$ ramuri consumă în medie $\approx 2.9206$ noduri PUCT (derivat Monte Carlo în `simulate_puct_benchmark.py`). Un oracol cu prioritate perfectă consumă exact $1.000$ nod. Prin urmare, economia absolută maximă posibilă pe $K=4$ este de exact $1.9206$ noduri (reducere de 65.8%). Niciun algoritm nu poate depăși această limită structurală.
4. **Căutarea neinformativă pe $K=4$ nu poate coborî sub reperul PUCT fără scurgere de oracol:**  
   Dacă un algoritm fără informație externă ar obține semnificativ sub $\approx 2.921$ noduri pe $K=4$, acesta ar dispune de o scurgere de oracol (oracle leakage). În PUCT cu $c=1.414$ și recompensă sub-optimă $Q=0.25$, revizitarea ramurilor suboptime majorează costul peste cel al unei selecții oarbe fără repetiție ($2.50$). În V3, baseline-ul respectă riguros această barieră (compatibil cu reperul PUCT $\pm 3 \cdot SE$, $r^2 < 0.01$ față de ordinea ramurilor).

---

## 2. Scurgerea prin ordonare: Mecanism, Magnitudine în Pilotul Inițial și Rezolvare în V3

### 2.1 Mecanismul erorii în pilotul inițial (retras prin PR #165)
În implementarea istorică (`07_EVALUATION/luna/planning_influence_mve.py`):
1. Ramura optimă era asociată invariabil primei poziții din permutare (`optimal=order[0]`, linia 153);
2. Departajarea scorurilor egale în căutarea PUCT se realiza ordonat după index: `-branches.index(candidate)` (linia 252).
La pasul inițial de căutare cu prior uniform ($P(b) = 0.25$) și $N=0$, funcția alegea întotdeauna candidatul cu cel mai mic index (`order[0]`). Deoarece `order[0]` era chiar optimul, brațul de bază obținea soluția instantaneu la pasul 1 ($1.0$ nod), creând iluzia unei performanțe perfecte a căutării neghidate.

### 2.2 Magnitudinea distorsiunii
În studiul inițial, baseline-ul raporta o medie de $1.0$ nod în loc de valoarea reală PUCT de $\approx 2.921$ noduri. Această diferență de $\sim 1.92$ noduri a invalidat concluziile comparative ale pilotului și a impus retragerea acestuia prin PR #165.

### 2.3 Rezolvarea implementată în V3
În `planning_influence_mve_v3.py`, au fost introduse mecanisme riguroase de izolare:
1. **Distribuție uniform echilibrată a poziției optime:**  
   La generarea scenariilor (`build_scenarios_v3`), poziția ramurii optime este distribuită perfect uniform pe intervalul $0 \dots K-1$ (`opt_idx = idx % num_branches`).
2. **Tie-breaking ortogonal invariant la poziție prin SHA-256:**  
   Departajarea scorurilor PUCT egale utilizează un hash criptografic deterministic:
   $$\text{Hash} = \text{SHA256}(\text{scenario\_id} \parallel \text{candidate} \parallel \text{step} \parallel \text{seed})$$
   Nicio poziție de index din listă nu este favorizată.

### 2.4 Verificarea prin suita de teste de izolare (`20_TESTS/test_planning_influence_isolation.py`)
Cele 7 teste de izolare confirmă eliminarea scurgerii și integritatea invariantelor:
- `test_baseline_does_not_know_the_answer`: Costul mediu al bazei este statistic compatibil cu reperul PUCT ($2.9206 \pm 3 \cdot SE$), iar corelația cu indexul optim este nulă ($r^2 < 0.01$).
- `test_permutation_invariance`: Permutarea ordinii de prezentare a opțiunilor produce decizii și costuri identice.
- `test_contradiction_veto_enforces_strictly_uniform_priors`: Memoriile marcate `CONFIRMED_CONTRADICTION` forțează priori strict uniformi ($0.25$) pentru toate politicile, cu zero acțiuni de verificare.
- `test_planted_oracle_leakage_is_detected`: Monkeypatch-ul defectului vechi în `run_planner_v3` prăbușește media la $1.00$ și eșuează testul de izolare cu `AssertionError`.
- `test_legacy_planning_influence_mve_does_not_leak_oracle_to_baseline`: Test strict `xfail(strict=True)` care certifică prezența defectului în harnașamentul vechi retras.
- `test_static_code_inspection_zero_oracle_access`: Verificare pe AST că planificatorul și compilatorul de memorie nu accesează atribute private de oracol.
- `test_v1_frozen_constants_match_specification_document`: Constantele V1 sunt verificate caracter-cu-caracter față de documentul de specificație.

---

## 3. Curbele Principale și Pragurile de Acuratețe ($p^*$)

### 3.1 Tabelul principal al experimentului (`table_luna_1_main_experiment.csv`, fragment sinoptic)
Evaluare pe $N=200$ scenarii independente per celulă (total 17.600 evaluări de planificare):

| Mod Aplicabilitate | Acuratețe ($p$) | Politică | Noduri Medii | 95% CI Noduri | Fatale Medii | $\Delta_{\text{nodes}}$ vs Base | 95% CI $\Delta_{\text{nodes}}$ | $\Delta_{\text{fatals}}$ vs Base |
| :--- | :--- | :--- | :--- | :--- | :--- | :--- | :--- | :--- |
| calibrated | 0.0 | baseline | 2.880 | [2.685, 3.095] | 0.995 | +0.000 | [0.000, 0.000] | +0.000 |
| calibrated | 0.0 | v1_uncertainty | 3.435 | [3.265, 3.620] | 1.255 | -0.555 | [-0.755, -0.370] | +0.260 |
| calibrated | 0.0 | v2_verification_action | 3.660 | [3.445, 3.880] | 1.130 | -0.780 | [-0.990, -0.580] | +0.135 |
| calibrated | 0.4 | baseline | 2.755 | [2.555, 2.960] | 0.915 | +0.000 | [0.000, 0.000] | +0.000 |
| calibrated | **0.4 ($p^*$)** | **v1_uncertainty** | 2.445 | [2.225, 2.655] | 0.715 | **+0.310** | [0.080, 0.565] | -0.200 |
| calibrated | 0.4 | v2_verification_action | 2.685 | [2.455, 2.915] | 0.705 | +0.070 | [-0.175, 0.340] | -0.210 |
| calibrated | 0.5 | baseline | 2.810 | [2.610, 3.005] | 0.985 | +0.000 | [0.000, 0.000] | +0.000 |
| calibrated | 0.5 | v1_uncertainty | 2.110 | [1.910, 2.310] | 0.620 | +0.700 | [0.460, 0.965] | -0.365 |
| calibrated | **0.5 ($p^*$)** | **v2_verification_action** | 2.320 | [2.110, 2.540] | 0.540 | **+0.490** | [0.230, 0.745] | -0.445 |
| calibrated | 0.8 | baseline | 2.865 | [2.670, 3.050] | 0.975 | +0.000 | [0.000, 0.000] | +0.000 |
| calibrated | 0.8 | v1_uncertainty | 1.435 | [1.305, 1.575] | 0.235 | +1.430 | [1.190, 1.640] | -0.740 |
| calibrated | 0.8 | v2_verification_action | 1.710 | [1.570, 1.845] | 0.215 | +1.155 | [0.940, 1.370] | -0.760 |
| calibrated | 1.0 | v1_uncertainty | 1.000 | [1.000, 1.000] | 0.000 | +1.840 | [1.655, 2.040] | -0.940 |
| uninformative | 0.0 | v1_uncertainty | 3.800 | [3.590, 4.055] | 1.370 | -0.800 | [-1.080, -0.545] | +0.310 |
| uninformative | **0.5 ($p^*$)** | **v1_uncertainty** | 2.415 | [2.180, 2.650] | 0.660 | **+0.565** | [0.300, 0.835] | -0.395 |
| uninformative | **0.7 ($p^*$)** | **v2_verification_action** | 2.420 | [2.205, 2.640] | 0.595 | **+0.550** | [0.305, 0.780] | -0.415 |

### 3.2 Pragurile critice de rentabilitate ($p^*$) (`table_luna_2_accuracy_thresholds.csv`)
Pragul $p^*$ este definit formal ca nivelul minim de acuratețe la care $\Delta_{\text{nodes}} > 0$ cu $95\%$ interval bootstrap strict pozitiv și $\Delta_{\text{fatals}} \le 0.05$:

- **Mod Calibrat (Calibrated Applicability):**
  - Politica `v1_uncertainty`: $p^* = \mathbf{0.4}$ ($\Delta_{\text{nodes}} = +0.310$, 95% CI [0.080, 0.565]).
  - Politica `v2_verification_action`: $p^* = \mathbf{0.5}$ ($\Delta_{\text{nodes}} = +0.490$, 95% CI [0.230, 0.745]).
- **Mod Neinformativ (Uninformative Applicability):**
  - Politica `v1_uncertainty`: $p^* = \mathbf{0.5}$ ($\Delta_{\text{nodes}} = +0.565$, 95% CI [0.300, 0.835]).
  - Politica `v2_verification_action`: $p^* = \mathbf{0.7}$ ($\Delta_{\text{nodes}} = +0.550$, 95% CI [0.305, 0.780]).

---

## 4. Verificarea ca Acțiune: Analiza Cost-Beneficiu

Rezultatele experimentului de ablație (`table_luna_4_verification_ablation.csv`) demonstrează mecanica costului de verificare:

| Acuratețe | Noduri Baseline | Fatale Base | Noduri V1 | Fatale V1 | Noduri V2 | Fatale V2 | Noduri Verificare Consumate | Reducere Fatale (V2 vs V1) | Economie Netă Cost (V2 vs V1) |
| :---: | :---: | :---: | :---: | :---: | :---: | :---: | :---: | :---: | :---: |
| 0.2 | 2.790 | 0.970 | 2.820 | 0.980 | 3.055 | 0.945 | 0.215 | +0.035 | **-0.235** |
| 0.5 | 2.760 | 1.000 | 2.035 | 0.555 | 2.340 | 0.555 | 0.245 | +0.000 | **-0.305** |
| 0.8 | 2.810 | 0.990 | 1.380 | 0.195 | 1.675 | 0.190 | 0.280 | +0.005 | **-0.295** |

### Concluzii privind acțiunea de verificare:
1. **Eficiența nodurilor:** În spațiul restrâns de $K=4$, politica V2 consumă între $0.215$ și $0.280$ noduri adiționale în medie pentru acțiuni de verificare.
2. **Bilanț economic negativ pe $K=4$:** Economia netă este negativă ($-0.235$ până la $-0.305$ noduri) deoarece atenuarea pasivă V1 reduce deja riscul fatal fără a plăti taxa explicită de 1.0 nod pe căutare.
3. **Când este justificată verificarea ca acțiune:** Verificarea explicită devine avantajoasă exclusiv când numărul de ramuri crește ($K \ge 6$), unde o eroare nefiltrată costă 3-5 noduri de explorare irosită.

---

## 5. Robustețea pe Toată Grila (`table_luna_5_robustness_grid.csv`)

Evaluarea variațiilor dimensionale confirmă scalarea avantajului memoriei:

### 5.1 Scalarea numărului de ramuri ($K = 4 \to K = 6 \to K = 8$)
- **La $K=4$ (Standard, $F=2, c=1.414$):**
  - Baseline: $2.840$ noduri | V1 ($acc=0.8$): $1.450$ noduri ($\Delta = \mathbf{+1.390}$ noduri).
- **La $K=6$ (LargeSearch, $F=3, c=1.414$):**
  - Baseline: $4.720$ noduri | V1 ($acc=0.8$): $1.980$ noduri ($\Delta = \mathbf{+2.740}$ noduri).
  - Vizitele fatale scad de la $1.545$ la $0.400$.
- **La $K=8$ (DenseSearch, $F=4, c=1.414$):**
  - Baseline: $6.975$ noduri | V1 ($acc=0.8$): $2.085$ noduri ($\Delta = \mathbf{+4.890}$ noduri).
  - Vizitele fatale scad de la $2.065$ la $0.350$.
- **Concluzie critică:** Valoarea ghidării prin memorie crește super-liniar cu dimensiunea spațiului de căutare. În spații largi ($K \ge 8$), memoria economisește până la 70% din noduri.

### 5.2 Sensibilitatea la densitatea acțiunilor fatale
- La $K=4, F=1$ (LowFatal): Reducerea nodurilor la $acc=0.8$ este $+1.635$ noduri (baseline $3.155 \to 1.520$).
- La $K=4, F=2$ (Standard): Reducerea este $+1.390$ noduri. Memoria oferă protecție robustă indiferent de densitatea capcanelor.

### 5.3 Sensibilitatea la constanta de explorare ($c$)
- La $c=1.0$ (Exploration_Low): Baseline $3.190$ noduri $\to$ V1 ($acc=0.8$) $1.510$ noduri ($\Delta = +1.680$).
- La $c=1.414$ (Standard PUCT): Baseline $2.840$ noduri $\to$ V1 ($acc=0.8$) $1.450$ noduri ($\Delta = +1.390$).
- La $c=2.0$ (Exploration_High): Baseline $2.560$ noduri $\to$ V1 ($acc=0.8$) $1.405$ noduri ($\Delta = +1.155$).

### 5.4 Brațul Stale (Veto de siguranță sub contradicție, `table_luna_3_stale_arm.csv`)
În scenarii în care memoria recomandă o acțiune, dar starea de contradicție este confirmată (`CONFIRMED_CONTRADICTION`):

| Politică | Noduri Medii | 95% CI Noduri | Fatale Medii | 95% CI Fatale | $\Delta_{\text{fatals}}$ vs Base | Veto Siguranță Intact |
| :--- | :--- | :--- | :--- | :--- | :--- | :--- |
| `baseline` | 2.790 | [2.580, 3.005] | 1.010 | [0.895, 1.125] | +0.000 | `True` |
| `advisory` | 2.790 | [2.580, 3.005] | 1.010 | [0.895, 1.125] | +0.000 | `True` |
| `v1_uncertainty` | 2.790 | [2.580, 3.005] | 1.010 | [0.895, 1.125] | +0.000 | `True` |
| `v2_verification_action` | 2.790 | [2.580, 3.005] | 1.010 | [0.895, 1.125] | +0.000 | `True` |

- **Concluzie:** Mecanismul de veto bazat pe contradicție cu prioritate absolută anulează complet influența memoriei învechite pe toate politicile (inclusiv `v2_verification_action`), asigurând $\Delta_{\text{fatals}} = 0.0000$ și conservând identic comportamentul de bază.

---

## 6. DEVIATIONS (Devieri față de preînregistrare și retrageri anterioare)

Conform mandatului de rigurozitate științifică, sunt documentate explicit următoarele trei devieri:

1. **Retragerea reperului $2.50$ și adoptarea reperului PUCT $\approx 2.921$ noduri:**  
   Preînregistrarea V2 menționa formula $(K+1)/2 = 2.50$ pentru $K=4$. Această formulă este exactă exclusiv pentru eșantionare aleatoare fără repetiție (sampling without replacement). În algoritmul PUCT real, dacă prima ramură explorată este suboptimă (recompensă $0.25 > 0.0$), scorul său PUCT la pasul următor este $Q + U = 0.25 + 0.1767 = 0.4267$, depășind scorul ramurilor neexplorate ($0.3535$). Astfel, PUCT revizitează ramura suboptimă înainte de a atinge toate ramurile neexplorate, crescând costul mediu al căutării la $\mu \approx 2.9206$ noduri ($SE = 0.003219$ pe 200.000 rulări în `simulate_puct_benchmark.py`). Reperul V2 de $2.50$ a fost retras formal prin Addendum-ul preînregistrat `PLANNING_INFLUENCE_PREREGISTRATION_V2_1.md`.
2. **Corecția vetoului de contradicție (Prioritate Absolută):**  
   În versiunea preliminară V3, politica `v2_verification_action` permitea ca o verificare cu rezultat `is_safe == True` să reactiveze influența asimetrică a memoriei, chiar dacă aceasta se afla în stare `CONFIRMED_CONTRADICTION`. Această scăpare reactiva influența în 45/200 de cazuri. În versiunea finală V3, regula preînregistrată V2.1 a conferit vetoului prioritate absolută: `verification_required` este forțat `False`, iar `priors` sunt forțați strict uniformi ($0.25$) pentru toate politicile, garantând $\Delta_{\text{fatals}} = 0.0000$ pe brațul stale.
3. **Retragerile din rapoartele vechi:**  
   Pilotul original Planning Influence (`07_EVALUATION/luna/planning_influence_mve.py`) a fost retras formal din depozit prin PR #165 din cauza scurgerii structurale de oracol (`optimal = order[0]` și departajare `-branches.index(candidate)`). Toate tabelele și aserțiunile din rapoartele locale anterioare asociate sunt nule și înlocuite de prezentul raport V3.

---

## 7. Decizia Conform Porții de Validare

Pe baza rezultatelor empirice obținute pe harnașamentul V3 cu oracol complet izolat:

1. **Criteriul Pragului de Acuratețe:**  
   Pragul minim de rentabilitate măsurat este $p^* = 0.4$ (calibrat) și $p^* = 0.5$ (neinformativ) pentru politica `v1_uncertainty`, ambele sub plafonul admisibil de $0.70$.
2. **Criteriul Siguranței la Contradicție:**  
   Brațul stale demonstrează neutralizarea totală a memoriei contrazise ($\Delta_{\text{fatals}} = 0.0000$).
3. **Criteriul Izolării Structurale:**  
   Brațul de bază respectă reperul matematic al dinamicii PUCT ($2.9206 \pm 3 \cdot SE$, $r^2 < 0.01$).

### Decizie Oficială:
$$\mathbf{POARTĂ\ TRECUTĂ\ (GATE\ PASSED)}$$

**Limitare Metodologică Declarată:**  
Acest experiment reprezintă o **simulare deterministă cu oracol de scenariu**. El validează matematic mecanica de fuziune a memoriei în planificare (izolarea căutării, ponderarea Bayesiană a priorităților prin incertitudine, prioritatea absolută a vetoului de contradicție), dar **nu constituie o dovadă că un agent real cu LLM planifică mai bine**. Validarea pe agenți LLM reali constituie o fază viitoare distinctă.

---

## 8. Inventarul de Hash-uri SHA-256 al Tuturor Artefactelor

Toate hash-urile sunt calculate peste conținutul normalizat LF (corespunzător stocării exacte a blob-urilor git):

| Fișier Artefact | Dimensiune LF | SHA-256 Hash |
| :--- | :--- | :--- |
| `07_EVALUATION/luna/PLANNING_INFLUENCE_PREREGISTRATION_V2.md` | 10,660 bytes | `862af62c18fa85d5bf67acf494039448ddaaf29e1df25645b2ed54db7824ada8` |
| `07_EVALUATION/luna/PLANNING_INFLUENCE_PREREGISTRATION_V2_1.md` | 6,402 bytes | `8d321e36e5a6001b2b9e06e69778398109eec2a2c356ca573567b18571703cd3` |
| `07_EVALUATION/luna/planning_influence_mve_v3.py` | 13,409 bytes | `3d75c52496020b778b44a65df68dc891713867d24cf9c8083adfe67a5e7d0168` |
| `07_EVALUATION/luna/simulate_puct_benchmark.py` | 2,237 bytes | `a71df8feb72b1c186bc193e13f0fb47dee63ea9aa42036cdd230e7b88eb9a2ce` |
| `07_EVALUATION/luna/run_experiments_v3.py` | 14,518 bytes | `1b6ae510a18d74445e8b3a4d0c494a0656635c48fe48fbdeedebda456d14f218` |
| `07_EVALUATION/luna/generate_planning_influence_report.py` | 25,246 bytes | `9b1e2f2b6d79ba58ae7818475c8960f8a6a08f8fecf266b9cd2dc1a7e1ac5149` |
| `07_EVALUATION/luna/run_all_v3.py` | 5,851 bytes | `7856b202e571c7b1e81c42bb4038a91734cf1e194abae0b0c359460d6408e2e8` |
| `20_TESTS/test_planning_influence_isolation.py` | 13,875 bytes | `d26be1d295611ffb7e09bdf131494e0673e13c9be0aac43997d69172d5190526` |
| `07_EVALUATION/luna/tables/table_luna_1_main_experiment.csv` | 8,858 bytes | `ef1b73d25578cfd81167ab0c7012e6d6d67a578b31ce3ba2e7178a5337b22f95` |
| `07_EVALUATION/luna/tables/table_luna_2_accuracy_thresholds.csv` | 417 bytes | `335a6543a504daed068d91df0a9c2b72f22d6cccae2c8acdf4aecf2e6de1504a` |
| `07_EVALUATION/luna/tables/table_luna_3_stale_arm.csv` | 367 bytes | `78efb911824fae5ded39ea1f4ebd2a211edafc4687c69e9fc27527531634fac2` |
| `07_EVALUATION/luna/tables/table_luna_4_verification_ablation.csv` | 368 bytes | `88f0c4ee48b5a536ed58deb2a7a5739ea56c492c8831f3915a0ce4b5c8455b67` |
| `07_EVALUATION/luna/tables/table_luna_5_robustness_grid.csv` | 1,511 bytes | `22bbb07dca4f734b157b091c59123ba14036f000628b9787c219558096f1ce20` |

---
**Autentificare:** Raport generat deterministic din tabelele CSV de `generate_planning_influence_report.py`.
