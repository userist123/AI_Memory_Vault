# PREÎNREGISTRARE — Testarea Formală a Botului Existent (Owner: XAUUSD) pe Fereastra de Holdout

> **Document de Preînregistrare Formală — Poartă de Angajament Criptografică**  
> **Data Înregistrării**: 2026-09-15  
> **Proiect**: `02_PRODUCT/projects/imported/bot/trade/elite_quant_bot_v12`  
> **Fișier Țintă**: `07_EVALUATION/metatrader/research/PREREGISTRATION_OWNER_BOT.md`  
> **Poarta de Angajament (Commit Gate)**: Acest fișier este comis în Git **ÎNAINTE** de scrierea sau execuția scriptului `analyze_owner_bot_holdout.py`.

---

## 0. Context și Obiectiv Științific

### 0.1. Miza
Utilizatorul deține un sistem de tranzacționare algoritmică pe aur (`XAUUSD`), dezvoltat și înghețat în data de **2026-06-19** (`elite_quant_bot_v12`).
Fereastra temporală dintre **2026-06-20 și 2026-09-14** (aproximativ 3 luni calendaristice, 60 de zile de tranzacționare, 1.395 de bare orare H1) reprezintă o perioadă strict **out-of-sample (Holdout)**, complet nevăzută la data finalizării botului.

Întrebarea de cercetare: **Generează strategiile înghețate din botul XAUUSD un raport Sharpe net pozitiv și statistic semnificativ ($SR_{net} \ge 0.50$, $p < 0.05$) pe această fereastră de holdout, după scăderea tuturor costurilor reale de tranzacționare (spread din tick-uri reale, comision și swap)?**

### 0.2. Analiza de Putere Statistică a Eșantionului de Holdout (Pre-Test Power Analysis)
Din Partea B a cercetării cunoaștem penalizarea severă a eșantioanelor mici:
- Pe un orizont de $T = 60$ de zile de tranzacționare, o strategie cu raport Sharpe anualizat $SR = 1.0$ are un raport Sharpe zilnic de $1.0 / \sqrt{252} \approx 0.063$.
- Eroarea standard a mediei zilnice este $\text{SE} = \sigma / \sqrt{60}$. Statistica t asociată este $t = 0.063 \times \sqrt{60} \approx 0.488$.
- **Puterea statistică pe 60 de zile D1 este sub 8% pentru a respinge $H_0: SR \le 0$ la $\alpha = 0.05$**.
- Pe date H1 (1.395 bare), dacă strategiile generează tranzacții intraday:
  Pentru ca un test să aibă o putere statistică de cel puțin 80% ($1 - \beta \ge 0.80$) la $\alpha = 0.05$ ($t \ge 1.96 + 0.84 = 2.80$), este necesar:
  * fie un număr minim de **$N_{trades} \ge 60$ tranzacții** cu un Sharpe per trade $\ge 0.36$;
  * fie un raport Sharpe anualizat excepțional ($SR \ge 2.5$).
Dacă numărul de tranzacții generate pe holdout este sub 30, testul va avea putere scăzută ($< 50\%$), iar un eventual eșec de a respinge ipoteza nulă poate reflecta insuficiența eșantionului. Acest prag este formal asumat înainte de rulare.

---

## 1. Strategii Testate și Parametri Înghețați

Toate strategiile sunt evaluate cu parametrii extrași exact din codul înghețat la 2026-06-19 (`strategies/families/xau_library.py` și `config_overrides.json`), fără nicio modificare:

| Strategie | Familie | Timeframe | Parametri Înghețați | Sursa Cod |
|---|---|---|---|---|
| **M1_LIQ_SWEEP** | `xau_liquidity_sweep` | H1 | Lookback=120, Sweep PDH/PDL, Body close back in range | `xau_library.py:56-75` |
| **M2_ASIAN_BOX** | `xau_asian_box_break` | H1 | Fereastră asiatică 00:00–07:00 UTC, Breakout între 07:00–17:00 UTC | `xau_library.py:78-100` |
| **M3_BODY_BRK_15** | `xau_body_close_breakout` | H1 | Lookback = 15 bare, full body breakout | `xau_library.py:103-120` |
| **M4_BODY_BRK_25** | `xau_body_close_breakout` | H1 | Lookback = 25 bare, full body breakout | `xau_library.py:103-120` |
| **M5_BODY_BRK_40** | `xau_body_close_breakout` | H1 | Lookback = 40 bare, full body breakout | `xau_library.py:103-120` |
| **M6_FVG_PULLBACK** | `xau_fvg_pullback` | H1 | 3-candle Fair Value Gap în ultimele 15 bare + pullback 50% | `xau_library.py:123-150` |
| **M7_MACD_MOM** | `macd_momentum` | H1 | Fast=5, Signal=5, Slow=35 | `daily_report_20260613.json` |

---

## 2. Modelul de Cost Aplicat (Conform Rezultatelor din Partea C)

Din auditul pe 2.400.320 tick-uri reale de XAUUSD (Partea C):
- **Spread de execuție**: `21.0 points` (\$0.21 per uncie troy, echivalent cu 2.1 pips pe cotație XAUUSD).
- **Comision per lot**: `2.0 USD / lot`.
- **Dimensiune contract**: `100.0 oz` per lot.
- **Valoare punct**: `0.01 USD` per point.
- **Swap finanțare**: Long = -4.75 points/zi, Short = -4.75 points/zi, Triple swap = miercuri.

---

## 3. Controale Negative și Criterii de Succes

### 3.1. Controale
1. **$S0a$ (Buy & Hold pe XAUUSD)**: Expunere pasivă 100% Long pe aur pe durata ferestrei de holdout.
2. **$S0b$ (Intrare Aleatoare cu Expunere Identică)**: 1.000 de rulări Monte Carlo cu intrări/ieșiri la momente aleatoare, păstrând exact numărul de tranzacții și durata medie de deținere ale strategiei testate.

### 3.2. Criterii de Succes
O strategie din bot este considerată validată științific dacă și numai dacă:
1. Raportul Sharpe net anualizat este $SR_{net} \ge 0.50$;
2. Valoarea p obținută prin testul de bootstrap (față de $S0b$) este $p < 0.05$;
3. Valoarea p Hansen SPA (corectată pentru cele 7 strategii testate din bot) este $p_{SPA} < 0.05$ față de Cash și față de Buy & Hold ($S0a$).

### 3.3. Testul de Calibrare a Probabilităților ML
Dacă modelul ML (din `ml_store`) a produs scoruri de probabilitate:
- Calculul scorului Brier: $BS = \frac{1}{N} \sum (p_i - y_i)^2$;
- Compararea cu scorul Brier al unui predictor naiv (frecvența marginală istorică a câștigurilor);
- Verificarea fenomenului de overconfidence observat în raportul din 2026-06-13 (`avg_ml_prob_win = 0.999` cu 0% win rate).

---

## 4. Clauza de Consumare Definitivă a Holdout-ului
Executarea scriptului `analyze_owner_bot_holdout.py` reprezintă **consumarea definitivă a ferestrei de holdout 2026-06-20 – 2026-09-14**. Nicio iterație viitoare, niciun tweaking de parametri și nicio altă modificare de cod nu va mai putea pretinde că acest set de date este out-of-sample. Rezultatul acestei rulări unice este definitiv și consemnat ca atare.
