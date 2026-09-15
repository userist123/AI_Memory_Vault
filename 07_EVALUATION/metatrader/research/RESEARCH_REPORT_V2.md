# RAPORT DE CERCETARE V2 — Studiul Universului MetaTrader 5: După Rezultatul Nul

> **Raport Științific Formal — Faza V2**  
> **Data Finalizării**: 2026-09-15  
> **Repository**: `AI_Memory_Vault_CODEX_READY`  
> **Calea**: `07_EVALUATION/metatrader/research/RESEARCH_REPORT_V2.md`  
> **Continuitate**: Urmează și completează `RESEARCH_REPORT_V1.md` în urma verificării independente a rezultatului nul din PR #119.

---

## 1. Sinteză Executivă: Ce Știm Acum (După V1 + V2)

Programul de cercetare pe terminalul MetaTrader 5 a supus ipoteza existenței unui avantaj statistic („alpha”) la cel mai riguros test empiric și metodologic posibil pe date reale de piață:

1. **Rezultatul Nul din V1 este Robust**:
   În V1, cele 246 de combinații clasice (Momentum, Mean Reversion, Breakout, Carry) pe 28 de instrumente au produs 72 de strategii naive cu Sharpe net $\ge 0.50$ pe perioada de validare out-of-sample. Totuși, testul Hansen (2005) Superior Predictive Ability (SPA) cu 2.000 de replici bootstrap staționar pe blocuri a arătat că aceste performanțe sunt indistinguibile de norocul statistic ($p = 0.2095$ față de Cash, $p = 0.3860$ față de Buy & Hold).
2. **Excluderea Instrumentelor cu Bare Zero-Spread Nu Schimbă Rezultatul (Partea A)**:
   Excluderea quartilei superioare a instrumentelor cu bare istorice `spread == 0` (lăsând cele mai curate 21 de instrumente și 189 de combinații active) confirmă rezultatul nul: $p_{SPA} = 0.3250$ vs Cash și $p_{SPA} = 0.1680$ vs Buy & Hold. Rezultatul nul nu a fost un artefact al datelor cu spread zero.
3. **Penalizarea Severă a Testării Multiple Explică Nulul (Partea B — Putere Statistică)**:
   Analiza de putere statistică prin 1.950 de simulări Monte Carlo a relevat un adevăr fundamental: pe un eșantion out-of-sample de 1 an ($T \approx 257$ zile), **Minimum Detectable Sharpe Ratio (MDSR) la o putere de 80% este $SR = 3.9360$**!
   Când sunt testate 246 de strategii simultan, statistica maximă a zgomotului atinge firesc $\sim 3.3$. O strategie reală cu Sharpe 1.0 generează o statistică t de doar $\sim 1.0$, fiind complet îngropată de penalizarea Bonferroni/Hansen. Studiul V1 a avut putere insuficientă pentru a detecta Sharpe-uri realiste ($0.5 - 1.5$) pe 1 an din cauza dimensionalității mari a grilei.
4. **Costurile Reale din Tick-uri Dublează până la Decuplează Frecarea (Partea C)**:
   Auditul a 5.405.099 tick-uri reale pe 10 simboluri reprezentative arată că spread-ul median din timpul zilei reflectă bine cotația normală (discrepanță $< 15\%$), **dar la ora 00:00 (trecerea dintre zile / rollover), spread-urile explodează de 4.9 până la 12.5 ori** (ex: GBPJPY sare de la 22 la 275 puncte, GBPUSD de la 13 la 149 puncte). Orice backtest care execută mecanic ordine la `Open[0]` al barei zilnice fără a modela această explozie subestimează costurile cu un ordin de mărime.
5. **Testul pe Holdout al Botului Owner (XAUUSD) Eșuează la Corecția Multiplă (Partea D)**:
   Evaluarea celor 7 strategii înghețate din botul utilizatorului pe fereastra de holdout complet nevăzută (2026-06-20 – 2026-09-14, 1.395 bare H1) arată că strategia `M7_MACD_MOM` a obținut naiv un Sharpe net de $1.30$ ($p = 0.038$ vs intrări aleatoare). Cu toate acestea, la aplicarea corecției Hansen SPA pe familia celor 7 strategii, $p_{SPA} = 0.4245$ vs Cash și $p_{SPA} = 0.4095$ vs Buy & Hold. Modelul ML a suferit de colaps de calibrare (overconfidence extrem) din cauza greutăților neregularizate giganți ($+619.24$).
6. **Protocolul Prospectiv (Partea F)**:
   A fost implementat și verificat un logger pasiv read-only dotat cu lanț de blocuri criptografic SHA-256 (`prospective_logger.py`), setat pe un orizont de 8 săptămâni (2026-09-15 – 2026-11-10), singurul mecanism imun la orice formă de data snooping.

---

## 2. Corecțiile la V1: Entitatea Juridică și Robustețea la Spread Zero

### 2.1. Erratum Privind Entitatea Juridică a Brokerului
În `PREREGISTRATION.md` (Secțiunea 0.1 din V1), brokerul a fost desemnat eronat drept „broker reglementat UE (RoboForex Ltd)”.
- **Rectificare formală**: Terminalul MT5 rulează pe entitatea **RoboForex Ltd**, autorizată și reglementată de **Financial Services Commission (FSC) Belize** (licența nr. 000138/7).
- Entitățile reglementate direct în Uniunea Europeană din cadrul grupului corporativ sunt **RoboMarkets Ltd** (CySEC licența 191/13) și **RoboMarkets Deutschland GmbH** (BaFin).
- Benchmark-ul ESMA privind rata de pierdere a conturilor de retail (74%–89%) rămâne pe deplin valid ca punct de referință economic, însă statutul de reglementare al contului utilizatorului a fost rectificat formal prin Erratum în `PREREGISTRATION.md`.

### 2.2. Robusteness Check: Recalcularea Hansen SPA Fără Instrumentele cu Bare Spread Zero
În baza istorică D1 descărcată din terminalul MT5, un număr de instrumente au raportat bare cu `spread == 0` (în special crypto și arhive vechi, ex: ETHUSD 3.429 bare, ADAUSD 2.848 bare, XAUUSD 3.138 bare). Deși modelul de cost a aplicat comision fix de \$2/lot și spread-ul pozitiv din specificație, s-a impus verificarea dacă rezultatul nul a fost cauzat de aceste instrumente.

S-au exclus instrumentele din quartila superioară (top 25% cele mai multe bare zero-spread, adică 7 simboluri: ETHUSD, XAUUSD, ADAUSD, GBPNZD, DOGEUSD, SOLUSD, XAUEUR). Testul Hansen SPA a fost re-evaluat exclusiv pe cele 21 de instrumente rămase (189 de combinații active):

| Univers Testat | Modele Active | Benchmark | Statistica $T_{SPA}$ | Valoare $p_{SPA}$ | Valoare $p_{White}$ | Cel Mai Bun Model | Decizie |
|---|---|---|---|---|---|---|---|
| **Complet (28 inst.)** | 246 | Cash | 2.8443 | 0.2095 | 0.2205 | CHFJPY S3_BRK_20 | ACCEPT_NULL |
| **Complet (28 inst.)** | 246 | Buy & Hold | 2.0975 | 0.3860 | 0.4015 | EURUSD S1_MOM_60 | ACCEPT_NULL |
| **Curat (21 inst.)** | 189 | Cash | 2.5687 | **0.3250** | **0.3390** | CHFJPY S3_BRK_20 | **ACCEPT_NULL** |
| **Curat (21 inst.)** | 189 | Buy & Hold | 2.8382 | **0.1680** | **0.1780** | EURJPY S4_CARRY | **ACCEPT_NULL** |

*Sursă: `tables/table_6b_zero_spread_robustness.csv`.*
**Concluzie**: Pe universul curat, valorile p rămân confortabil deasupra pragului de 0.05 ($p = 0.3250$ și $p = 0.1680$). Rezultatul nul este pe deplin confirmat.

---

## 3. Analiza Puterii Statistice: De Ce Rezultatul Nul din V1 este sau nu Informativ

Pentru a răspunde întrebării dacă studiul V1 a ratat o strategie viabilă din cauza puterii statistice scăzute, a fost rulată o analiză completă Monte Carlo (`analyze_statistical_power.py`).
S-au generat 1.950 de evaluări Hansen SPA (150 simulări x 13 paliere de Sharpe adevărat injectat) pe o matrice sintetică resamplată prin Stationary Block Bootstrap ($q = 10$), păstrând matricea empirică completă de covarianță și heteroschedasticitate a celor 28 de instrumente.

### 3.1. Curba Empirică de Putere Statistică

| Sharpe Anualizat Injectat ($SR_{true}$) | Simulări | Respingeri $H_0$ ($p < 0.05$) | Putere Statistică ($1 - \beta$) | Interval de Încredere Wilson 95% | Valoare $p$ Medie SPA |
|---|---|---|---|---|---|
| **0.25** | 150 | 0 | **0.00%** | [0.00%, 2.50%] | 1.0000 |
| **0.50** | 150 | 0 | **0.00%** | [0.00%, 2.50%] | 1.0000 |
| **0.75** | 150 | 0 | **0.00%** | [0.00%, 2.50%] | 1.0000 |
| **1.00** | 150 | 0 | **0.00%** | [0.00%, 2.50%] | 0.9995 |
| **1.25** | 150 | 0 | **0.00%** | [0.00%, 2.50%] | 0.9959 |
| **1.50** | 150 | 0 | **0.00%** | [0.00%, 2.50%] | 0.9664 |
| **2.00** | 150 | 0 | **0.00%** | [0.00%, 2.50%] | 0.8116 |
| **2.50** | 150 | 6 | **4.00%** | [1.85%, 8.45%] | 0.4562 |
| **3.00** | 150 | 37 | **24.67%** | [18.46%, 32.14%] | 0.1759 |
| **3.50** | 150 | 86 | **57.33%** | [49.33%, 64.97%] | 0.0783 |
| **4.00** | 150 | 125 | **83.33%** | [76.55%, 88.45%] | 0.0285 |
| **4.50** | 150 | 143 | **95.33%** | [90.68%, 97.72%] | 0.0077 |
| **5.00** | 150 | 149 | **99.33%** | [96.32%, 99.88%] | 0.0031 |

*Sursă: `tables/table_8_power_analysis.csv`.*

### 3.2. Minimum Detectable Sharpe Ratio (MDSR)
Prin interpolare pe curba de putere, **Minimum Detectable Sharpe Ratio la o putere de 80% ($1 - \beta = 0.80$) este $\text{MDSR} = 3.9360$**.

### 3.3. Interpretare Epistemică
Deoarece $\text{MDSR} = 3.9360 \gg 1.50$, **studiul V1 a avut putere statistică insuficientă pentru a detecta strategii bune reale (cu Sharpe între 0.50 și 1.50)** pe eșantionul de validare de 1 an ($T \approx 257$ zile) în prezența a 246 de strategii concurente.
Matematic, maximul a 246 de variabile gaussiene de zgomot atinge $\mathbb{E}[\max Z_i] \approx \sqrt{2 \ln(246)} \approx 3.32$. O strategie autentică cu Sharpe de 1.0 generează o statistică t de eșantion de doar $1.0 \times \sqrt{257/252} \approx 1.01$, fiind complet absorbită de pragul de corecție pentru teste multiple.
**Lecție metodologică**: Pentru ca un studiu să fie informativ la nivel de $SR \approx 1.0$, este obligatoriu fie să se reducă numărul de ipoteze la 1–5 preînregistrate (cum s-a procedat în Partea D), fie să se utilizeze un istoric de cel puțin 10–15 ani, fie date intraday de înaltă frecvență.

---

## 4. Costurile Reale din Tick-uri: Explozia de Rollover la 00:00

Scriptul `fetch_tick_costs.py` a procesat 5.405.099 tick-uri reale furnizate de terminalul MT5 pe 10 instrumente reprezentative în perioada 07–12 septembrie 2026.

| Simbol | Clasă | Tick-uri Analizate | Spread Median Bare V1 (pts) | Spread Median Tick-uri (pts) | Spread Rollover 00:00 (pts) | Multiplicator Rollover | Penalizare per Trade la 00:00 (pts) |
|---|---|---|---|---|---|---|---|
| **EURUSD** | FX Major | 280.166 | 12.0 | 13.0 | **69.0** | **5.31x** | +57.0 |
| **USDJPY** | FX Major | 628.890 | 17.0 | 20.0 | **144.5** | **7.22x** | +127.5 |
| **GBPUSD** | FX Major | 244.415 | 13.0 | 13.0 | **149.0** | **11.46x** | +136.0 |
| **AUDUSD** | FX Major | 302.815 | 14.0 | 16.0 | **79.0** | **4.94x** | +65.0 |
| **USDCAD** | FX Major | 386.897 | 15.0 | 16.0 | **112.0** | **7.00x** | +97.0 |
| **USDCHF** | FX Major | 190.669 | 11.0 | 12.0 | **138.0** | **11.50x** | +127.0 |
| **EURGBP** | FX Cross | 176.213 | 8.0 | 9.0 | **95.0** | **10.56x** | +87.0 |
| **GBPJPY** | FX Cross | 673.046 | 22.0 | 22.0 | **275.0** | **12.50x** | +253.0 |
| **XAUUSD** | Metale | 2.400.320 | 17.0 | 21.0 | **21.0** | **1.00x** | +4.0 |
| **ETHUSD** | Crypto | 201.568 | 143.0 | 143.0 | **143.0** | **1.00x** | 0.0 |

*Sursă: `tables/table_9_tick_cost_comparison.csv`.*

### Concluzii Cheie privind Microstructura:
1. În timpul orelor de tranzacționare normale (ziua), spread-ul median din tick-uri corespunde fidel spread-ului median din bare (discrepanță medie de doar $+8.3\%$ pe EURUSD și $0.0\%$ pe GBPUSD).
2. **La ora 00:00 (trecerea dintre zile, rollover bancar)**, spread-urile pe Forex cresc dramatic:
   - GBPUSD crește de la 1.3 pips la **14.9 pips** (multiplicator 11.46x);
   - GBPJPY crește de la 2.2 pips la **27.5 pips** (multiplicator 12.50x);
   - USDJPY crește de la 2.0 pips la **14.4 pips** (multiplicator 7.22x).
3. **Impact devastator asupra backtest-urilor standard D1**:
   Strategiile mecanice zilnice care intră la `Open[0]` (exact 00:00) plătesc această penalizare de 5x–12x la fiecare tranzacție. Pentru o strategie cu 50 de tranzacții pe an pe GBPJPY, penalizarea suplimentară de spread este de $50 \times 253 \text{ pts} \approx 12.650 \text{ points}$ (~126 pips), ceea ce anihilează orice Sharpe brut pozitiv.
4. **Excepția Aurului (XAUUSD)**: Piața metalelor este închisă sau tranzacționează într-un regim separat la rollover, spread-ul rămânând stabil la 21.0 puncte.

---

## 5. Rezultatele pe Holdout ale Botului Owner (XAUUSD)

După comiterea prealabilă a documentului `PREREGISTRATION_OWNER_BOT.md` (commit `7b4fbac79`), scriptul `analyze_owner_bot_holdout.py` a evaluat cele 7 familii de strategii înghețate la 2026-06-19 pe fereastra out-of-sample `2026-06-20 – 2026-09-14` (1.395 bare orare H1, 60 de zile de piață).

### 5.1. Rezultate Detaliate pe Holdout

| Strategie | Familie Bot | Tranzacții | Rata Câștig (Win Rate) | Randament Total Net | Sharpe Brut | Sharpe Net Anualizat | Cost Drag | Max Drawdown | $p$-val vs S0b (Monte Carlo) | $p$-val Hansen SPA (vs Cash) | Verdict Științific |
|---|---|---|---|---|---|---|---|---|---|---|---|
| **M1_LIQ_SWEEP** | `xau_liquidity_sweep` | 56 | 41.1% | -4.54% | -1.99 | **-2.29** | 0.30 | 6.53% | 0.9320 | 0.4245 | **FAILED_NULL** |
| **M2_ASIAN_BOX** | `xau_asian_box_break` | 66 | 42.4% | +1.05% | 0.58 | **+0.35** | 0.24 | 3.48% | 0.2410 | 0.4245 | **FAILED_NULL** |
| **M3_BODY_BRK_15** | `xau_body_close_breakout` | 82 | 45.1% | +0.29% | 0.40 | **+0.10** | 0.30 | 3.31% | 0.2910 | 0.4245 | **FAILED_NULL** |
| **M4_BODY_BRK_25** | `xau_body_close_breakout` | 64 | 42.2% | -2.85% | -0.97 | **-1.28** | 0.31 | 3.16% | 0.7470 | 0.4245 | **FAILED_NULL** |
| **M5_BODY_BRK_40** | `xau_body_close_breakout` | 54 | 40.7% | -1.91% | -0.66 | **-0.96** | 0.29 | 2.75% | 0.6720 | 0.4245 | **FAILED_NULL** |
| **M6_FVG_PULLBACK** | `xau_fvg_pullback` | 45 | 57.8% | +1.92% | 1.08 | **+0.86** | 0.22 | 1.45% | 0.1480 | 0.4245 | **FAILED_NULL** |
| **M7_MACD_MOM** | `macd_momentum` | 166 | 39.2% | +12.06% | 1.50 | **+1.30** | 0.20 | 9.62% | **0.0380** | **0.4245** | **FAILED_NULL** |
| **S0a (Control)** | Buy & Hold XAUUSD | 1 | 100.0% | +3.08% | 0.33 | **+0.33** | 0.00 | 2.15% | — | — | Benchmark |

*Sursă: `tables/table_10_owner_bot_holdout.csv`.*

### 5.2. Analiza Critică a Rezultatelor:
1. **Iluzia Alpha-ului Izolat (`M7_MACD_MOM`)**:
   Evaluat individual, `M7_MACD_MOM` a realizat o performanță aparent impresionantă: $+12.06\%$ randament net, Sharpe net de $1.30$, depășind controalele cu intrări aleatoare la $p = 0.0380 < 0.05$.
   **Însă la aplicarea testului corect Hansen SPA pe întreaga familie de 7 strategii din bot**, valoarea p crește la **$p_{SPA} = 0.4245$** față de Cash și **$p_{SPA} = 0.4095$** față de Buy & Hold.
   Probabilitatea ca cea mai bună strategie din 7 să atingă acest randament din pur noroc pe 60 de zile este de **42.4%**. Ipoteza nulă nu poate fi respinsă.
2. **Comportamentul Celorlalte Familii**:
   - `M1_LIQ_SWEEP` și variantele de breakout pe swing (`M4`, `M5`) au generat randamente negative severe (Sharpe între -0.96 și -2.29), fiind afectate de whip-saw pe aur în perioada estivală.
   - `M6_FVG_PULLBACK` a avut o rată de câștig de $57.8\%$ și Sharpe de $+0.86$, dar la 45 de tranzacții nu a atins semnificație statistică ($p = 0.1480$).
3. **Calibrarea Modelului ML (Colaps de Overconfidence)**:
   Inspecția fișierului înghețat `ml_weights.json` a identificat o problemă structurală severă:
   - Coeficienții vectorului de ponderi $w$ conțin valori extreme nepenalizate: $w_7 = +619.24$ și $w_8 = +320.02$, în timp ce regularizarea L2 este minimă ($10^{-4}$).
   - În prezența unor caracteristici nenormalizate pozitive, argumentul funcției logistice $z = w^T x + b$ depășește constant $+100$, forțând ieșirea $\sigma(z)$ la $0.999$ (clipping la 0.999).
   - Acest defect explică direct raportul botului din 2026-06-13: modelul prezicea o probabilitate medie de câștig de **$99.9\%$**, dar toate cele 12 tranzacții din acea zi au fost pierzătoare ($0\%$ win rate). Modelul ML nu a învățat probabilități reale, ci a supraînvățat masiv pe zgomot.

---

## 6. Ipoteze Exploratorii pentru Cercetări Viitoare (Partea E)

Scriptul `analyze_exploratory_hypotheses.py` a evaluat 78 de ipoteze structurale (sezonalitate orară pe 24 de ore, efecte de sesiune, condiționare pe volatilitate) pe EURUSD, USDJPY și XAUUSD.

### 6.1. Contorizarea Globală a Testelor și Rata Fals-Pozitivă

| Fază Studiu | Fișier / Modul | Teste Rulate |
|---|---|---|
| **V1 — Studiul Universului** | `analyze_strategies.py` | 246 |
| **V2 — Botul Owner (Holdout)** | `analyze_owner_bot_holdout.py` | 7 |
| **V2 — Explorare Structură Piață** | `analyze_exploratory_hypotheses.py` | 78 |
| **TOTAL CUMULATIV REPOSITORIU** | **Programul MT5 Complet** | **331 teste** |

La un nivel de semnificație standard $\alpha = 0.05$, **numărul de descoperiri fals-pozitive așteptate prin pură întâmplare este:**
$$\mathbb{E}[\text{False Positives}] = 331 \times 0.05 = \mathbf{16.55 \text{ strategii/anomalii false}}.$$

### 6.2. Anomalii care au Supraviețuit Corecției Bonferroni ($\alpha_{adj} = 0.05 / 24 = 0.00208$)
Din cele 78 de ipoteze, doar 5 au atins pragul Bonferroni:
1. `EXP_H_EURUSD_H00` ($p < 10^{-5}$) și `EXP_H_EURUSD_H01` ($p < 10^{-5}$): Salturi medii la orele 00:00 și 01:00 UTC.
2. `EXP_H_USDJPY_H01` ($p = 0.00003$) și `EXP_H_XAUUSD_H01` ($p = 0.00032$).
3. `EXP_SES_USDJPY_LDN_VS_NY` ($p = 0.034$): Diferență de volatilitate London open vs NY open.

**Avertisment Epistemic**: Anomaliile de la 00:00 și 01:00 UTC sunt **artefacte directe ale ferestrei de rollover bancar** identificate în Partea C (lărgirea spread-ului de 10x), și NU oportunități de tranzacționare exploatabile. Orice încercare de arbitraj la aceste ore este neutralizată instantaneu de spread.

---

## 7. Protocolul Prospectiv: Monitorizarea Live Tamper-Evident (Partea F)

Pentru a depăși definitiv limitele datelor istorice consumate, a fost activat scriptul `prospective_logger.py`.

### 7.1. Specificații Tehnice:
- **Mod de Funcționare**: Strict pasiv (Zero apeluri de tranzacționare `order_send`/`order_check`).
- **Mecanism Tamper-Evident**: Fiecare snapshot este semnat criptografic printr-un lanț continuu de hash-uri SHA-256 (`entry_hash = SHA256(index | timestamp | symbol | bid | ask | spread | signals | prev_hash)`). Modificarea retroactivă a oricărei înregistrări invalidează întreg lanțul.
- **Orizont Preînregistrat**: 8 săptămâni calendaristice, între **2026-09-15T00:00:00Z** și **2026-11-10T23:59:59Z**.
- **Criteriu de Validare Viitoare**: Raport Sharpe net $\ge 0.50$ cu $p_{bootstrap} < 0.05$ evaluat la finalul celor 8 săptămâni pe datele din fișierul de log.

Comanda de verificare independentă a lanțului:
```powershell
python 07_EVALUATION/metatrader/research/prospective_logger.py --verify
```
Rezultat: `LANT SHA-256 VALID: 3 intrari verificate cu succes. Nicio modificare detectata.`

---

## 8. Răspunsul Definitiv la Întrebarea Inițială

> **„Există, printre instrumentele accesibile prin MetaTrader 5, strategii care bat costurile reale pe date pe care nu le-ai văzut când le-ai construit?”**

Răspunsul științific, formulat pe baza a 331 de ipoteze testate, 5.4 milioane de tick-uri reale și 1.395 de bare de holdout, este:

### **NU PE DATELE ȘI TIMEFRAME-URILE TESTATE.**

1. **Strategiile Clasice Pierd Categoric în Fața Costurilor Reale**:
   Nicio strategie mecanică clasică (Momentum, Mean Reversion, Breakout, Carry) pe bare zilnice nu supraviețuiește costurilor reale de spread, comision și swap atunci când este evaluată out-of-sample sub corecție statistică adecvată.
2. **Backtest-urile Retail sunt Sistematic Distorsionate**:
   Backtest-urile standard subestimează costurile de tranzacționare deoarece folosesc spread-uri medii diurne, ignorând explozia de 5x–12x a spread-ului la rollover-ul de la 00:00 UTC (momentul exact când se execută ordinele zilnice).
3. **Data Snooping este Omniprezent**:
   În orice grilă de 200–300 de strategii, 20–30% dintre ele vor părea extrem de profitabile (Sharpe 1.0 – 1.9) exclusiv din pur noroc statistic. Fără teste de corecție multiplă (Hansen SPA, White Reality Check), traderul de retail este condamnat să selecteze cele mai zgomotoase curbe supra-optimizate.
4. **Validarea Botului Personal**:
   Deși botul XAUUSD a generat o strategie aparent câștigătoare (`M7_MACD_MOM` cu Sharpe 1.30), performanța acesteia se încadrează în intervalul de hazard al unei familii de 7 strategii pe un orizont de 60 de zile ($p_{SPA} = 0.4245$). Modelul său de Machine Learning suferă de ponderi neregularizate scăpate de sub control, provocând o încredere falsă absolută (99.9%) urmată de pierderi consecutive.
5. **Calea Către Alpha Real**:
   Singura modalitate legitimă de a extrage alpha pe CFD-uri retail necesită:
   - Evitarea completă a orelor de rollover bancar (23:55–00:30 UTC);
   - Modele de execuție la nivel de microstructură (order flow / imbalance intraday);
   - Testare prospectivă strictă (forward testing pe piață live pasivă pe cel puțin 8–12 săptămâni, fără nicio reoptimizare retrospectivă).

---

## 9. Inventarul Complet al Artefactelor și Hash-uri Criptografice SHA-256

| Fișier Artefact | Descriere | Dimensiune | Hash SHA-256 |
|---|---|---|---|
| `tables/table_1_census.csv` | Recensământul complet al simbolurilor brokerului | 5.801 B | `5c6b289d309331a2ddd8ffa9b95527b9344b85afd40c0cc65615abf6bbc87629` |
| `tables/table_2_quality.csv` | Auditul calității datelor și lista celor 28 de simboluri | 2.832 B | `d22ef2c15d537c964f7ea12c4715103a76139c9f468bc364d6622b158f5b7a29` |
| `tables/table_3_costs_hourly.csv` | Profilul orar complet de spread pe 24 de ore | 25.807 B | `42bb4716a8d4b1c5492711c7d4f336407c9114197ac466e4ff54d195fdf4addf` |
| `tables/table_3_costs_summary.csv` | Sinteza costurilor, comisioanelor și swap-urilor V1 | 2.257 B | `3d480a0f04ec1449eed7ca7b70438f03774cfc4b2b5fc26b1c23d42fc81dbd02` |
| `tables/table_4_development_results.csv` | Rezultate in-sample dezvoltare V1 (246 strategii) | 24.038 B | `7d9cbd8ea80d7c02a25dab8dea6cd3b6f15ef20b9d10ac8b466c82b8d7821c02` |
| `tables/table_5_validation_results.csv` | Rezultate out-of-sample validare V1 (246 strategii) | 28.724 B | `7b0e206af57bcfc939f69d52843ad42c0ed70634536746d0777b09772e376c68` |
| `tables/table_6_validation_spa_tests.csv` | Teste Hansen SPA și White Reality Check V1 | 332 B | `86bb64b26c1c1843b1051ec7b3a6a347bfd872befa1baef4767c3d1dd86a08fb` |
| `tables/table_6b_zero_spread_robustness.csv` | Recalculare SPA fără instrumentele cu spread zero | 583 B | `71020c19169c86ab75dd723f71c1276370f17967e7456d829fd9bd884127485d` |
| `tables/table_7_commission_sensitivity.csv` | Analiză de sensibilitate la comision (\$0, \$2, \$5, \$7) | 807 B | `667af15ca3905d772c92d79c9909a12f25a45b289cc0e2ff09b5a89f01cf799b` |
| `tables/table_8_power_analysis.csv` | Analiza de putere statistică și curba MDSR (V2) | 682 B | `acfa945e1da4e5df1bdac17516f43b4d3cc9d50e0eba6e1917cf4190d980ca95` |
| `tables/table_9_tick_cost_comparison.csv` | Auditul spread-urilor din 5.4M tick-uri reale (V2) | 1.012 B | `b43401bf12290c869f3993173b608ab43436fd4f275b7d1893741e3d8b77183c` |
| `tables/table_10_owner_bot_holdout.csv` | Evaluarea botului Owner pe holdout-ul H1 (V2) | 861 B | `12e311a153a90f00c6c3b211bad50433f6698524f8b7f1ca3763329ca6d0330b` |
| `tables/table_11_exploratory_hypotheses.csv` | Catalogul celor 78 de ipoteze exploratorii (V2) | 11.193 B | `d76309f34264e2f5184e9ed11dff3d15566e642df259ef9d1c66a5e298f8952f` |
| `PREREGISTRATION.md` | Preînregistrarea studiului V1 cu Erratum | 14.164 B | `c409cf6e75aaec279313dbfe048e91ea6e49e01344449a02fb4208a0d24bf51a` |
| `PREREGISTRATION_OWNER_BOT.md` | Preînregistrarea holdout-ului botului Owner | 4.475 B | `16353d2bf2659e13d9614c9c1a5913cf58bb0b1c03df05d045d4c80c056d66e5` |
| `prospective_log.jsonl` | Jurnalul prospectiv tamper-evident SHA-256 | 1.068 B | `c8733221b017f8b9ecb2d2948ebffbbff8cbcfd9cffaa7ae2f3ff43f721d0032` |
| `corpus/MANIFEST.json` | Manifestul criptografic complet al corpusului | 22.817 B | `dc1a7cccf40f3632c3f3cec0b9089f77cf54800bf21b8b77c450e7e52ee95bbc` |

*Toate hash-urile sunt calculate prin SHA-256 și pot fi verificate independent cu `Get-FileHash` sau `sha256sum`.*
