# PROGRAM DE CERCETARE — ANTIGRAVITY: Planning Influence, reconstruit pe un control care nu știe răspunsul

> Emis: 2026-09-16 · **Fără termen** — lucrezi până e gata și raportezi atunci.
> Un singur livrabil mare. Cercetare deterministă, offline. Fără bani, fără rețea, fără modele.
> Înlocuiește ca prioritate programele MT5; partea 0 le închide.

---

## PARTEA 0 — Închiderea liniei MT5 (înainte de orice altceva)

Verificarea V2 (descrisă în PR #119) a confirmat rezultatul nul și a găsit cinci lucruri de corectat. Le rezolvi pe ramura `antigravity/mt5-universe-study`, fără să rulezi analize noi:

1. **M7_MACD_MOM** e preînregistrat ca „parametri înghețați" din `daily_report_20260613`, dar acea strategie rula pe **M5**, nu pe H1. Singurul MACD rulat de bot pe H1 a fost 12/9/26. Declară asta în `DEVIATIONS`, lângă rezultatul M7. **Nu testa varianta 12/9/26 pe holdout** — holdout-ul e consumat.
2. **Rescrierea strategiilor**: `gen_m3_body_breakout` folosește fereastra `[i-lookback-1, i-2]`, originalul din `xau_library.py` folosește barele imediat dinaintea ultimei. Declară diferența și adaugă un test de echivalență care rulează funcțiile **originale** cu un feed simulat și le compară cu rescrierea pe aceleași date.
3. `RESEARCH_REPORT_V2.md` **nu are secțiune `DEVIATIONS`**. Adaug-o: 150 de simulări pe nivel față de minimum 500, plus punctele 1–2.
4. **Orele de rollover sunt ora serverului** (`fetch_tick_costs.py:106`), nu UTC. Corectează eticheta.
5. `table_8_power_analysis.csv` se poate calcula offline; `run_all.py` trebuie să-l **regenereze**, nu doar să-i verifice hash-ul.

Loggerul prospectiv rămâne pornit și **nu se evaluează înainte de 2026-11-10**.

Și regula de igienă, încă valabilă: **reclonează** înainte de lucru și nu face push dacă vreun commit accesibil conține `Buget_Personal_2026` sau `account-statement_2026`.

---

## 1. Ce s-a găsit: rezultatul negativ publicat nu măsura memoria

`07_EVALUATION/luna/` conține cercetarea de bază a proiectului: poate memoria externă să influențeze planificarea în mod măsurabil și sigur? Trei documente raportează același rezultat negativ:

```
baseline:   30/30 succes · 30 noduri · 0 fatale
advisory:   30/30 succes · 30 noduri · 0 fatale
treatment:  30/30 succes · 54 noduri · 12 fatale
stale:      30/30 succes · 30 noduri · 0 fatale
```

(`PLANNING_INFLUENCE_MVE_V2_VALIDATED.md`, `…_APPLICABILITY_PILOT_LOCAL_20260904.md`, `…_UNCERTAINTY_PILOT_LOCAL_20260904.md`, reprodus exact din `planning_influence_mve.py` pe `main`.)

**Brațul de bază găsește răspunsul corect din primul nod, în toate cele 30 de scenarii.** Un planificator care nu știe nimic nu poate face asta pe patru ramuri cu două fatale. Cauza e în cod:

- `build_scenarios`, linia 153: `optimal=order[0]` — ramura optimă e **mereu prima** în ordine;
- `run_planner`, liniile 247–252: la priori uniformi, toate scorurile PUCT sunt egale, iar departajarea `-branches.index(candidate)` alege **prima ramură**.

Deci baza primește răspunsul prin ordinea ramurilor. Garda `oracle_leakage_guard=compiler_does_not_read_scenario.optimal` protejează compilatorul de memorie, dar nu și planificatorul.

**Măsurat de Claude**, amestecând ordinea ramurilor în fiecare scenariu (100 de amestecări, restul codului neschimbat):

| braț | ordine originală | ordine amestecată (medie) |
|---|---|---|
| baseline | 30 noduri · 0 fatale | **87,3 noduri · 29,8 fatale** |
| advisory | 30 · 0 | 87,3 · 29,8 |
| **treatment** | **54 · 12** | **90,2 · 28,9** |
| stale | 30 · 0 | 87,3 · 29,8 |

„Tratamentul e mult mai prost decât controlul" dispare. Ce rămâne e **aproximativ egalitate** — și nici asta nu spune mare lucru, fiindcă memoria din harnașament recomandă greșit în **23 din 30** de scenarii, printr-o alegere fixă de proiectare. Un experiment în care memoria greșește în 77% din cazuri nu poate arăta dacă memoria corectă ajută.

**Întrebarea reală nu a fost pusă încă.**

---

## 2. Întrebarea

**De la ce acuratețe a memoriei și cu ce politică reduce influența memoriei costul căutării, fără să crească vizitele fatale, față de un control care nu are acces la răspuns?**

Rezultatul e o curbă, nu un da/nu: cost și risc în funcție de acuratețea memoriei, pe fiecare politică, cu pragul de la care influența începe să ajute — sau dovada că nu există un asemenea prag.

---

## PARTEA A — Preînregistrarea

`07_EVALUATION/luna/PLANNING_INFLUENCE_PREREGISTRATION_V2.md`, comisă înainte de orice rulare pe harnașamentul reparat.

- **Factori controlați**, cu nivelurile fixate:
  - acuratețea recomandării memoriei: de la 0 la 1, cel puțin 11 niveluri;
  - calitatea etichetei de aplicabilitate: calibrată (corelată cu corectitudinea la un nivel declarat) sau neinformativă;
  - numărul de ramuri și densitatea ramurilor fatale — o grilă mică, fixată;
  - brațul `stale`: memorie contrazisă sau învechită, ca în politica V1.
- **Politicile comparate**: uniformă (bază), consultativă, politica de incertitudine V1 **cu constantele ei înghețate** (`base_prior 0.25`, `influence_budget 0.40`, puterile de aplicabilitate 1,00 / 0,35 / 0,15 / 0,00) și cel mult o politică nouă, specificată complet aici.
- **Metrici primare**: noduri până la soluția verificată, vizite fatale, rata de succes. **Metrici secundare**: cererile de verificare și costul lor, influența aplicată efectiv, influența fals-pozitivă și fals-negativă.
- **Unitatea statistică**: scenariul. Intervale bootstrap pe scenarii, cu număr de replicări și sămânță fixate.
- **Dimensiunea eșantionului**: stabilită printr-o analiză a puterii, pentru un efect minim relevant declarat numeric (de exemplu o reducere a nodurilor de X% fără creșterea fatalelor peste Y). Simularea e ieftină; nu există motiv pentru 30 de scenarii.
- **Condițiile de falsificare** din `PLANNING_INFLUENCE_UNCERTAINTY_POLICY_V1.md`, secțiunea 7, aplicate ca atare.
- **Ce înseamnă nul**: nicio politică nu reduce costul la vreun nivel de acuratețe fără să crească fatalele.

---

## PARTEA B — Repararea harnașamentului

Într-un fișier nou, `planning_influence_mve_v3.py`. Cel vechi rămâne neatins, pentru audit.

- **Ordinea ramurilor e aleatoare** în fiecare scenariu, cu sămânță, iar poziția ramurii optime e **echilibrată** pe toate pozițiile.
- **Departajarea nu mai depinde de poziție**: aleatoare cu sămânță, sau o regulă despre care se poate arăta că nu corelează cu `optimal`.
- **Verificarea devine acțiune**, cum cerea politica V1 (secțiunea 4): verificarea unei ramuri costă noduri; politica alege între a verifica și a acționa.
- Constantele politicii V1, copiate **exact**, cu un test care le compară cu documentul.

**Testele de izolare față de oracol**, în `20_TESTS/`, pe care harnașamentul trebuie să le treacă înainte de orice rulare:

1. **Baza nu știe răspunsul**: pe multe scenarii cu ordine aleatoare, costul mediu al bazei e statistic egal cu cel așteptat pentru o căutare fără informație, iar poziția ramurii optime nu prezice costul.
2. **Invarianța la permutare**: aceeași scenă, cu etichetele ramurilor permutate, dă aceeași distribuție de cost pentru bază.
3. **Scurgere plantată**: o variantă a planificatorului căreia i se dă intenționat acces la `optimal` trebuie să fie **detectată** de testele 1–2. Dacă nu e, testele nu protejează nimic.
4. **Acces static**: niciun apel către `scenario.optimal` sau `scenario.suboptimal` din planificator sau din compilatorul de memorie, verificat pe codul sursă. Accesul permis e doar prin `oracle()`, contorizat ca nod.

---

## PARTEA C — Experimentul principal

Grila preînregistrată, pe harnașamentul reparat:

- curbe de noduri și fatale în funcție de acuratețea memoriei, pe fiecare politică și fiecare nivel de calitate a aplicabilității;
- **pragul de acuratețe** de la care fiecare politică bate baza, cu interval de încredere, sau constatarea că nu există;
- brațul `stale`: politica nu trebuie să devină mai periculoasă când memoria e contrazisă;
- verificarea: reduce ea efectiv daunele memoriei greșite, și cu ce cost?

---

## PARTEA D — Robustețe

Grila mică din preînregistrare: număr de ramuri, densitatea fatalelor, constanta de explorare. **Raportezi toate celulele**, nu cele care arată bine.

---

## PARTEA E — Corectarea documentelor vechi

Cele trei documente din secțiunea 1 primesc câte o **notă de erată** la început, cu trimitere la raportul nou. **Nu se rescriu**: au fost înregistrarea corectă a ceea ce s-a crezut atunci, iar istoria greșelii contează.

Același lucru pentru rândurile din README (`Planning Influence`, secțiunea „Dovada deterministă actuală" și lacuna 5 din „Lacune cunoscute"), în ambele limbi, doar după ce raportul e verificat.

---

## PARTEA F — Reproductibilitate și CI

- `07_EVALUATION/luna/run_all_v3.py`: regenerează toate tabelele într-un director temporar, le compară octet cu octet cu cele comise, iese cu eroare la orice diferență.
- `.github/workflows/planning-influence-mve.yml` rulează și testele noi de izolare. Dovezile anterioare erau `CI: UNVERIFIED` pentru că rularea rămăsese în coadă — raportul nou spune explicit dacă CI a trecut, cu link-ul la rulare.
- Toate sursele de aleatoriu, cu sămânță fixă.

---

## PARTEA G — Raportul

`07_EVALUATION/luna/PLANNING_INFLUENCE_RESULTS_V3.md`:

1. Ce nu se poate sau nu există.
2. Scurgerea prin ordonare: cum funcționa, cât a distorsionat rezultatul vechi, cum e închisă, cu testele care o dovedesc.
3. Curbele principale și pragurile de acuratețe, cu intervale.
4. Verificarea ca acțiune: beneficiu și cost.
5. Robustețea, toate celulele.
6. `DEVIATIONS`.
7. **Decizia**, după poarta din politica V1, secțiunea 8: se justifică un MVE susținut de model, se reproiectează politica, sau se oprește linia.
8. Fișierele, fiecare cu SHA-256, toate comise.

---

## Reguli

- **Nicio modificare** în `03_IMPLEMENTATION/packages/` sau în `cognitive_core/`. Harnașamentul e izolat, ca până acum.
- **Niciun MVE cu model**: politica V1 interzice explicit asta până când harnașamentul determinist arată fie un beneficiu reproductibil, fie o falsificare clară.
- Constantele politicii V1 **nu se modifică**. O politică nouă e o preînregistrare nouă.
- Nicio afirmație citată ca verificată fără script comis; nicio descompunere fără cifrele ei; nicio valoare p sau interval scris de mână.
- Ramură proprie din `origin/main`; suita completă și validatoarele de layout și igienă, verzi.

## Cum voi verifica

1. Rulez testele de izolare, inclusiv scurgerea plantată, și cer ca ea să fie detectată.
2. Refac, independent de codul tău, costul bazei pe ordine aleatoare și îl compar cu cel raportat.
3. Recalculez un prag de acuratețe dintr-o celulă a grilei.
4. Verific că preînregistrarea precede rulările și că constantele V1 sunt identice cu documentul.
5. `run_all_v3.py` offline, apoi cu un tabel alterat, și cer eroare.
6. Rularea CI, pe link.
