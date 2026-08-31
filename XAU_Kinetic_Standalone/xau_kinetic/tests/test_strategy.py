"""
Unit tests for XAU_Kinetic V2 Strategy engine.
"""

import unittest
from datetime import datetime, timezone
import pandas as pd

from xau_kinetic.domain.models import SignalType
from xau_kinetic.strategies.xau_kinetic_v2 import XAUKineticV2Strategy


class TestXAUKineticV2Strategy(unittest.TestCase):
    def setUp(self):
        self.strategy = XAUKineticV2Strategy(symbol="XAUUSD")

    def test_insufficient_data_returns_hold(self):
        df = pd.DataFrame()
        sig = self.strategy.generate_signal(df)
        self.assertEqual(sig.signal_type, SignalType.HOLD)

    def test_synthetic_data_signal_generation(self):
        # Generate 100 synthetic closed bars
        now = datetime.now(timezone.utc)
        dates = pd.date_range(end=now, periods=100, freq="15min", tz=timezone.utc)

        # Create a trending price curve
        closes = [2600.0 + (i * 0.5) for i in range(100)]
        df = pd.DataFrame(
            {
                "time": dates,
                "open": [c - 0.2 for c in closes],
                "high": [c + 0.5 for c in closes],
                "low": [c - 0.5 for c in closes],
                "close": closes,
                "tick_volume": [100 for _ in range(100)],
                "spread": [20 for _ in range(100)],
                "real_volume": [0 for _ in range(100)],
            }
        )

        sig = self.strategy.generate_signal(df)
        self.assertIn(sig.signal_type, [SignalType.BUY, SignalType.SELL, SignalType.HOLD])
        self.assertEqual(sig.symbol, "XAUUSD")


if __name__ == "__main__":
    unittest.main()
