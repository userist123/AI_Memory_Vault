"""Pure-Python / numpy indicators. All functions tolerate short series
by returning None when not enough data is available.
"""
from __future__ import annotations

from typing import List, Optional, Sequence, Tuple

try:
    import numpy as np
except ImportError:  # pragma: no cover
    np = None  # type: ignore


def sma(values: Sequence[float], period: int) -> Optional[float]:
    if len(values) < period or period <= 0:
        return None
    return float(sum(values[-period:]) / period)


def ema(values: Sequence[float], period: int) -> Optional[float]:
    if len(values) < period or period <= 0:
        return None
    k = 2.0 / (period + 1.0)
    e = float(sum(values[:period]) / period)
    for v in values[period:]:
        e = v * k + e * (1 - k)
    return e


def ema_series(values: Sequence[float], period: int) -> List[float]:
    if len(values) < period or period <= 0:
        return []
    k = 2.0 / (period + 1.0)
    e = float(sum(values[:period]) / period)
    out = [e]
    for v in values[period:]:
        e = v * k + e * (1 - k)
        out.append(e)
    return out


def rsi(values: Sequence[float], period: int = 14) -> Optional[float]:
    if len(values) < period + 1:
        return None
    gains = 0.0
    losses = 0.0
    for i in range(-period, 0):
        diff = values[i] - values[i - 1]
        if diff >= 0:
            gains += diff
        else:
            losses -= diff
    if losses == 0:
        return 100.0
    rs = (gains / period) / (losses / period)
    return 100.0 - (100.0 / (1.0 + rs))


def atr(highs: Sequence[float], lows: Sequence[float],
        closes: Sequence[float], period: int = 14) -> Optional[float]:
    n = len(closes)
    if n < period + 1:
        return None
    trs: List[float] = []
    for i in range(1, n):
        tr = max(highs[i] - lows[i],
                 abs(highs[i] - closes[i - 1]),
                 abs(lows[i] - closes[i - 1]))
        trs.append(tr)
    return float(sum(trs[-period:]) / period)


def macd(values: Sequence[float], fast: int = 12, slow: int = 26,
         signal: int = 9) -> Optional[Tuple[float, float, float]]:
    if len(values) < slow + signal:
        return None
    ef = ema_series(values, fast)
    es = ema_series(values, slow)
    n = min(len(ef), len(es))
    if n < signal:
        return None
    macd_line = [ef[-n + i] - es[-n + i] for i in range(n)]
    sig = ema_series(macd_line, signal)
    if not sig:
        return None
    m = macd_line[-1]
    s = sig[-1]
    return m, s, m - s


def bollinger(values: Sequence[float], period: int = 20,
              mult: float = 2.0) -> Optional[Tuple[float, float, float]]:
    if len(values) < period:
        return None
    window = list(values[-period:])
    mean = sum(window) / period
    var = sum((v - mean) ** 2 for v in window) / period
    sd = var ** 0.5
    return mean, mean + mult * sd, mean - mult * sd


def stochastic(highs: Sequence[float], lows: Sequence[float],
               closes: Sequence[float], k: int = 14,
               d: int = 3) -> Optional[Tuple[float, float]]:
    if len(closes) < k + d:
        return None
    ks: List[float] = []
    for i in range(-k - d + 1, 0 + 1):
        hh = max(highs[i - k + 1:i + 1] if i != 0 else highs[-k:])
        ll = min(lows[i - k + 1:i + 1] if i != 0 else lows[-k:])
        c = closes[i] if i != 0 else closes[-1]
        if hh == ll:
            ks.append(50.0)
        else:
            ks.append(100.0 * (c - ll) / (hh - ll))
    k_val = ks[-1]
    d_val = sum(ks[-d:]) / d
    return k_val, d_val


def adx(highs: Sequence[float], lows: Sequence[float],
        closes: Sequence[float], period: int = 14) -> Optional[float]:
    n = len(closes)
    if n < period * 2:
        return None
    plus_dm = []
    minus_dm = []
    trs = []
    for i in range(1, n):
        up = highs[i] - highs[i - 1]
        dn = lows[i - 1] - lows[i]
        plus_dm.append(up if (up > dn and up > 0) else 0.0)
        minus_dm.append(dn if (dn > up and dn > 0) else 0.0)
        trs.append(max(highs[i] - lows[i],
                       abs(highs[i] - closes[i - 1]),
                       abs(lows[i] - closes[i - 1])))
    atr_v = sum(trs[-period:]) / period or 1e-9
    pdi = 100.0 * (sum(plus_dm[-period:]) / period) / atr_v
    mdi = 100.0 * (sum(minus_dm[-period:]) / period) / atr_v
    s = pdi + mdi
    if s == 0:
        return 0.0
    dx = 100.0 * abs(pdi - mdi) / s
    return dx


def vwap(highs: Sequence[float], lows: Sequence[float],
         closes: Sequence[float], volumes: Sequence[float]) -> Optional[float]:
    if not closes or len(volumes) != len(closes):
        return None
    num = sum(((h + l + c) / 3.0) * v
              for h, l, c, v in zip(highs, lows, closes, volumes))
    den = sum(volumes) or 1e-9
    return num / den


def fractal_high(highs: Sequence[float]) -> bool:
    if len(highs) < 5:
        return False
    h = highs
    i = -3
    return h[i] > h[i - 1] and h[i] > h[i - 2] and h[i] > h[i + 1] and h[i] > h[i + 2]


def fractal_low(lows: Sequence[float]) -> bool:
    if len(lows) < 5:
        return False
    l = lows
    i = -3
    return l[i] < l[i - 1] and l[i] < l[i - 2] and l[i] < l[i + 1] and l[i] < l[i + 2]


def is_bullish_engulfing(opens, closes) -> bool:
    if len(closes) < 2:
        return False
    return (closes[-2] < opens[-2]
            and closes[-1] > opens[-1]
            and closes[-1] > opens[-2]
            and opens[-1] < closes[-2])


def is_bearish_engulfing(opens, closes) -> bool:
    if len(closes) < 2:
        return False
    return (closes[-2] > opens[-2]
            and closes[-1] < opens[-1]
            and closes[-1] < opens[-2]
            and opens[-1] > closes[-2])


def is_pinbar(opens, highs, lows, closes) -> Optional[str]:
    if not closes:
        return None
    o, h, l, c = opens[-1], highs[-1], lows[-1], closes[-1]
    body = abs(c - o) or 1e-9
    upper = h - max(c, o)
    lower = min(c, o) - l
    if lower > 2 * body and upper < body:
        return "BULL"
    if upper > 2 * body and lower < body:
        return "BEAR"
    return None
