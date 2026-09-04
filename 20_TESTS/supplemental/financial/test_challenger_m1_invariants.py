"""
Adversarial Verification Suite for Financial Schema & Invariants P0-P18 (Challenger M1-2).

This suite empirically probes and challenges:
1. P0 Trust Boundary: AI Self-Verification Gate (block verification='verified' and unauthorized verification strings).
2. P2 Trust Boundary: Privileged Provenance Isolation (block source_type in {user, official, experience, import} and unauthorized strings for AI).
3. P3 Trust Boundary: Lifecycle Creation Scoping (block creation into {ACTIVE, VERIFIED, SUPERSEDED, ARCHIVED} and unauthorized strings for AI).
4. UUID Strictness & ID Spoofing/Injection Attacks (SQLi, path traversal, hex corruption, missing/null ID).
5. Mathematical Bounds & Fuzzing (RSI [0, 100], ATR >= 0, Win Prob [35, 90], Confluences [0, 5], Score [-5, 5], Impact [1, 5]).
6. Schema Tampering & Variant Disguise Attacks (probing anyOf Variant C universal wildcard match).
7. Model Serialization & Roundtrip Fidelity.
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


@pytest.fixture
def canonical_base_dict():
    """Generates a strictly valid base canonical financial note dictionary."""
    return {
        "id": str(uuid.uuid4()),
        "type": "knowledge",
        "lifecycle": "REVIEW",
        "category": "financial-asset-profile",
        "tags": ["finance", "crypto", "btc"],
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
            {"relation": "related_to", "target": "[[Asset_Bitcoin]]", "target_id": str(uuid.uuid4())}
        ],
        "ticker": "BTC-USD",
        "symbol": "BTC-USD",
        "instrument_name": "Bitcoin USD",
        "asset_class": "CRYPTO",
        "price_data": {
            "open": 63000.0,
            "high": 64500.0,
            "low": 62800.0,
            "close": 64120.0,
            "change_day_pct": 1.78,
            "volume": 28000000000,
            "rvol": 1.15
        },
        "technical_indicators": {
            "rsi_14": 62.4,
            "rsi_status": "Momentum ascendent",
            "macd": 450.2,
            "macd_signal": 310.0,
            "macd_hist": 140.2,
            "macd_cross": "Impuls pozitiv activ",
            "ma20": 62500.0,
            "ma50": 60100.0,
            "ma200": 54000.0,
            "ma_cross": "Golden Cross",
            "trend": "Bullish",
            "bb_mid": 62500.0,
            "bb_sup": 65000.0,
            "bb_inf": 60000.0,
            "bb_width": 5000.0,
            "atr_14": 1850.0,
            "stoch_k": 74.2,
            "stoch_d": 68.1,
            "momentum_10d": 4.5,
            "support_20d": 61000.0,
            "resistance_20d": 65000.0,
            "rvol": 1.15
        },
        "quantitative_signal": {
            "signal": "BUY",
            "score": 4,
            "confluences": 4,
            "stop_loss": 61345.0,
            "take_profit": 69670.0,
            "risk_reward_ratio": 2.0,
            "win_probability_pct": 72.0,
            "timeframe": "1D",
            "trigger_condition": "RSI=62.4 | Golden Cross",
            "status": "In asteptare"
        },
        "risk_metrics": {
            "impact": 4,
            "probability_pct": 65.0,
            "score": 2.6,
            "horizon": "Swing",
            "actions": "Limit stop loss below 61k support",
            "sl_atr_multiple": 1.5,
            "tp_atr_multiple": 3.0,
            "planned_rr": 2.0
        },
        "macro_context": {
            "vix": 15.4,
            "yield_10y": 4.10,
            "usd_index": 101.8,
            "fear_greed_index": 65
        },
        "commentary": {
            "movement_explanation": "BTC breakout above 63k consolidation.",
            "opportunity_alert": "Bullish trend continuation setup.",
            "educational_lesson": "Always maintain dynamic ATR stops."
        },
        "content_markdown": "# Bitcoin (BTC-USD)\n\nStrong technical setup."
    }


# ============================================================================
# 1. P0 TRUST BOUNDARY: AI SELF-VERIFICATION GATE PROBING
# ============================================================================

class TestP0SelfVerificationGateAdversarial:
    """Adversarial testing of P0: AI agents MUST NOT be able to self-verify notes."""

    def test_direct_verified_rejection_flat_canonical(self, canonical_base_dict):
        """Flat canonical note with verification='verified' must be rejected for AI agent."""
        bad_note = canonical_base_dict.copy()
        bad_note["verification"] = "verified"

        is_valid, errors = validate_financial_note(bad_note, is_ai_agent=True)
        assert is_valid is False
        assert any("P0" in err and "verified" in err for err in errors)

    def test_direct_verified_rejection_nested_payload(self, canonical_base_dict):
        """Nested frontmatter payload with verification='verified' must be rejected for AI agent."""
        nested_payload = {
            "frontmatter": {
                "id": str(uuid.uuid4()),
                "type": "knowledge",
                "lifecycle": "REVIEW",
                "category": "indici",
                "tags": ["macro"],
                "created": "2026-08-26",
                "updated": "2026-08-26",
                "provenance": {"source_type": "execution", "source_ref": "test"},
                "confidence": "high",
                "verification": "verified",  # Privileged attempt
                "relations": []
            },
            "title": "Escalation Test",
            "category": "indici",
            "indicators": {},
            "signals": [],
            "risk_metrics": {}
        }
        is_valid, errors = validate_financial_note(nested_payload, is_ai_agent=True)
        assert is_valid is False
        assert any("P0" in err for err in errors)

    def test_human_admin_can_set_verified(self, canonical_base_dict):
        """Human/Admin context (is_ai_agent=False) MUST be permitted to attest verification='verified'."""
        verified_note = canonical_base_dict.copy()
        verified_note["verification"] = "verified"

        is_valid, errors = validate_financial_note(verified_note, is_ai_agent=False)
        assert is_valid is True
        assert len(errors) == 0

    @pytest.mark.parametrize("valid_ai_verification", ["partially_verified", "unverified", "inferred"])
    def test_permitted_ai_verification_states(self, canonical_base_dict, valid_ai_verification):
        """AI agent is strictly permitted: partially_verified, unverified, inferred."""
        note = canonical_base_dict.copy()
        note["verification"] = valid_ai_verification

        is_valid, errors = validate_financial_note(note, is_ai_agent=True)
        assert is_valid is True, f"Failed for valid AI verification '{valid_ai_verification}': {errors}"

    @pytest.mark.parametrize("forged_verification", [
        "VERIFIED", "Verified", " verified ", "verified\n", "attested", "100%", "true", "True", "admin_confirmed"
    ])
    def test_forged_and_variant_verification_strings_rejected(self, canonical_base_dict, forged_verification):
        """Forged or non-standard verification strings must be rejected either by JSON schema or invariant."""
        note = canonical_base_dict.copy()
        note["verification"] = forged_verification

        is_valid, errors = validate_financial_note(note, is_ai_agent=True)
        assert is_valid is False, f"Vulnerability: Forged verification '{forged_verification}' was accepted!"


# ============================================================================
# 2. P2 TRUST BOUNDARY: PRIVILEGED PROVENANCE ISOLATION PROBING
# ============================================================================

class TestP2PrivilegedProvenanceAdversarial:
    """Adversarial testing of P2: AI agents cannot claim privileged provenance source_types."""

    @pytest.mark.parametrize("priv_source", ["user", "official", "experience", "import"])
    def test_ai_agent_claiming_privileged_provenance_rejected(self, canonical_base_dict, priv_source):
        """AI agents attempting to claim user, official, experience, or import must be rejected."""
        note = canonical_base_dict.copy()
        note["provenance"] = {
            "source_type": priv_source,
            "source_ref": "forged_source"
        }

        is_valid, errors = validate_financial_note(note, is_ai_agent=True)
        assert is_valid is False
        assert any("P2" in err and priv_source in err for err in errors)

    @pytest.mark.parametrize("priv_source", ["user", "official", "experience", "import"])
    def test_human_admin_can_claim_privileged_provenance(self, canonical_base_dict, priv_source):
        """Human/Admin context (is_ai_agent=False) can set user/official/experience/import."""
        note = canonical_base_dict.copy()
        note["provenance"] = {
            "source_type": priv_source,
            "source_ref": "verified_human_input"
        }

        is_valid, errors = validate_financial_note(note, is_ai_agent=False)
        assert is_valid is True, f"Failed for human setting {priv_source}: {errors}"

    @pytest.mark.parametrize("permitted_source", ["execution", "ai", "inference", "unknown"])
    def test_ai_permitted_provenance_sources(self, canonical_base_dict, permitted_source):
        """AI agents can use execution, ai, inference, unknown."""
        note = canonical_base_dict.copy()
        note["provenance"] = {
            "source_type": permitted_source,
            "source_ref": "automated_job"
        }

        is_valid, errors = validate_financial_note(note, is_ai_agent=True)
        assert is_valid is True, f"Failed for permitted source {permitted_source}: {errors}"

    @pytest.mark.parametrize("forged_source", ["root", "system", "admin", "kernel", "god_mode", "USER", "OFFICIAL"])
    def test_unregistered_or_case_mismatched_provenance_rejected(self, canonical_base_dict, forged_source):
        """Any provenance source_type not in Draft-07 enum must fail Draft-07 schema validation."""
        note = canonical_base_dict.copy()
        note["provenance"] = {
            "source_type": forged_source,
            "source_ref": "injection"
        }

        is_valid, errors = validate_financial_note(note, is_ai_agent=True)
        assert is_valid is False, f"Vulnerability: Forged provenance source_type '{forged_source}' was accepted!"


# ============================================================================
# 3. P3 TRUST BOUNDARY: LIFECYCLE CREATION SCOPING PROBING
# ============================================================================

class TestP3CreationLifecycleAdversarial:
    """Adversarial testing of P3: AI agents can only propose into {RAW, CLASSIFIED, NORMALIZED, REVIEW}."""

    @pytest.mark.parametrize("prohibited_lc", ["ACTIVE", "VERIFIED", "SUPERSEDED", "ARCHIVED"])
    def test_ai_direct_creation_in_prohibited_lifecycles_rejected(self, canonical_base_dict, prohibited_lc):
        """Prohibited creation lifecycles must fail for AI agent."""
        note = canonical_base_dict.copy()
        note["lifecycle"] = prohibited_lc

        is_valid, errors = validate_financial_note(note, is_ai_agent=True)
        assert is_valid is False
        assert any("P3" in err and prohibited_lc in err for err in errors)

    @pytest.mark.parametrize("prohibited_lc", ["ACTIVE", "VERIFIED", "SUPERSEDED", "ARCHIVED"])
    def test_human_admin_can_create_in_any_valid_lifecycle(self, canonical_base_dict, prohibited_lc):
        """Human/Admin context (is_ai_agent=False) can create directly into ACTIVE, VERIFIED, etc."""
        note = canonical_base_dict.copy()
        note["lifecycle"] = prohibited_lc

        is_valid, errors = validate_financial_note(note, is_ai_agent=False)
        assert is_valid is True, f"Failed for human setting lifecycle {prohibited_lc}: {errors}"

    @pytest.mark.parametrize("permitted_lc", ["RAW", "CLASSIFIED", "NORMALIZED", "REVIEW"])
    def test_ai_permitted_creation_lifecycles(self, canonical_base_dict, permitted_lc):
        """Permitted lifecycles for AI must succeed."""
        note = canonical_base_dict.copy()
        note["lifecycle"] = permitted_lc

        is_valid, errors = validate_financial_note(note, is_ai_agent=True)
        assert is_valid is True, f"Failed for permitted lifecycle {permitted_lc}: {errors}"

    @pytest.mark.parametrize("invalid_lc", ["PRODUCTION", "LIVE", "DRAFT", "review", "active", "DELETED", "123"])
    def test_invalid_lifecycle_enums_rejected(self, canonical_base_dict, invalid_lc):
        """Invalid lifecycle strings outside the schema must be rejected."""
        note = canonical_base_dict.copy()
        note["lifecycle"] = invalid_lc

        is_valid, errors = validate_financial_note(note, is_ai_agent=True)
        assert is_valid is False, f"Vulnerability: Invalid lifecycle '{invalid_lc}' was accepted!"


# ============================================================================
# 4. UUID ENFORCEMENT & ID FORGERY / INJECTION ATTACKS
# ============================================================================

class TestUUIDEnforcementAndIDForgery:
    """Stress tests strict UUID v4 checking against malformed IDs, SQLi, and path traversal."""

    @pytest.mark.parametrize("hostile_id", [
        "' OR '1'='1",
        "'; DROP TABLE notes; --",
        "../../00_CORE/Identity.md",
        "..\\..\\00_CORE\\Rules.md",
        "<script>alert(1)</script>",
        "00000000-0000-0000-0000-00000000000g",  # non-hex 'g'
        "12345678-1234-1234-1234-12345678901",   # 35 chars
        "12345678-1234-1234-1234-1234567890123", # 37 chars
        "not-a-uuid",
        "",
        " ",
        "\t\n",
        123456,
        None,
    ])
    def test_hostile_and_malformed_ids_rejected(self, canonical_base_dict, hostile_id):
        """All hostile, malformed, or injection IDs must be rejected for canonical notes."""
        note = canonical_base_dict.copy()
        note["id"] = hostile_id

        is_valid, errors = validate_financial_note(note, is_ai_agent=True)
        assert is_valid is False, f"Vulnerability: Hostile ID '{hostile_id}' was accepted!"

    def test_valid_uuid_variants_accepted(self, canonical_base_dict):
        """Both lowercase and uppercase standard UUID strings must be accepted."""
        u = str(uuid.uuid4())
        note_lower = canonical_base_dict.copy()
        note_lower["id"] = u.lower()
        is_val, errs = validate_financial_note(note_lower, is_ai_agent=True)
        assert is_val is True

        note_upper = canonical_base_dict.copy()
        note_upper["id"] = u.upper()
        is_val, errs = validate_financial_note(note_upper, is_ai_agent=True)
        assert is_val is True


# ============================================================================
# 5. MATHEMATICAL BOUNDS FUZZING & INDICATOR CONSTRAINTS
# ============================================================================

class TestMathematicalBoundsAndIndicatorsFuzzing:
    """Stress tests numeric boundary constraints on technical indicators and signals."""

    @pytest.mark.parametrize("bad_rsi", [-0.01, -100.0, 100.01, 150.0, 999.0])
    def test_rsi_out_of_bounds_rejected_by_pydantic_and_schema(self, canonical_base_dict, bad_rsi):
        """RSI must be bounded between [0, 100]."""
        with pytest.raises(Exception):
            FinancialIndicators(rsi_14=bad_rsi)

        note = canonical_base_dict.copy()
        note["technical_indicators"]["rsi_14"] = bad_rsi
        is_valid, errors = validate_financial_note(note, is_ai_agent=True)
        assert is_valid is False, f"Vulnerability: Out-of-bounds RSI '{bad_rsi}' was accepted!"

    @pytest.mark.parametrize("bad_atr", [-0.0001, -1.0, -50.0])
    def test_atr_negative_rejected(self, canonical_base_dict, bad_atr):
        """ATR must be non-negative (>= 0)."""
        with pytest.raises(Exception):
            FinancialIndicators(atr_14=bad_atr)

        note = canonical_base_dict.copy()
        note["technical_indicators"]["atr_14"] = bad_atr
        is_valid, errors = validate_financial_note(note, is_ai_agent=True)
        assert is_valid is False, f"Vulnerability: Negative ATR '{bad_atr}' was accepted!"

    @pytest.mark.parametrize("bad_score", [-6, -10, 6, 10, 100])
    def test_signal_score_out_of_bounds_rejected(self, canonical_base_dict, bad_score):
        """Signal score must be between -5 and 5."""
        with pytest.raises(Exception):
            TradeSignal(score=bad_score)

        note = canonical_base_dict.copy()
        note["quantitative_signal"]["score"] = bad_score
        is_valid, errors = validate_financial_note(note, is_ai_agent=True)
        assert is_valid is False, f"Vulnerability: Out-of-bounds score '{bad_score}' was accepted!"

    @pytest.mark.parametrize("bad_conf", [-1, 6, 10])
    def test_signal_confluences_out_of_bounds_rejected(self, canonical_base_dict, bad_conf):
        """Confluences count must be between 0 and 5."""
        with pytest.raises(Exception):
            TradeSignal(confluences=bad_conf)

        note = canonical_base_dict.copy()
        note["quantitative_signal"]["confluences"] = bad_conf
        is_valid, errors = validate_financial_note(note, is_ai_agent=True)
        assert is_valid is False, f"Vulnerability: Out-of-bounds confluences '{bad_conf}' was accepted!"

    @pytest.mark.parametrize("bad_win_prob", [34.9, 0.0, -10.0, 90.1, 100.0])
    def test_win_probability_out_of_bounds_rejected(self, canonical_base_dict, bad_win_prob):
        """Win probability must be between 35% and 90%."""
        with pytest.raises(Exception):
            TradeSignal(win_probability_pct=bad_win_prob)

        note = canonical_base_dict.copy()
        note["quantitative_signal"]["win_probability_pct"] = bad_win_prob
        is_valid, errors = validate_financial_note(note, is_ai_agent=True)
        assert is_valid is False, f"Vulnerability: Out-of-bounds win prob '{bad_win_prob}' was accepted!"

    @pytest.mark.parametrize("bad_impact", [0, -1, 6, 10])
    def test_risk_impact_out_of_bounds_rejected(self, canonical_base_dict, bad_impact):
        """Risk impact must be an integer between 1 and 5."""
        with pytest.raises(Exception):
            RiskMetrics(impact=bad_impact)

        note = canonical_base_dict.copy()
        note["risk_metrics"]["impact"] = bad_impact
        is_valid, errors = validate_financial_note(note, is_ai_agent=True)
        assert is_valid is False, f"Vulnerability: Out-of-bounds impact '{bad_impact}' was accepted!"


# ============================================================================
# 6. ZERO-SECRET POLICY AUDIT IN SERIALIZATION
# ============================================================================

class TestZeroSecretPolicyAdversarial:
    """Verifies that serialization and note structures do not leak credentials or tokens."""

    def test_no_hardcoded_secrets_in_model_dumps(self, canonical_base_dict):
        """Ensures full serialization of FinancialNoteModel has zero secret signatures."""
        model = FinancialNoteModel(**canonical_base_dict)
        dumped = model.model_dump_json()

        secret_patterns = ["bearer", "api_key", "secret", "private_key", "password", "token", "sk-", "ghp_"]
        for pat in secret_patterns:
            assert pat not in dumped.lower(), f"Potential secret pattern '{pat}' detected in model output!"


# ============================================================================
# 7. MODEL ROUNDTRIP & SCHEMA CONSISTENCY
# ============================================================================

class TestDomainModelRoundtripConsistency:
    """Verifies round-trip consistency across Pydantic models and Draft-07 schema."""

    def test_financial_note_model_roundtrip(self, canonical_base_dict):
        """FinancialNoteModel -> dict -> validate_financial_note -> FinancialNoteModel."""
        model1 = FinancialNoteModel(**canonical_base_dict)
        d1 = model1.model_dump()

        is_valid, errors = validate_financial_note(d1, is_ai_agent=True)
        assert is_valid is True, f"Validation failed on dumped model: {errors}"

        model2 = FinancialNoteModel(**d1)
        assert model1.id == model2.id
        assert model1.ticker == model2.ticker
        assert model1.verification == model2.verification
        assert model1.lifecycle == model2.lifecycle
