# Milestone 1 Fix Strategy: Financial Schema & Invariant Hardening

**Author**: Explorer M1 Fix (`teamwork_preview_explorer`)  
**Target Files**:
- `memory_controller/financial_schema.py`
- `tests/financial/test_schema.py`
**Adversarial & Invariant Reference Suites**:
- `tests/financial/test_challenger_m1_adversarial.py`
- `tests/financial/test_challenger_m1_invariants.py`
**Authoritative References**:
- `PROJECT.md` § Interface Contracts (M1 ↔ M2)
- `.agents/ORIGINAL_REQUEST.md` § R1, Acceptance Criteria
- `AGENTS.md` § 0, 1, 6, 8, 11, 13, 19
- `.agents/rules/vault_cognitive_rules.md` § 1 (Trust Boundary Invariants P0-P18)
- `.agents/auditor_m1_1/report.md` (Forensic Integrity Audit Report)
- `.agents/challenger_m1_1/handoff.md` & `.agents/challenger_m1_2/handoff.md`

---

## 1. Executive Summary & Defect Catalog

A forensic evaluation and adversarial stress-testing across `tests/financial/test_schema.py`, `tests/financial/test_challenger_m1_adversarial.py`, and `tests/financial/test_challenger_m1_invariants.py` revealed **55 failing test cases** out of 280 total tests.

The failures stem from five primary root causes:
1. **Critical Schema Facade / Wildcard Bypass**: In `FINANCIAL_NOTE_SCHEMA`, Variant C (`Raw Financial Note Payload`) contained zero `required` fields and set `additionalProperties: True`. Under Draft-07 JSON Schema `anyOf` semantics, any dictionary (even one with corrupted frontmatter, invalid types, or out-of-bounds indicators) matched Variant C with 0 errors, rendering schema validation completely ineffective.
2. **Unhandled `TypeError` Crash on Fuzzed / Non-String Inputs**: Direct set membership tests in `validate_financial_note` (`if src_type in privileged_sources` and `if lifecycle in {"ACTIVE", ...}`) crashed with `TypeError: cannot use 'dict' as a set element (unhashable type: 'dict')` when inputs contained unhashable objects.
3. **Null UUID Bypass**: `validate_financial_note` checked `if "id" in fm and fm["id"] is not None:`, allowing notes with `id: None` to skip UUID verification and pass validation via the Variant C wildcard.
4. **Cognitive Invariant Case / Forgery Bypasses (P0, P2, P3)**: Invariant validations relied on case-sensitive equality against narrow blacklists without case normalization, strict whitelist validation, or type guards, allowing case variations (e.g. `"VERIFIED"`, `"USER"`, `"active"`) and arbitrary forged strings (`"attested"`, `"root"`, `"PRODUCTION"`) to bypass security gates.
5. **Pydantic Model Union Inheritance Inconsistency**: In `FinancialNoteModel`, `technical_indicators` and `quantitative_signal` were typed as `Optional[Union[TechnicalIndicatorsPayload, Dict[str, Any]]]` rather than supporting base classes `FinancialIndicators` and `TradeSignal`.

---

## 2. Deep-Dive Root Cause Analysis

### 2.1 Root Cause 1: Draft-07 `anyOf` Wildcard Matching in `FINANCIAL_NOTE_SCHEMA`
In `memory_controller/financial_schema.py:385-400`:
```python
# Variant C: Raw Financial Note Payload (e.g. before frontmatter creation)
{
    "type": "object",
    "properties": {
        "title": {"type": "string"},
        "symbol": {"type": "string"},
        "category": {"type": "string"},
        "indicators": {"type": "object"},
        "signals": {"type": "array"},
        "risk_metrics": {"type": "object"},
        "narrative": {"type": "string"},
        "raw_content": {"type": "string"}
    },
    "additionalProperties": True
}
```
**Mechanism of Failure**:
- Draft-07 JSON Schema defines `anyOf` as valid if **at least one** subschema validates without error.
- Variant C specified `type: "object"`, defined 8 optional properties, required 0 properties, and permitted `additionalProperties: True`.
- When an invalid canonical note was validated (e.g., `{'id': 'not-a-uuid', 'type': 'invalid_enum', 'technical_indicators': {'rsi_14': 999999}}`), it failed Variant A and Variant B.
- However, when evaluated against Variant C:
  - The object is a dictionary (`type: "object"`: PASS).
  - None of `title`, `symbol`, etc. were violated (PASS).
  - All unrecognized keys (`id`, `type`, `technical_indicators`) were accepted by `additionalProperties: True` (PASS).
- As a result, `Draft7Validator.iter_errors()` returned zero errors for **every** Python dictionary.

### 2.2 Root Cause 2: Unhandled `TypeError` on Unhashable Types in `validate_financial_note`
In `memory_controller/financial_schema.py:465, 473`:
```python
# Line 465
if src_type in privileged_sources:
# Line 473
if lifecycle in {"ACTIVE", "VERIFIED", "SUPERSEDED", "ARCHIVED"}:
```
**Mechanism of Failure**:
- If `lifecycle` or `provenance.source_type` is a dictionary or list (e.g., `{"lifecycle": {"bad": "data"}}`), Python executes `hash(dict)` during set lookup, raising `TypeError: cannot use 'dict' as a set element (unhashable type: 'dict')`.
- This crashed fuzzing and exception-safety test harnesses.

### 2.3 Root Cause 3: Null UUID Bypass
In `memory_controller/financial_schema.py:441`:
```python
if "id" in fm and fm["id"] is not None:
    try:
        val = uuid.UUID(str(fm["id"]))
```
**Mechanism of Failure**:
- When `id: None` was passed, the condition `fm["id"] is not None` evaluated to `False`, skipping UUID verification.
- Combined with Root Cause 1, notes with `id: None` were reported as valid (`is_valid=True, errors=[]`).

### 2.4 Root Cause 4: Cognitive Invariant Enforcement Weaknesses (P0, P2, P3)
In `memory_controller/financial_schema.py:452-478`:
- **P0**: Tested `if verification == "verified":`. Passing uppercase `"VERIFIED"`, whitespace `" verified "`, or unwhitelisted strings like `"attested"` bypassed the check.
- **P2**: Tested `if src_type in privileged_sources:`. Passing uppercase `"USER"`, `"OFFICIAL"`, or custom strings like `"root"` or `"admin"` bypassed the check.
- **P3**: Tested `if lifecycle in {"ACTIVE", ...}:`. Passing lowercase `"active"` or arbitrary strings like `"PRODUCTION"` bypassed the check.

### 2.5 Root Cause 5: Pydantic Model Union Type Inconsistency
In `FinancialNoteModel`:
- `technical_indicators` was typed as `Optional[Union[TechnicalIndicatorsPayload, Dict[str, Any]]]`.
- Passing an instance of base class `FinancialIndicators` caused Pydantic v2 validation errors because `FinancialIndicators` is not listed in the Union.

---

## 3. Comprehensive Fix Architecture

```
+---------------------------------------------------------------------------------------------------+
|                                  HARDENED FINANCIAL_NOTE_SCHEMA                                   |
|  - Definitions: UUID (format: uuid), Provenance, Relation, PriceData, TechnicalIndicators,       |
|    QuantitativeSignal, RiskMetrics, MacroContext, Commentary (Strict ranges: RSI, ATR, Score...) |
|  - Variant A (Canonical Note): required [id, type, lifecycle, category, tags, created, updated,   |
|    provenance, confidence, verification, relations], strict enums, subschemas, addlProps: True    |
|  - Variant B (Nested Payload): required [frontmatter], strict frontmatter subschema, addlProps: T |
|  - Variant C (Raw Payload): required [title, category], strict properties, addlProps: False       |
+---------------------------------------------------------------------------------------------------+
                                                  |
                                                  v
+---------------------------------------------------------------------------------------------------+
|                                HARDENED validate_financial_note                                   |
|  1. Input Type Guard: isinstance(data, dict)                                                      |
|  2. Draft-07 JSON Schema Validation: Draft7Validator + FormatChecker()                            |
|  3. Strict UUID RFC 4122 Check: id must be non-null, non-empty str, canonical 8-4-4-4-12 format  |
|  4. Strict Whitelist & Type Guards:                                                              |
|     - type: isinstance(str) and type in allowed_types                                             |
|     - confidence: isinstance(str) and confidence in allowed_confidence                           |
|  5. Trust Boundary Invariant Gate (P0, P2, P3):                                                   |
|     - P0: verification in allowed_verification (reject case variations & forged strings;           |
|           if is_ai_agent and ver_clean == "verified" -> REJECT)                                   |
|     - P2: prov.source_type in allowed_sources (reject case variations & forged strings;           |
|           if is_ai_agent and src_clean in {"user", "official", "experience", "import"} -> REJECT)|
|     - P3: lifecycle in allowed_lifecycles (reject case variations & forged strings;               |
|           if is_ai_agent and lc_clean in {"ACTIVE", "VERIFIED", "SUPERSEDED", "ARCHIVED"} -> REJ) |
+---------------------------------------------------------------------------------------------------+
                                                  |
                                                  v
+---------------------------------------------------------------------------------------------------+
|                                HARDENED PYDANTIC V2 DOMAIN MODELS                                 |
|  - FinancialFrontmatter, PriceDataPayload, FinancialIndicators, TechnicalIndicatorsPayload,       |
|    TradeSignal, QuantitativeSignalPayload, RiskMetrics, MacroContextPayload, CommentaryPayload    |
|  - FinancialNoteModel: accepts base classes, subclasses, or Dict[str, Any] for all sub-payloads   |
+---------------------------------------------------------------------------------------------------+
```

---

## 4. Concrete Proposed Code Patches

### 4.1 Proposed Implementation: `memory_controller/financial_schema.py`

Below is the complete, drop-in replacement implementation for `memory_controller/financial_schema.py`:

```python
"""
Financial Schema & Domain Models for AI Memory Vault.
Defines:
1. Complete Draft-07 JSON Schema (FINANCIAL_NOTE_SCHEMA) covering canonical frontmatter
   and financial market payloads with zero-wildcard isolation.
2. validate_financial_note(data: dict) -> tuple[bool, list[str]] validator enforcing
   Draft-07 schema and P0-P18 Trust Boundary Invariants (AI verification gate,
   privileged provenance restrictions, creation lifecycle scoping).
3. Type-safe Pydantic v2 domain models for financial memory notes and sub-payloads.
"""

from __future__ import annotations

import enum
import json
import uuid
from datetime import datetime, timezone
from typing import Any, Dict, List, Optional, Tuple, Union

import jsonschema
from jsonschema import Draft7Validator, FormatChecker
from pydantic import BaseModel, ConfigDict, Field, field_validator


# ============================================================================
# 1. ENUMS
# ============================================================================

class MemoryTypeEnum(str, enum.Enum):
    KNOWLEDGE = "knowledge"
    DECISION = "decision"
    EXPERIENCE = "experience"
    ERROR = "error"
    LESSON = "lesson"
    RESOURCE = "resource"
    HYPOTHESIS = "hypothesis"
    PROJECT = "project"
    PROCEDURE = "procedure"
    PREFERENCE = "preference"
    SYSTEM = "system"
    CORE = "core"
    INDEX = "index"


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


class AssetClassEnum(str, enum.Enum):
    INDICI = "INDICI"
    ACTIUNI = "ACTIUNI"
    CRYPTO = "CRYPTO"
    VALUTE = "VALUTE"
    MATERII_PRIME = "MATERII_PRIME"
    MACRO = "MACRO"
    indici = "indici"
    actiuni = "actiuni"
    crypto = "crypto"
    valute = "valute"
    materii_prime = "materii_prime"
    macro = "macro"


class SignalEnum(str, enum.Enum):
    BUY = "BUY"
    SELL = "SELL"
    WAIT = "WAIT"


class TrendEnum(str, enum.Enum):
    BULLISH = "Bullish"
    BEARISH = "Bearish"
    SIDEWAYS = "Sideways"


# ============================================================================
# 2. DRAFT-07 JSON SCHEMA
# ============================================================================

FINANCIAL_NOTE_SCHEMA: Dict[str, Any] = {
    "$schema": "http://json-schema.org/draft-07/schema#",
    "title": "FinancialMemoryNote",
    "description": "Draft-07 JSON Schema for canonical financial memory notes and payloads in AI Memory Vault adhering to AGENTS.md and P0-P18 invariants.",
    "type": "object",
    "definitions": {
        "UUID": {
            "type": "string",
            "format": "uuid"
        },
        "Provenance": {
            "type": "object",
            "required": ["source_type", "source_ref"],
            "properties": {
                "source_type": {
                    "type": "string",
                    "enum": ["user", "official", "execution", "experience", "ai", "inference", "import", "unknown"]
                },
                "source_ref": {"type": "string"},
                "source_date": {"type": ["string", "null"]},
                "extraction_date": {"type": ["string", "null"]},
                "original_path": {"type": ["string", "null"]},
                "redaction": {"type": ["string", "null"], "enum": ["none", "applied", "not_applicable", None]},
                "provenance_status": {"type": ["string", "null"], "enum": ["complete", "incomplete", None]},
                "timestamp": {"type": ["string", "null"]}
            },
            "additionalProperties": True
        },
        "Relation": {
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
                "target": {"type": "string"},
                "target_id": {"type": ["string", "null"], "format": "uuid"}
            },
            "additionalProperties": True
        },
        "PriceData": {
            "type": "object",
            "properties": {
                "open": {"type": ["number", "null"]},
                "high": {"type": ["number", "null"]},
                "low": {"type": ["number", "null"]},
                "close": {"type": ["number", "null"]},
                "change_day_pct": {"type": ["number", "null"]},
                "change_week_pct": {"type": ["number", "null"]},
                "change_month_pct": {"type": ["number", "null"]},
                "volume": {"type": ["integer", "number", "null"]},
                "avg_volume_20d": {"type": ["integer", "number", "null"]},
                "rvol": {"type": ["number", "null"]}
            },
            "additionalProperties": True
        },
        "TechnicalIndicators": {
            "type": "object",
            "properties": {
                "rsi_14": {"type": ["number", "null"], "minimum": 0.0, "maximum": 100.0},
                "rsi_status": {"type": ["string", "null"]},
                "macd": {"type": ["number", "null"]},
                "macd_signal": {"type": ["number", "null"]},
                "macd_hist": {"type": ["number", "null"]},
                "macd_cross": {"type": ["string", "null"]},
                "ma20": {"type": ["number", "null"]},
                "ma50": {"type": ["number", "null"]},
                "ma200": {"type": ["number", "null"]},
                "ma_cross": {"type": ["string", "null"]},
                "trend": {"type": ["string", "null"], "enum": ["Bullish", "Bearish", "Sideways", "bullish", "bearish", "sideways", None]},
                "bb_mid": {"type": ["number", "null"]},
                "bb_sup": {"type": ["number", "null"]},
                "bb_inf": {"type": ["number", "null"]},
                "bb_width": {"type": ["number", "null"]},
                "atr_14": {"type": ["number", "null"], "minimum": 0.0},
                "stoch_k": {"type": ["number", "null"]},
                "stoch_d": {"type": ["number", "null"]},
                "momentum_10d": {"type": ["number", "null"]},
                "support_20d": {"type": ["number", "null"]},
                "resistance_20d": {"type": ["number", "null"]},
                "rvol": {"type": ["number", "null"]}
            },
            "additionalProperties": True
        },
        "QuantitativeSignal": {
            "type": "object",
            "properties": {
                "signal": {"type": ["string", "null"], "enum": ["BUY", "SELL", "WAIT", "buy", "sell", "wait", None]},
                "score": {"type": ["integer", "null"], "minimum": -5, "maximum": 5},
                "confluences": {"type": ["integer", "null"], "minimum": 0, "maximum": 5},
                "stop_loss": {"type": ["number", "null"]},
                "take_profit": {"type": ["number", "null"]},
                "risk_reward_ratio": {"type": ["number", "null"]},
                "win_probability_pct": {"type": ["number", "null"], "minimum": 35.0, "maximum": 90.0},
                "timeframe": {"type": ["string", "null"]},
                "trigger_condition": {"type": ["string", "null"]},
                "status": {"type": ["string", "null"]}
            },
            "additionalProperties": True
        },
        "RiskMetrics": {
            "type": "object",
            "properties": {
                "impact": {"type": ["integer", "null"], "minimum": 1, "maximum": 5},
                "probability_pct": {"type": ["number", "null"], "minimum": 0.0, "maximum": 100.0},
                "score": {"type": ["number", "null"]},
                "horizon": {"type": ["string", "null"]},
                "actions": {"type": ["string", "null"]},
                "sl_atr_multiple": {"type": ["number", "null"]},
                "tp_atr_multiple": {"type": ["number", "null"]},
                "planned_rr": {"type": ["number", "null"]}
            },
            "additionalProperties": True
        },
        "MacroContext": {
            "type": "object",
            "properties": {
                "vix": {"type": ["number", "null"]},
                "yield_10y": {"type": ["number", "null"]},
                "yield_2y": {"type": ["number", "null"]},
                "yield_30y": {"type": ["number", "null"]},
                "usd_index": {"type": ["number", "null"]},
                "fear_greed_index": {"type": ["integer", "null"]},
                "fed_funds_rate": {"type": ["number", "null"]},
                "cpi": {"type": ["number", "null"]},
                "unemployment_rate": {"type": ["number", "null"]},
                "gdp": {"type": ["number", "null"]}
            },
            "additionalProperties": True
        },
        "Commentary": {
            "type": "object",
            "properties": {
                "movement_explanation": {"type": ["string", "null"]},
                "opportunity_alert": {"type": ["string", "null"]},
                "educational_lesson": {"type": ["string", "null"]}
            },
            "additionalProperties": True
        }
    },
    "anyOf": [
        # Variant A: Canonical Note (Flat frontmatter + financial payload fields)
        {
            "type": "object",
            "required": [
                "id", "type", "lifecycle", "category", "tags",
                "created", "updated", "provenance", "confidence",
                "verification", "relations"
            ],
            "properties": {
                "id": {"$ref": "#/definitions/UUID"},
                "type": {
                    "type": "string",
                    "enum": [
                        "knowledge", "decision", "experience", "error", "lesson",
                        "resource", "hypothesis", "project", "procedure",
                        "preference", "system", "core", "index"
                    ]
                },
                "lifecycle": {
                    "type": "string",
                    "enum": ["RAW", "CLASSIFIED", "NORMALIZED", "REVIEW", "VERIFIED", "ACTIVE", "SUPERSEDED", "ARCHIVED"]
                },
                "category": {"type": "string"},
                "tags": {
                    "type": "array",
                    "items": {"type": "string"}
                },
                "created": {"type": "string"},
                "updated": {"type": "string"},
                "provenance": {"$ref": "#/definitions/Provenance"},
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
                        "anyOf": [
                            {"$ref": "#/definitions/Relation"},
                            {"type": "string"}
                        ]
                    }
                },
                "symbol": {"type": ["string", "null"]},
                "ticker": {"type": ["string", "null"]},
                "title": {"type": ["string", "null"]},
                "instrument_name": {"type": ["string", "null"]},
                "asset_class": {"type": ["string", "null"]},
                "price_data": {
                    "anyOf": [
                        {"$ref": "#/definitions/PriceData"},
                        {"type": "null"}
                    ]
                },
                "technical_indicators": {
                    "anyOf": [
                        {"$ref": "#/definitions/TechnicalIndicators"},
                        {"type": "null"}
                    ]
                },
                "indicators": {
                    "anyOf": [
                        {"$ref": "#/definitions/TechnicalIndicators"},
                        {"type": "object"},
                        {"type": "null"}
                    ]
                },
                "quantitative_signal": {
                    "anyOf": [
                        {"$ref": "#/definitions/QuantitativeSignal"},
                        {"type": "null"}
                    ]
                },
                "signals": {
                    "type": ["array", "null"],
                    "items": {
                        "anyOf": [
                            {"$ref": "#/definitions/QuantitativeSignal"},
                            {"type": "object"}
                        ]
                    }
                },
                "risk_metrics": {
                    "anyOf": [
                        {"$ref": "#/definitions/RiskMetrics"},
                        {"type": "null"}
                    ]
                },
                "macro_context": {
                    "anyOf": [
                        {"$ref": "#/definitions/MacroContext"},
                        {"type": "null"}
                    ]
                },
                "commentary": {
                    "anyOf": [
                        {"$ref": "#/definitions/Commentary"},
                        {"type": "null"}
                    ]
                },
                "narrative": {"type": ["string", "null"]},
                "raw_content": {"type": ["string", "null"]},
                "content_markdown": {"type": ["string", "null"]}
            },
            "additionalProperties": True
        },
        # Variant B: Nested Frontmatter Note Payload (e.g. FinancialNotePayload)
        {
            "type": "object",
            "required": ["frontmatter"],
            "properties": {
                "frontmatter": {
                    "type": "object",
                    "required": [
                        "id", "type", "lifecycle", "category", "tags",
                        "created", "updated", "provenance", "confidence",
                        "verification", "relations"
                    ],
                    "properties": {
                        "id": {"$ref": "#/definitions/UUID"},
                        "type": {
                            "type": "string",
                            "enum": [
                                "knowledge", "decision", "experience", "error", "lesson",
                                "resource", "hypothesis", "project", "procedure",
                                "preference", "system", "core", "index"
                            ]
                        },
                        "lifecycle": {
                            "type": "string",
                            "enum": ["RAW", "CLASSIFIED", "NORMALIZED", "REVIEW", "VERIFIED", "ACTIVE", "SUPERSEDED", "ARCHIVED"]
                        },
                        "category": {"type": "string"},
                        "tags": {"type": "array", "items": {"type": "string"}},
                        "created": {"type": "string"},
                        "updated": {"type": "string"},
                        "provenance": {"$ref": "#/definitions/Provenance"},
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
                                "anyOf": [
                                    {"$ref": "#/definitions/Relation"},
                                    {"type": "string"}
                                ]
                            }
                        }
                    },
                    "additionalProperties": True
                },
                "title": {"type": ["string", "null"]},
                "symbol": {"type": ["string", "null"]},
                "category": {"type": "string"},
                "indicators": {
                    "anyOf": [
                        {"$ref": "#/definitions/TechnicalIndicators"},
                        {"type": "object"},
                        {"type": "null"}
                    ]
                },
                "technical_indicators": {
                    "anyOf": [
                        {"$ref": "#/definitions/TechnicalIndicators"},
                        {"type": "null"}
                    ]
                },
                "signals": {
                    "type": ["array", "null"],
                    "items": {
                        "anyOf": [
                            {"$ref": "#/definitions/QuantitativeSignal"},
                            {"type": "object"}
                        ]
                    }
                },
                "quantitative_signal": {
                    "anyOf": [
                        {"$ref": "#/definitions/QuantitativeSignal"},
                        {"type": "null"}
                    ]
                },
                "risk_metrics": {
                    "anyOf": [
                        {"$ref": "#/definitions/RiskMetrics"},
                        {"type": "null"}
                    ]
                },
                "macro_context": {
                    "anyOf": [
                        {"$ref": "#/definitions/MacroContext"},
                        {"type": "null"}
                    ]
                },
                "commentary": {
                    "anyOf": [
                        {"$ref": "#/definitions/Commentary"},
                        {"type": "null"}
                    ]
                },
                "narrative": {"type": ["string", "null"]},
                "raw_content": {"type": ["string", "null"]}
            },
            "additionalProperties": True
        },
        # Variant C: Raw Financial Note Payload (Constrained, non-wildcard)
        {
            "type": "object",
            "required": ["title", "category"],
            "properties": {
                "title": {"type": "string"},
                "symbol": {"type": ["string", "null"]},
                "category": {"type": "string"},
                "indicators": {
                    "anyOf": [
                        {"$ref": "#/definitions/TechnicalIndicators"},
                        {"type": "object"},
                        {"type": "null"}
                    ]
                },
                "technical_indicators": {
                    "anyOf": [
                        {"$ref": "#/definitions/TechnicalIndicators"},
                        {"type": "null"}
                    ]
                },
                "signals": {
                    "type": ["array", "null"],
                    "items": {
                        "anyOf": [
                            {"$ref": "#/definitions/QuantitativeSignal"},
                            {"type": "object"}
                        ]
                    }
                },
                "quantitative_signal": {
                    "anyOf": [
                        {"$ref": "#/definitions/QuantitativeSignal"},
                        {"type": "null"}
                    ]
                },
                "risk_metrics": {
                    "anyOf": [
                        {"$ref": "#/definitions/RiskMetrics"},
                        {"type": "null"}
                    ]
                },
                "macro_context": {
                    "anyOf": [
                        {"$ref": "#/definitions/MacroContext"},
                        {"type": "null"}
                    ]
                },
                "commentary": {
                    "anyOf": [
                        {"$ref": "#/definitions/Commentary"},
                        {"type": "null"}
                    ]
                },
                "narrative": {"type": ["string", "null"]},
                "raw_content": {"type": ["string", "null"]},
                "price_data": {
                    "anyOf": [
                        {"$ref": "#/definitions/PriceData"},
                        {"type": "null"}
                    ]
                }
            },
            "additionalProperties": False
        }
    ]
}


# ============================================================================
# 3. SCHEMA VALIDATOR FUNCTION
# ============================================================================

def validate_financial_note(
    data: Dict[str, Any],
    is_ai_agent: bool = True
) -> Tuple[bool, List[str]]:
    """
    Validates an input dictionary against FINANCIAL_NOTE_SCHEMA and enforces
    Trust Boundary Invariants (P0-P18).

    Args:
        data: Financial note dictionary (canonical note or payload).
        is_ai_agent: If True, enforces strict AI agent trust boundary invariants:
          - P0: Cannot produce verification='verified' (allowed: partially_verified, unverified, inferred).
          - P2: Cannot claim privileged provenance source_type ('user', 'official', 'experience', 'import').
          - P3: Cannot directly propose into 'ACTIVE', 'VERIFIED', 'SUPERSEDED', or 'ARCHIVED'.

    Returns:
        Tuple[bool, List[str]]: (is_valid, error_messages)
    """
    if not isinstance(data, dict):
        return False, ["Input data must be a dictionary"]

    errors: List[str] = []

    # 1. Validate Draft-07 JSON Schema
    validator = Draft7Validator(FINANCIAL_NOTE_SCHEMA, format_checker=FormatChecker())
    for error in validator.iter_errors(data):
        path = " -> ".join(str(p) for p in error.path) if error.path else "root"
        errors.append(f"Schema error at [{path}]: {error.message}")

    # Extract frontmatter subdict if present or root dict
    fm = data.get("frontmatter") if isinstance(data.get("frontmatter"), dict) else data

    # 2. Strict UUID enforcement for ID if id is present
    if "id" in fm:
        id_val = fm.get("id")
        if id_val is None:
            errors.append("Invalid UUID for id: 'None' (ID cannot be null)")
        elif not isinstance(id_val, str) or not id_val.strip():
            errors.append(f"Invalid UUID for id: '{id_val}'")
        else:
            try:
                val = uuid.UUID(id_val.strip())
                if id_val.strip().lower() != str(val).lower():
                    errors.append(f"Invalid UUID string format for id: '{id_val}'")
            except (ValueError, AttributeError, TypeError):
                errors.append(f"Invalid UUID for id: '{id_val}'")

    # 3. Canonical Frontmatter Enum Whitelisting & Type Guards
    if "type" in fm:
        mem_type = fm.get("type")
        if not isinstance(mem_type, str):
            errors.append(f"Type must be a string, got {type(mem_type).__name__}")
        else:
            allowed_types = {
                "knowledge", "decision", "experience", "error", "lesson",
                "resource", "hypothesis", "project", "procedure",
                "preference", "system", "core", "index"
            }
            if mem_type not in allowed_types:
                errors.append(f"Invalid type: '{mem_type}'. Allowed: {allowed_types}")

    if "confidence" in fm:
        confidence = fm.get("confidence")
        if not isinstance(confidence, str):
            errors.append(f"Confidence must be a string, got {type(confidence).__name__}")
        else:
            allowed_confidence = {"very_high", "high", "medium", "low", "unknown"}
            if confidence not in allowed_confidence:
                errors.append(f"Invalid confidence: '{confidence}'. Allowed: {allowed_confidence}")

    # 4. Invariant Enforcement (P0, P2, P3)
    if "verification" in fm:
        verification = fm.get("verification")
        if not isinstance(verification, str):
            errors.append(f"Verification must be a string, got {type(verification).__name__}")
        else:
            ver_clean = verification.strip().lower()
            allowed_verification = {"verified", "partially_verified", "unverified", "inferred"}
            if ver_clean not in allowed_verification or verification != ver_clean:
                errors.append(f"Invalid verification status: '{verification}'. Allowed: {allowed_verification}")
            elif is_ai_agent and ver_clean == "verified":
                errors.append(
                    "Trust Boundary Violation (P0): AI agents cannot produce verification='verified'. "
                    "Permitted: 'partially_verified', 'unverified', 'inferred'. Human attestation required."
                )

    if "provenance" in fm:
        prov = fm.get("provenance")
        if not isinstance(prov, dict):
            errors.append(f"Provenance must be a dictionary, got {type(prov).__name__}")
        else:
            src_type = prov.get("source_type")
            if src_type is not None:
                if not isinstance(src_type, str):
                    errors.append(f"Provenance source_type must be a string, got {type(src_type).__name__}")
                else:
                    src_clean = src_type.strip().lower()
                    all_sources = {"user", "official", "execution", "experience", "ai", "inference", "import", "unknown"}
                    if src_clean not in all_sources or src_type != src_clean:
                        errors.append(f"Invalid provenance source_type: '{src_type}'. Allowed: {all_sources}")
                    elif is_ai_agent and src_clean in {"user", "official", "experience", "import"}:
                        errors.append(
                            f"Trust Boundary Violation (P2): AI agents cannot claim privileged source_type '{src_type}'. "
                            "Permitted: 'execution', 'ai', 'inference', 'unknown'."
                        )

    if "lifecycle" in fm:
        lifecycle = fm.get("lifecycle")
        if not isinstance(lifecycle, str):
            errors.append(f"Lifecycle must be a string, got {type(lifecycle).__name__}")
        else:
            lc_clean = lifecycle.strip().upper()
            all_lifecycles = {"RAW", "CLASSIFIED", "NORMALIZED", "REVIEW", "VERIFIED", "ACTIVE", "SUPERSEDED", "ARCHIVED"}
            if lc_clean not in all_lifecycles or lifecycle != lc_clean:
                errors.append(f"Invalid lifecycle: '{lifecycle}'. Allowed: {all_lifecycles}")
            elif is_ai_agent and lc_clean in {"ACTIVE", "VERIFIED", "SUPERSEDED", "ARCHIVED"}:
                errors.append(
                    f"Trust Boundary Violation (P3): AI agents can only propose into {{RAW, CLASSIFIED, NORMALIZED, REVIEW}}. "
                    f"Direct creation into '{lifecycle}' is prohibited."
                )

    return len(errors) == 0, errors


# ============================================================================
# 4. PYDANTIC V2 DOMAIN MODELS
# ============================================================================

class ProvenanceModel(BaseModel):
    model_config = ConfigDict(extra="allow", populate_by_name=True)
    source_type: str = Field(default="execution", description="Provenance origin per P2 invariant")
    source_ref: str = Field(default="financial_pipeline", description="System module or tool reference")
    source_date: Optional[str] = Field(None, description="ISO-8601 source date YYYY-MM-DD")
    extraction_date: Optional[str] = Field(None, description="ISO-8601 extraction date YYYY-MM-DD")
    original_path: Optional[str] = None
    redaction: str = Field(default="none")
    provenance_status: str = Field(default="complete")
    timestamp: Optional[str] = None

    @field_validator("source_type")
    @classmethod
    def validate_source_type_allowed(cls, v: str) -> str:
        allowed = {"user", "official", "execution", "experience", "ai", "inference", "import", "unknown"}
        if v not in allowed:
            raise ValueError(f"Invalid source_type '{v}'. Allowed: {allowed}")
        return v


class RelationModel(BaseModel):
    model_config = ConfigDict(extra="allow", populate_by_name=True)
    relation: str = Field(..., description="Semantic wikilink relationship type")
    target: str = Field(..., description="Obsidian wikilink target e.g. [[Target]]")
    target_id: Optional[str] = Field(None, description="Target note UUID")


class FinancialFrontmatter(BaseModel):
    model_config = ConfigDict(extra="allow", populate_by_name=True)
    id: str = Field(default_factory=lambda: str(uuid.uuid4()), description="Valid UUID4 string")
    type: str = Field(default="knowledge", description="Canonical memory type")
    lifecycle: str = Field(default="REVIEW", description="Lifecycle state (defaults to REVIEW per P3)")
    category: str = Field(default="financial", description="Category")
    tags: List[str] = Field(default_factory=list, description="Categorization tags")
    created: str = Field(default_factory=lambda: datetime.now(timezone.utc).strftime("%Y-%m-%d"), description="ISO-8601 creation date")
    updated: str = Field(default_factory=lambda: datetime.now(timezone.utc).strftime("%Y-%m-%d"), description="ISO-8601 update date")
    provenance: Union[ProvenanceModel, Dict[str, Any]] = Field(
        default_factory=lambda: {"source_type": "execution", "source_ref": "financial_ingestion_pipeline"}
    )
    confidence: str = Field(default="high", description="Confidence level")
    verification: str = Field(default="partially_verified", description="Verification level")
    relations: List[Union[RelationModel, Dict[str, Any], str]] = Field(default_factory=list)

    @field_validator("id")
    @classmethod
    def validate_uuid_id(cls, v: str) -> str:
        try:
            val = uuid.UUID(str(v))
            return str(val)
        except (ValueError, AttributeError, TypeError):
            raise ValueError(f"Invalid UUID format for id: '{v}'")

    @field_validator("verification")
    @classmethod
    def validate_verification_allowed(cls, v: str) -> str:
        allowed = {"verified", "partially_verified", "unverified", "inferred"}
        if v not in allowed:
            raise ValueError(f"Invalid verification '{v}'. Allowed: {allowed}")
        return v

    @field_validator("lifecycle")
    @classmethod
    def validate_lifecycle_allowed(cls, v: str) -> str:
        allowed = {"RAW", "CLASSIFIED", "NORMALIZED", "REVIEW", "VERIFIED", "ACTIVE", "SUPERSEDED", "ARCHIVED"}
        if v not in allowed:
            raise ValueError(f"Invalid lifecycle '{v}'. Allowed: {allowed}")
        return v


class PriceDataPayload(BaseModel):
    model_config = ConfigDict(extra="allow", populate_by_name=True)
    open: Optional[float] = None
    high: Optional[float] = None
    low: Optional[float] = None
    close: float = Field(default=0.0)
    change_day_pct: float = Field(default=0.0)
    change_week_pct: Optional[float] = None
    change_month_pct: Optional[float] = None
    volume: int = Field(default=0)
    avg_volume_20d: Optional[int] = None
    rvol: float = Field(default=1.0)


class FinancialIndicators(BaseModel):
    model_config = ConfigDict(extra="allow", populate_by_name=True)
    rsi_14: Optional[float] = Field(default=50.0, ge=0.0, le=100.0)
    rsi_status: Optional[str] = Field(default="Echilibru")
    macd: Optional[float] = None
    macd_signal: Optional[float] = None
    macd_hist: Optional[float] = None
    macd_cross: Optional[str] = Field(default="Neutru")
    ma20: Optional[float] = None
    ma50: Optional[float] = None
    ma200: Optional[float] = None
    ma_cross: Optional[str] = Field(default="Neutru")
    trend: Optional[str] = Field(default="Sideways")
    bb_mid: Optional[float] = None
    bb_sup: Optional[float] = None
    bb_inf: Optional[float] = None
    bb_width: Optional[float] = None
    atr_14: Optional[float] = Field(default=0.0, ge=0.0)
    stoch_k: Optional[float] = None
    stoch_d: Optional[float] = None
    momentum_10d: Optional[float] = None
    support_20d: Optional[float] = None
    resistance_20d: Optional[float] = None
    rvol: Optional[float] = 1.0


class TechnicalIndicatorsPayload(FinancialIndicators):
    pass


class TradeSignal(BaseModel):
    model_config = ConfigDict(extra="allow", populate_by_name=True)
    signal: str = Field(default="WAIT")  # BUY | SELL | WAIT
    score: int = Field(default=0, ge=-5, le=5)
    confluences: int = Field(default=0, ge=0, le=5)
    stop_loss: Optional[float] = None
    take_profit: Optional[float] = None
    risk_reward_ratio: Optional[float] = None
    win_probability_pct: Optional[float] = Field(default=35.0, ge=35.0, le=90.0)
    timeframe: Optional[str] = "1D"
    trigger_condition: Optional[str] = None
    status: Optional[str] = "In asteptare"


class QuantitativeSignalPayload(TradeSignal):
    pass


class RiskMetrics(BaseModel):
    model_config = ConfigDict(extra="allow", populate_by_name=True)
    impact: Optional[int] = Field(default=3, ge=1, le=5)
    probability_pct: Optional[float] = Field(default=50.0, ge=0.0, le=100.0)
    score: Optional[float] = None
    horizon: Optional[str] = None
    actions: Optional[str] = None
    sl_atr_multiple: Optional[float] = 1.5
    tp_atr_multiple: Optional[float] = 3.0
    planned_rr: Optional[float] = 2.0


class MacroContextPayload(BaseModel):
    model_config = ConfigDict(extra="allow", populate_by_name=True)
    vix: Optional[float] = None
    yield_10y: Optional[float] = None
    yield_2y: Optional[float] = None
    yield_30y: Optional[float] = None
    usd_index: Optional[float] = None
    fear_greed_index: Optional[int] = None
    fed_funds_rate: Optional[float] = None
    cpi: Optional[float] = None
    unemployment_rate: Optional[float] = None
    gdp: Optional[float] = None


class MarketCommentaryPayload(BaseModel):
    model_config = ConfigDict(extra="allow", populate_by_name=True)
    movement_explanation: Optional[str] = None
    opportunity_alert: Optional[str] = None
    educational_lesson: Optional[str] = None


class FinancialNotePayload(BaseModel):
    model_config = ConfigDict(extra="allow", populate_by_name=True)
    frontmatter: Optional[FinancialFrontmatter] = None
    title: str = Field(default="")
    symbol: Optional[str] = None
    category: str = Field(default="indici")
    indicators: Dict[str, Any] = Field(default_factory=dict)
    signals: List[Dict[str, Any]] = Field(default_factory=list)
    risk_metrics: Dict[str, Any] = Field(default_factory=dict)
    narrative: str = Field(default="")
    raw_content: str = Field(default="")


class FinancialNoteModel(BaseModel):
    model_config = ConfigDict(extra="allow", populate_by_name=True)
    id: str = Field(default_factory=lambda: str(uuid.uuid4()), description="Canonical Note UUID")
    type: MemoryTypeEnum = Field(default=MemoryTypeEnum.KNOWLEDGE)
    lifecycle: LifecycleEnum = Field(default=LifecycleEnum.REVIEW)
    category: str = Field(default="financial-asset-profile")
    tags: List[str] = Field(default_factory=list)
    created: str = Field(default_factory=lambda: datetime.now(timezone.utc).strftime("%Y-%m-%d"))
    updated: str = Field(default_factory=lambda: datetime.now(timezone.utc).strftime("%Y-%m-%d"))
    provenance: Union[ProvenanceModel, Dict[str, Any]] = Field(
        default_factory=lambda: {"source_type": "execution", "source_ref": "financial_ingestion_pipeline"}
    )
    confidence: ConfidenceEnum = Field(default=ConfidenceEnum.HIGH)
    verification: VerificationEnum = Field(default=VerificationEnum.PARTIALLY_VERIFIED)
    relations: List[Union[RelationModel, Dict[str, Any], str]] = Field(default_factory=list)

    ticker: Optional[str] = None
    symbol: Optional[str] = None
    instrument_name: Optional[str] = None
    title: Optional[str] = None
    asset_class: Optional[str] = None

    price_data: Optional[Union[PriceDataPayload, Dict[str, Any]]] = None
    technical_indicators: Optional[Union[TechnicalIndicatorsPayload, FinancialIndicators, Dict[str, Any]]] = None
    indicators: Optional[Union[TechnicalIndicatorsPayload, FinancialIndicators, Dict[str, Any]]] = None
    quantitative_signal: Optional[Union[QuantitativeSignalPayload, TradeSignal, Dict[str, Any]]] = None
    signals: Optional[List[Union[QuantitativeSignalPayload, TradeSignal, Dict[str, Any]]]] = None
    risk_metrics: Optional[Union[RiskMetrics, Dict[str, Any]]] = None
    macro_context: Optional[Union[MacroContextPayload, Dict[str, Any]]] = None
    commentary: Optional[Union[MarketCommentaryPayload, Dict[str, Any]]] = None
    narrative: Optional[str] = None
    raw_content: Optional[str] = None
    content_markdown: Optional[str] = None

    @field_validator("id")
    @classmethod
    def validate_uuid_id(cls, v: str) -> str:
        try:
            val = uuid.UUID(str(v))
            return str(val)
        except (ValueError, AttributeError, TypeError):
            raise ValueError(f"Invalid UUID format for id: '{v}'")
```

---

### 4.2 Proposed Test Suite Hardening: `tests/financial/test_schema.py`

To address the Forensic Auditor's finding regarding omitted negative tests, add the following test classes to `tests/financial/test_schema.py`:

```python
class TestDraft07NegativeSchemaValidation:
    """Negative validation test cases ensuring malformed structures are rejected by schema."""

    def test_schema_rejects_missing_required_frontmatter_fields(self, valid_canonical_note_dict):
        """Omitting required canonical fields must fail Draft-07 validation."""
        required_fields = ["id", "type", "lifecycle", "category", "tags", "created", "updated", "provenance", "confidence", "verification", "relations"]
        for req in required_fields:
            bad_note = valid_canonical_note_dict.copy()
            del bad_note[req]
            validator = Draft7Validator(FINANCIAL_NOTE_SCHEMA, format_checker=FormatChecker())
            errors = list(validator.iter_errors(bad_note))
            assert len(errors) > 0, f"Expected Draft-07 schema error for missing field '{req}'"

    def test_schema_rejects_out_of_bounds_indicators(self, valid_canonical_note_dict):
        """Indicators with values exceeding mathematical bounds must fail schema validation."""
        bad_rsi_note = valid_canonical_note_dict.copy()
        bad_rsi_note["technical_indicators"] = {"rsi_14": 150.0}
        validator = Draft7Validator(FINANCIAL_NOTE_SCHEMA, format_checker=FormatChecker())
        errors = list(validator.iter_errors(bad_rsi_note))
        assert len(errors) > 0, "Expected schema rejection for RSI 150.0"

    def test_schema_rejects_invalid_provenance_source_type(self, valid_canonical_note_dict):
        """Invalid provenance source_type must fail schema validation."""
        bad_prov_note = valid_canonical_note_dict.copy()
        bad_prov_note["provenance"] = {"source_type": "invalid_source", "source_ref": "test"}
        validator = Draft7Validator(FINANCIAL_NOTE_SCHEMA, format_checker=FormatChecker())
        errors = list(validator.iter_errors(bad_prov_note))
        assert len(errors) > 0, "Expected schema rejection for invalid provenance source_type"

    def test_schema_rejects_corrupted_relations_list(self, valid_canonical_note_dict):
        """Non-object, non-string items in relations must fail schema validation."""
        bad_rel_note = valid_canonical_note_dict.copy()
        bad_rel_note["relations"] = [12345]
        validator = Draft7Validator(FINANCIAL_NOTE_SCHEMA, format_checker=FormatChecker())
        errors = list(validator.iter_errors(bad_rel_note))
        assert len(errors) > 0, "Expected schema rejection for relations containing integers"


class TestPydanticUnionBaseClassPolymorphism:
    """Ensures FinancialNoteModel accepts base classes in union fields."""

    def test_financial_note_model_accepts_base_financial_indicators(self, valid_canonical_note_dict):
        base_ind = FinancialIndicators(rsi_14=48.5, atr_14=15.0)
        note = valid_canonical_note_dict.copy()
        note["technical_indicators"] = base_ind
        model = FinancialNoteModel(**note)
        assert model.technical_indicators.rsi_14 == 48.5

    def test_financial_note_model_accepts_base_trade_signal(self, valid_canonical_note_dict):
        base_sig = TradeSignal(signal="BUY", score=3)
        note = valid_canonical_note_dict.copy()
        note["quantitative_signal"] = base_sig
        model = FinancialNoteModel(**note)
        assert model.quantitative_signal.signal == "BUY"
```

---

## 5. Verification & Test Plan

To independently verify the complete fix across all test tracks:

1. **Run Full Test Suite**:
   ```powershell
   python -m pytest tests/financial/test_schema.py tests/financial/test_challenger_m1_adversarial.py tests/financial/test_challenger_m1_invariants.py -v
   ```
   **Expected Outcome**: **100% PASS** (0 failures across all 280+ tests).

2. **Verify Specific Adversarial & Invariant Vectors**:
   - `test_defect_1_schema_bypass_on_corrupted_provenance` -> PASSED
   - `test_defect_1_schema_bypass_on_corrupted_relations` -> PASSED
   - `test_defect_2_unhandled_type_error_on_unhashable_lifecycle` -> PASSED
   - `test_defect_2_unhandled_type_error_on_unhashable_source_type` -> PASSED
   - `test_defect_3_none_id_accepted_as_valid` -> PASSED
   - `test_forged_and_variant_verification_strings_rejected` -> PASSED (all variants)
   - `test_unregistered_or_case_mismatched_provenance_rejected` -> PASSED (all variants)
   - `test_invalid_lifecycle_enums_rejected` -> PASSED (all variants)
   - `test_mathematical_bounds_rejected` -> PASSED (all bounds)

3. **Verify Zero Secrets**:
   Ensure automated secret scanning confirms 0 hardcoded credentials or tokens in persisted models or code.
