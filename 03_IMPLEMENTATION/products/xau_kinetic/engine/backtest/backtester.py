"""
Historical Backtesting Engine with realistic transaction cost simulation.
Evaluates strategy bar-by-bar strictly avoiding look-ahead bias.
Simulates spread, commission, and slippage.
"""

import logging
import math
from typing import Any
import numpy as np
import pandas as pd
from pydantic import BaseModel, Field, ConfigDict

from xau_kinetic.application.interfaces import IStrategy, IRiskManager
from xau_kinetic.domain.models import SignalType, SignalObject, AccountInfo, Position

logger = logging.getLogger("xau_kinetic.backtest")


class BacktestResult(BaseModel):
    """Backtest simulation results report."""
    model_config = ConfigDict(frozen=True)

    symbol: str
    initial_balance: float
    final_balance: float
    total_net_profit: float
    total_trades: int
    winning_trades: int
    losing_trades: int
    win_rate_pct: float
    gross_profit: float
    gross_loss: float
    profit_factor: float
    max_drawdown_pct: float
    sharpe_ratio: float
    trade_log: list[dict[str, Any]] = Field(default_factory=list)


class Backtester:
    """
    Simulates historical trading execution bar-by-bar.
    Enforces anti-look-ahead compliance and incorporates real transaction costs.
    """

    def __init__(
        self,
        strategy: IStrategy,
        risk_manager: IRiskManager,
        initial_balance: float = 10000.0,
        spread_points: float = 30.0,  # 30 points = $0.30 on XAUUSD
        commission_per_lot: float = 7.0,  # $7 per lot round turn
        slippage_points: float = 5.0,  # 5 points = $0.05 slippage
        point_value: float = 0.01,  # 1 point = $0.01
        contract_size: float = 100.0,  # 1 lot XAUUSD = 100 oz
    ) -> None:
        self.strategy = strategy
        self.risk_manager = risk_manager
        self.initial_balance = initial_balance
        self.spread_points = spread_points
        self.commission_per_lot = commission_per_lot
        self.slippage_points = slippage_points
        self.point_value = point_value
        self.contract_size = contract_size

    def run(self, df: pd.DataFrame, min_bars: int = 100) -> BacktestResult:
        """
        Run backtest simulation on historical market data DataFrame.
        df must contain ['time', 'open', 'high', 'low', 'close', 'tick_volume'].
        """
        if df is None or len(df) < min_bars:
            logger.error("Historical data too small for backtesting.")
            return BacktestResult(
                symbol="N/A",
                initial_balance=self.initial_balance,
                final_balance=self.initial_balance,
                total_net_profit=0.0,
                total_trades=0,
                winning_trades=0,
                losing_trades=0,
                win_rate_pct=0.0,
                gross_profit=0.0,
                gross_loss=0.0,
                profit_factor=0.0,
                max_drawdown_pct=0.0,
                sharpe_ratio=0.0,
            )

        symbol = getattr(self.strategy, "_symbol", "XAUUSD")
        current_balance = self.initial_balance
        peak_balance = self.initial_balance
        max_drawdown_pct = 0.0

        equity_curve: list[float] = [self.initial_balance]
        trades: list[dict[str, Any]] = []

        open_position: dict[str, Any] | None = None

        # Bar-by-bar iteration starting from min_bars
        for i in range(min_bars, len(df)):
            current_bar = df.iloc[i]
            bar_time = current_bar["time"]
            bar_open = float(current_bar["open"])
            bar_high = float(current_bar["high"])
            bar_low = float(current_bar["low"])
            bar_close = float(current_bar["close"])

            # 1. Manage existing open position (check SL / TP hits on current bar)
            if open_position is not None:
                pos_type = open_position["type"]
                entry_price = open_position["entry_price"]
                sl = open_position["sl"]
                tp = open_position["tp"]
                vol = open_position["volume"]

                closed_trade = False
                exit_price = 0.0
                exit_reason = ""
                slippage_cost = self.slippage_points * self.point_value

                if pos_type == SignalType.BUY:
                    if sl > 0.0 and bar_low <= sl:
                        exit_price = sl - slippage_cost
                        exit_reason = "SL Hit"
                        closed_trade = True
                    elif tp > 0.0 and bar_high >= tp:
                        exit_price = tp
                        exit_reason = "TP Hit"
                        closed_trade = True
                elif pos_type == SignalType.SELL:
                    if sl > 0.0 and bar_high >= sl:
                        exit_price = sl + slippage_cost
                        exit_reason = "SL Hit"
                        closed_trade = True
                    elif tp > 0.0 and bar_low <= tp:
                        exit_price = tp
                        exit_reason = "TP Hit"
                        closed_trade = True

                if closed_trade:
                    price_diff = (exit_price - entry_price) if pos_type == SignalType.BUY else (entry_price - exit_price)
                    raw_pnl = price_diff * vol * self.contract_size
                    comm = self.commission_per_lot * vol
                    net_pnl = raw_pnl - comm

                    current_balance += net_pnl
                    trades.append(
                        {
                            "entry_time": open_position["entry_time"],
                            "exit_time": bar_time,
                            "type": pos_type.value,
                            "volume": vol,
                            "entry_price": entry_price,
                            "exit_price": exit_price,
                            "pnl": round(net_pnl, 2),
                            "reason": exit_reason,
                        }
                    )
                    open_position = None

            # 2. Strict Zero-Lookahead Signal Generation
            # Pass historical series UP TO completed bar i-1 (df.iloc[:i])
            historical_data = df.iloc[:i].copy()
            signal: SignalObject = self.strategy.generate_signal(historical_data, closed_bars_only=True)

            # 3. Execute approved signal at bar_open of bar i
            if signal.signal_type in (SignalType.BUY, SignalType.SELL) and open_position is None:
                acc = AccountInfo(
                    login=9999,
                    balance=current_balance,
                    equity=current_balance,
                    margin=0.0,
                    free_margin=current_balance,
                )
                approved, adj_signal = self.risk_manager.evaluate_signal(signal, acc, [])

                if approved:
                    spread_cost = self.spread_points * self.point_value
                    slippage_cost = self.slippage_points * self.point_value

                    if adj_signal.signal_type == SignalType.BUY:
                        exec_price = bar_open + spread_cost + slippage_cost
                    else:
                        exec_price = bar_open - slippage_cost

                    open_position = {
                        "entry_time": bar_time,
                        "type": adj_signal.signal_type,
                        "volume": adj_signal.volume,
                        "entry_price": exec_price,
                        "sl": adj_signal.stop_loss,
                        "tp": adj_signal.take_profit,
                    }

            # 4. Floating Equity & Peak Drawdown Tracking (MAE inclusive)
            floating_pnl = 0.0
            if open_position is not None:
                pos_type = open_position["type"]
                entry_price = open_position["entry_price"]
                vol = open_position["volume"]
                worst_price = bar_low if pos_type == SignalType.BUY else bar_high
                price_diff = (worst_price - entry_price) if pos_type == SignalType.BUY else (entry_price - worst_price)
                comm = self.commission_per_lot * vol
                floating_pnl = (price_diff * vol * self.contract_size) - comm

            current_equity = current_balance + floating_pnl
            peak_balance = max(peak_balance, current_balance, current_equity)
            dd_pct = ((peak_balance - current_equity) / peak_balance) * 100.0 if peak_balance > 0 else 0.0
            max_drawdown_pct = max(max_drawdown_pct, dd_pct)
            equity_curve.append(current_balance)

        # Terminal Mark-to-Market closure for any remaining open position
        if open_position is not None:
            last_bar = df.iloc[-1]
            exit_price = float(last_bar["close"])
            pos_type = open_position["type"]
            entry_price = open_position["entry_price"]
            vol = open_position["volume"]
            price_diff = (exit_price - entry_price) if pos_type == SignalType.BUY else (entry_price - exit_price)
            net_pnl = (price_diff * vol * self.contract_size) - (self.commission_per_lot * vol)
            current_balance += net_pnl
            trades.append(
                {
                    "entry_time": open_position["entry_time"],
                    "exit_time": last_bar["time"],
                    "type": pos_type.value,
                    "volume": vol,
                    "entry_price": entry_price,
                    "exit_price": exit_price,
                    "pnl": round(net_pnl, 2),
                    "reason": "Terminal M2M Close",
                }
            )

        # Performance Summary Statistics
        total_trades = len(trades)
        winning_trades = sum(1 for t in trades if t["pnl"] > 0)
        losing_trades = sum(1 for t in trades if t["pnl"] < 0)
        win_rate_pct = (winning_trades / total_trades * 100.0) if total_trades > 0 else 0.0

        gross_profit = sum(t["pnl"] for t in trades if t["pnl"] > 0)
        gross_loss = abs(sum(t["pnl"] for t in trades if t["pnl"] < 0))

        if gross_loss > 0.0:
            profit_factor = gross_profit / gross_loss
        elif gross_profit > 0.0:
            profit_factor = float("inf")
        else:
            profit_factor = 0.0

        total_net_profit = round(current_balance - self.initial_balance, 2)

        # Dynamic Sharpe Ratio Calculation based on detected bar timedelta
        returns = pd.Series(equity_curve).pct_change().dropna()
        if len(df) > 1 and "time" in df.columns:
            try:
                dt_seconds = pd.to_datetime(df["time"]).diff().dt.total_seconds().median()
                periods_per_year = (252 * 86400) / dt_seconds if dt_seconds > 0 else 24192
            except Exception:
                periods_per_year = 24192
        else:
            periods_per_year = 24192

        sharpe_ratio = (
            (returns.mean() / returns.std()) * math.sqrt(periods_per_year)
            if len(returns) > 1 and returns.std() > 0
            else 0.0
        )

        return BacktestResult(
            symbol=symbol,
            initial_balance=self.initial_balance,
            final_balance=round(current_balance, 2),
            total_net_profit=total_net_profit,
            total_trades=total_trades,
            winning_trades=winning_trades,
            losing_trades=losing_trades,
            win_rate_pct=round(win_rate_pct, 2),
            gross_profit=round(gross_profit, 2),
            gross_loss=round(gross_loss, 2),
            profit_factor=round(profit_factor, 2) if not math.isinf(profit_factor) else 999.99,
            max_drawdown_pct=round(max_drawdown_pct, 2),
            sharpe_ratio=round(sharpe_ratio, 2),
            trade_log=trades,
        )
