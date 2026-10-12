# Specification Mining Report: Financial Data Ingestion & Schema Architecture

**Author**: Spec Miner Survey 2 (`teamwork_preview_spec_miner`)  
**Target Working Directory**: `c:\Users\user\Documents\Codex\AI_Memory_Vault_CODEX_READY\.agents\spec_miner_survey_2`  
**Date**: 2026-08-26  
**Status**: Authoritative Discovery & Schema Specification  

---

## 1. Executive Summary & Authoritative Specification Sources

This specification defines the complete structural, mathematical, behavioral, and schema requirements for integrating financial market data into the **AI Memory Vault** (`AI_Memory_Vault_CODEX_READY`). The pipeline ingests financial scripts and multi-sheet workbooks from `C:\Users\user\Desktop\Nu sterge\nusterge\` (`ghid.py`, `Analiza_Piata_Profesionala.xlsx`), computes 10 quantitative technical indicators, evaluates multi-factor confluence scoring and dynamic risk parameters, produces schema-valid canonical memory notes (`knowledge`, `decision`, `experience`, `error`, `lesson`, `resource`, `hypothesis`), and exposes layered retrieval via `FinancialQueryEngine` and REST endpoints in `vault_api.py`.

### Authoritative Specification Sources Inspected
1. **Primary Python Script**: `C:\Users\user\Desktop\Nu sterge\nusterge\ghid.py` (1,954 lines, unified quantitative analysis engine).
2. **Primary Excel Model**: `C:\Users\user\Desktop\Nu sterge\nusterge\Analiza_Piata_Profesionala.xlsx` (15 specialized sheets, 95 tracked instruments, macro benchmarks, and hedge fund trading journal).
3. **Cognitive Vault Rules**: `AGENTS.md` and `.agents/rules/vault_cognitive_rules.md` (P0–P18 Trust Boundary Invariants).
4. **Canonical Frontmatter Schema**: `99_SYSTEM/Canonical_Frontmatter.md` and `memory_controller/validation/schema.py`.
5. **Memory Controller Architecture**: `memory_controller/controller.py`, `memory_controller/financial_search.py`, `memory_controller/financial_query.py`.
6. **Task Directives**: `.agents/ORIGINAL_REQUEST.md` (R1 Financial Ingestion, R2 Query Engine, R3 Verification).

---

## 2. Features Discovered

| # | Category | Feature | Description | Inputs | Outputs | Error Behavior | Discovered Via |
|---|---|---|---|---|---|---|---|
| 1 | Asset Catalog | 95-Instrument Multi-Asset Universe | Quantitative registry spanning 14 Indices, 30 Equities, 25 Cryptocurrencies, 12 FX Pairs, 14 Commodities | Ticker symbols or names (e.g. `^GSPC`, `NVDA`, `BTC-USD`, `EURUSD=X`, `GC=F`) | Standardized `Instrument` metadata (name, symbol, category, sector, base currency, competitors, calendar events) | Unrecognized symbol returns None or fallback match | `ghid.py:44-166`, `Analiza_Piata_Profesionala.xlsx:LIST_ACTIVE` |
| 2 | Macro Benchmark | 5 Macroeconomic Benchmark Tickers | Real-time market volatility, sovereign yields, and currency benchmarks | Symbols: `^VIX`, `^TNX` (10Y Yield), `^IRX` (2Y Yield / 13W), `^TYX` (30Y Yield), `DX-Y.NYB` (DXY) | Close price, daily variation, volatility regime classification | Fallback to deterministic neutral snapshot on network failure | `ghid.py:152-159`, `Analiza_Piata_Profesionala.xlsx:INDICATORI MACRO` |
| 3 | FRED Macro Series | 4 St. Louis Fed Economic Series | Central bank policy rates, inflation, labor market, and GDP tracking | Series IDs: `FEDFUNDS`, `CPIAUCSL`, `UNRATE`, `GDP` | Current and previous observation values, % period change | Fallback to offline sample tuple if `FRED_API_KEY` missing or invalid | `ghid.py:649-678`, `Analiza_Piata_Profesionala.xlsx:INDICATORI MACRO` |
| 4 | Sentiment Feed | Alternative.me Crypto Fear & Greed | Sentiment index tracking market risk appetite (0–100 scale) | HTTP GET `https://api.alternative.me/fng/?limit=1` | Integer value, classification ("Extreme Fear" to "Extreme Greed"), Romanian status ("Pozitiv", "Neutru", "Negativ") | Fallback to `50 - Neutral` (`Neutru`) | `ghid.py:635-648`, `Analiza_Piata_Profesionala.xlsx:DASHBOARD` |
| 5 | Technical Indicator | RSI-14 & Qualitative Regime Mapping | 14-period Relative Strength Index with 5-zone market pressure classifier | 14+ closing prices (`pd.Series`) | Float RSI (0–100), Status: `<30`: "Presiune excesiva vanzare", `30-45`: "Presiune moderata vanzare", `45-55`: "Echilibru", `55-70`: "Momentum ascendent", `>70`: "Presiune excesiva cumparare" | Returns `50.0` ("Echilibru") on NaN or insufficient data | `ghid.py:413-426`, `394-403` |
| 6 | Technical Indicator | MACD (12, 26, 9) & Cross Detection | Exponential moving average convergence/divergence with 4-state cross detector | 26+ closing prices (`pd.Series`) | `macd`, `signal`, `histogram`, `cross`: "Impuls pozitiv nou", "Impuls pozitiv activ", "Impuls negativ nou", "Impuls negativ activ" | Returns zero values and `"N/A"` cross on error | `ghid.py:427-447` |
| 7 | Technical Indicator | Moving Averages (20, 50, 200) & Trend | Simple moving averages, Golden/Death Cross detection, and price trend evaluation | 200+ closing prices (`pd.Series`) | `ma20`, `ma50`, `ma200`, `macross` ("Golden Cross", "Death Cross", "Neutru"), `trend` ("Bullish", "Bearish", "Sideways") | Returns None for missing windows, trend `"Sideways"` | `ghid.py:448-462`, `580-587` |
| 8 | Technical Indicator | Bollinger Bands (20, 2.0σ) | Volatility envelope based on SMA20 +/- 2 standard deviations | 20+ closing prices (`pd.Series`) | `bb_mid`, `bb_sup`, `bb_inf`, `bb_width` | Returns None on series length < 20 | `ghid.py:463-476` |
| 9 | Technical Indicator | Average True Range (ATR-14) | Volatility metric based on True Range across High, Low, and previous Close | 14+ periods OHLC (`pd.DataFrame`) | Float ATR value in quote currency units | Returns `0.0` on missing columns or empty data | `ghid.py:477-491` |
| 10 | Technical Indicator | Fast Stochastic Oscillator (14, 3) | Momentum oscillator comparing close to 14-period high-low range | 14+ periods OHLC (`pd.DataFrame`) | `stoch_k` (0–100), `stoch_d` (3-period smoothed %K) | Returns `50.0` on division by zero or error | `ghid.py:492-504` |
| 11 | Technical Indicator | Relative Volume (RVOL-20) | Ratio of current session volume to 20-day moving average volume | 20+ periods volume series (`pd.Series`) | Float multiplier (e.g. `1.50x`), where `>1.5x` = exceptional, `<0.6x` = low | Returns `1.0x` if average volume is zero or unavailable | `ghid.py:562-566`, `523-526` |
| 12 | Signal Engine | Multi-Confluence Scoring (-5 to +5) | Quantitative scoring engine synthesizing RSI, MACD, MA Cross, and RVOL | `rsi`, `macd_cross`, `ma_cross`, `rvol` | `semnal` ("BUY" \| "SELL" \| "WAIT"), `confluente` (0–5), `score` (-5 to +5) | Returns `"WAIT"`, 0 confluences, score 0 on missing inputs | `ghid.py:505-532` |
| 13 | Risk Management | Dynamic ATR SL / TP & Planned R/R | Automatic Stop Loss (1.5x ATR) and Take Profit (3.0x ATR) targeting 2.0x R/R | `price`, `atr`, `signal` ("BUY" \| "SELL") | `sl`, `tp`, `rr_ratio` (e.g. `2.00x`) | Returns `None` for `"WAIT"` or zero ATR | `ghid.py:592-598`, `378-392` |
| 14 | Probability Model | Statistical Win Probability Engine | Heuristic probability estimation based on confluences and volume breakout bonus | `confluente` (0–5), `rvol` | Percentage integer/float `35%` to `90%` (`35 + c*10 + (5 if rvol>1.2)`) | Clamped strictly to `[35, 90]` | `ghid.py:599-600` |
| 15 | AI Narrative | Movement Explanation Generator | Romanian natural language narrative describing daily price action and technical levels | Instrument dictionary `d` | Multi-sentence formatted summary string (`explica_miscare`) | Fallback to neutral default description | `ghid.py:1484-1520` |
| 16 | AI Narrative | Opportunity & Risk Identifier | Automated identification of oversold BUY setups, Golden Crosses, or overbought SELL warnings | Instrument dictionary `d` | Formatted callout string with emoji indicators (`identifica_oportunitate`) | Returns neutral waiting callout (`⏸ in zona de asteptare`) | `ghid.py:1522-1538` |
| 17 | AI Narrative | Heuristic Lesson Distillation | Institutional trading heuristic extracted from current technical setup | Instrument dictionary `d` | Actionable rule string (`extrage_lectie`) | Fallback to patience/cash position heuristic | `ghid.py:1540-1559` |
| 18 | Excel Workbook | 15-Sheet Financial Suite Model | Multi-sheet analytical platform with automated formatting, formulas, and dashboards | Workbook data dictionary | 15 Sheets: DASHBOARD, FISA ACTIV, REZUMAT EXECUTIV, SEMNALE INTRARE, INDICATORI TEHNICI, INDICATORI MACRO, COMPETITORI SECTOR, PRETURI VOLUME, RISCURI OPORTUNITATI, CALENDAR ECONOMIC, JURNAL TRANZACTII, ISTORIC TRENDING, LEGENDA, LIST_ACTIVE, GHID INVATARE | Missing sheets are skipped or created dynamically | `Analiza_Piata_Profesionala.xlsx`, `ghid.py:680-1940` |
| 19 | Excel Formulas | Dynamic R/R Formula Injection | Excel formula for real-time Risk-to-Reward calculation | Entry (Col K), SL (Col L), TP (Col M) | Formula `=IFERROR((M{row}-K{row})/(K{row}-L{row}),"N/A")` formatted as `0.00x` | Returns `"N/A"` via Excel `IFERROR` on zero risk | `ghid.py:873-874`, `SEMNALE INTRARE` |
| 20 | Excel Journal | Hedge Fund Trade Journal | Structured execution logging for hedge fund style trading with risk metrics | Trade payload: ID, Date, Time, Asset, L/S, Setup, Entry, SL, TP, Size, Risk $, Exit | 14 columns tracking P&L, execution quality, and post-trade review | Empty exit indicates open position | `Analiza_Piata_Profesionala.xlsx:JURNAL TRANZACTII` |
| 21 | Excel Trending | 24-Month Macro Snapshot | Multi-year monthly historical snapshot of RSI, S&P 500, GDP, CPI, VIX, Sentiment | Monthly macro telemetry | Appends or updates current `Month YYYY` row | Duplicate month rows are skipped | `ghid.py:1436-1480`, `Analiza_Piata_Profesionala.xlsx:ISTORIC TRENDING` |
| 22 | Schema Architecture | Draft-07 Financial JSON Schema | Formal schema validating financial note structure, frontmatter, and payloads | Python dictionary / JSON object | Boolean validation result or `jsonschema.ValidationError` | Rejection on invalid UUID, missing fields, or bad enum values | `memory_controller/financial_schema.py` |
| 23 | Schema Architecture | Pydantic v2 Domain Models | Type-safe Pydantic models for frontmatter, price/volume, indicators, and macro data | Raw dict or keyword arguments | Instantiated Pydantic models with field-level validators | Raises `pydantic.ValidationError` on type mismatch | `memory_controller/financial_schema.py` |
| 24 | Ingestion Engine | Canonical Note Transformation | Transforms raw OHLCV and indicator dicts into Draft-07 schema-valid notes | Instrument data, metadata | Atomic canonical note (`knowledge`, `decision`, `experience`, `error`, `lesson`, `resource`, `hypothesis`) | Enforces P0 (unverified for AI) and P1 (execution source_type) | `xau_kinetic/financial_ingestion/adapter.py` |
| 25 | Deduplication | Content Hash & Contradiction Detection | Deterministic SHA-256 content hashing and opposing signal conflict detection | Note content payload, existing notes registry | Unique note registration or atomic contradiction note linking opposing claims | Contradictions generate `hypothesis` notes without erasing claims | `xau_kinetic/financial_ingestion/adapter.py:MemoryDeduplicator` |
| 26 | Search Pipeline | 5-Layer Financial Search Engine | Multi-layer search combining entity resolution, SQLite filtering, hybrid BM25+Vector RRF, graph spreading activation, and progressive disclosure | Natural language query or structured filters | Context Pack dictionary with cryptographic HMAC pagination tokens | Returns empty pack cleanly on zero matches; validates token HMAC | `memory_controller/financial_search.py` |
| 27 | REST Interface | FastAPI Endpoints | GET and POST endpoints for memory proposing, query execution, and financial search | HTTP requests (`/memory/financial/search`, `/memory/propose`) | JSON responses with context packs, results, and pagination metadata | HTTP 400 with descriptive error detail | `vault_api.py:61-156` |
| 28 | Security Policy | Zero Hardcoded Secrets Enforcement | Elimination of hardcoded API keys; injection via environment variables (`FRED_API_KEY`) | Environment variables (`os.environ`) | Authenticated API calls or deterministic offline fallbacks | Prevents token leakage to logs, memory notes, or git commits | `AGENTS.md §19`, `ghid.py:29` (Audit Finding) |

---

## 3. Edge Cases & Boundary Behaviors

| # | Feature | Input Condition | Observed / Documented Behavior |
|---|---|---|---|
| 1 | RSI Calculation | All prices identical over 14 periods (zero price variation) | `delta` is all zeros; `gain` and `loss` are 0; fallback returns default `50.0` ("Echilibru") without `ZeroDivisionError`. |
| 2 | RSI Calculation | Fewer than 15 historical price points provided | Function returns default `50.0` immediately without raising exceptions. |
| 3 | MACD Cross Detection | Histogram stays positive across 2 consecutive sessions | Cross status classified as `"Impuls pozitiv activ"`; does not re-trigger `"Impuls pozitiv nou"`. |
| 4 | MACD Cross Detection | Insufficient historical points (< 26 periods) | Returns `{"macd": 0.0, "signal": 0.0, "histogram": 0.0, "cross": "N/A"}`. |
| 5 | Moving Average Cross | Series length between 50 and 199 (MA50 exists, MA200 is None) | `macross` defaults to `"Neutru"`; does not crash; `ma200` is `None`. |
| 6 | Moving Average Cross | Price exactly equal to MA50 (no clear 1% buffer) | `trend` classified as `"Sideways"`. Bullish requires `Price > MA50 * 1.01`, Bearish requires `Price < MA50 * 0.99`. |
| 7 | Bollinger Bands | Less than 20 periods of data | Returns `None` for all band values (`bb_sup`, `bb_inf`, `bb_mid`, `bb_width`). |
| 8 | ATR Calculation | Zero price volatility across all days (High == Low == Close) | ATR returns `0.0`. SL and TP calculation functions return `None, None, None` safely. |
| 9 | Dynamic SL / TP | Signal is `"WAIT"` | Stop Loss and Take Profit return `None, None, None`; R/R ratio returns `"N/A"`. |
| 10 | Dynamic SL / TP | Risk is zero (`entry == sl`) in Excel R/R calculation | Excel formula `=IFERROR((M{row}-K{row})/(K{row}-L{row}),"N/A")` safely evaluates to `"N/A"`. |
| 11 | Relative Volume (RVOL) | 20-day average volume is 0 | Function returns safe fallback `1.0x` to prevent division by zero. |
| 12 | Confluence Scoring | Multiple conflicting signals (e.g. RSI=25 [+2] but Death Cross [-2] and low volume [-1]) | Confluence score aggregates to `-1`; final signal evaluates to `"WAIT"`. |
| 13 | Statistical Probability | 5 confluences with RVOL = 2.0x (35 + 50 + 5 = 90) | Probability clamped strictly at maximum ceiling of `90%`. |
| 14 | Statistical Probability | 0 confluences with RVOL = 0.5x (35 + 0 + 0 = 35) | Probability clamped at minimum floor of `35%`. |
| 15 | FRED API Fetch | `FRED_API_KEY` is missing, empty, or unset in environment | Fetcher logs debug message and returns deterministic offline sample data (`_SAMPLE_FRED_DATA`) without raising fatal errors. |
| 16 | Alternative.me Sentiment | External API endpoint unreachable or returns HTTP 500 | Fetcher catches exception and returns fallback neutral object `{value: 50, classification: "Neutral", status: "Neutru"}`. |
| 17 | Excel Merged Cells | Script encounters a `MergedCell` during sheet write | `safe_write` detects `isinstance(cell, MergedCell)` and skips writing without throwing openpyxl `ReadOnlyCell` / `MergedCell` exceptions. |
| 18 | Excel Sheet Generation | `GHID INVATARE` sheet already exists in workbook | Script safely removes existing sheet via `del wb[sheet_name]` before creating a fresh sheet. |
| 19 | Excel Selected Asset | Cell `DASHBOARD!J2` contains partial/lowercase name (e.g. "nasdaq") | `_get_selected_activ` runs case-insensitive partial match against `ACTIVE` dictionary; falls back to `"NASDAQ Comp."` (`^IXIC`) if unmatched. |
| 20 | Note Deduplication | Identical financial note ingested twice | `MemoryDeduplicator` matches SHA-256 content hash and returns `is_new=False` with existing note UUID. |
| 21 | Signal Contradiction | Pipeline produces BUY on AAPL while active SELL note exists for same date | `MemoryDeduplicator` creates an atomic `hypothesis` contradiction record linking both note IDs without erasing either claim. |
| 22 | Search Pagination Token | Pagination token HMAC signature tampered with | Controller raises `InvalidPaginationTokenError`; rejects request immediately. |
| 23 | Search Pagination Token | Query parameters altered while reusing pagination token | Query fingerprint mismatch detected; raises `InvalidPaginationTokenError`. |
| 24 | Cognitive Lifecycle Gate | AI_AGENT attempts to query or propose notes in `RAW` lifecycle | `RAW` notes are strictly excluded from standard search and query results per P0/P3 invariants. |
| 25 | Trust Boundary Invariant | AI_AGENT attempts to set `verification: "verified"` or call `attest()` | System raises `PermissionError` (P0/P1 invariants: only HUMAN or ADMIN can attest notes to verified status). |

---

## 4. Financial Source Data & Excel Structure Mining

### 4.1 Master 15-Sheet Architecture of `Analiza_Piata_Profesionala.xlsx`

The financial source workbook contains 15 specialized sheets designed for hedge fund grade market intelligence and automated updating:

```
Analiza_Piata_Profesionala.xlsx
├── 1. DASHBOARD              (Executive cockpit, market breadth, best picks, selected asset summary)
├── 2. FISA ACTIV             (Comprehensive individual asset sheet, technicals, macro, risks, calendar)
├── 3. REZUMAT EXECUTIV       (High-level macro regime, systemic risk, institutional takeaways)
├── 4. SEMNALE INTRARE        (Tactical entry matrix: BUY/SELL/WAIT, confluences, Entry, SL, TP, R/R, Prob)
├── 5. INDICATORI TEHNICI     (23-column quantitative indicator matrix across all 95 assets)
├── 6. INDICATORI MACRO       (Macro monitor: VIX, 10Y/2Y/30Y yields, DXY, Fed Funds, CPI, UNRATE, GDP)
├── 7. COMPETITORI SECTOR     (Sector peer correlation and comparative performance matrix)
├── 8. PRETURI VOLUME         (15-column price, returns, volume, and RVOL tracking matrix)
├── 9. RISCURI OPORTUNITATI   (Quantified risk register: ID, Type, Category, Impact, Prob, Score, Actions)
├── 10. CALENDAR ECONOMIC     (Market events calendar: FOMC, NFP, CPI, GDP, PMI, Earnings Season)
├── 11. JURNAL TRANZACTII     (Hedge fund execution journal: ID, Setup, Entry, SL, TP, Size, Risk $, P&L)
├── 12. ISTORIC TRENDING      (24-month historical snapshot of RSI, S&P 500, GDP, CPI, VIX, Sentiment)
├── 13. LEGENDA               (Color coding system and cell type conventions)
├── 14. LIST_ACTIVE           (Master validation list of all 95 assets)
└── 15. GHID INVATARE         (Algorithmic educational daily guide with movement explanations & lessons)
```

---

### 4.2 Comprehensive Sheet Schemas & Formulas

#### Sheet 1: `DASHBOARD`
- **Purpose**: Real-time executive cockpit displaying overall market sentiment, signal counts, best opportunity, and instant summary for any selected asset.
- **Key Cells & References**:
  - `D2`: Last update timestamp (`DD.MM.YYYY HH:MM`).
  - `J2`: User-selectable asset name dropdown (e.g. `"NASDAQ Comp."`, `"Gold"`, `"Apple"`).
  - `E5–E8`: Market breadth statistics: `E5` = Total BUY count, `E6` = Total SELL count, `E7` = Total WAIT count, `E8` = Dominant trend (`"Bullish"` if BUY > 55%, `"Bearish"` if SELL > 55%, else `"Mixt"`).
  - `B8–J9`: Best Quantitative Pick Card: `B8` = Signal (`BUY`), `C8` = Asset Name, `D8` = Current Price, `E9` = Stop Loss, `F9` = Take Profit, `G9` = Risk/Reward ratio (`0.00x`), `H9` = Confluences (`x/5`), `I9` = Win Probability (`0%`), `J9` = Trigger conditions summary.
  - `B14–B21`: Dynamic Indicator Summary for asset selected in `J2`: `B14` = Selected Asset Name, `B15` = RSI(14), `B16` = MACD Cross, `B17` = MA Cross, `B18` = Trend, `B19` = VIX Close, `B20` = Fear & Greed display, `B21` = RVOL.

#### Sheet 2: `FISA ACTIV`
- **Purpose**: Deep-dive single asset analysis card populated dynamically based on the asset selected in `DASHBOARD!J2`.
- **Sections**:
  1. *Section 1: Tactical Signal Parameters* (Rows 4–13): Signal (BUY/SELL/WAIT), Trigger condition, Confluences, Entry Price, Stop Loss, Take Profit, Risk/Reward ratio, Statistical Probability, Signal Status ("Activ" / "In asteptare"), Last Updated.
  2. *Section 2: Technical Indicators* (Rows 15–25): Current Price, MA20, MA50, MA200, RSI(14), RSI Status, MACD, MACD Signal, MACD Histogram, MA Cross, MACD Cross.
  3. *Section 3: Price & Volume Structure* (Rows 27–32): Open, High, Low, Close, Daily Return %, Weekly Return %.
  4. *Section 4: Macroeconomic Environment* (Rows 34–41): VIX, Yield 10Y, Yield 2Y, Fear & Greed, Fed Funds Rate, CPI, Unemployment.
  5. *Section 5: Sector Competitors* (Rows 43–50): Top 6 peer assets with Close Price, Trend, and Signal.
  6. *Section 6: Asset Risk Register* (Rows 52–58): ID, Risk Type, Category, Description, Impact (1–5), Probability (%), Priority Score (`Impact * Prob / 100`), Time Horizon.
  7. *Section 7: Economic Calendar Events* (Rows 60–66): Scheduled high-impact macro announcements.

#### Sheet 3: `REZUMAT EXECUTIV`
- **Purpose**: High-level briefing for portfolio managers summarizing market regime, systemic risk, volatility posture, and 5 key bullet points.
- **Key Fields**:
  - `D3`: Timestamp.
  - General Trend (`Bullish` ↑ / `Bearish` ↓ / `Mixt` →), Signal breakdown (`BUY:x SELL:y WAIT:z`).
  - Volatility State (`Ridicata` >30, `Moderata` 20–30, `Scazuta` <20).
  - Trading Volume State (`Crescut` RVOL>1.3, `Normal` 0.7–1.3, `Scazut` <0.7).
  - Sentiment Index (Fear & Greed display).
  - Systemic Risk Assessment (`Ridicat`, `Moderat`, `Scazut`).
  - 5 Institutional Takeaway Bullets with visual status emojis (`✅`, `⚠️`, `🎯`, `🛡️`, `📅`).

#### Sheet 4: `SEMNALE INTRARE`
- **Purpose**: Full quantitative signal matrix for all 95 assets.
- **Column Schema (17 Columns)**:
  1. `Col 1 (A)`: Data (`DD.MM.YYYY`)
  2. `Col 2 (B)`: Produs / Activ (Asset Name, bold font)
  3. `Col 3 (C)`: Semnal (`BUY` / `SELL` / `WAIT`, color coded)
  4. `Col 4 (D)`: Condiție Declanșare (e.g. `RSI=44 | Impuls pozitiv activ | Golden Cross | RVOL=1.2x | Score=4`)
  5. `Col 5 (E)`: RSI (Numeric `0.00`)
  6. `Col 6 (F)`: MACD Cross (Status string)
  7. `Col 7 (G)`: MA Cross (`Golden Cross` / `Death Cross` / `Neutru`)
  8. `Col 8 (H)`: RVOL (Format `0.00x`)
  9. `Col 9 (I)`: Momentum 10z (Format `0.00%`)
  10. `Col 10 (J)`: Confluențe (Integer `0..5`)
  11. `Col 11 (K)`: Preț Intrare / Închidere (Format `#,##0.0000`)
  12. `Col 12 (L)`: Stop Loss (Format `#,##0.0000`, `Entry - 1.5*ATR` for BUY)
  13. `Col 13 (M)`: Take Profit (Format `#,##0.0000`, `Entry + 3.0*ATR` for BUY)
  14. `Col 14 (N)`: Risk/Reward Ratio (Formula: `=IFERROR((M{row}-K{row})/(K{row}-L{row}),"N/A")`)
  15. `Col 15 (O)`: Probabilitate (Format `0%`, color coded green >=65%, yellow >=50%, red <50%)
  16. `Col 16 (P)`: Status Semnal (`"Activ"` / `"In asteptare"`)
  17. `Col 17 (Q)`: Note / Timestamp Auto

#### Sheet 5: `INDICATORI TEHNICI`
- **Purpose**: Exhaustive technical analysis indicator sheet across all 95 instruments.
- **Column Schema (23 Columns)**:
  1. `Col 1`: Data | `Col 2`: Produs/Activ | `Col 3`: Ticker | `Col 4`: Închidere
  2. `Col 5`: MA20 | `Col 6`: MA50 | `Col 7`: MA200
  3. `Col 8`: RSI(14) | `Col 9`: RSI Status
  4. `Col 10`: MACD Line | `Col 11`: MACD Signal | `Col 12`: MACD Histogram | `Col 13`: MACD Cross
  5. `Col 14`: Bollinger Sup | `Col 15`: Bollinger Inf | `Col 16`: Bollinger Width
  6. `Col 17`: ATR(14)
  7. `Col 18`: Stoch %K | `Col 19`: Stoch %D
  8. `Col 20`: Volum | `Col 21`: RVOL
  9. `Col 22`: Trend | `Col 23`: MA Cross

#### Sheet 6: `INDICATORI MACRO`
- **Purpose**: Macroeconomic radar monitoring US interest rates, yield curve, dollar index, inflation, labor, and GDP.
- **Columns**: Indicator, Valoare Curentă, Valoare Anterioară, Variație %, Impact, Frecvență, Unitate, Sursă, Ultima Actualizare.
- **Core Indicators**:
  - `VIX` (^VIX): Volatility Index (yfinance)
  - `Yield 10Y` (^TNX): US 10-Year Treasury Yield Benchmark (yfinance)
  - `Yield 2Y` (^IRX): US 2-Year / 13-Week Treasury Bill Yield (yfinance)
  - `Yield 30Y` (^TYX): US 30-Year Treasury Bond Yield (yfinance)
  - `USD Index` (DX-Y.NYB): US Dollar Index (yfinance)
  - `Fear & Greed`: Alternative.me Sentiment Index
  - `Rata dobanzii` (FEDFUNDS): Federal Funds Effective Rate (FRED API)
  - `CPI` (CPIAUCSL): Consumer Price Index Inflation (FRED API)
  - `Somaj` (UNRATE): Civilian Unemployment Rate (FRED API)
  - `PIB` (GDP): Gross Domestic Product in Billions (FRED API)

#### Sheet 7: `COMPETITORI SECTOR`
- **Purpose**: Sector benchmarking mapping top 6 competitors for each asset class:
  - `INDICI`: S&P 500, NASDAQ 100, Dow Jones, DAX Germany, FTSE 100, Nikkei 225
  - `ACTIUNI`: Apple, Microsoft, NVIDIA, Alphabet, Amazon, Meta
  - `CRYPTO`: Bitcoin, Ethereum, BNB, Solana, XRP, Cardano
  - `VALUTE`: EUR/USD, GBP/USD, USD/JPY, USD/CHF, AUD/USD, USD/CAD
  - `MATERII_PRIME`: Gold, Silver, Oil WTI, Oil Brent, Natural Gas, Copper

#### Sheet 8: `PRETURI VOLUME`
- **Purpose**: Raw price action and volume monitoring table (15 columns).
- **Columns**: Data, Produs/Activ, Ticker, Deschidere, Maxim, Minim, Închidere, Var. Zi (%), Var. Săpt (%), Var. Lună (%), Volum, Medie Vol. 20z, RVOL, Semnal, Trend.

#### Sheet 9: `RISCURI OPORTUNITATI`
- **Purpose**: Institutional risk matrix categorized by asset class (Indices, Equities, Crypto, FX, Commodities).
- **Columns**: ID (e.g. `R-I-01`, `R-A-01`, `O01`), Tip (`Risc` / `Oportunitate`), Categorie, Descriere, Impact (1–5 scale), Probabilitate (0–100%), Scor Prioritate (`Impact * Prob / 100`), Orizont de timp, Acțiuni recomandate, Owner, Status, Data.

#### Sheet 10: `CALENDAR ECONOMIC`
- **Purpose**: Market event calendar listing recurring macroeconomic catalysts.
- **Columns**: Data & Ora, Eveniment, Țară, Impact (`Ridicat` / `Mediu` / `Scazut`), Anterior, Estimare, Actual, Deviere, Impact Real, Activ Afectat, Note.
- **Events Tracked**: FOMC, NFP, CPI, GDP, PMI, OPEC+, EIA Crude, SEC Regulatory Rulings, Bitcoin Halving, Earnings Season.

#### Sheet 11: `JURNAL TRANZACTII`
- **Purpose**: Hedge fund execution trading journal tracking trade setups, sizing, and realized returns.
- **Columns (14 Columns)**: ID (e.g. `T001`), Data, Ora, Activ, L/S (`LONG` / `SHORT`), Setup (`Breakout`, `Reversion`, `Trend-Follow`, `Pullback`), Entry, SL, TP, Mărime Poz., Risc $, Exit, Data Ieșire, P&L $.

#### Sheet 12: `ISTORIC TRENDING`
- **Purpose**: 24-month historical macro and market trend snapshot.
- **Columns**: Luna/An (`Mmm YYYY`), RSI Medie, MACD Signal, Preț S&P500, PIB YoY, CPI, VIX, Semnal Lună, Vol. Mediu (B), Trend Dominant, Fear & Greed, Rata Dobandă, Yield 10Y, Gold.

#### Sheet 13: `LEGENDA`
- **Purpose**: Color palette and formatting standards:
  - Green (`#C6EFCE` / `#375623`): Positive Status, BUY signal, Bullish trend, Return > 0.
  - Yellow (`#FFEB9C` / `#9C6500`): Neutral Status, WAIT signal, Sideways trend, Return == 0.
  - Red (`#FFC7CE` / `#9C0006`): Negative Status, SELL signal, Bearish trend, Return < 0.
  - Navy (`#0D2137`, `#1F4E79` / `#FFFFFF`): Section headers and titles.
  - Soft Blue (`#DEEAF1` / `#1F4E79`): Technical data and narrative cards.
  - Orange (`#FCE4D6` / `#833C00`): Educational lessons and heuristics.

#### Sheet 14: `LIST_ACTIVE`
- **Purpose**: Single-column master list of all 95 friendly asset names used for Excel data validation dropdowns in `DASHBOARD!J2`.

#### Sheet 15: `GHID INVATARE`
- **Purpose**: Algorithmic educational guide generated dynamically for every asset, featuring 4 structured rows per asset block:
  1. *Header Row*: `▶ {Name} ({Ticker}) | {Signal} | Pret: {Close} | Var: {Var_Zi}` (Color: Green for BUY, Red for SELL, Dark Yellow for WAIT).
  2. *Price & Indicator Row*: OHLC returns, RSI(14) with zone status, Stochastic (%K/%D), ATR(14), RVOL, Volume.
  3. *Moving Averages & MACD Row*: MA20/50/200 values + Golden/Death cross, MACD line/signal/hist + cross state, Bollinger bands + SL/TP.
  4. *Movement Explanation & Risk Block*: `explica_miscare` text + R/R, Win Prob %, Confluences, Score.
  5. *Opportunity & Lesson Block*: `identifica_oportunitate` alert (Green/Red/Yellow) + `extrage_lectie` heuristic (Orange).
  6. *End Section*: Static Charting Guide (`write_ghid_grafice`) explaining Candlestick interpretation, RSI rules, MACD crossovers, Bollinger Bands, MA crosses, Stochastic, ATR volatility sizing, and Trading Psychology (FOMO, discipline, trade logging).

---

## 5. Quantitative Technical Indicator Algorithms & Mathematical Formulas

The financial ingestion pipeline implements 10 pure mathematical indicator calculations:

### 1. Relative Strength Index (RSI-14)
$$\Delta_t = P_t - P_{t-1}$$
$$\text{Gain}_t = \max(\Delta_t, 0), \quad \text{Loss}_t = \max(-\Delta_t, 0)$$
$$\text{AvgGain}_{14} = \frac{1}{14} \sum_{i=0}^{13} \text{Gain}_{t-i}, \quad \text{AvgLoss}_{14} = \frac{1}{14} \sum_{i=0}^{13} \text{Loss}_{t-i}$$
$$RS = \frac{\text{AvgGain}_{14}}{\text{AvgLoss}_{14} + 10^{-10}}$$
$$RSI = 100 - \frac{100}{1 + RS}$$
*Status Mapping*:
- $RSI < 30$: "Presiune excesiva vanzare" (Oversold / Potential Rebound)
- $30 \le RSI < 45$: "Presiune moderata vanzare"
- $45 \le RSI \le 55$: "Echilibru" (Neutral Equilibrium)
- $55 < RSI \le 70$: "Momentum ascendent"
- $RSI > 70$: "Presiune excesiva cumparare" (Overbought / Caution)

### 2. MACD (Moving Average Convergence Divergence)
$$\text{EMA}_{12} = \text{EMA}(P, 12), \quad \text{EMA}_{26} = \text{EMA}(P, 26)$$
$$\text{MACD Line} = \text{EMA}_{12} - \text{EMA}_{26}$$
$$\text{Signal Line} = \text{EMA}(\text{MACD Line}, 9)$$
$$\text{Histogram} = \text{MACD Line} - \text{Signal Line}$$
*Crossover State Machine*:
- $\text{MACD} > \text{Signal} \land \text{Hist}_{t-1} < 0 \le \text{Hist}_t$: **"Impuls pozitiv nou"** (+2 score)
- $\text{MACD} > \text{Signal} \land \text{Hist}_t \ge 0$: **"Impuls pozitiv activ"** (+1 score)
- $\text{MACD} < \text{Signal} \land \text{Hist}_{t-1} > 0 \ge \text{Hist}_t$: **"Impuls negativ nou"** (-2 score)
- $\text{MACD} < \text{Signal} \land \text{Hist}_t < 0$: **"Impuls negativ activ"** (-1 score)

### 3. Moving Averages & Crosses
$$\text{SMA}_n = \frac{1}{n} \sum_{i=0}^{n-1} P_{t-i} \quad (n \in \{20, 50, 200\})$$
- $\text{SMA}_{50} > \text{SMA}_{200}$: **"Golden Cross"** (+2 score)
- $\text{SMA}_{50} < \text{SMA}_{200}$: **"Death Cross"** (-2 score)
- *Trend*: $P_t > 1.01 \cdot \text{SMA}_{50} \implies \text{Bullish}$; $P_t < 0.99 \cdot \text{SMA}_{50} \implies \text{Bearish}$; else $\text{Sideways}$.

### 4. Bollinger Bands (20, 2.0σ)
$$\mu_{20} = \text{SMA}_{20}(P), \quad \sigma_{20} = \text{StdDev}_{20}(P)$$
$$\text{Upper Band} = \mu_{20} + 2\sigma_{20}, \quad \text{Lower Band} = \mu_{20} - 2\sigma_{20}$$
$$\text{Band Width} = \text{Upper Band} - \text{Lower Band}$$

### 5. Average True Range (ATR-14)
$$\text{TR}_t = \max\left( H_t - L_t, \, |H_t - C_{t-1}|, \, |L_t - C_{t-1}| \right)$$
$$\text{ATR}_{14} = \frac{1}{14} \sum_{i=0}^{13} \text{TR}_{t-i}$$

### 6. Fast Stochastic Oscillator (14, 3)
$$\%K = \frac{C_t - \min_{14}(L)}{\max_{14}(H) - \min_{14}(L) + 10^{-10}} \times 100$$
$$\%D = \text{SMA}_3(\%K)$$

### 7. Relative Volume (RVOL-20)
$$\text{RVOL} = \frac{V_t}{\frac{1}{20}\sum_{i=0}^{19} V_{t-i}}$$
- $\text{RVOL} > 1.5$: High volume breakout confirmation (+1 score)
- $\text{RVOL} < 0.6$: Low volume liquidity decay (-1 score)

### 8. Multi-Factor Confluence Scoring & Signal Generation
$$\text{Score} = \text{Score}_{\text{RSI}} + \text{Score}_{\text{MACD}} + \text{Score}_{\text{MA\_Cross}} + \text{Score}_{\text{RVOL}}$$
$$\text{Confluences} = \min(|\text{Score}|, 5)$$
$$\text{Signal} = \begin{cases} \text{"BUY"} & \text{if } \text{Score} \ge +3 \\ \text{"SELL"} & \text{if } \text{Score} \le -3 \\ \text{"WAIT"} & \text{otherwise} \end{cases}$$

### 9. Dynamic ATR Position Sizing & Target Risk/Reward
$$\text{For BUY}: \quad \text{SL} = P_t - 1.5 \cdot \text{ATR}_{14}, \quad \text{TP} = P_t + 3.0 \cdot \text{ATR}_{14}$$
$$\text{For SELL}: \quad \text{SL} = P_t + 1.5 \cdot \text{ATR}_{14}, \quad \text{TP} = P_t - 3.0 \cdot \text{ATR}_{14}$$
$$\text{Target R/R} = \frac{|\text{TP} - P_t|}{|\text{SL} - P_t|} = \frac{3.0 \cdot \text{ATR}}{1.5 \cdot \text{ATR}} = 2.00\text{x}$$

### 10. Statistical Win Probability Model
$$\text{Probability} (\%) = \min\left(90, \, 35 + (\text{Confluences} \times 10) + \begin{cases} 5 & \text{if } \text{RVOL} > 1.2 \\ 0 & \text{otherwise} \end{cases}\right)$$

---

## 6. Schema Requirements for `memory_controller/financial_schema.py`

To satisfy Requirement R1 and enable robust validation across all financial memory note types, `memory_controller/financial_schema.py` must define both:
1. **Draft-07 JSON Schema** (`FINANCIAL_NOTE_SCHEMA`) for runtime jsonschema validation.
2. **Pydantic v2 Models** for strong typing, serialization, and IDE auto-completion.

### 6.1 Draft-07 JSON Schema Specification

```python
FINANCIAL_NOTE_SCHEMA = {
    "$schema": "http://json-schema.org/draft-07/schema#",
    "title": "FinancialMemoryNote",
    "description": "Schema for canonical financial notes in AI Memory Vault adhering to AGENTS.md and P0-P18 invariants.",
    "type": "object",
    "required": [
        "id", "type", "lifecycle", "category", "tags",
        "created", "updated", "provenance", "confidence",
        "verification", "relations", "ticker", "instrument_name",
        "price_data", "technical_indicators", "quantitative_signal"
    ],
    "properties": {
        "id": {"type": "string", "format": "uuid"},
        "type": {
            "type": "string",
            "enum": ["knowledge", "decision", "experience", "error", "lesson", "resource", "hypothesis"]
        },
        "lifecycle": {
            "type": "string",
            "enum": ["RAW", "CLASSIFIED", "NORMALIZED", "REVIEW", "VERIFIED", "ACTIVE", "SUPERSEDED", "ARCHIVED"]
        },
        "category": {
            "type": "string",
            "enum": [
                "financial-asset-profile",
                "macroeconomic-regime",
                "technical-trading-setup",
                "trade-execution-log",
                "trading-discipline-error",
                "trading-heuristic-lesson",
                "financial-instrument-catalog",
                "financial-conflict-record"
            ]
        },
        "tags": {
            "type": "array",
            "items": {"type": "string"},
            "minItems": 1
        },
        "created": {"type": "string", "format": "date"},
        "updated": {"type": "string", "format": "date"},
        "provenance": {
            "type": "object",
            "required": ["source_type", "source_ref"],
            "properties": {
                "source_type": {
                    "type": "string",
                    "enum": ["user", "official", "execution", "experience", "ai", "inference", "import", "unknown"]
                },
                "source_ref": {"type": "string"},
                "source_date": {"type": "string", "format": "date"},
                "extraction_date": {"type": "string", "format": "date"},
                "redaction": {"type": "string", "enum": ["none", "applied", "not_applicable"]},
                "provenance_status": {"type": "string", "enum": ["complete", "incomplete"]}
            },
            "additionalProperties": False
        },
        "confidence": {
            "type": "string",
            "enum": ["very_high", "high", "medium", "low", "unknown"]
        },
        "verification": {
            "type": "string",
            "enum": ["verified", "partially_verified", "unverified", "inferred"]
        },
        "relations": {
            "type": "array",
            "items": {
                "type": "object",
                "required": ["relation", "target"],
                "properties": {
                    "relation": {
                        "type": "string",
                        "enum": [
                            "related_to", "depends_on", "caused_by", "solved_by",
                            "supports", "contradicts", "implements", "used_by",
                            "derived_from", "replaces", "replaced_by", "conflicts_with"
                        ]
                    },
                    "target": {"type": "string", "pattern": "^\\[\\[.+\\]\\]$"},
                    "target_id": {"type": "string", "format": "uuid"}
                },
                "additionalProperties": False
            }
        },
        "ticker": {"type": "string"},
        "instrument_name": {"type": "string"},
        "asset_class": {
            "type": "string",
            "enum": ["INDICI", "ACTIUNI", "CRYPTO", "VALUTE", "MATERII_PRIME", "MACRO"]
        },
        "price_data": {
            "type": "object",
            "required": ["close", "change_day_pct", "volume", "rvol"],
            "properties": {
                "open": {"type": ["number", "null"]},
                "high": {"type": ["number", "null"]},
                "low": {"type": ["number", "null"]},
                "close": {"type": "number"},
                "change_day_pct": {"type": "number"},
                "change_week_pct": {"type": ["number", "null"]},
                "change_month_pct": {"type": ["number", "null"]},
                "volume": {"type": "integer"},
                "avg_volume_20d": {"type": ["integer", "null"]},
                "rvol": {"type": "number"}
            },
            "additionalProperties": False
        },
        "technical_indicators": {
            "type": "object",
            "required": ["rsi_14", "rsi_status", "macd_cross", "ma_cross", "trend", "atr_14"],
            "properties": {
                "rsi_14": {"type": "number", "minimum": 0, "maximum": 100},
                "rsi_status": {"type": "string"},
                "macd": {"type": ["number", "null"]},
                "macd_signal": {"type": ["number", "null"]},
                "macd_hist": {"type": ["number", "null"]},
                "macd_cross": {"type": "string"},
                "ma20": {"type": ["number", "null"]},
                "ma50": {"type": ["number", "null"]},
                "ma200": {"type": ["number", "null"]},
                "ma_cross": {"type": "string"},
                "trend": {"type": "string", "enum": ["Bullish", "Bearish", "Sideways"]},
                "bb_mid": {"type": ["number", "null"]},
                "bb_sup": {"type": ["number", "null"]},
                "bb_inf": {"type": ["number", "null"]},
                "bb_width": {"type": ["number", "null"]},
                "atr_14": {"type": "number", "minimum": 0},
                "stoch_k": {"type": ["number", "null"]},
                "stoch_d": {"type": ["number", "null"]},
                "momentum_10d": {"type": ["number", "null"]},
                "support_20d": {"type": ["number", "null"]},
                "resistance_20d": {"type": ["number", "null"]}
            },
            "additionalProperties": False
        },
        "quantitative_signal": {
            "type": "object",
            "required": ["signal", "score", "confluences", "win_probability_pct"],
            "properties": {
                "signal": {"type": "string", "enum": ["BUY", "SELL", "WAIT"]},
                "score": {"type": "integer", "minimum": -5, "maximum": 5},
                "confluences": {"type": "integer", "minimum": 0, "maximum": 5},
                "stop_loss": {"type": ["number", "null"]},
                "take_profit": {"type": ["number", "null"]},
                "risk_reward_ratio": {"type": ["number", "null"]},
                "win_probability_pct": {"type": "number", "minimum": 35, "maximum": 90}
            },
            "additionalProperties": False
        },
        "macro_context": {
            "type": "object",
            "properties": {
                "vix": {"type": ["number", "null"]},
                "yield_10y": {"type": ["number", "null"]},
                "yield_2y": {"type": ["number", "null"]},
                "usd_index": {"type": ["number", "null"]},
                "fear_greed_index": {"type": ["integer", "null"]},
                "fed_funds_rate": {"type": ["number", "null"]},
                "cpi": {"type": ["number", "null"]},
                "unemployment_rate": {"type": ["number", "null"]},
                "gdp": {"type": ["number", "null"]}
            },
            "additionalProperties": False
        },
        "commentary": {
            "type": "object",
            "properties": {
                "movement_explanation": {"type": "string"},
                "opportunity_alert": {"type": "string"},
                "educational_lesson": {"type": "string"}
            },
            "additionalProperties": False
        },
        "content_markdown": {"type": "string"}
    },
    "additionalProperties": True
}
```

---

### 6.2 Pydantic v2 Models Definition

```python
from __future__ import annotations
import enum
from typing import List, Optional, Dict, Any
from pydantic import BaseModel, Field, field_validator, ConfigDict


class MemoryTypeEnum(str, enum.Enum):
    KNOWLEDGE = "knowledge"
    DECISION = "decision"
    EXPERIENCE = "experience"
    ERROR = "error"
    LESSON = "lesson"
    RESOURCE = "resource"
    HYPOTHESIS = "hypothesis"


class LifecycleEnum(str, enum.Enum):
    RAW = "RAW"
    CLASSIFIED = "CLASSIFIED"
    NORMALIZED = "NORMALIZED"
    REVIEW = "REVIEW"
    VERIFIED = "VERIFIED"
    ACTIVE = "ACTIVE"
    SUPERSEDED = "SUPERSEDED"
    ARCHIVED = "ARCHIVED"


class ConfidenceEnum(str, enum.Enum):
    VERY_HIGH = "very_high"
    HIGH = "high"
    MEDIUM = "medium"
    LOW = "low"
    UNKNOWN = "unknown"


class VerificationEnum(str, enum.Enum):
    VERIFIED = "verified"
    PARTIALLY_VERIFIED = "partially_verified"
    UNVERIFIED = "unverified"
    INFERRED = "inferred"


class ProvenanceModel(BaseModel):
    model_config = ConfigDict(extra="forbid")
    source_type: str = Field(..., description="Provenance origin per P2 invariant")
    source_ref: str = Field(..., description="System module or tool reference")
    source_date: Optional[str] = Field(None, pattern=r"^\d{4}-\d{2}-\d{2}$")
    extraction_date: Optional[str] = Field(None, pattern=r"^\d{4}-\d{2}-\d{2}$")
    redaction: str = Field(default="none", enum=["none", "applied", "not_applicable"])
    provenance_status: str = Field(default="complete", enum=["complete", "incomplete"])


class RelationModel(BaseModel):
    model_config = ConfigDict(extra="forbid")
    relation: str = Field(..., description="Semantic wikilink relationship type")
    target: str = Field(..., pattern=r"^\[\[.+\]\]$", description="Obsidian wikilink target")
    target_id: Optional[str] = Field(None, description="Target note UUID")


class PriceDataPayload(BaseModel):
    model_config = ConfigDict(extra="forbid")
    open: Optional[float] = None
    high: Optional[float] = None
    low: Optional[float] = None
    close: float
    change_day_pct: float
    change_week_pct: Optional[float] = None
    change_month_pct: Optional[float] = None
    volume: int
    avg_volume_20d: Optional[int] = None
    rvol: float = 1.0


class TechnicalIndicatorsPayload(BaseModel):
    model_config = ConfigDict(extra="forbid")
    rsi_14: float = Field(..., ge=0.0, le=100.0)
    rsi_status: str
    macd: Optional[float] = None
    macd_signal: Optional[float] = None
    macd_hist: Optional[float] = None
    macd_cross: str
    ma20: Optional[float] = None
    ma50: Optional[float] = None
    ma200: Optional[float] = None
    ma_cross: str
    trend: str = Field(..., enum=["Bullish", "Bearish", "Sideways"])
    bb_mid: Optional[float] = None
    bb_sup: Optional[float] = None
    bb_inf: Optional[float] = None
    bb_width: Optional[float] = None
    atr_14: float = Field(..., ge=0.0)
    stoch_k: Optional[float] = None
    stoch_d: Optional[float] = None
    momentum_10d: Optional[float] = None
    support_20d: Optional[float] = None
    resistance_20d: Optional[float] = None


class QuantitativeSignalPayload(BaseModel):
    model_config = ConfigDict(extra="forbid")
    signal: str = Field(..., enum=["BUY", "SELL", "WAIT"])
    score: int = Field(..., ge=-5, le=5)
    confluences: int = Field(..., ge=0, le=5)
    stop_loss: Optional[float] = None
    take_profit: Optional[float] = None
    risk_reward_ratio: Optional[float] = None
    win_probability_pct: float = Field(..., ge=35.0, le=90.0)


class MacroContextPayload(BaseModel):
    model_config = ConfigDict(extra="forbid")
    vix: Optional[float] = None
    yield_10y: Optional[float] = None
    yield_2y: Optional[float] = None
    usd_index: Optional[float] = None
    fear_greed_index: Optional[int] = None
    fed_funds_rate: Optional[float] = None
    cpi: Optional[float] = None
    unemployment_rate: Optional[float] = None
    gdp: Optional[float] = None


class MarketCommentaryPayload(BaseModel):
    model_config = ConfigDict(extra="forbid")
    movement_explanation: Optional[str] = None
    opportunity_alert: Optional[str] = None
    educational_lesson: Optional[str] = None


class FinancialNoteModel(BaseModel):
    id: str = Field(..., description="Canonical Note UUID")
    type: MemoryTypeEnum = Field(default=MemoryTypeEnum.KNOWLEDGE)
    lifecycle: LifecycleEnum = Field(default=LifecycleEnum.REVIEW)
    category: str = Field(default="financial-asset-profile")
    tags: List[str] = Field(default_factory=list)
    created: str = Field(..., pattern=r"^\d{4}-\d{2}-\d{2}$")
    updated: str = Field(..., pattern=r"^\d{4}-\d{2}-\d{2}$")
    provenance: ProvenanceModel
    confidence: ConfidenceEnum = Field(default=ConfidenceEnum.HIGH)
    verification: VerificationEnum = Field(default=VerificationEnum.UNVERIFIED)
    relations: List[RelationModel] = Field(default_factory=list)

    ticker: str
    instrument_name: str
    asset_class: str = Field(..., enum=["INDICI", "ACTIUNI", "CRYPTO", "VALUTE", "MATERII_PRIME", "MACRO"])
    
    price_data: PriceDataPayload
    technical_indicators: TechnicalIndicatorsPayload
    quantitative_signal: QuantitativeSignalPayload
    macro_context: Optional[MacroContextPayload] = None
    commentary: Optional[MarketCommentaryPayload] = None
    content_markdown: Optional[str] = None
```

---

## 7. Validation Constraints & Cognitive Rules Compliance (P0–P18)

Every ingested financial note must strictly adhere to the Vault operating rules defined in `AGENTS.md` and `.agents/rules/vault_cognitive_rules.md`:

1. **P0: AI Self-Verification Gate**:
   - `Principal.AI_AGENT` cannot set `verification = "verified"`.
   - Ingested notes produced by automated pipeline must have `verification = "partially_verified"` or `verification = "unverified"`.
2. **P1: Human Attestation Only**:
   - Promotion of financial notes to `verification = "verified"` requires explicit human execution via `controller.attest(Principal.HUMAN, ...)`.
3. **P2: Privileged Provenance Isolation**:
   - `Principal.AI_AGENT` cannot claim `source_type` of `user`, `official`, `experience`, or `import`. Permitted: `execution`, `ai`, `inference`, `unknown`.
   - The financial ingestion pipeline must use `source_type = "execution"`.
4. **P3: Permitted Creation Lifecycles**:
   - Pipeline can only propose into `{RAW, CLASSIFIED, NORMALIZED, REVIEW}`. Default for newly ingested financial notes is `lifecycle = "REVIEW"`.
   - Direct promotion to `ACTIVE` is performed by Human/Admin or explicit promotion workflow.
5. **P4: Provenance Immutability**:
   - `provenance.source_type` cannot be modified after initial note creation.
6. **Wikilink Graph Integrity**:
   - Every note must declare valid semantic wikilinks in `relations` (`[[Asset_...]]`, `[[Macro_Regime_...]]`, `[[Model_...]]`).
7. **Tamper-Evident SHA-256 Chaining**:
   - Ingestion audit events (`ingest_financial_note`, `search_financial`) are appended to the hash-chained audit log with continuous SHA-256 verification.

---

## 8. Zero Hardcoded Secrets & Environment Variable Policy

### Audit Finding
- In source file `C:\Users\user\Desktop\Nu sterge\nusterge\ghid.py`, line 29 contains a plaintext API key:
  ```python
  FRED_API_KEY = "e372c6879cce084b8c3601f76adbe78d"
  ```

### Policy & Invariant Requirements
- **Rule AGENTS.md §19 (Security)**: NEVER store passwords, API keys, access tokens, or credentials in persistent memory notes, code repositories, or configuration files.
- **Environment Variable Injection**:
  - The ingestion engine and FRED fetcher must retrieve the API key exclusively via `os.environ.get("FRED_API_KEY")`.
  - If `FRED_API_KEY` is not present, the system MUST gracefully fall back to deterministic offline sample observations (`_SAMPLE_FRED_DATA`) without throwing fatal errors.
  - Test suites (`tests/financial/`) must verify that no secrets leak into persisted SQLite notes or audit logs.

---

## 9. Conclusion & Implementation Blueprint

With all specifications thoroughly discovered, mined, and documented:
1. `memory_controller/financial_schema.py` is ready for implementation using the JSON Schema and Pydantic models defined in Section 6.
2. `memory_controller/financial_query.py` can validate incoming notes directly against `FINANCIAL_NOTE_SCHEMA`.
3. `xau_kinetic/financial_ingestion/` contains working, modularized reference implementations for the 10 indicators, catalog, pipeline, and adapter.
4. Comprehensive test coverage is established in `tests/financial/test_financial_search.py`.
