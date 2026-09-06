"""
Trading Bot — Data Fetcher
Real-time + historical data via yfinance.
"""
import logging
import yfinance as yf
import pandas as pd
import numpy as np
from datetime import datetime, timedelta
from core.config import SYMBOL_MAP, INTERVAL_MAP, PERIOD_MAP

log = logging.getLogger("tradingbot.data")


class DataFetcher:
    def __init__(self):
        self.cache = {}
        self._last_fetch = {}

    def resolve_symbol(self, symbol: str) -> str:
        sym = symbol.strip().upper().replace(" ", "")
        return SYMBOL_MAP.get(sym, symbol.strip())

    def fetch(self, symbol: str, interval: str = "1D",
              start: str = None, end: str = None) -> pd.DataFrame:
        yf_symbol = self.resolve_symbol(symbol)
        yf_interval = INTERVAL_MAP.get(interval, "1d")
        period = PERIOD_MAP.get(interval, "5y")

        try:
            ticker = yf.Ticker(yf_symbol)
            if start and end:
                df = ticker.history(start=start, end=end, interval=yf_interval)
            else:
                df = ticker.history(period=period, interval=yf_interval)

            if df.empty:
                raise ValueError(f"Nu s-au gasit date pentru {yf_symbol}")

            df = df[["Open", "High", "Low", "Close", "Volume"]].copy()
            df.dropna(inplace=True)
            df.index = pd.to_datetime(df.index)
            if df.index.tz is not None:
                df.index = df.index.tz_localize(None)

            self.cache[f"{yf_symbol}_{interval}"] = df
            self._last_fetch[f"{yf_symbol}_{interval}"] = datetime.now()
            return df

        except Exception as e:
            raise ConnectionError(f"Eroare date {symbol}: {e}")

    def get_info(self, symbol: str) -> dict:
        yf_symbol = self.resolve_symbol(symbol)
        try:
            info = yf.Ticker(yf_symbol).info
            return {
                "name": info.get("longName") or info.get("shortName", yf_symbol),
                "sector": info.get("sector", "N/A"),
                "market_cap": info.get("marketCap", 0),
                "currency": info.get("currency", "USD"),
                "exchange": info.get("exchange", "N/A"),
                "52w_high": info.get("fiftyTwoWeekHigh", 0),
                "52w_low": info.get("fiftyTwoWeekLow", 0),
                "pe_ratio": info.get("trailingPE"),
                "dividend_yield": info.get("dividendYield"),
                "beta": info.get("beta"),
                "avg_volume": info.get("averageVolume", 0),
            }
        except Exception:
            return {"name": yf_symbol}

    def get_live_price(self, symbol: str) -> float:
        yf_symbol = self.resolve_symbol(symbol)
        try:
            t = yf.Ticker(yf_symbol)
            data = t.history(period="1d", interval="1m")
            if not data.empty:
                return float(data["Close"].iloc[-1])
            data = t.history(period="5d")
            if not data.empty:
                return float(data["Close"].iloc[-1])
        except Exception:
            pass
        return 0.0

    def fetch_multiple(self, symbols: list, interval: str = "1D") -> dict:
        results = {}
        for sym in symbols:
            try:
                df = self.fetch(sym, interval)
                results[sym] = df
            except Exception as e:
                log.warning(f"Eroare fetch {sym}: {e}")
        return results
