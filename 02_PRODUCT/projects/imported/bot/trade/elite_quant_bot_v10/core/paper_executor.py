"""Paper-trading executor — simulates fills without sending orders to MT5.

Drop-in for `core.execution.Executor`: same `send` / `close` /
`position_pnl` surface, plus `partial_close` and `modify_sl` to support
the multi-TP ladder. Slippage = 0; fills at the requested price.
SL is checked against the live `tick` on every state-machine reconciliation
pass; TPs are managed externally by the state machine.
"""
from __future__ import annotations

import itertools
import threading
from dataclasses import dataclass
from datetime import datetime, timezone
from typing import Dict, List, Optional

import config


@dataclass
class _SimPosition:
    ticket: int
    symbol: str
    side: str
    volume: float
    open_price: float
    sl: float
    tp: float
    strategy_id: str
    opened_at: datetime
    profit: float = 0.0
    magic: int = 0
    type: int = 0


class PaperExecutor:
    is_paper = True
    _ticket_seq = itertools.count(9_000_001)

    def __init__(self, client) -> None:
        self.client = client
        self._open: Dict[int, _SimPosition] = {}
        self._closed_pnl: Dict[int, float] = {}
        self._closed_meta: Dict[int, _SimPosition] = {}
        self._lock = threading.Lock()

    # ----------------------------------------------------------- send/close
    def send(self, symbol: str, plan) -> Optional[int]:
        info = self.client.symbol_info(symbol)
        if info is None:
            return None
        ticket = next(self._ticket_seq)
        # When ladder is active the state machine manages TP, so we keep
        # broker-side TP at the last ladder level (or zero if absent).
        tp_val = float(plan.tp_ladder[-1]) if getattr(plan, "tp_ladder", None) \
            else float(plan.tp)
        pos = _SimPosition(
            ticket=ticket, symbol=symbol, side=plan.side,
            volume=float(plan.lot), open_price=float(plan.price),
            sl=float(plan.sl), tp=tp_val,
            strategy_id=plan.strategy_id,
            opened_at=datetime.now(timezone.utc),
            magic=config.MAGIC,
            type=0 if plan.side == "BUY" else 1,
        )
        with self._lock:
            self._open[ticket] = pos
        return ticket

    def close(self, position) -> bool:
        return self.partial_close(position,
                                  float(getattr(position, "volume", 0.0)))

    def partial_close(self, position, volume_to_close: float) -> bool:
        ticket = int(getattr(position, "ticket", 0))
        with self._lock:
            pos = self._open.get(ticket)
            if pos is None or volume_to_close <= 0:
                return False
            info = self.client.symbol_info(pos.symbol)
            step = float(getattr(info, "volume_step", 0.01)) or 0.01
            vmin = float(getattr(info, "volume_min", 0.01))
            vol = round(volume_to_close / step) * step
            vol = max(vmin, min(pos.volume, vol))
            tick = self.client.tick(pos.symbol)
            if tick is None:
                return False
            price = tick.bid if pos.side == "BUY" else tick.ask
            contract = float(getattr(info, "trade_contract_size", 100_000.0))
            if pos.side == "BUY":
                pnl = (price - pos.open_price) * vol * contract
            else:
                pnl = (pos.open_price - price) * vol * contract
            pos.volume -= vol
            self._closed_pnl[ticket] = self._closed_pnl.get(ticket, 0.0) + float(pnl)
            if pos.volume <= 1e-9:
                self._open.pop(ticket, None)
                self._closed_meta[ticket] = pos
                pos.profit = float(self._closed_pnl[ticket])
        return True

    def modify_sl(self, position, new_sl: float) -> bool:
        ticket = int(getattr(position, "ticket", 0))
        with self._lock:
            pos = self._open.get(ticket)
            if pos is None:
                return False
            pos.sl = float(new_sl)
        return True

    # ---------------------------------------------------------- mt5 shims
    def positions_get(self, ticket: Optional[int] = None) -> List[_SimPosition]:
        with self._lock:
            if ticket is None:
                return list(self._open.values())
            p = self._open.get(int(ticket))
            return [p] if p else []

    def position_pnl(self, ticket: int, date_from=None, date_to=None) -> float:
        with self._lock:
            return float(self._closed_pnl.get(int(ticket), 0.0))

    def closed_meta(self, ticket: int) -> Optional[_SimPosition]:
        with self._lock:
            return self._closed_meta.get(int(ticket))

    # ----------------------------------------------------------- simulation
    def step(self) -> List[int]:
        """Mark-to-market open positions and trigger SL only (TPs managed
        externally by the state machine via partial_close)."""
        closed_now: List[int] = []
        with self._lock:
            tickets = list(self._open.keys())
        for t in tickets:
            with self._lock:
                pos = self._open.get(t)
            if pos is None:
                continue
            tick = self.client.tick(pos.symbol)
            info = self.client.symbol_info(pos.symbol)
            if tick is None or info is None:
                continue
            bid, ask = float(tick.bid), float(tick.ask)
            contract = float(getattr(info, "trade_contract_size", 100_000.0))
            if pos.side == "BUY":
                cur = bid
                pos.profit = (cur - pos.open_price) * pos.volume * contract
                if pos.sl and cur <= pos.sl:
                    self._finalise(t, pos.sl, reason="SL")
                    closed_now.append(t)
                elif pos.tp and cur >= pos.tp:
                    self._finalise(t, pos.tp, reason="TP")
                    closed_now.append(t)
            else:
                cur = ask
                pos.profit = (pos.open_price - cur) * pos.volume * contract
                if pos.sl and cur >= pos.sl:
                    self._finalise(t, pos.sl, reason="SL")
                    closed_now.append(t)
                elif pos.tp and cur <= pos.tp:
                    self._finalise(t, pos.tp, reason="TP")
                    closed_now.append(t)
        return closed_now

    def _finalise(self, ticket: int, exit_price: float, reason: str) -> bool:
        with self._lock:
            pos = self._open.pop(int(ticket), None)
            if pos is None:
                return False
            info = self.client.symbol_info(pos.symbol)
            contract = float(getattr(info, "trade_contract_size", 100_000.0))
            if pos.side == "BUY":
                pnl = (exit_price - pos.open_price) * pos.volume * contract
            else:
                pnl = (pos.open_price - exit_price) * pos.volume * contract
            self._closed_pnl[int(ticket)] = self._closed_pnl.get(int(ticket), 0.0) + float(pnl)
            self._closed_meta[int(ticket)] = pos
            pos.profit = float(self._closed_pnl[int(ticket)])
        return True
