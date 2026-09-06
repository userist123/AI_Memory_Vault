"""Strategy interface + per-instance stat tracking."""
from __future__ import annotations

from dataclasses import dataclass, field
from typing import Dict, Optional, Tuple


SIGNAL_BUY = "BUY"
SIGNAL_SELL = "SELL"
SIGNAL_NEUTRAL = "NEUTRAL"


@dataclass
class StrategyStats:
    trades: int = 0
    wins: int = 0
    enabled: bool = True

    @property
    def win_rate(self) -> float:
        if self.trades == 0:
            return 0.5  # cold-start neutral
        return self.wins / self.trades


@dataclass
class Strategy:
    id: str
    family: str
    timeframe: str
    params: Dict[str, object]
    fn: object  # Callable[(ctx, params, tf) -> (signal, confidence)]
    stats: StrategyStats = field(default_factory=StrategyStats)

    def evaluate(self, ctx: dict) -> Tuple[str, float]:
        try:
            sig, conf = self.fn(ctx, self.params, self.timeframe)
        except Exception:
            return SIGNAL_NEUTRAL, 0.0
        if sig not in (SIGNAL_BUY, SIGNAL_SELL, SIGNAL_NEUTRAL):
            sig = SIGNAL_NEUTRAL
        conf = max(0.0, min(1.0, float(conf)))
        return sig, conf
