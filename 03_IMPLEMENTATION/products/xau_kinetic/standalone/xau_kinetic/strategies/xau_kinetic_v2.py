"""
XAU_Kinetic V2 Strategy Implementation for Gold (XAUUSD).
Pure functional decision engine based on EMA trend filter, ATR volatility channels,
and RSI momentum confirmations using CLOSED candles ONLY (anti-look-ahead compliant).
"""

import logging
from typing import Any
import numpy as np
import pandas as pd

from xau_kinetic.application.interfaces import IStrategy
from xau_kinetic.domain.models import SignalObject, SignalType

logger = logging.getLogger("xau_kinetic.strategy.v2")


class XAUKineticV2Strategy(IStrategy):
    """
    Pure functional strategy engine for XAUUSD.
    Reads historical DataFrame, calculates technical indicators, and returns SignalObject.
    Zero network or stateful side-effects.
    """

    def __init__(
        self,
        symbol: str = "XAUUSD",
        fast_ema: int = 12,
        slow_ema: int = 26,
        rsi_period: int = 14,
        atr_period: int = 14,
        atr_multiplier_sl: float = 1.5,
        atr_multiplier_tp: float = 2.5,
        rsi_overbought: float = 70.0,
        rsi_oversold: float = 30.0,
    ) -> None:
        self._symbol = symbol
        self.fast_ema = fast_ema
        self.slow_ema = slow_ema
        self.rsi_period = rsi_period
        self.atr_period = atr_period
        self.atr_multiplier_sl = atr_multiplier_sl
        self.atr_multiplier_tp = atr_multiplier_tp
        self.rsi_overbought = rsi_overbought
        self.rsi_oversold = rsi_oversold

    @property
    def name(self) -> str:
        return "XAU_Kinetic_v2"

    def _calculate_rsi(self, series: pd.Series, period: int) -> pd.Series:
        """
        Calculate Relative Strength Index (RSI) using Wilder's Exponential Smoothing.
        Mathematically exact standard RSI implementation.
        """
        delta = series.diff()
        gain = delta.clip(lower=0.0)
        loss = -delta.clip(upper=0.0)

        # Wilder's Smoothing: ewm with alpha = 1 / period
        avg_gain = gain.ewm(alpha=1.0 / period, min_periods=period, adjust=False).mean()
        avg_loss = loss.ewm(alpha=1.0 / period, min_periods=period, adjust=False).mean()

        rs = avg_gain / avg_loss

        # Zero-loss and zero-gain handling
        rsi = pd.Series(
            np.where(
                avg_loss == 0.0,
                np.where(avg_gain == 0.0, 50.0, 100.0),
                100.0 - (100.0 / (1.0 + rs)),
            ),
            index=series.index,
        )
        return rsi.bfill().fillna(50.0)

    def _calculate_atr(self, df: pd.DataFrame, period: int) -> pd.Series:
        """
        Calculate Average True Range (ATR) using Wilder's Exponential Smoothing.
        Mathematically exact standard ATR implementation.
        """
        high = df["high"]
        low = df["low"]
        close_prev = df["close"].shift(1)

        tr1 = high - low
        tr2 = (high - close_prev).abs()
        tr3 = (low - close_prev).abs()

        tr = pd.concat([tr1, tr2, tr3], axis=1).max(axis=1)
        atr = tr.ewm(alpha=1.0 / period, min_periods=period, adjust=False).mean()
        return atr.bfill().fillna(0.0)

    def generate_signal(self, market_data: pd.DataFrame, closed_bars_only: bool = True) -> SignalObject:
        """
        Pure functional signal generator.
        When closed_bars_only=True, market_data iloc[-1] is treated as the latest completed bar N-1.
        When closed_bars_only=False, market_data iloc[-2] is treated as bar N-1.
        """
        if market_data is None or len(market_data) < max(self.slow_ema, self.rsi_period, self.atr_period) + 5:
            logger.warning("Insufficient bar data to evaluate strategy.")
            return SignalObject(symbol=self._symbol, signal_type=SignalType.HOLD)

        df = market_data.copy()

        # Compute Technical Indicators
        df["ema_fast"] = df["close"].ewm(span=self.fast_ema, adjust=False).mean()
        df["ema_slow"] = df["close"].ewm(span=self.slow_ema, adjust=False).mean()
        df["rsi"] = self._calculate_rsi(df["close"], self.rsi_period)
        df["atr"] = self._calculate_atr(df, self.atr_period)

        # Bar indexing selection for strict anti-look-ahead
        idx_closed = -1 if closed_bars_only else -2
        idx_prev = -2 if closed_bars_only else -3

        closed_bar = df.iloc[idx_closed]
        prev_closed_bar = df.iloc[idx_prev]

        close_price = float(closed_bar["close"])
        fast_ema_val = float(closed_bar["ema_fast"])
        slow_ema_val = float(closed_bar["ema_slow"])
        rsi_val = float(closed_bar["rsi"])
        atr_val = float(closed_bar["atr"])

        prev_fast_ema = float(prev_closed_bar["ema_fast"])
        prev_slow_ema = float(prev_closed_bar["ema_slow"])

        # Crossover checks
        bullish_cross = (prev_fast_ema <= prev_slow_ema) and (fast_ema_val > slow_ema_val)
        bearish_cross = (prev_fast_ema >= prev_slow_ema) and (fast_ema_val < slow_ema_val)

        signal_type = SignalType.HOLD
        confidence = 0.0
        stop_loss = 0.0
        take_profit = 0.0
        metadata: dict[str, Any] = {
            "close": close_price,
            "ema_fast": fast_ema_val,
            "ema_slow": slow_ema_val,
            "rsi": rsi_val,
            "atr": atr_val,
        }

        if bullish_cross and rsi_val < self.rsi_overbought:
            signal_type = SignalType.BUY
            confidence = min(1.0, 0.6 + (50.0 - abs(rsi_val - 50.0)) / 100.0)
            stop_loss = round(close_price - (atr_val * self.atr_multiplier_sl), 2)
            take_profit = round(close_price + (atr_val * self.atr_multiplier_tp), 2)
            metadata["reason"] = "Bullish EMA Crossover + Momentum Confirmation"

        elif bearish_cross and rsi_val > self.rsi_oversold:
            signal_type = SignalType.SELL
            confidence = min(1.0, 0.6 + (50.0 - abs(rsi_val - 50.0)) / 100.0)
            stop_loss = round(close_price + (atr_val * self.atr_multiplier_sl), 2)
            take_profit = round(close_price - (atr_val * self.atr_multiplier_tp), 2)
            metadata["reason"] = "Bearish EMA Crossover + Momentum Confirmation"

        return SignalObject(
            symbol=self._symbol,
            signal_type=signal_type,
            confidence=confidence,
            target_price=close_price,
            stop_loss=stop_loss,
            take_profit=take_profit,
            volume=0.1,
            metadata=metadata,
        )
