"""
Trading Bot — Core Configuration & Constants
"""
import json
import os
from pathlib import Path
from dataclasses import dataclass, field, asdict
from typing import Optional

APP_NAME = "Trading Bot"
APP_VERSION = "2.0.0"
CONFIG_DIR = Path.home() / ".tradingbot"
CONFIG_FILE = CONFIG_DIR / "config.json"
CREDENTIALS_FILE = CONFIG_DIR / "credentials.enc"
LOG_DIR = CONFIG_DIR / "logs"
DB_FILE = CONFIG_DIR / "tradingbot.db"

# Ensure dirs exist
CONFIG_DIR.mkdir(parents=True, exist_ok=True)
LOG_DIR.mkdir(parents=True, exist_ok=True)


@dataclass
class BrokerConfig:
    name: str = ""                # e.g. "binance", "kraken", "alpaca", "interactive_brokers"
    api_key: str = ""
    api_secret: str = ""
    passphrase: str = ""          # some brokers need this
    sandbox: bool = True          # paper trading by default
    base_url: str = ""            # custom endpoint if needed


@dataclass
class AppConfig:
    theme: str = "dark"
    default_symbol: str = "BTC-USD"
    default_timeframe: str = "1D"
    auto_refresh_seconds: int = 60
    max_risk_per_trade_pct: float = 2.0
    max_open_positions: int = 5
    default_leverage: float = 1.0
    sound_alerts: bool = True
    broker: BrokerConfig = field(default_factory=BrokerConfig)
    watchlist: list = field(default_factory=lambda: [
        "BTC-USD", "ETH-USD", "AAPL", "TSLA", "NVDA",
        "EURUSD=X", "GC=F", "^GSPC"
    ])

    def save(self):
        with open(CONFIG_FILE, "w", encoding="utf-8") as f:
            json.dump(asdict(self), f, indent=2, ensure_ascii=False)

    @classmethod
    def load(cls) -> "AppConfig":
        if CONFIG_FILE.exists():
            try:
                with open(CONFIG_FILE, "r", encoding="utf-8") as f:
                    data = json.load(f)
                broker_data = data.pop("broker", {})
                cfg = cls(**{k: v for k, v in data.items() if k in cls.__dataclass_fields__})
                cfg.broker = BrokerConfig(**{k: v for k, v in broker_data.items() if k in BrokerConfig.__dataclass_fields__})
                return cfg
            except Exception:
                pass
        return cls()


# ── Symbol mapping (comprehensive) ──────────────────────────────
SYMBOL_MAP = {
    # Crypto
    "BTC": "BTC-USD", "BITCOIN": "BTC-USD", "ETH": "ETH-USD",
    "ETHEREUM": "ETH-USD", "BNB": "BNB-USD", "SOL": "SOL-USD",
    "XRP": "XRP-USD", "ADA": "ADA-USD", "DOGE": "DOGE-USD",
    "AVAX": "AVAX-USD", "DOT": "DOT-USD", "MATIC": "MATIC-USD",
    "LINK": "LINK-USD", "UNI": "UNI-USD", "ATOM": "ATOM-USD",
    "LTC": "LTC-USD", "SHIB": "SHIB-USD", "NEAR": "NEAR-USD",
    "SUI": "SUI-USD", "ARB": "ARB-USD", "OP": "OP-USD",
    "RNDR": "RNDR-USD", "AAVE": "AAVE-USD", "FIL": "FIL-USD",
    "XMR": "XMR-USD", "TRX": "TRX-USD", "XLM": "XLM-USD",
    # Forex
    "EURUSD": "EURUSD=X", "EUR/USD": "EURUSD=X",
    "GBPUSD": "GBPUSD=X", "GBP/USD": "GBPUSD=X",
    "USDJPY": "USDJPY=X", "USD/JPY": "USDJPY=X",
    "USDCHF": "USDCHF=X", "AUDUSD": "AUDUSD=X",
    "USDCAD": "USDCAD=X", "EURRON": "EURRON=X",
    "USDRON": "USDRON=X", "EURGBP": "EURGBP=X",
    # Commodities
    "GOLD": "GC=F", "XAUUSD": "GC=F", "SILVER": "SI=F",
    "XAGUSD": "SI=F", "OIL": "CL=F", "BRENT": "BZ=F",
    "NATGAS": "NG=F", "COPPER": "HG=F",
    # Indices
    "SP500": "^GSPC", "S&P500": "^GSPC", "SPX": "^GSPC",
    "NASDAQ": "^IXIC", "NDX": "^NDX", "DOW": "^DJI",
    "VIX": "^VIX", "DAX": "^GDAXI", "FTSE": "^FTSE",
    "NIKKEI": "^N225", "RUSSELL": "^RUT",
}

INTERVAL_MAP = {
    "1m": "1m", "5m": "5m", "15m": "15m", "30m": "30m",
    "1h": "1h", "4h": "1h", "1D": "1d", "1W": "1wk", "1M": "1mo",
}

PERIOD_MAP = {
    "1m": "7d", "5m": "60d", "15m": "60d", "30m": "60d",
    "1h": "730d", "4h": "730d", "1D": "5y", "1W": "10y", "1M": "20y",
}

# Broker presets
BROKER_PRESETS = {
    "binance": {
        "name": "binance",
        "base_url": "https://api.binance.com",
        "sandbox_url": "https://testnet.binance.vision",
    },
    "binance_futures": {
        "name": "binance",
        "base_url": "https://fapi.binance.com",
        "sandbox_url": "https://testnet.binancefuture.com",
    },
    "kraken": {
        "name": "kraken",
        "base_url": "https://api.kraken.com",
    },
    "coinbase": {
        "name": "coinbasepro",
        "base_url": "https://api.exchange.coinbase.com",
        "sandbox_url": "https://api-public.sandbox.exchange.coinbase.com",
    },
    "alpaca": {
        "name": "alpaca",
        "base_url": "https://api.alpaca.markets",
        "sandbox_url": "https://paper-api.alpaca.markets",
    },
    "bybit": {
        "name": "bybit",
        "base_url": "https://api.bybit.com",
        "sandbox_url": "https://api-testnet.bybit.com",
    },
    "kucoin": {
        "name": "kucoin",
        "base_url": "https://api.kucoin.com",
        "sandbox_url": "https://openapi-sandbox.kucoin.com",
    },
    "okx": {
        "name": "okx",
        "base_url": "https://www.okx.com",
    },
    "interactive_brokers": {
        "name": "ibkr",
        "base_url": "https://localhost:5000",
    },
    "xtb": {
        "name": "xtb",
        "base_url": "https://xapi.xtb.com",
    },
}
