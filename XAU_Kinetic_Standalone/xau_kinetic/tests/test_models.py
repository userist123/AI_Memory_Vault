"""
Unit tests for Pydantic V2 domain models.
"""

import unittest
from datetime import datetime, timezone
from pydantic import ValidationError

from xau_kinetic.domain.models import (
    TickData,
    SignalObject,
    SignalType,
    AccountInfo,
    Position,
    OrderResult,
    AuditEvent,
)


class TestDomainModels(unittest.TestCase):
    def test_tick_data_valid(self):
        tick = TickData(
            symbol="XAUUSD",
            bid=2650.00,
            ask=2650.50,
            last=2650.25,
            volume=1.5,
        )
        self.assertEqual(tick.symbol, "XAUUSD")
        self.assertEqual(tick.bid, 2650.00)
        self.assertEqual(tick.ask, 2650.50)

    def test_tick_data_invalid_spread(self):
        with self.assertRaises(ValidationError):
            TickData(symbol="XAUUSD", bid=2650.50, ask=2649.00)

    def test_signal_object(self):
        sig = SignalObject(
            symbol="XAUUSD",
            signal_type=SignalType.BUY,
            confidence=0.85,
            target_price=2650.0,
            stop_loss=2640.0,
            take_profit=2670.0,
            volume=0.2,
        )
        self.assertEqual(sig.signal_type, SignalType.BUY)
        self.assertEqual(sig.volume, 0.2)

    def test_account_info(self):
        acc = AccountInfo(
            login=1001,
            trade_mode=0,
            leverage=100,
            balance=10000.0,
            equity=10000.0,
            margin=0.0,
            free_margin=10000.0,
        )
        self.assertEqual(acc.login, 1001)
        self.assertEqual(acc.equity, 10000.0)

    def test_audit_event_immutability(self):
        event = AuditEvent(
            event_id="test-123",
            event_type="TEST",
            payload={"key": "val"},
            prev_hash="0" * 64,
            current_hash="1" * 64,
        )
        with self.assertRaises(ValidationError):
            event.event_type = "MUTATED"


if __name__ == "__main__":
    unittest.main()
