import pytest
from cognitive_core.council_budget_controller import CouncilBudgetController, CouncilTier


def test_simple_task_skips_council():
    cbc = CouncilBudgetController()
    decision = cbc.decide("what is the weather", complexity=1)
    assert decision.tier == CouncilTier.NONE
    assert decision.should_dispatch is False


def test_complex_task_gets_light_tier():
    cbc = CouncilBudgetController(complexity_threshold=2)
    decision = cbc.decide("summarize the last 3 projects", complexity=2)
    assert decision.tier == CouncilTier.LIGHT
    assert decision.run_retrieval is False
    assert decision.run_verifier is True


def test_risky_keyword_forces_standard_tier_even_at_low_complexity():
    cbc = CouncilBudgetController()
    decision = cbc.decide("delete the old note", complexity=1)
    assert decision.tier == CouncilTier.STANDARD
    assert decision.run_retrieval is True
    assert decision.run_verifier is True


def test_risky_and_complex_forces_high_risk_tier():
    cbc = CouncilBudgetController(complexity_threshold=2)
    decision = cbc.decide("deploy to production", complexity=2)
    assert decision.tier == CouncilTier.HIGH_RISK


def test_explicit_require_review_forces_high_risk_regardless_of_complexity():
    cbc = CouncilBudgetController()
    decision = cbc.decide("trivial query", complexity=1, require_review=True)
    assert decision.tier == CouncilTier.HIGH_RISK


def test_custom_risky_keywords():
    cbc = CouncilBudgetController(risky_keywords=["nuclear"])
    assert cbc.decide("launch the nuclear codes", complexity=1).tier == CouncilTier.STANDARD
    assert cbc.decide("delete a file", complexity=1).tier == CouncilTier.NONE
