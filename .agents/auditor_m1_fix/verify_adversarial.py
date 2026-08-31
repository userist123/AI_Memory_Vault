import sys
import os
sys.path.insert(0, os.path.abspath("."))
import uuid
import pytest
from jsonschema import Draft7Validator, FormatChecker
from memory_controller.financial_schema import (
    FINANCIAL_NOTE_SCHEMA,
    validate_financial_note,
    FinancialFrontmatter,
    FinancialNotePayload,
    FinancialNoteModel,
    FinancialIndicators,
    TradeSignal,
    TechnicalIndicatorsPayload,
    QuantitativeSignalPayload,
    RiskMetrics,
    LifecycleEnum,
    VerificationEnum,
    ConfidenceEnum,
    MemoryTypeEnum
)

print("Starting Comprehensive Empirical Forensic Verification...")

# 1. Variant C & Schema Bypass Checks
print("\n--- Testing Variant C & Catch-All Bypasses ---")
assert not validate_financial_note({}, is_ai_agent=True)[0], "Empty dict must fail!"
assert not validate_financial_note({"title": "Test"}, is_ai_agent=True)[0], "Missing category must fail!"
assert not validate_financial_note({"category": "indici"}, is_ai_agent=True)[0], "Missing title must fail!"
assert not validate_financial_note({"title": "Test", "category": "indici", "unauthorized_extra_key": 123}, is_ai_agent=True)[0], "Additional property must fail!"
assert not validate_financial_note({"title": "Test", "category": "indici", "technical_indicators": {"rsi_14": 150.0}}, is_ai_agent=True)[0], "RSI > 100 must fail!"

# 2. P0 Checks
print("\n--- Testing P0: AI Self-Verification Gate ---")
base_note = {
    "id": str(uuid.uuid4()),
    "type": "knowledge",
    "lifecycle": "REVIEW",
    "category": "financial-asset-profile",
    "tags": ["finance"],
    "created": "2026-08-26",
    "updated": "2026-08-26",
    "provenance": {"source_type": "execution", "source_ref": "test"},
    "confidence": "high",
    "verification": "partially_verified",
    "relations": []
}

# AI agent cannot set verified
ai_verified = base_note.copy()
ai_verified["verification"] = "verified"
ok, errs = validate_financial_note(ai_verified, is_ai_agent=True)
assert not ok and any("P0" in e for e in errs), f"P0 failed: {errs}"

# Human can set verified
ok_human, errs_human = validate_financial_note(ai_verified, is_ai_agent=False)
assert ok_human and len(errs_human) == 0, f"Human verified failed: {errs_human}"

# Forged / case mismatch rejected
forged_ver = base_note.copy()
forged_ver["verification"] = "VERIFIED"
ok_forged, _ = validate_financial_note(forged_ver, is_ai_agent=True)
assert not ok_forged, "Uppercase VERIFIED must be rejected!"

# 3. P2 Checks
print("\n--- Testing P2: Privileged Provenance Isolation ---")
for priv in ["user", "official", "experience", "import"]:
    prov_note = base_note.copy()
    prov_note["provenance"] = {"source_type": priv, "source_ref": "test"}
    ok_ai, errs = validate_financial_note(prov_note, is_ai_agent=True)
    assert not ok_ai and any("P2" in e for e in errs), f"P2 check failed for {priv}: {errs}"
    ok_h, _ = validate_financial_note(prov_note, is_ai_agent=False)
    assert ok_h, f"Human must be able to claim {priv}"

for perm in ["execution", "ai", "inference", "unknown"]:
    prov_note = base_note.copy()
    prov_note["provenance"] = {"source_type": perm, "source_ref": "test"}
    ok_ai, errs = validate_financial_note(prov_note, is_ai_agent=True)
    assert ok_ai, f"AI must be permitted {perm}: {errs}"

# 4. P3 Checks
print("\n--- Testing P3: Creation Lifecycle Scoping ---")
for esc in ["ACTIVE", "VERIFIED", "SUPERSEDED", "ARCHIVED"]:
    lc_note = base_note.copy()
    lc_note["lifecycle"] = esc
    ok_ai, errs = validate_financial_note(lc_note, is_ai_agent=True)
    assert not ok_ai and any("P3" in e for e in errs), f"P3 check failed for {esc}: {errs}"

for perm in ["RAW", "CLASSIFIED", "NORMALIZED", "REVIEW"]:
    lc_note = base_note.copy()
    lc_note["lifecycle"] = perm
    ok_ai, errs = validate_financial_note(lc_note, is_ai_agent=True)
    assert ok_ai, f"AI must be permitted lifecycle {perm}: {errs}"

# 5. Type Safety & Unhashable Objects
print("\n--- Testing Unhashable & Corrupted Types Exception Safety ---")
corrupted = [
    {"lifecycle": {"unhashable": "dict"}},
    {"type": ["unhashable", "list"]},
    {"confidence": 12345},
    {"provenance": {"source_type": {"nested": "dict"}, "source_ref": "test"}},
    {"verification": ["invalid"]},
    {"id": None}
]
for item in corrupted:
    test_d = base_note.copy()
    test_d.update(item)
    ok, errs = validate_financial_note(test_d, is_ai_agent=True)
    assert not ok, f"Corrupted item must fail: {item}"
    assert len(errs) > 0

# 6. Pydantic Polymorphism & Validation
print("\n--- Testing Pydantic Polymorphism ---")
note_with_base_ind = base_note.copy()
note_with_base_ind["technical_indicators"] = FinancialIndicators(rsi_14=42.0)
note_with_base_ind["quantitative_signal"] = TradeSignal(signal="BUY", score=2)
model = FinancialNoteModel(**note_with_base_ind)
assert model.technical_indicators.rsi_14 == 42.0
assert model.quantitative_signal.signal == "BUY"

print("\n>>> ALL 6 FORENSIC MODULES VERIFIED WITH 100% EMPIRICAL INTEGRITY <<<")
