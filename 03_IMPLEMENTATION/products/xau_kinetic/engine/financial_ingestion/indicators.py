"""
Quantitative Technical Indicators & Narrative Engine.
Pure mathematical indicator calculations for market analysis:
RSI, MACD, Moving Averages, Bollinger Bands, ATR, Stochastic, Momentum,
Relative Volume (RVOL), Support/Resistance, Confluence Scoring, Dynamic ATR SL/TP,
and Institutional Educational Narrative Generation.
"""

from typing import Dict, Optional, Tuple
import pandas as pd
import numpy as np


def safe_float(val: object, default: float = 0.0) -> float:
    """Safely converts a value to float, handling None, NaN, and conversion errors."""
    if val is None:
        return default
    try:
        v = float(val)
        return default if pd.isna(v) or np.isneginf(v) or np.isposinf(v) else v
    except (ValueError, TypeError):
        return default


def fmt_price(x: Optional[float], decimals: int = 4) -> str:
    """Formats price with commas and standard decimal precision."""
    if x is None:
        return ""
    try:
        if pd.isna(x):
            return ""
        return f"{float(x):,.{decimals}f}"
    except (ValueError, TypeError):
        return ""


def fmt_pct(val: Optional[float], decimals: int = 2) -> str:
    """Formats percentage change with +/- sign."""
    if val is None:
        return "N/A"
    try:
        if pd.isna(val):
            return "N/A"
        return f"{float(val):+.{decimals}f}%"
    except (ValueError, TypeError):
        return "N/A"


def rr_value(entry: Optional[float], sl: Optional[float], tp: Optional[float]) -> Optional[float]:
    """Calculates risk-to-reward ratio value."""
    try:
        if entry is None or sl is None or tp is None:
            return None
        e, s, t = float(entry), float(sl), float(tp)
        risk = abs(e - s)
        reward = abs(t - e)
        if risk == 0:
            return None
        return reward / risk
    except (ValueError, TypeError, ZeroDivisionError):
        return None


def rr_text(entry: Optional[float], sl: Optional[float], tp: Optional[float]) -> str:
    """Formats risk-to-reward ratio string."""
    v = rr_value(entry, sl, tp)
    return f"{v:.2f}x" if v is not None else "N/A"


# ============================================================================
# 1. TECHNICAL INDICATORS
# ============================================================================

def calc_rsi(prices: pd.Series, period: int = 14) -> float:
    """
    Computes Relative Strength Index (RSI-14).
    Uses standard rolling mean formula with 50.0 fallback.
    """
    try:
        if prices is None or len(prices) < period + 1:
            return 50.0
        clean_prices = pd.Series(pd.to_numeric(prices, errors="coerce")).dropna()
        if len(clean_prices) < period + 1 or clean_prices.std() == 0:
            return 50.0

        delta = clean_prices.diff()
        gain = delta.clip(lower=0)
        loss = (-delta).clip(lower=0)
        avg_g = gain.rolling(window=period, min_periods=period).mean()
        avg_l = loss.rolling(window=period, min_periods=period).mean()

        rs = avg_g / avg_l.replace(0, 1e-10)
        rsi = 100 - (100 / (1 + rs))
        res = rsi.iloc[-1]
        return 50.0 if pd.isna(res) else round(float(res), 2)
    except Exception:
        return 50.0


def map_rsi_status(rsi: float) -> str:
    """Maps numerical RSI value into qualitative market regime status."""
    try:
        r = float(rsi)
        if r < 30:
            return "Presiune excesiva vanzare"
        if r < 45:
            return "Presiune moderata vanzare"
        if r <= 55:
            return "Echilibru"
        if r <= 70:
            return "Momentum ascendent"
        return "Presiune excesiva cumparare"
    except (ValueError, TypeError):
        return "N/A"


def calc_macd(
    prices: pd.Series,
    fast: int = 12,
    slow: int = 26,
    signal_period: int = 9
) -> Dict[str, object]:
    """
    Computes MACD line, signal line, histogram, and detects crossover events.
    """
    default_res = {"macd": 0.0, "signal": 0.0, "histogram": 0.0, "cross": "N/A"}
    try:
        if prices is None or len(prices) < slow:
            return default_res
        clean_prices = pd.to_numeric(prices, errors="coerce").dropna()
        if len(clean_prices) < slow:
            return default_res

        ema12 = clean_prices.ewm(span=fast, adjust=False).mean()
        ema26 = clean_prices.ewm(span=slow, adjust=False).mean()
        macd = ema12 - ema26
        signal = macd.ewm(span=signal_period, adjust=False).mean()
        hist = macd - signal

        m = float(macd.iloc[-1])
        s = float(signal.iloc[-1])
        h = float(hist.iloc[-1])
        prev_h = float(hist.iloc[-2]) if len(hist) >= 2 else h

        if m > s and prev_h < 0 <= h:
            cross = "Impuls pozitiv nou"
        elif m > s and h >= 0:
            cross = "Impuls pozitiv activ"
        elif m < s and prev_h > 0 >= h:
            cross = "Impuls negativ nou"
        else:
            cross = "Impuls negativ activ"

        return {
            "macd": round(m, 6),
            "signal": round(s, 6),
            "histogram": round(h, 6),
            "cross": cross,
        }
    except Exception:
        return default_res


def calc_ma(prices: pd.Series) -> Dict[str, object]:
    """
    Computes SMA20, SMA50, SMA200, and evaluates Golden/Death Cross and trend posture.
    """
    def _ma(n: int) -> Optional[float]:
        if prices is None or len(prices) < n:
            return None
        clean = pd.to_numeric(prices, errors="coerce").dropna()
        if len(clean) < n:
            return None
        v = clean.rolling(n).mean().iloc[-1]
        return None if pd.isna(v) else round(float(v), 6)

    ma20 = _ma(20)
    ma50 = _ma(50)
    ma200 = _ma(200)

    if ma50 is not None and ma200 is not None:
        if ma50 > ma200:
            macross = "Golden Cross"
        elif ma50 < ma200:
            macross = "Death Cross"
        else:
            macross = "Neutru"
    else:
        macross = "Neutru"

    trend = "Sideways"
    if prices is not None and len(prices) > 0 and ma50 is not None:
        current_price = safe_float(prices.iloc[-1])
        if current_price > ma50 * 1.01:
            trend = "Bullish"
        elif current_price < ma50 * 0.99:
            trend = "Bearish"
        else:
            trend = "Sideways"

    return {
        "ma20": ma20,
        "ma50": ma50,
        "ma200": ma200,
        "macross": macross,
        "trend": trend,
    }


def calc_bollinger(prices: pd.Series, period: int = 20, num_std: float = 2.0) -> Dict[str, Optional[float]]:
    """
    Computes Bollinger Bands (SMA20 +/- 2 std dev) and band width.
    """
    default_res = {"bb_sup": None, "bb_inf": None, "bb_mid": None, "bb_width": None}
    try:
        if prices is None or len(prices) < period:
            return default_res
        clean = pd.to_numeric(prices, errors="coerce").dropna()
        if len(clean) < period:
            return default_res

        m = clean.rolling(period).mean()
        std = clean.rolling(period).std()
        sup = m + num_std * std
        inf = m - num_std * std

        bb_mid = round(float(m.iloc[-1]), 6) if pd.notna(m.iloc[-1]) else None
        bb_sup = round(float(sup.iloc[-1]), 6) if pd.notna(sup.iloc[-1]) else None
        bb_inf = round(float(inf.iloc[-1]), 6) if pd.notna(inf.iloc[-1]) else None
        bb_width = round(float(bb_sup - bb_inf), 6) if bb_sup is not None and bb_inf is not None else None

        return {
            "bb_mid": bb_mid,
            "bb_sup": bb_sup,
            "bb_inf": bb_inf,
            "bb_width": bb_width,
        }
    except Exception:
        return default_res


def calc_atr(hist: pd.DataFrame, period: int = 14) -> float:
    """
    Computes Average True Range (ATR-14) from High, Low, and Close prices.
    """
    try:
        if hist is None or len(hist) < period + 1:
            return 0.0
        high = pd.to_numeric(hist["High"], errors="coerce") if "High" in hist.columns else pd.Series(0.0, index=hist.index)
        low = pd.to_numeric(hist["Low"], errors="coerce") if "Low" in hist.columns else pd.Series(0.0, index=hist.index)
        close_prev = pd.to_numeric(hist["Close"], errors="coerce").shift(1) if "Close" in hist.columns else pd.Series(0.0, index=hist.index)

        tr = pd.concat([
            high - low,
            (high - close_prev).abs(),
            (low - close_prev).abs()
        ], axis=1).max(axis=1)

        atr = tr.rolling(period).mean().iloc[-1]
        return 0.0 if pd.isna(atr) else round(float(atr), 6)
    except Exception:
        return 0.0


def calc_stochastic(hist: pd.DataFrame, period: int = 14, smooth_d: int = 3) -> Dict[str, float]:
    """
    Computes Fast Stochastic Oscillator (%K and %D).
    """
    default_res = {"stoch_k": 50.0, "stoch_d": 50.0}
    try:
        if hist is None or len(hist) < period:
            return default_res
        high = pd.to_numeric(hist["High"], errors="coerce") if "High" in hist.columns else pd.Series(0.0, index=hist.index)
        low = pd.to_numeric(hist["Low"], errors="coerce") if "Low" in hist.columns else pd.Series(0.0, index=hist.index)
        close = pd.to_numeric(hist["Close"], errors="coerce") if "Close" in hist.columns else pd.Series(0.0, index=hist.index)

        low14 = low.rolling(period).min()
        high14 = high.rolling(period).max()
        denom = (high14 - low14).replace(0, 1e-10)

        k = ((close - low14) / denom * 100)
        d = k.rolling(smooth_d).mean()

        sk = round(float(k.iloc[-1]), 2) if pd.notna(k.iloc[-1]) else 50.0
        sd = round(float(d.iloc[-1]), 2) if pd.notna(d.iloc[-1]) else 50.0
        return {"stoch_k": sk, "stoch_d": sd}
    except Exception:
        return default_res


def calc_momentum(prices: pd.Series, period: int = 10) -> float:
    """Computes N-day percentage momentum."""
    try:
        if prices is None or len(prices) <= period:
            return 0.0
        clean = pd.to_numeric(prices, errors="coerce").dropna()
        if len(clean) <= period:
            return 0.0
        mom = float(clean.pct_change(period).iloc[-1]) * 100
        return 0.0 if pd.isna(mom) else round(mom, 2)
    except Exception:
        return 0.0


def calc_rvol(volumes: pd.Series, period: int = 20) -> float:
    """Computes Relative Volume (RVOL) against the 20-period moving average volume."""
    try:
        if volumes is None or len(volumes) == 0:
            return 1.0
        clean_vol = pd.to_numeric(volumes, errors="coerce").fillna(0)
        if len(clean_vol) == 0:
            return 1.0
        volum = float(clean_vol.iloc[-1])
        avg_vol = float(clean_vol.tail(period).mean()) if len(clean_vol) >= period else volum
        if avg_vol <= 0:
            return 1.0
        return round(volum / avg_vol, 2)
    except Exception:
        return 1.0


def calc_support_resistance(hist: pd.DataFrame, period: int = 20) -> Dict[str, float]:
    """Calculates 20-day Support (lowest low) and Resistance (highest high)."""
    try:
        if hist is None or len(hist) == 0:
            return {"support": 0.0, "resistance": 0.0}
        lows = pd.to_numeric(hist["Low"], errors="coerce").dropna() if "Low" in hist.columns else pd.Series([], dtype=float)
        highs = pd.to_numeric(hist["High"], errors="coerce").dropna() if "High" in hist.columns else pd.Series([], dtype=float)
        supp = float(lows.tail(period).min()) if len(lows) > 0 else 0.0
        res = float(highs.tail(period).max()) if len(highs) > 0 else 0.0
        return {
            "support": round(supp, 6) if not pd.isna(supp) else 0.0,
            "resistance": round(res, 6) if not pd.isna(res) else 0.0,
        }
    except Exception:
        return {"support": 0.0, "resistance": 0.0}


# ============================================================================
# 2. CONFLUENCE SIGNAL & RISK PARAMETER ENGINE
# ============================================================================

def calc_signal(
    rsi: float,
    macd_cross: str,
    ma_cross: str,
    rvol: float
) -> Tuple[str, int, int]:
    """
    Synthesizes multiple technical factors into a composite Confluence Score (-5 to +5).
    Returns (signal: "BUY" | "SELL" | "WAIT", confluences: 0..5, raw_score: -5..+5).
    """
    score = 0
    r = safe_float(rsi, 50.0)

    # 1. RSI Score
    if r < 35:
        score += 2
    elif r < 45:
        score += 1
    elif r > 75:
        score -= 2
    elif r > 65:
        score -= 1

    # 2. MACD Cross Score
    mc = str(macd_cross).lower()
    if "impuls pozitiv nou" in mc:
        score += 2
    elif "impuls pozitiv activ" in mc:
        score += 1
    elif "impuls negativ nou" in mc:
        score -= 2
    elif "impuls negativ activ" in mc:
        score -= 1

    # 3. MA Cross Score
    mx = str(ma_cross).lower()
    if "golden cross" in mx:
        score += 2
    elif "death cross" in mx:
        score -= 2

    # 4. RVOL Confirmation
    rv = safe_float(rvol, 1.0)
    if rv > 1.5:
        score += 1
    elif rv < 0.6:
        score -= 1

    confluences = min(abs(score), 5)
    if score >= 3:
        semnal = "BUY"
    elif score <= -3:
        semnal = "SELL"
    else:
        semnal = "WAIT"

    return semnal, confluences, score


def calc_sl_tp(
    price: float,
    atr: float,
    signal: str,
    risk_mult: float = 1.5,
    reward_mult: float = 3.0
) -> Tuple[Optional[float], Optional[float], Optional[float]]:
    """
    Computes dynamic ATR Stop Loss (1.5x ATR) and Take Profit (3.0x ATR) targeting R/R = 2.0x.
    Returns (stop_loss, take_profit, rr_ratio).
    """
    if price <= 0 or atr <= 0:
        return None, None, None

    sig_upper = str(signal).strip().upper()
    if sig_upper == "BUY":
        sl = round(price - risk_mult * atr, 6)
        tp = round(price + reward_mult * atr, 6)
        rr = rr_value(price, sl, tp)
        return sl, tp, rr
    elif sig_upper == "SELL":
        sl = round(price + risk_mult * atr, 6)
        tp = round(price - reward_mult * atr, 6)
        rr = rr_value(price, sl, tp)
        return sl, tp, rr
    else:
        return None, None, None


def calc_probability(confluences: int, rvol: float) -> float:
    """Calculates statistical trade probability percentage based on confluences and RVOL."""
    c = max(0, min(confluences, 5))
    rv_bonus = 5 if safe_float(rvol, 1.0) > 1.2 else 0
    prob = min(90.0, 35.0 + (c * 10.0) + rv_bonus)
    return float(prob)


# ============================================================================
# 3. UNIFIED INDICATOR CALCULATION SUITE
# ============================================================================

def compute_all_indicators(
    hist: pd.DataFrame,
    name: str = "",
    ticker: str = ""
) -> Dict[str, object]:
    """
    Computes all 10 quantitative technical indicators and confluence scores from historical OHLCV data.
    """
    if hist is None or len(hist) < 5:
        return {}

    closes = pd.to_numeric(hist["Close"], errors="coerce").dropna() if "Close" in hist.columns else pd.Series([], dtype=float)
    if len(closes) < 5:
        return {}

    price = round(float(closes.iloc[-1]), 6)
    o_price = round(safe_float(hist["Open"].iloc[-1], price), 6) if "Open" in hist.columns else price
    h_price = round(safe_float(hist["High"].iloc[-1], price), 6) if "High" in hist.columns else price
    l_price = round(safe_float(hist["Low"].iloc[-1], price), 6) if "Low" in hist.columns else price

    def _pct(idx: int) -> float:
        """Percentage change of the latest close against the close at negative
        position `idx`.

        Boundary contract: `closes.iloc[idx]` is valid as soon as
        `len(closes) >= abs(idx)` -- for a series of exactly `abs(idx)` bars,
        `iloc[idx]` addresses the FIRST element. The guard below must therefore
        be `>=`, not `>`.
        """
        try:
            if len(closes) >= abs(idx):
                prev = float(closes.iloc[idx])
                return round((price - prev) / prev * 100, 4) if prev else 0.0
            return 0.0
        except Exception:
            return 0.0

    var_zi = _pct(-2)
    var_sapt = _pct(-6)
    var_luna = _pct(-21)

    vol_ser = pd.to_numeric(hist["Volume"], errors="coerce").fillna(0) if "Volume" in hist.columns else pd.Series(0, index=hist.index)
    volum = int(vol_ser.iloc[-1]) if len(vol_ser) > 0 else 0
    avg_vol = int(vol_ser.tail(20).mean()) if len(vol_ser) >= 20 else volum
    rvol = calc_rvol(vol_ser, period=20)

    rsi = calc_rsi(closes)
    rsi_status = map_rsi_status(rsi)
    macd_res = calc_macd(closes)
    ma_res = calc_ma(closes)
    bb_res = calc_bollinger(closes)
    atr = calc_atr(hist)
    stoch = calc_stochastic(hist)
    mom10 = calc_momentum(closes, period=10)
    sr_res = calc_support_resistance(hist, period=20)

    semnal, confluente, score = calc_signal(
        rsi, str(macd_res["cross"]), str(ma_res["macross"]), rvol
    )
    sl, tp, rr = calc_sl_tp(price, atr, semnal)
    prob = calc_probability(confluente, rvol)

    now = pd.Timestamp.now(tz="UTC")

    return {
        "name": name or ticker,
        "ticker": ticker,
        "data": now.strftime("%Y-%m-%d"),
        "timestamp": now.strftime("%Y-%m-%d %H:%M:%S UTC"),
        "deschidere": o_price,
        "maxim": h_price,
        "minim": l_price,
        "inchidere": price,
        "var_zi_pct": var_zi,
        "var_sapt_pct": var_sapt,
        "var_luna_pct": var_luna,
        "volum": volum,
        "avg_vol_20": avg_vol,
        "rvol": rvol,
        "rsi": rsi,
        "rsi_status": rsi_status,
        "macd": macd_res["macd"],
        "macd_signal": macd_res["signal"],
        "macd_hist": macd_res["histogram"],
        "macd_cross": macd_res["cross"],
        "ma20": ma_res["ma20"],
        "ma50": ma_res["ma50"],
        "ma200": ma_res["ma200"],
        "macross": ma_res["macross"],
        "trend": ma_res["trend"],
        "bb_mid": bb_res["bb_mid"],
        "bb_sup": bb_res["bb_sup"],
        "bb_inf": bb_res["bb_inf"],
        "bb_width": bb_res["bb_width"],
        "atr": atr,
        "stoch_k": stoch["stoch_k"],
        "stoch_d": stoch["stoch_d"],
        "momentum_10z": mom10,
        "semnal": semnal,
        "score": score,
        "confluente": confluente,
        "sl": sl,
        "tp": tp,
        "rr_ratio": rr,
        "probabilitate": prob,
        "support": sr_res["support"],
        "resistance": sr_res["resistance"],
    }


# ============================================================================
# 4. INSTITUTIONAL NARRATIVE & HEURISTIC ENGINE
# ============================================================================

def explica_miscare(d: Dict[str, object]) -> str:
    """Generates an institutional narrative explaining price movement and technical structure."""
    name = str(d.get("name", d.get("ticker", "Activul")))
    vz = safe_float(d.get("var_zi_pct"), 0.0)
    rvol = safe_float(d.get("rvol"), 1.0)
    rsi = safe_float(d.get("rsi"), 50.0)
    macd_c = str(d.get("macd_cross", ""))
    macross = str(d.get("macross", ""))
    bb_sup = d.get("bb_sup")
    bb_inf = d.get("bb_inf")
    price = safe_float(d.get("inchidere"), 0.0)
    ma50 = d.get("ma50")
    ma200 = d.get("ma200")
    sl_v = d.get("sl")
    tp_v = d.get("tp")
    semnal = str(d.get("semnal", "WAIT"))

    dir_txt = "crescut" if vz > 0 else "scazut"
    intens = "semnificativ" if abs(vz) > 2 else "moderat" if abs(vz) > 0.5 else "marginal"
    vol_txt = "exceptionale" if rvol > 1.5 else "normale" if rvol > 0.7 else "scazute"
    rsi_txt = (
        "supravandut — potential rebound" if rsi < 30
        else "zona neutra" if rsi < 60
        else "zona supracumparare — prudenta recomandata"
    )

    bb_txt = ""
    if bb_sup is not None and bb_inf is not None and price > 0:
        bb_sup_f = float(bb_sup)
        bb_inf_f = float(bb_inf)
        if price > bb_sup_f * 0.99:
            bb_txt = "Pretul testeaza banda superioara Bollinger (potentiala rezistenta)."
        elif price < bb_inf_f * 1.01:
            bb_txt = "Pretul testeaza banda inferioara Bollinger (potential suport)."
        else:
            bb_txt = "Pretul se afla in interiorul benzilor Bollinger."

    ma_txt = ""
    if ma50 is not None and ma200 is not None:
        ma_txt = f"Raport MA50/MA200: {macross}."

    sl_tp_txt = ""
    if sl_v is not None and tp_v is not None:
        sl_tp_txt = f"SL recomandat: {fmt_price(float(sl_v))} | TP: {fmt_price(float(tp_v))}."

    return (
        f"{name} a {dir_txt} {intens} cu {abs(vz):.2f}% in sedinta curenta. "
        f"Volumele sunt {vol_txt} (RVOL={rvol:.1f}x). "
        f"RSI({rsi:.1f}): {rsi_txt}. "
        f"MACD: {macd_c}. "
        f"{ma_txt} {bb_txt} "
        f"Semnal tehnic: {semnal}. {sl_tp_txt}"
    ).strip()


def identifica_oportunitate(d: Dict[str, object]) -> str:
    """Synthesizes high-probability market opportunities or risk warnings."""
    semnal = str(d.get("semnal", "WAIT"))
    rsi = safe_float(d.get("rsi"), 50.0)
    rvol = safe_float(d.get("rvol"), 1.0)
    macross = str(d.get("macross", ""))
    name = str(d.get("name", d.get("ticker", "Activul")))

    if semnal == "BUY" and rsi < 45:
        return f"✅ OPORTUNITATE: {name} prezinta semnal BUY cu RSI scazut ({rsi:.0f}) — potential entry atractiv."
    elif semnal == "BUY" and "golden" in macross.lower():
        return f"✅ OPORTUNITATE: Golden Cross confirmat pe {name} cu semnal BUY activ."
    elif semnal == "SELL" and rsi > 70:
        return f"⚠️ RISC: {name} supraevaluat (RSI={rsi:.0f}) cu semnal SELL — potential short sau exit long."
    elif rvol > 2.0:
        return f"🔥 ATENTIE: Volum exceptional pe {name} (RVOL={rvol:.1f}x) — miscare semnificativa posibila."
    else:
        return f"⏸ {name} in zona de asteptare (WAIT) — se monitorizeaza confirmare semnal."


def extrage_lectie(d: Dict[str, object]) -> str:
    """Distills actionable, repeatable trading heuristics from the asset state."""
    rsi = safe_float(d.get("rsi"), 50.0)
    macross = str(d.get("macross", ""))
    macd_c = str(d.get("macd_cross", ""))
    rvol = safe_float(d.get("rvol"), 1.0)

    if "golden" in macross.lower():
        return "Lectie: Golden Cross (MA50>MA200) este un semnal bullish pe termen lung, confirmat de MACD."
    elif "death" in macross.lower():
        return "Lectie: Death Cross (MA50<MA200) semnaleaza potentiala tendinta descendenta — fii prudent cu pozitiile long."
    elif rsi < 30:
        return "Lectie: RSI sub 30 indica supravanzare — pretul poate reveni, dar asteapta confirmare MACD."
    elif rsi > 70:
        return "Lectie: RSI peste 70 indica supracumparare — risc de corectie. NU intra in long la extrema RSI."
    elif "pozitiv nou" in macd_c.lower():
        return "Lectie: Crossover MACD pozitiv nou = potential inceput de tendinta ascendenta — oportunitate entry."
    elif rvol > 1.5:
        return "Lectie: Volum ridicat confirma miscarile de pret — un breakout cu volum este mai credibil."
    else:
        return "Lectie: In absenta unui semnal clar, cash-ul este o pozitie valida. Rabdarea este o virtute in trading."
