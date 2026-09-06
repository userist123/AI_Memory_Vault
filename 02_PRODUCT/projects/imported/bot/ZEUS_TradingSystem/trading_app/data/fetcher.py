"""
ZEUS Data Fetcher - Descarca date din surse multiple
Yahoo Finance (actiuni, forex, crypto) + fallback CSV
"""

import yfinance as yf
import pandas as pd
import numpy as np
from datetime import datetime, timedelta


# Mapare simboluri populare
SYMBOL_MAP = {
    # Crypto
    "BTC": "BTC-USD", "BITCOIN": "BTC-USD",
    "ETH": "ETH-USD", "ETHEREUM": "ETH-USD",
    "BNB": "BNB-USD", "SOL": "SOL-USD",
    "XRP": "XRP-USD", "ADA": "ADA-USD",
    "DOGE": "DOGE-USD", "AVAX": "AVAX-USD",
    "DOT": "DOT-USD", "MATIC": "MATIC-USD",
    "LINK": "LINK-USD", "UNI": "UNI-USD",
    # Forex (perechi majore)
    "EURUSD": "EURUSD=X", "EUR/USD": "EURUSD=X",
    "GBPUSD": "GBPUSD=X", "GBP/USD": "GBPUSD=X",
    "USDJPY": "USDJPY=X", "USD/JPY": "USDJPY=X",
    "USDCHF": "USDCHF=X", "AUDUSD": "AUDUSD=X",
    "NZDUSD": "NZDUSD=X", "USDCAD": "USDCAD=X",
    "XAUUSD": "GC=F", "GOLD": "GC=F",
    "XAGUSD": "SI=F", "SILVER": "SI=F",
    # Indici
    "SP500": "^GSPC", "S&P500": "^GSPC",
    "NASDAQ": "^IXIC", "DOW": "^DJI",
    "VIX": "^VIX",
    # Actiuni populare
    "AAPL": "AAPL", "APPLE": "AAPL",
    "MSFT": "MSFT", "GOOGL": "GOOGL",
    "AMZN": "AMZN", "TSLA": "TSLA",
    "META": "META", "NVDA": "NVDA",
    "AMD": "AMD", "NFLX": "NFLX",
}

INTERVAL_MAP = {
    "1m": "1m", "5m": "5m", "15m": "15m",
    "30m": "30m", "1h": "1h", "4h": "1h",
    "1D": "1d", "1W": "1wk", "1M": "1mo",
}

PERIOD_MAP = {
    "1m": "7d", "5m": "60d", "15m": "60d",
    "30m": "60d", "1h": "730d", "4h": "730d",
    "1D": "5y", "1W": "10y", "1M": "20y",
}


class DataFetcher:
    def __init__(self):
        self.cache = {}

    def resolve_symbol(self, symbol: str) -> str:
        sym_upper = symbol.strip().upper()
        return SYMBOL_MAP.get(sym_upper, symbol.strip())

    def fetch(self, symbol: str, interval: str = "1D", 
              start: str = None, end: str = None) -> pd.DataFrame:
        """
        Descarca date OHLCV
        Returns DataFrame cu coloane: Open, High, Low, Close, Volume
        """
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

            # Standardizeaza coloanele
            df = df[["Open", "High", "Low", "Close", "Volume"]].copy()
            df.dropna(inplace=True)
            df.index = pd.to_datetime(df.index)

            # Remove timezone info for simplicity
            if df.index.tz is not None:
                df.index = df.index.tz_localize(None)

            return df

        except Exception as e:
            raise ConnectionError(f"Eroare la descarcarea datelor pentru {symbol}: {e}")

    def get_info(self, symbol: str) -> dict:
        """Informatii generale despre simbol"""
        yf_symbol = self.resolve_symbol(symbol)
        try:
            ticker = yf.Ticker(yf_symbol)
            info = ticker.info
            return {
                "name": info.get("longName", yf_symbol),
                "sector": info.get("sector", "N/A"),
                "market_cap": info.get("marketCap", 0),
                "currency": info.get("currency", "USD"),
                "exchange": info.get("exchange", "N/A"),
                "52w_high": info.get("fiftyTwoWeekHigh", 0),
                "52w_low": info.get("fiftyTwoWeekLow", 0),
                "pe_ratio": info.get("trailingPE", 0),
                "dividend_yield": info.get("dividendYield", 0),
            }
        except Exception:
            return {"name": yf_symbol, "sector": "N/A"}

    def generate_demo_data(self, symbol: str = "DEMO", bars: int = 500) -> pd.DataFrame:
        """Genereaza date demo pentru testare fara internet"""
        np.random.seed(42)
        dates = pd.date_range(end=datetime.now(), periods=bars, freq="D")
        price = 100.0
        prices = [price]
        for _ in range(bars - 1):
            change = np.random.normal(0.0002, 0.02)
            price = max(price * (1 + change), 0.01)
            prices.append(price)

        df = pd.DataFrame(index=dates)
        df["Close"] = prices
        df["Open"] = df["Close"].shift(1).fillna(df["Close"] * 0.999)
        df["High"] = df[["Open", "Close"]].max(axis=1) * (1 + abs(np.random.normal(0, 0.005, bars)))
        df["Low"] = df[["Open", "Close"]].min(axis=1) * (1 - abs(np.random.normal(0, 0.005, bars)))
        df["Volume"] = np.random.lognormal(15, 0.5, bars).astype(int)
        return df
