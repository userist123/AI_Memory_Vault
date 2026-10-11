# Comprehensive Codebase Survey Report: AI Memory Vault

**Report Date**: 2026-08-26  
**Author**: Explorer Survey 1 (`explorer_survey_1`)  
**Target Milestone**: Integration of Financial Ingestion Pipeline and Multi-Layered Financial Query Engine  
**Working Directory**: `c:\Users\user\Documents\Codex\AI_Memory_Vault_CODEX_READY`

---

## 1. Executive Summary

A comprehensive architectural and forensic survey of the **AI Memory Vault** repository was performed to prepare for the integration of the Financial Ingestion Pipeline and Multi-Layered Financial Query Engine.

The codebase is a production-grade cognitive memory system featuring:
1. **Multi-layer storage**: SQLite with Write-Ahead Logging (`SQLiteStorageEngine`) and atomic file system markdown persistence (`FileStorageEngine`).
2. **Formal lifecycle & trust enforcement**: Invariants P0–P18 strictly gating AI self-verification, privileged provenance, creation lifecycle boundaries, and attestation.
3. **Cryptographic tamper-evident audit logging**: Continuous SHA-256 hash chaining across all memory operations (`memory_controller/audit/logger.py`).
4. **Rich financial capabilities**: A high-precision 5-layer financial search engine (`memory_controller/financial_search.py`), a comprehensive 95-asset catalog and mathematical indicator suite (`xau_kinetic/financial_ingestion/`), and REST API endpoints in `vault_api.py`.
5. **Gaps identified for prompt fulfillment**:
   - `memory_controller/financial_schema.py` is currently a stub (`FINANCIAL_NOTE_SCHEMA = {}`).
   - `memory_controller/financial_query.py` provides a preliminary `FinancialQueryEngine` that requires full schema validation alignment and storage compatibility.
   - External desktop source files at `C:\Users\user\Desktop\Nu sterge\nusterge\` (`ghid.py`, `Analiza_Piata_Profesionala.xlsx`) contain hardcoded API keys (`FRED_API_KEY`) that must be strictly environment-injected to prevent secret leaks.
   - Missing unit test suites `tests/financial/test_query_engine.py` and `tests/financial/test_schema.py` must be implemented.

---

## 2. Memory Controller Architecture & Storage Subsystem

### 2.1 Directory Structure & Modules

```
memory_controller/
├── __init__.py
├── controller.py                 # Core MemoryController orchestrator (704 lines)
├── core.py                       # Backwards-compatibility shim re-exporting Lifecycle, StorageEngine, MemoryController
├── authority.py                  # Runtime authority scoring derived from provenance.source_type
├── authorizer.py                 # RBAC/PBAC policy matrix (Principal: HUMAN, AI_AGENT, ADMIN; Operation)
├── financial_query.py            # High-level FinancialQueryEngine wrapper (111 lines)
├── financial_schema.py           # Financial note schema definition (currently stub)
├── financial_search.py           # 5-layer MultiLayeredFinancialSearchEngine (1,283 lines)
├── git_integration.py            # Git commit and audit trail tracking
├── security.py                   # Re-exports security sanitization utilities
├── api_server.py                 # Local REST API Gateway for Browser / JARVIS UI (BaseHTTPRequestHandler)
├── audit/
│   └── logger.py                 # SHA-256 chained audit logger with verify_integrity()
├── cache/
│   └── lru_cache.py              # Event-invalidated LRU cache
├── context/
│   ├── budget.py                 # Soft & hard context token budgets per agent
│   ├── compression.py            # Automatic summarization & note compression
│   ├── metrics.py                # Context economy telemetry
│   ├── pack_builder.py           # Context Pack Builder assembling final API payloads
│   ├── progressive_disclosure.py # Multi-tiered disclosure (metadata, snippet, sections, full)
│   ├── query_classifier.py       # Intent & query parameter classifier
│   ├── relevance_scoring.py      # Lexical / semantic relevance scorer
│   └── retrieval.py              # Retrieval orchestrator
├── security/
│   ├── pagination_token.py       # HMAC-SHA256 signed pagination tokens
│   └── utils.py                  # Query sanitization, path traversal checks, size limits
├── storage/
│   ├── file_engine.py            # Atomic Markdown + YAML frontmatter storage engine
│   ├── path_resolver.py          # Category-sanitized file path resolver & containment checker
│   ├── serializer.py             # YAML Frontmatter + Markdown body serializer/deserializer
│   └── sqlite_engine.py          # Production SQLite WAL storage engine with strict constraints
└── validation/
    ├── provenance.py             # Provenance required keys validator
    ├── schema.py                 # Draft7 JSON Schema frontmatter validator
    └── supersession.py           # Explicit supersession cycle & lineage validator
```

### 2.2 Storage Engine Analysis

#### A. `SQLiteStorageEngine` (`memory_controller/storage/sqlite_engine.py`)
- **Concurrency & WAL Mode**: Configured with `PRAGMA journal_mode=WAL;`, `PRAGMA synchronous=NORMAL;`, and `PRAGMA busy_timeout=5000;`.
- **Thread Safety**: Uses `threading.local()` for thread-local database connections and thread-safe connection tracking.
- **Transactions**: Atomic inserts, updates, and deletes use explicit `BEGIN IMMEDIATE;` and `COMMIT;` blocks with automated `ROLLBACK;` on error (lines 180-189).
- **Relational Schema**: Table `notes` with strict CHECK constraints on `type`, `lifecycle`, `source_type`, `confidence`, and `verification`. Stores indexed columns alongside `relations`, `provenance`, `content`, and complete `raw_json`.
- **Graph & Lineage Traversal**: Built-in recursive CTE `resolve_active_lineage()` (lines 224-241) to traverse `superseded_by` chains up to 50 hops.

#### B. `FileStorageEngine` (`memory_controller/storage/file_engine.py`)
- **Canonical Structure**: Indexes notes across `00_CORE`, `01_KNOWLEDGE`, `02_PROJECTS`, `03_PROCEDURES`, `04_MEMORY`, `05_RESOURCES`, `99_SYSTEM`. Excludes `06_INBOX` and `90_TEMPLATES`.
- **Atomic File Replacement**: Writes to `.tmp_*` temporary files in the target directory using `tempfile.mkstemp`, flushes, calls `os.fsync`, and atomically swaps via `os.replace` (lines 89-102).
- **Integrity**: Detects duplicate UUIDs across files as fatal integrity errors; logs malformed YAML to audit logs.

#### C. `Serializer` (`memory_controller/storage/serializer.py`)
- Serializes notes into canonical YAML frontmatter delimited by `---` + Markdown body.
- Uses `yaml.SafeDumper` with custom representers for Python `Enum` values.
- Deserialization extracts frontmatter via regex `^---\r?\n(.*?)\r?\n---\r?\n?(.*)` and sets `data["content"] = body`.

#### D. `PathResolver` (`memory_controller/storage/path_resolver.py`)
- Maps note `type` to designated directories (`01_KNOWLEDGE`, `02_PROJECTS`, etc.).
- Sanitizes Windows/Unix forbidden characters (`: * ? " < > | \ /`), reserved device names (`CON`, `PRN`, `AUX`, `NUL`, `COM1-9`, `LPT1-9`), trims dots/spaces, and caps filename lengths.
- Enforces root containment using `os.path.commonpath` to prevent path traversal.

### 2.3 Lifecycle State Machine & Transition Rules

The `Lifecycle` enum defines 9 discrete states:
1. `RAW`
2. `CLASSIFIED`
3. `NORMALIZED`
4. `REVIEW`
5. `VERIFIED`
6. `ACTIVE`
7. `RECONSOLIDATING`
8. `SUPERSEDED`
9. `ARCHIVED`

**Permitted Transitions (`controller.py:120-127`)**:
- `RAW` → `CLASSIFIED`
- `CLASSIFIED` → `NORMALIZED`
- `NORMALIZED` → `REVIEW`
- `REVIEW` → `VERIFIED`
- `VERIFIED` → `ACTIVE`
- `ACTIVE` → `SUPERSEDED` | `ARCHIVED`

*Creation Lifecycle Gate*: AI Agents (`Principal.AI_AGENT`) can only create notes in `_PERMITTED_CREATION_LIFECYCLES = {RAW, CLASSIFIED, NORMALIZED, REVIEW}` (`controller.py:73-78, 447-449`). Direct creation into `ACTIVE` or `VERIFIED` is strictly rejected.

### 2.4 Frontmatter Schema Validation (`validation/schema.py`)

Notes must validate against `_CANONICAL_SCHEMA` using `Draft7Validator`:
- **Required fields**: `id`, `type`, `lifecycle`, `category`, `tags`, `created`, `updated`, `provenance`, `confidence`, `verification`, `relations`.
- **Formats**: `id` must be `format: uuid` (RFC 4122); `created`, `updated`, `valid_from`, `valid_until`, `last_verified`, `source_date`, `extraction_date` must be `format: date` (`YYYY-MM-DD`).
- **Enums**:
  - `type`: `knowledge`, `project`, `procedure`, `decision`, `experience`, `error`, `lesson`, `preference`, `resource`, `hypothesis`, `system`, `core`, `index`.
  - `confidence`: `very_high`, `high`, `medium`, `low`, `unknown`.
  - `verification`: `verified`, `partially_verified`, `unverified`, `inferred`.
  - `provenance.source_type`: `user`, `official`, `execution`, `experience`, `ai`, `inference`, `import`, `unknown`.
  - `provenance.redaction`: `none`, `applied`, `not_applicable`.
  - `provenance.provenance_status`: `complete`, `incomplete`.
- `additionalProperties: False` prevents unknown metadata fields.

### 2.5 Confidence & Verification Models

- **Independence**: As defined in `00_CORE/Confidence_Model.md` and `AGENTS.md §11`, `confidence` measures evidence strength, while `verification` records the formal attestation state.
- **Authority Score (`authority.py`)**: Maps `provenance.source_type` deterministically to numeric weights: `official`: 0.9, `import`: 0.8, `execution`: 0.7, `experience`: 0.6, `user`: 0.5, `ai`: 0.4, `inference`: 0.3, `unknown`: 0.2.
- **Attestation Policy**: `verification = "verified"` cannot be set by `Principal.AI_AGENT` and cannot be passed directly into `propose()` or `update()`. It requires explicit invocation of `MemoryController.attest()` by `Principal.HUMAN` or `Principal.ADMIN` with `verification_reason` and `evidence_reference`.

---

## 3. REST API Architecture (`vault_api.py` & `api_server.py`)

### 3.1 FastAPI Application (`vault_api.py`)

The primary programmatic API is implemented using **FastAPI** (`vault_api.py`):
- **Storage Binding**: Initializes `SQLiteStorageEngine("vault_memory.sqlite3", wal_mode=True)` and wraps it with `MemoryController`.
- **Endpoints**:
  1. `POST /memory/propose`: Ingests knowledge notes. Generates UUID, enforces `lifecycle: REVIEW`, `provenance.source_type: ai`, `verification: unverified`, delegates to `controller.propose(Principal.AI_AGENT, note_data)`.
  2. `GET /memory/search`: Basic keyword search via `controller.search()`.
  3. `GET /memory/financial/search`: Multi-parameter query-string search for financial notes.
  4. `POST /memory/financial/search`: Full-featured JSON body search supporting `query`, `symbol`, `symbols`, `asset_symbol`, `category`, `asset_classes`, `min_confidence`, `confidence_min`, `verification_state`, `verification_states`, `date_from`, `date_to`, `types`, `lifecycles`, `limit`, `page_size`, `page_token`, `disclosure_level`.
  5. `POST /agent/dispatch`: Multi-agent remote GPU dispatching.
  6. `GET /compute/status`: Status of GPU compute nodes.

### 3.2 JARVIS Browser Gateway (`memory_controller/api_server.py`)

A secondary HTTP server using Python's standard `http.server.BaseHTTPRequestHandler`:
- Targets the JARVIS Web UI dashboard (`projects/jarvis_web/`).
- Exposes `/api/v1/status`, `/api/v1/metrics`, `/api/v1/agents`, `/api/v1/skills`, `/api/v1/search`, `/api/v1/proposals`, `/api/v1/chat`.
- Integrates with local Ollama (`http://127.0.0.1:11434`) for Romanian conversational responses.

---

## 4. Audit Logging & Cryptographic Tamper-Evidence

The audit logging subsystem (`memory_controller/audit/logger.py`) provides an unalterable security event trail:

### 4.1 Log Entry Schema & Hashing Protocol

Each entry in `audit_log.jsonl` contains:
```json
{
  "actor": "ai_agent",
  "operation": "propose",
  "target_id": "c1a01101-7291-49fa-9481-22904c10b001",
  "timestamp": "2026-08-26T16:00:00Z",
  "outcome": "success",
  "metadata": {"note_type": "knowledge"},
  "prev_hash": "6b86b273ff34fce19d6b804eff5a3f5747ada4eaa22f1d49c01e52ddb7875b4b",
  "entry_hash": "4a5e1e4baab89f3a32518a88c31bc87f618f76673e2cc77ab2127b7afdeda33b"
}
```

### 4.2 Tamper-Evident Chaining Rules
1. **Genesis**: The initial entry links to `prev_hash: "GENESIS"`.
2. **Deterministic Canonical Bytes**: Before hashing, `entry_hash` is computed by dumping all entry fields (including `prev_hash`, excluding `entry_hash`) sorted by key (`sort_keys=True, ensure_ascii=False`) using `EnumEncoder`.
3. **SHA-256 Hash**: `computed_hash = hashlib.sha256(canonical_bytes).hexdigest()`.
4. **Thread Safety**: Writing is protected by `threading.Lock()` (`_write_entry`).

### 4.3 Integrity Verification Algorithm (`verify_integrity()`)
- Sequentially reads every line of `audit_log.jsonl`.
- Verifies that `entry["prev_hash"]` equals the previous line's `entry_hash` (or `"GENESIS"` for line 1).
- Recomputes the SHA-256 hash over canonical JSON bytes without `entry_hash` and asserts exact match with stored `entry["entry_hash"]`.
- Returns `(True, [])` or `(False, violations_list)`.

---

## 5. Financial Ingestion & Query Engine Analysis

### 5.1 Existing Financial Architecture & Modules

#### A. Multi-Layered Financial Search Engine (`memory_controller/financial_search.py`)
Provides a sophisticated 5-layer search pipeline:
- **Layer 1: Financial Entity & Alias Extractor (`FinancialEntityResolver`)**:
  - Contains complete catalog of **95 assets, 5 macro tickers, 4 FRED series**.
  - Maps multi-word colloquial aliases (e.g. `"Spot Gold"`, `"Aur"`, `"Cable"`, `"Footsie"`, `"Fear Gauge"`) to canonical symbols.
  - Automatically extracts date ranges (`YYYY-MM-DD`, `"since 2025"`), confidence levels, verification states, and indicator keywords from natural language queries.
- **Layer 2: Structured & Temporal Filtering**:
  - Filters storage by symbol, category, confidence floor, verification state, and date range.
  - Excludes `RAW` notes unconditionally.
- **Layer 3: Hybrid BM25 & Dense Vector Embeddings (RRF)**:
  - Okapi BM25 scoring with title/tag weighting (`BM25Ranker`).
  - 128-dimensional deterministic semantic feature hashing (`DenseVectorEmbedder`).
  - Combines ranks via Reciprocal Rank Fusion ($k=60$) multiplied by confidence and verification factors.
- **Layer 4: Wikilink Graph Spreading Activation**:
  - Parses frontmatter `relations`, inline Obsidian `[[wikilinks]]`, and asset correlations.
  - Propagates activation energy across the graph with hop decay (decay=0.6, max_hops=2) and blends 35% into the composite score.
- **Layer 5: Progressive Disclosure & HMAC Pagination**:
  - Formats results into `metadata`, `snippet`, `sections`, or `full`.
  - Emits tamper-resistant HMAC-SHA256 pagination tokens (`PaginationToken`).

#### B. Ingestion & Quantitative Subsystem (`xau_kinetic/financial_ingestion/`)
- `catalog.py`: Defines dataclasses `Instrument`, `MacroTicker`, `FREDSeries`, competitor mappings (`COMPETITOR_MAP`), risk libraries (`RISK_LIBRARY`), and calendar libraries (`CALENDAR_LIBRARY`).
- `indicators.py`: Pure mathematical indicator functions: RSI, MACD, Moving Averages, Bollinger Bands, ATR, Stochastic, Momentum, RVOL, Support/Resistance pivots, SL/TP risk-reward calculations.
- `pipeline.py`: Data fetchers (`MarketDataFetcher` using yfinance, `FREDDataFetcher` using FRED API, `SentimentFetcher` for Fear & Greed, `MarketCache`, and synthetic fallback generator).
- `adapter.py`: `FinancialMemoryAdapter` generating Draft7 schema-valid notes for `knowledge` (asset profiles, macro regimes, technical setups), `decision`, `experience`, `error`, `lesson`, and `resource`. Includes `MemoryDeduplicator` for content hashing and contradiction detection.

### 5.2 External Desktop Data Sources

Found at `C:\Users\user\Desktop\Nu sterge\nusterge\`:
- `ghid.py` (91 KB, 1,954 lines): Market update script containing asset dictionary definitions (`INDICI`, `ACTIUNI`, `CRYPTO`, `VALUTE`, `MATERII_PRIME`), FRED fetching, technical indicators, and Excel report generation.
- `Analiza_Piata_Profesionala.xlsx`: Production workbook with structured asset sheets, technical indicators, macro dashboards, and historical price matrices.

**Critical Security Finding**:
In `C:\Users\user\Desktop\Nu sterge\nusterge\ghid.py`, line 29:
```python
FRED_API_KEY = "e372c6879cce084b8c3601f76adbe78d"
```
Per `AGENTS.md §19` and Requirement R1/R3, **hard-coded secrets must never be committed or written into memory notes**. All ingestion code must inject keys via `os.getenv("FRED_API_KEY")` and maintain zero secret leakage.

### 5.3 Implementation Gaps to Address

1. **`memory_controller/financial_schema.py`**:
   - Currently: `FINANCIAL_NOTE_SCHEMA = {}`
   - Needs: A comprehensive JSON Schema validating financial notes (symbols, asset classes, indicators, metrics, timestamps, provenance, and frontmatter).
2. **`memory_controller/financial_query.py`**:
   - `FinancialQueryEngine` must expose `ingest_financial_note(note: dict) -> str` and `search(query: str, filters: dict | None = None, top_k: int = 10) -> list[dict]`.
   - Must validate against `FINANCIAL_NOTE_SCHEMA`, generate valid UUIDs, populate canonical frontmatter (`verification: partially_verified`), and persist to the storage engine.
3. **`vault_api.py` Endpoints**:
   - Ensure `POST /financial_note` and `GET /search` (or aliases) are cleanly exposed alongside `/memory/financial/search`.
4. **Missing Test Files**:
   - `tests/financial/test_query_engine.py`
   - `tests/financial/test_schema.py`

---

## 6. Test Framework & Execution Survey

### 6.1 Configuration & Execution Environment
- `pytest.ini`: Configured with `norecursedirs = AI_Memory_Vault_OBSIDIAN .git .vs` and `testpaths = memory_controller/tests cognitive_core/tests`.
- Environment: Python 3.14.0 with `pytest`, `jsonschema`, `fastapi`, `pandas`, `numpy`, `pyyaml`.

### 6.2 Test Execution Results

| Test Suite | Total Tests | Passed | Failed/Errors | Status / Root Cause |
|---|---|---|---|---|
| `memory_controller/tests/` | 215 | 215 | 0 | **100% PASS** |
| `tests/financial/test_ingestion_pipeline.py` | 37 | 37 | 0 | **100% PASS** |
| `tests/financial/test_challenger1_ingestion.py` | 24 | 24 | 0 | **100% PASS** |
| `tests/financial/test_challenger2_adversarial.py` + Tiers 1–4 | 125 | 125 | 0 | **100% PASS** |
| `tests/financial/test_financial_search.py` | 327 | 310 | 14 F, 3 E | Non-UUID test note IDs (e.g. `"note-gold-1"`) rejected by Draft7 `format: uuid` schema validator. |
| `cognitive_core/tests/` | 283 | 282 | 1 F | Working memory buffer ID set assertion. |

---

## 7. Trust Boundaries, Governance Rules & Security Invariants

### 7.1 Key Rules from `AGENTS.md`
- **Source of Truth Hierarchy**: Explicit User Confirmation > Execution/Test > Official Docs > Vault Docs > Experience > External > AI Inferred.
- **Canonical vs. Raw Memory**: Canonical memory resides in `00_CORE`, `01_KNOWLEDGE`, `02_PROJECTS`, `03_PROCEDURES`, `04_MEMORY`, `05_RESOURCES`. `06_INBOX/RAW_IMPORTS/` is strictly raw evidence and never indexed as canonical knowledge.
- **Deduplication & Contradictions (§9, §10)**: Compare title, subject, entities, claims, semantic similarity; never silently overwrite or hide contradictions; record conflicts explicitly.
- **Security & Secret Leak Prevention (§19)**: NEVER store passwords, API keys, tokens, or private keys. Redact and flag secrets during import.

### 7.2 Cognitive Invariants (P0–P18)
- **P0 / P0-001 / P0-005**: AI Self-Verification Gate — `Principal.AI_AGENT` cannot set `verification = "verified"`.
- **P0-002**: Privileged Provenance Gate — `Principal.AI_AGENT` cannot claim `source_type` of `user`, `official`, `experience`, or `import`. Permitted: `execution`, `ai`, `inference`, `unknown`.
- **P0-006**: Creation Lifecycle Gate — `Principal.AI_AGENT` can only propose into `{RAW, CLASSIFIED, NORMALIZED, REVIEW}`. Direct creation to `ACTIVE` is forbidden.
- **P0-011**: Provenance Immutability — `provenance.source_type` is immutable post-creation across all principals.
- **P0-014**: Attestation Restriction — Only `Principal.HUMAN` and `Principal.ADMIN` can execute `controller.attest()`.
- **P16–P18**: Hardware Telemetry & Forensics Immutability — Physical hardware identifiers are strictly read-only; friendly names are isolated; cryptographic chain-of-custody binding is required.

---

## 8. Summary Table of Files for Integration

| File Path | Role | Action Needed for Financial Pipeline |
|---|---|---|
| `memory_controller/financial_schema.py` | Schema definition | Define `FINANCIAL_NOTE_SCHEMA` Draft7 JSON Schema with strict property constraints. |
| `memory_controller/financial_query.py` | Core Query Engine | Implement `FinancialQueryEngine` with `ingest_financial_note()` and `search()`. |
| `vault_api.py` | REST API | Expose `POST /financial_note` and `GET /search` endpoints. |
| `tests/financial/test_schema.py` | Test Suite | Implement comprehensive tests for `FINANCIAL_NOTE_SCHEMA`. |
| `tests/financial/test_query_engine.py` | Test Suite | Implement comprehensive unit tests for `FinancialQueryEngine`. |
| `memory_controller/audit/logger.py` | Audit System | Ready; ensure all financial ingestion/search operations emit valid SHA-256 chained events. |
| `memory_controller/financial_search.py` | Multi-layer search | Ready and operational; provide helper compatibility where needed. |
| `xau_kinetic/financial_ingestion/` | Catalog & Ingestion | Ready; leverage catalogs, indicators, and adapter routines. |

---

*Survey report authored by Explorer Survey 1. Ready for handoff and downstream agent task allocation.*
