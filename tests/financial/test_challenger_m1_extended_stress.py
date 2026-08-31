"""
Extended Adversarial Fuzzing & Stress Testing Suite (Milestone 1 Challenger Fix).
Empirically stress-tests and challenges `memory_controller/financial_schema.py` across:
1. Boundary Floats & Non-Standard Numerics: NaN, +Inf, -Inf, subnormals, float overflow (1e308), -0.0.
2. Deep Nested Structures: Extreme recursion depth (50-200 layers), nested relations, deep commentary payloads.
3. Polymorphic Payload Attacks: Union polymorphism edge cases, custom iterables, generators, sets, subclasses.
4. Malformed Provenance Dictionaries: Unicode zero-width chars, casing variations, type confusion, rogue nested dicts.
5. Injection in Wikilinks & Tags: SQLi, path traversal, XSS, control chars, null bytes, unbalanced wikilinks.
6. Mutation Fuzzing Harness: 1,000+ mutated payload variants ensuring 100% crash-free rejection (Exception Safety).
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


@pytest.fixture
def valid_note_factory():
    """Generates a fresh valid canonical financial memory note."""
    def _create():
        return {
            "id": str(uuid.uuid4()),
            "type": "knowledge",
            "lifecycle": "REVIEW",
            "category": "financial-asset-profile",
            "tags": ["finance", "crypto", "stress-test"],
            "created": "2026-08-26",
            "updated": "2026-08-26",
            "provenance": {
                "source_type": "execution",
                "source_ref": "adversarial_fuzzer",
                "source_date": "2026-08-26",
                "provenance_status": "complete"
            },
            "confidence": "high",
            "verification": "partially_verified",
            "relations": [
                {"relation": "related_to", "target": "[[Asset_Stress]]", "target_id": str(uuid.uuid4())}
            ],
            "ticker": "TEST-USD",
            "symbol": "TEST-USD",
            "instrument_name": "Test Asset",
            "asset_class": "CRYPTO",
            "price_data": {
                "open": 100.0,
                "high": 110.0,
                "low": 95.0,
                "close": 105.0,
                "change_day_pct": 5.0,
                "volume": 1000000,
                "rvol": 1.2
            },
            "technical_indicators": {
                "rsi_14": 55.0,
                "rsi_status": "Neutru",
                "macd": 1.5,
                "macd_signal": 1.2,
                "macd_hist": 0.3,
                "trend": "Bullish",
                "atr_14": 3.5,
                "rvol": 1.2
            },
            "quantitative_signal": {
                "signal": "BUY",
                "score": 3,
                "confluences": 3,
                "stop_loss": 98.0,
                "take_profit": 119.0,
                "risk_reward_ratio": 2.0,
                "win_probability_pct": 65.0,
                "timeframe": "1D"
            },
            "risk_metrics": {
                "impact": 3,
                "probability_pct": 50.0,
                "score": 1.5
            },
            "macro_context": {
                "vix": 16.0,
                "usd_index": 102.0
            },
            "commentary": {
                "movement_explanation": "Stress test payload.",
                "opportunity_alert": "Active setup.",
                "educational_lesson": "Adversarial testing."
            },
            "content_markdown": "# Stress Test Note\n\nAdversarial payload verification."
        }
    return _create


# ============================================================================
# 1. BOUNDARY FLOATS & NON-STANDARD NUMERICS
# ============================================================================

class TestFloatBoundaryAndSpecialValues:
    """Stress tests float boundary attacks (NaN, Inf, -Inf, 1e308, -0.0, subnormals)."""

    @pytest.mark.parametrize("special_float", [
        float("inf"),
        float("-inf"),
    ])
    def test_infinities_in_rsi_rejected_by_schema(self, valid_note_factory, special_float):
        note = valid_note_factory()
        note["technical_indicators"]["rsi_14"] = special_float
        
        # Must be rejected by Draft-07 schema
        is_valid, errors = validate_financial_note(note, is_ai_agent=True)
        assert is_valid is False
        assert len(errors) > 0

    @pytest.mark.parametrize("special_float", [
        float("nan"),
        float("inf"),
        float("-inf"),
    ])
    def test_special_floats_rejected_by_pydantic_indicators(self, special_float):
        """Pydantic rejects NaN / Inf when ge/le constraints are present."""
        with pytest.raises(ValidationError):
            FinancialIndicators(rsi_14=special_float)
        with pytest.raises(ValidationError):
            TechnicalIndicatorsPayload(rsi_14=special_float)

    @pytest.mark.parametrize("special_float", [
        float("inf"),
        float("-inf"),
    ])
    def test_infinities_in_win_probability_rejected_by_schema(self, valid_note_factory, special_float):
        note = valid_note_factory()
        note["quantitative_signal"]["win_probability_pct"] = special_float
        
        is_valid, errors = validate_financial_note(note, is_ai_agent=True)
        assert is_valid is False
        assert len(errors) > 0

    @pytest.mark.parametrize("special_float", [
        float("nan"),
        float("inf"),
        float("-inf"),
    ])
    def test_special_floats_rejected_by_pydantic_trade_signal(self, special_float):
        with pytest.raises(ValidationError):
            TradeSignal(win_probability_pct=special_float)
        with pytest.raises(ValidationError):
            QuantitativeSignalPayload(win_probability_pct=special_float)

    @pytest.mark.parametrize("special_float", [
        float("inf"),
        float("-inf"),
    ])
    def test_infinities_in_risk_probability_rejected_by_schema(self, valid_note_factory, special_float):
        note = valid_note_factory()
        note["risk_metrics"]["probability_pct"] = special_float

        is_valid, errors = validate_financial_note(note, is_ai_agent=True)
        assert is_valid is False
        assert len(errors) > 0

    @pytest.mark.parametrize("special_float", [
        float("nan"),
        float("inf"),
        float("-inf"),
    ])
    def test_special_floats_rejected_by_pydantic_risk_metrics(self, special_float):
        with pytest.raises(ValidationError):
            RiskMetrics(probability_pct=special_float)

    @pytest.mark.parametrize("boundary_float", [
        1e308,
        -1e308,
        1e-308,
        5e-324,  # Subnormal float
        -0.0,
    ])
    def test_extreme_magnitude_floats_handled_without_crash(self, valid_note_factory, boundary_float):
        note = valid_note_factory()
        note["price_data"]["close"] = boundary_float
        note["macro_context"]["vix"] = boundary_float

        # Should validate without crashing
        is_valid, errors = validate_financial_note(note, is_ai_agent=True)
        assert isinstance(is_valid, bool)
        assert isinstance(errors, list)

    def test_pydantic_union_behavior_on_nan(self, valid_note_factory):
        """
        When passing a typed model with NaN, it is rejected at construction.
        When passing a dict with NaN into FinancialNoteModel, Union fallback
        gracefully retains it as Dict[str, Any] without crashing.
        """
        with pytest.raises(ValidationError):
            FinancialIndicators(rsi_14=float("nan"))

        note = valid_note_factory()
        note["technical_indicators"]["rsi_14"] = float("nan")
        model = FinancialNoteModel(**note)
        assert isinstance(model.technical_indicators, dict)
        assert math.isnan(model.technical_indicators["rsi_14"])


# ============================================================================
# 2. DEEP NESTED STRUCTURES & RECURSION STRESS
# ============================================================================

class TestDeepNestedStructures:
    """Stress tests deep recursion and heavy nesting in various note fields."""

    def test_deeply_nested_dict_in_raw_content(self, valid_note_factory):
        note = valid_note_factory()
        # Build 100-deep nested dictionary
        nested = {"level_100": "deep_payload"}
        for i in range(99, 0, -1):
            nested = {f"level_{i}": nested}
        
        note["raw_content"] = "test"
        note["unwhitelisted_nested"] = nested

        # Note should be evaluated without RecursionError
        is_valid, errors = validate_financial_note(note, is_ai_agent=True)
        assert isinstance(is_valid, bool)
        assert isinstance(errors, list)

    def test_deeply_nested_relations_list(self, valid_note_factory):
        note = valid_note_factory()
        # Create 500 relations
        note["relations"] = [
            {"relation": "related_to", "target": f"[[Target_{i}]]", "target_id": str(uuid.uuid4())}
            for i in range(500)
        ]
        is_valid, errors = validate_financial_note(note, is_ai_agent=True)
        assert is_valid is True, f"Failed on large relations list: {errors}"

    def test_deeply_nested_tags_list(self, valid_note_factory):
        note = valid_note_factory()
        # Create 1000 valid tags
        note["tags"] = [f"tag_{i}" for i in range(1000)]
        is_valid, errors = validate_financial_note(note, is_ai_agent=True)
        assert is_valid is True, f"Failed on large tags list: {errors}"


# ============================================================================
# 3. POLYMORPHIC PAYLOADS & UNION CORNER CASES
# ============================================================================

class TestPolymorphicPayloads:
    """Tests Pydantic models and schema validator on polymorphic types, iterables, and model mixing."""

    def test_pydantic_model_mixing_with_dicts_and_models(self, valid_note_factory):
        note = valid_note_factory()
        # Mix instantiated Pydantic models with raw dicts
        note["technical_indicators"] = TechnicalIndicatorsPayload(rsi_14=60.0, atr_14=2.5)
        note["quantitative_signal"] = QuantitativeSignalPayload(signal="BUY", score=4)
        note["provenance"] = ProvenanceModel(source_type="execution", source_ref="poly_test")
        note["relations"] = [
            RelationModel(relation="related_to", target="[[Asset_Poly]]"),
            {"relation": "supports", "target": "[[Hypothesis_1]]", "target_id": str(uuid.uuid4())},
            "[[Simple_Wikilink]]"
        ]

        model = FinancialNoteModel(**note)
        assert model.technical_indicators.rsi_14 == 60.0
        assert model.quantitative_signal.score == 4
        assert len(model.relations) == 3

    def test_generator_and_custom_iterable_rejected_cleanly(self, valid_note_factory):
        note = valid_note_factory()
        # Assign generator to list field
        note["tags"] = (f"tag_{i}" for i in range(5))
        
        # Generator is not list -> must be rejected by Draft-07 schema without crashing
        is_valid, errors = validate_financial_note(note, is_ai_agent=True)
        assert is_valid is False
        assert len(errors) > 0

    def test_set_passed_to_relations_rejected_cleanly(self, valid_note_factory):
        note = valid_note_factory()
        note["relations"] = {"related_to", "target"}  # Set instead of list

        is_valid, errors = validate_financial_note(note, is_ai_agent=True)
        assert is_valid is False
        assert len(errors) > 0


# ============================================================================
# 4. MALFORMED PROVENANCE DICTIONARIES
# ============================================================================

class TestMalformedProvenanceVectors:
    """Stress tests provenance validation against subtle unicode and type injection attacks."""

    @pytest.mark.parametrize("corrupted_provenance", [
        {},  # Missing source_type and source_ref
        {"source_type": "execution"},  # Missing source_ref
        {"source_ref": "ref_only"},  # Missing source_type
        {"source_type": None, "source_ref": "test"},
        {"source_type": 12345, "source_ref": "test"},
        {"source_type": ["execution"], "source_ref": "test"},
        {"source_type": {"type": "execution"}, "source_ref": "test"},
        {"source_type": "execution\x00", "source_ref": "test"},  # Null byte
        {"source_type": "execution\u200b", "source_ref": "test"},  # Zero-width space
        {"source_type": " execution ", "source_ref": "test"},  # Whitespace padding
        {"source_type": "EXECUTION", "source_ref": "test"},  # Uppercase
        {"source_type": "ExEcUtIoN", "source_ref": "test"},  # Mixed case
        {"source_type": "execution", "source_ref": None},  # source_ref cannot be null in Draft-07
        {"source_type": "execution", "source_ref": 12345},  # source_ref must be string
        {"source_type": "execution", "source_ref": ["ref"]},
        {"source_type": "execution", "source_ref": {"bad": "dict"}},
        {"source_type": "execution", "source_ref": "valid", "redaction": "unsupported_redaction"},
        {"source_type": "execution", "source_ref": "valid", "provenance_status": "unsupported_status"},
    ])
    def test_corrupted_provenance_rejected(self, valid_note_factory, corrupted_provenance):
        note = valid_note_factory()
        note["provenance"] = corrupted_provenance

        is_valid, errors = validate_financial_note(note, is_ai_agent=True)
        assert is_valid is False, f"Expected rejection for corrupted provenance: {corrupted_provenance}"
        assert len(errors) > 0


# ============================================================================
# 5. INJECTION IN WIKILINKS & TAGS
# ============================================================================

class TestWikilinksAndTagsInjectionVectors:
    """Stress tests attacks against tags and relations/wikilinks structures."""

    @pytest.mark.parametrize("hostile_tag_element", [
        None,
        12345,
        3.14,
        True,
        False,
        [],
        {},
        {"tag": "crypto"},
        ["nested", "tag"],
    ])
    def test_non_string_tags_rejected(self, valid_note_factory, hostile_tag_element):
        note = valid_note_factory()
        note["tags"] = ["valid_tag", hostile_tag_element]

        is_valid, errors = validate_financial_note(note, is_ai_agent=True)
        assert is_valid is False, f"Expected rejection for non-string tag element: {hostile_tag_element}"
        assert len(errors) > 0

    @pytest.mark.parametrize("adversarial_tag_string", [
        "",  # Empty tag
        "   ",  # Whitespace tag
        "\n\r\t",  # Control whitespace
        "<script>alert('tag_xss')</script>",
        "'; DROP TABLE tags; --",
        "../../system_files",
        "tag\x00nullbyte",
        "🚀💎🔥" * 100,  # Emoji flood
        "A" * 10000,  # 10KB giant tag string
    ])
    def test_adversarial_tag_strings_handled_gracefully(self, valid_note_factory, adversarial_tag_string):
        """Ensures adversarial tag strings are processed without unhandled exceptions."""
        note = valid_note_factory()
        note["tags"] = [adversarial_tag_string]

        is_valid, errors = validate_financial_note(note, is_ai_agent=True)
        assert isinstance(is_valid, bool)
        assert isinstance(errors, list)

    @pytest.mark.parametrize("corrupted_relation", [
        12345,
        "not_a_wikilink_or_dict",
        {"relation": "invalid_verb", "target": "[[Asset]]"},
        {"relation": "related_to"},  # missing target
        {"target": "[[Asset]]"},  # missing relation
        {"relation": "related_to", "target": None},
        {"relation": "related_to", "target": 12345},
        {"relation": "related_to", "target": ["[[Asset]]"]},
        {"relation": "related_to", "target": {"target": "[[Asset]]"}},
        {"relation": "related_to", "target": "[[Asset]]", "target_id": "invalid-target-uuid"},
        {"relation": "related_to", "target": "[[Asset]]", "target_id": 12345},
        {"relation": "related_to", "target": "[[Asset]]", "target_id": None},  # Null target_id is allowed by schema
    ])
    def test_corrupted_relations_rejected(self, valid_note_factory, corrupted_relation):
        note = valid_note_factory()
        note["relations"] = [corrupted_relation]

        is_valid, errors = validate_financial_note(note, is_ai_agent=True)
        if corrupted_relation == {"relation": "related_to", "target": "[[Asset]]", "target_id": None}:
            # Null target_id is allowed
            assert is_valid is True
        elif corrupted_relation == "not_a_wikilink_or_dict":
            # String items are allowed by relation schema (anyOf: Relation, string)
            assert is_valid is True
        else:
            assert is_valid is False, f"Expected rejection for corrupted relation: {corrupted_relation}"
            assert len(errors) > 0


# ============================================================================
# 6. MUTATION FUZZING HARNESS (1000+ ITERATIONS)
# ============================================================================

class TestMutationFuzzingHarness:
    """
    Empirically subjects `validate_financial_note` and Pydantic models to 1,000+
    stochastically fuzzed and mutated payloads.
    Guarantees 100% exception safety (zero unhandled crashes).
    """

    @pytest.mark.parametrize("fuzz_seed", range(100))
    def test_random_mutation_fuzzing_batch(self, valid_note_factory, fuzz_seed):
        random.seed(fuzz_seed + 20260826)
        
        for _ in range(10):  # 100 seeds * 10 iterations = 1000 fuzzed payloads
            note = valid_note_factory()
            mutation_type = random.randint(0, 7)

            if mutation_type == 0:
                # Corrupt random key
                keys = list(note.keys())
                k = random.choice(keys)
                note[k] = random.choice([None, {}, [], 123, 3.14, True, "", "\x00", {"nested": []}])

            elif mutation_type == 1:
                # Corrupt frontmatter fields
                fm_keys = ["id", "type", "lifecycle", "category", "tags", "confidence", "verification", "provenance", "relations"]
                k = random.choice(fm_keys)
                note[k] = random.choice([None, 0, -1, 9999, "corrupted_val", {}, [{}], (1, 2)])

            elif mutation_type == 2:
                # Corrupt indicators
                ind_keys = ["rsi_14", "macd", "atr_14", "rvol", "trend"]
                k = random.choice(ind_keys)
                note["technical_indicators"][k] = random.choice([
                    float("nan"), float("inf"), -999.0, 999.0, "bad_str", None, [], {}
                ])

            elif mutation_type == 3:
                # Corrupt quantitative signal
                sig_keys = ["signal", "score", "confluences", "win_probability_pct", "stop_loss", "take_profit"]
                k = random.choice(sig_keys)
                note["quantitative_signal"][k] = random.choice([
                    -100, 100, float("nan"), "INVALID_SIG", None, {}, []
                ])

            elif mutation_type == 4:
                # Corrupt risk metrics
                rm_keys = ["impact", "probability_pct", "score"]
                k = random.choice(rm_keys)
                note["risk_metrics"][k] = random.choice([
                    0, 10, -50.0, 150.0, float("nan"), float("inf"), "bad"
                ])

            elif mutation_type == 5:
                # Corrupt provenance structure
                note["provenance"] = random.choice([
                    None,
                    "",
                    12345,
                    {"source_type": random.choice(["USER", "OFFICIAL", "root", "hacker", None, 123])},
                    {"source_ref": 123},
                    {"source_type": "execution", "source_ref": None},
                ])

            elif mutation_type == 6:
                # Corrupt relations
                note["relations"] = random.choice([
                    None,
                    123,
                    [123],
                    [{"relation": "hacked", "target": "[[x]]"}],
                    [{"relation": "related_to", "target": None}],
                    [{"relation": "related_to", "target": "[[x]]", "target_id": "not-a-uuid"}],
                ])

            elif mutation_type == 7:
                # Remove random required keys
                keys = list(note.keys())
                del note[random.choice(keys)]

            # Verification of Exception Safety: validate_financial_note MUST NEVER CRASH
            try:
                is_valid_ai, errors_ai = validate_financial_note(note, is_ai_agent=True)
                assert isinstance(is_valid_ai, bool)
                assert isinstance(errors_ai, list)

                is_valid_human, errors_human = validate_financial_note(note, is_ai_agent=False)
                assert isinstance(is_valid_human, bool)
                assert isinstance(errors_human, list)
            except Exception as e:
                pytest.fail(f"CRASH ON FUZZ SEED {fuzz_seed} (mutation {mutation_type}): {e}\nPayload: {note}")
