"""
Unit tests for AppConfig configuration system.
"""

import tempfile
import unittest
from pathlib import Path
from pydantic import ValidationError

from xau_kinetic.config import AppConfig, RiskConfig, StrategyConfig


class TestAppConfig(unittest.TestCase):
    def test_default_config_valid(self):
        cfg = AppConfig()
        self.assertEqual(cfg.symbol, "XAUUSD")
        self.assertEqual(cfg.timeframe, "M15")
        self.assertEqual(cfg.risk.max_daily_drawdown_pct, 3.0)

    def test_invalid_risk_drawdown(self):
        with self.assertRaises(ValidationError):
            RiskConfig(max_daily_drawdown_pct=60.0)  # > 50.0 limit

    def test_load_from_file(self):
        with tempfile.NamedTemporaryFile("w+", delete=False, suffix=".json") as f:
            f.write('{"symbol": "EURUSD", "timeframe": "H1", "risk": {"max_daily_drawdown_pct": 2.5}}')
            temp_path = f.name

        try:
            cfg = AppConfig.load_from_file(temp_path)
            self.assertEqual(cfg.symbol, "EURUSD")
            self.assertEqual(cfg.timeframe, "H1")
            self.assertEqual(cfg.risk.max_daily_drawdown_pct, 2.5)
        finally:
            Path(temp_path).unlink(missing_ok=True)


if __name__ == "__main__":
    unittest.main()
