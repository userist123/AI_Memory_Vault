# PREÎNREGISTRARE — Studiul Universului MetaTrader 5: Strategii Clasice vs. Costuri Reale și Controale Aleatoare

> **Document de Preînregistrare Formală**  
> **Data înregistrării**: 2026-09-14  
> **Repository**: `AI_Memory_Vault_CODEX_READY`  
> **Calea**: `07_EVALUATION/metatrader/research/PREREGISTRATION.md`  
> **Poarta de angajament (Commit Gate)**: Acest fișier este comis în Git **înainte** de rularea sau scrierea oricărui script care leagă semnale de tranzacționare de randamente viitoare.

---

## 0. Ipoteza Științifică și Obiectivul Studiului

### 0.1. Întrebarea de cercetare
Există, în universul de instrumente financiare accesibile prin terminalul MetaTrader 5 la un broker reglementat UE (RoboForex Ltd), strategii clasice de tranzacționare mecanică (Momentum, Revenire la medie, Breakout, Carry) care generează un randament net statistic semnificativ (Sharpe net anualizat >= 0.50) după scăderea tuturor costurilor reale de tranzacționare (spread măsurat, comision pe lot, swap de finanțare peste noapte) pe date nevăzute în faza de proiectare, depășind atât rata de bază a pierderilor de retail ESMA (74%–89% conturi în pierdere), cât și controalele cu intrări aleatoare cu expunere identică?

### 0.2. Invariante Negative și Reguli de Integritate
1. **Interdicție absolută de tranzacționare**: Niciun apel `order_send`, `order_check`, `order_calc_margin` sau orice alt apel care plasează comenzi sau atinge balanța contului nu este permis. Cercetarea se face exclusiv pe date istorice extrase în mod pasiv.
2. **Zero date de autentificare**: Nicio parolă, niciun număr de cont, niciun nume de server nu este stocat în cod, fișiere de date, metadate sau commit-uri Git. Brokerul este identificat exclusiv prin denumirea corporativă `RoboForex Ltd`.
3. **Fără constante literale pentru valori p**: Nicio valoare p și niciun interval de încredere nu este scris manual (`raw_p = ...`). Toate valorile p și intervalele sunt calculate matematic prin bootstrap staționar pe blocuri.
4. **Reproductibilitate octet-cu-octet**: Toate tabelele finale sunt regenerate de runner-ul `run_all.py` într-un director temporar și comparate binar cu tabelele comise; orice diferență declanșează ieșirea cu cod de eroare nenul.
5. **Separarea strictă a scripturilor**: Scripturile `fetch_*.py` interoghează terminalul MT5 și salvează date brute; scripturile `analyze_*.py` rulează complet offline pe fișierele comise.

---

## A1. Regula de Selecție a Universului (Criterii Exclusiv Non-Performanță)

Un instrument din recensământul complet al brokerului este inclus în universul eligibil de testare dacă și numai dacă satisface simultan următoarele patru criterii obiective, evaluate **înainte de orice calcul de semnal sau randament**:

1. **Mod de tranzacționare complet**:
   `trade_mode == SYMBOL_TRADE_MODE_FULL` (cod 4 în MT5).
   Instrumentul permite atât deschiderea, cât și închiderea pozițiilor long și short fără restricții de tip 'close only' sau 'disabled'.
2. **Adâncime istorică minimă pe D1**:
   $N_{\text{bars}} \ge 1{,}250$ bare zilnice (aproximativ 5 ani de tranzacționare neîntreruptă).
   Toate instrumentele din univers trebuie să aibă istoric complet acoperind cel puțin perioada 2021-01-01 – 2026-09-14.
3. **Lichiditate și Cost Relativ Acceptabil**:
   $$\text{Cost Relativ} = \frac{\text{Spread Median (puncte)}}{\text{ATR}(14) \text{ Median (puncte)}} \le 0.12 \quad (12\%)$$
   Un instrument al cărui spread median depășește $12\%$ din variația zilnică medie este exclus din start, fiind matematic incompatibil cu exploatarea anomaliilor de preț la nivel de retail.
4. **Calitatea Datelor (Integritate structurală)**:
   - **Fără goluri anormale**: Maximum de bare lipsă consecutive în zile lucrătoare $\le 5$ bare.
   - **Zero bare înghețate**: Numărul de secvențe de bare consecutive unde $\text{Open} = \text{High} = \text{Low} = \text{Close}$ cu volum $> 0$ trebuie să fie $0$.
   - **Zero prețuri non-tranzacționabile**: Zero bare sau tick-uri cu spread negativ ($\text{Ask} < \text{Bid}$) sau spread egal cu zero pe instrumente CFD/FX fără comision fix.
   - **Ajustare split/dividende**: Pentru active cu acțiuni/CFD acțiuni (dacă există), seriile trebuie verificate empiric dacă sunt ajustate.

Niciun instrument nu poate fi adăugat sau eliminat din univers după observarea performanței oricărei strategii.

---

## A2. Specificația Strategiilor și Grila de Parametri

Se testează exact 5 familii de strategii (4 active clasice + 1 familie de controale nule):

### S1. Momentum pe Serie de Timp (Time-Series Momentum — TSMOM)
- **Concept**: Semnul randamentului cumulat pe o fereastră istorică de $L$ zile dictează direcția poziției.
- **Formulă semnal**:
  $$R_{t-L, t} = \frac{\text{Close}_t - \text{Close}_{t-L}}{\text{Close}_{t-L}}$$
  $$\text{Signal}_t = \begin{cases} +1 & \text{dacă } R_{t-L, t} > 0 \\ -1 & \text{dacă } R_{t-L, t} < 0 \\ 0 & \text{dacă } R_{t-L, t} = 0 \end{cases}$$
- **Grilă de parametri fixată**: $L \in \{20, 60, 120, 252\}$ zile lucrătoare (corespunzând la 1 lună, 3 luni, 6 luni, 1 an).

### S2. Revenire la Medie (Mean Reversion — Bollinger Z-Score)
- **Concept**: Prețul care deviază semnificativ de la media mobilă tinde să revină către medie.
- **Formulă semnal**:
  $$\mu_t = \frac{1}{W} \sum_{i=0}^{W-1} \text{Close}_{t-i}, \quad \sigma_t = \sqrt{\frac{1}{W-1}\sum_{i=0}^{W-1} (\text{Close}_{t-i} - \mu_t)^2}$$
  $$Z_t = \frac{\text{Close}_t - \mu_t}{\sigma_t}$$
  - Intrare Long: Dacă $Z_t < -Z_{\text{thresh}}$, $\text{Signal}_t = +1$. Poziția se menține până când $Z_t \ge 0$ sau au trecut maximum $K_{\text{max}} = 10$ zile de deținere.
  - Intrare Short: Dacă $Z_t > +Z_{\text{thresh}}$, $\text{Signal}_t = -1$. Poziția se menține până când $Z_t \le 0$ sau au trecut maximum $K_{\text{max}} = 10$ zile de deținere.
  - În afara condițiilor de declanșare sau după ieșire: $\text{Signal}_t = 0$.
- **Grilă de parametri fixată**: $W \in \{20, 50\}$, $Z_{\text{thresh}} = 2.0$, $K_{\text{max}} = 10$.

### S3. Breakout de Canal (Donchian Breakout)
- **Concept**: Cumpără când prețul depășește maximul pe $L$ zile; vinde când prețul scade sub minimul pe $L$ zile.
- **Formulă semnal**:
  $$\text{HighMax}_t = \max_{i=1 \dots L} \text{High}_{t-i}, \quad \text{LowMin}_t = \min_{i=1 \dots L} \text{Low}_{t-i}$$
  $$\text{Signal}_t = \begin{cases} +1 & \text{dacă } \text{Close}_t > \text{HighMax}_t \\ -1 & \text{dacă } \text{Close}_t < \text{LowMin}_t \\ \text{Signal}_{t-1} & \text{altfel (menține poziția curentă)} \end{cases}$$
- **Grilă de parametri fixată**: $L \in \{20, 50\}$ zile.

### S4. FX Carry (Swap Differential)
- **Concept**: Exploatează diferențialul de dobândă între valute prin menținerea poziției cu swap pozitiv.
- **Formulă semnal**:
  $$\text{Signal}_t = \begin{cases} +1 & \text{dacă } \text{swap\_long} > 0 \text{ și } \text{swap\_short} \le 0 \\ -1 & \text{dacă } \text{swap\_short} > 0 \text{ și } \text{swap\_long} \le 0 \\ 0 & \text{dacă ambele swap-uri sunt negative} \end{cases}$$
- **Notă de preînregistrare**: Dacă pentru o pereche FX brokerul impune swap negativ pe ambele sensuri (situație frecventă la retail din cauza marjei de finanțare a brokerului), semnalul este strict $0$ (nicio poziție deschisă).

### S0. Controale de Bază Obligatorii
1. **S0a — Cumpără-și-Ține (Passive Buy & Hold)**:
   $$\text{Signal}_t = +1 \quad \forall t$$
   Servește drept etalon pentru piața bull structurală (ex. aur, crypto).
2. **S0b — Intrări Aleatoare cu Expunere Potrivită (Random-Entry Matched Control)**:
   Pentru fiecare strategie activă $S \in \{S1, S2, S3, S4\}$, se măsoară:
   - Numărul total de tranzacții $N_{\text{trades}}$;
   - Distribuția empirică a duratelor de deținere $H = \{h_1, h_2, \dots, h_n\}$;
   - Fracția de timp petrecută long, short și flat.
   Se generează $M = 1{,}000$ traiectorii sintetice de intrări aleatoare pseudo-stochastice (cu sămânță fixată) care reproduc fidel aceeași frecvență de tranzacționare și aceeași durată de deținere. Performanța strategiei $S$ trebuie să bată distribuția empirică a controlului aleator $S0b$ la nivel de semnificație $p < 0.05$.

---

## A3. Reguli de Execuție și Modelul de Costuri Reale

### 3.1. Invariantul Anti-Lookahead (Timp de Execuție)
- Semnalul $\text{Signal}_t$ este calculat **strict** la momentul închiderii barei $t$ folosind exclusiv $\{\text{Open}_i, \text{High}_i, \text{Low}_i, \text{Close}_i, \text{Volume}_i\}_{i \le t}$.
- Execuția tranzacției are loc la **deschiderea barei următoare $t+1$** la prețul $\text{Open}_{t+1}$.
- Nicio valoare de la bara $t+1$ (inclusiv $\text{High}_{t+1}$, $\text{Low}_{t+1}$, $\text{Close}_{t+1}$) nu este accesibilă la generarea semnalului.

### 3.2. Modelul Complet de Costuri
Fiecare tranzacție dus-întors și fiecare noapte de deținere suportă deduceri de costuri:
1. **Costul Spread-ului la Intrare și Ieșire**:
   $$\text{Cost}_{\text{spread}} = \frac{\text{Spread}_{\text{entry}} + \text{Spread}_{\text{exit}}}{2 \cdot \text{Price}}$$
   Spread-ul utilizat este spread-ul median măsurat empiric pe oră din eșantionul de tick-uri.
2. **Comision Broker**:
   Deoarece comisionul depinde de tipul de cont (Standard vs. ECN/Prime), se evaluează o grilă de 4 scenarii de comision explicit declarate:
   - **Nivel 0**: $0.00 \ \$/\text{lot}$ (conturi retail standard cu comision inclus în spread);
   - **Nivel 1**: $2.00 \ \$/\text{lot}$ round-trip;
   - **Nivel 2**: $5.00 \ \$/\text{lot}$ round-trip (nivel tipic ECN);
   - **Nivel 3**: $7.00 \ \$/\text{lot}$ round-trip (nivel conservator retail).
3. **Costul Swap-ului Peste Noapte (Finanțare)**:
   Pentru fiecare noapte în care poziția rămâne deschisă la trecerea orei de rollover a serverului (00:00 server time):
   $$\text{Cost}_{\text{swap}} = \text{Swap Rate} \times \text{Zile Swap}$$
   Unde $\text{Zile Swap} = 3$ pentru noaptea de rollover triplu (miercuri spre joi pe FX și Metale conform `swap_rollover3days == 3`, respectiv vineri spre sâmbătă pe Crypto conform `swap_rollover3days == 5`), și $\text{Zile Swap} = 1$ în celelalte zile.
4. **Fără Levier (1:1 Unleveraged)**:
   Toate randamentele sunt calculate pe expunere noțională 100% finanțată (capital = valoarea contractului). Levierul este fixat la 1:1 pentru a evalua exclusiv avantajul informațional (alpha), nu multiplicarea riscului.

---

## A4. Împărțirea Temporală a Datelor (Temporal Split)

Data finală a corpusului este fixată la **$T_{\text{end}} = 2026-09-14$** (ultima zi de tranzacționare completă încheiată).

| Segment | Interval Calendaristic | Durată | Rol Metodologic |
|---|---|---|---|
| **Dezvoltare (In-Sample)** | Început istoric $\to$ $2025-03-14$ | Tot istoricul minus 18 luni | Calibrare parametri și inspecție preliminară |
| **Validare (Out-of-Sample)** | $2025-03-15 \to 2026-03-14$ | 12 luni (lunile 18–6 înainte de final) | Testare walk-forward și selecție modele |
| **Holdout Blocat (Blind)** | $2026-03-15 \to 2026-09-14$ | 6 luni | **Complet izolat.** Nu se atinge decât în Partea E, o singură dată, exclusiv pentru strategiile validate. |

---

## A5. Cadrul Statistic și Corecția pentru Testări Multiple

### 5.1. Metrici Principale
- **Randament Net Anualizat ($CAGR_{\text{net}}$)**;
- **Volatilitate Anualizată ($\sigma_{\text{ann}}$)**;
- **Raportul Sharpe Net Anualizat ($SR_{\text{net}}$)**:
  $$SR_{\text{net}} = \frac{\mathbb{E}[R_{\text{net}}] - R_f}{\sigma(R_{\text{net}})} \cdot \sqrt{252}, \quad \text{cu } R_f = 0$$
- **Maximum Drawdown ($MDD$)**;
- **Cost Drag**: Diferența dintre Sharpe Brut și Sharpe Net.

### 5.2. Testul Hansen SPA (Superior Predictive Ability) și White Reality Check
Deoarece se testează multiple combinații (strategii $\times$ parametri $\times$ instrumente), selecția naivă a celor mai bune este supusă riscului masiv de data snooping (White 2000, Hansen 2005).

- **Metodă**: Testul SPA (Hansen 2005) cu bootstrap staționar pe blocuri (Politis & Romano 1994).
- **Număr de replici bootstrap**: $B = 2{,}000$.
- **Lungimea medie a blocului geometric**: $q = 10$ zile de tranzacționare.
- **Sămânță PRNG**: `seed = 42`.
- **Valoarea $p$**: Calculată numeric ca fracția din replicile de bootstrap în care performanța centrată a celei mai bune strategii alternative depășește performanța observată. Nicio valoare $p$ nu este aproximată analitic sau scrisă manual.

### 5.3. MRES (Minimum Relevant Effect Size)
O strategie este considerată un candidat viabil dacă și numai dacă:
$$SR_{\text{net, validation}} \ge 0.50 \quad \text{și} \quad p_{\text{SPA}} < 0.05 \quad \text{și} \quad SR_{\text{net}} > SR_{S0b} \ (95\%\text{ CI})$$

### 5.4. Definiția Rezultatului Nul
Dacă nicio strategie nu atinge $SR_{\text{net}} \ge 0.50$ cu $p_{\text{SPA}} < 0.05$ pe perioada de validare după deducerea costurilor reale:
- **Rezultatul nul este acceptat formal ca răspuns științific valid și definitiv.**
- Holdout-ul blocat **rămâne sigilat și nu este deschis**.
- Se raportează numărul de strategii care păreau profitabile înainte de corecție vs. după corecție.
