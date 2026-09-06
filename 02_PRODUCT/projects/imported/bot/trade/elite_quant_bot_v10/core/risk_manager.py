"""Risk management: ATR-based sizing, circuit breakers, cooldowns,
spread / session / dead-market filters.
"""
from __future__ import annotations

from dataclasses import dataclass, field
from datetime import datetime, time as dtime, timedelta, timezone
from typing import List, Optional

import config


@dataclass
class RiskState:
    recent_results: List[float] = field(default_factory=list)   # last trade PnLs
    cooldown_until: Optional[datetime] = None
    orders_today: int = 0
    day_start_balance: float = 0.0
    day_anchor: Optional[datetime] = None   # midnight UTC of current day

    def reset_day_if_needed(self, balance: float) -> None:
        now = datetime.now(timezone.utc)
        midnight = datetime.combine(now.date(), dtime(0, 0), tzinfo=timezone.utc)
        if self.day_anchor != midnight:
            self.day_anchor = midnight
            self.day_start_balance = balance
            self.orders_today = 0


class RiskManager:
    def __init__(self) -> None:
        self.state = RiskState()

    # ----------------------------------------------------------- sizing
    @staticmethod
    def calc_lot(balance: float, sl_distance: float, info) -> float:
        if sl_distance <= 0 or info is None:
            return 0.0
        risk_amount = balance * config.RISK_PCT
        tick_size = float(info.trade_tick_size) or 1e-5
        tick_value = float(info.trade_tick_value) or 1.0
        sl_ticks = sl_distance / tick_size
        if sl_ticks <= 0:
            return 0.0
        raw = risk_amount / (sl_ticks * tick_value)
        step = float(info.volume_step) or 0.01
        lot = round(raw / step) * step
        lot = max(float(info.volume_min), min(float(info.volume_max), lot))
        return round(lot, 2)

    # --------------------------------------------------------- breakers
    def update_after_trade(self, pnl: float) -> None:
        self.state.recent_results.append(pnl)
        if len(self.state.recent_results) > 50:
            self.state.recent_results = self.state.recent_results[-50:]
        # consecutive losses
        last = self.state.recent_results[-config.MAX_CONSECUTIVE_LOSSES:]
        if (len(last) >= config.MAX_CONSECUTIVE_LOSSES
                and all(p < 0 for p in last)):
            self.state.cooldown_until = (
                datetime.now(timezone.utc)
                + timedelta(minutes=config.COOLDOWN_MINUTES)
            )
            self.state.recent_results.clear()

    def in_cooldown(self) -> bool:
        cu = self.state.cooldown_until
        if cu is None:
            return False
        if datetime.now(timezone.utc) >= cu:
            self.state.cooldown_until = None
            return False
        return True

    def daily_loss_breached(self, current_balance: float,
                            realised_pnl_today: float) -> bool:
        if self.state.day_start_balance <= 0:
            return False
        limit = self.state.day_start_balance * config.DAILY_LOSS_LIMIT_PCT
        return realised_pnl_today <= -limit

    def order_limit_reached(self) -> bool:
        return self.state.orders_today >= config.MAX_ORDERS_PER_DAY

    def register_order(self) -> None:
        self.state.orders_today += 1

    # ---------------------------------------------------------- filters
    @staticmethod
    def spread_ok(tick, info) -> bool:
        if tick is None or info is None:
            return False
        point = float(info.point) or 1e-5
        spread_pips = (tick.ask - tick.bid) / (point * 10.0)
        return spread_pips <= config.MAX_SPREAD_PIPS

    @staticmethod
    def in_session() -> bool:
        now = datetime.now(timezone.utc)
        return config.SESSION_START_HOUR_UTC <= now.hour < config.SESSION_END_HOUR_UTC

    @staticmethod
    def market_alive(atr_now: float, atr_long: float) -> bool:
        if atr_long <= 0:
            return False
        return atr_now >= 0.5 * atr_long
