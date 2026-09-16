# PROGRAM DE CERCETARE V2 — ANTIGRAVITY: după rezultatul nul

> Emis: 2026-09-15 · **Fără termen** — lucrezi până e gata și raportezi atunci.
> Continuă `ANTIGRAVITY_MT5_RESEARCH_PROGRAM_V1.md`; toate regulile de acolo rămân în vigoare.
> **Cercetare pe date. Niciun ordin, nici pe demo.**

---

## 0. Ce a stabilit V1 și ce a rezistat verificării

Verificat independent de Claude, pe ramura `antigravity/mt5-universe-study` (PR #119):

| afirmație din raportul V1 | verificare |
|---|---|
| `run_all.py` reproduce tabelele | **da** — 7 tabele identice la octet, compară efectiv și iese cu 1 la diferență |
| 246 combinații, 115 cu Sharpe net > 0, 72 ≥ 0,50, 29 bat S0b la p < 0,05 | **da**, recalculat din `table_5` |
| regula de univers aplicată mecanic | **da** pentru istoric, cost relativ și bare înghețate — nicio abatere |
| niciun apel de tranzacționare, nicio credențială | **da** |
| testul anti-privire-în-viitor | **da** — un semnal cu informație perfectă de la `t+1` dă Sharpe brut sub 2 după întârzierea impusă |
| SPA: p = 0,2095 față de zero, 0,386 față de cumpără-și-ține | reprodus de runner; **nerecalculat independent** |

**Rezultatul nul rămâne în picioare.** Trei lucruri însă trebuie corectate înainte de orice construiește V2 peste el:

1. **Brokerul e descris greșit.** `PREREGISTRATION.md`, linia 14, spune „un broker reglementat UE (RoboForex Ltd)". RoboForex Ltd e licențiat de FSC Belize. Entitățile UE ale grupului sunt RoboMarkets Ltd (CySEC) și RoboMarkets Deutschland GmbH (BaFin).
2. **O regulă preînregistrată nu a fost aplicată și nici declarată.** Preînregistrarea exclude instrumentele cu „spread egal cu zero pe instrumente CFD/FX fără comision fix". **Toate cele 28 de instrumente eligibile au bare D1 cu spread zero** (până la 3.429 la ETHUSD). Aplicată literal, regula ar fi golit universul. Poate a fost considerată inaplicabilă pentru că modelul de cost include un comision de 2 $/lot — dar atunci trebuia declarat în `DEVIATIONS`, iar secțiunea spune „nicio altă abatere".
3. **Botul anterior al lui Owner nu a fost testat.** Programul V1 permitea includerea celor patru familii XAU din `elite_quant_bot_v12`; raportul nu le menționează.

Și o problemă de mediu: **istoricul depozitului a fost rescris** ca să fie eliminate fișiere personale. Directorul tău de lucru are istoricul vechi. **Clonează din nou înainte de orice lucru.** Înainte de fiecare push, verifică că niciun commit accesibil nu conține `Buget_Personal_2026` sau `account-statement_2026`; dacă găsește ceva, nu faci push și raportezi.

---

## 1. De ce V2 nu e „mai multe strategii"

După un rezultat nul, tentația e să mai încerci familii, parametri, intervale de timp. Fiecare încercare nouă pe aceleași date e un test în plus, iar după destule teste câștigă ceva din noroc, oricât de riguros ar fi fiecare pas luat separat.

Perioada de validare din V1 **a fost deja privită**. Nu mai poate servi drept dovadă proaspătă pentru nimic. Holdout-ul de 6 luni e încă sigilat și poate fi folosit **o singură dată**.

Deci V2 face trei lucruri pe care V1 nu le-a putut face — măsoară cât de informativ era nulul, testează singurul set de strategii care avea deja o fereastră out-of-sample reală, și construiește singura sursă de dovezi cu adevărat curate: date care nu există încă.

---

## PARTEA A — Corecțiile la V1

Pe ramura V1, ca livrare separată:

- linia 14 din `PREREGISTRATION.md` corectată, **cu nota că a fost corectată după livrare**, nu rescrisă în tăcere;
- regula spread-zero: declarată în `DEVIATIONS`, cu motivul, plus rezultatul SPA recalculat în varianta în care instrumentele cu cele mai multe bare cu spread zero sunt excluse. Dacă nulul se schimbă, trebuie să știm.

---

## PARTEA B — Cât de informativ e rezultatul nul (analiza puterii)

**Întrebarea: dacă ar exista o strategie cu Sharpe net real de 0,5, 1,0 sau 1,5 în grila V1, cu ce probabilitate ar fi detectat-o testul SPA?**

- Simulare: date sintetice cu aceeași lungime, volatilitate, autocorelație și structură de corelație între instrumente ca datele reale; o singură strategie „plantată" cu Sharpe cunoscut, printre 245 fără avantaj; SPA rulat exact ca în V1.
- Pentru fiecare Sharpe plantat: rata de detecție pe cel puțin 500 de simulări, cu interval de încredere.
- **Rezultatul-cheie: Sharpe-ul minim detectabil cu 80% probabilitate.**

Dacă acesta iese 1,5, atunci nulul din V1 spune doar că **nu există strategii excepționale** — nu că nu există strategii bune. Diferența schimbă complet ce înseamnă „nicio strategie nu a trecut", și trebuie scrisă ca atare în raport.

---

## PARTEA C — Costurile din tick-uri, nu din bare

V1 a estimat spread-ul din câmpul `spread` al barelor. Existența a mii de bare cu spread zero arată că acel câmp nu e costul de execuție.

- Pe toate cele 28 de instrumente: `copy_ticks_range` cu bid și ask, pe cât istoric oferă brokerul, eșantionat pe zile și ore.
- Distribuția spread-ului bid-ask **la momentul execuției strategiei** (deschiderea barei), inclusiv fereastra de rollover, unde V1 a măsurat lărgiri de până la 8,7×.
- Comparație directă: cost din tick-uri față de cost din bare, pe instrument. Dacă diferă semnificativ, rezultatele V1 se recalculează cu costul corect și se raportează ambele.

---

## PARTEA D — Botul lui Owner: un singur test, pe date nevăzute

Singurul set de strategii care a existat **înainte** de perioada sigilată.

- Familiile din `02_PRODUCT/projects/imported/bot/trade/elite_quant_bot_v12/strategies/families/xau_library.py` — `xau_liquidity_sweep`, `xau_asian_box_break`, `xau_body_close_breakout`, `xau_fvg_pullback` — plus variantele MACD numite în rapoartele zilnice, **cu parametrii originali, nemodificați**. Codul lor se citește; `core/mt5_client.py` nu se importă (conține `order_send`).
- **Preînregistrare separată**, comisă înainte de orice rulare: setul exact de strategii, fereastra de test, costurile din partea C, controalele S0, criteriul de succes.
- Fereastra de test: **de la 2026-06-20** (după ultimul raport al botului) **până la 2026-09-14** — adică partea din holdout-ul V1 ulterioară creării strategiilor. Aceasta **consumă holdout-ul**; se consemnează că nu mai poate fi folosit pentru nimic altceva.
- Rulat **o singură dată**. Nicio ajustare după. Un rezultat negativ e rezultatul.
- Dacă modelul ML al botului poate fi reconstruit din cod fără reantrenare: **test de calibrare** pe aceeași fereastră — probabilitate prezisă față de frecvența reală, pe intervale. Rapoartele au arătat 0,999 prezis într-o zi cu 3 câștiguri din 50.
- Dacă Owner exportă istoricul contului pe care a rulat botul: comparație între ce spune backtest-ul și ce s-a întâmplat. **Exportul rămâne local; depozitul e public.** În depozit intră doar statistici agregate, fără număr de cont, nume sau sume identificabile.

Trei luni de date XAU dau puține tranzacții. **Calculează întâi, în preînregistrare, câte tranzacții sunt necesare** pentru a distinge un avantaj real de zero la puterea din partea B. Dacă fereastra nu le conține, spune-o înainte de rulare — și atunci testul e informativ doar pentru o eroare mare, nu pentru un avantaj mic.

---

## PARTEA E — Explorare declarată ca explorare

Dacă vrei să încerci familii noi — sezonalitate intrazi, regim de volatilitate, efecte de sesiune, rollover — e permis, **cu trei condiții**:

1. Raportul le etichetează explicit ca **generatoare de ipoteze**, fără nicio afirmație de avantaj.
2. Numărul lor de teste se **adaugă** la cele 246 din V1 în orice corecție pentru comparații multiple. Nu pornesc de la zero.
3. Nu ating fereastra din partea D.

Ce iese de aici nu se poate verifica pe date existente, fiindcă nu mai există date nevăzute. Merge doar în partea F.

---

## PARTEA F — Testul prospectiv: date care încă nu există

Singura dovadă cu adevărat curată sunt datele care vor apărea de acum încolo.

- Un logger **doar de citire**: la fiecare semnal al strategiilor selectate, înregistrează momentul, bid-ul și ask-ul reale din terminal, prețul la care s-ar fi executat și, la ieșire, rezultatul ipotetic. **Niciun ordin.**
- Candidați: strategiile din partea D, oricare ar fi rezultatul lor, plus maximum cinci ipoteze din partea E, alese și preînregistrate **înainte** de pornirea loggerului.
- **Preînregistrare: numărul minim de tranzacții sau săptămâni înainte de orice evaluare, și criteriul de succes.** Nimic nu se evaluează înainte. Nu există „mă uit să văd cum merge".
- Fiecare înregistrare cu marcaj de timp și SHA-256 pe fișierul zilnic, comis zilnic, ca să nu poată fi rescris retroactiv.
- Loggerul are nevoie de terminalul pornit. Dacă nu rulează continuu, golurile se înregistrează, nu se umplu.

---

## PARTEA G — Un broker din UE (condiționat)

V1 a rulat pe o entitate din Belize cu 37 de instrumente, fără acțiuni și indici.

- **Dacă** Owner deschide un cont demo la o entitate autorizată ASF sau cu pașaport UE, verificată în registrul ASF: refaci recensământul și calitatea datelor (B1–B3 din V1) pe acel server, inclusiv acțiuni și indici dacă există.
- Grila V1 **nu se reia** pe acel univers ca nouă căutare. Se raportează universul, costurile și calitatea; orice test de strategie pe el intră sub regulile din partea E.
- Dacă nu există cont, partea G se consemnează ca neexecutată.

---

## PARTEA H — Raportul

`07_EVALUATION/metatrader/research/RESEARCH_REPORT_V2.md`:

1. Ce nu se poate sau nu există.
2. Corecțiile la V1.
3. **Sharpe-ul minim detectabil** și ce înseamnă pentru nulul din V1.
4. Costurile din tick-uri față de cele din bare.
5. Botul lui Owner pe fereastra nevăzută, inclusiv calibrarea ML — cu numărul de tranzacții și puterea statistică lângă rezultat.
6. Explorarea, etichetată ca atare, cu numărul cumulat de teste.
7. Starea testului prospectiv: pornit, preînregistrat, fără evaluare până la pragul stabilit.
8. `DEVIATIONS`.
9. Fișierele, fiecare cu SHA-256, toate comise.

---

## Reguli suplimentare față de V1

- **Reclonare obligatorie** și verificarea fișierelor personale înainte de fiecare push (secțiunea 0).
- **Nicio afirmație care numără ca dovadă perioada de validare V1** pentru o strategie nouă.
- **Holdout-ul se consumă o singură dată**, în partea D, și se consemnează momentul.
- **Testul prospectiv nu se evaluează înainte de pragul preînregistrat**, indiferent cât de bine sau de rău arată.
- În raport, orice cifră de performanță stă lângă numărul de tranzacții și intervalul de încredere.

## Cum voi verifica

1. Corecțiile din partea A, inclusiv nota că linia 14 a fost corectată ulterior.
2. Analiza puterii: rulez o simulare cu un Sharpe plantat și compar rata de detecție.
3. Costul din tick-uri pe un instrument ales de mine.
4. Că preînregistrarea părții D precede rularea, că parametrii botului sunt identici cu cei din `xau_library.py`, și că fereastra începe după 2026-06-19.
5. Că numărul de teste din partea E e adunat la cele 246.
6. Că fișierele zilnice ale loggerului sunt comise cronologic și nicio evaluare nu apare înainte de prag.
7. `run_all.py` offline, cod de ieșire zero, apoi modific un tabel și cer eroare.

---

*Nu este sfat de investiții. Orice decizie cu bani reali e a lui Owner.*
