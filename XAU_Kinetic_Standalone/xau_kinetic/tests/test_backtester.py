"""
Unit tests for Historical Backtester Engine.
"""

import unittest
from datetime import datetime, timezone
import pandas as pd

from xau_kinetic.backtest.backtester import Backtester, BacktestResult
from xau_kinetic.risk.risk_manager import RiskManager
from xau_kinetic.strategies.xau_kinetic_v2 import XAUKineticV2Strategy


class TestBacktester(unittest.TestCase):
    def setUp(self):
        self.strategy = XAUKineticV2Strategy(symbol="XAUUSD")
        self.risk_manager = RiskManager(
            max_daily_drawdown_pct=5.0,
            max_symbol_exposure_lots=2.0,
            max_risk_per_trade_pct=1.0,
        )
        self.backtester = Backtester(
            strategy=self.strategy,
            risk_manager=self.risk_manager,
            initial_balance=10000.0,
            spread_points=20.0,
            commission_per_lot=5.0,
            slippage_points=2.0,
        )

    def test_backtest_execution_with_synthetic_data(self):
        # Generate 250 synthetic 15-minute bars
        now = datetime.now(timezone.utc)
        dates = pd.date_range(end=now, periods=250, freq="15min", tz=timezone.utc)

        # Create a wave pattern to trigger EMA crossovers
        closes = [2600.0 + (10.0 * (i % 20 > 10)) for i in range(250)]
        df = pd.DataFrame(
            {
                "time": dates,
                "open": [c - 0.5 for c in closes],
                "high": [c + 2.0 for c in closes],
                "low": [c - 2.0 for c in closes],
                "close": closes,
                "tick_volume": [150 for _ in range(250)],
            }
        )

        result: BacktestResult = self.backtester.run(df, min_bars=50)

        self.assertIsInstance(result, BacktestResult)
        self.assertEqual(result.symbol, "XAUUSD")
        self.assertEqual(result.initial_balance, 10000.0)
        self.assertGreaterEqual(result.max_drawdown_pct, 0.0)
        self.assertIsInstance(result.trade_log, list)


if __name__ == "__main__":
    unittest.main()
