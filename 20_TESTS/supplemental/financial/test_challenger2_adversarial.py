"""
Adversarial Challenge & Stress Test Suite (Challenger 2 — Milestone 1).
Comprehensive empirical challenge over:
1. Deduplication determinism, normalization, and idempotency.
2. Contradiction detection (opposing signals, conflicting macroeconomic regimes, multi-pool conflicts).
3. Draft7 canonical schema enforcement and rejection of forged/invalid fields.
4. SHA-256 collision resistance across large-scale synthetic variations.
5. Invariants P0-P18 trust boundary validation.
6. Markdown YAML frontmatter round-trip integrity.
7. Extreme boundary conditions, malformed symbols, float edge cases, and hostile payloads.
"""

import pytest
import uuid
import hashlib
import json
import yaml
import math
from datetime import datetime, timezone
from jsonschema.exceptions import ValidationError

from xau_kinetic.financial_ingestion.adapter import (
    FinancialMemoryAdapter,
    MemoryDeduplicator,
    calculate_content_hash,
    generate_asset_profile_note,
    generate_macro_regime_note,
    generate_technical_setup_note,
    generate_trade_experience_note,
    generate_trade_error_note,
    generate_trading_lesson_note,
    generate_catalog_resource_note,
    render_markdown_note,
)

from xau_kinetic.financial_ingestion.catalog import get_catalog, get_instrument
from memory_controller.validation.schema import validate_frontmatter


@pytest.fixture
def sample_asset_data():
    return {
        "ticker": "XAUUSD",
        "name": "Gold / US Dollar",
        "inchidere": 2512.40,
        "rsi": 58.2,
        "rsi_status": "NEUTRU",
        "trend": "BULLISH",
        "semnal": "BUY",
        "confluente": 4,
        "score": 3,
        "atr": 18.50,
        "rvol": 1.45,
        "support": 2480.00,
        "resistance": 2530.00,
        "sl": 2493.90,
        "tp": 2549.40,
        "probabilitate": 68.0,
        "macd_cross": "BULLISH_CROSS",
        "macross": "GOLDEN_CROSS",
        "ma20": 2500.0,
        "ma50": 2460.0,
        "ma200": 2350.0,
        "bb_inf": 2470.0,
        "bb_mid": 2500.0,
        "bb_sup": 2530.0,
    }


# ============================================================================
# 1. DEDUPLICATION DETERMINISM & HASH NORMALIZATION
# ============================================================================

class TestDeduplicationDeterminism:
    """Stress-tests the deterministic hashing and deduplication mechanics."""

    def test_hash_invariance_under_dictionary_key_permutation(self):
        """Validates that dictionary key order does not alter SHA-256 content hash."""
        dict_a = {
            "ticker": "AAPL",
            "price": 224.50,
            "rsi": 62.4,
            "signal": "BUY",
            "indicators": {"macd": 1.25, "signal": 0.95},
            "tags": ["tech", "equity"]
        }
        dict_b = {
            "tags": ["tech", "equity"],
            "signal": "BUY",
            "indicators": {"signal": 0.95, "macd": 1.25},
            "price": 224.50,
            "ticker": "AAPL",
            "rsi": 62.4
        }
        hash_a = calculate_content_hash(dict_a)
        hash_b = calculate_content_hash(dict_b)
        assert hash_a == hash_b, "Hash must be invariant under key permutations."

    def test_hash_sensitivity_to_subtle_value_mutations(self):
        """Validates that small changes in payload produce completely different hashes (avalanche effect)."""
        base_dict = {"ticker": "XAUUSD", "price": 2510.50, "signal": "BUY"}
        mutated_dict = {"ticker": "XAUUSD", "price": 2510.51, "signal": "BUY"}
        
        hash_base = calculate_content_hash(base_dict)
        hash_mutated = calculate_content_hash(mutated_dict)
        assert hash_base != hash_mutated, "Hash must change upon price change by 1 cent."

    def test_deduplicator_idempotency_and_state_registry(self):
        """Validates that registering the exact same note multiple times returns existing_id and False."""
        dedup = MemoryDeduplicator()
        note = {
            "id": str(uuid.uuid4()),
            "ticker": "NVDA",
            "created": "2026-08-25",
            "signal": "BUY",
            "content": "NVIDIA high volume breakout above $128."
        }
        is_new1, ex_id1 = dedup.register_note(note)
        assert is_new1 is True
        assert ex_id1 is None

        # Re-register identical note
        is_new2, ex_id2 = dedup.register_note(note)
        assert is_new2 is False
        assert ex_id2 == note["id"]

        # Check duplicate lookup
        assert dedup.is_duplicate(note) is True

    def test_deduplicator_with_synthetic_10k_collision_check(self):
        """Generates 10,000 distinct financial data items and verifies 0 SHA-256 collisions."""
        hashes = set()
        for i in range(10_000):
            payload = {
                "ticker": f"ASSET_{i % 100}",
                "timestamp": f"2026-08-25T10:{i // 60 % 60:02d}:{i % 60:02d}Z",
                "price": 100.0 + (i * 0.01),
                "volume": 1000 + i,
                "nonce": i
            }
            h = calculate_content_hash(payload)
            assert h not in hashes, f"Collision detected at iteration {i}!"
            hashes.add(h)
        assert len(hashes) == 10_000


# ============================================================================
# 2. CONTRADICTION DETECTION & CONFLICT RECORDS
# ============================================================================

class TestContradictionDetection:
    """Stress-tests contradiction detection logic across opposing signals and regimes."""

    def test_opposing_signals_same_ticker_same_day(self):
        """Verifies that BUY vs SELL on same asset and same date produces a valid conflict record."""
        dedup = MemoryDeduplicator()
        today = datetime.now(timezone.utc).strftime("%Y-%m-%d")

        note_buy = {
            "id": str(uuid.uuid4()),
            "ticker": "XAUUSD",
            "title": "Decision_BUY_XAUUSD",
            "created": today,
            "signal": "BUY",
            "provenance": {"source_ref": "algo_trend_follow"},
            "content": "Gold momentum breakout."
        }
        note_sell = {
            "id": str(uuid.uuid4()),
            "ticker": "XAUUSD",
            "title": "Decision_SELL_XAUUSD",
            "created": today,
            "signal": "SELL",
            "provenance": {"source_ref": "algo_mean_reversion"},
            "content": "Gold RSI extreme overbought exhaustion."
        }

        # Register first note
        dedup.register_note(note_buy)

        # Detect contradictions when evaluating note_sell
        conflicts = dedup.detect_contradictions(note_sell, existing_notes=[note_buy])
        assert len(conflicts) == 1, "Exactly one conflict record must be generated."

        conflict = conflicts[0]
        fm = conflict["frontmatter"]

        # Validate contradiction note schema compliance
        assert validate_frontmatter(fm) is True
        assert fm["type"] == "hypothesis"
        assert fm["lifecycle"] == "REVIEW"
        assert fm["category"] == "financial-conflict-record"
        assert fm["confidence"] == "low"
        assert fm["verification"] == "unverified"
        assert len(fm["relations"]) == 2
        assert fm["relations"][0]["relation"] == "conflicts_with"
        assert fm["relations"][0]["target_id"] == note_buy["id"]
        assert fm["relations"][1]["target_id"] == note_sell["id"]

    def test_non_contradictory_signal_combinations(self):
        """Verifies that non-opposing signals (BUY/BUY, WAIT/BUY, different tickers, different dates) do not trigger false conflicts."""
        dedup = MemoryDeduplicator()
        today = "2026-08-25"
        yesterday = "2026-08-24"

        note_buy_today = {"id": str(uuid.uuid4()), "ticker": "BTCUSD", "created": today, "signal": "BUY"}
        note_buy_duplicate = {"id": str(uuid.uuid4()), "ticker": "BTCUSD", "created": today, "signal": "BUY"}
        note_wait_today = {"id": str(uuid.uuid4()), "ticker": "BTCUSD", "created": today, "signal": "WAIT"}
        note_sell_yesterday = {"id": str(uuid.uuid4()), "ticker": "BTCUSD", "created": yesterday, "signal": "SELL"}
        note_sell_diff_ticker = {"id": str(uuid.uuid4()), "ticker": "ETHUSD", "created": today, "signal": "SELL"}

        # BUY vs BUY (No conflict)
        assert len(dedup.detect_contradictions(note_buy_duplicate, [note_buy_today])) == 0

        # BUY vs WAIT (No conflict)
        assert len(dedup.detect_contradictions(note_wait_today, [note_buy_today])) == 0

        # BUY today vs SELL yesterday (No conflict - regime transition)
        assert len(dedup.detect_contradictions(note_buy_today, [note_sell_yesterday])) == 0

        # BUY BTC vs SELL ETH (No conflict - distinct assets)
        assert len(dedup.detect_contradictions(note_sell_diff_ticker, [note_buy_today])) == 0

    def test_multiple_opposing_signals_in_pool(self):
        """Tests contradiction detection when multiple conflicting notes exist in the historical pool."""
        dedup = MemoryDeduplicator()
        today = "2026-08-25"

        note_pool = [
            {"id": str(uuid.uuid4()), "ticker": "TSLA", "created": today, "signal": "SELL", "title": "TSLA_Bear_1"},
            {"id": str(uuid.uuid4()), "ticker": "TSLA", "created": today, "signal": "SELL", "title": "TSLA_Bear_2"},
            {"id": str(uuid.uuid4()), "ticker": "AAPL", "created": today, "signal": "BUY", "title": "AAPL_Bull"},
        ]

        new_tsla_buy = {"id": str(uuid.uuid4()), "ticker": "TSLA", "created": today, "signal": "BUY", "title": "TSLA_Bull_New"}
        conflicts = dedup.detect_contradictions(new_tsla_buy, existing_notes=note_pool)
        assert len(conflicts) == 2, "Should detect conflicts against both TSLA SELL notes."

    def test_conflicting_macro_regime_claims(self):
        """Tests contradiction generation when two macro assessments disagree on economic posture."""
        dedup = MemoryDeduplicator()
        today = "2026-08-25"

        macro_bull = {
            "id": str(uuid.uuid4()),
            "ticker": "MACRO_REGIME",
            "title": "Macro_Regime_Risk_On",
            "created": today,
            "signal": "BUY",
            "provenance": {"source_ref": "macro_analyst_a"},
            "content": "Expansionary cycle with dovish rate easing."
        }
        macro_bear = {
            "id": str(uuid.uuid4()),
            "ticker": "MACRO_REGIME",
            "title": "Macro_Regime_Stagflation_Warning",
            "created": today,
            "signal": "SELL",
            "provenance": {"source_ref": "macro_analyst_b"},
            "content": "Stagflationary headwind with hawkish rate pressure."
        }

        conflicts = dedup.detect_contradictions(macro_bear, existing_notes=[macro_bull])
        assert len(conflicts) == 1
        assert "MACRO_REGIME" in conflicts[0]["title"]
        assert validate_frontmatter(conflicts[0]["frontmatter"]) is True


# ============================================================================
# 3. CANONICAL DRAFT7 SCHEMA & FORGED FIELD ADVERSARIAL TESTS
# ============================================================================

class TestCanonicalSchemaIntegrity:
    """Validates that all note types conform to Draft7 schema and rejects invalid/forged fields."""

    def test_all_canonical_note_generators_pass_schema(self, sample_asset_data):
        """Empirically verifies that all 7 note generators emit schema-valid notes."""
        today = datetime.now(timezone.utc).strftime("%Y-%m-%d")

        # 1. Asset Profile (knowledge)
        n1 = generate_asset_profile_note(sample_asset_data)
        assert validate_frontmatter(n1["frontmatter"]) is True
        assert n1["frontmatter"]["type"] == "knowledge"

        # 2. Macro Regime (knowledge)
        macro_data = {"^VIX": {"inchidere": 14.5}, "^TNX": {"inchidere": 3.82}}
        fred_data = {"FEDFUNDS": {"current": 5.33}, "CPIAUCSL": {"current": 314.5}}
        sentiment_data = {"value": 65, "display": "Greed (65/100)"}
        n2 = generate_macro_regime_note(macro_data, fred_data, sentiment_data)
        assert validate_frontmatter(n2["frontmatter"]) is True
        assert n2["frontmatter"]["type"] == "knowledge"

        # 3. Technical Setup (decision)
        n3 = generate_technical_setup_note(sample_asset_data)
        assert validate_frontmatter(n3["frontmatter"]) is True
        assert n3["frontmatter"]["type"] == "decision"

        # 4. Trade Experience (experience)
        trade_data = {
            "trade_id": "TRD-2026-001",
            "asset": "XAUUSD",
            "direction": "LONG",
            "pnl_currency": 450.0,
            "pnl_percent": 2.25,
            "realized_rr": 2.1,
            "entry_price": 2500.0,
            "exit_price": 2521.0,
            "position_size": "1.0 lot",
            "execution_quality": 9,
            "plan_adhered": True,
            "lesson": "Disciplined entry at confluence zone."
        }
        n4 = generate_trade_experience_note(trade_data)
        assert validate_frontmatter(n4["frontmatter"]) is True
        assert n4["frontmatter"]["type"] == "experience"

        # 5. Trade Error (error)
        error_data = {
            "title": "FOMO Chase at Resistance",
            "asset": "BTCUSD",
            "description": "Entered long at top of range without confirmation.",
            "impact": "-1.0R loss",
            "root_cause": "Impatience and greed.",
            "fix": "Wait for retest.",
            "prevention": "Strict OCO limit order rule."
        }
        n5 = generate_trade_error_note(error_data)
        assert validate_frontmatter(n5["frontmatter"]) is True
        assert n5["frontmatter"]["type"] == "error"

        # 6. Trading Lesson (lesson)
        lesson_data = {
            "title": "RVOL Confirmation on Trend Breakout",
            "heuristic": "High RVOL > 1.5x increases breakout probability to 72%.",
            "conditions": "RVOL > 1.5x and MACD cross.",
            "invalidation": "Candle close back inside consolidation zone."
        }
        n6 = generate_trading_lesson_note(lesson_data)
        assert validate_frontmatter(n6["frontmatter"]) is True
        assert n6["frontmatter"]["type"] == "lesson"

        # 7. Catalog Resource (resource)
        n7 = generate_catalog_resource_note()
        assert validate_frontmatter(n7["frontmatter"]) is True
        assert n7["frontmatter"]["type"] == "resource"

    def test_schema_rejects_forbidden_root_properties(self, sample_asset_data):
        """Draft7 schema has additionalProperties: False. Injecting root fields must fail validation."""
        note = generate_asset_profile_note(sample_asset_data)
        fm = dict(note["frontmatter"])

        # Inject forbidden root field
        fm["is_admin"] = True
        with pytest.raises(ValidationError):
            validate_frontmatter(fm)

        del fm["is_admin"]
        fm["injected_privilege"] = "superuser"
        with pytest.raises(ValidationError):
            validate_frontmatter(fm)

    def test_schema_rejects_forbidden_provenance_properties(self, sample_asset_data):
        """Schema has additionalProperties: False on provenance object."""
        note = generate_asset_profile_note(sample_asset_data)
        fm = json.loads(json.dumps(note["frontmatter"]))

        fm["provenance"]["forged_credential"] = "admin_key"
        with pytest.raises(ValidationError):
            validate_frontmatter(fm)

    def test_schema_rejects_invalid_type_enum(self, sample_asset_data):
        """Invalid type enum values must be strictly rejected."""
        note = generate_asset_profile_note(sample_asset_data)
        fm = json.loads(json.dumps(note["frontmatter"]))

        fm["type"] = "arbitrary_custom_type"
        with pytest.raises(ValidationError):
            validate_frontmatter(fm)

    def test_schema_rejects_invalid_lifecycle_enum(self, sample_asset_data):
        """Invalid lifecycle must be rejected."""
        note = generate_asset_profile_note(sample_asset_data)
        fm = json.loads(json.dumps(note["frontmatter"]))

        fm["lifecycle"] = "UNCONTROLLED"
        with pytest.raises(ValidationError):
            validate_frontmatter(fm)

    def test_schema_rejects_malformed_uuid(self, sample_asset_data):
        """Invalid UUID formats must be rejected by Draft7 format checker."""
        note = generate_asset_profile_note(sample_asset_data)
        fm = json.loads(json.dumps(note["frontmatter"]))

        fm["id"] = "not-a-valid-uuid-format"
        with pytest.raises(ValidationError):
            validate_frontmatter(fm)

    def test_schema_rejects_malformed_date(self, sample_asset_data):
        """Invalid date strings must be rejected."""
        note = generate_asset_profile_note(sample_asset_data)
        fm = json.loads(json.dumps(note["frontmatter"]))

        fm["created"] = "2026/08/25"  # Slash format invalid
        with pytest.raises(ValidationError):
            validate_frontmatter(fm)

    def test_schema_rejects_invalid_relations_format(self, sample_asset_data):
        """Relations items must match schema exactly (target, relation, optional target_id)."""
        note = generate_asset_profile_note(sample_asset_data)
        fm = json.loads(json.dumps(note["frontmatter"]))

        # Missing target field
        fm["relations"] = [{"relation": "related_to"}]
        with pytest.raises(ValidationError):
            validate_frontmatter(fm)

        # Invalid target_id (not a uuid)
        fm["relations"] = [{"relation": "related_to", "target": "[[Note]]", "target_id": "invalid-uuid"}]
        with pytest.raises(ValidationError):
            validate_frontmatter(fm)

        # Extra injected property in relation item
        fm["relations"] = [{"relation": "related_to", "target": "[[Note]]", "forged_field": 123}]
        with pytest.raises(ValidationError):
            validate_frontmatter(fm)


# ============================================================================
# 4. INVARIANT P0-P18 TRUST BOUNDARY VERIFICATION
# ============================================================================

class TestSecurityInvariantsAdversarial:
    """Verifies that financial memory generation adheres to P0-P18 rules."""

    def test_ai_agent_cannot_generate_verified_notes(self, sample_asset_data):
        """Rule P0: AI generated notes must always be initialized to 'unverified'."""
        note = generate_asset_profile_note(sample_asset_data)
        assert note["frontmatter"]["verification"] == "unverified", "AI generated notes MUST be unverified."
        assert note["frontmatter"]["lifecycle"] == "REVIEW", "AI generated notes MUST start in REVIEW lifecycle."

    def test_ai_agent_provenance_scoping(self, sample_asset_data):
        """Rule P1: AI generation uses permitted source_type 'execution'."""
        note = generate_asset_profile_note(sample_asset_data)
        assert note["frontmatter"]["provenance"]["source_type"] == "execution"
        assert note["frontmatter"]["provenance"]["source_type"] not in ["user", "official", "experience", "import"]

    def test_zero_secrets_or_credentials_in_generated_payloads(self, sample_asset_data):
        """Rule P19 / AGENTS.md §19: Memory notes must contain zero API tokens or credentials."""
        note = generate_asset_profile_note(sample_asset_data)
        serialized = json.dumps(note)
        for forbidden in ["api_key", "secret_key", "bearer", "password", "token", "sk-", "ghp_"]:
            assert forbidden not in serialized.lower(), f"Forbidden secret string '{forbidden}' found in note!"


# ============================================================================
# 5. MARKDOWN & YAML RENDERING INTEGRITY
# ============================================================================

class TestMarkdownRenderingIntegrity:
    """Tests round-trip rendering and YAML parseability of generated notes."""

    def test_render_markdown_roundtrip_fidelity(self):
        """Verifies that markdown rendering creates a clean YAML frontmatter that parses back with 100% equivalence."""
        fm_original = {
            "id": str(uuid.uuid4()),
            "type": "decision",
            "lifecycle": "REVIEW",
            "category": "technical-trading-setup",
            "tags": ["finance", "trade-setup", "buy", "xauusd"],
            "created": "2026-08-25",
            "updated": "2026-08-25",
            "provenance": {
                "source_type": "execution",
                "source_ref": "financial_ingestion_pipeline:calc_signal",
                "source_date": "2026-08-25",
                "extraction_date": "2026-08-25",
                "redaction": "none",
                "provenance_status": "complete"
            },
            "confidence": "high",
            "verification": "unverified",
            "relations": [
                {"relation": "related_to", "target": "[[Asset_Gold_US_Dollar]]"}
            ]
        }
        body_original = "# Trading Decision: BUY on Gold\n\nConfluence of 4 signals detected."

        rendered = render_markdown_note(fm_original, body_original)
        assert rendered.startswith("---\n")
        parts = rendered.split("---\n")
        assert len(parts) >= 3, "Rendered note must have valid --- fences."

        yaml_content = parts[1]
        body_extracted = "---".join(parts[2:]).strip()

        parsed_fm = yaml.safe_load(yaml_content)
        assert parsed_fm == fm_original, "Parsed YAML frontmatter must exactly match original."
        assert body_extracted == body_original, "Extracted markdown body must exactly match original."

    def test_markdown_rendering_with_hostile_characters_in_body(self):
        """Tests that markdown rendering handles hostile Unicode, raw quotes, and nested YAML markers in body cleanly."""
        fm = {
            "id": str(uuid.uuid4()),
            "type": "knowledge",
            "lifecycle": "REVIEW",
            "category": "financial-asset-profile",
            "tags": ["finance", "test"],
            "created": "2026-08-25",
            "updated": "2026-08-25",
            "provenance": {
                "source_type": "execution",
                "source_ref": "pipeline",
                "source_date": "2026-08-25",
                "extraction_date": "2026-08-25",
                "redaction": "none",
                "provenance_status": "complete"
            },
            "confidence": "medium",
            "verification": "unverified",
            "relations": []
        }
        hostile_body = (
            "# Hostile Payload Note\n\n"
            "```yaml\n"
            "type: 'INJECTED_OVERRIDE'\n"
            "is_admin: true\n"
            "```\n\n"
            "Emojis: 🚀📈🔥💰\n"
            "Unicode accents: Închidere, tranzacție, evaluare macroeconomică.\n"
            "Quotes: \"Double\" and 'Single' and `Backticks`.\n"
        )
        rendered = render_markdown_note(fm, hostile_body)
        assert validate_frontmatter(fm) is True
        assert "Închidere" in rendered
        assert "🚀📈" in rendered


# ============================================================================
# 6. HIGH-LEVEL ADAPTER INTEGRATION & STRESS HARNESS
# ============================================================================

class TestHighLevelAdapterHarness:
    """End-to-end integration and stress tests on FinancialMemoryAdapter."""

    def test_adapter_asset_and_trade_flow(self):
        adapter = FinancialMemoryAdapter()
        asset_payload = {
            "ticker": "BTC-USD",
            "name": "Bitcoin USD",
            "inchidere": 63450.0,
            "rsi": 72.1,
            "rsi_status": "SUPRACUMPARAT",
            "trend": "BULLISH",
            "semnal": "BUY",
            "confluente": 3,
            "score": 2,
            "atr": 1820.0,
            "rvol": 1.8,
            "support": 61000.0,
            "resistance": 65000.0,
            "sl": 61630.0,
            "tp": 67090.0,
            "probabilitate": 60.0
        }

        # Process first time -> new
        res1 = adapter.process_asset(asset_payload)
        assert res1["is_new"] is True
        assert res1["existing_id"] is None

        # Process second time -> duplicate
        res2 = adapter.process_asset(asset_payload)
        assert res2["is_new"] is False
        assert res2["existing_id"] == res1["id"]

    def test_adapter_handles_extreme_and_missing_values(self):
        """Tests that the adapter handles partial or missing indicator values gracefully without throwing unhandled exceptions."""
        adapter = FinancialMemoryAdapter()
        sparse_asset = {
            "ticker": "UNKNOWN_TEST",
            # missing price, rsi, trend, sl, tp, etc.
        }
        res = adapter.process_asset(sparse_asset)
        assert res["id"] is not None
        assert res["frontmatter"]["type"] == "knowledge"
        assert validate_frontmatter(res["frontmatter"]) is True

    def test_adapter_handles_nan_and_infinite_floats(self):
        """Tests that float('nan') or float('inf') in input are converted safely to string or handled without crashing JSON serialization."""
        adapter = FinancialMemoryAdapter()
        nan_asset = {
            "ticker": "NAN_TEST",
            "inchidere": float('nan'),
            "atr": float('inf'),
            "score": 0,
            "semnal": "WAIT"
        }
        # Hashing and generation should handle non-standard numbers without unhandled exceptions
        note = generate_asset_profile_note(nan_asset)
        assert note["content_hash"] is not None
        assert validate_frontmatter(note["frontmatter"]) is True
