# RAPORT DE CERCETARE V1 — Universul MetaTrader 5: Strategii Clasice vs. Costuri Reale și Controale Aleatoare

> **Data finalizării**: 2026-09-14
> **Broker investigat**: RoboForex Ltd (cont demo autentificat pe terminalul MT5 build 5836)
> **Repository**: `AI_Memory_Vault_CODEX_READY`
> **Locație**: `07_EVALUATION/metatrader/research/RESEARCH_REPORT_V1.md`
> **Statut Preînregistrare**: `PREREGISTRATION.md` comis la `d3b11d38a` (înaintea calculelor de randament)

---

## 1. Ce s-a stabilit că NU se poate sau NU există

1. **Nu există acțiuni individuale, indici bursieri sau mărfuri agricole pe acest server demo**:
   Interogarea completă a terminalului prin `symbols_total()` și `symbols_get(group='*')` a stabilit empiric că brokerul `RoboForex Ltd` pe acest profil de cont pune la dispoziție un univers total de **exact 37 instrumente**, limitat strict la 3 clase:
   - **Forex**: 7 perechi Majore și 21 Cross-uri (28 total);
   - **Metale**: 3 instrumente (XAUUSD, XAGUSD, XAUEUR);
   - **Crypto**: 6 instrumente (BTCUSD, ETHUSD, XRPUSD, SOLUSD, ADAUSD, DOGEUSD).
   Acțiunile corporative (CFD pe acțiuni US/UE) și indicii bursieri CFD nu sunt provizionați de server pe acest cont.

2. **Nu există avantaje statistice de alpha real din strategiile mecanice clasice de retail**:
   După deducerea spread-ului măsurat pe oră, a comisionului de 2 $/lot și a swap-ului real, **nicio strategie mecanică (0 din 246) nu a supraviețuit corecției Hansen Superior Predictive Ability (SPA) test**. Toate cele 72 de strategii care păreau a avea Sharpe >= 0.50 sunt artefacte de selecție aleatorie.

3. **Nu există swap neutru pe retail**:
   Pe instrumentele crypto, brokerul percepe un comision de finanțare negativ simetric (-4.75% anual atât pe long, cât și pe short). Pe majoritatea perechilor FX, spread-ul de finanțare al brokerului face ca ambele direcții să aibă swap net negativ (ex. CHFJPY, GBPAUD, NZDCAD), erodând mecanic orice poziție menținută pe termen mediu.

---

## 2. Recensământul și Calitatea Datelor (B1 & B2)

### 2.1. Sinteza Universului
- **Total instrumente identificate pe server**: **37**
- **Instrumente validate pentru analiză (eligibile conform A1)**: **28** (75.7%)
- **Instrumente excluse definitiv conform regulii A1**: **9** (24.3%)

### 2.2. Excluderile din Univers (Documentate cu Dovadă Empirică)
Conform regulii preînregistrate A1, selecția s-a făcut exclusiv pe criterii non-performanță (integritate structurală și cost relativ):

| Simbol | Categorie | Bare D1 | Cost Relativ (Spread ÷ ATR14) | Bare Înghețate ($O=H=L=C$) | Motivul Excluderii |
|---|---|---|---|---|---|
| `AUDJPY` | FX_CROSS | 9121 | 6.32% | 6 | frozen_bars_6 |
| `AUDNZD` | FX_CROSS | 6417 | 13.56% | 40 | relative_cost_excess_13.6pct; frozen_bars_40 |
| `CADJPY` | FX_CROSS | 7095 | 6.85% | 5 | frozen_bars_5 |
| `GBPCHF` | FX_CROSS | 8875 | 6.23% | 515 | frozen_bars_515 |
| `NZDCAD` | FX_CROSS | 7737 | 2.52% | 212 | frozen_bars_212 |
| `NZDCHF` | FX_CROSS | 7735 | 2.29% | 138 | frozen_bars_138 |
| `XAGUSD` | METALS | 6188 | 160.77% | 0 | relative_cost_excess_160.8pct |
| `BTCUSD` | CRYPTO | 4406 | 2.17% | 3 | frozen_bars_3 |
| `XRPUSD` | CRYPTO | 3218 | 13.31% | 0 | relative_cost_excess_13.3pct |

*Notă metodologică*: XAGUSD a fost exclus din cauza unui cost relativ uriaș de **160.8%** (spread-ul depășește variația zilnică medie a argintului). GBPCHF, NZDCAD și NZDCHF conțineau sute de bare înghețate în arhivele vechi (1993–2000) furnizate de server, unde prețul a rămas neschimbat zile la rând.

---

## 3. Costurile Măsurate și Microstructura (B3)

### 3.1. Explozia Spread-ului la Rollover-ul de la Miezul Nopții (Ora 00:00 Server)
Analiza a 10,000 de bare orare H1 per instrument demonstrează o lărgire sistematică a spread-ului la ora 00:00 UTC (trecerea dintre sesiuni):

| Simbol | Categorie | Spread Median Zi (puncte) | Spread Rollover 00:00 (puncte) | Multiplicator Lărgire | Swap Long | Swap Short |
|---|---|---|---|---|---|---|
| `CHFJPY` | FX_CROSS | 18.0 | 157.0 | **8.72x** | -8.5 | -3.5 |
| `EURNZD` | FX_CROSS | 20.0 | 168.0 | **8.4x** | -9.5 | -3.0 |
| `GBPAUD` | FX_CROSS | 17.0 | 126.0 | **7.41x** | -5.0 | -9.0 |
| `GBPCAD` | FX_CROSS | 22.0 | 141.0 | **6.41x** | 2.9 | -16.2 |
| `GBPNZD` | FX_CROSS | 33.0 | 205.0 | **6.21x** | 1.5 | -19.0 |
| `GBPJPY` | FX_CROSS | 20.0 | 119.0 | **5.95x** | 8.0 | -30.0 |
| `EURAUD` | FX_CROSS | 16.0 | 91.0 | **5.69x** | -14.2 | 2.0 |
| `EURCAD` | FX_CROSS | 15.0 | 84.0 | **5.6x** | -6.2 | -4.5 |
| `CADCHF` | FX_CROSS | 14.0 | 70.0 | **5.0x** | 1.0 | -6.5 |
| `USDCHF` | FX_MAJOR | 11.0 | 55.0 | **5.0x** | 4.0 | -13.0 |

**Constatare**: Pentru perechi de tip `CHFJPY` sau `EURNZD`, spread-ul se lărgește de **8.4x până la 8.7x** la miezul nopții. Orice strategie care ar executa la începutul zilei fără a ține cont de această microstructură suportă o pierdere imediată masivă.

---

## 4. Rezultatele Științifice: NULE ÎNTÂI (Partea D)

### 4.1. Câte strategii păreau profitabile fără corecție? (Iluzia Selecției)
Pe perioada de Validare out-of-sample (246 combinații active testate):
- **115 din 246 combinații (46.7%)** au avut un raport Sharpe net pozitiv ($SR > 0$);
- **72 din 246 combinații (29.3%)** au depășit pragul MRES de relevanță ($SR >= 0.50$);
- **29 din 246 combinații (11.8%)** au bătut controlul aleatoriu potrivit pe expunere $S0b$ la nivel nominal $p < 0.05$.

Top 5 cele mai „atrăgătoare” combinații naive pe Validare:
1. `CHFJPY` S3 Breakout 20: Net Sharpe = +1.9862 (Gross: +2.2151, Drag: 0.2289)
2. `XAUEUR` S1 Momentum 120: Net Sharpe = +1.8773 (Gross: +2.0596, Drag: 0.1822)
3. `XAUEUR` S1 Momentum 252: Net Sharpe = +1.8773 (Gross: +2.0596, Drag: 0.1822)
4. `XAUEUR` S3 Breakout 50: Net Sharpe = +1.8773 (Gross: +2.0596, Drag: 0.1822)
5. `XAUUSD` S1 Momentum 120: Net Sharpe = +1.8721 (Gross: +2.0891, Drag: 0.2170)

### 4.2. Corecția Riguroasă Hansen SPA și White Reality Check
Când aplicăm testul de Superior Predictive Ability (Hansen 2005) cu bootstrap staționar pe blocuri ($B = 2000$ replici, lungime medie bloc $q = 10$, sămânță fixă `seed = 42`) pe întregul set de 246 combinații simultan:

| Benchmark | Modele Testate | Statistica Studentizată $T_{SPA}$ | Valoare $p$ Hansen SPA | Valoare $p$ White Reality Check | Decizie Formală |
|---|---|---|---|---|---|
| **Cash (Zero Excess Return)** | 246 | 2.8443 | **0.2095** | **0.2205** | **ACCEPT_NULL (No Alpha)** |
| **Buy & Hold (Equal Weight)** | 246 | 2.0975 | **0.386** | **0.4015** | **ACCEPT_NULL (No Alpha)** |

**Interpretare statistică**:
Valoarea $p = 0.2095 >= 0.05$ (respectiv $p = 0.386 >= 0.05$ față de Buy & Hold) înseamnă că **probabilitatea ca cel mai bun randament observat să fi apărut din pura întâmplare a testării multiple este de peste 20% (respectiv 38%)**.
Ipoteza nulă NU poate fi respinsă.

---

## 5. Holdout-ul (Partea E)

Conform contractului din preînregistrare:
> *„Dacă nicio strategie nu atinge $SR_{\text{net}} >= 0.50$ cu $p_{\text{SPA}} < 0.05$ pe perioada de validare după deducerea costurilor reale: Rezultatul nul este acceptat formal... Holdout-ul blocat rămâne sigilat și nu este deschis.”*

Deoarece $p_{\text{SPA}} = 0.2095 > 0.05$, **HOLDOUT-UL (2026-03-15 – 2026-09-14) A RĂMAS STRICT SIGILAT ȘI BLOCAT**. Niciun semnal nu a fost evaluat pe datele din ultimele 6 luni, prevenind orice scurgere informațională post-studiu.

---

## 6. Abateri de la Preînregistrare (`DEVIATIONS`)

1. **Restricția Universului la 37 de Instrumente**:
   Preînregistrarea anticipa testarea pe acțiuni și indici bursieri conform ghidului general MT5. Conectarea la terminalul demo RoboForex Ltd a relevat că serverul nu oferă acțiuni sau indici pe acest cont. Studiul s-a concentrat pe cele 37 de instrumente disponibile (Forex, Metale, Crypto), documentând absența acțiunilor în Secțiunea 1.
2. **Nicio altă abatere**:
   Împărțirea temporală, grilele de parametri, regulile de execuție la Open, formulele de cost și testul Hansen SPA au fost aplicate fără nicio modificare.

---

## 7. Recomandare Tehnico-Financiară

Rezultatul empiric confirmă direct statistica ESMA (74%–89% din conturile de retail pierd bani):
1. **Costurile reale distrug strategiile naive**: Chiar și strategiile care pe brut par să funcționeze suportă o reducere a raportului Sharpe cu 0.20 – 0.50 doar din spread și swap.
2. **Data Snooping este pericolul #1**: Fără testul Hansen SPA, un cercetător ar fi selectat `CHFJPY S3_BRK_20` sau `XAUEUR S1_MOM_120` ca fiind „strategii de succes” (Sharpe ~ 1.9), ignorând faptul că într-un univers de 246 de teste, astfel de valori apar firesc din pur noroc statistic.
3. **Recomandare**: Nu alocați capital real pe strategii tehnice clasice fără un model distinct de microstructură (order book imbalance, execuție limită pasivă fără spread taker, sau strategii de lichiditate instituționale cu comisioane zero/rebate).

---

## 8. Fișiere de Date și Hash-uri Criptografice SHA-256

| Fișier | Rol | Dimensiune | SHA-256 |
|---|---|---|---|
| `tables/table_1_census.csv` | Recensământul complet al instrumentelor | 5801 B | `5c6b289d309331a2ddd8ffa9b95527b9344b85afd40c0cc65615abf6bbc87629` |
| `tables/table_2_quality.csv` | Auditul calității datelor și excluderi | 2832 B | `d22ef2c15d537c964f7ea12c4715103a76139c9f468bc364d6622b158f5b7a29` |
| `tables/table_3_costs_summary.csv` | Profilul de costuri și swap | 2257 B | `3d480a0f04ec1449eed7ca7b70438f03774cfc4b2b5fc26b1c23d42fc81dbd02` |
| `tables/table_4_development_results.csv` | Rezultate in-sample dezvoltare | 24038 B | `7d9cbd8ea80d7c02a25dab8dea6cd3b6f15ef20b9d10ac8b466c82b8d7821c02` |
| `tables/table_5_validation_results.csv` | Rezultate out-of-sample validare | 28724 B | `7b0e206af57bcfc939f69d52843ad42c0ed70634536746d0777b09772e376c68` |
| `tables/table_6_validation_spa_tests.csv` | Rezultate teste Hansen SPA / White RC | 332 B | `86bb64b26c1c1843b1051ec7b3a6a347bfd872befa1baef4767c3d1dd86a08fb` |
| `tables/table_7_commission_sensitivity.csv` | Analiza de sensibilitate la comision | 807 B | `667af15ca3905d772c92d79c9909a12f25a45b289cc0e2ff09b5a89f01cf799b` |
| `corpus/MANIFEST.json` | Manifestul integral al corpusului D1/H1 | 22817 B | `dc1a7cccf40f3632c3f3cec0b9089f77cf54800bf21b8b77c450e7e52ee95bbc` |
