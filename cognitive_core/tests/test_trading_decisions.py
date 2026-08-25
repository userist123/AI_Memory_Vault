from cognitive_core.trading_decisions import TradingDecision, TradingDecisionLogger


def test_to_candidate_builds_raw_decision_with_trading_tags():
    logger = TradingDecisionLogger()
    decision = TradingDecision(
        symbol="BTCUSD", strategy="mean_reversion", action="long",
        rationale="RSI oversold on 4h timeframe", backtest_ref="bt-2026-08-25",
    )
    candidate = logger.to_candidate(decision, source_ref="trading_bot:session1")
    assert candidate.type == "decision"
    assert candidate.category == "trading"
    assert candidate.lifecycle == "RAW"
    assert "btcusd" in candidate.tags and "mean_reversion" in candidate.tags
    assert "bt-2026-08-25" in candidate.content


def test_batch_converts_multiple_decisions():
    logger = TradingDecisionLogger()
    decisions = [
        TradingDecision(symbol="EURUSD", strategy="breakout", action="short", rationale="resistance rejection"),
        TradingDecision(symbol="BTCUSD", strategy="trend", action="long", rationale="MA crossover"),
    ]
    candidates = logger.batch(decisions, source_ref="trading_bot:session2")
    assert len(candidates) == 2
    assert all(c.category == "trading" for c in candidates)
