"""
Unit tests for Multi-TP Ladder Manager and Break-Even Trailing Stop Engine.
"""

import unittest
from xau_kinetic.domain.models import SignalType, Position
from xau_kinetic.risk.tp_ladder import TPLadderManager, TPLadderTarget


class TestTPLadderManager(unittest.TestCase):
    def setUp(self):
        self.ladder = TPLadderManager(
            tp1_atr_mult=1.0,
            tp1_vol_frac=0.5,
            tp2_atr_mult=2.0,
            tp2_vol_frac=0.3,
            tp3_atr_mult=3.5,
            tp3_vol_frac=0.2,
            break_even_atr_trigger=1.0,
            break_even_buffer_points=10.0,  # $0.10
        )

    def test_calculate_ladder_targets_buy(self):
        targets = self.ladder.calculate_ladder_targets(
            entry_price=2650.0,
            signal_type=SignalType.BUY,
            atr_value=10.0,
        )
        self.assertEqual(len(targets), 3)

        self.assertEqual(targets[0].target_price, 2660.0)  # 2650 + 1.0*10
        self.assertEqual(targets[0].volume_fraction, 0.5)

        self.assertEqual(targets[1].target_price, 2670.0)  # 2650 + 2.0*10
        self.assertEqual(targets[1].volume_fraction, 0.3)

        self.assertEqual(targets[2].target_price, 2685.0)  # 2650 + 3.5*10
        self.assertEqual(targets[2].volume_fraction, 0.2)

    def test_calculate_ladder_targets_sell(self):
        targets = self.ladder.calculate_ladder_targets(
            entry_price=2650.0,
            signal_type=SignalType.SELL,
            atr_value=10.0,
        )
        self.assertEqual(len(targets), 3)
        self.assertEqual(targets[0].target_price, 2640.0)  # 2650 - 1.0*10
        self.assertEqual(targets[1].target_price, 2630.0)  # 2650 - 2.0*10

    def test_evaluate_break_even_buy_triggered(self):
        pos = Position(
            ticket=1001,
            symbol="XAUUSD",
            type=SignalType.BUY,
            volume=1.0,
            open_price=2650.0,
            sl=2640.0,  # initial SL below entry
        )
        # Price moves up to 2662 (+12 >= 1.0*10 trigger)
        new_sl = self.ladder.evaluate_break_even(pos, current_price=2662.0, atr_value=10.0)
        self.assertIsNotNone(new_sl)
        self.assertEqual(new_sl, 2650.10)  # entry + 0.10 buffer

    def test_evaluate_break_even_buy_not_triggered(self):
        pos = Position(
            ticket=1001,
            symbol="XAUUSD",
            type=SignalType.BUY,
            volume=1.0,
            open_price=2650.0,
            sl=2640.0,
        )
        # Price moves up only to 2655 (+5 < 1.0*10 trigger)
        new_sl = self.ladder.evaluate_break_even(pos, current_price=2655.0, atr_value=10.0)
        self.assertIsNone(new_sl)


if __name__ == "__main__":
    unittest.main()
