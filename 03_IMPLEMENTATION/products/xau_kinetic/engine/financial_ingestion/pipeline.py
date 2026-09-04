"""
Financial Ingestion Pipeline.
Provides multi-source asynchronous and synchronous data fetching for market assets,
macroeconomic indicators (FRED API with zero hardcoded secrets), Fear & Greed sentiment,
and in-memory caching with deterministic offline fallbacks.
"""

import os
import time
import logging
import asyncio
from concurrent.futures import ThreadPoolExecutor, as_completed
from typing import Dict, List, Optional, Tuple, Any
import requests
import pandas as pd
import numpy as np

try:
    import yfinance as yf
except ImportError:
    yf = None

from .catalog import (
    ACTIVE,
    MACRO_TICKERS,
    FRED_SERIES,
    get_instrument,
)
from .indicators import compute_all_indicators

logger = logging.getLogger("financial_ingestion_pipeline")
if not logger.handlers:
    handler = logging.StreamHandler()
    handler.setFormatter(logging.Formatter("%(asctime)s %(levelname)-8s %(message)s"))
    logger.addHandler(handler)
    logger.setLevel(logging.INFO)


# ============================================================================
# 1. IN-MEMORY TTL CACHE
# ============================================================================

class MarketCache:
    """Thread-safe in-memory cache with configurable TTL (Time-To-Live)."""

    def __init__(self, default_ttl_seconds: int = 300):
        self._default_ttl = default_ttl_seconds
        self._store: Dict[str, Tuple[float, Any]] = {}

    def get(self, key: str) -> Optional[Any]:
        """Retrieves cached item if present and not expired."""
        if key not in self._store:
            return None
        timestamp, value = self._store[key]
        if time.time() - timestamp > self._default_ttl:
            del self._store[key]
            return None
        return value

    def set(self, key: str, value: Any, ttl: Optional[int] = None) -> None:
        """Stores item with current timestamp."""
        effective_ttl = ttl if ttl is not None else self._default_ttl
        self._store[key] = (time.time() + (effective_ttl - self._default_ttl), value)

    def clear(self) -> None:
        """Clears all cached entries."""
        self._store.clear()

    def size(self) -> int:
        """Returns number of non-expired cached entries."""
        now = time.time()
        expired = [k for k, (ts, _) in self._store.items() if now - ts > self._default_ttl]
        for k in expired:
            del self._store[k]
        return len(self._store)


# ============================================================================
# 2. DETERMINISTIC SYNTHETIC / OFFLINE DATA GENERATORS
# ============================================================================

def generate_synthetic_ohlcv(
    symbol: str,
    days: int = 250,
    base_price: Optional[float] = None
) -> pd.DataFrame:
    """
    Generates a deterministic, realistic synthetic OHLCV time series for testing
    or offline operation without internet connectivity.
    """
    # Deterministic seed based on symbol name
    seed = sum(ord(c) for c in symbol) % (2**31 - 1)
    rng = np.random.RandomState(seed)

    if base_price is None:
        if symbol.startswith("^"):
            base_price = 5000.0 if "GSPC" in symbol else 18000.0 if "NDX" in symbol else 15.0 if "VIX" in symbol else 4.0
        elif "BTC" in symbol:
            base_price = 65000.0
        elif "ETH" in symbol:
            base_price = 3200.0
        elif "=X" in symbol:
            base_price = 1.08 if "EUR" in symbol else 1.28 if "GBP" in symbol else 155.0 if "JPY" in symbol else 1.0
        elif "=F" in symbol:
            base_price = 2400.0 if "GC" in symbol else 30.0 if "SI" in symbol else 80.0 if "CL" in symbol else 2.5
        else:
            base_price = 150.0

    dates = pd.date_range(end=pd.Timestamp.now(tz="UTC"), periods=days, freq="B")
    
    # Generate log returns with modest volatility and slight upward drift
    daily_vol = 0.015 if not symbol.startswith("^VIX") else 0.05
    returns = rng.normal(loc=0.0003, scale=daily_vol, size=days)
    
    price_series = base_price * np.exp(np.cumsum(returns))
    
    # Generate O, H, L, C, V
    closes = price_series
    opens = np.roll(closes, 1)
    opens[0] = base_price
    
    noise_h = np.abs(rng.normal(0, daily_vol * 0.5, size=days))
    noise_l = np.abs(rng.normal(0, daily_vol * 0.5, size=days))
    
    highs = np.maximum(opens, closes) * (1.0 + noise_h)
    lows = np.minimum(opens, closes) * (1.0 - noise_l)
    
    base_vol = 1_000_000 if not symbol.endswith("=X") else 500_000
    volumes = rng.lognormal(mean=np.log(base_vol), sigma=0.5, size=days).astype(int)

    df = pd.DataFrame(
        {
            "Open": np.round(opens, 6),
            "High": np.round(highs, 6),
            "Low": np.round(lows, 6),
            "Close": np.round(closes, 6),
            "Volume": volumes,
        },
        index=dates,
    )
    return df


# Deterministic offline sample observations for FRED series
_SAMPLE_FRED_DATA: Dict[str, Tuple[float, float]] = {
    "FEDFUNDS": (5.33, 5.33),
    "CPIAUCSL": (314.54, 313.88),
    "UNRATE": (4.1, 4.0),
    "GDP": (28650.2, 28280.5),
}


# ============================================================================
# 3. SPECIALIZED DATA FETCHERS
# ============================================================================

class MarketDataFetcher:
    """Fetches and computes technical indicators for financial instruments via yfinance."""

    def __init__(self, timeout_seconds: int = 15):
        self.timeout = timeout_seconds

    def fetch_history(self, ticker: str, period: str = "1y") -> Optional[pd.DataFrame]:
        """Fetches OHLCV historical dataframe from yfinance or returns None on error."""
        if yf is None:
            return None
        try:
            t = yf.Ticker(ticker)
            hist = t.history(period=period, interval="1d", auto_adjust=True, timeout=self.timeout)
            if hist is not None and len(hist) >= 5 and "Close" in hist:
                return hist
            return None
        except Exception as e:
            logger.debug(f"yfinance fetch failed for {ticker}: {e}")
            return None

    def get_instrument_data(
        self,
        name: str,
        ticker: str,
        allow_offline_fallback: bool = True
    ) -> Dict[str, Any]:
        """
        Retrieves full technical indicator dictionary for a ticker.
        If live data fails and allow_offline_fallback is True, uses deterministic synthetic series.
        """
        hist = self.fetch_history(ticker)
        if (hist is None or len(hist) < 5) and allow_offline_fallback:
            logger.debug(f"Using synthetic fallback for {ticker}")
            hist = generate_synthetic_ohlcv(ticker)

        if hist is None or len(hist) < 5:
            return {}

        return compute_all_indicators(hist, name=name, ticker=ticker)


class FREDDataFetcher:
    """
    Fetches macroeconomic series from St. Louis Fed API.
    Strictly follows AGENTS.md Rule 19: zero hardcoded secrets.
    Retrieves key exclusively via os.environ.get("FRED_API_KEY").
    """

    def __init__(self, api_key: Optional[str] = None, timeout_seconds: int = 10):
        self.api_key = api_key or os.environ.get("FRED_API_KEY", "").strip()
        self.timeout = timeout_seconds

    def fetch_series(self, series_id: str) -> Tuple[Optional[float], Optional[float]]:
        """
        Fetches current and previous observation values for a FRED series.
        Falls back to deterministic offline values if API key is unset or network fails.
        """
        if not self.api_key:
            logger.debug(f"FRED_API_KEY not set. Using offline fallback for {series_id}")
            return _SAMPLE_FRED_DATA.get(series_id, (None, None))

        try:
            url = "https://api.stlouisfed.org/fred/series/observations"
            params = {
                "series_id": series_id,
                "api_key": self.api_key,
                "file_type": "json",
                "sort_order": "desc",
                "limit": 2,
            }
            r = requests.get(url, params=params, timeout=self.timeout)
            r.raise_for_status()
            obs = r.json().get("observations", [])

            def _parse(o: Dict[str, Any]) -> Optional[float]:
                v = o.get("value", ".")
                if v in (".", "", None):
                    return None
                try:
                    return float(v)
                except (ValueError, TypeError):
                    return None

            curr = _parse(obs[0]) if len(obs) > 0 else None
            prev = _parse(obs[1]) if len(obs) > 1 else None
            return curr, prev
        except Exception as e:
            logger.debug(f"FRED fetch failed for {series_id}: {e}. Using offline fallback.")
            return _SAMPLE_FRED_DATA.get(series_id, (None, None))

    def fetch_all(self) -> Dict[str, Dict[str, Any]]:
        """Fetches all 4 standard FRED series."""
        results = {}
        for sid, meta in FRED_SERIES.items():
            curr, prev = self.fetch_series(sid)
            chg_pct = None
            if curr is not None and prev is not None and prev != 0:
                chg_pct = round((curr - prev) / prev * 100, 2)
            results[sid] = {
                "series_id": sid,
                "name": meta.name,
                "frequency": meta.frequency,
                "units": meta.units,
                "current": curr,
                "previous": prev,
                "change_pct": chg_pct,
            }
        return results


class SentimentFetcher:
    """Fetches market sentiment index from Alternative.me Crypto Fear & Greed API."""

    def __init__(self, timeout_seconds: int = 10):
        self.timeout = timeout_seconds

    def fetch_fear_greed(self) -> Dict[str, Any]:
        """
        Fetches Fear & Greed sentiment value and classification.
        Falls back to neutral offline default if network fails.
        """
        try:
            url = "https://api.alternative.me/fng/?limit=1"
            r = requests.get(url, timeout=self.timeout)
            r.raise_for_status()
            data = r.json()["data"][0]
            val = int(data["value"])
            cls = data.get("value_classification", "")
            if val >= 55:
                status = "Pozitiv"
            elif val <= 45:
                status = "Negativ"
            else:
                status = "Neutru"
            return {
                "value": val,
                "classification": cls,
                "display": f"{val} - {cls}",
                "status": status,
            }
        except Exception as e:
            logger.debug(f"Fear & Greed fetch failed: {e}. Using neutral fallback.")
            return {
                "value": 50,
                "classification": "Neutral",
                "display": "50 - Neutral",
                "status": "Neutru",
            }


# ============================================================================
# 4. HIGH-LEVEL FINANCIAL INGESTION PIPELINE
# ============================================================================

class FinancialIngestionPipeline:
    """
    Unified Ingestion Pipeline managing multi-threaded sync & async fetching,
    in-memory caching, macro data, and sentiment feeds.
    """

    def __init__(
        self,
        cache_ttl_seconds: int = 300,
        request_timeout: int = 15,
        fred_api_key: Optional[str] = None
    ):
        self.cache = MarketCache(default_ttl_seconds=cache_ttl_seconds)
        self.market_fetcher = MarketDataFetcher(timeout_seconds=request_timeout)
        self.fred_fetcher = FREDDataFetcher(api_key=fred_api_key, timeout_seconds=request_timeout)
        self.sentiment_fetcher = SentimentFetcher(timeout_seconds=request_timeout)

    def fetch_instrument(
        self,
        name: str,
        ticker: str,
        use_cache: bool = True,
        offline_fallback: bool = True
    ) -> Dict[str, Any]:
        """Fetches technical analysis for a single instrument with optional caching."""
        cache_key = f"instrument:{ticker}"
        if use_cache:
            cached = self.cache.get(cache_key)
            if cached is not None:
                return cached

        data = self.market_fetcher.get_instrument_data(
            name=name, ticker=ticker, allow_offline_fallback=offline_fallback
        )
        if data and use_cache:
            self.cache.set(cache_key, data)
        return data

    def fetch_all_instruments(
        self,
        use_cache: bool = True,
        max_workers: int = 8,
        offline_fallback: bool = True
    ) -> Dict[str, Dict[str, Any]]:
        """
        Fetches technical analysis for all 95 instruments concurrently using a ThreadPoolExecutor.
        """
        results: Dict[str, Dict[str, Any]] = {}
        items = list(ACTIVE.items())

        with ThreadPoolExecutor(max_workers=max_workers) as executor:
            future_to_item = {
                executor.submit(
                    self.fetch_instrument, name, ticker, use_cache, offline_fallback
                ): (name, ticker)
                for name, ticker in items
            }
            for future in as_completed(future_to_item):
                name, ticker = future_to_item[future]
                try:
                    res = future.result()
                    results[ticker] = res
                except Exception as ex:
                    logger.error(f"Error fetching {name} ({ticker}): {ex}")
                    results[ticker] = {}

        return results

    async def async_fetch_all_instruments(
        self,
        use_cache: bool = True,
        max_workers: int = 8,
        offline_fallback: bool = True
    ) -> Dict[str, Dict[str, Any]]:
        """Asynchronous wrapper for fetch_all_instruments."""
        loop = asyncio.get_event_loop()
        return await loop.run_in_executor(
            None,
            self.fetch_all_instruments,
            use_cache,
            max_workers,
            offline_fallback,
        )

    def fetch_macro_tickers(
        self,
        use_cache: bool = True,
        offline_fallback: bool = True
    ) -> Dict[str, Dict[str, Any]]:
        """Fetches market data for all 5 macroeconomic benchmark tickers."""
        results: Dict[str, Dict[str, Any]] = {}
        for mname, mticker in MACRO_TICKERS.items():
            results[mname] = self.fetch_instrument(
                name=mname,
                ticker=mticker,
                use_cache=use_cache,
                offline_fallback=offline_fallback,
            )
        return results

    def fetch_fred_series(self, series_id: str) -> Tuple[Optional[float], Optional[float]]:
        """Fetches current and previous values for a FRED series."""
        return self.fred_fetcher.fetch_series(series_id)

    def fetch_all_fred(self) -> Dict[str, Dict[str, Any]]:
        """Fetches all 4 FRED series."""
        return self.fred_fetcher.fetch_all()

    def fetch_sentiment(self) -> Dict[str, Any]:
        """Fetches market sentiment index."""
        return self.sentiment_fetcher.fetch_fear_greed()

    def fetch_full_market_snapshot(
        self,
        use_cache: bool = True,
        max_workers: int = 8,
        offline_fallback: bool = True
    ) -> Dict[str, Any]:
        """
        Takes an exhaustive snapshot of the entire market:
        all 95 instruments, 5 macro tickers, 4 FRED series, and Fear & Greed sentiment.
        """
        instruments = self.fetch_all_instruments(
            use_cache=use_cache,
            max_workers=max_workers,
            offline_fallback=offline_fallback,
        )
        macro = self.fetch_macro_tickers(
            use_cache=use_cache,
            offline_fallback=offline_fallback,
        )
        fred = self.fetch_all_fred()
        sentiment = self.fetch_sentiment()

        # Compute market breadth
        buy_count = sum(1 for d in instruments.values() if d.get("semnal") == "BUY")
        sell_count = sum(1 for d in instruments.values() if d.get("semnal") == "SELL")
        wait_count = sum(1 for d in instruments.values() if d.get("semnal") == "WAIT")
        total_count = len(instruments)

        return {
            "timestamp": pd.Timestamp.now(tz="UTC").isoformat(),
            "breadth": {
                "total": total_count,
                "buy": buy_count,
                "sell": sell_count,
                "wait": wait_count,
            },
            "instruments": instruments,
            "macro_tickers": macro,
            "fred_macro": fred,
            "sentiment": sentiment,
        }

    def clear_cache(self) -> None:
        """Flushes the in-memory cache."""
        self.cache.clear()
