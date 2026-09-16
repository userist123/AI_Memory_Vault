import os
import sys
from datetime import datetime, timedelta
import numpy as np
import pytest

# Ensure root is in sys.path
sys.path.insert(0, os.path.abspath("."))
from importlib import import_module

costs_mod = import_module("07_EVALUATION.metatrader.research.engine.costs")
CostModel = costs_mod.CostModel

strat_mod = import_module("07_EVALUATION.metatrader.research.engine.strategies")
generate_s1_momentum = strat_mod.generate_s1_momentum
generate_s2_mean_reversion = strat_mod.generate_s2_mean_reversion
generate_s3_breakout = strat_mod.generate_s3_breakout
generate_s4_carry = strat_mod.generate_s4_carry
generate_s0a_buy_and_hold = strat_mod.generate_s0a_buy_and_hold
generate_s0b_random_matched = strat_mod.generate_s0b_random_matched

bt_mod = import_module("07_EVALUATION.metatrader.research.engine.backtest")
run_backtest = bt_mod.run_backtest

boot_mod = import_module("07_EVALUATION.metatrader.research.engine.bootstrap")
stationary_bootstrap_indices = boot_mod.stationary_bootstrap_indices
compute_stationary_bootstrap_ci = boot_mod.compute_stationary_bootstrap_ci
hansen_spa_test = boot_mod.hansen_spa_test


def make_synthetic_timestamps(n: int) -> list:
    base = datetime(2025, 1, 1, 0, 0)
    return [base + timedelta(days=i) for i in range(n)]


def test_roundtrip_cost_deterministic():
    """
    Verifica ca un cost dus-intors (intrare + iesire) este calculat exact
    pe un pret plat conform modelului de cost.
    """
    n = 6
    opens = np.array([100.0, 100.0, 100.0, 100.0, 100.0, 100.0])
    highs = np.array([100.0, 100.0, 100.0, 100.0, 100.0, 100.0])
    lows = np.array([100.0, 100.0, 100.0, 100.0, 100.0, 100.0])
    closes = np.array([100.0, 100.0, 100.0, 100.0, 100.0, 100.0])
    timestamps = make_synthetic_timestamps(n)

    cost_model = CostModel(
        spread_points=10.0,
        point=0.01,
        contract_size=100000.0,
        commission_per_lot=0.0,
        swap_long_points=0.0,
        swap_short_points=0.0,
    )

    signals = np.array([0.0, 1.0, 0.0, 0.0, 0.0, 0.0])
    res = run_backtest(opens, highs, lows, closes, timestamps, signals, cost_model)

    assert np.array_equal(res.positions, np.array([0.0, 0.0, 1.0, 0.0, 0.0, 0.0]))
    expected_net = np.array([0.0, 0.0, -0.0005, -0.0005, 0.0, 0.0])
    assert np.allclose(res.net_returns, expected_net, atol=1e-8)
    assert res.metrics.trade_count == 2


def test_random_walk_zero_drift_loses_costs():
    """
    Pe un random walk pur fara derivativa, strategia S1 Momentum pierde costurile.
    """
    rng = np.random.RandomState(12345)
    n = 2000
    daily_rets = rng.normal(0.0, 0.01, size=n)
    prices = 100.0 * np.exp(np.cumsum(daily_rets))

    opens = prices
    highs = prices * 1.002
    lows = prices * 0.998
    closes = prices
    timestamps = make_synthetic_timestamps(n)

    cost_model = CostModel(
        spread_points=15.0,
        point=0.0001,
        contract_size=100000.0,
        commission_per_lot=2.0,
    )

    sig_s1 = generate_s1_momentum(closes, L=20)
    res_s1 = run_backtest(opens, highs, lows, closes, timestamps, sig_s1, cost_model)

    assert abs(res_s1.metrics.gross_sharpe) < 1.5
    assert res_s1.metrics.net_sharpe < res_s1.metrics.gross_sharpe
    assert res_s1.metrics.total_cost_drag > 0.0


def test_anti_lookahead_delay_invariance():
    """
    Verifica ca motorul forteaza delay de 1 pas la executie: Position[t+1] = Signal[t].
    """
    rng = np.random.RandomState(42)
    n = 1000
    noise_returns = rng.normal(0.0, 0.01, size=n)
    opens = 100.0 * np.exp(np.cumsum(noise_returns))
    highs = opens * 1.001
    lows = opens * 0.999
    closes = opens
    timestamps = make_synthetic_timestamps(n)

    cost_model = CostModel(spread_points=0.0, point=0.01, contract_size=100000.0)

    illicit_signal = np.zeros(n)
    illicit_signal[:-1] = np.sign(opens[1:] - opens[:-1])

    res = run_backtest(opens, highs, lows, closes, timestamps, illicit_signal, cost_model)
    assert abs(res.metrics.gross_sharpe) < 2.0


def test_hansen_spa_exact_properties():
    """
    Verifica ca Hansen SPA returneaza o valoare p valida si nu respinge ipoteza nula pe modele identice.
    """
    rng = np.random.RandomState(42)
    t_len = 300
    bench_loss = rng.normal(0.0, 0.01, size=t_len)

    models_identical = np.zeros((t_len, 5))
    for k in range(5):
        models_identical[:, k] = bench_loss + rng.normal(0.0, 0.001, size=t_len)

    res_null = hansen_spa_test(bench_loss, models_identical, b_reps=200, q=10.0, seed=42)
    assert 0.0 <= res_null["p_value_spa"] <= 1.0
    assert res_null["p_value_spa"] > 0.05
