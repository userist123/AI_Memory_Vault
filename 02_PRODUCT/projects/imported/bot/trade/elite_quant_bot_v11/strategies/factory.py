"""Factory: cartesian product of (family x param grid x timeframe).

Guarantees >= 300 instances. Persists per-instance stats so scores survive
between sessions. Tracks a 3-state health flag per instance:
HEALTHY / UNDERPERFORMING / DISABLED.
"""
from __future__ import annotations

import json
import os
from typing import Callable, Dict, List, Optional, Tuple

import config
from strategies.base import Strategy, StrategyStats
from strategies.families.library import FAMILIES


TIMEFRAMES = ("M5", "M15", "H1")

HEALTHY = "HEALTHY"
UNDERPERFORMING = "UNDERPERFORMING"
DISABLED = "DISABLED"


class StrategyFactory:
    def __init__(self) -> None:
        self.instances: List[Strategy] = []
        self._status: Dict[str, str] = {}
        self._on_status_change: Optional[Callable[[str, str, str], None]] = None

    def set_status_listener(self,
                            cb: Callable[[str, str, str], None]) -> None:
        """Register `cb(strategy_id, old_status, new_status)`."""
        self._on_status_change = cb

    # ------------------------------------------------------------- build
    def build_all(self) -> List[Strategy]:
        self.instances.clear()
        for fam_name, fn, grid in FAMILIES:
            for params in grid:
                for tf in TIMEFRAMES:
                    sid = self._sid(fam_name, params, tf)
                    self.instances.append(
                        Strategy(id=sid, family=fam_name, timeframe=tf,
                                 params=dict(params), fn=fn)
                    )
        extra_tfs = ("H4", "M1")
        i = 0
        while len(self.instances) < 300:
            fam_name, fn, grid = FAMILIES[i % len(FAMILIES)]
            params = grid[i % len(grid)]
            tf = extra_tfs[i % len(extra_tfs)]
            sid = self._sid(fam_name, params, tf) + f"_x{i}"
            self.instances.append(
                Strategy(id=sid, family=fam_name, timeframe=tf,
                         params=dict(params), fn=fn)
            )
            i += 1
        self._load_stats()
        for s in self.instances:
            self._status[s.id] = self._compute_status(s)
        return self.instances

    @staticmethod
    def _sid(fam: str, params: dict, tf: str) -> str:
        kv = "_".join(f"{k}{v}" for k, v in sorted(params.items()))
        return f"{fam}_{kv}_{tf}" if kv else f"{fam}_{tf}"

    # ------------------------------------------------------------- runtime
    def active(self) -> List[Strategy]:
        return [s for s in self.instances if s.stats.enabled]

    def record_result(self, sid: str, won: bool) -> None:
        for s in self.instances:
            if s.id == sid:
                s.stats.trades += 1
                if won:
                    s.stats.wins += 1
                old = self._status.get(sid, HEALTHY)
                if (s.stats.trades >= config.MIN_TRADES_FOR_DISABLE
                        and s.stats.win_rate < config.DISABLE_SCORE_THRESHOLD):
                    s.stats.enabled = False
                new = self._compute_status(s)
                self._status[sid] = new
                if new != old and self._on_status_change:
                    try:
                        self._on_status_change(sid, old, new)
                    except Exception:
                        pass
                self._save_stats()
                return

    def status(self, sid: str) -> str:
        return self._status.get(sid, HEALTHY)

    def _compute_status(self, s: Strategy) -> str:
        if not s.stats.enabled:
            return DISABLED
        if (s.stats.trades >= config.MIN_TRADES_FOR_DISABLE
                and s.stats.win_rate < config.UNDERPERFORM_WINRATE):
            return UNDERPERFORMING
        return HEALTHY

    def top_n(self, n: int = 10) -> List[Tuple[str, float, int]]:
        ranked = sorted(self.instances, key=lambda s: s.stats.win_rate,
                        reverse=True)
        return [(s.id, s.stats.win_rate, s.stats.trades) for s in ranked[:n]]

    def worst_n(self, n: int = 10) -> List[Tuple[str, float, int]]:
        traded = [s for s in self.instances if s.stats.trades > 0]
        ranked = sorted(traded, key=lambda s: s.stats.win_rate)
        return [(s.id, s.stats.win_rate, s.stats.trades) for s in ranked[:n]]

    # ------------------------------------------------------------ persist
    def _save_stats(self) -> None:
        os.makedirs(os.path.dirname(config.STRATEGY_STATS_FILE) or ".",
                    exist_ok=True)
        data = {s.id: {"trades": s.stats.trades,
                       "wins": s.stats.wins,
                       "enabled": s.stats.enabled}
                for s in self.instances}
        with open(config.STRATEGY_STATS_FILE, "w") as f:
            json.dump(data, f)

    def _load_stats(self) -> None:
        if not os.path.exists(config.STRATEGY_STATS_FILE):
            return
        try:
            with open(config.STRATEGY_STATS_FILE) as f:
                data = json.load(f)
        except Exception:
            return
        for s in self.instances:
            if s.id in data:
                d = data[s.id]
                s.stats = StrategyStats(trades=int(d.get("trades", 0)),
                                        wins=int(d.get("wins", 0)),
                                        enabled=bool(d.get("enabled", True)))
