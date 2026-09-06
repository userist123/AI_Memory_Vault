"""
ZEUS AI ADVISOR - Motor de inteligenta artificiala pentru trading
Analiza tehnica completa + semnale + sfaturi
"""

import pandas as pd
import numpy as np
from dataclasses import dataclass
from typing import Optional
import ta
from ta.trend import MACD, EMAIndicator, SMAIndicator, ADXIndicator
from ta.momentum import RSIIndicator, StochasticOscillator
from ta.volatility import BollingerBands, AverageTrueRange
from ta.volume import VolumeWeightedAveragePrice


@dataclass
class Signal:
    direction: str        # BUY / SELL / HOLD
    confidence: float     # 0-100
    strength: str         # STRONG / MODERATE / WEAK
    reason: str
    entry: float
    stop_loss: float
    take_profit_1: float
    take_profit_2: float
    take_profit_3: float
    risk_reward: float
    timeframe: str


@dataclass
class AdviceReport:
    symbol: str
    price: float
    signal: Signal
    trend: str
    momentum: str
    volatility: str
    support_levels: list
    resistance_levels: list
    key_indicators: dict
    summary: str
    warnings: list
    score: float          # overall score 0-100


class ZeusAIAdvisor:
    """
    Motor principal de analiza tehnica + AI advisory
    Combina 20+ indicatori tehnici cu logica proprie
    """

    def __init__(self):
        self.weights = {
            "trend": 0.30,
            "momentum": 0.25,
            "volatility": 0.20,
            "volume": 0.15,
            "pattern": 0.10,
        }

    def analyze(self, df: pd.DataFrame, symbol: str, timeframe: str = "1D") -> AdviceReport:
        """
        Analiza completa a unui simbol pe baza datelor OHLCV
        df trebuie sa aiba coloane: open, high, low, close, volume
        """
        if len(df) < 50:
            return self._insufficient_data_report(symbol, df)

        df = df.copy()
        df.columns = [c.lower() for c in df.columns]

        # Calculeaza toti indicatorii
        indicators = self._calculate_indicators(df)

        # Analizeaza trend
        trend_analysis = self._analyze_trend(df, indicators)

        # Analizeaza momentum
        momentum_analysis = self._analyze_momentum(indicators)

        # Analizeaza volatilitate
        volatility_analysis = self._analyze_volatility(df, indicators)

        # Analizeaza volum
        volume_analysis = self._analyze_volume(df, indicators)

        # Detecteaza pattern-uri candlestick
        patterns = self._detect_patterns(df)

        # Calculeaza support / resistance
        supports, resistances = self._find_sr_levels(df)

        # Genereaza scor total
        score = self._calculate_score(
            trend_analysis, momentum_analysis,
            volatility_analysis, volume_analysis, patterns
        )

        # Genereaza semnal de tranzactionare
        price = float(df["close"].iloc[-1])
        signal = self._generate_signal(
            score, price, indicators, supports, resistances,
            trend_analysis, volatility_analysis, symbol, timeframe
        )

        # Genereaza raport complet
        summary = self._generate_summary(
            symbol, price, signal, trend_analysis,
            momentum_analysis, volatility_analysis, patterns, score
        )

        warnings = self._generate_warnings(df, indicators, signal)

        return AdviceReport(
            symbol=symbol,
            price=price,
            signal=signal,
            trend=trend_analysis["label"],
            momentum=momentum_analysis["label"],
            volatility=volatility_analysis["label"],
            support_levels=supports[:3],
            resistance_levels=resistances[:3],
            key_indicators={
                "RSI": round(indicators.get("rsi", 50), 2),
                "MACD": round(indicators.get("macd", 0), 4),
                "MACD_Signal": round(indicators.get("macd_signal", 0), 4),
                "BB_Upper": round(indicators.get("bb_upper", price), 2),
                "BB_Lower": round(indicators.get("bb_lower", price), 2),
                "BB_Width": round(indicators.get("bb_width", 0), 4),
                "EMA_20": round(indicators.get("ema20", price), 2),
                "EMA_50": round(indicators.get("ema50", price), 2),
                "EMA_200": round(indicators.get("ema200", price), 2),
                "ADX": round(indicators.get("adx", 20), 2),
                "Stoch_K": round(indicators.get("stoch_k", 50), 2),
                "Stoch_D": round(indicators.get("stoch_d", 50), 2),
                "ATR": round(indicators.get("atr", 0), 4),
            },
            summary=summary,
            warnings=warnings,
            score=round(score, 1),
        )

    def _calculate_indicators(self, df: pd.DataFrame) -> dict:
        ind = {}
        close = df["close"]
        high = df["high"]
        low = df["low"]
        vol = df["volume"]

        # RSI
        try:
            rsi = RSIIndicator(close=close, window=14)
            ind["rsi"] = float(rsi.rsi().iloc[-1])
        except Exception:
            ind["rsi"] = 50.0

        # MACD
        try:
            macd = MACD(close=close)
            ind["macd"] = float(macd.macd().iloc[-1])
            ind["macd_signal"] = float(macd.macd_signal().iloc[-1])
            ind["macd_diff"] = float(macd.macd_diff().iloc[-1])
            ind["macd_prev_diff"] = float(macd.macd_diff().iloc[-2]) if len(df) > 1 else 0
        except Exception:
            ind["macd"] = ind["macd_signal"] = ind["macd_diff"] = 0.0
            ind["macd_prev_diff"] = 0.0

        # Bollinger Bands
        try:
            bb = BollingerBands(close=close, window=20, window_dev=2)
            ind["bb_upper"] = float(bb.bollinger_hband().iloc[-1])
            ind["bb_lower"] = float(bb.bollinger_lband().iloc[-1])
            ind["bb_mid"] = float(bb.bollinger_mavg().iloc[-1])
            ind["bb_width"] = float(bb.bollinger_wband().iloc[-1])
            ind["bb_pct"] = float(bb.bollinger_pband().iloc[-1])
        except Exception:
            p = float(close.iloc[-1])
            ind["bb_upper"] = p * 1.02
            ind["bb_lower"] = p * 0.98
            ind["bb_mid"] = p
            ind["bb_width"] = 0.04
            ind["bb_pct"] = 0.5

        # EMA
        for w in [9, 20, 50, 100, 200]:
            try:
                ind[f"ema{w}"] = float(EMAIndicator(close=close, window=w).ema_indicator().iloc[-1])
            except Exception:
                ind[f"ema{w}"] = float(close.iloc[-1])

        # SMA
        for w in [20, 50, 200]:
            try:
                ind[f"sma{w}"] = float(SMAIndicator(close=close, window=w).sma_indicator().iloc[-1])
            except Exception:
                ind[f"sma{w}"] = float(close.iloc[-1])

        # Stochastic
        try:
            stoch = StochasticOscillator(high=high, low=low, close=close)
            ind["stoch_k"] = float(stoch.stoch().iloc[-1])
            ind["stoch_d"] = float(stoch.stoch_signal().iloc[-1])
        except Exception:
            ind["stoch_k"] = ind["stoch_d"] = 50.0

        # ADX
        try:
            adx = ADXIndicator(high=high, low=low, close=close)
            ind["adx"] = float(adx.adx().iloc[-1])
            ind["adx_pos"] = float(adx.adx_pos().iloc[-1])
            ind["adx_neg"] = float(adx.adx_neg().iloc[-1])
        except Exception:
            ind["adx"] = 20.0
            ind["adx_pos"] = ind["adx_neg"] = 10.0

        # ATR
        try:
            atr = AverageTrueRange(high=high, low=low, close=close)
            ind["atr"] = float(atr.average_true_range().iloc[-1])
        except Exception:
            ind["atr"] = float(close.iloc[-1]) * 0.01

        # Volume indicators
        try:
            vol_ma = vol.rolling(20).mean()
            ind["vol_ratio"] = float(vol.iloc[-1] / vol_ma.iloc[-1]) if float(vol_ma.iloc[-1]) > 0 else 1.0
        except Exception:
            ind["vol_ratio"] = 1.0

        return ind

    def _analyze_trend(self, df: pd.DataFrame, ind: dict) -> dict:
        close = float(df["close"].iloc[-1])
        ema20 = ind.get("ema20", close)
        ema50 = ind.get("ema50", close)
        ema200 = ind.get("ema200", close)

        scores = []

        # Price vs EMAs
        if close > ema20: scores.append(1)
        elif close < ema20: scores.append(-1)
        else: scores.append(0)

        if close > ema50: scores.append(1)
        elif close < ema50: scores.append(-1)
        else: scores.append(0)

        if close > ema200: scores.append(1)
        elif close < ema200: scores.append(-1)
        else: scores.append(0)

        # Golden/Death cross
        if ema20 > ema50 > ema200: scores.append(2)
        elif ema20 < ema50 < ema200: scores.append(-2)

        # ADX trend strength
        adx = ind.get("adx", 20)
        adx_pos = ind.get("adx_pos", 10)
        adx_neg = ind.get("adx_neg", 10)

        if adx > 25:
            if adx_pos > adx_neg: scores.append(1)
            else: scores.append(-1)

        total = sum(scores) / max(len(scores), 1)

        if total >= 0.6: label = "BULLISH PUTERNIC"
        elif total >= 0.2: label = "BULLISH"
        elif total <= -0.6: label = "BEARISH PUTERNIC"
        elif total <= -0.2: label = "BEARISH"
        else: label = "NEUTRAL / LATERAL"

        return {"score": total, "label": label, "adx": adx}

    def _analyze_momentum(self, ind: dict) -> dict:
        scores = []

        rsi = ind.get("rsi", 50)
        if rsi > 70: scores.append(-1)      # overbought
        elif rsi > 60: scores.append(0.5)
        elif rsi > 50: scores.append(0.25)
        elif rsi > 40: scores.append(-0.25)
        elif rsi > 30: scores.append(-0.5)
        else: scores.append(1)              # oversold = potential bounce

        macd_diff = ind.get("macd_diff", 0)
        macd_prev = ind.get("macd_prev_diff", 0)
        if macd_diff > 0 and macd_prev <= 0: scores.append(2)    # crossover bullish
        elif macd_diff < 0 and macd_prev >= 0: scores.append(-2)  # crossover bearish
        elif macd_diff > 0: scores.append(1)
        elif macd_diff < 0: scores.append(-1)

        stoch_k = ind.get("stoch_k", 50)
        stoch_d = ind.get("stoch_d", 50)
        if stoch_k > 80 and stoch_d > 80: scores.append(-1)
        elif stoch_k < 20 and stoch_d < 20: scores.append(1)
        elif stoch_k > stoch_d: scores.append(0.5)
        else: scores.append(-0.5)

        total = sum(scores) / max(len(scores), 1)

        if total >= 0.8: label = "MOMENTUM PUTERNIC BULLISH"
        elif total >= 0.3: label = "MOMENTUM BULLISH"
        elif total <= -0.8: label = "MOMENTUM PUTERNIC BEARISH"
        elif total <= -0.3: label = "MOMENTUM BEARISH"
        else: label = "MOMENTUM NEUTRU"

        return {"score": total, "label": label, "rsi": rsi}

    def _analyze_volatility(self, df: pd.DataFrame, ind: dict) -> dict:
        bb_width = ind.get("bb_width", 0.02)
        bb_pct = ind.get("bb_pct", 0.5)
        atr = ind.get("atr", 0)
        close = float(df["close"].iloc[-1])
        atr_pct = (atr / close * 100) if close > 0 else 0

        if bb_width > 0.1 or atr_pct > 3: label = "VOLATILITATE RIDICATA"
        elif bb_width > 0.04 or atr_pct > 1.5: label = "VOLATILITATE MEDIE"
        else: label = "VOLATILITATE SCAZUTA (COMPRESIE)"

        return {
            "label": label, "bb_width": bb_width,
            "bb_pct": bb_pct, "atr_pct": atr_pct
        }

    def _analyze_volume(self, df: pd.DataFrame, ind: dict) -> dict:
        vol_ratio = ind.get("vol_ratio", 1.0)
        if vol_ratio > 2.0: label = "VOLUM FOARTE RIDICAT (confirmare)"
        elif vol_ratio > 1.3: label = "VOLUM RIDICAT"
        elif vol_ratio < 0.7: label = "VOLUM SCAZUT (neconfirmat)"
        else: label = "VOLUM NORMAL"
        return {"label": label, "ratio": vol_ratio}

    def _detect_patterns(self, df: pd.DataFrame) -> list:
        patterns = []
        if len(df) < 3:
            return patterns

        o = df["open"].values
        h = df["high"].values
        l = df["low"].values
        c = df["close"].values

        # Doji
        body = abs(c[-1] - o[-1])
        candle_range = h[-1] - l[-1]
        if candle_range > 0 and body / candle_range < 0.1:
            patterns.append(("Doji", "NEUTRAL", "Indecizii pe piata"))

        # Hammer
        if candle_range > 0:
            lower_shadow = min(o[-1], c[-1]) - l[-1]
            upper_shadow = h[-1] - max(o[-1], c[-1])
            if lower_shadow > 2 * body and upper_shadow < body * 0.5:
                patterns.append(("Hammer", "BULLISH", "Potential rebound bullish"))

        # Shooting Star
        if candle_range > 0:
            upper_shadow = h[-1] - max(o[-1], c[-1])
            lower_shadow = min(o[-1], c[-1]) - l[-1]
            if upper_shadow > 2 * body and lower_shadow < body * 0.5:
                patterns.append(("Shooting Star", "BEARISH", "Potential rebound bearish"))

        # Engulfing
        if len(df) >= 2:
            prev_body = abs(c[-2] - o[-2])
            curr_body = abs(c[-1] - o[-1])
            if (c[-2] < o[-2] and c[-1] > o[-1] and
                    curr_body > prev_body and o[-1] < c[-2] and c[-1] > o[-2]):
                patterns.append(("Bullish Engulfing", "BULLISH", "Semnal de inversare bullish puternic"))
            elif (c[-2] > o[-2] and c[-1] < o[-1] and
                    curr_body > prev_body and o[-1] > c[-2] and c[-1] < o[-2]):
                patterns.append(("Bearish Engulfing", "BEARISH", "Semnal de inversare bearish puternic"))

        # Three soldiers / crows
        if len(df) >= 3:
            if all(c[i] > o[i] for i in [-3, -2, -1]) and c[-1] > c[-2] > c[-3]:
                patterns.append(("Three White Soldiers", "BULLISH", "Trend bullish puternic confirmat"))
            elif all(c[i] < o[i] for i in [-3, -2, -1]) and c[-1] < c[-2] < c[-3]:
                patterns.append(("Three Black Crows", "BEARISH", "Trend bearish puternic confirmat"))

        return patterns

    def _find_sr_levels(self, df: pd.DataFrame, window: int = 20) -> tuple:
        highs = df["high"].values
        lows = df["low"].values

        resistances = []
        supports = []

        for i in range(window, len(df) - window):
            if highs[i] == max(highs[i-window:i+window]):
                resistances.append(float(highs[i]))
            if lows[i] == min(lows[i-window:i+window]):
                supports.append(float(lows[i]))

        price = float(df["close"].iloc[-1])

        # Keep levels relevant to current price
        resistances = sorted([r for r in set(resistances) if r > price])[:5]
        supports = sorted([s for s in set(supports) if s < price], reverse=True)[:5]

        return supports, resistances

    def _calculate_score(self, trend, momentum, volatility, volume, patterns) -> float:
        score = 50.0

        # Trend contribution
        score += trend["score"] * 20

        # Momentum contribution  
        score += momentum["score"] * 15

        # Volume confirmation
        vol_ratio = volume["ratio"]
        if vol_ratio > 1.5: score += 5
        elif vol_ratio < 0.7: score -= 5

        # Pattern contribution
        for name, direction, desc in patterns:
            if direction == "BULLISH": score += 5
            elif direction == "BEARISH": score -= 5

        return max(0, min(100, score))

    def _generate_signal(self, score, price, ind, supports, resistances, 
                          trend, volatility, symbol, timeframe) -> Signal:
        atr = ind.get("atr", price * 0.01)

        if score >= 65:
            direction = "BUY"
            confidence = min(95, score)
            if score >= 80: strength = "STRONG"
            elif score >= 70: strength = "MODERATE"
            else: strength = "WEAK"

            sl = max(supports[0] if supports else price - 2*atr, price - 2*atr)
            tp1 = price + 1.5 * atr
            tp2 = resistances[0] if resistances else price + 3 * atr
            tp3 = resistances[1] if len(resistances) > 1 else price + 5 * atr
            reason = f"Trend {trend['label']}, scor AI {score:.0f}/100. Momentum pozitiv confirmat."

        elif score <= 35:
            direction = "SELL"
            confidence = min(95, 100 - score)
            if score <= 20: strength = "STRONG"
            elif score <= 30: strength = "MODERATE"
            else: strength = "WEAK"

            sl = min(resistances[0] if resistances else price + 2*atr, price + 2*atr)
            tp1 = price - 1.5 * atr
            tp2 = supports[0] if supports else price - 3 * atr
            tp3 = supports[1] if len(supports) > 1 else price - 5 * atr
            reason = f"Trend {trend['label']}, scor AI {score:.0f}/100. Presiune bearish dominanta."

        else:
            direction = "HOLD"
            confidence = 50 + abs(score - 50)
            strength = "WEAK"
            sl = price - 2 * atr
            tp1 = price + atr
            tp2 = price + 2 * atr
            tp3 = price + 3 * atr
            reason = f"Piata laterala / indecisa. Scor AI {score:.0f}/100. Asteapta confirmare directie."

        risk = abs(price - sl)
        reward = abs(tp2 - price)
        rr = round(reward / risk, 2) if risk > 0 else 0

        return Signal(
            direction=direction,
            confidence=round(confidence, 1),
            strength=strength,
            reason=reason,
            entry=round(price, 6),
            stop_loss=round(sl, 6),
            take_profit_1=round(tp1, 6),
            take_profit_2=round(tp2, 6),
            take_profit_3=round(tp3, 6),
            risk_reward=rr,
            timeframe=timeframe,
        )

    def _generate_summary(self, symbol, price, signal, trend, momentum, 
                           volatility, patterns, score) -> str:
        lines = []
        lines.append(f"═══ ANALIZA ZEUS AI — {symbol} ═══")
        lines.append(f"Pret curent: {price:.6g}")
        lines.append(f"Scor general: {score:.0f}/100")
        lines.append("")
        lines.append(f"📈 TREND: {trend['label']}")
        lines.append(f"⚡ MOMENTUM: {momentum['label']}")
        lines.append(f"🌊 VOLATILITATE: {volatility['label']}")
        lines.append("")
        lines.append(f"🎯 SEMNAL: {signal.direction} ({signal.strength})")
        lines.append(f"   Confidenta: {signal.confidence:.0f}%")
        lines.append(f"   Entry: {signal.entry:.6g}")
        lines.append(f"   Stop Loss: {signal.stop_loss:.6g}")
        lines.append(f"   TP1: {signal.take_profit_1:.6g}")
        lines.append(f"   TP2: {signal.take_profit_2:.6g}")
        lines.append(f"   TP3: {signal.take_profit_3:.6g}")
        lines.append(f"   Risk/Reward: 1:{signal.risk_reward}")
        lines.append("")
        lines.append(f"📌 MOTIVARE: {signal.reason}")
        if patterns:
            lines.append("")
            lines.append("🕯️ PATTERN-URI CANDLESTICK:")
            for name, direction, desc in patterns:
                lines.append(f"   • {name} ({direction}): {desc}")
        return "\n".join(lines)

    def _generate_warnings(self, df, ind, signal) -> list:
        warnings = []
        rsi = ind.get("rsi", 50)

        if rsi > 75:
            warnings.append("⚠️ RSI > 75: Zona de supracumparare extrema. Risc de corectie.")
        elif rsi < 25:
            warnings.append("⚠️ RSI < 25: Zona de supravanzare extrema. Risc de bounce.")

        bb_pct = ind.get("bb_pct", 0.5)
        if bb_pct > 0.95:
            warnings.append("⚠️ Pret la banda superioara Bollinger. Rezistenta puternica.")
        elif bb_pct < 0.05:
            warnings.append("⚠️ Pret la banda inferioara Bollinger. Suport puternic.")

        vol_ratio = ind.get("vol_ratio", 1.0)
        if signal.direction != "HOLD" and vol_ratio < 0.7:
            warnings.append("⚠️ Volum mic. Semnalul nu este bine confirmat de volum.")

        adx = ind.get("adx", 20)
        if adx < 15:
            warnings.append("⚠️ ADX < 15: Piata laterala slaba. Evita tendintele false.")

        if signal.risk_reward < 1.5:
            warnings.append(f"⚠️ Risk/Reward {signal.risk_reward} este sub 1.5. Setup suboptimal.")

        return warnings

    def _insufficient_data_report(self, symbol, df) -> AdviceReport:
        price = float(df["close"].iloc[-1]) if len(df) > 0 else 0
        sig = Signal("HOLD", 0, "WEAK", "Date insuficiente pentru analiza", 
                     price, price, price, price, price, 0, "N/A")
        return AdviceReport(symbol, price, sig, "N/A", "N/A", "N/A", 
                            [], [], {}, "Date insuficiente (min 50 candle)", [], 50)
