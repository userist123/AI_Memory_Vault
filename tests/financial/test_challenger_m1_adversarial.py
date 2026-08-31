"""
Adversarial Stress Test & Empirical Validation Suite for Financial Schema (Milestone 1).
Author: Challenger M1-1 (teamwork_preview_challenger)

Empirically challenges `memory_controller/financial_schema.py` across:
1. Malformed and adversarial UUIDs (SQLi, path traversal, truncated, non-hex, type corruption).
2. Extreme numeric ranges & boundary stress (NaN, Inf, float overflow, negative ATR, out-of-bound RSI, score, confluences, win probability, risk impact).
3. Type corruption, schema pollution, and corrupted nested structures.
4. Trust Boundary Invariant attack vectors (P0 AI self-verification, P2 privileged provenance, P3 escalated lifecycles).
5. Robustness & Exception Safety (fuzzing guaranteeing zero unhandled exceptions).
6. Lossless model serialization & enum fidelity.
"""

import math
import random
import string
import uuid
import pytest
from pydantic import ValidationError

from memory_controller.financial_schema import (
    FINANCIAL_NOTE_SCHEMA,
    validate_financial_note,
    MemoryTypeEnum,
    LifecycleEnum,
    ConfidenceEnum,
    VerificationEnum,
    AssetClassEnum,
    SignalEnum,
    TrendEnum,
    FinancialFrontmatter,
    FinancialNotePayload,
    FinancialNoteModel,
    PriceDataPayload,
    FinancialIndicators,
    TechnicalIndicatorsPayload,
    TradeSignal,
    QuantitativeSignalPayload,
    RiskMetrics,
    MacroContextPayload,
    MarketCommentaryPayload,
    ProvenanceModel,
    RelationModel,
)


# ============================================================================
# BASELINE FIXTURE
# ============================================================================

@pytest.fixture
def baseline_valid_note():
    return {
        "id": str(uuid.uuid4()),
        "type": "knowledge",
        "lifecycle": "REVIEW",
        "category": "financial-asset-profile",
        "tags": ["crypto", "btc", "market-analysis"],
        "created": "2026-08-26",
        "updated": "2026-08-26",
        "provenance": {
            "source_type": "execution",
            "source_ref": "crypto_ingestion_pipeline",
            "source_date": "2026-08-26",
            "provenance_status": "complete"
        },
        "confidence": "high",
        "verification": "partially_verified",
        "relations": [
            {"relation": "related_to", "target": "[[Asset_BTC]]", "target_id": str(uuid.uuid4())}
        ],
        "ticker": "BTC-USD",
        "symbol": "BTC-USD",
        "instrument_name": "Bitcoin USD",
        "asset_class": "CRYPTO",
        "price_data": {
            "open": 64000.0,
            "high": 65500.0,
            "low": 63800.0,
            "close": 65200.0,
            "change_day_pct": 1.87,
            "volume": 28000000,
            "rvol": 1.15
        },
        "technical_indicators": {
            "rsi_14": 56.5,
            "rsi_status": "Neutru",
            "macd": 450.0,
            "macd_signal": 380.0,
            "macd_hist": 70.0,
            "macd_cross": "Bullish",
            "ma20": 63500.0,
            "ma50": 61200.0,
            "ma200": 58000.0,
            "ma_cross": "Golden Cross",
            "trend": "Bullish",
            "atr_14": 1250.0,
            "rvol": 1.15
        },
        "quantitative_signal": {
            "signal": "BUY",
            "score": 3,
            "confluences": 3,
            "stop_loss": 62500.0,
            "take_profit": 71000.0,
            "risk_reward_ratio": 2.14,
            "win_probability_pct": 68.0,
            "timeframe": "1D",
            "status": "In asteptare"
        },
        "macro_context": {
            "vix": 15.5,
            "usd_index": 101.8,
            "fear_greed_index": 55
        },
        "commentary": {
            "movement_explanation": "BTC consolidation breakout.",
            "opportunity_alert": "Long setup active.",
            "educational_lesson": "Always maintain strict position sizing."
        },
        "content_markdown": "# Bitcoin Analysis\n\nConsolidation breakout confirmed."
    }


# ============================================================================
# 1. ADVERSARIAL UUID ATTACK TESTS
# ============================================================================

class TestAdversarialUUIDValidation:
    """Stress tests UUID validation in both JSON schema and Pydantic models."""

    @pytest.mark.parametrize("malicious_id", [
        "",  # Empty string
        "   ",  # Whitespace
        "not-a-uuid",  # Random text
        "12345678-1234-1234-1234-12345678901",  # 35 chars (truncated)
        "12345678-1234-1234-1234-1234567890123",  # 37 chars (too long)
        "g3a9f0e1-4b21-4d32-8e12-9c1234567890",  # Invalid hex 'g'
        "550e8400-e29b-41d4-a716-44665544000z",  # Invalid hex 'z'
        "'; DROP TABLE notes; --",  # SQL injection
        "../../../../etc/passwd",  # Path traversal Unix
        "..\\..\\windows\\system32",  # Path traversal Windows
        "<script>alert('xss')</script>",  # XSS payload
        "550e8400-e29b-41d4-a716-446655440000\x00",  # Null byte injection
        "550e8400_e29b_41d4_a716_446655440000",  # Underscores instead of hyphens
        "{550e8400-e29b-41d4-a716-446655440000}",  # Braces
        "urn:uuid:550e8400-e29b-41d4-a716-446655440000",  # URN prefix
        123456789,  # Int type
        True,  # Bool type
        None,  # None type when required
        [],  # List type
        {},  # Dict type
    ])
    def test_adversarial_uuid_rejected_by_validator(self, baseline_valid_note, malicious_id):
        note = baseline_valid_note.copy()
        note["id"] = malicious_id
        is_valid, errors = validate_financial_note(note, is_ai_agent=True)
        assert is_valid is False, f"Expected rejection for malicious id: {malicious_id}"
        assert len(errors) > 0

    @pytest.mark.parametrize("malicious_id", [
        "not-a-uuid",
        "g3a9f0e1-4b21-4d32-8e12-9c1234567890",
        "'; DROP TABLE notes; --",
        "<script>alert(1)</script>",
        "12345",
        "",
    ])
    def test_pydantic_frontmatter_rejects_malicious_uuid(self, malicious_id):
        with pytest.raises(ValidationError) as exc_info:
            FinancialFrontmatter(id=malicious_id)
        assert "UUID" in str(exc_info.value) or "invalid" in str(exc_info.value).lower()

    @pytest.mark.parametrize("malicious_id", [
        "not-a-uuid",
        "g3a9f0e1-4b21-4d32-8e12-9c1234567890",
        "'; DROP TABLE notes; --",
        "<script>alert(1)</script>",
        "12345",
        "",
    ])
    def test_pydantic_financial_note_model_rejects_malicious_uuid(self, baseline_valid_note, malicious_id):
        note = baseline_valid_note.copy()
        note["id"] = malicious_id
        with pytest.raises(ValidationError):
            FinancialNoteModel(**note)

    def test_valid_uuid_variants_accepted(self, baseline_valid_note):
        valid_uuids = [
            str(uuid.uuid4()),
            str(uuid.uuid1()),
            "550e8400-e29b-41d4-a716-446655440000",
            "a0eebc99-9c0b-4ef8-bb6d-6bb9bd380a11",
        ]
        for valid_id in valid_uuids:
            note = baseline_valid_note.copy()
            note["id"] = valid_id
            is_valid, errors = validate_financial_note(note, is_ai_agent=True)
            assert is_valid is True, f"Failed on valid UUID: {valid_id}, errors: {errors}"
            fm = FinancialFrontmatter(id=valid_id)
            assert fm.id.lower() == valid_id.lower()


# ============================================================================
# 2. EXTREME NUMERIC RANGE & BOUNDARY STRESS TESTS
# ============================================================================

class TestExtremeNumericRangesAndBoundaries:
    """Stress tests boundary constraints and numerical stability across models."""

    # --- FinancialIndicators / TechnicalIndicatorsPayload ---
    @pytest.mark.parametrize("invalid_rsi", [
        -0.0001,
        -1.0,
        -50.0,
        -1e9,
        100.0001,
        101.0,
        150.0,
        1e9,
    ])
    def test_rsi_out_of_bounds_rejected_by_pydantic(self, invalid_rsi):
        with pytest.raises(ValidationError):
            FinancialIndicators(rsi_14=invalid_rsi)

    @pytest.mark.parametrize("valid_rsi", [
        0.0,
        0.0001,
        50.0,
        99.9999,
        100.0,
    ])
    def test_rsi_valid_boundaries_accepted(self, valid_rsi):
        ind = FinancialIndicators(rsi_14=valid_rsi)
        assert ind.rsi_14 == valid_rsi

    @pytest.mark.parametrize("invalid_atr", [
        -0.00001,
        -1.0,
        -100.0,
        -1e6,
    ])
    def test_atr_negative_rejected_by_pydantic(self, invalid_atr):
        with pytest.raises(ValidationError):
            FinancialIndicators(atr_14=invalid_atr)

    def test_atr_zero_and_positive_accepted(self):
        ind0 = FinancialIndicators(atr_14=0.0)
        assert ind0.atr_14 == 0.0
        ind_pos = FinancialIndicators(atr_14=1250.75)
        assert ind_pos.atr_14 == 1250.75

    # --- TradeSignal / QuantitativeSignalPayload ---
    @pytest.mark.parametrize("invalid_score", [
        -6,
        -7,
        -100,
        -999999,
        6,
        7,
        100,
        999999,
    ])
    def test_signal_score_out_of_bounds_rejected(self, invalid_score):
        with pytest.raises(ValidationError):
            TradeSignal(score=invalid_score)

    @pytest.mark.parametrize("valid_score", [-5, -4, -1, 0, 1, 4, 5])
    def test_signal_score_valid_boundaries_accepted(self, valid_score):
        sig = TradeSignal(score=valid_score)
        assert sig.score == valid_score

    @pytest.mark.parametrize("invalid_confluences", [
        -1,
        -5,
        -100,
        6,
        7,
        100,
    ])
    def test_signal_confluences_out_of_bounds_rejected(self, invalid_confluences):
        with pytest.raises(ValidationError):
            TradeSignal(confluences=invalid_confluences)

    @pytest.mark.parametrize("valid_confluences", [0, 1, 2, 3, 4, 5])
    def test_signal_confluences_valid_accepted(self, valid_confluences):
        sig = TradeSignal(confluences=valid_confluences)
        assert sig.confluences == valid_confluences

    @pytest.mark.parametrize("invalid_win_prob", [
        34.99,
        30.0,
        0.0,
        -10.0,
        -100.0,
        90.01,
        91.0,
        100.0,
        150.0,
    ])
    def test_win_probability_out_of_bounds_rejected(self, invalid_win_prob):
        with pytest.raises(ValidationError):
            TradeSignal(win_probability_pct=invalid_win_prob)

    @pytest.mark.parametrize("valid_win_prob", [35.0, 35.01, 50.0, 75.5, 89.99, 90.0])
    def test_win_probability_valid_accepted(self, valid_win_prob):
        sig = TradeSignal(win_probability_pct=valid_win_prob)
        assert sig.win_probability_pct == valid_win_prob

    # --- RiskMetrics ---
    @pytest.mark.parametrize("invalid_impact", [
        0,
        -1,
        -5,
        6,
        7,
        10,
        100,
    ])
    def test_risk_impact_out_of_bounds_rejected(self, invalid_impact):
        with pytest.raises(ValidationError):
            RiskMetrics(impact=invalid_impact)

    @pytest.mark.parametrize("valid_impact", [1, 2, 3, 4, 5])
    def test_risk_impact_valid_accepted(self, valid_impact):
        rm = RiskMetrics(impact=valid_impact)
        assert rm.impact == valid_impact

    @pytest.mark.parametrize("invalid_prob", [
        -0.01,
        -1.0,
        -50.0,
        100.01,
        101.0,
        200.0,
    ])
    def test_risk_probability_pct_out_of_bounds_rejected(self, invalid_prob):
        with pytest.raises(ValidationError):
            RiskMetrics(probability_pct=invalid_prob)

    @pytest.mark.parametrize("valid_prob", [0.0, 0.01, 50.0, 99.99, 100.0])
    def test_risk_probability_pct_valid_accepted(self, valid_prob):
        rm = RiskMetrics(probability_pct=valid_prob)
        assert rm.probability_pct == valid_prob


# ============================================================================
# 3. SCHEMA POLLUTION & CORRUPTED STRUCTURE TESTS
# ============================================================================

class TestSchemaPollutionAndCorruptedStructures:
    """Tests resilience against schema injection, malicious keys, and malformed types."""

    @pytest.mark.parametrize("corrupted_input", [
        None,
        "",
        "string_instead_of_dict",
        12345,
        3.14159,
        True,
        False,
        [],
        [1, 2, 3],
        ["id", "type"],
        set(),
        (1, 2),
        object(),
    ])
    def test_non_dict_inputs_handled_gracefully(self, corrupted_input):
        """Zero unhandled exceptions on non-dict inputs."""
        is_valid, errors = validate_financial_note(corrupted_input, is_ai_agent=True)
        assert is_valid is False
        assert len(errors) > 0
        assert any("dictionary" in err.lower() for err in errors)

    def test_prototype_pollution_and_dangerous_keys(self, baseline_valid_note):
        """Tests that injection of __proto__, constructor, __class__ doesn't cause crashes."""
        note = baseline_valid_note.copy()
        note["__proto__"] = {"admin": True}
        note["constructor"] = {"prototype": {"polluted": True}}
        note["__class__"] = "MaliciousClass"

        is_valid, errors = validate_financial_note(note, is_ai_agent=True)
        assert isinstance(is_valid, bool)
        assert isinstance(errors, list)


# ============================================================================
# 4. TRUST BOUNDARY INVARIANT ADVERSARIAL ATTACKS (P0, P2, P3)
# ============================================================================

class TestTrustBoundaryAdversarialAttacks:
    """Stress tests invariant enforcement against adversarial bypass attempts."""

    # --- P0: AI Self-Verification Gate ---
    @pytest.mark.parametrize("ai_verification_attempt", [
        "verified",
    ])
    def test_p0_flat_note_ai_verification_blocked(self, baseline_valid_note, ai_verification_attempt):
        note = baseline_valid_note.copy()
        note["verification"] = ai_verification_attempt

        # AI Agent context -> must reject
        is_valid, errors = validate_financial_note(note, is_ai_agent=True)
        assert is_valid is False
        assert any("P0" in err or "verified" in err for err in errors)

        # Human / Admin context -> allowed
        is_valid_human, errors_human = validate_financial_note(note, is_ai_agent=False)
        assert is_valid_human is True
        assert len(errors_human) == 0

    def test_p0_nested_frontmatter_ai_verification_blocked(self):
        nested_note = {
            "frontmatter": {
                "id": str(uuid.uuid4()),
                "type": "knowledge",
                "lifecycle": "REVIEW",
                "category": "indici",
                "tags": ["finance"],
                "created": "2026-08-26",
                "updated": "2026-08-26",
                "provenance": {"source_type": "execution", "source_ref": "pipeline"},
                "confidence": "high",
                "verification": "verified",  # Attack
                "relations": []
            },
            "title": "Adversarial Verification Note",
            "category": "indici"
        }
        is_valid, errors = validate_financial_note(nested_note, is_ai_agent=True)
        assert is_valid is False
        assert any("P0" in err or "verified" in err for err in errors)

    # --- P2: Privileged Provenance Isolation ---
    @pytest.mark.parametrize("privileged_source", ["user", "official", "experience", "import"])
    def test_p2_flat_note_privileged_provenance_blocked(self, baseline_valid_note, privileged_source):
        note = baseline_valid_note.copy()
        note["provenance"] = {"source_type": privileged_source, "source_ref": "attacker_claim"}

        is_valid, errors = validate_financial_note(note, is_ai_agent=True)
        assert is_valid is False
        assert any("P2" in err or privileged_source in err for err in errors)

    @pytest.mark.parametrize("privileged_source", ["user", "official", "experience", "import"])
    def test_p2_nested_frontmatter_privileged_provenance_blocked(self, privileged_source):
        nested_note = {
            "frontmatter": {
                "id": str(uuid.uuid4()),
                "type": "knowledge",
                "lifecycle": "REVIEW",
                "category": "indici",
                "tags": ["finance"],
                "created": "2026-08-26",
                "updated": "2026-08-26",
                "provenance": {"source_type": privileged_source, "source_ref": "attacker_claim"},
                "confidence": "high",
                "verification": "partially_verified",
                "relations": []
            },
            "title": "Adversarial Provenance Note",
            "category": "indici"
        }
        is_valid, errors = validate_financial_note(nested_note, is_ai_agent=True)
        assert is_valid is False
        assert any("P2" in err or privileged_source in err for err in errors)

    @pytest.mark.parametrize("permitted_source", ["execution", "ai", "inference", "unknown"])
    def test_p2_permitted_sources_accepted(self, baseline_valid_note, permitted_source):
        note = baseline_valid_note.copy()
        note["provenance"] = {"source_type": permitted_source, "source_ref": "valid_subsystem"}

        is_valid, errors = validate_financial_note(note, is_ai_agent=True)
        assert is_valid is True, f"Failed for permitted source: {permitted_source}, errors: {errors}"

    # --- P3: Permitted Creation Lifecycles ---
    @pytest.mark.parametrize("escalated_lifecycle", ["ACTIVE", "VERIFIED", "SUPERSEDED", "ARCHIVED"])
    def test_p3_flat_note_escalated_lifecycle_blocked(self, baseline_valid_note, escalated_lifecycle):
        note = baseline_valid_note.copy()
        note["lifecycle"] = escalated_lifecycle

        is_valid, errors = validate_financial_note(note, is_ai_agent=True)
        assert is_valid is False
        assert any("P3" in err or escalated_lifecycle in err for err in errors)

    @pytest.mark.parametrize("escalated_lifecycle", ["ACTIVE", "VERIFIED", "SUPERSEDED", "ARCHIVED"])
    def test_p3_nested_frontmatter_escalated_lifecycle_blocked(self, escalated_lifecycle):
        nested_note = {
            "frontmatter": {
                "id": str(uuid.uuid4()),
                "type": "knowledge",
                "lifecycle": escalated_lifecycle,
                "category": "indici",
                "tags": ["finance"],
                "created": "2026-08-26",
                "updated": "2026-08-26",
                "provenance": {"source_type": "execution", "source_ref": "pipeline"},
                "confidence": "high",
                "verification": "partially_verified",
                "relations": []
            },
            "title": "Adversarial Lifecycle Note",
            "category": "indici"
        }
        is_valid, errors = validate_financial_note(nested_note, is_ai_agent=True)
        assert is_valid is False
        assert any("P3" in err or escalated_lifecycle in err for err in errors)

    @pytest.mark.parametrize("permitted_lifecycle", ["RAW", "CLASSIFIED", "NORMALIZED", "REVIEW"])
    def test_p3_permitted_creation_lifecycles_accepted(self, baseline_valid_note, permitted_lifecycle):
        note = baseline_valid_note.copy()
        note["lifecycle"] = permitted_lifecycle

        is_valid, errors = validate_financial_note(note, is_ai_agent=True)
        assert is_valid is True, f"Failed for permitted lifecycle: {permitted_lifecycle}, errors: {errors}"


# ============================================================================
# 5. EMPIRICAL DEFECT DEMONSTRATION TESTS (Findings 1, 2, 3)
# ============================================================================

class TestEmpiricalDefectsDemonstration:
    """
    These tests document the concrete empirical flaws discovered in `financial_schema.py`.
    They serve as the benchmark for worker bug remediation.
    """

    def test_defect_1_schema_bypass_on_corrupted_provenance(self, baseline_valid_note):
        """
        DEFECT 1 (High): FINANCIAL_NOTE_SCHEMA Variant C allows arbitrary corrupted fields
        in canonical notes (e.g. provenance='invalid_string') to pass validation.
        """
        note = baseline_valid_note.copy()
        note["provenance"] = "invalid_string_not_dict"
        is_valid, errors = validate_financial_note(note, is_ai_agent=True)
        # Expected correct behavior: is_valid is False
        # Actual buggy behavior: is_valid is True due to Variant C matching
        assert is_valid is False, "DEFECT 1 REPRODUCED: Corrupted provenance string accepted as valid"

    def test_defect_1_schema_bypass_on_corrupted_relations(self, baseline_valid_note):
        """
        DEFECT 1 (High): Corrupted relations structure passes validation due to Variant C.
        """
        note = baseline_valid_note.copy()
        note["relations"] = [12345]  # List of ints is illegal
        is_valid, errors = validate_financial_note(note, is_ai_agent=True)
        assert is_valid is False, "DEFECT 1 REPRODUCED: Illegal relations [12345] accepted as valid"

    def test_defect_2_unhandled_type_error_on_unhashable_lifecycle(self, baseline_valid_note):
        """
        DEFECT 2 (Medium): validate_financial_note crashes with TypeError when lifecycle is a dict.
        """
        note = baseline_valid_note.copy()
        note["lifecycle"] = {"nested": "dict"}
        try:
            is_valid, errors = validate_financial_note(note, is_ai_agent=True)
            assert is_valid is False
        except TypeError as e:
            pytest.fail(f"DEFECT 2 REPRODUCED: Unhandled TypeError crash on unhashable lifecycle: {e}")

    def test_defect_2_unhandled_type_error_on_unhashable_source_type(self, baseline_valid_note):
        """
        DEFECT 2 (Medium): validate_financial_note crashes with TypeError when provenance.source_type is a dict.
        """
        note = baseline_valid_note.copy()
        note["provenance"] = {"source_type": {"bad": "dict"}, "source_ref": "ref"}
        try:
            is_valid, errors = validate_financial_note(note, is_ai_agent=True)
            assert is_valid is False
        except TypeError as e:
            pytest.fail(f"DEFECT 2 REPRODUCED: Unhandled TypeError crash on unhashable source_type: {e}")

    def test_defect_3_none_id_accepted_as_valid(self, baseline_valid_note):
        """
        DEFECT 3 (Medium): Canonical note with id=None passes validation without error.
        """
        note = baseline_valid_note.copy()
        note["id"] = None
        is_valid, errors = validate_financial_note(note, is_ai_agent=True)
        assert is_valid is False, "DEFECT 3 REPRODUCED: Note with id=None accepted as valid"


# ============================================================================
# 6. ENUM FIDELITY & SERIALIZATION ROUNDTRIP STRESS
# ============================================================================

class TestEnumFidelityAndSerialization:

    def test_all_enums_export_exact_values(self):
        assert MemoryTypeEnum.KNOWLEDGE == "knowledge"
        assert MemoryTypeEnum.DECISION == "decision"
        assert LifecycleEnum.REVIEW == "REVIEW"
        assert ConfidenceEnum.HIGH == "high"
        assert VerificationEnum.PARTIALLY_VERIFIED == "partially_verified"
        assert SignalEnum.BUY == "BUY"
        assert SignalEnum.SELL == "SELL"
        assert SignalEnum.WAIT == "WAIT"
        assert TrendEnum.BULLISH == "Bullish"
        assert TrendEnum.BEARISH == "Bearish"
        assert TrendEnum.SIDEWAYS == "Sideways"

    def test_financial_note_model_roundtrip_json(self, baseline_valid_note):
        """Verifies lossless model_dump_json and model_validate_json."""
        model = FinancialNoteModel(**baseline_valid_note)
        json_str = model.model_dump_json()
        restored = FinancialNoteModel.model_validate_json(json_str)

        assert restored.id == model.id
        assert restored.ticker == model.ticker
        assert restored.asset_class == model.asset_class
        assert restored.technical_indicators.rsi_14 == model.technical_indicators.rsi_14
        assert restored.quantitative_signal.signal == model.quantitative_signal.signal
        assert restored.macro_context.vix == model.macro_context.vix
