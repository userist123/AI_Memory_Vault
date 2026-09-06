"""Multi-timeframe rate cache."""
from __future__ import annotations

import time
from typing import Dict, Optional, Tuple

try:
    import MetaTrader5 as mt5
except ImportError:  # pragma: no cover
    mt5 = None  # type: ignore

from core.mt5_client import MT5Client


TF_MAP = {
    "M1":  getattr(mt5, "TIMEFRAME_M1", 1)   if mt5 else 1,
    "M5":  getattr(mt5, "TIMEFRAME_M5", 5)   if mt5 else 5,
    "M15": getattr(mt5, "TIMEFRAME_M15", 15) if mt5 else 15,
    "M30": getattr(mt5, "TIMEFRAME_M30", 30) if mt5 else 30,
    "H1":  getattr(mt5, "TIMEFRAME_H1", 60)  if mt5 else 60,
    "H4":  getattr(mt5, "TIMEFRAME_H4", 240) if mt5 else 240,
    "D1":  getattr(mt5, "TIMEFRAME_D1", 1440) if mt5 else 1440,
}

CACHE_TTL = {"M1": 5, "M5": 15, "M15": 30, "M30": 45, "H1": 60, "H4": 120, "D1": 300}


class DataFeed:
    def __init__(self, client: MT5Client) -> None:
        self.client = client
        self._cache: Dict[Tuple[str, str], Tuple[float, object]] = {}

    def rates(self, symbol: str, tf: str, count: int = 200):
        key = (symbol, tf)
        now = time.time()
        if key in self._cache:
            ts, data = self._cache[key]
            if now - ts < CACHE_TTL.get(tf, 30) and data is not None and len(data) >= count:
                return data[-count:]
        tf_const = TF_MAP[tf]
        data = self.client.rates(symbol, tf_const, max(count, 250))
        if data is None:
            return None
        self._cache[key] = (now, data)
        return data[-count:]
