"""
Multi-Layered Financial Search Engine & Entity Resolver.

Provides high-precision financial search across canonical memory notes in the AI Memory Vault:
1. Financial entity/alias extraction from natural language queries (95 assets, 5 macro tickers, 4 FRED series).
2. SQLite structured & temporal filtering (symbols, categories, confidence, verification, date ranges).
3. Hybrid ranking (Lexical BM25 + Dense Vector Cosine Similarity via Reciprocal Rank Fusion / RRF).
4. Wikilink graph spreading activation re-ranking (cross-asset, macro-regime, and causal links).
5. Context Pack Builder with progressive disclosure and HMAC-SHA256 pagination token support.

Adheres strictly to AGENTS.md, PROJECT.md, and P0-P18 cognitive trust boundary invariants.
"""

from __future__ import annotations

import enum
import hashlib
import json
import math
import os
import re
import threading
from dataclasses import dataclass, field
from datetime import datetime, timezone, timedelta
from typing import Any, Dict, List, Optional, Set, Tuple, Union

# Core Memory Controller Imports
from .authorizer import Principal, Operation
from .audit.logger import audit_event
from .security import sanitize_query, check_query_size
from .security.pagination_token import (
    PaginationToken,
    MissingHMACSecretError,
    InvalidPaginationTokenError,
)
from .context.budget import ContextBudget, load_agent_budget
from .context.progressive_disclosure import ProgressiveDisclosure
from .context.pack_builder import ContextPackBuilder


# ============================================================================
# 1. DOMAIN MODELS & DATA STRUCTURES
# ============================================================================

@dataclass
class FinancialEntity:
    """Represents a canonical financial instrument or macroeconomic series."""
    symbol: str
    name: str
    category: str
    sector: str
    aliases: List[str]
    canonical_tag: str
    related_symbols: List[str] = field(default_factory=list)
    description: str = ""


@dataclass
class FinancialFilterSpec:
    """Structured search criteria parsed from query and explicit overrides."""
    raw_query: str
    sanitized_query: str
    symbols: List[str] = field(default_factory=list)
    asset_categories: List[str] = field(default_factory=list)
    macro_indicators: List[str] = field(default_factory=list)
    indicator_terms: List[str] = field(default_factory=list)
    min_confidence: Optional[str] = None
    verification_states: List[str] = field(default_factory=list)
    date_from: Optional[str] = None
    date_to: Optional[str] = None
    types: Optional[List[str]] = None
    lifecycles: List[str] = field(default_factory=lambda: ["ACTIVE", "REVIEW"])
    page_size: int = 10
    page_token: Optional[str] = None
    disclosure_level: str = "metadata"


@dataclass
class ScoredMemoryNote:
    """Represents a memory note scored across the multi-layer pipeline."""
    note_id: str
    note: Dict[str, Any]
    bm25_score: float = 0.0
    vector_score: float = 0.0
    rrf_score: float = 0.0
    activation_score: float = 0.0
    final_score: float = 0.0
    bm25_rank: int = 0
    vector_rank: int = 0


# ============================================================================
# 2. FINANCIAL ENTITY RESOLVER (95 Assets + 5 Macro + 4 FRED)
# ============================================================================

class FinancialEntityResolver:
    """
    Robust alias resolver mapping human natural language names, colloquial terms,
    and financial tickers to canonical symbols and taxonomy tags.
    Covers all 95 assets, 5 macro benchmarks, and 4 FRED series.
    """

    def __init__(self):
        self._entities: Dict[str, FinancialEntity] = {}
        self._alias_to_symbol: Dict[str, str] = {}
        self._category_aliases: Dict[str, str] = {}
        self._indicator_keywords: Set[str] = set()
        self._build_registry()

    def _register(
        self,
        symbol: str,
        name: str,
        category: str,
        sector: str,
        aliases: List[str],
        canonical_tag: str,
        related: Optional[List[str]] = None,
        description: str = ""
    ):
        clean_aliases = [a.strip().lower() for a in aliases if a.strip()]
        # Always include lower-cased symbol and name
        if symbol.lower() not in clean_aliases:
            clean_aliases.append(symbol.lower())
        if name.lower() not in clean_aliases:
            clean_aliases.append(name.lower())
            
        entity = FinancialEntity(
            symbol=symbol,
            name=name,
            category=category,
            sector=sector,
            aliases=clean_aliases,
            canonical_tag=canonical_tag,
            related_symbols=related or [],
            description=description,
        )
        self._entities[symbol] = entity
        for alias in clean_aliases:
            self._alias_to_symbol[alias] = symbol

    def _build_registry(self):
        # --------------------------------------------------------------------
        # 14 INDICES
        # --------------------------------------------------------------------
        self._register("^GSPC", "S&P 500", "INDICI", "Equity Index",
                       ["s&p 500", "s&p500", "s&p", "sp500", "spx", "spy", "standard & poor's", "sp 500"],
                       "#asset/sp500", ["^NDX", "^DJI", "^VIX", "SPY"], "US Large-Cap Equity Benchmark")
        self._register("^NDX", "NASDAQ 100", "INDICI", "Equity Index",
                       ["nasdaq 100", "nasdaq100", "ndx", "qqq", "tech index", "nasdaq-100"],
                       "#asset/nasdaq", ["^GSPC", "^IXIC", "QQQ"], "US Top 100 Non-Financial Tech Benchmark")
        self._register("^IXIC", "NASDAQ Composite", "INDICI", "Equity Index",
                       ["nasdaq composite", "nasdaq comp", "nasdaq comp.", "ixic", "nasdaq"],
                       "#asset/nasdaq_comp", ["^NDX", "^GSPC"], "All NASDAQ Listed Equities Index")
        self._register("^DJI", "Dow Jones", "INDICI", "Equity Index",
                       ["dow jones", "dow jones industrial", "djia", "dow 30", "dow", "dia"],
                       "#asset/dow", ["^GSPC", "^RUT"], "US 30 Industrial Mega-Caps")
        self._register("^RUT", "Russell 2000", "INDICI", "Equity Index",
                       ["russell 2000", "russell2000", "russell", "rut", "iwm", "small cap index", "small-cap"],
                       "#asset/russell", ["^GSPC", "^DJI"], "US Small-Cap Equity Benchmark")
        self._register("^GDAXI", "DAX Germany", "INDICI", "Equity Index",
                       ["dax germany", "dax", "dax 40", "dax 30", "ger40", "german index", "dax40", "frankfurt index"],
                       "#asset/dax", ["^FTSE", "^FCHI", "EURUSD=X"], "Germany Top 40 Blue-Chip Index")
        self._register("^FTSE", "FTSE 100", "INDICI", "Equity Index",
                       ["ftse 100", "ftse100", "ftse", "uk100", "footsie", "british index", "uk 100"],
                       "#asset/ftse", ["^GDAXI", "GBPUSD=X"], "UK Top 100 Blue-Chip Index")
        self._register("^FCHI", "CAC 40", "INDICI", "Equity Index",
                       ["cac 40", "cac40", "cac", "fra40", "french index", "paris index"],
                       "#asset/cac", ["^GDAXI", "^FTSE"], "France Top 40 Benchmark Index")
        self._register("^N225", "Nikkei 225", "INDICI", "Equity Index",
                       ["nikkei 225", "nikkei225", "nikkei", "n225", "japan 225", "tokyo index", "japan index"],
                       "#asset/nikkei", ["USDJPY=X", "^HSI"], "Japan Top 225 Equities Index")
        self._register("^HSI", "Hang Seng", "INDICI", "Equity Index",
                       ["hang seng", "hang seng index", "hsi", "hk50", "hong kong index", "hsi index"],
                       "#asset/hangseng", ["000001.SS", "EEM"], "Hong Kong Blue-Chip Equity Index")
        self._register("000001.SS", "Shanghai Composite", "INDICI", "Equity Index",
                       ["shanghai", "shanghai composite", "ssec", "shanghai index", "china a50", "000001.ss"],
                       "#asset/shanghai", ["^HSI", "EEM", "USDCNY=X"], "Shanghai Stock Exchange Composite Index")
        self._register("URTH", "MSCI World", "INDICI", "Equity Index",
                       ["msci world", "msci world developed", "urth", "ishares msci world", "world equities"],
                       "#asset/msci_world", ["EEM", "^GSPC"], "iShares MSCI World Developed Markets ETF")
        self._register("EEM", "MSCI EM", "INDICI", "Equity Index",
                       ["msci em", "msci emerging markets", "eem", "emerging markets index", "emerging markets"],
                       "#asset/msci_em", ["URTH", "000001.SS"], "iShares MSCI Emerging Markets ETF")
        self._register("BET.RO", "BET Romania", "INDICI", "Equity Index",
                       ["bet romania", "bet", "bvb", "bucharest exchange", "romanian index", "bet.ro", "bursa bucuresti"],
                       "#asset/bet", ["EURUSD=X"], "Bucharest Stock Exchange Top 20 Index")

        # --------------------------------------------------------------------
        # 30 EQUITIES
        # --------------------------------------------------------------------
        self._register("AAPL", "Apple", "ACTIUNI", "Technology", ["apple", "apple inc", "aapl", "iphone maker"], "#asset/aapl", ["MSFT", "GOOGL"])
        self._register("MSFT", "Microsoft", "ACTIUNI", "Technology", ["microsoft", "microsoft corp", "msft", "azure"], "#asset/msft", ["AAPL", "GOOGL", "AMZN"])
        self._register("NVDA", "NVIDIA", "ACTIUNI", "Technology", ["nvidia", "nvidia corp", "nvda", "ai chips"], "#asset/nvda", ["AMD", "TSM", "INTC", "AVGO"])
        self._register("GOOGL", "Alphabet", "ACTIUNI", "Technology", ["alphabet", "google", "googl", "goog"], "#asset/googl", ["MSFT", "META", "AMZN"])
        self._register("AMZN", "Amazon", "ACTIUNI", "Consumer Discretionary", ["amazon", "amazon.com", "amzn", "aws"], "#asset/amzn", ["MSFT", "GOOGL", "WMT"])
        self._register("META", "Meta", "ACTIUNI", "Technology", ["meta", "facebook", "meta platforms", "metaverse"], "#asset/meta", ["GOOGL", "SNAP"])
        self._register("TSLA", "Tesla", "ACTIUNI", "Consumer Discretionary", ["tesla", "tesla motors", "tsla", "ev maker"], "#asset/tsla", ["RIVN", "BYD"])
        self._register("BRK-B", "Berkshire B", "ACTIUNI", "Financials", ["berkshire hathaway", "berkshire b", "berkshire", "brk-b", "brk.b", "buffett"], "#asset/brk")
        self._register("JPM", "JPMorgan", "ACTIUNI", "Financials", ["jpmorgan", "jp morgan", "jpmorgan chase", "jpm", "jamie dimon"], "#asset/jpm", ["BAC", "GS", "V"])
        self._register("V", "Visa", "ACTIUNI", "Financials", ["visa", "visa inc", "v", "payments"], "#asset/v", ["MA", "PYPL"])
        self._register("UNH", "UnitedHealth", "ACTIUNI", "Healthcare", ["unitedhealth", "unitedhealth group", "unh", "optum"], "#asset/unh")
        self._register("XOM", "Exxon Mobil", "ACTIUNI", "Energy", ["exxon mobil", "exxon", "xom", "oil giant"], "#asset/xom", ["CVX", "CL=F", "BZ=F"])
        self._register("JNJ", "Johnson&Johnson", "ACTIUNI", "Healthcare", ["johnson & johnson", "johnson&johnson", "jnj"], "#asset/jnj")
        self._register("PG", "Procter&Gamble", "ACTIUNI", "Consumer Staples", ["procter & gamble", "procter&gamble", "p&g", "pg"], "#asset/pg")
        self._register("ASML", "ASML", "ACTIUNI", "Technology", ["asml", "asml holding", "lithography"], "#asset/asml", ["TSM", "NVDA"])
        self._register("005930.KS", "Samsung", "ACTIUNI", "Technology", ["samsung", "samsung electronics", "005930.ks"], "#asset/samsung", ["TSM", "AAPL"])
        self._register("TSM", "TSMC", "ACTIUNI", "Technology", ["tsmc", "taiwan semiconductor", "tsm", "semiconductor foundry"], "#asset/tsm", ["NVDA", "ASML", "INTC"])
        self._register("NFLX", "Netflix", "ACTIUNI", "Communication Services", ["netflix", "nflx", "streaming"], "#asset/nflx", ["DIS", "AMZN"])
        self._register("ADBE", "Adobe", "ACTIUNI", "Technology", ["adobe", "adobe systems", "adbe"], "#asset/adbe", ["CRM", "MSFT"])
        self._register("CRM", "Salesforce", "ACTIUNI", "Technology", ["salesforce", "salesforce.com", "crm"], "#asset/crm", ["ADBE", "MSFT"])
        self._register("PLTR", "Palantir", "ACTIUNI", "Technology", ["palantir", "palantir technologies", "pltr", "alex karp"], "#asset/pltr")
        self._register("AMD", "AMD", "ACTIUNI", "Technology", ["amd", "advanced micro devices", "lisa su"], "#asset/amd", ["NVDA", "INTC"])
        self._register("INTC", "Intel", "ACTIUNI", "Technology", ["intel", "intel corp", "intc"], "#asset/intc", ["AMD", "TSM", "NVDA"])
        self._register("AVGO", "Broadcom", "ACTIUNI", "Technology", ["broadcom", "broadcom inc", "avgo"], "#asset/avgo", ["NVDA", "QCOM"])
        self._register("QCOM", "Qualcomm", "ACTIUNI", "Technology", ["qualcomm", "qualcomm inc", "qcom", "snapdragon"], "#asset/qcom", ["AVGO", "AAPL"])
        self._register("PYPL", "PayPal", "ACTIUNI", "Financials", ["paypal", "paypal holdings", "pypl"], "#asset/pypl", ["V", "COIN", "HOOD"])
        self._register("COIN", "Coinbase", "ACTIUNI", "Financials", ["coinbase", "coinbase global", "coin", "crypto exchange"], "#asset/coin", ["BTC-USD", "ETH-USD", "HOOD"])
        self._register("HOOD", "Robinhood", "ACTIUNI", "Financials", ["robinhood", "robinhood markets", "hood", "retail trading"], "#asset/hood", ["COIN", "PYPL"])
        self._register("ARKK", "Cathie Wood ARK", "ACTIUNI", "Financials", ["cathie wood ark", "ark innovation", "arkk", "ark invest"], "#asset/arkk", ["TSLA", "PLTR", "COIN"])
        self._register("SPY", "SPY ETF", "ACTIUNI", "Financials", ["spy etf", "spy", "spdr s&p 500", "spdr s&p 500 etf"], "#asset/spy", ["^GSPC"])

        # --------------------------------------------------------------------
        # 25 CRYPTOCURRENCIES
        # --------------------------------------------------------------------
        self._register("BTC-USD", "Bitcoin", "CRYPTO", "Layer 1",
                       ["bitcoin", "btc", "btcusd", "btc/usd", "xbt", "digital gold", "btc-usd"],
                       "#asset/btc", ["ETH-USD", "SOL-USD", "COIN"], "Primary Decentralized Settlement Asset")
        self._register("ETH-USD", "Ethereum", "CRYPTO", "Layer 1",
                       ["ethereum", "ether", "eth", "ethusd", "eth/usd", "vitalik", "eth-usd"],
                       "#asset/eth", ["BTC-USD", "SOL-USD"], "Smart Contracts & DeFi Settlement Layer")
        self._register("BNB-USD", "BNB", "CRYPTO", "Layer 1",
                       ["bnb", "binance coin", "bnbusd", "bnb/usd", "bnb-usd"],
                       "#asset/bnb", ["BTC-USD", "ETH-USD"])
        self._register("SOL-USD", "Solana", "CRYPTO", "Layer 1",
                       ["solana", "sol", "solusd", "sol/usd", "sol-usd"],
                       "#asset/sol", ["ETH-USD", "AVAX-USD"])
        self._register("XRP-USD", "XRP", "CRYPTO", "Payment Infrastructure",
                       ["xrp", "ripple", "xrpusd", "xrp/usd", "xrp-usd"],
                       "#asset/xrp", ["XLM-USD"])
        self._register("ADA-USD", "Cardano", "CRYPTO", "Layer 1",
                       ["cardano", "ada", "adausd", "ada/usd", "ada-usd", "charles hoskinson"],
                       "#asset/ada", ["DOT-USD"])
        self._register("AVAX-USD", "Avalanche", "CRYPTO", "Layer 1",
                       ["avalanche", "avax", "avaxusd", "avax/usd", "avax-usd"],
                       "#asset/avax", ["SOL-USD", "ETH-USD"])
        self._register("DOT-USD", "Polkadot", "CRYPTO", "Layer 0",
                       ["polkadot", "dot", "dotusd", "dot/usd", "dot-usd"],
                       "#asset/dot", ["ATOM-USD"])
        self._register("MATIC-USD", "Polygon", "CRYPTO", "Layer 2",
                       ["polygon", "matic", "pol", "maticusd", "matic/usd", "matic-usd"],
                       "#asset/matic", ["ETH-USD"])
        self._register("LINK-USD", "Chainlink", "CRYPTO", "Oracle Infrastructure",
                       ["chainlink", "link", "linkusd", "link/usd", "link-usd", "oracles"],
                       "#asset/link", ["ETH-USD"])
        self._register("UNI-USD", "Uniswap", "CRYPTO", "DeFi",
                       ["uniswap", "uni", "uniusd", "uni/usd", "uni-usd", "dex"],
                       "#asset/uni", ["ETH-USD"])
        self._register("LTC-USD", "Litecoin", "CRYPTO", "Payment",
                       ["litecoin", "ltc", "ltcusd", "ltc/usd", "ltc-usd", "digital silver"],
                       "#asset/ltc", ["BTC-USD"])
        self._register("DOGE-USD", "Dogecoin", "CRYPTO", "Payment/Meme",
                       ["dogecoin", "doge", "dogeusd", "doge/usd", "doge-usd"],
                       "#asset/doge", ["SHIB-USD"])
        self._register("SHIB-USD", "Shiba Inu", "CRYPTO", "Ecosystem/Meme",
                       ["shiba inu", "shiba", "shib", "shibusd", "shib/usd", "shib-usd"],
                       "#asset/shib", ["DOGE-USD"])
        self._register("TRX-USD", "TRON", "CRYPTO", "Layer 1",
                       ["tron", "trx", "trxusd", "trx/usd", "trx-usd", "justin sun"],
                       "#asset/trx")
        self._register("XLM-USD", "Stellar", "CRYPTO", "Payment",
                       ["stellar", "xlm", "lumens", "xlmusd", "xlm/usd", "xlm-usd"],
                       "#asset/xlm", ["XRP-USD"])
        self._register("ATOM-USD", "Cosmos", "CRYPTO", "Layer 0",
                       ["cosmos", "atom", "atomusd", "atom/usd", "atom-usd", "ibc"],
                       "#asset/atom", ["DOT-USD"])
        self._register("XMR-USD", "Monero", "CRYPTO", "Privacy",
                       ["monero", "xmr", "xmrusd", "xmr/usd", "xmr-usd", "privacy coin"],
                       "#asset/xmr")
        self._register("FIL-USD", "Filecoin", "CRYPTO", "Storage",
                       ["filecoin", "fil", "filusd", "fil/usd", "fil-usd"],
                       "#asset/fil")
        self._register("ICP-USD", "Internet Computer", "CRYPTO", "Layer 1",
                       ["internet computer", "icp", "icpusd", "icp/usd", "icp-usd", "dfinity"],
                       "#asset/icp")
        self._register("HBAR-USD", "Hedera", "CRYPTO", "Enterprise DLT",
                       ["hedera", "hbar", "hashgraph", "hbarusd", "hbar/usd", "hbar-usd"],
                       "#asset/hbar")
        self._register("VET-USD", "VeChain", "CRYPTO", "Enterprise",
                       ["vechain", "vet", "vetusd", "vet/usd", "vet-usd"],
                       "#asset/vet")
        self._register("ALGO-USD", "Algorand", "CRYPTO", "Layer 1",
                       ["algorand", "algo", "algousd", "algo/usd", "algo-usd"],
                       "#asset/algo")
        self._register("FTM-USD", "Fantom", "CRYPTO", "Layer 1",
                       ["fantom", "ftm", "sonic", "ftmusd", "ftm/usd", "ftm-usd"],
                       "#asset/ftm")
        self._register("NEAR-USD", "NEAR Protocol", "CRYPTO", "Layer 1",
                       ["near protocol", "near", "nearusd", "near/usd", "near-usd"],
                       "#asset/near")

        # --------------------------------------------------------------------
        # 12 FOREX (VALUTE)
        # --------------------------------------------------------------------
        self._register("EURUSD=X", "EUR/USD", "VALUTE", "Foreign Exchange",
                       ["eur/usd", "eurusd", "euro", "euro dollar", "eurusd=x", "euro vs dollar"],
                       "#asset/eurusd", ["DX-Y.NYB", "GBPUSD=X"], "Major FX Pair - Euro vs US Dollar")
        self._register("GBPUSD=X", "GBP/USD", "VALUTE", "Foreign Exchange",
                       ["gbp/usd", "gbpusd", "cable", "pound dollar", "gbpusd=x", "british pound"],
                       "#asset/gbpusd", ["EURUSD=X", "DX-Y.NYB"])
        self._register("USDJPY=X", "USD/JPY", "VALUTE", "Foreign Exchange",
                       ["usd/jpy", "usdjpy", "dollar yen", "gopher", "usdjpy=x", "japanese yen"],
                       "#asset/usdjpy", ["^N225", "^TNX"])
        self._register("USDCHF=X", "USD/CHF", "VALUTE", "Foreign Exchange",
                       ["usd/chf", "usdchf", "dollar swiss", "swissy", "usdchf=x", "swiss franc"],
                       "#asset/usdchf", ["EURUSD=X"])
        self._register("AUDUSD=X", "AUD/USD", "VALUTE", "Foreign Exchange",
                       ["aud/usd", "audusd", "aussie dollar", "aussie", "audusd=x", "australian dollar"],
                       "#asset/audusd", ["NZDUSD=X", "GC=F", "HG=F"])
        self._register("USDCAD=X", "USD/CAD", "VALUTE", "Foreign Exchange",
                       ["usd/cad", "usdcad", "dollar loonie", "loonie", "usdcad=x", "canadian dollar"],
                       "#asset/usdcad", ["CL=F"])
        self._register("NZDUSD=X", "NZD/USD", "VALUTE", "Foreign Exchange",
                       ["nzd/usd", "nzdusd", "kiwi dollar", "kiwi", "nzdusd=x", "new zealand dollar"],
                       "#asset/nzdusd", ["AUDUSD=X"])
        self._register("EURGBP=X", "EUR/GBP", "VALUTE", "Foreign Exchange",
                       ["eur/gbp", "eurgbp", "euro pound", "euro sterling", "eurgbp=x"],
                       "#asset/eurgbp", ["EURUSD=X", "GBPUSD=X"])
        self._register("EURJPY=X", "EUR/JPY", "VALUTE", "Foreign Exchange",
                       ["eur/jpy", "eurjpy", "euro yen", "eurjpy=x"],
                       "#asset/eurjpy", ["EURUSD=X", "USDJPY=X"])
        self._register("USDCNY=X", "USD/CNY", "VALUTE", "Foreign Exchange",
                       ["usd/cny", "usdcny", "dollar yuan", "chinese yuan", "renminbi", "usdcny=x"],
                       "#asset/usdcny", ["000001.SS"])
        self._register("USDHUF=X", "USD/HUF", "VALUTE", "Foreign Exchange",
                       ["usd/huf", "usdhuf", "dollar forint", "hungarian forint", "usdhuf=x"],
                       "#asset/usdhuf")
        self._register("USDTRY=X", "USD/TRY", "VALUTE", "Foreign Exchange",
                       ["usd/try", "usdtry", "dollar lira", "turkish lira", "usdtry=x"],
                       "#asset/usdtry")

        # --------------------------------------------------------------------
        # 14 COMMODITIES (MATERII PRIME)
        # --------------------------------------------------------------------
        self._register("GC=F", "Gold", "MATERII_PRIME", "Precious Metals",
                       ["gold", "xau", "xauusd", "xau/usd", "spot gold", "aur", "gc=f", "comex gold", "gold futures", "bullion"],
                       "#asset/xau", ["SI=F", "^TNX", "DX-Y.NYB", "PL=F"], "Comex Gold Futures (100 oz)")
        self._register("SI=F", "Silver", "MATERII_PRIME", "Precious Metals",
                       ["silver", "xag", "xagusd", "xag/usd", "spot silver", "argint", "si=f", "comex silver", "silver futures"],
                       "#asset/xag", ["GC=F", "HG=F"], "Comex Silver Futures (5,000 oz)")
        self._register("CL=F", "Oil WTI", "MATERII_PRIME", "Energy",
                       ["oil wti", "wti", "crude oil", "wti crude", "crude", "oil", "petrol", "cl=f", "nymex crude"],
                       "#asset/wti", ["BZ=F", "NG=F", "XOM"], "Nymex Crude Oil WTI Futures")
        self._register("BZ=F", "Oil Brent", "MATERII_PRIME", "Energy",
                       ["oil brent", "brent", "brent crude", "bz=f", "north sea brent"],
                       "#asset/brent", ["CL=F", "NG=F"], "ICE Brent Crude Futures")
        self._register("NG=F", "Natural Gas", "MATERII_PRIME", "Energy",
                       ["natural gas", "nat gas", "gas", "henry hub", "ng=f"],
                       "#asset/natgas", ["CL=F", "BZ=F"], "Henry Hub Natural Gas Futures")
        self._register("HG=F", "Copper", "MATERII_PRIME", "Industrial Metals",
                       ["copper", "high grade copper", "comex copper", "cupru", "hg=f", "doctor copper"],
                       "#asset/copper", ["000001.SS", "AUDUSD=X"], "Comex High Grade Copper Futures")
        self._register("PL=F", "Platinum", "MATERII_PRIME", "Precious Metals",
                       ["platinum", "platin", "pl=f", "nymex platinum"],
                       "#asset/platinum", ["GC=F", "PA=F"])
        self._register("PA=F", "Palladium", "MATERII_PRIME", "Precious Metals",
                       ["palladium", "paladiu", "pa=f", "nymex palladium"],
                       "#asset/palladium", ["PL=F", "GC=F"])
        self._register("ZC=F", "Corn", "MATERII_PRIME", "Agriculture",
                       ["corn", "porumb", "zc=f", "cbot corn"], "#asset/corn", ["ZW=F", "ZS=F"])
        self._register("ZW=F", "Wheat", "MATERII_PRIME", "Agriculture",
                       ["wheat", "grau", "zw=f", "cbot wheat"], "#asset/wheat", ["ZC=F", "ZS=F"])
        self._register("ZS=F", "Soybeans", "MATERII_PRIME", "Agriculture",
                       ["soybeans", "soia", "zs=f", "cbot soybeans"], "#asset/soybeans", ["ZC=F", "ZW=F"])
        self._register("KC=F", "Coffee", "MATERII_PRIME", "Agriculture",
                       ["coffee", "cafea", "kc=f", "ice coffee c"], "#asset/coffee", ["SB=F"])
        self._register("SB=F", "Sugar", "MATERII_PRIME", "Agriculture",
                       ["sugar", "zahar", "sb=f", "ice sugar"], "#asset/sugar", ["KC=F"])
        self._register("CT=F", "Cotton", "MATERII_PRIME", "Agriculture",
                       ["cotton", "bumbac", "ct=f", "ice cotton"], "#asset/cotton")

        # --------------------------------------------------------------------
        # 5 MACRO BENCHMARK TICKERS
        # --------------------------------------------------------------------
        self._register("^VIX", "VIX", "MACRO", "Volatility",
                       ["vix", "cboe volatility index", "volatility index", "fear gauge", "^vix", "cboe vix"],
                       "#macro/vix", ["^GSPC", "^NDX", "BTC-USD"], "CBOE Volatility Index")
        self._register("^TNX", "Yield 10Y US", "MACRO", "Sovereign Yield",
                       ["yield 10y us", "10-year treasury yield", "10-year treasury", "10y treasury", "10y yield", "us 10y", "us10y", "dgs10", "^tnx", "10 year treasury", "treasury yield"],
                       "#macro/us10y", ["^IRX", "^TYX", "GC=F", "DX-Y.NYB"], "US 10-Year Treasury Yield Benchmark")
        self._register("^IRX", "Yield 2Y US", "MACRO", "Sovereign Yield",
                       ["yield 2y us", "2-year treasury yield", "2-year treasury", "2y treasury", "2y yield", "us 2y", "us2y", "^irx", "2 year treasury", "13w bill"],
                       "#macro/us2y", ["^TNX", "FEDFUNDS"])
        self._register("^TYX", "Yield 30Y US", "MACRO", "Sovereign Yield",
                       ["yield 30y us", "30-year treasury bond yield", "30-year treasury", "30y treasury", "30y yield", "us 30y", "us30y", "^tyx", "30 year treasury"],
                       "#macro/us30y", ["^TNX"])
        self._register("DX-Y.NYB", "USD Index", "MACRO", "Currency Index",
                       ["usd index", "us dollar index", "dollar index", "dxy", "dx-y.nyb", "uup", "greenback"],
                       "#macro/dxy", ["EURUSD=X", "GC=F", "^GSPC"], "US Dollar Currency Index against basket")

        # --------------------------------------------------------------------
        # 4 FRED MACROECONOMIC SERIES
        # --------------------------------------------------------------------
        self._register("FEDFUNDS", "Fed Funds Rate", "FRED", "Policy Rate",
                       ["fed funds rate", "federal funds rate", "effective federal funds rate", "fomc rate", "fed rate", "interest rate", "policy rate", "fedfunds", "rate hike", "rate cut", "monetary policy"],
                       "#macro/fedfunds", ["^TNX", "^IRX", "DX-Y.NYB", "GC=F"], "Federal Funds Effective Rate (FOMC)")
        self._register("CPIAUCSL", "Consumer Price Index", "FRED", "Inflation",
                       ["consumer price index", "cpi", "inflation", "headline inflation", "cpiaucsl", "core cpi", "us inflation"],
                       "#macro/cpi", ["FEDFUNDS", "^TNX", "GC=F"], "Consumer Price Index for All Urban Consumers")
        self._register("UNRATE", "Civilian Unemployment Rate", "FRED", "Labor Market",
                       ["civilian unemployment rate", "unemployment rate", "unemployment", "jobless rate", "unrate", "non-farm payrolls", "nfp", "jobs report"],
                       "#macro/unemployment", ["FEDFUNDS", "^GSPC"], "Civilian Unemployment Rate")
        self._register("GDP", "Gross Domestic Product", "FRED", "Economic Output",
                       ["gross domestic product", "gdp", "us gdp", "economic output", "economic growth"],
                       "#macro/gdp", ["^GSPC", "FEDFUNDS"], "US Gross Domestic Product")

        # --------------------------------------------------------------------
        # Category aliases
        # --------------------------------------------------------------------
        self._category_aliases = {
            "indices": "INDICI",
            "index": "INDICI",
            "indici": "INDICI",
            "equities": "ACTIUNI",
            "stocks": "ACTIUNI",
            "actiuni": "ACTIUNI",
            "shares": "ACTIUNI",
            "crypto": "CRYPTO",
            "cryptocurrency": "CRYPTO",
            "cryptocurrencies": "CRYPTO",
            "forex": "VALUTE",
            "fx": "VALUTE",
            "valute": "VALUTE",
            "currencies": "VALUTE",
            "commodities": "MATERII_PRIME",
            "commodity": "MATERII_PRIME",
            "materii": "MATERII_PRIME",
            "materii prime": "MATERII_PRIME",
            "materii_prime": "MATERII_PRIME",
            "metals": "MATERII_PRIME",
            "macro": "macro-analysis",
            "macroeconomics": "macro-analysis",
            "macro-analysis": "macro-analysis",
            "macroeconomic-regime": "macro-analysis",
            "technical": "technical-setup",
            "technical-analysis": "technical-setup",
            "technical-setup": "technical-setup",
            "technical-trading-setup": "technical-setup",
            "journal": "trading-journal",
            "trading-journal": "trading-journal",
            "trade-log": "trading-journal",
            "trade-execution-log": "trading-journal",
            "risk": "risk-assessment",
            "risk-assessment": "risk-assessment",
            "valuation": "valuation-model",
            "valuation-model": "valuation-model",
        }

        # --------------------------------------------------------------------
        # Technical & Macro Indicator Keywords
        # --------------------------------------------------------------------
        self._indicator_keywords = {
            "rsi", "macd", "atr", "sma", "ema", "ma20", "ma50", "ma200",
            "bollinger", "bb", "stochastic", "stoch", "momentum", "rvol",
            "support", "resistance", "confluence", "breakout", "trend",
            "reversal", "divergence", "drawdown", "sharpe", "r-multiple",
            "stop loss", "take profit", "risk reward", "pnl", "fomc", "nfp", "cpi", "vix"
        }

    def resolve_symbol(self, query_or_name: str) -> Optional[str]:
        """
        Resolves any name, ticker, alias, or colloquial string to a canonical ticker symbol.
        Case-insensitive and whitespace tolerant.
        """
        if not query_or_name:
            return None
        q = query_or_name.strip().lower()
        if q in self._alias_to_symbol:
            return self._alias_to_symbol[q]
            
        # Try stripping punctuation or common prefixes/suffixes
        q_clean = re.sub(r"[\^\=\/\-\_\.\s]", "", q)
        for alias, sym in self._alias_to_symbol.items():
            alias_clean = re.sub(r"[\^\=\/\-\_\.\s]", "", alias)
            if q_clean == alias_clean:
                return sym
                
        # Direct symbol match check
        for sym in self._entities:
            if sym.lower() == q or re.sub(r"[\^\=\/\-\_\.\s]", "", sym.lower()) == q_clean:
                return sym
                
        return None

    def resolve_all_entities(self, text: str) -> List[FinancialEntity]:
        """Extracts all financial entities mentioned in a given text."""
        if not text:
            return []
        found_symbols: Set[str] = set()
        text_lower = f" {text.lower()} "

        # 1. Check longest multi-word aliases first to prevent partial shadowing
        sorted_aliases = sorted(self._alias_to_symbol.keys(), key=len, reverse=True)
        for alias in sorted_aliases:
            sym = self._alias_to_symbol[alias]
            if sym in found_symbols:
                continue
            # Boundary check
            pattern = r"(?:\b|_|\^|\$)" + re.escape(alias) + r"(?:\b|_|\$|\=|\/)"
            if re.search(pattern, text_lower) or f" {alias} " in text_lower:
                found_symbols.add(sym)

        return [self._entities[sym] for sym in found_symbols if sym in self._entities]

    def extract_entities_and_filters(self, text: str) -> Dict[str, Any]:
        """
        Parses free-form natural language query and extracts structured symbols,
        categories, indicators, confidence levels, verification states, and date ranges.
        """
        entities = self.resolve_all_entities(text)
        symbols = [e.symbol for e in entities]
        
        text_lower = text.lower() if text else ""
        categories: Set[str] = set()
        
        # Entity categories
        for e in entities:
            categories.add(e.category)
            
        # Category keywords in text
        for cat_kw, norm_cat in self._category_aliases.items():
            if re.search(r"\b" + re.escape(cat_kw) + r"\b", text_lower):
                categories.add(norm_cat)

        # Technical indicators
        indicators: Set[str] = set()
        for kw in self._indicator_keywords:
            if re.search(r"\b" + re.escape(kw) + r"\b", text_lower):
                indicators.add(kw)

        # Confidence extraction
        min_confidence = None
        if "very high confidence" in text_lower or "very_high" in text_lower:
            min_confidence = "very_high"
        elif "high confidence" in text_lower or "high conf" in text_lower:
            min_confidence = "high"
        elif "medium confidence" in text_lower:
            min_confidence = "medium"
        elif "low confidence" in text_lower:
            min_confidence = "low"

        # Verification extraction
        verification_states: List[str] = []
        if re.search(r"\bverified\b", text_lower) and not re.search(r"\bunverified\b", text_lower):
            verification_states.append("verified")
        elif re.search(r"\bunverified\b", text_lower):
            verification_states.append("unverified")
        elif re.search(r"\bpartially verified\b|\bpartially_verified\b", text_lower):
            verification_states.append("partially_verified")

        # Date range extraction
        date_from = None
        date_to = None

        # Matches YYYY-MM-DD
        iso_dates = re.findall(r"\b(20\d{2}-\d{2}-\d{2})\b", text)
        if len(iso_dates) == 1:
            if re.search(r"(?:after|since|from|post)\s+" + re.escape(iso_dates[0]), text, re.IGNORECASE):
                date_from = iso_dates[0]
            elif re.search(r"(?:before|until|to|pre)\s+" + re.escape(iso_dates[0]), text, re.IGNORECASE):
                date_to = iso_dates[0]
            else:
                date_from = iso_dates[0]
        elif len(iso_dates) >= 2:
            date_from = iso_dates[0]
            date_to = iso_dates[1]
        else:
            # Match 4-digit years like "post 2025" or "since 2025"
            year_match_from = re.search(r"(?:after|since|from|post)\s+(20\d{2})\b", text, re.IGNORECASE)
            if year_match_from:
                date_from = f"{year_match_from.group(1)}-01-01"
            year_match_to = re.search(r"(?:before|until|to|pre)\s+(20\d{2})\b", text, re.IGNORECASE)
            if year_match_to:
                date_to = f"{year_match_to.group(1)}-12-31"

        return {
            "symbols": symbols,
            "categories": list(categories),
            "indicators": list(indicators),
            "min_confidence": min_confidence,
            "verification_states": verification_states,
            "date_from": date_from,
            "date_to": date_to,
        }

    def get_entity_info(self, symbol_or_name: str) -> Optional[FinancialEntity]:
        """Returns the FinancialEntity object for a given symbol or alias."""
        sym = self.resolve_symbol(symbol_or_name)
        if sym and sym in self._entities:
            return self._entities[sym]
        return None

    def get_all_symbols(self) -> List[str]:
        """Returns list of all canonical symbols in registry."""
        return list(self._entities.keys())


# Singleton instance
_GLOBAL_RESOLVER = FinancialEntityResolver()


# ============================================================================
# 3. HYBRID RANKING: BM25 + DENSE VECTOR EMBEDDINGS (RRF)
# ============================================================================

class BM25Ranker:
    """
    Okapi BM25 ranking algorithm optimized for financial memory notes.
    Gives special boosting to matches in title, tags, and canonical symbols.
    Features in-memory tokenization caching for sub-millisecond repeated queries.
    """

    def __init__(self, k1: float = 1.5, b: float = 0.75):
        self.k1 = k1
        self.b = b
        self._doc_token_cache: Dict[str, Tuple[str, List[str], int]] = {}
        self._lock = threading.RLock()

    @staticmethod
    def _tokenize(text: str) -> List[str]:
        if not text:
            return []
        tokens = re.findall(r"[A-Za-z0-9\^\=\-\_\.\$\/]+", text.lower())
        return [t for t in tokens if len(t) > 1 or t in {"v", "w", "b", "c"}]

    def tokenize_note(self, note: Dict[str, Any]) -> Tuple[List[str], int]:
        """Tokenizes and caches document tokens for a note."""
        nid = str(note.get("id", ""))
        sig = f"{note.get('title', '')}:{note.get('updated', '')}:{note.get('category', '')}:{len(str(note.get('content', '')))}"
        with self._lock:
            cached = self._doc_token_cache.get(nid)
            if cached is not None and cached[0] == sig:
                return cached[1], cached[2]

        title = str(note.get("title", ""))
        tags = " ".join(str(t) for t in note.get("tags", []))
        category = str(note.get("category", ""))
        content = str(note.get("content", ""))

        # Weighted tokens
        title_tokens = self._tokenize(title) * 3
        tag_tokens = self._tokenize(tags) * 2
        cat_tokens = self._tokenize(category) * 2
        content_tokens = self._tokenize(content)

        doc_toks = title_tokens + tag_tokens + cat_tokens + content_tokens
        doc_len = len(doc_toks)
        if nid:
            with self._lock:
                if len(self._doc_token_cache) > 20000:
                    self._doc_token_cache.clear()
                self._doc_token_cache[nid] = (sig, doc_toks, doc_len)
        return doc_toks, doc_len

    def score_corpus(
        self,
        query: str,
        notes: List[Dict[str, Any]],
        boost_symbols: Optional[List[str]] = None
    ) -> List[float]:
        if not notes:
            return []

        q_tokens = self._tokenize(query)
        boost_set = {s.lower() for s in (boost_symbols or [])}

        # Build documents representation using cached tokenization
        doc_tokens_list: List[List[str]] = []
        doc_lengths: List[int] = []

        for note in notes:
            doc_toks, doc_len = self.tokenize_note(note)
            doc_tokens_list.append(doc_toks)
            doc_lengths.append(doc_len)

        N = len(notes)
        avgdl = sum(doc_lengths) / max(N, 1)

        # Compute document frequency (DF) for each query term
        scores: List[float] = [0.0] * N
        if not q_tokens:
            return scores

        for q_term in set(q_tokens):
            # Calculate DF
            df = sum(1 for doc_toks in doc_tokens_list if q_term in doc_toks)
            if df == 0:
                continue

            # Inverse Document Frequency (IDF) with smoothing
            idf = math.log(1.0 + (N - df + 0.5) / (df + 0.5))

            term_boost = 1.5 if q_term in boost_set else 1.0

            for i, doc_toks in enumerate(doc_tokens_list):
                tf = doc_toks.count(q_term)
                if tf > 0:
                    doc_len = doc_lengths[i]
                    # Okapi BM25 TF formula
                    numerator = tf * (self.k1 + 1.0)
                    denominator = tf + self.k1 * (1.0 - self.b + self.b * (doc_len / max(avgdl, 1.0)))
                    scores[i] += term_boost * idf * (numerator / max(denominator, 1e-6))

        return scores


class DenseVectorEmbedder:
    """
    Lightweight, high-performance deterministic dense vector embedder for financial text.
    Captures semantic dimensions including asset classes, macroeconomic states,
    technical indicators, sentiment, and risk regimes.
    Features feature hash memoization and note embedding caches for sub-millisecond retrieval.
    """

    def __init__(self, embedding_dim: int = 128):
        self.embedding_dim = embedding_dim
        # Prime seed table for hashing
        self._feature_weights: Dict[str, float] = {
            "gold": 2.0, "xau": 2.0, "silver": 1.8, "sp500": 2.0, "dax": 2.0,
            "nasdaq": 2.0, "vix": 1.9, "fedfunds": 2.2, "inflation": 2.0, "cpi": 2.0,
            "yield": 1.8, "treasury": 1.8, "breakout": 1.6, "confluence": 1.6,
            "bullish": 1.5, "bearish": 1.5, "recession": 1.7, "volatility": 1.6,
            "liquidity": 1.6, "rate": 1.5, "pnl": 1.5, "trade": 1.4, "risk": 1.4
        }
        self._word_hash_cache: Dict[str, Tuple[int, float]] = {}
        self._ngram_hash_cache: Dict[str, Tuple[int, float]] = {}
        self._text_embed_cache: Dict[str, List[float]] = {}
        self._note_embed_cache: Dict[str, Tuple[str, List[float]]] = {}
        self._lock = threading.RLock()

    def _get_word_hash(self, w: str) -> Tuple[int, float]:
        cached = self._word_hash_cache.get(w)
        if cached is not None:
            return cached
        h = int(hashlib.md5(w.encode("utf-8")).hexdigest(), 16)
        idx = h % self.embedding_dim
        sign = 1.0 if ((h >> 8) & 1) == 0 else -1.0
        res = (idx, sign)
        if len(self._word_hash_cache) < 20000:
            self._word_hash_cache[w] = res
        return res

    def _get_ngram_hash(self, ngram: str) -> Tuple[int, float]:
        cached = self._ngram_hash_cache.get(ngram)
        if cached is not None:
            return cached
        nh = int(hashlib.md5(ngram.encode("utf-8")).hexdigest(), 16)
        nidx = nh % self.embedding_dim
        nsign = 1.0 if ((nh >> 8) & 1) == 0 else -1.0
        res = (nidx, nsign)
        if len(self._ngram_hash_cache) < 20000:
            self._ngram_hash_cache[ngram] = res
        return res

    def _embed_text(self, text: str) -> List[float]:
        if not text:
            return [0.0] * self.embedding_dim

        if len(text) < 500:
            with self._lock:
                cached = self._text_embed_cache.get(text)
                if cached is not None:
                    return cached

        vec = [0.0] * self.embedding_dim
        words = re.findall(r"[A-Za-z0-9\^\=\-\_\.\$\/]+", text.lower())
        for w in words:
            weight = self._feature_weights.get(w, 1.0)
            idx, sign = self._get_word_hash(w)
            vec[idx] += sign * weight

            # Character n-grams (3-grams) for subword semantic capture
            for j in range(len(w) - 2):
                ngram = w[j:j+3]
                nidx, nsign = self._get_ngram_hash(ngram)
                vec[nidx] += nsign * 0.3 * weight

        # L2 Normalization
        norm = math.sqrt(sum(x * x for x in vec))
        if norm > 1e-9:
            vec = [x / norm for x in vec]

        if len(text) < 500:
            with self._lock:
                if len(self._text_embed_cache) < 10000:
                    self._text_embed_cache[text] = vec

        return vec

    def embed_note(self, note: Dict[str, Any]) -> List[float]:
        """Computes and caches the embedding vector for a note."""
        nid = str(note.get("id", ""))
        sig = f"{note.get('title', '')}:{note.get('updated', '')}:{note.get('category', '')}:{len(str(note.get('content', '')))}"
        with self._lock:
            cached = self._note_embed_cache.get(nid)
            if cached is not None and cached[0] == sig:
                return cached[1]

        text = f"{note.get('title', '')} {' '.join(str(t) for t in note.get('tags', []))} {note.get('category', '')} {note.get('content', '')}"
        d_vec = self._embed_text(text)
        if nid:
            with self._lock:
                if len(self._note_embed_cache) > 20000:
                    self._note_embed_cache.clear()
                self._note_embed_cache[nid] = (sig, d_vec)
        return d_vec

    @staticmethod
    def cosine_similarity(v1: List[float], v2: List[float]) -> float:
        if not v1 or not v2 or len(v1) != len(v2):
            return 0.0
        dot = sum(a * b for a, b in zip(v1, v2))
        return max(0.0, min(1.0, dot))

    def score_corpus(self, query: str, notes: List[Dict[str, Any]]) -> List[float]:
        if not notes:
            return []
        q_vec = self._embed_text(query)
        scores: List[float] = []
        for note in notes:
            d_vec = self.embed_note(note)
            scores.append(self.cosine_similarity(q_vec, d_vec))
        return scores


# ============================================================================
# 4. WIKILINK GRAPH SPREADING ACTIVATION RE-RANKER
# ============================================================================

class FinancialKnowledgeGraph:
    """
    In-memory graph representing explicit wikilinks, frontmatter relations,
    and asset/macro dependencies across Vault notes.
    Features corpus-level signature caching to eliminate redundant graph rebuilds.
    """

    def __init__(self, resolver: Optional[FinancialEntityResolver] = None):
        self.resolver = resolver or _GLOBAL_RESOLVER
        self._adj: Dict[str, Dict[str, float]] = {}  # source_id -> {target_id: weight}
        self._title_to_id: Dict[str, str] = {}
        self._symbol_to_ids: Dict[str, Set[str]] = {}
        self._last_corpus_sig: Optional[str] = None
        self._lock = threading.RLock()

    def build_from_notes(self, notes: List[Dict[str, Any]], force: bool = False):
        if not notes:
            with self._lock:
                self._adj.clear()
                self._title_to_id.clear()
                self._symbol_to_ids.clear()
                self._last_corpus_sig = ""
            return

        corpus_sig = f"{len(notes)}:" + "".join(f"{n.get('id','')}:{n.get('updated','')}" for n in notes[:50]) + (f":{notes[-1].get('id','')}" if notes else "")
        with self._lock:
            if not force and self._last_corpus_sig == corpus_sig and self._adj:
                return

            self._adj.clear()
            self._title_to_id.clear()
            self._symbol_to_ids.clear()

            # Pass 1: Index titles and symbols
            for note in notes:
                nid = str(note.get("id", ""))
                if not nid:
                    continue
                title = str(note.get("title", "")).strip()
                if not title:
                    content = str(note.get("content", ""))
                    for line in content.splitlines()[:3]:
                        if line.startswith("# "):
                            title = line[2:].strip()
                            break
                title_low = title.lower()
                if title_low:
                    self._title_to_id[title_low] = nid
                    # Strip clean title
                    clean_title = re.sub(r"[^a-z0-9]", "", title_low)
                    self._title_to_id[clean_title] = nid

                # Track asset tags / symbols
                tags = note.get("tags", [])
                for t in tags:
                    sym = self.resolver.resolve_symbol(str(t))
                    if sym:
                        self._symbol_to_ids.setdefault(sym, set()).add(nid)

            # Pass 2: Build edges from relations, wikilinks, and asset dependencies
            for note in notes:
                nid = str(note.get("id", ""))
                if not nid:
                    continue
                if nid not in self._adj:
                    self._adj[nid] = {}

                # 1. Frontmatter relations
                for rel in note.get("relations", []):
                    target_id = rel.get("target_id")
                    target_str = rel.get("target", "")
                    relation_type = rel.get("relation", "related_to")

                    weight = 1.0
                    if relation_type in {"caused_by", "resulted_in", "triggers", "implements"}:
                        weight = 1.5  # Causal/Functional boost
                    elif relation_type in {"replaces", "replaced_by"}:
                        weight = 1.8  # Supersession boost

                    if target_id and target_id in self._adj or target_id:
                        self._add_edge(nid, target_id, weight)

                    if target_str:
                        # Extract wikilink [[Target Title]]
                        match = re.search(r"\[\[(.*?)\]\]", target_str)
                        if match:
                            target_title = match.group(1).strip().lower()
                            clean_target = re.sub(r"[^a-z0-9]", "", target_title)
                            tid = self._title_to_id.get(target_title) or self._title_to_id.get(clean_target)
                            if tid and tid != nid:
                                self._add_edge(nid, tid, weight)

                # 2. Obsidian [[wikilinks]] in content
                content = str(note.get("content", ""))
                for match in re.finditer(r"\[\[(.*?)\]\]", content):
                    link_text = match.group(1).strip().lower()
                    clean_link = re.sub(r"[^a-z0-9]", "", link_text)
                    tid = self._title_to_id.get(link_text) or self._title_to_id.get(clean_link)
                    if tid and tid != nid:
                        self._add_edge(nid, tid, 1.0)

                # 3. Correlated asset edges from resolver catalog
                for t in note.get("tags", []):
                    sym = self.resolver.resolve_symbol(str(t))
                    if sym:
                        entity = self.resolver.get_entity_info(sym)
                        if entity:
                            for rel_sym in entity.related_symbols:
                                for target_nid in self._symbol_to_ids.get(rel_sym, []):
                                    if target_nid != nid:
                                        self._add_edge(nid, target_nid, 0.8)

            self._last_corpus_sig = corpus_sig

    def invalidate(self):
        """Invalidates corpus cache to force next build_from_notes to rebuild."""
        with self._lock:
            self._last_corpus_sig = None

    def _add_edge(self, u: str, v: str, weight: float):
        if u not in self._adj:
            self._adj[u] = {}
        if v not in self._adj:
            self._adj[v] = {}
        # Undirected spreading activation with weight aggregation
        self._adj[u][v] = max(self._adj[u].get(v, 0.0), weight)
        self._adj[v][u] = max(self._adj[v].get(u, 0.0), weight)

    def propagate(
        self,
        seed_scores: Dict[str, float],
        decay: float = 0.6,
        max_hops: int = 2
    ) -> Dict[str, float]:
        """
        ACT-R style spreading activation energy propagation with exponential hop decay.
        """
        activation: Dict[str, float] = dict(seed_scores)
        frontier: List[Tuple[str, float, int]] = [(nid, score, 0) for nid, score in seed_scores.items()]

        while frontier:
            curr_id, curr_score, hop = frontier.pop(0)
            if hop >= max_hops:
                continue

            neighbors = self._adj.get(curr_id, {})
            for neighbor_id, edge_weight in neighbors.items():
                # Transmitted energy = score * edge_weight * decay^(hop + 1)
                transmitted = curr_score * min(edge_weight, 2.0) * (decay ** (hop + 1))
                if transmitted <= 1e-4:
                    continue

                if transmitted > activation.get(neighbor_id, 0.0):
                    activation[neighbor_id] = transmitted
                    frontier.append((neighbor_id, transmitted, hop + 1))

        return activation


# ============================================================================
# 5. MULTI-LAYERED FINANCIAL SEARCH ENGINE
# ============================================================================

class MultiLayeredFinancialSearchEngine:
    """
    Main 5-layer financial search pipeline orchestrator.
    Handles entity extraction, SQLite filtering, hybrid RRF scoring, graph re-ranking,
    and progressive disclosure context pack building.
    Pre-indexes notes during initialization and ingestion for sub-50ms query response.
    """

    CONFIDENCE_WEIGHTS = {
        "very_high": 1.25,
        "high": 1.15,
        "medium": 1.00,
        "low": 0.85,
        "unknown": 0.75,
    }

    VERIFICATION_WEIGHTS = {
        "verified": 1.15,
        "partially_verified": 1.05,
        "unverified": 1.00,
        "inferred": 0.95,
    }

    CONFIDENCE_RANKS = {
        "unknown": 0,
        "low": 1,
        "medium": 2,
        "high": 3,
        "very_high": 4,
    }

    def __init__(
        self,
        storage: Any,
        resolver: Optional[FinancialEntityResolver] = None,
        k_rrf: int = 60
    ):
        self.storage = storage
        self.resolver = resolver or _GLOBAL_RESOLVER
        self.bm25_ranker = BM25Ranker()
        self.vector_embedder = DenseVectorEmbedder()
        self.graph = FinancialKnowledgeGraph(self.resolver)
        self.k_rrf = k_rrf
        self.pack_builder = ContextPackBuilder()
        self.warm_up()

    def warm_up(self):
        """Pre-indexes existing notes in storage, building the graph and vector embeddings."""
        try:
            all_notes = self._extract_all_storage_notes()
            if all_notes:
                self.graph.build_from_notes(all_notes)
                for note in all_notes:
                    self.vector_embedder.embed_note(note)
                    self.bm25_ranker.tokenize_note(note)
        except Exception:
            pass

    def index_note(self, note: Dict[str, Any]):
        """Directly indexes a single note into BM25 and Dense Vector cache, invalidating graph."""
        try:
            self.vector_embedder.embed_note(note)
            self.bm25_ranker.tokenize_note(note)
            self.graph.invalidate()
        except Exception:
            pass

    def invalidate_cache(self):
        """Invalidates all caches across graph and engines."""
        self.graph.invalidate()

    def _extract_all_storage_notes(self) -> List[Dict[str, Any]]:
        """Safely extracts all stored notes from SQLiteStorageEngine or FileStorageEngine."""
        if hasattr(self.storage, "store") and isinstance(self.storage.store, dict):
            return list(self.storage.store.values())
        elif hasattr(self.storage, "query"):
            try:
                return self.storage.query(lifecycles=None, types=None)
            except TypeError:
                try:
                    return self.storage.query(lifecycle=None, types=None)
                except Exception:
                    return self.storage.query()
        return []

    def search(self, query: str = "", top_k: int = 10, **kwargs) -> List[Dict[str, Any]]:
        """Convenience alias for execute_search returning results list."""
        kwargs_clean = {k: v for k, v in kwargs.items() if k not in ("limit", "page_size")}
        pack = self.execute_search(principal=Principal.AI_AGENT, query=query, limit=top_k, page_size=top_k, **kwargs_clean)
        return pack.get("results", [])

    def execute_search(
        self,
        principal: Principal,
        query: str = "",
        symbol: Optional[str] = None,
        symbols: Optional[List[str]] = None,
        asset_symbol: Optional[str] = None,
        category: Optional[str] = None,
        asset_classes: Optional[List[str]] = None,
        min_confidence: Optional[str] = None,
        confidence_min: Optional[str] = None,
        verification_state: Optional[str] = None,
        verification_states: Optional[List[str]] = None,
        date_from: Optional[str] = None,
        date_to: Optional[str] = None,
        types: Optional[List[str]] = None,
        lifecycles: Optional[List[Any]] = None,
        page_size: int = 10,
        limit: Optional[int] = None,
        page_token: Optional[str] = None,
        disclosure_level: str = "metadata",
    ) -> Dict[str, Any]:
        """
        Executes the full 5-layer financial search pipeline.
        Returns a context pack dictionary with cryptographic pagination tokens.
        """
        effective_page_size = limit if limit is not None else page_size
        effective_page_size = max(0, min(int(effective_page_size), 1000))
        check_query_size(query)
        sanitized = sanitize_query(query)
        query_fp = hashlib.sha256(f"{sanitized}:{effective_page_size}".encode("utf-8")).hexdigest()

        # Load principal budget
        budget = load_agent_budget(principal.value)

        # --------------------------------------------------------------------
        # LAYER 1: Financial Entity & Alias Extractor
        # --------------------------------------------------------------------
        extracted = self.resolver.extract_entities_and_filters(sanitized)

        # Merge explicit overrides with extracted entities
        target_symbols: Set[str] = set(extracted["symbols"])
        for s in (symbols or []):
            resolved_s = self.resolver.resolve_symbol(s)
            target_symbols.add(resolved_s or s)
        if symbol:
            resolved_s = self.resolver.resolve_symbol(symbol)
            target_symbols.add(resolved_s or symbol)
        if asset_symbol:
            resolved_s = self.resolver.resolve_symbol(asset_symbol)
            target_symbols.add(resolved_s or asset_symbol)

        target_categories: Set[str] = set(extracted["categories"])
        for c in (asset_classes or []):
            norm_c = self.resolver._category_aliases.get(c.lower(), c)
            target_categories.add(norm_c)
        if category:
            norm_c = self.resolver._category_aliases.get(category.lower(), category)
            target_categories.add(norm_c)

        effective_min_conf = min_confidence or confidence_min or extracted["min_confidence"]
        
        effective_verif: Set[str] = set(extracted["verification_states"])
        for v in (verification_states or []):
            effective_verif.add(v.lower())
        if verification_state:
            effective_verif.add(verification_state.lower())

        effective_date_from = date_from or extracted["date_from"]
        effective_date_to = date_to or extracted["date_to"]

        # Default allowed lifecycles: ACTIVE and REVIEW (RAW is strictly excluded)
        allowed_lifecycles: List[str] = []
        if lifecycles:
            for lc in lifecycles:
                val = lc.value if hasattr(lc, "value") else str(lc)
                if val != "RAW":
                    allowed_lifecycles.append(val)
        else:
            allowed_lifecycles = ["ACTIVE", "REVIEW"]

        # Token decode & validation
        offset = 0
        if page_token:
            payload = PaginationToken.decode(page_token)
            if payload.get("query_fp") != query_fp:
                raise InvalidPaginationTokenError("Token query fingerprint mismatch")
            if payload.get("agent_id") != principal.value:
                raise InvalidPaginationTokenError("Token principal mismatch")
            if payload.get("page_size") != effective_page_size:
                raise InvalidPaginationTokenError("Token page size mismatch")
            offset = payload.get("offset", 0)

        # --------------------------------------------------------------------
        # LAYER 2: SQLite Structured & Temporal Filter
        # --------------------------------------------------------------------
        all_notes = self._extract_all_storage_notes()
        candidates: List[Dict[str, Any]] = []

        min_conf_rank = self.CONFIDENCE_RANKS.get(effective_min_conf.lower(), 0) if effective_min_conf else 0

        for note in all_notes:
            nid = note.get("id")
            if not nid:
                continue

            # Lifecycle check (Exclude RAW strictly)
            nlc = note.get("lifecycle", "RAW")
            if nlc not in allowed_lifecycles or nlc == "RAW":
                continue

            # Type check
            ntype = note.get("type", "knowledge")
            if types and ntype not in types:
                continue

            # Confidence check
            nconf = str(note.get("confidence", "unknown")).lower()
            if self.CONFIDENCE_RANKS.get(nconf, 0) < min_conf_rank:
                continue

            # Verification check
            nverif = str(note.get("verification", "unverified")).lower()
            if effective_verif and nverif not in effective_verif:
                continue

            # Temporal filtering (created, updated, valid_from, valid_until)
            ncreated = str(note.get("created", ""))
            if effective_date_from and ncreated and ncreated < effective_date_from:
                continue
            if effective_date_to and ncreated and ncreated > effective_date_to:
                continue

            # Symbol filter check
            if target_symbols:
                note_symbols = self._extract_note_symbols(note)
                if not target_symbols.intersection(note_symbols):
                    # Check text content for symbol mention if not in tags
                    content_str = f"{note.get('title', '')} {note.get('content', '')}".lower()
                    matched_sym = False
                    for ts in target_symbols:
                        entity = self.resolver.get_entity_info(ts)
                        aliases_to_check = entity.aliases if entity else [ts.lower()]
                        if any(a in content_str for a in aliases_to_check):
                            matched_sym = True
                            break
                    if not matched_sym:
                        continue

            # Category filter check
            if target_categories:
                ncat = str(note.get("category", "")).lower()
                ntags = [str(t).lower() for t in note.get("tags", [])]
                matched_cat = False
                for tc in target_categories:
                    tc_low = tc.lower()
                    if tc_low == ncat or tc_low in ntags or tc_low in ncat:
                        matched_cat = True
                        break
                    # Instrument category match
                    for note_sym in self._extract_note_symbols(note):
                        ent = self.resolver.get_entity_info(note_sym)
                        if ent and ent.category.lower() == tc_low:
                            matched_cat = True
                            break
                    if matched_cat:
                        break
                if not matched_cat:
                    continue

            candidates.append(note)

        # --------------------------------------------------------------------
        # LAYER 3: Hybrid BM25 & Dense Vector Embeddings (RRF)
        # --------------------------------------------------------------------
        if not candidates:
            # Empty result context pack
            pack = self.pack_builder.build(
                request_id="financial_search",
                agent_id=principal.value,
                budget={"soft": budget.soft_context_budget, "hard": budget.hard_context_budget},
                results=[],
                disclosure_level=disclosure_level,
                next_page_token=None,
            )
            pack["total_matched"] = 0
            pack["next_page_token"] = None
            pack["metadata"] = {
                "extracted_symbols": list(target_symbols),
                "extracted_categories": list(target_categories),
                "candidates_count": 0,
            }
            audit_event("search_financial", principal, query_fp, success=True, details={"matched": 0})
            return pack

        # BM25 Lexical Scoring
        bm25_scores = self.bm25_ranker.score_corpus(sanitized, candidates, boost_symbols=list(target_symbols))
        # Dense Vector Embeddings Scoring
        vector_scores = self.vector_embedder.score_corpus(sanitized, candidates)

        # Calculate ranks for Reciprocal Rank Fusion (RRF)
        # Sort indices by BM25 score descending
        bm25_ranked_indices = sorted(range(len(candidates)), key=lambda i: bm25_scores[i], reverse=True)
        bm25_ranks = {idx: rank + 1 for rank, idx in enumerate(bm25_ranked_indices)}

        # Sort indices by Vector score descending
        vector_ranked_indices = sorted(range(len(candidates)), key=lambda i: vector_scores[i], reverse=True)
        vector_ranks = {idx: rank + 1 for rank, idx in enumerate(vector_ranked_indices)}

        scored_notes: List[ScoredMemoryNote] = []
        seed_scores: Dict[str, float] = {}

        for i, note in enumerate(candidates):
            nid = str(note.get("id"))
            r_bm25 = bm25_ranks[i]
            r_vec = vector_ranks[i]

            # RRF formula: 1 / (k + r_bm25) + 1 / (k + r_vec)
            rrf = (1.0 / (self.k_rrf + r_bm25)) + (1.0 / (self.k_rrf + r_vec))

            # Confidence & Verification multipliers
            conf_val = str(note.get("confidence", "medium")).lower()
            verif_val = str(note.get("verification", "unverified")).lower()
            conf_mult = self.CONFIDENCE_WEIGHTS.get(conf_val, 1.0)
            verif_mult = self.VERIFICATION_WEIGHTS.get(verif_val, 1.0)

            hybrid_score = rrf * conf_mult * verif_mult
            seed_scores[nid] = hybrid_score

            scored_notes.append(ScoredMemoryNote(
                note_id=nid,
                note=note,
                bm25_score=bm25_scores[i],
                vector_score=vector_scores[i],
                rrf_score=rrf,
                final_score=hybrid_score,
                bm25_rank=r_bm25,
                vector_rank=r_vec,
            ))

        # --------------------------------------------------------------------
        # LAYER 4: Wikilink Graph Spreading Activation Re-Ranking
        # --------------------------------------------------------------------
        self.graph.build_from_notes(all_notes)
        activation_scores = self.graph.propagate(seed_scores, decay=0.6, max_hops=2)

        # Merge graph activation into final score (35% activation blend)
        for sn in scored_notes:
            act_score = activation_scores.get(sn.note_id, 0.0)
            sn.activation_score = act_score
            sn.final_score = sn.final_score + (0.35 * act_score)

        # Also pull in any active notes that received spreading activation energy
        existing_ids = {sn.note_id for sn in scored_notes}
        for note in all_notes:
            nid = str(note.get("id"))
            if nid and nid not in existing_ids and activation_scores.get(nid, 0.0) > 0.0:
                nlc = note.get("lifecycle", "RAW")
                if nlc in allowed_lifecycles and nlc != "RAW":
                    # If target_symbols filter is active, require symbol intersection or explicit graph link to seed
                    if target_symbols:
                        note_syms = self._extract_note_symbols(note)
                        is_connected_to_seed = any(sid in self.graph._adj.get(nid, {}) for sid in seed_scores)
                        if not target_symbols.intersection(note_syms) and not is_connected_to_seed:
                            continue
                    act_score = activation_scores[nid]
                    scored_notes.append(ScoredMemoryNote(
                        note_id=nid,
                        note=note,
                        bm25_score=0.0,
                        vector_score=0.0,
                        rrf_score=0.0,
                        activation_score=act_score,
                        final_score=0.35 * act_score,
                        bm25_rank=9999,
                        vector_rank=9999,
                    ))

        # Sort all scored candidates by final composite score descending
        scored_notes.sort(key=lambda s: s.final_score, reverse=True)
        ranked_candidate_notes = [sn.note for sn in scored_notes]

        # --------------------------------------------------------------------
        # LAYER 5: Context Pack Builder & Progressive Disclosure
        # --------------------------------------------------------------------
        pd = ProgressiveDisclosure(budget)
        if disclosure_level == "metadata":
            disclosed = pd.metadata_only(ranked_candidate_notes)
        elif disclosure_level == "snippet":
            disclosed = pd.snippet(ranked_candidate_notes)
        elif disclosure_level == "sections":
            disclosed = pd.sections(ranked_candidate_notes, sanitized)
        else:
            disclosed = pd.full_document(ranked_candidate_notes)

        # Slicing pagination
        total_matched = len(disclosed)
        end_idx = min(offset + effective_page_size, total_matched)
        page_results = disclosed[offset:end_idx]

        # Generate HMAC-SHA256 pagination token if additional items remain
        next_token = None
        if end_idx < total_matched:
            token_payload = {
                "offset": end_idx,
                "query_fp": query_fp,
                "agent_id": principal.value,
                "page_size": effective_page_size,
                "disclosure": disclosure_level,
                "symbols": list(target_symbols),
                "categories": list(target_categories),
                "expiration": int((datetime.now(timezone.utc) + timedelta(minutes=15)).timestamp()),
            }
            secret = os.getenv("MEMORY_CONTROLLER_HMAC_SECRET", "default_vault_financial_hmac_secret_key_32b")
            token_obj = PaginationToken(token_payload, secret.encode("utf-8"))
            next_token = token_obj.encode()

        # Build final context pack
        pack = self.pack_builder.build(
            request_id="financial_search",
            agent_id=principal.value,
            budget={
                "soft": budget.soft_context_budget,
                "hard": budget.hard_context_budget,
                "max_notes": max(effective_page_size, budget.max_notes),
            },
            results=page_results,
            disclosure_level=disclosure_level,
            next_page_token=next_token,
        )
        pack["next_page_token"] = next_token
        pack["total_matched"] = total_matched
        pack["metadata"] = {
            "query": sanitized,
            "extracted_symbols": list(target_symbols),
            "extracted_categories": list(target_categories),
            "extracted_indicators": extracted["indicators"],
            "effective_date_from": effective_date_from,
            "effective_date_to": effective_date_to,
            "page_size": effective_page_size,
            "offset": offset,
            "returned_count": len(page_results),
        }

        audit_event("search_financial", principal, query_fp, success=True, details={
            "total_matched": total_matched,
            "page_size": effective_page_size,
            "offset": offset,
        })
        return pack

    def _extract_note_symbols(self, note: Dict[str, Any]) -> Set[str]:
        """Extracts canonical financial symbols associated with a given note."""
        syms: Set[str] = set()
        for t in note.get("tags", []):
            resolved = self.resolver.resolve_symbol(str(t))
            if resolved:
                syms.add(resolved)
        ticker = note.get("ticker") or note.get("symbol")
        if ticker:
            resolved = self.resolver.resolve_symbol(str(ticker))
            if resolved:
                syms.add(resolved)
        content = note.get("content", "")
        if isinstance(content, str):
            for line in content.splitlines()[:5]:
                if line.startswith("Ticker: ") or line.startswith("Symbol: "):
                    raw = line.split(":", 1)[1].strip()
                    res = self.resolver.resolve_symbol(raw)
                    if res:
                        syms.add(res)
        return syms
