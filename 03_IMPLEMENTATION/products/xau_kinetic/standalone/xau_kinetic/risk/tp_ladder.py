"""
Multi-TP Ladder Position Manager & Automated Break-Even Trailing Stop Engine.
Manages institutional scaling out of trades and break-even risk elimination.
"""

import logging
from typing import Any
from pydantic import BaseModel, Field, ConfigDict

from xau_kinetic.domain.models import SignalType, Position

logger = logging.getLogger("xau_kinetic.tp_ladder")


class TPLadderTarget(BaseModel):
    """Individual Take-Profit ladder level target."""
    model_config = ConfigDict(frozen=True)

    level: int = Field(..., gt=0)
    atr_multiplier: float = Field(..., gt=0.0)
    volume_fraction: float = Field(..., gt=0.0, le=1.0)
    target_price: float = Field(..., gt=0.0)


class TPLadderManager:
    """
    Manages multi-tier Take Profit ladder targets and Break-Even trailing stop shifts.
    """

    def __init__(
        self,
        tp1_atr_mult: float = 1.0,
        tp1_vol_frac: float = 0.5,
        tp2_atr_mult: float = 2.0,
        tp2_vol_frac: float = 0.3,
        tp3_atr_mult: float = 3.5,
        tp3_vol_frac: float = 0.2,
        break_even_atr_trigger: float = 1.0,
        break_even_buffer_points: float = 10.0,  # 10 points = $0.10 buffer above entry
    ) -> None:
        self.tp1_atr_mult = tp1_atr_mult
        self.tp1_vol_frac = tp1_vol_frac
        self.tp2_atr_mult = tp2_atr_mult
        self.tp2_vol_frac = tp2_vol_frac
        self.tp3_atr_mult = tp3_atr_mult
        self.tp3_vol_frac = tp3_vol_frac
        self.break_even_atr_trigger = break_even_atr_trigger
        self.break_even_buffer_points = break_even_buffer_points

    def calculate_ladder_targets(
        self,
        entry_price: float,
        signal_type: SignalType,
        atr_value: float,
    ) -> list[TPLadderTarget]:
        """
        Calculate multi-tier TP ladder price targets based on entry price and ATR.
        """
        if entry_price <= 0.0 or atr_value <= 0.0:
            return []

        direction = 1.0 if signal_type == SignalType.BUY else -1.0

        tp1_price = round(entry_price + (direction * atr_value * self.tp1_atr_mult), 2)
        tp2_price = round(entry_price + (direction * atr_value * self.tp2_atr_mult), 2)
        tp3_price = round(entry_price + (direction * atr_value * self.tp3_atr_mult), 2)

        return [
            TPLadderTarget(level=1, atr_multiplier=self.tp1_atr_mult, volume_fraction=self.tp1_vol_frac, target_price=tp1_price),
            TPLadderTarget(level=2, atr_multiplier=self.tp2_atr_mult, volume_fraction=self.tp2_vol_frac, target_price=tp2_price),
            TPLadderTarget(level=3, atr_multiplier=self.tp3_atr_mult, volume_fraction=self.tp3_vol_frac, target_price=tp3_price),
        ]

    def evaluate_break_even(
        self,
        position: Position,
        current_price: float,
        atr_value: float,
    ) -> float | None:
        """
        Evaluate whether position profit threshold is reached to shift Stop Loss to Break-Even.
        Returns new SL price if break-even should be set, or None if no update required.
        """
        if position.open_price <= 0.0 or current_price <= 0.0 or atr_value <= 0.0:
            return None

        buffer_usd = self.break_even_buffer_points * 0.01

        if position.type == SignalType.BUY:
            price_distance = current_price - position.open_price
            trigger_distance = atr_value * self.break_even_atr_trigger
            new_sl = round(position.open_price + buffer_usd, 2)

            # Trigger break-even if price reached trigger distance and current SL is below new SL
            if price_distance >= trigger_distance and position.sl < new_sl:
                logger.info(f"Break-Even triggered for BUY Position #{position.ticket}: New SL = ${new_sl:.2f}")
                return new_sl

        elif position.type == SignalType.SELL:
            price_distance = position.open_price - current_price
            trigger_distance = atr_value * self.break_even_atr_trigger
            new_sl = round(position.open_price - buffer_usd, 2)

            # Trigger break-even for SELL if price dropped sufficiently and SL is above new SL
            if price_distance >= trigger_distance and (position.sl <= 0.0 or position.sl > new_sl):
                logger.info(f"Break-Even triggered for SELL Position #{position.ticket}: New SL = ${new_sl:.2f}")
                return new_sl

        return None
