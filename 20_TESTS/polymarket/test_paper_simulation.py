import pytest

from packages.polymarket.paper_simulation import (
    PaperInstruction,
    PaperQuote,
    PaperRun,
    PortfolioLedger,
    SIDE_BUY,
    STATUS_FILLED,
    STATUS_PARTIAL,
    STATUS_REFUSED,
    simulate_fill,
)


def quote(price=0.50, depth=1000.0):
    return PaperQuote(
        market_id="m1",
        outcome_id="yes",
        observed_at="2026-03-01T12:00:00Z",
        price=price,
        available_depth=depth,
        source_ref="hist:m1:yes",
    )


def instruction(size=50.0, slippage=100.0):
    return PaperInstruction(
        market_id="m1",
        outcome_id="yes",
        side=SIDE_BUY,
        submitted_at="2026-03-01T12:00:01Z",
        research_size=size,
        max_slippage_bps=slippage,
    )


def test_full_fill_is_deterministic():
    a = simulate_fill(instruction(50.0), quote())
    b = simulate_fill(instruction(50.0), quote())
    assert a == b
    assert a.status == STATUS_FILLED
    assert a.unfilled_notional == pytest.approx(0.0)
    assert a.filled_units > 0


def test_depth_can_force_partial_fill():
    result = simulate_fill(instruction(200.0), quote(depth=1000.0))
    assert result.status == STATUS_PARTIAL
    assert result.filled_notional == pytest.approx(100.0)
    assert result.unfilled_notional == pytest.approx(100.0)


def test_identity_mismatch_refuses_without_state_change():
    wrong = PaperQuote(
        market_id="other", outcome_id="yes", observed_at="2026-03-01T12:00:00Z",
        price=0.50, available_depth=1000.0, source_ref="hist:other",
    )
    result = simulate_fill(instruction(), wrong)
    assert result.status == STATUS_REFUSED
    assert result.filled_notional == 0.0
    assert result.unfilled_notional == pytest.approx(50.0)


def test_zero_depth_refuses():
    bad = PaperQuote(
        market_id="m1", outcome_id="yes", observed_at="2026-03-01T12:00:00Z",
        price=0.50, available_depth=0.0, source_ref="hist:m1:yes",
    )
    with pytest.raises(ValueError):
        bad.validate()


def test_fee_and_slippage_are_explicit():
    result = simulate_fill(
        instruction(50.0, slippage=200.0), quote(price=0.50, depth=1000.0),
        fee_bps=100.0, slippage_bps_per_depth=200.0,
    )
    assert result.fee == pytest.approx(0.5)
    assert result.slippage_bps == pytest.approx(10.0)
    assert result.fill_price > 0.50


def test_portfolio_ledger_applies_buy_fill():
    run = PaperRun(PortfolioLedger(cash=1000.0))
    fill = run.execute_simulation(
        instruction(50.0), quote(), fee_bps=100.0,
    )
    assert fill.status == STATUS_FILLED
    assert run.ledger.cash == pytest.approx(949.5)
    assert run.ledger.fees_paid == pytest.approx(0.5)
    assert run.ledger.units_for("m1:yes") == pytest.approx(fill.filled_units)


def test_refused_fill_does_not_move_portfolio():
    run = PaperRun(PortfolioLedger(cash=1000.0))
    wrong = PaperQuote(
        market_id="other", outcome_id="yes", observed_at="2026-03-01T12:00:00Z",
        price=0.50, available_depth=1000.0, source_ref="hist:other",
    )
    fill = run.execute_simulation(instruction(), wrong)
    assert fill.status == STATUS_REFUSED
    assert run.ledger.cash == pytest.approx(1000.0)
    assert run.ledger.positions == ()
    assert run.ledger.fees_paid == 0.0


def test_invalid_slippage_policy_rejected():
    with pytest.raises(ValueError):
        simulate_fill(instruction(), quote(), slippage_bps_per_depth=-1.0)


def test_no_network_or_credentials_symbols_present():
    import pathlib

    text = pathlib.Path(
        "03_IMPLEMENTATION/packages/polymarket/paper_simulation.py"
    ).read_text(encoding="utf-8")
    lowered = text.lower()
    assert "urllib" not in lowered
    assert "requests" not in lowered
    assert "authorization" not in lowered
    assert "private_key" not in lowered
    assert "wallet" not in lowered
