"""
Unit tests for Risk Manager and Circuit Breaker engine.
"""

import unittest
from xau_kinetic.domain.models import SignalObject, SignalType, AccountInfo, Position
from xau_kinetic.risk.risk_manager import RiskManager


class TestRiskManager(unittest.TestCase):
    def setUp(self):
        self.risk = RiskManager(
            max_daily_drawdown_pct=3.0,
            max_symbol_exposure_lots=1.0,
            max_risk_per_trade_pct=1.0,
            min_free_margin_usd=500.0,
        )
        self.account = AccountInfo(
            login=12345,
            balance=10000.0,
            equity=10000.0,
            margin=0.0,
            free_margin=10000.0,
        )

    def test_approve_normal_buy_signal(self):
        sig = SignalObject(
            symbol="XAUUSD",
            signal_type=SignalType.BUY,
            target_price=2650.0,
            stop_loss=2640.0,
            take_profit=2670.0,
            volume=0.1,
        )
        approved, adj_sig = self.risk.evaluate_signal(sig, self.account, [])
        self.assertTrue(approved)
        self.assertGreater(adj_sig.volume, 0.0)

    def test_circuit_breaker_drawdown_trigger(self):
        # Establish initial baseline
        self.risk.is_circuit_broken(self.account)

        # Equity drops by 500 (5% drawdown, exceeding 3% max)
        bad_account = AccountInfo(
            login=12345,
            balance=10000.0,
            equity=9400.0,  # 6% loss
            margin=0.0,
            free_margin=9400.0,
        )

        sig = SignalObject(
            symbol="XAUUSD",
            signal_type=SignalType.BUY,
            target_price=2650.0,
            stop_loss=2640.0,
            volume=0.1,
        )
        approved, _ = self.risk.evaluate_signal(sig, bad_account, [])
        self.assertFalse(approved)
        self.assertTrue(self.risk.is_circuit_broken(bad_account))

    def test_exposure_limit_veto(self):
        existing_pos = Position(
            ticket=1,
            symbol="XAUUSD",
            type=SignalType.BUY,
            volume=1.0,  # max_symbol_exposure_lots is 1.0
            open_price=2645.0,
        )
        sig = SignalObject(
            symbol="XAUUSD",
            signal_type=SignalType.BUY,
            target_price=2650.0,
            stop_loss=2640.0,
            volume=0.1,
        )
        approved, _ = self.risk.evaluate_signal(sig, self.account, [existing_pos])
        self.assertFalse(approved)


if __name__ == "__main__":
    unittest.main()
