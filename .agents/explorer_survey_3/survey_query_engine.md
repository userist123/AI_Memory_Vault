# Architecture & Design Report: Financial Ingestion Pipeline, Multi-Layered Query Engine & Verification Strategy

**Author**: Explorer Survey 3 (`teamwork_preview_explorer`)  
**Date**: 2026-08-26  
**Target Repository**: `AI_Memory_Vault_CODEX_READY`  
**Integrity Mode**: Development / Enterprise Cognitive OS (P0–P18 Invariants)  
**Deliverable Document**: `survey_query_engine.md`  

---

## Executive Summary

This report establishes the complete architectural specification, data schemas, multi-layered retrieval algorithms, REST API integration contracts, and verification test harnesses for integrating financial quantitative intelligence into the **AI Memory Vault (`AI_Memory_Vault_CODEX_READY`)**.

The target domain source data encompasses:
1. `ghid.py`: A 1,954-line Python algorithmic guide and execution system defining 95 financial instruments (14 indices, 30 equities, 25 cryptocurrencies, 12 FX pairs, 14 commodities), 5 macroeconomic benchmark tickers (^VIX, ^TNX, ^IRX, ^TYX, DX-Y.NYB), 4 St. Louis Fed FRED series (FEDFUNDS, CPIAUCSL, UNRATE, GDP), a 30-factor structured risk matrix, competitor relationships, economic calendar events, and technical indicator scoring engines.
2. `Analiza_Piata_Profesionala.xlsx`: An enterprise financial workbook (1.6 MB) containing multi-asset technical dashboards, macro regime correlations, conditional formatting rules, trade execution journals, and learning guides.

The architecture strictly adheres to **AGENTS.md**, **PROJECT.md**, **Canonical Frontmatter (Draft 7)**, and the **P0–P18 Cognitive Trust Boundary Invariants**, guaranteeing zero hardcoded secret leaks, deterministic deduplication, contradiction handling, and tamper-evident SHA-256 audit logging.

---

## 1. Ingestion Pipeline Architecture

```
+-----------------------------------------------------------------------------------------------+
|                                      INGESTION PIPELINE                                       |
+-----------------------------------------------------------------------------------------------+
|  RAW SOURCES:                                                                                 |
|  - C:\Users\user\Desktop\Nu sterge\nusterge\ghid.py                                         |
|  - C:\Users\user\Desktop\Nu sterge\nusterge\Analiza_Piata_Profesionala.xlsx                  |
+-----------------------------------------------------------------------------------------------+
                                                |
                                                v
+-----------------------------------------------------------------------------------------------+
|  STAGE 1: 06_INBOX/RAW_IMPORTS/ (Immutable Raw Archive)                                        |
|  - 06_INBOX/RAW_IMPORTS/financial/ghid.py                                                     |
|  - 06_INBOX/RAW_IMPORTS/financial/Analiza_Piata_Profesionala.xlsx                              |
|  - Raw files preserved with lifecycle: RAW (never indexed as canonical memory)                |
+-----------------------------------------------------------------------------------------------+
                                                |
                                                v
+-----------------------------------------------------------------------------------------------+
|  STAGE 2: SECRET SCRUBBER & SANITIZER (CI & Ingestion Gate)                                   |
|  - Redacts raw secrets (e.g. hardcoded FRED API key in ghid.py:29)                             |
|  - Mandates injection via environment variable: os.getenv("FRED_API_KEY")                      |
|  - Sanitizes user input and stripping dangerous control sequences                             |
+-----------------------------------------------------------------------------------------------+
                                                |
                                                v
+-----------------------------------------------------------------------------------------------+
|  STAGE 3: MULTI-MODAL PARSERS & AST EXTRACTORS                                                |
|  +--------------------------------------------+--------------------------------------------+  |
|  | Python AST & Static Analyzer (ghid.py)     | Excel Workbook Parser (openpyxl / pandas)  |  |
|  | - 95 Instrument Catalog Definitions        | - Sheet: Market Overview & Dashboards      |  |
|  | - 5 Macro Tickers & 4 FRED Series          | - Sheet: Risk Matrices & Competitor Maps   |  |
|  | - 30 Risk Library Items & Calendars        | - Sheet: Technical Setups & Trade Journals |  |
|  | - Indicator Formulas (RSI, MACD, S/R, etc.)| - Cell styles, PnL colors, and thresholds  |  |
|  +--------------------------------------------+--------------------------------------------+  |
+-----------------------------------------------------------------------------------------------+
                                                |
                                                v
+-----------------------------------------------------------------------------------------------+
|  STAGE 4: DEDUPLICATION & CONTRADICTION ENGINE (AGENTS.md §4, 9, 10)                           |
|  - Content Hash: SHA-256 normalized JSON digest                                               |
|  - Entity Key: {symbol}:{date}:{note_type}                                                    |
|  - Contradiction Detector: Flags opposing signals (BUY vs SELL) -> Emits hypothesis note      |
+-----------------------------------------------------------------------------------------------+
                                                |
                                                v
+-----------------------------------------------------------------------------------------------+
|  STAGE 5: CANONICAL MEMORY ADAPTER & NOTE EMITTER                                              |
|  - Applies Draft 7 JSON Schema & validate_frontmatter()                                       |
|  - Target Locations:                                                                          |
|    * 01_KNOWLEDGE/FINANCIAL/ASSETS/       (Asset profiles, e.g. Asset_SP500.md)               |
|    * 01_KNOWLEDGE/FINANCIAL/MACRO/        (Macro snapshots, e.g. Macro_Regime.md)             |
|    * 01_KNOWLEDGE/FINANCIAL/MODELS/       (Technical models, e.g. Model_Confluence.md)        |
|    * 04_MEMORY/FINANCIAL/DECISIONS/       (Actionable setups, type: decision)                 |
|    * 04_MEMORY/FINANCIAL/EXPERIENCES/     (Trade logs, type: experience)                      |
|    * 04_MEMORY/FINANCIAL/ERRORS/          (Discipline & risk breaches, type: error)           |
|    * 04_MEMORY/FINANCIAL/LESSONS/         (Trading heuristics & edges, type: lesson)          |
|    * 05_RESOURCES/FINANCIAL/              (Catalog index, type: resource)                     |
|  - Trust Invariants: lifecycle = REVIEW, provenance.source_type = "execution"|"import"        |
|  - Attestation Gate: AI cannot set verification = "verified" (defaults to "unverified")      |
+-----------------------------------------------------------------------------------------------+
```

### 1.1 Source Ingestion & File Mapping
1. **Raw Ingestion**:
   Raw artifacts are stored permanently in `06_INBOX/RAW_IMPORTS/financial/`. In accordance with `00_CORE/Memory_Protocol.md` and `AGENTS.md §8`, `RAW` data is never modified, never deleted, and never indexed directly into canonical knowledge.
2. **Canonical Transformation**:
   The `FinancialMemoryAdapter` reads raw inputs and generates structured Markdown notes with YAML frontmatter conforming to `99_SYSTEM/Canonical_Frontmatter.md`:
   - **Knowledge Notes (`01_KNOWLEDGE/FINANCIAL/ASSETS/`)**: 95 notes covering INDICI (14), ACTIUNI (30), CRYPTO (25), VALUTE (12), and MATERII_PRIME (14). Each contains ticker, sector, base currency, description, competitors, calendar events, risk factors, and `[[wikilinks]]`.
   - **Macroeconomic Notes (`01_KNOWLEDGE/FINANCIAL/MACRO/`)**: Snapshots combining FRED rates (FEDFUNDS, CPI, UNRATE, GDP), treasury yields (10Y, 2Y, 30Y), VIX volatility index, and Fear & Greed sentiment index.
   - **Model Notes (`01_KNOWLEDGE/FINANCIAL/MODELS/`)**: Multi-confluence mathematical models combining RSI (14), MACD (12, 26, 9), Moving Averages (20, 50, 200), Bollinger Bands (20, 2.0), ATR (14), Stochastic (%K 14, %D 3), Momentum (10), RVOL, Support/Resistance pivots, and Confluence Scoring (0–100 scale).
   - **Trade Decisions (`04_MEMORY/FINANCIAL/DECISIONS/`)**: Actionable setups with calculated Entry, Stop Loss, Take Profit 1/2, Risk-Reward ratio ($\ge 1.5\text{x}$), Win Probability, and Invalidation conditions.
   - **Trade Experiences (`04_MEMORY/FINANCIAL/EXPERIENCES/`)**: Historical execution journal tracking entry/exit prices, position sizing, realized P&L ($ / %), execution quality score, emotional discipline, and plan adherence.
   - **Trade Errors (`04_MEMORY/FINANCIAL/ERRORS/`)**: Post-mortem error analysis of risk breaches, FOMO entries, premature exits, and stop-loss alterations with Root Cause and Prevention rules per `AGENTS.md §16`.
   - **Trading Lessons (`04_MEMORY/FINANCIAL/LESSONS/`)**: Reusable heuristics and quantitative rules distilled from experiences and errors.
   - **Resource Catalog (`05_RESOURCES/FINANCIAL/`)**: Comprehensive asset dictionary and ticker registry linked to `[[Knowledge Graph Home]]`.

### 1.2 JSON Schema Specification: `memory_controller/financial_schema.py`
The schema enforces Draft 7 validation across all financial domain payloads:

```python
FINANCIAL_NOTE_SCHEMA = {
    "$schema": "http://json-schema.org/draft-07/schema#",
    "title": "FinancialNotePayload",
    "type": "object",
    "required": ["symbol", "category", "content"],
    "properties": {
        "symbol": {
            "type": "string",
            "description": "Canonical ticker symbol (e.g. ^GSPC, AAPL, BTC-USD, GC=F)"
        },
        "asset_name": {
            "type": "string",
            "description": "Common asset name (e.g. S&P 500, Gold, Apple)"
        },
        "category": {
            "type": "string",
            "enum": ["INDICI", "ACTIUNI", "CRYPTO", "VALUTE", "MATERII_PRIME", "MACRO", "MODEL", "JOURNAL"]
        },
        "sector": {"type": "string"},
        "currency_base": {"type": "string"},
        "timeframe": {"type": "string", "default": "1D"},
        "price_data": {
            "type": "object",
            "properties": {
                "open": {"type": "number"},
                "high": {"type": "number"},
                "low": {"type": "number"},
                "close": {"type": "number"},
                "volume": {"type": "number"},
                "change_pct": {"type": "number"}
            }
        },
        "indicators": {
            "type": "object",
            "properties": {
                "rsi_14": {"type": "number", "minimum": 0, "maximum": 100},
                "macd": {"type": "number"},
                "macd_signal": {"type": "number"},
                "macd_hist": {"type": "number"},
                "sma_20": {"type": "number"},
                "sma_50": {"type": "number"},
                "sma_200": {"type": "number"},
                "bollinger_upper": {"type": "number"},
                "bollinger_middle": {"type": "number"},
                "bollinger_lower": {"type": "number"},
                "atr_14": {"type": "number"},
                "stoch_k": {"type": "number"},
                "stoch_d": {"type": "number"},
                "momentum_10": {"type": "number"},
                "rvol": {"type": "number"},
                "support_level": {"type": "number"},
                "resistance_level": {"type": "number"},
                "confluence_score": {"type": "number", "minimum": 0, "maximum": 100}
            }
        },
        "trading_plan": {
            "type": "object",
            "properties": {
                "signal": {"type": "string", "enum": ["BUY", "SELL", "WAIT"]},
                "entry_zone": {"type": "string"},
                "stop_loss": {"type": "number"},
                "take_profit_1": {"type": "number"},
                "take_profit_2": {"type": "number"},
                "risk_reward_ratio": {"type": "number"},
                "win_probability_pct": {"type": "number"}
            }
        },
        "macro_context": {
            "type": "object",
            "properties": {
                "fed_funds_rate": {"type": "number"},
                "cpi_inflation": {"type": "number"},
                "unemployment_rate": {"type": "number"},
                "us10y_yield": {"type": "number"},
                "vix_index": {"type": "number"},
                "fear_and_greed_score": {"type": "number"}
            }
        },
        "risks": {
            "type": "array",
            "items": {
                "type": "object",
                "required": ["risk_id", "type", "description", "impact", "probability", "horizon"],
                "properties": {
                    "risk_id": {"type": "string"},
                    "type": {"type": "string"},
                    "description": {"type": "string"},
                    "impact": {"type": "integer", "minimum": 1, "maximum": 5},
                    "probability": {"type": "integer", "minimum": 1, "maximum": 100},
                    "horizon": {"type": "string"}
                }
            }
        },
        "competitors": {"type": "array", "items": {"type": "string"}},
        "calendar_events": {"type": "array", "items": {"type": "string"}},
        "tags": {"type": "array", "items": {"type": "string"}},
        "relations": {
            "type": "array",
            "items": {
                "type": "object",
                "required": ["relation", "target"],
                "properties": {
                    "relation": {"type": "string"},
                    "target": {"type": "string"},
                    "target_id": {"type": "string", "format": "uuid"}
                }
            }
        },
        "content": {"type": "string"}
    }
}
```

---

## 2. Multi-Layered Financial Query Engine Architecture

The query engine implements a 5-layer cascading pipeline designed for sub-50ms retrieval latency, high precision, and cognitive context bounds.

```
                          NATURAL LANGUAGE QUERY
                         e.g. "NASDAQ RSI divergence post 2025"
                                    |
                                    v
+-----------------------------------------------------------------------+
| LAYER 1: Financial Entity & Alias Resolver                            |
| - Extracts canonical symbols: ^NDX (from "nasdaq", "qqq", "tech")     |
| - Extracts categories: INDICI                                         |
| - Extracts indicators: "rsi", "divergence"                            |
| - Extracts temporal range: date_from = "2025-01-01"                   |
+-----------------------------------------------------------------------+
                                    |
                                    v
+-----------------------------------------------------------------------+
| LAYER 2: SQLite Structured & Temporal Filter                          |
| - Applies exact metadata filtering in SQLite WAL                      |
| - Checks lifecycles (ACTIVE, REVIEW only; strictly excludes RAW)      |
| - Enforces confidence floor (very_high > high > medium > low)          |
| - Enforces verification state & date range boundaries                 |
+-----------------------------------------------------------------------+
                                    |
                                    v
+-----------------------------------------------------------------------+
| LAYER 3: Hybrid Lexical BM25 & Dense Vector Scoring (RRF)             |
| - Okapi BM25 with Title (3x), Tags (2x), Category (2x) Boosting       |
| - Config-Gated Dense Vector Semantic Cosine Similarity                |
|   (Deterministic dense embedder fallback if external API is offline)  |
| - Reciprocal Rank Fusion: RRF = 1/(k + r_bm25) + 1/(k + r_vec)        |
| - Confidence & Verification Multipliers                               |
+-----------------------------------------------------------------------+
                                    |
                                    v
+-----------------------------------------------------------------------+
| LAYER 4: Wikilink Graph Spreading Activation Re-Ranking               |
| - Builds in-memory graph from [[wikilinks]], relations, correlations  |
| - ACT-R Spreading Activation with hop decay (decay = 0.6, max_hops=2)  |
| - Final Score = Hybrid_Score + (0.35 * Activation_Score)              |
+-----------------------------------------------------------------------+
                                    |
                                    v
+-----------------------------------------------------------------------+
| LAYER 5: Context Pack Builder & Progressive Disclosure                |
| - Formats context within Agent Context Budget (Soft / Hard token cap) |
| - Disclosure Levels: metadata_only | snippet | sections | full_doc    |
| - Cryptographic HMAC-SHA256 Pagination Token for stateful slicing     |
+-----------------------------------------------------------------------+
                                    |
                                    v
                          SCORED CONTEXT PACK
```

### 2.1 Layer Details & Algorithms

#### Layer 1: Financial Entity & Alias Resolver
- Covers all 95 assets, 5 macro tickers, 4 FRED series, and Romanian/English colloquial synonyms.
- Normalizes punctuation (`^`, `=`, `/`, `.`, `-`), handles case-insensitivity, and executes longest-match prefix resolution to prevent partial substring shadowing (e.g. "NASDAQ 100" vs "NASDAQ Comp").

#### Layer 2: Structured & Temporal Filter
- Filters stored notes by candidate metadata before expensive ranking.
- Excludes `RAW` notes unconditionally per `AGENTS.md §8` and `P0–P15`.
- Evaluates confidence thresholds: $\text{very\_high} (4) > \text{high} (3) > \text{medium} (2) > \text{low} (1) > \text{unknown} (0)$.

#### Layer 3: Hybrid BM25 + Vector Ranking with Reciprocal Rank Fusion (RRF)
- **Okapi BM25 Lexical Ranking**:
  $$\text{IDF}(q_i) = \ln\left(1 + \frac{N - \text{DF}(q_i) + 0.5}{\text{DF}(q_i) + 0.5}\right)$$
  $$\text{Score}_{\text{BM25}}(D, Q) = \sum_{q_i \in Q} \text{IDF}(q_i) \cdot \frac{f(q_i, D) \cdot (k_1 + 1)}{f(q_i, D) + k_1 \cdot \left(1 - b + b \cdot \frac{|D|}{\text{avgdl}}\right)} \cdot \text{boost}(q_i)$$
  Field weights: Title $\times 3$, Tags $\times 2$, Category $\times 2$, Content $\times 1$, Symbol Match $\times 1.5$.
- **Dense Vector Embedding & Config Gate**:
  - Gated by `ENABLE_VECTOR_SEARCH = os.getenv("ENABLE_VECTOR_SEARCH", "false").lower() == "true"`.
  - Offline / Fast Fallback: Deterministic 128-dimensional dense feature hasher and character 3-gram embedder with domain feature weights (gold, xau, sp500, nasdaq, vix, fedfunds, inflation, yield, breakout, confluence, bullish, bearish, pnl, risk).
  - Online Option: Local Ollama / sentence-transformers embedding when available.
- **Reciprocal Rank Fusion (RRF)** with $k = 60$:
  $$\text{RRF}(D) = \frac{1}{k + \text{rank}_{\text{BM25}}(D)} + \frac{1}{k + \text{rank}_{\text{vector}}(D)}$$
  $$\text{Score}_{\text{hybrid}}(D) = \text{RRF}(D) \cdot W_{\text{confidence}} \cdot W_{\text{verification}}$$

#### Layer 4: Wikilink Graph Spreading Activation
- Spreading activation spreads associative relevance through the knowledge graph:
  $$A_j = \max_{i \in \text{Frontier}} \left( A_i \cdot w_{ij} \cdot \delta^{\text{hop}+1} \right)$$
  where decay factor $\delta = 0.6$, maximum depth $= 2$ hops.
- Causal relations (`caused_by`, `resulted_in`, `triggers`) receive $1.5\text{x}$ edge weight; supersession links (`replaces`, `replaced_by`) receive $1.8\text{x}$ weight.

#### Layer 5: Progressive Disclosure & HMAC Pagination
- Enforces context budgets (e.g. 8,000 soft tokens / 12,000 hard tokens for subagents).
- Generates HMAC-SHA256 encrypted pagination tokens containing `query_fp`, `offset`, `agent_id`, `page_size`, and 15-minute expiration timestamp.

---

## 3. REST API Integration in `vault_api.py`

### 3.1 Endpoint Specifications

#### 1. Ingestion Endpoint: `POST /financial_note` (and `/api/v1/financial/ingest`)
- **Summary**: Ingests and validates a financial note proposal.
- **Security Invariants**: Principal defaults to `Principal.AI_AGENT`. `AI_AGENT` cannot claim `user` or `official` source types, and cannot self-attest `verification = "verified"` (defaults to `unverified`). Lifecycle is restricted to `REVIEW`.
- **Request Body (Pydantic)**:
```python
class FinancialNoteIngestRequest(BaseModel):
    symbol: str
    category: str
    content: str
    asset_name: Optional[str] = None
    sector: Optional[str] = None
    currency_base: Optional[str] = None
    timeframe: str = "1D"
    price_data: Optional[Dict[str, Any]] = None
    indicators: Optional[Dict[str, Any]] = None
    trading_plan: Optional[Dict[str, Any]] = None
    macro_context: Optional[Dict[str, Any]] = None
    risks: Optional[List[Dict[str, Any]]] = None
    competitors: Optional[List[str]] = None
    calendar_events: Optional[List[str]] = None
    tags: List[str] = []
    confidence: str = "medium"
    relations: List[Dict[str, Any]] = []
```
- **Response**:
```json
{
  "status": "success",
  "note_id": "4b68e980-60a6-4f76-9da8-7c8be379a2f1",
  "lifecycle": "REVIEW",
  "verification": "unverified",
  "message": "Financial note validated, hashed, and stored for review."
}
```
- **Error Statuses**:
  - `400 Bad Request`: Schema validation error or missing required fields.
  - `403 Forbidden`: Trust boundary violation (e.g. attempting to self-verify or forge privileged provenance).
  - `422 Unprocessable Entity`: Malformed JSON or type mismatch.

#### 2. Search Endpoint: `GET /search` (and `/api/v1/search`, `/memory/financial/search`)
- **Query Parameters**:
  - `q` / `query` (string, optional): Free-form search query (e.g. `"NASDAQ RSI divergence"`).
  - `symbol` (string, optional): Specific ticker override (e.g. `^NDX`, `AAPL`, `GC=F`).
  - `category` (string, optional): Category filter (`INDICI`, `ACTIUNI`, `CRYPTO`, `VALUTE`, `MATERII_PRIME`).
  - `min_confidence` (string, optional): Minimum confidence level (`low`, `medium`, `high`, `very_high`).
  - `verification_state` (string, optional): `verified`, `partially_verified`, `unverified`.
  - `date_from` / `date_to` (string, optional): ISO date range filter (`YYYY-MM-DD`).
  - `layer` (string, optional): `all`, `lexical`, `semantic`, `graph`.
  - `disclosure_level` (string, optional): `metadata`, `snippet`, `sections`, `full_document` (default: `metadata`).
  - `limit` / `page_size` (int, default: 10).
  - `page_token` (string, optional): Cryptographic HMAC-SHA256 token for pagination.
- **Response**:
```json
{
  "status": "success",
  "results": [
    {
      "id": "18f97fa6-6644-48cb-ab68-98eef62a9394",
      "title": "Asset_NASDAQ_100",
      "type": "knowledge",
      "lifecycle": "ACTIVE",
      "category": "INDICI",
      "confidence": "very_high",
      "verification": "partially_verified",
      "tags": ["finance", "^ndx", "indici", "tech"],
      "created": "2026-08-25",
      "summary": "US Tech Benchmark Index profile and confluence metrics."
    }
  ],
  "total_matched": 1,
  "next_page_token": null,
  "metadata": {
    "query": "nasdaq rsi",
    "extracted_symbols": ["^NDX"],
    "extracted_categories": ["INDICI"],
    "returned_count": 1
  }
}
```

#### 3. Structured POST Search: `POST /memory/financial/search`
- Accepts `FinancialSearchRequest` Pydantic model for rich programmatic queries.

---

## 4. Comprehensive Test Design & Verification Strategy

### 4.1 Test Hierarchy in `tests/financial/`

| Test Module | Target Functionality | Test Scenarios |
|---|---|---|
| `test_schema.py` | `FINANCIAL_NOTE_SCHEMA` & `validate_frontmatter` | 1. Validation of valid complete financial note.<br>2. Validation of valid minimal financial note.<br>3. Rejection of invalid UUID formats.<br>4. Rejection of invalid lifecycle / confidence / verification enums.<br>5. Provenance source_type restriction testing.<br>6. Rejection of unpermitted extra properties in frontmatter. |
| `test_query_engine.py` / `test_financial_search.py` | `MultiLayeredFinancialSearchEngine` & `FinancialQueryEngine` | 1. 95-asset & macro ticker alias resolution.<br>2. BM25 keyword matching for "NASDAQ", "RSI", "support", "confluence".<br>3. Structured filtering (symbol, category, confidence, verification, date).<br>4. Strict exclusion of `RAW` notes.<br>5. Vector search toggle (`ENABLE_VECTOR_SEARCH=True/False`) & dense fallback.<br>6. RRF hybrid rank computation.<br>7. Wikilink graph spreading activation boost.<br>8. HMAC-SHA256 pagination token validation and tamper rejection. |
| `test_ingestion_pipeline.py` | `FinancialIngestionPipeline` & `FinancialMemoryAdapter` | 1. Python AST parsing of `ghid.py` (95 assets, 5 macro, 4 FRED, risk library).<br>2. Excel parsing of `Analiza_Piata_Profesionala.xlsx`.<br>3. Canonical note generation for all 7 note types (`knowledge`, `decision`, `experience`, `error`, `lesson`, `resource`, `hypothesis`).<br>4. SHA-256 content deduplication.<br>5. Contradiction detection (opposing BUY vs SELL signals -> conflict note). |
| `test_vault_api_financial.py` | FastAPI `vault_api.py` Endpoints | 1. `POST /financial_note` ingestion and storage.<br>2. `GET /search` and `GET /memory/financial/search` retrieval.<br>3. `POST /memory/financial/search` structured search.<br>4. P0 invariant AI self-attestation block.<br>5. Error handling for malformed queries and invalid pagination tokens. |

### 4.2 CI Secret-Leak Prevention Strategy
1. **Zero Hardcoded Secrets Policy**:
   - The ingestion pipeline strips and redacts raw secrets from all parsed code/spreadsheets before markdown note emission.
   - API keys are injected exclusively via environment variables (`FRED_API_KEY`, `FINANCIAL_DATA_API_KEY`, `MEMORY_CONTROLLER_HMAC_SECRET`).
2. **Automated Secret Scanning Gate**:
   - Integrated scanning step in CI using regex patterns for 32-character hexadecimal FRED keys (`\b[a-f0-9]{32}\b`), JWT tokens, private keys, and high-entropy strings.
   - Test fixture in `tests/financial/test_ingestion_pipeline.py` specifically asserting that no note content or metadata contains raw API keys.

### 4.3 Tamper-Evident SHA-256 Audit Logging
- Every financial note proposal, search execution, attestation, archive, and supersession triggers an atomic write to `audit_log.jsonl`.
- Each log entry is cryptographically linked via `prev_hash` $\to$ `entry_hash` ($SHA-256$).
- `AuditLogger.verify_integrity()` is executed in test suites to verify that the cryptographic chain remains 100% unbroken.

---

## 5. Architectural Verification Plan & Acceptance Matrix

| Requirement | Acceptance Metric | Verification Method |
|---|---|---|
| **R1. Financial Ingestion Pipeline** | 95 assets, 5 macro tickers, 4 FRED series, 30 risks parsed from `ghid.py` and `Analiza_Piata_Profesionala.xlsx` into canonical notes with valid frontmatter. Zero hardcoded secrets. | `python -m pytest -q tests/financial/test_ingestion_pipeline.py` passes with 0 failures. |
| **R2. Multi-Layered Financial Query Engine** | 5-Layer pipeline: Entity resolution $\to$ SQLite filter $\to$ BM25/Vector RRF $\to$ Graph spreading activation $\to$ Progressive disclosure. Searching for "NASDAQ" returns `^NDX` notes. | `python -m pytest -q tests/financial/test_financial_search.py` passes with 0 failures. |
| **R3. REST API Integration** | `POST /financial_note` and `GET /search` exposed in `vault_api.py`, handling schemas, validation, security invariants, and pagination. | `python -m pytest -q tests/financial/test_vault_api_financial.py` passes with 0 failures. |
| **R4. Cognitive Trust Boundaries** | `AI_AGENT` cannot self-verify, cannot claim privileged provenance, cannot bypass `REVIEW` lifecycle. | Unit tests in `test_financial_search.py::TestSecurityInvariantsPreservation` pass. |
| **R5. Audit Log Chain** | Tamper-evident SHA-256 hash chaining remains intact. | `audit_logger.verify_integrity()` returns `(True, [])`. |

---

## 6. Conclusion & Implementation Directives

1. **Immediate Codebase Alignment**:
   - Populate `memory_controller/financial_schema.py` with `FINANCIAL_NOTE_SCHEMA`.
   - Update `memory_controller/financial_query.py` to bridge `FinancialQueryEngine` with `MultiLayeredFinancialSearchEngine` and the schema validator.
   - Ensure `vault_api.py` routes `POST /financial_note` and `GET /search` directly to the `MemoryController` financial query pipeline.
   - Adjust test fixtures in `tests/financial/test_financial_search.py` to utilize standard RFC 4122 UUID strings to align with `validate_frontmatter` format checks.
2. **Downstream Handoff**:
   The architectural blueprint in this document is complete, self-contained, and ready for immediate implementation by the implementation subagents.
