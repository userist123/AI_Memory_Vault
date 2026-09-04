"""
Pytest Fixtures and Test Infrastructure for Financial Research & Trading Journal E2E Suites.
Provides isolated storage engines, mock market feeds, FRED macro responses,
trade record generators, and cryptographic audit verifiers.
"""

import os
import tempfile
import shutil
import sqlite3
import hashlib
import json
import pytest
from datetime import datetime, timezone
from typing import Dict, List, Any, Optional

from memory_controller.storage.sqlite_engine import SQLiteStorageEngine
from memory_controller.storage.file_engine import FileStorageEngine
from memory_controller.controller import MemoryController, Lifecycle, StorageEngine
from memory_controller.authorizer import Principal, DefaultAuthorizer
from memory_controller.audit.logger import AuditLogger


@pytest.fixture
def temp_vault_dir():
    """Provides a fresh isolated Vault folder structure."""
    temp_dir = tempfile.mkdtemp(prefix="vault_e2e_")
    for folder in [
        "00_CORE",
        "01_KNOWLEDGE",
        "01_KNOWLEDGE/FINANCE",
        "02_PROJECTS",
        "03_PROCEDURES",
        "04_MEMORY",
        "04_MEMORY/DECISIONS/FINANCE",
        "04_MEMORY/EXPERIENCES/FINANCE",
        "04_MEMORY/ERRORS/FINANCE",
        "04_MEMORY/LESSONS/FINANCE",
        "05_RESOURCES",
        "05_RESOURCES/FINANCE",
        "06_INBOX/RAW_IMPORTS",
        "99_SYSTEM",
    ]:
        os.makedirs(os.path.join(temp_dir, folder), exist_ok=True)
    yield temp_dir
    shutil.rmtree(temp_dir, ignore_errors=True)


@pytest.fixture
def temp_sqlite_db():
    """Provides a temporary SQLite database configured in WAL mode."""
    fd, path = tempfile.mkstemp(suffix=".sqlite3")
    os.close(fd)
    if os.path.exists(path):
        os.remove(path)
    engine = SQLiteStorageEngine(path, wal_mode=True, timeout=5.0)
    yield path, engine
    engine.close()
    for ext in ["", "-wal", "-shm"]:
        p = path + ext
        if os.path.exists(p):
            try:
                os.remove(p)
            except Exception:
                pass


@pytest.fixture
def isolated_controller(temp_sqlite_db):
    """Provides an isolated MemoryController bound to temporary SQLite storage and DefaultAuthorizer."""
    db_path, sqlite_engine = temp_sqlite_db
    ctrl = MemoryController(sqlite_engine, DefaultAuthorizer())
    return ctrl


@pytest.fixture
def asset_catalog():
    """Complete 95 assets + 5 macro tickers catalog matching ghid.py specification."""
    return {
        "INDICI": [
            ("^GSPC", "S&P 500", "US", "Equity Index"),
            ("^NDX", "NASDAQ 100", "US", "Tech Index"),
            ("^IXIC", "NASDAQ Composite", "US", "Broad Tech"),
            ("^DJI", "Dow Jones", "US", "Blue Chip"),
            ("^RUT", "Russell 2000", "US", "Small Cap"),
            ("^GDAXI", "DAX 40", "EU", "Germany Index"),
            ("^FTSE", "FTSE 100", "UK", "UK Index"),
            ("^FCHI", "CAC 40", "EU", "France Index"),
            ("^N225", "Nikkei 225", "JP", "Japan Index"),
            ("^HSI", "Hang Seng", "HK", "Hong Kong Index"),
            ("000001.SS", "Shanghai Composite", "CN", "China Index"),
            ("URTH", "MSCI World ETF", "Global", "World Index"),
            ("EEM", "MSCI Emerging Markets", "Global", "EM Index"),
            ("BET.RO", "BET Romania", "RO", "Romania Index"),
        ],
        "ACTIUNI": [
            ("AAPL", "Apple Inc.", "US", "Technology"),
            ("MSFT", "Microsoft Corp.", "US", "Technology"),
            ("NVDA", "NVIDIA Corp.", "US", "Semiconductors"),
            ("GOOGL", "Alphabet Inc.", "US", "Communication"),
            ("AMZN", "Amazon.com Inc.", "US", "Consumer Cyclical"),
            ("META", "Meta Platforms Inc.", "US", "Communication"),
            ("TSLA", "Tesla Inc.", "US", "Auto & CleanTech"),
            ("NFLX", "Netflix Inc.", "US", "Communication"),
            ("ADBE", "Adobe Inc.", "US", "Technology"),
            ("CRM", "Salesforce Inc.", "US", "Technology"),
            ("PLTR", "Palantir Technologies", "US", "Enterprise AI"),
            ("AMD", "Advanced Micro Devices", "US", "Semiconductors"),
            ("INTC", "Intel Corp.", "US", "Semiconductors"),
            ("AVGO", "Broadcom Inc.", "US", "Semiconductors"),
            ("QCOM", "Qualcomm Inc.", "US", "Semiconductors"),
            ("BRK-B", "Berkshire Hathaway", "US", "Financials"),
            ("JPM", "JPMorgan Chase", "US", "Financials"),
            ("V", "Visa Inc.", "US", "Financials"),
            ("PYPL", "PayPal Holdings", "US", "Financials"),
            ("COIN", "Coinbase Global", "US", "Crypto Brokerage"),
            ("HOOD", "Robinhood Markets", "US", "Brokerage"),
            ("UNH", "UnitedHealth Group", "US", "Healthcare"),
            ("JNJ", "Johnson & Johnson", "US", "Healthcare"),
            ("PG", "Procter & Gamble", "US", "Consumer Defensive"),
            ("XOM", "Exxon Mobil Corp.", "US", "Energy"),
            ("ASML", "ASML Holding", "EU", "Semiconductor Equipment"),
            ("005930.KS", "Samsung Electronics", "KR", "Technology"),
            ("TSM", "Taiwan Semiconductor", "TW", "Semiconductors"),
            ("ARKK", "ARK Innovation ETF", "US", "Disruptive Tech"),
            ("SPY", "SPDR S&P 500 ETF", "US", "Equity ETF"),
        ],
        "CRYPTO": [
            ("BTC-USD", "Bitcoin", "Crypto", "Store of Value"),
            ("ETH-USD", "Ethereum", "Crypto", "Smart Contracts"),
            ("BNB-USD", "Binance Coin", "Crypto", "Exchange Utility"),
            ("SOL-USD", "Solana", "Crypto", "High-Throughput L1"),
            ("XRP-USD", "XRP", "Crypto", "Cross-Border Payments"),
            ("ADA-USD", "Cardano", "Crypto", "Proof-of-Stake L1"),
            ("AVAX-USD", "Avalanche", "Crypto", "Subnet L1"),
            ("DOT-USD", "Polkadot", "Crypto", "Interoperability"),
            ("MATIC-USD", "Polygon", "Crypto", "Ethereum L2"),
            ("LINK-USD", "Chainlink", "Crypto", "Decentralized Oracle"),
            ("UNI-USD", "Uniswap", "Crypto", "DEX Governance"),
            ("LTC-USD", "Litecoin", "Crypto", "Peer-to-Peer Cash"),
            ("DOGE-USD", "Dogecoin", "Crypto", "Meme Currency"),
            ("SHIB-USD", "Shiba Inu", "Crypto", "Meme Ecosystem"),
            ("TRX-USD", "Tron", "Crypto", "Content & Settlement"),
            ("XLM-USD", "Stellar Lumens", "Crypto", "Remittances"),
            ("ATOM-USD", "Cosmos", "Crypto", "Hub & Spoke L1"),
            ("XMR-USD", "Monero", "Crypto", "Privacy Currency"),
            ("FIL-USD", "Filecoin", "Crypto", "Decentralized Storage"),
            ("ICP-USD", "Internet Computer", "Crypto", "Cloud Compute"),
            ("HBAR-USD", "Hedera Hashgraph", "Crypto", "Enterprise DLT"),
            ("VET-USD", "VeChain", "Crypto", "Supply Chain"),
            ("ALGO-USD", "Algorand", "Crypto", "Pure PoS L1"),
            ("FTM-USD", "Fantom", "Crypto", "DAG Smart Contracts"),
            ("NEAR-USD", "NEAR Protocol", "Crypto", "Sharded L1"),
        ],
        "VALUTE": [
            ("EURUSD=X", "EUR/USD", "FX", "Major Currency Pair"),
            ("GBPUSD=X", "GBP/USD", "FX", "Cable Major"),
            ("USDJPY=X", "USD/JPY", "FX", "Yen Major"),
            ("USDCHF=X", "USD/CHF", "FX", "Swiss Franc Major"),
            ("AUDUSD=X", "AUD/USD", "FX", "Aussie Commodity Currency"),
            ("USDCAD=X", "USD/CAD", "FX", "Loonie Commodity Currency"),
            ("NZDUSD=X", "NZD/USD", "FX", "Kiwi Currency"),
            ("EURGBP=X", "EUR/GBP", "FX", "European Cross"),
            ("EURJPY=X", "EUR/JPY", "FX", "Euro-Yen Cross"),
            ("USDCNY=X", "USD/CNY", "FX", "China Yuan Cross"),
            ("USDHUF=X", "USD/HUF", "FX", "Hungarian Forint"),
            ("USDTRY=X", "USD/TRY", "FX", "Turkish Lira Exotic"),
        ],
        "MATERII_PRIME": [
            ("GC=F", "Gold Spot/Futures", "Commodity", "Precious Metal"),
            ("SI=F", "Silver Futures", "Commodity", "Precious / Industrial"),
            ("PL=F", "Platinum Futures", "Commodity", "Industrial Metal"),
            ("PA=F", "Palladium Futures", "Commodity", "Autocatalyst Metal"),
            ("CL=F", "Crude Oil WTI", "Commodity", "Energy"),
            ("BZ=F", "Brent Crude Oil", "Commodity", "Energy"),
            ("NG=F", "Natural Gas", "Commodity", "Energy"),
            ("HG=F", "Copper Futures", "Commodity", "Doctor Copper / Macro"),
            ("ZC=F", "Corn Futures", "Commodity", "Agriculture"),
            ("ZW=F", "Wheat Futures", "Commodity", "Agriculture"),
            ("ZS=F", "Soybeans Futures", "Commodity", "Agriculture"),
            ("KC=F", "Coffee Futures", "Commodity", "Soft Commodity"),
            ("SB=F", "Sugar Futures", "Commodity", "Soft Commodity"),
            ("CT=F", "Cotton Futures", "Commodity", "Soft Commodity"),
        ],
        "MACRO_TICKERS": [
            ("^VIX", "CBOE Volatility Index", "Macro", "Implied Volatility"),
            ("^TNX", "US 10-Year Treasury Yield", "Macro", "Benchmark Real Yield"),
            ("^IRX", "US 13-Week Treasury Bill Yield", "Macro", "Short-Term Cash Yield"),
            ("^TYX", "US 30-Year Treasury Bond Yield", "Macro", "Long-Term Yield"),
            ("DX-Y.NYB", "US Dollar Index DXY", "Macro", "Global Reserve Currency"),
        ],
    }


@pytest.fixture
def mock_fred_series():
    """Deterministic macroeconomic series data from FRED API."""
    return {
        "FEDFUNDS": [
            {"date": "2026-05-01", "value": "5.33"},
            {"date": "2026-06-01", "value": "5.33"},
            {"date": "2026-07-01", "value": "5.08"},
            {"date": "2026-08-01", "value": "4.83"},
        ],
        "CPIAUCSL": [
            {"date": "2026-05-01", "value": "313.2"},
            {"date": "2026-06-01", "value": "313.8"},
            {"date": "2026-07-01", "value": "314.5"},
            {"date": "2026-08-01", "value": "315.1"},
        ],
        "UNRATE": [
            {"date": "2026-05-01", "value": "3.9"},
            {"date": "2026-06-01", "value": "4.0"},
            {"date": "2026-07-01", "value": "4.1"},
            {"date": "2026-08-01", "value": "4.3"},
        ],
        "GDP": [
            {"date": "2025-10-01", "value": "28100.5"},
            {"date": "2026-01-01", "value": "28350.2"},
            {"date": "2026-04-01", "value": "28600.8"},
            {"date": "2026-07-01", "value": "28850.0"},
        ],
    }


@pytest.fixture
def sample_ohlcv_gold():
    """Deterministic 30-period OHLCV bar series for Gold (GC=F / XAUUSD)."""
    base_price = 2450.0
    bars = []
    for i in range(30):
        o = base_price + (i * 2.5)
        h = o + 5.0 + (i % 3)
        l = o - 4.0 - (i % 2)
        c = o + 3.0
        v = 10000 + (i * 500)
        bars.append({
            "timestamp": f"2026-08-{i+1:02d}T16:00:00Z",
            "open": round(o, 2),
            "high": round(h, 2),
            "low": round(l, 2),
            "close": round(c, 2),
            "volume": v,
        })
    return bars


@pytest.fixture
def sample_trade_records():
    """Pre-configured 21-attribute trading journal records for testing."""
    return [
        {
            "trade_id": "T-2026-0825-001",
            "date": "2026-08-25",
            "time": "09:30",
            "asset": "GC=F",
            "direction": "LONG",
            "setup": "Kinetic Volatility Breakout",
            "entry_price": 2510.50,
            "stop_loss": 2504.00,
            "take_profit": 2523.50,
            "position_size": 2.0,
            "risk_amount": 1300.0,
            "exit_price": 2523.50,
            "exit_date": "2026-08-25 14:15",
            "pnl_currency": 2600.0,
            "pnl_percent": 0.52,
            "realized_rr": 2.0,
            "execution_quality": 9,
            "emotion": "Disciplined",
            "plan_adhered": True,
            "lesson": "Wait for 15m candle close before entry on London breakout",
            "evidence_ref": "mt5_ticket_9871234",
        },
        {
            "trade_id": "T-2026-0825-002",
            "date": "2026-08-25",
            "time": "15:45",
            "asset": "NVDA",
            "direction": "SHORT",
            "setup": "Mean Reversion Exhaustion",
            "entry_price": 128.50,
            "stop_loss": 131.00,
            "take_profit": 123.50,
            "position_size": 100.0,
            "risk_amount": 250.0,
            "exit_price": 131.00,
            "exit_date": "2026-08-25 16:30",
            "pnl_currency": -250.0,
            "pnl_percent": -1.95,
            "realized_rr": -1.0,
            "execution_quality": 4,
            "emotion": "FOMO",
            "plan_adhered": False,
            "lesson": "Never short mega-cap tech directly into earnings momentum",
            "evidence_ref": "mt5_ticket_9871299",
        },
    ]
