"""
engine/backtest.py — Motor de simulare vectorizat, strict anti-lookahead.

Regulă structurală fundamentală:
Position[t+1] = Signal[t]
Execuția se realizează la Open[t+1].
Randamentul barei t+1 este (Open[t+2] - Open[t+1]) / Open[t+1].
Costul de tranzacție se aplică la Open[t+1] pentru |Position[t+1] - Position[t]|.
Costul de swap se aplică la Close[t+1] pentru Position[t+1] peste noapte.
"""

from dataclasses import dataclass
from datetime import datetime
from typing import List
import numpy as np

from .costs import CostModel
from .metrics import PerformanceMetrics, compute_metrics


@dataclass
class BacktestResult:
    net_returns: np.ndarray
    gross_returns: np.ndarray
    positions: np.ndarray
    metrics: PerformanceMetrics


def run_backtest(
    opens: np.ndarray,
    highs: np.ndarray,
    lows: np.ndarray,
    closes: np.ndarray,
    timestamps: List[datetime],
    signals: np.ndarray,
    cost_model: CostModel,
) -> BacktestResult:
    """
    Rulează backtestul pe date zilnice D1.
    """
    n = len(closes)
    if n < 3:
        raise ValueError(f"Insufficient bars for backtest: {n}")
    if len(signals) != n:
        raise ValueError(f"Signals length ({len(signals)}) != bars length ({n})")

    # STRICT ANTI-LOOKAHEAD ALIGNMENT:
    # Position la pasul t este Signal[t-1]
    positions = np.zeros(n, dtype=np.float64)
    positions[1:] = signals[:-1]  # Shift cu 1 pas la dreapta

    # Calcul randamente brute de piață Open-to-Open
    # Pe bara t (executată la Open[t]), menținută până la Open[t+1]
    # Pentru ultima bară n-1, folosim Close[n-1] ca preț final de decontare
    open_returns = np.zeros(n, dtype=np.float64)
    open_returns[:-1] = (opens[1:] - opens[:-1]) / opens[:-1]
    open_returns[-1] = (closes[-1] - opens[-1]) / opens[-1]

    # Gross return
    gross_returns = positions * open_returns

    # Costuri
    trade_costs = np.zeros(n, dtype=np.float64)
    swap_costs = np.zeros(n, dtype=np.float64)

    # Schimbări de poziție
    delta_pos = np.abs(np.diff(positions, prepend=0.0))

    for t in range(n):
        if delta_pos[t] > 0.0:
            trade_costs[t] = cost_model.calculate_trade_cost(opens[t], delta_pos[t])

        if positions[t] != 0.0:
            wd = timestamps[t].weekday()
            swap_costs[t] = cost_model.calculate_overnight_swap_cost(positions[t], closes[t], wd)

    # Net returns = Gross - Trade Costs - Swap Costs
    net_returns = gross_returns - trade_costs - swap_costs

    metrics = compute_metrics(net_returns, gross_returns, positions)

    return BacktestResult(
        net_returns=net_returns,
        gross_returns=gross_returns,
        positions=positions,
        metrics=metrics,
    )
