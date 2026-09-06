"""Execution: build orders + SL/TP ladder, send / partial-close / modify SL,
post-send confirmation, PnL aggregation by `position_id` + DEAL_ENTRY_OUT.
"""
from __future__ import annotations

import time
from dataclasses import dataclass, field
from typing import List, Optional

try:
    import MetaTrader5 as mt5
except ImportError:  # pragma: no cover
    mt5 = None  # type: ignore

import config
from core.mt5_client import MT5Client, get_filling_type


@dataclass
class OrderPlan:
    side: str
    price: float
    sl: float
    tp: float                       # broker-side TP (last ladder level)
    lot: float
    strategy_id: str
    tp_ladder: List[float] = field(default_factory=list)
    tp_fractions: List[float] = field(default_factory=list)
    sl_dist: float = 0.0


def build_levels(side: str, price: float, atr: float, digits: int):
    """Return (sl, tp, sl_dist) for a single-TP trade — legacy helper."""
    sl_dist = atr * config.ATR_SL_MULT
    tp_dist = atr * config.ATR_TP_MULT
    if side == "BUY":
        sl = round(price - sl_dist, digits)
        tp = round(price + tp_dist, digits)
    else:
        sl = round(price + sl_dist, digits)
        tp = round(price - tp_dist, digits)
    return sl, tp, sl_dist


def build_tp_ladder(side: str, price: float, sl_dist: float,
                    digits: int) -> List[float]:
    """Return TP prices for each ladder level, rounded to broker digits."""
    rr = config.TP_RR_MULTIPLIERS[:config.TP_LEVELS]
    ladder: List[float] = []
    for mult in rr:
        if side == "BUY":
            ladder.append(round(price + sl_dist * mult, digits))
        else:
            ladder.append(round(price - sl_dist * mult, digits))
    return ladder


def round_volume(volume: float, info) -> float:
    step = float(getattr(info, "volume_step", 0.01)) or 0.01
    vmin = float(getattr(info, "volume_min", 0.01))
    vmax = float(getattr(info, "volume_max", 100.0))
    v = round(volume / step) * step
    v = max(vmin, min(vmax, v))
    return round(v, 2)


class Executor:
    is_paper = False

    def __init__(self, client: MT5Client) -> None:
        self.client = client

    # ------------------------------------------------------------------ send
    def send(self, symbol: str, plan: OrderPlan) -> Optional[int]:
        info = self.client.symbol_info(symbol)
        if info is None or mt5 is None:
            return None
        order_type = mt5.ORDER_TYPE_BUY if plan.side == "BUY" else mt5.ORDER_TYPE_SELL
        # When ladder is active, leave broker TP at last level so the position
        # still has a hard guard; intermediate TPs are realised via partial closes.
        broker_tp = float(plan.tp_ladder[-1]) if plan.tp_ladder else float(plan.tp)
        request = {
            "action": mt5.TRADE_ACTION_DEAL,
            "symbol": symbol,
            "volume": float(plan.lot),
            "type": order_type,
            "price": float(plan.price),
            "tp": broker_tp,
            "sl": float(plan.sl),
            "deviation": config.DEVIATION_POINTS,
            "magic": config.MAGIC,
            "comment": f"ELITE_{plan.strategy_id[:8]}",
            "type_time": mt5.ORDER_TIME_GTC,
            "type_filling": get_filling_type(info),
        }
        result = self.client.order_send(request)
        if result is None or result.retcode != mt5.TRADE_RETCODE_DONE:
            return None

        time.sleep(config.POST_ORDER_CONFIRM_DELAY_SEC)
        candidates = [
            int(x) for x in (
                getattr(result, "position", 0),
                getattr(result, "order", 0),
                getattr(result, "deal", 0),
            ) if int(x or 0) > 0
        ]
        for ticket in candidates:
            positions = self.client.positions_get(ticket=ticket)
            if positions:
                return int(getattr(positions[0], "ticket", ticket))

        positions = self.client.positions_get(symbol=symbol)
        matches = [p for p in positions if getattr(p, "magic", 0) == config.MAGIC]
        if not matches:
            return None
        newest = max(matches, key=lambda p: getattr(p, "time", 0))
        return int(getattr(newest, "ticket"))

    # ---------------------------------------------------- partial close / SL
    def partial_close(self, position, volume_to_close: float) -> bool:
        """Close `volume_to_close` lots of a position. Returns True on success."""
        if mt5 is None:
            return False
        info = self.client.symbol_info(position.symbol)
        tick = self.client.tick(position.symbol)
        if info is None or tick is None:
            return False
        vol = round_volume(volume_to_close, info)
        cur_vol = float(getattr(position, "volume", 0.0))
        if vol <= 0 or vol > cur_vol + 1e-9:
            return False
        if position.type == mt5.POSITION_TYPE_BUY:
            close_type = mt5.ORDER_TYPE_SELL
            price = tick.bid
        else:
            close_type = mt5.ORDER_TYPE_BUY
            price = tick.ask
        request = {
            "action": mt5.TRADE_ACTION_DEAL,
            "symbol": position.symbol,
            "volume": float(vol),
            "type": close_type,
            "position": int(position.ticket),
            "price": float(price),
            "deviation": config.DEVIATION_POINTS,
            "magic": config.MAGIC,
            "comment": "ELITE_PARTIAL",
            "type_time": mt5.ORDER_TIME_GTC,
            "type_filling": get_filling_type(info),
        }
        result = self.client.order_send(request)
        return result is not None and result.retcode == mt5.TRADE_RETCODE_DONE

    def modify_sl(self, position, new_sl: float) -> bool:
        """Move SL on an existing position. Direction is enforced by caller."""
        if mt5 is None:
            return False
        request = {
            "action": mt5.TRADE_ACTION_SLTP,
            "symbol": position.symbol,
            "position": int(position.ticket),
            "sl": float(new_sl),
            "tp": float(getattr(position, "tp", 0.0) or 0.0),
            "magic": config.MAGIC,
        }
        result = self.client.order_send(request)
        return result is not None and result.retcode == mt5.TRADE_RETCODE_DONE

    # ----------------------------------------------------------- full close
    def close(self, position) -> bool:
        return self.partial_close(position, float(getattr(position, "volume", 0.0)))

    # ------------------------------------------------------- PnL helpers
    def position_pnl(self, ticket: int, date_from, date_to) -> float:
        """Aggregate realised PnL by position_id + DEAL_ENTRY_OUT (incl. partials)."""
        if mt5 is None:
            return 0.0
        deals = self.client.history_deals_get(date_from, date_to)
        closed = [d for d in deals
                  if d.position_id == ticket
                  and d.entry in (mt5.DEAL_ENTRY_OUT, mt5.DEAL_ENTRY_INOUT)]
        return float(sum(d.profit + d.commission + d.swap for d in closed))
