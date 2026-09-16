"""
engine/metrics.py — Metrici financiare deterministe pentru evaluarea strategiilor.
"""

from dataclasses import dataclass
import numpy as np


@dataclass
class PerformanceMetrics:
    total_return: float
    cagr: float
    annualized_vol: float
    sharpe_ratio: float
    max_drawdown: float
    trade_count: int
    exposure_pct: float
    total_cost_drag: float
    gross_sharpe: float
    net_sharpe: float


def compute_metrics(
    net_returns: np.ndarray,
    gross_returns: np.ndarray,
    positions: np.ndarray,
    annualization_factor: float = 252.0
) -> PerformanceMetrics:
    n = len(net_returns)
    if n == 0:
        return PerformanceMetrics(0, 0, 0, 0, 0, 0, 0, 0, 0, 0)

    # Cumulative wealth path
    cum_net = np.cumprod(1.0 + net_returns)
    total_return = float(cum_net[-1] - 1.0) if n > 0 else 0.0

    # CAGR
    years = n / annualization_factor
    cagr = float((cum_net[-1] ** (1.0 / years)) - 1.0) if years > 0 and cum_net[-1] > 0 else -1.0

    # Volatility
    std_net = float(np.std(net_returns))
    ann_vol = std_net * np.sqrt(annualization_factor)

    # Sharpe ratio (R_f = 0)
    mean_net = float(np.mean(net_returns))
    net_sharpe = (mean_net / std_net * np.sqrt(annualization_factor)) if std_net > 1e-12 else 0.0

    # Gross Sharpe
    std_gross = float(np.std(gross_returns))
    mean_gross = float(np.mean(gross_returns))
    gross_sharpe = (mean_gross / std_gross * np.sqrt(annualization_factor)) if std_gross > 1e-12 else 0.0

    cost_drag = gross_sharpe - net_sharpe

    # Max Drawdown
    peak = np.maximum.accumulate(cum_net)
    dd = (cum_net - peak) / peak
    max_dd = float(np.min(dd)) if len(dd) > 0 else 0.0

    # Trade count: number of times position changes
    pos_diff = np.abs(np.diff(positions, prepend=0.0))
    trade_count = int(np.sum(pos_diff > 0))

    # Exposure: fraction of time position is non-zero
    exposure_pct = float(np.mean(positions != 0.0) * 100.0)

    return PerformanceMetrics(
        total_return=round(total_return, 4),
        cagr=round(cagr, 4),
        annualized_vol=round(ann_vol, 4),
        sharpe_ratio=round(net_sharpe, 4),
        max_drawdown=round(max_dd, 4),
        trade_count=trade_count,
        exposure_pct=round(exposure_pct, 2),
        total_cost_drag=round(cost_drag, 4),
        gross_sharpe=round(gross_sharpe, 4),
        net_sharpe=round(net_sharpe, 4),
    )
