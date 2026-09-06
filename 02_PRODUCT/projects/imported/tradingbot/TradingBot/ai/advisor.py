"""
Trading Bot — AI Advisor Engine
25+ technical indicators, pattern detection, multi-timeframe analysis,
risk management, and position sizing.
"""
import pandas as pd
import numpy as np
from dataclasses import dataclass, field
from typing import Optional, List, Tuple
import logging

log = logging.getLogger("tradingbot.ai")

try:
    from ta.trend import MACD, EMAIndicator, SMAIndicator, ADXIndicator, IchimokuIndicator
    from ta.momentum import RSIIndicator, StochasticOscillator, WilliamsRIndicator
    from ta.volatility import BollingerBands, AverageTrueRange, KeltnerChannel
    from ta.volume import OnBalanceVolumeIndicator, MFIIndicator
    HAS_TA = True
except ImportError:
    HAS_TA = False
    log.warning("ta library not installed. pip install ta")


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
    position_size_pct: float = 0.0   # recommended % of portfolio


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
    score: float
    patterns: list = field(default_factory=list)
    ichimoku_cloud: str = ""
    market_regime: str = ""


class AIAdvisor:
    """
    Motor de analiza tehnica cu 25+ indicatori, pattern detection,
    Ichimoku Cloud, market regime detection, si position sizing.
    """

    def __init__(self):
        self.weights = {
            "trend": 0.28, "momentum": 0.22, "volatility": 0.18,
            "volume": 0.15, "pattern": 0.10, "ichimoku": 0.07,
        }

    def analyze(self, df: pd.DataFrame, symbol: str, timeframe: str = "1D",
                portfolio_value: float = 10000, max_risk_pct: float = 2.0) -> AdviceReport:
        if not HAS_TA:
            return self._no_ta_report(symbol, df)
        if len(df) < 50:
            return self._insufficient_data_report(symbol, df)

        df = df.copy()
        df.columns = [c.lower() for c in df.columns]

        ind = self._calculate_indicators(df)
        trend = self._analyze_trend(df, ind)
        momentum = self._analyze_momentum(ind)
        volatility = self._analyze_volatility(df, ind)
        volume = self._analyze_volume(df, ind)
        patterns = self._detect_patterns(df)
        ichimoku = self._analyze_ichimoku(df, ind)
        supports, resistances = self._find_sr_levels(df)
        regime = self._detect_regime(df, ind, trend, volatility)

        score = self._calculate_score(trend, momentum, volatility, volume, patterns, ichimoku)
        price = float(df["close"].iloc[-1])
        signal = self._generate_signal(
            score, price, ind, supports, resistances,
            trend, volatility, symbol, timeframe,
            portfolio_value, max_risk_pct
        )

        summary = self._generate_summary(
            symbol, price, signal, trend, momentum, volatility, patterns, score, regime, ichimoku
        )
        warnings = self._generate_warnings(df, ind, signal)

        return AdviceReport(
            symbol=symbol, price=price, signal=signal,
            trend=trend["label"], momentum=momentum["label"],
            volatility=volatility["label"],
            support_levels=supports[:3], resistance_levels=resistances[:3],
            key_indicators={
                "RSI": round(ind.get("rsi", 50), 2),
                "MACD": round(ind.get("macd", 0), 4),
                "MACD_Signal": round(ind.get("macd_signal", 0), 4),
                "MACD_Hist": round(ind.get("macd_diff", 0), 4),
                "BB_Upper": round(ind.get("bb_upper", price), 2),
                "BB_Lower": round(ind.get("bb_lower", price), 2),
                "BB_Width": round(ind.get("bb_width", 0), 4),
                "BB_%B": round(ind.get("bb_pct", 0.5), 4),
                "EMA_9": round(ind.get("ema9", price), 2),
                "EMA_20": round(ind.get("ema20", price), 2),
                "EMA_50": round(ind.get("ema50", price), 2),
                "EMA_200": round(ind.get("ema200", price), 2),
                "SMA_50": round(ind.get("sma50", price), 2),
                "SMA_200": round(ind.get("sma200", price), 2),
                "ADX": round(ind.get("adx", 20), 2),
                "+DI": round(ind.get("adx_pos", 10), 2),
                "-DI": round(ind.get("adx_neg", 10), 2),
                "Stoch_K": round(ind.get("stoch_k", 50), 2),
                "Stoch_D": round(ind.get("stoch_d", 50), 2),
                "Williams_%R": round(ind.get("williams_r", -50), 2),
                "ATR": round(ind.get("atr", 0), 4),
                "ATR_%": round(ind.get("atr_pct", 0), 2),
                "OBV_Trend": ind.get("obv_trend", "N/A"),
                "MFI": round(ind.get("mfi", 50), 2),
                "Vol_Ratio": round(ind.get("vol_ratio", 1.0), 2),
                "Ichimoku": ichimoku.get("label", "N/A"),
            },
            summary=summary, warnings=warnings,
            score=round(score, 1), patterns=patterns,
            ichimoku_cloud=ichimoku.get("label", ""),
            market_regime=regime,
        )

    def _calculate_indicators(self, df: pd.DataFrame) -> dict:
        ind = {}
        close = df["close"]
        high = df["high"]
        low = df["low"]
        vol = df["volume"]
        price = float(close.iloc[-1])

        # RSI
        try:
            ind["rsi"] = float(RSIIndicator(close=close, window=14).rsi().iloc[-1])
        except Exception:
            ind["rsi"] = 50.0

        # MACD
        try:
            m = MACD(close=close)
            ind["macd"] = float(m.macd().iloc[-1])
            ind["macd_signal"] = float(m.macd_signal().iloc[-1])
            ind["macd_diff"] = float(m.macd_diff().iloc[-1])
            ind["macd_prev_diff"] = float(m.macd_diff().iloc[-2]) if len(df) > 1 else 0
        except Exception:
            ind["macd"] = ind["macd_signal"] = ind["macd_diff"] = ind["macd_prev_diff"] = 0.0

        # Bollinger Bands
        try:
            bb = BollingerBands(close=close, window=20, window_dev=2)
            ind["bb_upper"] = float(bb.bollinger_hband().iloc[-1])
            ind["bb_lower"] = float(bb.bollinger_lband().iloc[-1])
            ind["bb_mid"] = float(bb.bollinger_mavg().iloc[-1])
            ind["bb_width"] = float(bb.bollinger_wband().iloc[-1])
            ind["bb_pct"] = float(bb.bollinger_pband().iloc[-1])
        except Exception:
            ind["bb_upper"] = price * 1.02
            ind["bb_lower"] = price * 0.98
            ind["bb_mid"] = price
            ind["bb_width"] = 0.04
            ind["bb_pct"] = 0.5

        # EMAs & SMAs
        for w in [9, 20, 50, 100, 200]:
            try:
                ind[f"ema{w}"] = float(EMAIndicator(close=close, window=w).ema_indicator().iloc[-1])
            except Exception:
                ind[f"ema{w}"] = price
        for w in [20, 50, 200]:
            try:
                ind[f"sma{w}"] = float(SMAIndicator(close=close, window=w).sma_indicator().iloc[-1])
            except Exception:
                ind[f"sma{w}"] = price

        # Stochastic
        try:
            s = StochasticOscillator(high=high, low=low, close=close)
            ind["stoch_k"] = float(s.stoch().iloc[-1])
            ind["stoch_d"] = float(s.stoch_signal().iloc[-1])
        except Exception:
            ind["stoch_k"] = ind["stoch_d"] = 50.0

        # Williams %R
        try:
            ind["williams_r"] = float(WilliamsRIndicator(high=high, low=low, close=close).williams_r().iloc[-1])
        except Exception:
            ind["williams_r"] = -50.0

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
            ind["atr"] = float(AverageTrueRange(high=high, low=low, close=close).average_true_range().iloc[-1])
            ind["atr_pct"] = (ind["atr"] / price * 100) if price > 0 else 0
        except Exception:
            ind["atr"] = price * 0.01
            ind["atr_pct"] = 1.0

        # Ichimoku
        try:
            ichi = IchimokuIndicator(high=high, low=low)
            ind["ichi_a"] = float(ichi.ichimoku_a().iloc[-1])
            ind["ichi_b"] = float(ichi.ichimoku_b().iloc[-1])
            ind["ichi_base"] = float(ichi.ichimoku_base_line().iloc[-1])
            ind["ichi_conv"] = float(ichi.ichimoku_conversion_line().iloc[-1])
        except Exception:
            ind["ichi_a"] = ind["ichi_b"] = ind["ichi_base"] = ind["ichi_conv"] = price

        # OBV
        try:
            obv = OnBalanceVolumeIndicator(close=close, volume=vol).on_balance_volume()
            obv_sma = obv.rolling(20).mean()
            ind["obv_trend"] = "BULLISH" if float(obv.iloc[-1]) > float(obv_sma.iloc[-1]) else "BEARISH"
        except Exception:
            ind["obv_trend"] = "N/A"

        # MFI
        try:
            ind["mfi"] = float(MFIIndicator(high=high, low=low, close=close, volume=vol).money_flow_index().iloc[-1])
        except Exception:
            ind["mfi"] = 50.0

        # Volume ratio
        try:
            vol_ma = vol.rolling(20).mean()
            ind["vol_ratio"] = float(vol.iloc[-1] / vol_ma.iloc[-1]) if float(vol_ma.iloc[-1]) > 0 else 1.0
        except Exception:
            ind["vol_ratio"] = 1.0

        return ind

    def _analyze_trend(self, df, ind):
        close = float(df["close"].iloc[-1])
        scores = []

        for ema_key in ["ema20", "ema50", "ema200"]:
            ema = ind.get(ema_key, close)
            if close > ema:
                scores.append(1)
            elif close < ema:
                scores.append(-1)
            else:
                scores.append(0)

        # Golden / Death cross
        if ind.get("ema20", 0) > ind.get("ema50", 0) > ind.get("ema200", 0):
            scores.append(2)
        elif ind.get("ema20", 0) < ind.get("ema50", 0) < ind.get("ema200", 0):
            scores.append(-2)

        # SMA 50/200 cross
        if ind.get("sma50", 0) > ind.get("sma200", 0):
            scores.append(1)
        elif ind.get("sma50", 0) < ind.get("sma200", 0):
            scores.append(-1)

        adx = ind.get("adx", 20)
        if adx > 25:
            if ind.get("adx_pos", 0) > ind.get("adx_neg", 0):
                scores.append(1)
            else:
                scores.append(-1)

        total = sum(scores) / max(len(scores), 1)
        if total >= 0.6: label = "BULLISH PUTERNIC"
        elif total >= 0.2: label = "BULLISH"
        elif total <= -0.6: label = "BEARISH PUTERNIC"
        elif total <= -0.2: label = "BEARISH"
        else: label = "NEUTRAL / LATERAL"

        return {"score": total, "label": label, "adx": adx}

    def _analyze_momentum(self, ind):
        scores = []
        rsi = ind.get("rsi", 50)
        if rsi > 70: scores.append(-1)
        elif rsi > 60: scores.append(0.5)
        elif rsi > 50: scores.append(0.25)
        elif rsi > 40: scores.append(-0.25)
        elif rsi > 30: scores.append(-0.5)
        else: scores.append(1)

        macd_diff = ind.get("macd_diff", 0)
        macd_prev = ind.get("macd_prev_diff", 0)
        if macd_diff > 0 and macd_prev <= 0: scores.append(2)
        elif macd_diff < 0 and macd_prev >= 0: scores.append(-2)
        elif macd_diff > 0: scores.append(1)
        elif macd_diff < 0: scores.append(-1)

        stoch_k = ind.get("stoch_k", 50)
        stoch_d = ind.get("stoch_d", 50)
        if stoch_k > 80 and stoch_d > 80: scores.append(-1)
        elif stoch_k < 20 and stoch_d < 20: scores.append(1)
        elif stoch_k > stoch_d: scores.append(0.5)
        else: scores.append(-0.5)

        # Williams %R
        wr = ind.get("williams_r", -50)
        if wr > -20: scores.append(-0.5)
        elif wr < -80: scores.append(0.5)

        # MFI
        mfi = ind.get("mfi", 50)
        if mfi > 80: scores.append(-0.5)
        elif mfi < 20: scores.append(0.5)

        total = sum(scores) / max(len(scores), 1)
        if total >= 0.8: label = "MOMENTUM PUTERNIC BULLISH"
        elif total >= 0.3: label = "MOMENTUM BULLISH"
        elif total <= -0.8: label = "MOMENTUM PUTERNIC BEARISH"
        elif total <= -0.3: label = "MOMENTUM BEARISH"
        else: label = "MOMENTUM NEUTRU"

        return {"score": total, "label": label, "rsi": rsi}

    def _analyze_volatility(self, df, ind):
        bb_width = ind.get("bb_width", 0.02)
        atr_pct = ind.get("atr_pct", 1.0)
        if bb_width > 0.1 or atr_pct > 3: label = "VOLATILITATE RIDICATA"
        elif bb_width > 0.04 or atr_pct > 1.5: label = "VOLATILITATE MEDIE"
        else: label = "VOLATILITATE SCAZUTA (COMPRESIE)"
        return {"label": label, "bb_width": bb_width, "bb_pct": ind.get("bb_pct", 0.5), "atr_pct": atr_pct}

    def _analyze_volume(self, df, ind):
        ratio = ind.get("vol_ratio", 1.0)
        if ratio > 2.0: label = "VOLUM FOARTE RIDICAT (confirmare)"
        elif ratio > 1.3: label = "VOLUM RIDICAT"
        elif ratio < 0.7: label = "VOLUM SCAZUT (neconfirmat)"
        else: label = "VOLUM NORMAL"
        return {"label": label, "ratio": ratio}

    def _analyze_ichimoku(self, df, ind):
        price = float(df["close"].iloc[-1])
        a = ind.get("ichi_a", price)
        b = ind.get("ichi_b", price)
        cloud_top = max(a, b)
        cloud_bot = min(a, b)

        if price > cloud_top:
            label = "PESTE CLOUD (Bullish)"
            score = 1
        elif price < cloud_bot:
            label = "SUB CLOUD (Bearish)"
            score = -1
        else:
            label = "IN CLOUD (Indecis)"
            score = 0

        # Conversion / base cross
        conv = ind.get("ichi_conv", price)
        base = ind.get("ichi_base", price)
        if conv > base:
            score += 0.5
        elif conv < base:
            score -= 0.5

        return {"label": label, "score": score}

    def _detect_patterns(self, df):
        patterns = []
        if len(df) < 3:
            return patterns

        o = df["open"].values
        h = df["high"].values
        l = df["low"].values
        c = df["close"].values

        body = abs(c[-1] - o[-1])
        rng = h[-1] - l[-1]

        if rng > 0:
            if body / rng < 0.1:
                patterns.append(("Doji", "NEUTRAL", "Indecizii pe piata"))

            lower_shadow = min(o[-1], c[-1]) - l[-1]
            upper_shadow = h[-1] - max(o[-1], c[-1])
            if lower_shadow > 2 * body and upper_shadow < body * 0.5 and body > 0:
                patterns.append(("Hammer", "BULLISH", "Potential rebound bullish"))
            if upper_shadow > 2 * body and lower_shadow < body * 0.5 and body > 0:
                patterns.append(("Shooting Star", "BEARISH", "Potential rebound bearish"))

        if len(df) >= 2:
            prev_body = abs(c[-2] - o[-2])
            curr_body = abs(c[-1] - o[-1])
            if c[-2] < o[-2] and c[-1] > o[-1] and curr_body > prev_body and o[-1] < c[-2] and c[-1] > o[-2]:
                patterns.append(("Bullish Engulfing", "BULLISH", "Inversare bullish puternic"))
            elif c[-2] > o[-2] and c[-1] < o[-1] and curr_body > prev_body and o[-1] > c[-2] and c[-1] < o[-2]:
                patterns.append(("Bearish Engulfing", "BEARISH", "Inversare bearish puternic"))

        if len(df) >= 3:
            if all(c[i] > o[i] for i in [-3, -2, -1]) and c[-1] > c[-2] > c[-3]:
                patterns.append(("Three White Soldiers", "BULLISH", "Trend bullish confirmat"))
            elif all(c[i] < o[i] for i in [-3, -2, -1]) and c[-1] < c[-2] < c[-3]:
                patterns.append(("Three Black Crows", "BEARISH", "Trend bearish confirmat"))

            # Morning/Evening star
            if len(df) >= 3:
                b1 = abs(c[-3] - o[-3])
                b2 = abs(c[-2] - o[-2])
                b3 = abs(c[-1] - o[-1])
                if c[-3] < o[-3] and b2 < b1 * 0.3 and c[-1] > o[-1] and c[-1] > (o[-3] + c[-3]) / 2:
                    patterns.append(("Morning Star", "BULLISH", "Inversare de fond"))
                elif c[-3] > o[-3] and b2 < b1 * 0.3 and c[-1] < o[-1] and c[-1] < (o[-3] + c[-3]) / 2:
                    patterns.append(("Evening Star", "BEARISH", "Inversare de top"))

        return patterns

    def _find_sr_levels(self, df, window: int = 20):
        highs = df["high"].values
        lows = df["low"].values
        resistances, supports = [], []

        for i in range(window, len(df) - window):
            if highs[i] == max(highs[i - window:i + window]):
                resistances.append(float(highs[i]))
            if lows[i] == min(lows[i - window:i + window]):
                supports.append(float(lows[i]))

        price = float(df["close"].iloc[-1])
        resistances = sorted(set(r for r in resistances if r > price))[:5]
        supports = sorted(set(s for s in supports if s < price), reverse=True)[:5]
        return supports, resistances

    def _detect_regime(self, df, ind, trend, vol):
        adx = ind.get("adx", 20)
        bb_width = ind.get("bb_width", 0.04)
        if adx > 30 and abs(trend["score"]) > 0.4:
            return "TRENDING"
        elif bb_width < 0.03:
            return "SQUEEZE (pre-breakout)"
        elif adx < 15:
            return "RANGING / LATERAL"
        else:
            return "TRANSITIE"

    def _calculate_score(self, trend, momentum, vol, volume, patterns, ichimoku):
        score = 50.0
        score += trend["score"] * 20
        score += momentum["score"] * 15
        ratio = volume["ratio"]
        if ratio > 1.5: score += 5
        elif ratio < 0.7: score -= 5
        for _, direction, _ in patterns:
            if direction == "BULLISH": score += 5
            elif direction == "BEARISH": score -= 5
        score += ichimoku.get("score", 0) * 5
        return max(0, min(100, score))

    def _generate_signal(self, score, price, ind, supports, resistances,
                          trend, vol, symbol, timeframe, portfolio, max_risk_pct):
        atr = ind.get("atr", price * 0.01)

        if score >= 65:
            direction = "BUY"
            confidence = min(95, score)
            strength = "STRONG" if score >= 80 else "MODERATE" if score >= 70 else "WEAK"
            sl = max(supports[0] if supports else price - 2 * atr, price - 2 * atr)
            tp1 = price + 1.5 * atr
            tp2 = resistances[0] if resistances else price + 3 * atr
            tp3 = resistances[1] if len(resistances) > 1 else price + 5 * atr
            reason = f"Trend {trend['label']}, scor {score:.0f}/100. Momentum pozitiv."
        elif score <= 35:
            direction = "SELL"
            confidence = min(95, 100 - score)
            strength = "STRONG" if score <= 20 else "MODERATE" if score <= 30 else "WEAK"
            sl = min(resistances[0] if resistances else price + 2 * atr, price + 2 * atr)
            tp1 = price - 1.5 * atr
            tp2 = supports[0] if supports else price - 3 * atr
            tp3 = supports[1] if len(supports) > 1 else price - 5 * atr
            reason = f"Trend {trend['label']}, scor {score:.0f}/100. Presiune bearish."
        else:
            direction = "HOLD"
            confidence = 50 + abs(score - 50)
            strength = "WEAK"
            sl = price - 2 * atr
            tp1 = price + atr
            tp2 = price + 2 * atr
            tp3 = price + 3 * atr
            reason = f"Piata laterala. Scor {score:.0f}/100. Asteapta confirmare."

        risk = abs(price - sl)
        reward = abs(tp2 - price)
        rr = round(reward / risk, 2) if risk > 0 else 0

        # Position sizing (fixed fractional)
        risk_amount = portfolio * (max_risk_pct / 100)
        position_size_pct = round((risk_amount / risk / price * 100) if risk > 0 and price > 0 else 0, 2)
        position_size_pct = min(position_size_pct, 100)

        return Signal(
            direction=direction, confidence=round(confidence, 1),
            strength=strength, reason=reason,
            entry=round(price, 6), stop_loss=round(sl, 6),
            take_profit_1=round(tp1, 6), take_profit_2=round(tp2, 6),
            take_profit_3=round(tp3, 6), risk_reward=rr,
            timeframe=timeframe, position_size_pct=position_size_pct,
        )

    def _generate_summary(self, symbol, price, signal, trend, momentum, vol, patterns, score, regime, ichimoku):
        lines = [
            f"═══ ANALIZA TRADING BOT — {symbol} ═══",
            f"Pret: {price:.6g}   |   Scor: {score:.0f}/100   |   Regim: {regime}",
            "",
            f"TREND: {trend['label']}  (ADX: {trend['adx']:.1f})",
            f"MOMENTUM: {momentum['label']}  (RSI: {momentum['rsi']:.1f})",
            f"VOLATILITATE: {vol['label']}",
            f"ICHIMOKU: {ichimoku.get('label', 'N/A')}",
            "",
            f"SEMNAL: {signal.direction} ({signal.strength})",
            f"  Confidenta: {signal.confidence:.0f}%",
            f"  Entry: {signal.entry:.6g}",
            f"  Stop Loss: {signal.stop_loss:.6g}",
            f"  TP1: {signal.take_profit_1:.6g}",
            f"  TP2: {signal.take_profit_2:.6g}",
            f"  TP3: {signal.take_profit_3:.6g}",
            f"  Risk/Reward: 1:{signal.risk_reward}",
            f"  Pozitie recomandata: {signal.position_size_pct:.1f}% din portofoliu",
            "",
            f"MOTIVARE: {signal.reason}",
        ]
        if patterns:
            lines.append("\nPATTERN-URI:")
            for name, direction, desc in patterns:
                lines.append(f"  • {name} ({direction}): {desc}")
        return "\n".join(lines)

    def _generate_warnings(self, df, ind, signal):
        warnings = []
        rsi = ind.get("rsi", 50)
        if rsi > 75: warnings.append("⚠ RSI > 75: Supracumparare extrema")
        elif rsi < 25: warnings.append("⚠ RSI < 25: Supravanzare extrema")
        bb_pct = ind.get("bb_pct", 0.5)
        if bb_pct > 0.95: warnings.append("⚠ Pret la banda superioara Bollinger")
        elif bb_pct < 0.05: warnings.append("⚠ Pret la banda inferioara Bollinger")
        if signal.direction != "HOLD" and ind.get("vol_ratio", 1) < 0.7:
            warnings.append("⚠ Volum mic — semnal neconfirmat")
        if ind.get("adx", 20) < 15:
            warnings.append("⚠ ADX < 15: Piata laterala slaba")
        if signal.risk_reward < 1.5 and signal.direction != "HOLD":
            warnings.append(f"⚠ R:R {signal.risk_reward} sub 1.5 — setup suboptimal")
        mfi = ind.get("mfi", 50)
        if mfi > 80: warnings.append("⚠ MFI > 80: Flux de bani excesiv")
        elif mfi < 20: warnings.append("⚠ MFI < 20: Uscaciune lichiditate")
        return warnings

    def _insufficient_data_report(self, symbol, df):
        price = float(df["close"].iloc[-1]) if len(df) > 0 else 0
        sig = Signal("HOLD", 0, "WEAK", "Date insuficiente (min 50 candle)", price, price, price, price, price, 0, "N/A")
        return AdviceReport(symbol, price, sig, "N/A", "N/A", "N/A", [], [], {}, "Date insuficiente", [], 50)

    def _no_ta_report(self, symbol, df):
        price = float(df["close"].iloc[-1]) if len(df) > 0 else 0
        sig = Signal("HOLD", 0, "WEAK", "Libraria 'ta' nu este instalata", price, price, price, price, price, 0, "N/A")
        return AdviceReport(symbol, price, sig, "N/A", "N/A", "N/A", [], [], {}, "pip install ta", [], 50)
