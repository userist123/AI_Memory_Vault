"""MT5 connectivity, symbol info caching, filling-mode detection,
and a single safe wrapper around `order_send`.
"""
from __future__ import annotations

import time
from dataclasses import dataclass
from typing import Optional

try:
    import MetaTrader5 as mt5
except ImportError:  # pragma: no cover - allow import on non-Windows for tests
    mt5 = None  # type: ignore


@dataclass
class TickData:
    bid: float
    ask: float
    time: int


def get_filling_type(info) -> int:
    """Decode `symbol_info().filling_mode` BITMASK to a valid MT5 constant.

    bit 1 -> FOK allowed, bit 2 -> IOC allowed. Default falls back to RETURN.
    Hardcoding ORDER_FILLING_IOC/FOK is the #1 cause of retcode 10030.
    """
    flags = int(getattr(info, "filling_mode", 0))
    if flags & 2:
        return mt5.ORDER_FILLING_IOC
    if flags & 1:
        return mt5.ORDER_FILLING_FOK
    return mt5.ORDER_FILLING_RETURN


class MT5Client:
    def __init__(self) -> None:
        self._connected: bool = False
        self._symbol_info_cache: dict = {}

    # ------------------------------------------------------------------ conn
    def connect(self) -> bool:
        if mt5 is None:
            return False
        if not mt5.initialize():
            return False
        self._connected = True
        return True

    def shutdown(self) -> None:
        if self._connected and mt5 is not None:
            mt5.shutdown()
            self._connected = False

    @property
    def connected(self) -> bool:
        return self._connected

    # ---------------------------------------------------------------- account
    def account_info(self):
        return mt5.account_info() if mt5 else None

    # ----------------------------------------------------------------- symbol
    def symbol_info(self, symbol: str):
        if symbol in self._symbol_info_cache:
            cached, ts = self._symbol_info_cache[symbol]
            if time.time() - ts < 5.0:
                return cached
        if mt5 is None:
            return None
        if not mt5.symbol_select(symbol, True):
            return None
        info = mt5.symbol_info(symbol)
        if info is not None:
            self._symbol_info_cache[symbol] = (info, time.time())
        return info

    def tick(self, symbol: str) -> Optional[TickData]:
        if mt5 is None:
            return None
        t = mt5.symbol_info_tick(symbol)
        if t is None:
            return None
        return TickData(bid=t.bid, ask=t.ask, time=t.time)

    # ------------------------------------------------------------------ rates
    def rates(self, symbol: str, timeframe: int, count: int):
        if mt5 is None:
            return None
        return mt5.copy_rates_from_pos(symbol, timeframe, 0, count)

    # ------------------------------------------------------------ positions
    def positions_get(self, **kwargs):
        if mt5 is None:
            return ()
        res = mt5.positions_get(**kwargs)
        return res or ()

    def history_deals_get(self, date_from, date_to):
        if mt5 is None:
            return ()
        res = mt5.history_deals_get(date_from, date_to)
        return res or ()

    # --------------------------------------------------------------- send
    def order_send(self, request: dict):
        """Thin wrapper. Retries on requote/price-changed retcodes."""
        if mt5 is None:
            return None
        last = None
        for _ in range(3):
            last = mt5.order_send(request)
            if last is None:
                time.sleep(0.2)
                continue
            rc = last.retcode
            if rc == mt5.TRADE_RETCODE_DONE:
                return last
            if rc in (mt5.TRADE_RETCODE_REQUOTE, mt5.TRADE_RETCODE_PRICE_CHANGED):
                time.sleep(0.2)
                continue
            return last
        return last
