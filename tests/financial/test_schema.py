"""
Unit Test Suite for Financial Schema & Pydantic Domain Models (Tier 1).

Validates:
1. Draft-07 JSON Schema compliance (FINANCIAL_NOTE_SCHEMA).
2. validate_financial_note validator behavior across canonical notes and payloads.
3. Strict UUID format enforcement for note IDs.
4. Pydantic v2 domain model serialization, type safety, and field validators.
5. Trust Boundary Invariants (P0-P18):
   - P0: Prohibiting AI agents from self-attesting verification='verified'.
   - P2: Restricting AI agents from claiming privileged provenance source_types.
   - P3: Restricting AI agents from directly creating into escalated lifecycles.
6. Mathematical and indicator boundary constraints.
7. Negative schema validation and polymorphic union handling.
"""

import uuid
import pytest
import jsonschema
from jsonschema import Draft7Validator, FormatChecker

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
from memory_controller.validation.schema import validate_frontmatter, _CANONICAL_SCHEMA


# ============================================================================
# FIXTURES & SAMPLE GENERATORS
# ============================================================================

@pytest.fixture
def valid_canonical_note_dict():
    """Generates a valid complete canonical financial memory note dict."""
    note_id = str(uuid.uuid4())
    return {
        "id": note_id,
        "type": "knowledge",
        "lifecycle": "REVIEW",
        "category": "financial-asset-profile",
        "tags": ["finance", "asset/xau", "precious-metals"],
        "created": "2026-08-26",
        "updated": "2026-08-26",
        "provenance": {
            "source_type": "execution",
            "source_ref": "financial_ingestion_pipeline",
            "source_date": "2026-08-26",
            "provenance_status": "complete"
        },
        "confidence": "high",
        "verification": "partially_verified",
        "relations": [
            {"relation": "related_to", "target": "[[Asset_Gold]]", "target_id": str(uuid.uuid4())}
        ],
        "ticker": "GC=F",
        "symbol": "GC=F",
        "instrument_name": "Gold Comex Futures",
        "asset_class": "MATERII_PRIME",
        "price_data": {
            "open": 2510.5,
            "high": 2530.0,
            "low": 2505.0,
            "close": 2524.8,
            "change_day_pct": 0.57,
            "change_week_pct": 1.85,
            "volume": 145000,
            "avg_volume_20d": 120000,
            "rvol": 1.21
        },
        "technical_indicators": {
            "rsi_14": 58.4,
            "rsi_status": "Momentum ascendent",
            "macd": 12.4,
            "macd_signal": 8.2,
            "macd_hist": 4.2,
            "macd_cross": "Impuls pozitiv activ",
            "ma20": 2490.0,
            "ma50": 2450.0,
            "ma200": 2350.0,
            "ma_cross": "Golden Cross",
            "trend": "Bullish",
            "bb_mid": 2490.0,
            "bb_sup": 2535.0,
            "bb_inf": 2445.0,
            "bb_width": 90.0,
            "atr_14": 22.5,
            "stoch_k": 78.5,
            "stoch_d": 72.1,
            "momentum_10d": 3.2,
            "support_20d": 2480.0,
            "resistance_20d": 2530.0,
            "rvol": 1.21
        },
        "quantitative_signal": {
            "signal": "BUY",
            "score": 4,
            "confluences": 4,
            "stop_loss": 2491.05,
            "take_profit": 2592.30,
            "risk_reward_ratio": 2.0,
            "win_probability_pct": 75.0,
            "timeframe": "1D",
            "trigger_condition": "RSI=58.4 | Impuls pozitiv activ | Golden Cross",
            "status": "In asteptare"
        },
        "macro_context": {
            "vix": 16.2,
            "yield_10y": 4.12,
            "yield_2y": 4.35,
            "usd_index": 102.4,
            "fear_greed_index": 62,
            "fed_funds_rate": 5.33,
            "cpi": 3.1,
            "unemployment_rate": 4.0,
            "gdp": 27000.0
        },
        "commentary": {
            "movement_explanation": "Gold maintained strong upward momentum testing 2525 resistance.",
            "opportunity_alert": "✅ Setup activ de cumparare confirmat.",
            "educational_lesson": "Respectarea raportului R/R minim 2:1 asigura longevitatea contului."
        },
        "content_markdown": "# Gold Comex Futures (GC=F)\n\nAnalysis and technical posture."
    }


# ============================================================================
# 1. DRAFT-07 JSON SCHEMA TESTS
# ============================================================================

class TestDraft07JsonSchemaValidation:

    def test_schema_itself_is_valid_draft07(self):
        """Ensures FINANCIAL_NOTE_SCHEMA conforms to the Draft-07 metaschema."""
        Draft7Validator.check_schema(FINANCIAL_NOTE_SCHEMA)

    def test_valid_note_passes_draft07_validation(self, valid_canonical_note_dict):
        """Ensures a compliant canonical note passes schema validation with 0 errors."""
        validator = Draft7Validator(FINANCIAL_NOTE_SCHEMA, format_checker=FormatChecker())
        errors = list(validator.iter_errors(valid_canonical_note_dict))
        assert len(errors) == 0, f"Unexpected validation errors: {[e.message for e in errors]}"

    def test_schema_accepts_nested_payload_variant(self):
        """Ensures FinancialNotePayload structure with nested frontmatter passes validation."""
        payload = {
            "frontmatter": {
                "id": str(uuid.uuid4()),
                "type": "knowledge",
                "lifecycle": "REVIEW",
                "category": "indici",
                "tags": ["tech", "nasdaq"],
                "created": "2026-08-26",
                "updated": "2026-08-26",
                "provenance": {
                    "source_type": "execution",
                    "source_ref": "ingest_pipeline"
                },
                "confidence": "high",
                "verification": "partially_verified",
                "relations": []
            },
            "title": "NASDAQ 100 Technical Analysis",
            "symbol": "^NDX",
            "category": "indici",
            "indicators": {"rsi_14": 62.5, "trend": "Bullish"},
            "signals": [{"signal": "BUY", "score": 3}],
            "risk_metrics": {"impact": 3, "probability_pct": 60.0},
            "narrative": "Tech momentum strong post-earnings.",
            "raw_content": "Raw data"
        }
        validator = Draft7Validator(FINANCIAL_NOTE_SCHEMA, format_checker=FormatChecker())
        errors = list(validator.iter_errors(payload))
        assert len(errors) == 0, f"Errors in nested payload: {[e.message for e in errors]}"


# ============================================================================
# 2. DRAFT-07 NEGATIVE SCHEMA TESTS
# ============================================================================

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

        bad_rsi_neg = valid_canonical_note_dict.copy()
        bad_rsi_neg["technical_indicators"] = {"rsi_14": -5.0}
        validator = Draft7Validator(FINANCIAL_NOTE_SCHEMA, format_checker=FormatChecker())
        errors = list(validator.iter_errors(bad_rsi_neg))
        assert len(errors) > 0, "Expected schema rejection for negative RSI"

        bad_atr = valid_canonical_note_dict.copy()
        bad_atr["technical_indicators"] = {"atr_14": -2.0}
        validator = Draft7Validator(FINANCIAL_NOTE_SCHEMA, format_checker=FormatChecker())
        errors = list(validator.iter_errors(bad_atr))
        assert len(errors) > 0, "Expected schema rejection for negative ATR"

    def test_schema_rejects_out_of_bounds_quantitative_signal(self, valid_canonical_note_dict):
        """Quantitative signal out of bounds must fail Draft-07 validation."""
        bad_score_note = valid_canonical_note_dict.copy()
        bad_score_note["quantitative_signal"] = {"score": 10}
        validator = Draft7Validator(FINANCIAL_NOTE_SCHEMA, format_checker=FormatChecker())
        errors = list(validator.iter_errors(bad_score_note))
        assert len(errors) > 0, "Expected schema rejection for signal score=10"

        bad_conf_note = valid_canonical_note_dict.copy()
        bad_conf_note["quantitative_signal"] = {"confluences": 7}
        validator = Draft7Validator(FINANCIAL_NOTE_SCHEMA, format_checker=FormatChecker())
        errors = list(validator.iter_errors(bad_conf_note))
        assert len(errors) > 0, "Expected schema rejection for confluences=7"

        bad_win_prob = valid_canonical_note_dict.copy()
        bad_win_prob["quantitative_signal"] = {"win_probability_pct": 95.0}
        validator = Draft7Validator(FINANCIAL_NOTE_SCHEMA, format_checker=FormatChecker())
        errors = list(validator.iter_errors(bad_win_prob))
        assert len(errors) > 0, "Expected schema rejection for win_probability_pct=95.0"

    def test_schema_rejects_out_of_bounds_risk_metrics(self, valid_canonical_note_dict):
        """Risk metrics out of bounds must fail Draft-07 validation."""
        bad_risk = valid_canonical_note_dict.copy()
        bad_risk["risk_metrics"] = {"impact": 6, "probability_pct": 120.0}
        validator = Draft7Validator(FINANCIAL_NOTE_SCHEMA, format_checker=FormatChecker())
        errors = list(validator.iter_errors(bad_risk))
        assert len(errors) > 0, "Expected schema rejection for out of bounds risk metrics"

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

    def test_schema_rejects_arbitrary_unstructured_dict(self):
        """Random dictionary without required fields must fail all schema variants."""
        unstructured = {"foo": "bar", "baz": 123}
        validator = Draft7Validator(FINANCIAL_NOTE_SCHEMA, format_checker=FormatChecker())
        errors = list(validator.iter_errors(unstructured))
        assert len(errors) > 0, "Expected schema rejection for unstructured dict"


# ============================================================================
# 3. VALIDATE_FINANCIAL_NOTE VALIDATOR & TRUST BOUNDARIES (P0-P18)
# ============================================================================

class TestValidateFinancialNoteTrustBoundaries:

    def test_valid_canonical_note_passes_validator(self, valid_canonical_note_dict):
        is_valid, errors = validate_financial_note(valid_canonical_note_dict, is_ai_agent=True)
        assert is_valid is True
        assert len(errors) == 0

    def test_non_dict_input_returns_false(self):
        is_valid, errors = validate_financial_note("Not a dictionary", is_ai_agent=True)
        assert is_valid is False
        assert any("dictionary" in err.lower() for err in errors)

    def test_strict_uuid_format_enforcement(self, valid_canonical_note_dict):
        """Invalid UUID strings must be rejected."""
        invalid_ids = [
            "note-12345",
            "not-a-valid-uuid",
            "12345678",
            "g3a9f0e1-4b21-4d32-8e12-9c1234567890",  # non-hex char 'g'
            "",
            None,
        ]
        for bad_id in invalid_ids:
            note = valid_canonical_note_dict.copy()
            note["id"] = bad_id
            is_valid, errors = validate_financial_note(note, is_ai_agent=True)
            assert is_valid is False, f"Expected rejection for invalid UUID: {bad_id}"
            assert any("UUID" in err for err in errors)

    def test_p0_invariant_ai_cannot_produce_verified(self, valid_canonical_note_dict):
        """P0 Trust Boundary: AI agents cannot set verification = 'verified'."""
        note = valid_canonical_note_dict.copy()
        note["verification"] = "verified"

        # AI Agent context -> must fail
        is_valid_ai, errors_ai = validate_financial_note(note, is_ai_agent=True)
        assert is_valid_ai is False
        assert any("P0" in err or "verified" in err for err in errors_ai)

        # Human / Admin context -> allowed
        is_valid_human, errors_human = validate_financial_note(note, is_ai_agent=False)
        assert is_valid_human is True

    def test_p2_invariant_ai_cannot_claim_privileged_provenance(self, valid_canonical_note_dict):
        """P2 Trust Boundary: AI agents cannot claim source_type in {user, official, experience, import}."""
        privileged_sources = ["user", "official", "experience", "import"]
        for src in privileged_sources:
            note = valid_canonical_note_dict.copy()
            note["provenance"] = {"source_type": src, "source_ref": "test_ref"}

            is_valid, errors = validate_financial_note(note, is_ai_agent=True)
            assert is_valid is False, f"Expected rejection for privileged provenance: {src}"
            assert any("P2" in err or src in err for err in errors)

    def test_p2_invariant_ai_permitted_provenance_sources(self, valid_canonical_note_dict):
        """P2 Permitted sources for AI: execution, ai, inference, unknown."""
        permitted_sources = ["execution", "ai", "inference", "unknown"]
        for src in permitted_sources:
            note = valid_canonical_note_dict.copy()
            note["provenance"] = {"source_type": src, "source_ref": "test_ref"}

            is_valid, errors = validate_financial_note(note, is_ai_agent=True)
            assert is_valid is True, f"Expected approval for permitted provenance: {src}, errors: {errors}"

    def test_p3_invariant_ai_cannot_create_into_escalated_lifecycles(self, valid_canonical_note_dict):
        """P3 Trust Boundary: AI agents can only propose into {RAW, CLASSIFIED, NORMALIZED, REVIEW}."""
        prohibited_lifecycles = ["ACTIVE", "VERIFIED", "SUPERSEDED", "ARCHIVED"]
        for lc in prohibited_lifecycles:
            note = valid_canonical_note_dict.copy()
            note["lifecycle"] = lc

            is_valid, errors = validate_financial_note(note, is_ai_agent=True)
            assert is_valid is False, f"Expected rejection for AI creating in lifecycle: {lc}"
            assert any("P3" in err or lc in err for err in errors)

    def test_p3_invariant_ai_permitted_creation_lifecycles(self, valid_canonical_note_dict):
        """P3 Permitted lifecycles for AI: RAW, CLASSIFIED, NORMALIZED, REVIEW."""
        permitted_lifecycles = ["RAW", "CLASSIFIED", "NORMALIZED", "REVIEW"]
        for lc in permitted_lifecycles:
            note = valid_canonical_note_dict.copy()
            note["lifecycle"] = lc

            is_valid, errors = validate_financial_note(note, is_ai_agent=True)
            assert is_valid is True, f"Expected approval for permitted lifecycle: {lc}, errors: {errors}"


# ============================================================================
# 4. PYDANTIC V2 DOMAIN MODELS VALIDATION
# ============================================================================

class TestPydanticDomainModels:

    def test_financial_frontmatter_instantiation_defaults(self):
        """Tests FinancialFrontmatter model with standard defaults."""
        fm = FinancialFrontmatter()
        assert uuid.UUID(fm.id)  # valid UUID
        assert fm.type == "knowledge"
        assert fm.lifecycle == "REVIEW"
        assert fm.confidence == "high"
        assert fm.verification == "partially_verified"
        assert isinstance(fm.tags, list)
        assert isinstance(fm.relations, list)

    def test_financial_frontmatter_uuid_validator_rejects_malformed(self):
        """Rejects non-UUID strings in id field."""
        with pytest.raises(Exception):
            FinancialFrontmatter(id="malformed-uuid-string")

    def test_financial_frontmatter_verification_validator(self):
        """Ensures invalid verification enum values are rejected."""
        with pytest.raises(Exception):
            FinancialFrontmatter(verification="invalid_status")

    def test_financial_frontmatter_lifecycle_validator(self):
        """Ensures invalid lifecycle enum values are rejected."""
        with pytest.raises(Exception):
            FinancialFrontmatter(lifecycle="UNKNOWN_LIFECYCLE")

    def test_price_data_payload_fields(self):
        """Tests PriceDataPayload model."""
        pd = PriceDataPayload(
            open=100.0,
            high=105.0,
            low=99.5,
            close=104.2,
            change_day_pct=4.2,
            volume=500000,
            rvol=1.45
        )
        assert pd.close == 104.2
        assert pd.rvol == 1.45
        data = pd.model_dump()
        assert data["close"] == 104.2
        assert data["volume"] == 500000

    def test_financial_indicators_boundary_constraints(self):
        """Tests boundary constraints on RSI and ATR."""
        # RSI out of bounds (> 100 or < 0)
        with pytest.raises(Exception):
            FinancialIndicators(rsi_14=105.0)
        with pytest.raises(Exception):
            FinancialIndicators(rsi_14=-5.0)

        # ATR negative
        with pytest.raises(Exception):
            FinancialIndicators(atr_14=-1.0)

        # Valid indicators
        ind = FinancialIndicators(rsi_14=45.5, atr_14=12.3, trend="Bullish")
        assert ind.rsi_14 == 45.5
        assert ind.atr_14 == 12.3
        assert ind.trend == "Bullish"

    def test_trade_signal_and_quantitative_signal_payload(self):
        """Tests TradeSignal constraints: score (-5 to 5), confluences (0 to 5), prob (35 to 90)."""
        # Score out of bounds
        with pytest.raises(Exception):
            TradeSignal(score=6)
        with pytest.raises(Exception):
            TradeSignal(score=-6)

        # Confluences out of bounds
        with pytest.raises(Exception):
            TradeSignal(confluences=6)
        with pytest.raises(Exception):
            TradeSignal(confluences=-1)

        # Win probability out of bounds
        with pytest.raises(Exception):
            TradeSignal(win_probability_pct=95.0)
        with pytest.raises(Exception):
            TradeSignal(win_probability_pct=25.0)

        # Valid signal
        sig = QuantitativeSignalPayload(
            signal="BUY",
            score=4,
            confluences=4,
            stop_loss=2480.0,
            take_profit=2560.0,
            risk_reward_ratio=2.0,
            win_probability_pct=75.0
        )
        assert sig.signal == "BUY"
        assert sig.win_probability_pct == 75.0

    def test_financial_note_payload_nested_model(self):
        """Tests FinancialNotePayload combining frontmatter and financial sections."""
        payload = FinancialNotePayload(
            title="S&P 500 Macro Breakdown",
            symbol="^GSPC",
            category="indici",
            indicators={"rsi_14": 52.0, "trend": "Sideways"},
            signals=[{"signal": "WAIT", "score": 0}],
            risk_metrics={"impact": 2, "probability_pct": 30.0},
            narrative="Market consolidating ahead of FOMC rate decision.",
            raw_content="Raw snapshot content"
        )
        assert payload.title == "S&P 500 Macro Breakdown"
        assert payload.symbol == "^GSPC"
        assert payload.category == "indici"
        assert payload.narrative != ""

    def test_financial_note_model_full_instantiation(self, valid_canonical_note_dict):
        """Tests complete FinancialNoteModel validation from dictionary."""
        note_model = FinancialNoteModel(**valid_canonical_note_dict)
        assert note_model.ticker == "GC=F"
        assert note_model.lifecycle == LifecycleEnum.REVIEW
        assert note_model.verification == VerificationEnum.PARTIALLY_VERIFIED
        assert note_model.confidence == ConfidenceEnum.HIGH
        assert note_model.type == MemoryTypeEnum.KNOWLEDGE

        dumped = note_model.model_dump()
        assert dumped["id"] == valid_canonical_note_dict["id"]
        assert dumped["ticker"] == "GC=F"


# ============================================================================
# 5. PYDANTIC UNION BASE CLASS POLYMORPHISM
# ============================================================================

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


# ============================================================================
# 6. CANONICAL FRONTMATTER & ENUM FIDELITY
# ============================================================================

class TestCanonicalFrontmatterIntegration:

    def test_frontmatter_schema_rejects_extra_top_level_keys(self, valid_canonical_note_dict):
        """FRONTMATTER_SCHEMA has additionalProperties: False, rejecting unwhitelisted keys."""
        bad_frontmatter = {
            "id": valid_canonical_note_dict["id"],
            "type": "knowledge",
            "lifecycle": "REVIEW",
            "category": "financial",
            "tags": ["finance"],
            "created": "2026-08-26",
            "updated": "2026-08-26",
            "provenance": {"source_type": "execution", "source_ref": "test"},
            "confidence": "high",
            "verification": "partially_verified",
            "relations": [],
            "unauthorized_extra_key": "illegal_value"
        }
        with pytest.raises(jsonschema.ValidationError):
            validate_frontmatter(bad_frontmatter)

    def test_valid_frontmatter_passes_canonical_validation(self, valid_canonical_note_dict):
        """Standard canonical frontmatter passes validate_frontmatter."""
        frontmatter_fields = [
            "id", "type", "lifecycle", "category", "tags",
            "created", "updated", "provenance", "confidence",
            "verification", "relations"
        ]
        clean_fm = {k: valid_canonical_note_dict[k] for k in frontmatter_fields}
        # Should not raise exception
        validate_frontmatter(clean_fm)
