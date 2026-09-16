from packages.polymarket.paper_simulation import (
    PaperInstruction,
    PaperQuote,
    PaperRun,
    PortfolioLedger,
    SIDE_BUY,
    STATUS_PARTIAL,
)
from packages.polymarket.risk_sizing import MarketConditions, PortfolioState, SIZE_APPROVED, size_position


def test_risk_size_can_enter_paper_ledger_without_live_adapter():
    portfolio = PortfolioState(bankroll=100_000.0, calibration_samples=500)
    conditions = MarketConditions(
        price=0.50,
        available_depth=10_000.0,
        quote_age_seconds=10.0,
        resolution_risk=0.05,
        net_edge=0.20,
    )
    sizing = size_position(0.70, conditions, portfolio)
    assert sizing.outcome == SIZE_APPROVED
    assert sizing.size > 0

    run = PaperRun(PortfolioLedger(cash=portfolio.bankroll))
    instruction = PaperInstruction(
        market_id="m1",
        outcome_id="yes",
        side=SIDE_BUY,
        submitted_at="2026-03-01T12:00:01Z",
        research_size=sizing.size,
    )
    quote = PaperQuote(
        market_id="m1",
        outcome_id="yes",
        observed_at="2026-03-01T12:00:00Z",
        price=conditions.price,
        available_depth=conditions.available_depth,
        source_ref="hist:m1:yes",
    )
    fill = run.execute_simulation(instruction, quote, fee_bps=100.0)
    assert fill.status in {"FILLED", STATUS_PARTIAL}
    assert run.ledger.cash < portfolio.bankroll
    assert run.ledger.units_for("m1:yes") > 0
