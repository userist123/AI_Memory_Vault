"""Smoke tests. Run with: python -m unittest tests/test_strategies.py"""
from __future__ import annotations

import math
import os
import tempfile
import unittest

import config
from core.execution import build_levels, build_tp_ladder, round_volume
from core.journal import Journal
from core.risk_manager import RiskManager
from data.indicators import atr, rsi
from strategies.factory import StrategyFactory


class _FakeInfo:
    digits = 5
    point = 0.00001
    trade_tick_size = 0.00001
    trade_tick_value = 1.0
    volume_min = 0.01
    volume_max = 100.0
    volume_step = 0.01
    trade_contract_size = 100000.0


class TestLevels(unittest.TestCase):
    def test_buy_direction(self):
        sl, tp, _ = build_levels("BUY", 1.10000, atr=0.0010, digits=5)
        self.assertLess(sl, 1.10000); self.assertGreater(tp, 1.10000)

    def test_sell_direction(self):
        sl, tp, _ = build_levels("SELL", 1.10000, atr=0.0010, digits=5)
        self.assertGreater(sl, 1.10000); self.assertLess(tp, 1.10000)


class TestLadder(unittest.TestCase):
    def test_buy_ladder_strictly_increasing(self):
        ladder = build_tp_ladder("BUY", 1.10000, 0.0010, 5)
        self.assertEqual(len(ladder), config.TP_LEVELS)
        self.assertTrue(all(ladder[i] < ladder[i + 1]
                            for i in range(len(ladder) - 1)))

    def test_sell_ladder_strictly_decreasing(self):
        ladder = build_tp_ladder("SELL", 1.10000, 0.0010, 5)
        self.assertTrue(all(ladder[i] > ladder[i + 1]
                            for i in range(len(ladder) - 1)))

    def test_volume_rounding(self):
        self.assertEqual(round_volume(0.137, _FakeInfo), 0.14)
        self.assertEqual(round_volume(0.001, _FakeInfo), _FakeInfo.volume_min)

    def test_tp_config_valid(self):
        self.assertEqual(config.validate_tp_config(), [])


class TestJournal(unittest.TestCase):
    def test_open_partial_close(self):
        with tempfile.TemporaryDirectory() as d:
            j = Journal(path=os.path.join(d, "j.sqlite"))
            j.open_trade(1, symbol="EURUSD", strategy_id="t",
                         side="BUY", entry_price=1.1, sl_price=1.099,
                         tp_plan=[1.101, 1.102, 1.103],
                         tp_fractions=[0.4, 0.3, 0.3],
                         initial_volume=1.0, sl_dist=0.001,
                         ml_prob_win=0.6, spread_entry=1.0, paper=True)
            j.record_partial(1, 0, 1.101, 0.4, 0.0)
            j.close_trade(1, exit_reason="TP_LADDER",
                          exit_price=1.103, gross_pnl=42.0)
            rows = j.query_recent_days(1)
            self.assertEqual(len(rows), 1)
            self.assertEqual(rows[0]["tp_hits"][0], True)
            self.assertAlmostEqual(rows[0]["gross_pnl"], 42.0)
            self.assertGreater(rows[0]["r_multiple"], 0)


class TestFactory(unittest.TestCase):
    def test_min_300_instances(self):
        f = StrategyFactory(); f.build_all()
        self.assertGreaterEqual(len(f.instances), 300)
        ids = [s.id for s in f.instances]
        self.assertEqual(len(ids), len(set(ids)))


class TestIndicators(unittest.TestCase):
    def test_rsi_all_up(self):
        self.assertEqual(rsi(list(range(1, 30)), 14), 100.0)
    def test_atr_insufficient(self):
        self.assertIsNone(atr([2]*5, [1]*5, [1.5]*5, 14))


class TestLotSizing(unittest.TestCase):
    def test_lot_within_bounds(self):
        rm = RiskManager()
        lot = rm.calc_lot(balance=10_000.0, sl_distance=0.0020, info=_FakeInfo())
        self.assertGreaterEqual(lot, _FakeInfo.volume_min)
        steps = lot / _FakeInfo.volume_step
        self.assertTrue(math.isclose(steps, round(steps), abs_tol=1e-6))


if __name__ == "__main__":
    unittest.main()
