"""The two properties this module exists for, and the arithmetic under them.

Section 15 of the brief: *"The system must be able to refuse a trade even when
EV appears positive."* Section 16: *"Never allow mathematical Kelly sizing to
override risk caps."* Those are the two tests that matter; the rest support
them.
"""
from __future__ import annotations

import math

import pytest

from packages.polymarket.risk_sizing import (
    SIZE_APPROVED,
    SIZE_REFUSED,
    SIZING_FIXED_FRACTIONAL,
    MarketConditions,
    PortfolioState,
    RiskLimits,
    SizingPolicy,
    check_limits,
    kelly_fraction,
    size_position,
)


def conditions(**overrides) -> MarketConditions:
    base = dict(price=0.50, available_depth=100_000.0, quote_age_seconds=10.0,
                resolution_risk=0.05, net_edge=0.10)
    base.update(overrides)
    return MarketConditions(**base)


def portfolio(**overrides) -> PortfolioState:
    base = dict(bankroll=100_000.0, calibration_samples=500)
    base.update(overrides)
    return PortfolioState(**base)


# --- the first property: EV does not buy permission --------------------------

@pytest.mark.parametrize("state,market,expected", [
    (dict(kill_switch_engaged=True), {}, "kill_switch_engaged"),
    (dict(drawdown_fraction=0.40), {}, "drawdown_limit_breached"),
    (dict(realised_loss_today=9_000.0), {}, "daily_loss_limit_breached"),
    (dict(open_exposure=25_000.0), {}, "portfolio_exposure_exhausted"),
    (dict(category_exposure=9_000.0), {}, "category_exposure_exhausted"),
    (dict(unresolved_exposure=20_000.0), {}, "unresolved_exposure_exhausted"),
    (dict(calibration_samples=3), {}, "insufficient_calibration_sample"),
    ({}, dict(quote_age_seconds=4_000.0), "stale_quote"),
    ({}, dict(resolution_risk=0.90), "resolution_risk_above_maximum"),
    ({}, dict(net_edge=0.001), "net_edge_below_minimum"),
    ({}, dict(available_depth=0.0), "no_available_depth"),
])
def test_a_positive_ev_trade_is_refused_on_any_single_limit(state, market, expected):
    """Every one of these carries an edge of 20 points, which is enormous.

    None of them may trade. A risk engine that only refuses bad trades is a
    filter; one that refuses good trades for reasons outside the trade is a
    risk engine.
    """
    decision = size_position(
        probability=0.70, conditions=conditions(**market),
        portfolio=portfolio(**state),
    )
    assert decision.outcome == SIZE_REFUSED
    assert expected in decision.refusals
    assert decision.size == 0.0


def test_every_reason_is_reported_not_just_the_first():
    """A report naming one cause sends someone to fix it, and they find the
    next only after they have."""
    decision = size_position(
        probability=0.70,
        conditions=conditions(quote_age_seconds=9_999.0, resolution_risk=0.95),
        portfolio=portfolio(drawdown_fraction=0.50, kill_switch_engaged=True),
    )
    assert {"kill_switch_engaged", "stale_quote", "resolution_risk_above_maximum",
            "drawdown_limit_breached"} <= set(decision.refusals)


def test_a_clean_book_with_a_real_edge_does_trade():
    """The counterpart. A gate that refuses everything is not a gate."""
    decision = size_position(0.70, conditions(), portfolio())
    assert decision.outcome == SIZE_APPROVED
    assert decision.size > 0.0


# --- the second property: Kelly never beats a cap ----------------------------

def test_kelly_cannot_exceed_the_position_cap_at_any_edge():
    """Half Kelly — the most the policy validator allows — at a 40-point edge
    on a 0.50 contract is 20% of bankroll, ten times the 2% cap.

    No probability, however confident, may move that cap.
    """
    limits = RiskLimits()
    for probability in (0.55, 0.70, 0.90, 0.99, 1.0):
        decision = size_position(
            probability, conditions(), portfolio(),
            limits=limits, policy=SizingPolicy(kelly_fraction=0.5),
        )
        assert decision.size_fraction <= limits.max_position_fraction + 1e-12, (
            f"probability {probability} breached the position cap"
        )


def test_a_fixed_fraction_above_the_cap_is_also_clamped():
    """The caps are applied after the method, whichever method it is — so a
    misconfigured fixed fraction cannot route around them either."""
    limits = RiskLimits(max_position_fraction=0.02)
    decision = size_position(
        0.70, conditions(), portfolio(), limits=limits,
        policy=SizingPolicy(method=SIZING_FIXED_FRACTIONAL, fixed_fraction=0.90),
    )
    assert decision.size_fraction == pytest.approx(0.02)
    assert decision.binding_constraint == "max_position_fraction"
    assert decision.uncapped_fraction == pytest.approx(0.90), (
        "the decision must record what was proposed before the cap bit"
    )


def test_a_policy_that_is_not_a_research_setting_is_rejected():
    with pytest.raises(ValueError, match="not a research setting"):
        SizingPolicy(kelly_fraction=0.9).validate()


def test_limits_that_let_one_position_breach_the_book_are_rejected():
    with pytest.raises(ValueError, match="max_portfolio_fraction"):
        RiskLimits(max_position_fraction=0.30, max_portfolio_fraction=0.20).validate()


# --- liquidity ---------------------------------------------------------------

def test_thin_depth_binds_before_the_position_cap():
    """A theoretical edge is worthless if it cannot be filled.

    5,000 of depth at a 10% participation ceiling allows 500 — a quarter of the
    2,000 the position cap would otherwise permit, and still above the minimum
    worth taking.
    """
    decision = size_position(
        0.70, conditions(available_depth=5_000.0), portfolio(),
    )
    assert decision.binding_constraint == "depth_ceiling"
    assert decision.size == pytest.approx(500.0)


def test_depth_too_thin_to_clear_the_minimum_refuses():
    """The floor and the depth ceiling meet here. 1,000 of depth allows 100 on a
    100,000 bankroll — a tenth of a percent, which pays fees to express no view.

    This is the case the floor was added for: the first version of this module
    happily returned that position."""
    decision = size_position(
        0.70, conditions(available_depth=1_000.0), portfolio(),
    )
    assert decision.outcome == SIZE_REFUSED
    assert "remaining_room_below_minimum_position" in decision.refusals
    assert decision.binding_constraint == "depth_ceiling", (
        "the refusal must still name what bound it, so a reader knows it was "
        "liquidity rather than the book"
    )


def test_a_floor_above_the_cap_is_rejected():
    with pytest.raises(ValueError, match="nothing would ever be sizeable"):
        RiskLimits(min_position_fraction=0.05, max_position_fraction=0.02).validate()


def test_a_book_with_no_room_left_refuses_rather_than_trading_small():
    """Exactly at a cap there is nothing left to allocate, and a token position
    is worse than none: it pays fees to express no view."""
    decision = size_position(
        0.70, conditions(),
        portfolio(category_exposure=7_999.0),
    )
    assert decision.outcome == SIZE_REFUSED
    assert "remaining_room_below_minimum_position" in decision.refusals


# --- the arithmetic ----------------------------------------------------------

def test_kelly_for_a_binary_contract():
    """(p - price) / (1 - price). At p=0.70 and price=0.50 that is 0.40."""
    assert kelly_fraction(0.70, 0.50) == pytest.approx(0.40)
    assert kelly_fraction(0.60, 0.40) == pytest.approx(1.0 / 3.0)


@pytest.mark.parametrize("probability,price", [(0.50, 0.50), (0.30, 0.50), (0.0, 0.50)])
def test_kelly_is_zero_without_an_edge(probability, price):
    assert kelly_fraction(probability, price) == 0.0


def test_a_refusal_carries_no_size_and_a_reason():
    decision = size_position(0.70, conditions(), portfolio(kill_switch_engaged=True))
    assert decision.size == 0.0
    assert decision.refusals
    decision.validate()


def test_the_decision_serialises_for_the_ledger():
    payload = size_position(0.70, conditions(), portfolio()).as_dict()
    assert payload["schema_version"] == "polymarket-risk-sizing.v1"
    assert payload["outcome"] == SIZE_APPROVED
    assert "uncapped_fraction" in payload
    assert isinstance(payload["refusals"], list)


def test_the_module_has_no_route_to_execution():
    """Its output is a number in a report. Nothing here builds an order, reaches
    a venue, or touches a credential, and a grep is how that stays true."""
    import pathlib
    source = (pathlib.Path(__file__).resolve().parents[2]
              / "03_IMPLEMENTATION" / "packages" / "polymarket"
              / "risk_sizing.py").read_text(encoding="utf-8")
    for forbidden in ("requests", "httpx", "urllib", "socket", "private_key",
                      "api_key", "sign(", "submit_order", "place_order"):
        assert forbidden not in source, f"{forbidden} has no business in a sizing module"
