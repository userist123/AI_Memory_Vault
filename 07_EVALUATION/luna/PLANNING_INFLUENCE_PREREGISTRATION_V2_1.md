# Addendum la Preînregistrarea Formală — Planning Influence V2.1

> **Protocol Științific Preînregistrat — Addendum V2.1**  
> **Data Emiterii**: 2026-09-19  
> **Status**: PRE-REGISTRATION ADDENDUM LOCKED — Committed BEFORE running evaluation harness and generating new tables  
> **Calea**: `07_EVALUATION/luna/PLANNING_INFLUENCE_PREREGISTRATION_V2_1.md`  
> **Document Părinte**: `07_EVALUATION/luna/PLANNING_INFLUENCE_PREREGISTRATION_V2.md` (rămâne neschimbat în istoric)  
> **Motiv**: Declararea prealabilă a reparațiilor metodologice pentru vetoul de contradicție, a noului reper PUCT și a noului control negativ.

---

## 1. Reparația Vetoului de Contradicție (Prioritate Absolută)

### 1.1. Defectul Identificat în Implementarea Inițială V3
În implementarea inițială din `planning_influence_mve_v3.py`, funcția `compile_memory_v3` atribuia corect priori uniformi pentru starea `CONFIRMED_CONTRADICTION`. Totuși, în `run_planner_v3`, pentru politica `v2_verification_action`, dacă scenariul avea concomitent `applicability == "APPLICABLE_WITH_VERIFICATION"`, planificatorul executa acțiunea de verificare; dacă ramura recomandată se dovedea non-fatală în oracolul de scenariu (`is_safe == True`), planificatorul promova ramura la influență maximă (`w_prior = base_p + INFLUENCE_BUDGET * 1.0 * evidence`), suprascriind vetoul inițial de contradicție.

Pe un set de 200 de scenarii cu memorie învechită, influența era reactivată în 45 de cazuri, dintre care în 8 cazuri recomandarea era eronată. Afirmația inițială conform căreia „memoria în contradicție este complet neutralizată” nu era susținută de cod.

### 1.2. Regula Preînregistrată V2.1
- **Prioritate Absolută a Vetoului**: Nicio acțiune ulterioară (inclusiv verificarea empirică a absenței fatalității) nu poate reabilita sau reaplica influența unei memorii marcate `CONFIRMED_CONTRADICTION` sau `NOT_APPLICABLE`.
- **Comportament Mandatat**: Când `contradiction_state == "CONFIRMED_CONTRADICTION"`, priorii efectivi utilizați în căutarea PUCT sunt **strict uniformi** ($P(b) = 1/K, \forall b$) pentru **toate politicile** (`baseline`, `advisory`, `v1_uncertainty`, `v2_verification_action`).
- Verificarea poate confirma că o ramură este sigură, dar nu are autoritatea epistemică de a transforma o memorie contrazisă într-un prior asimetric.

---

## 2. Reperul Corectat al Bazei (PUCT Monte Carlo)

### 2.1. De ce Reperul V2 $\mathbb{E} = 2.50$ Era Eronat
Preînregistrarea V2 (secțiunea 4.3, linia ~97) specifica:
$$\mathbb{E}[\text{noduri}] = \frac{K + 1}{2} = 2.50 \quad (\text{pentru } K=4).$$
Această formulă matematică este exactă **exclusiv** pentru o căutare liniară aleatoare fără repetiție (sampling without replacement), unde fiecare ramură explorată este eliminată din candidați până la găsirea optimului.

În planificatorul real, căutarea utilizează algoritmul PUCT (Predictor Upper Confidence bounds for Trees). Chiar și cu priorități uniforme ($P(b) = 1/4$) și recompense nule ($Q(b) = 0$), formula de explorare:
$$U(s, a) = c_{puct} P(s, a) \frac{\sqrt{\sum_b N(s, b)}}{1 + N(s, a)}$$
admite revizitarea unei ramuri suboptime înainte ca toate celelalte ramuri neexplorate să fie vizitate o primă dată. În consecință, costul mediu real este strict mai mare decât $2.50$. Înlocuirea cifrei în raportul inițial cu $2.9167$ fără o declarație prealabilă de deviere a constituit o eroare de raportare.

### 2.2. Derivarea Preînregistrată a Noului Reper
Noul reper este calculat deterministic printr-o simulare Monte Carlo a dinamicii exacte PUCT din planificator:
- **Metodă**: Rularea a cel puțin 200.000 de episoade independente de căutare PUCT cu prior uniform ($K=4$, $c_{puct} = 1.414$, departajare pseudo-aleatoare uniformă);
- **Artefact**: Script dedicat `30_SCRIPTS/evaluation/simulate_puct_baseline_benchmark.py` (sau modul în `07_EVALUATION/luna/`);
- **Reper Așteptat**: $\mu \approx 2.922 \pm 0.003$ noduri ($SE < 0.003$).
- **Criteriu de Izolare**: Brațul de bază al planificatorului pe $N=200$ de scenarii trebuie să fie compatibil statistic cu acest reper derivat (interval de toleranță de $\pm 3 \cdot SE_{sample}$), iar corelația dintre poziția indexului optim și costul căutării trebuie să fie nulă ($r^2 < 0.01$).

---

## 3. Noul Control Negativ (Monkeypatch al Scurgerii Reale)

### 3.1. Limita Fostului Test de Control Negativ
Fostul test `test_planted_oracle_leakage_is_detected` utiliza un planificator fictiv („mock planner”) care alegea direct ramura optimă în pasul 1 (cost 1.0) fără a executa codul `run_planner_v3`. Acest test nu demonstra sensibilitatea suitei la o scurgere reală în mecanismul de căutare.

### 3.2. Mecanismul Noului Control Negativ
Noul test de control negativ injectează direct în `run_planner_v3`, prin monkeypatch la runtime, defectul structural din harnașamentul vechi:
1. Reordonarea ramurilor astfel încât ramura optimă să fie plasată pe prima poziție (`order[0]`);
2. Suprascrierea funcției de departajare PUCT pentru a alege indexul cel mai mic în caz de egalitate (`-branches.index(candidate)`).

Testul rulează apoi funcția completă de evaluare a bazei. Controlul negativ **trebuie să eșueze** testul de izolare (costul scade la $1.00 \ll 2.92$, iar $r^2$ explodează), dovedind că suita detectează fără echivoc scurgerea de oracol în codul real.

---

## 4. Tabele și Artefacte ce se Regenerează

După comiterea acestui addendum și implementarea reparațiilor de cod, vor fi regenerate determinist următoarele artefacte:

1. `07_EVALUATION/luna/tables/table_luna_1_main_experiment.csv` (Experiment principal $p_{acc} \in [0.0, 1.0]$, modurile calibrat vs neinformativ);
2. `07_EVALUATION/luna/tables/table_luna_2_accuracy_thresholds.csv` (Praguri de rentabilitate $p^*$);
3. `07_EVALUATION/luna/tables/table_luna_3_stale_arm.csv` (Brațul de memorie contrazisă, reflectând vetoul absolut);
4. `07_EVALUATION/luna/tables/table_luna_4_verification_ablation.csv` (Ablația costului de verificare);
5. `07_EVALUATION/luna/tables/table_luna_5_robustness_grid.csv` (Grila topologică $K=4, 6, 8$, densități fatale 25% și 50%);
6. `07_EVALUATION/luna/PLANNING_INFLUENCE_RESULTS_V3.md` (Generat exclusiv din CSV-uri prin script automat, cu digest SHA-256 calculat peste conținutul git).
