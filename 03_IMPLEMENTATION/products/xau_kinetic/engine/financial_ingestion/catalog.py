"""
Financial Instruments & Macroeconomic Catalog.
Provides structured metadata, classification, sector mappings, currency bases,
risk libraries, competitor matrices, and economic calendars for 95 financial instruments,
5 macroeconomic benchmark tickers, and 4 St. Louis Fed (FRED) series.
"""

from dataclasses import dataclass, field
from typing import Dict, List, Optional


@dataclass(frozen=True)
class Instrument:
    """Represents a financial instrument in the quantitative ingestion catalog."""
    name: str
    symbol: str
    category: str
    sector: str
    currency_base: str
    description: str
    competitors: List[str] = field(default_factory=list)
    calendar_events: List[str] = field(default_factory=list)
    risk_factors: List[Dict[str, str]] = field(default_factory=list)


@dataclass(frozen=True)
class MacroTicker:
    """Represents a macroeconomic benchmark ticker."""
    name: str
    symbol: str
    description: str
    unit: str
    source: str = "yfinance"


@dataclass(frozen=True)
class FREDSeries:
    """Represents a Federal Reserve Economic Data (FRED) series."""
    series_id: str
    name: str
    frequency: str
    units: str
    description: str
    category: str = "Macroeconomics"


# ============================================================================
# 1. CORE TICKER REGISTRIES (95 Instruments across 5 Categories)
# ============================================================================

INDICI: Dict[str, str] = {
    "S&P 500": "^GSPC",
    "NASDAQ 100": "^NDX",
    "NASDAQ Comp.": "^IXIC",
    "Dow Jones": "^DJI",
    "Russell 2000": "^RUT",
    "DAX Germany": "^GDAXI",
    "FTSE 100": "^FTSE",
    "CAC 40": "^FCHI",
    "Nikkei 225": "^N225",
    "Hang Seng": "^HSI",
    "Shanghai": "000001.SS",
    "MSCI World": "URTH",
    "MSCI EM": "EEM",
    "BET Romania": "BET.RO",
}

ACTIUNI: Dict[str, str] = {
    "Apple": "AAPL",
    "Microsoft": "MSFT",
    "NVIDIA": "NVDA",
    "Alphabet": "GOOGL",
    "Amazon": "AMZN",
    "Meta": "META",
    "Tesla": "TSLA",
    "Berkshire B": "BRK-B",
    "JPMorgan": "JPM",
    "Visa": "V",
    "UnitedHealth": "UNH",
    "Exxon Mobil": "XOM",
    "Johnson&Johnson": "JNJ",
    "Procter&Gamble": "PG",
    "ASML": "ASML",
    "Samsung": "005930.KS",
    "TSMC": "TSM",
    "Netflix": "NFLX",
    "Adobe": "ADBE",
    "Salesforce": "CRM",
    "Palantir": "PLTR",
    "AMD": "AMD",
    "Intel": "INTC",
    "Broadcom": "AVGO",
    "Qualcomm": "QCOM",
    "PayPal": "PYPL",
    "Coinbase": "COIN",
    "Robinhood": "HOOD",
    "Cathie Wood ARK": "ARKK",
    "SPY ETF": "SPY",
}

CRYPTO: Dict[str, str] = {
    "Bitcoin": "BTC-USD",
    "Ethereum": "ETH-USD",
    "BNB": "BNB-USD",
    "Solana": "SOL-USD",
    "XRP": "XRP-USD",
    "Cardano": "ADA-USD",
    "Avalanche": "AVAX-USD",
    "Polkadot": "DOT-USD",
    "Polygon": "MATIC-USD",
    "Chainlink": "LINK-USD",
    "Uniswap": "UNI-USD",
    "Litecoin": "LTC-USD",
    "Dogecoin": "DOGE-USD",
    "Shiba Inu": "SHIB-USD",
    "TRON": "TRX-USD",
    "Stellar": "XLM-USD",
    "Cosmos": "ATOM-USD",
    "Monero": "XMR-USD",
    "Filecoin": "FIL-USD",
    "Internet Computer": "ICP-USD",
    "Hedera": "HBAR-USD",
    "VeChain": "VET-USD",
    "Algorand": "ALGO-USD",
    "Fantom": "FTM-USD",
    "NEAR Protocol": "NEAR-USD",
}

VALUTE: Dict[str, str] = {
    "EUR/USD": "EURUSD=X",
    "GBP/USD": "GBPUSD=X",
    "USD/JPY": "USDJPY=X",
    "USD/CHF": "USDCHF=X",
    "AUD/USD": "AUDUSD=X",
    "USD/CAD": "USDCAD=X",
    "NZD/USD": "NZDUSD=X",
    "EUR/GBP": "EURGBP=X",
    "EUR/JPY": "EURJPY=X",
    "USD/CNY": "USDCNY=X",
    "USD/HUF": "USDHUF=X",
    "USD/TRY": "USDTRY=X",
}

MATERII_PRIME: Dict[str, str] = {
    "Gold": "GC=F",
    "Silver": "SI=F",
    "Oil WTI": "CL=F",
    "Oil Brent": "BZ=F",
    "Natural Gas": "NG=F",
    "Copper": "HG=F",
    "Platinum": "PL=F",
    "Palladium": "PA=F",
    "Corn": "ZC=F",
    "Wheat": "ZW=F",
    "Soybeans": "ZS=F",
    "Coffee": "KC=F",
    "Sugar": "SB=F",
    "Cotton": "CT=F",
}

ACTIVE: Dict[str, str] = {
    **INDICI,
    **ACTIUNI,
    **CRYPTO,
    **VALUTE,
    **MATERII_PRIME,
}

# ============================================================================
# 2. MACRO TICKERS & FRED SERIES
# ============================================================================

MACRO_TICKERS: Dict[str, str] = {
    "VIX": "^VIX",
    "Yield 10Y US": "^TNX",
    "Yield 2Y US": "^IRX",
    "Yield 30Y US": "^TYX",
    "USD Index": "DX-Y.NYB",
}

MACRO_METADATA: Dict[str, MacroTicker] = {
    "^VIX": MacroTicker(
        name="VIX",
        symbol="^VIX",
        description="CBOE Volatility Index - Market Fear Gauge",
        unit="Index Points",
    ),
    "^TNX": MacroTicker(
        name="Yield 10Y US",
        symbol="^TNX",
        description="US 10-Year Treasury Yield Benchmark",
        unit="Percent (%)",
    ),
    "^IRX": MacroTicker(
        name="Yield 2Y US",
        symbol="^IRX",
        description="US 13-Week Treasury Bill Yield / Short-term Policy Rate Proxy",
        unit="Percent (%)",
    ),
    "^TYX": MacroTicker(
        name="Yield 30Y US",
        symbol="^TYX",
        description="US 30-Year Treasury Bond Yield",
        unit="Percent (%)",
    ),
    "DX-Y.NYB": MacroTicker(
        name="USD Index",
        symbol="DX-Y.NYB",
        description="US Dollar Currency Index (DXY) against basket of 6 major currencies",
        unit="Index Points",
    ),
}

FRED_SERIES: Dict[str, FREDSeries] = {
    "FEDFUNDS": FREDSeries(
        series_id="FEDFUNDS",
        name="Federal Funds Effective Rate",
        frequency="Monthly/Daily",
        units="Percent",
        description="Effective policy interest rate set by the Federal Open Market Committee (FOMC).",
    ),
    "CPIAUCSL": FREDSeries(
        series_id="CPIAUCSL",
        name="Consumer Price Index for All Urban Consumers",
        frequency="Monthly",
        units="Index 1982-1984=100",
        description="Key measure of headline consumer inflation in the United States.",
    ),
    "UNRATE": FREDSeries(
        series_id="UNRATE",
        name="Civilian Unemployment Rate",
        frequency="Monthly",
        units="Percent",
        description="Seasonally adjusted civilian unemployment rate in the United States.",
    ),
    "GDP": FREDSeries(
        series_id="GDP",
        name="Gross Domestic Product",
        frequency="Quarterly",
        units="Billions of Dollars",
        description="Seasonally adjusted annual rate of US Gross Domestic Product output.",
    ),
}

# ============================================================================
# 3. SECTOR, COMPETITOR & RISK MAPPINGS
# ============================================================================

COMPETITOR_MAP: Dict[str, List[str]] = {
    "INDICI": ["S&P 500", "NASDAQ 100", "Dow Jones", "DAX Germany", "FTSE 100", "Nikkei 225"],
    "ACTIUNI": ["Apple", "Microsoft", "NVIDIA", "Alphabet", "Amazon", "Meta"],
    "CRYPTO": ["Bitcoin", "Ethereum", "BNB", "Solana", "XRP", "Cardano"],
    "VALUTE": ["EUR/USD", "GBP/USD", "USD/JPY", "USD/CHF", "AUD/USD", "USD/CAD"],
    "MATERII_PRIME": ["Gold", "Silver", "Oil WTI", "Oil Brent", "Natural Gas", "Copper"],
    "MATERII": ["Gold", "Silver", "Oil WTI", "Oil Brent", "Natural Gas", "Copper"],
}

CALENDAR_LIBRARY: Dict[str, List[str]] = {
    "INDICI": ["FOMC Rate Decision", "Non-Farm Payrolls (NFP)", "CPI Inflation", "GDP Release", "Global PMI", "Earnings Season"],
    "ACTIUNI": ["Quarterly Earnings Report", "FOMC Rate Decision", "CPI Inflation", "NFP", "PCE Price Index", "Retail Sales"],
    "CRYPTO": ["Bitcoin Halving", "FOMC Decision", "SEC Regulatory Ruling", "CPI Inflation", "Ethereum Upgrade", "Network Hardfork"],
    "VALUTE": ["FOMC Meeting", "ECB Rate Decision", "BOE Decision", "BOJ Policy Meeting", "US CPI", "NFP"],
    "MATERII_PRIME": ["OPEC+ Ministerial Meeting", "EIA Crude Inventory", "FOMC Decision", "China Manufacturing PMI", "USD DXY Shift", "Geopolitical Summit"],
    "MATERII": ["OPEC+ Ministerial Meeting", "EIA Crude Inventory", "FOMC Decision", "China Manufacturing PMI", "USD DXY Shift", "Geopolitical Summit"],
}

RISK_LIBRARY: Dict[str, List[Dict[str, object]]] = {
    "INDICI": [
        {"ID": "R-I-01", "Tip": "Sistemic", "Categorie": "INDICI", "Descriere": "Recesiune globala / contractie PIB", "Impact": 5, "Probabilitate": 30, "Orizont": "6-12 luni"},
        {"ID": "R-I-02", "Tip": "Macro", "Categorie": "INDICI", "Descriere": "Crestere agresiva rate dobanda FED", "Impact": 4, "Probabilitate": 35, "Orizont": "3-6 luni"},
        {"ID": "R-I-03", "Tip": "Geopolit.", "Categorie": "INDICI", "Descriere": "Conflict armat major / tensiuni globale", "Impact": 4, "Probabilitate": 25, "Orizont": "0-3 luni"},
        {"ID": "R-I-04", "Tip": "Sectorial", "Categorie": "INDICI", "Descriere": "Criza bancara sistemica", "Impact": 5, "Probabilitate": 20, "Orizont": "3-12 luni"},
        {"ID": "R-I-05", "Tip": "Tehnic", "Categorie": "INDICI", "Descriere": "Spargere suport major / Death Cross", "Impact": 3, "Probabilitate": 40, "Orizont": "1-3 luni"},
        {"ID": "R-I-06", "Tip": "Lichid.", "Categorie": "INDICI", "Descriere": "Criza lichiditate / credit crunch", "Impact": 4, "Probabilitate": 20, "Orizont": "6-12 luni"},
    ],
    "ACTIUNI": [
        {"ID": "R-A-01", "Tip": "Earnings", "Categorie": "ACTIUNI", "Descriere": "Rezultate financiare sub asteptari", "Impact": 3, "Probabilitate": 45, "Orizont": "0-1 luni"},
        {"ID": "R-A-02", "Tip": "Macro", "Categorie": "ACTIUNI", "Descriere": "Stagflatie / crestere costuri operationale", "Impact": 4, "Probabilitate": 30, "Orizont": "3-9 luni"},
        {"ID": "R-A-03", "Tip": "Reglem.", "Categorie": "ACTIUNI", "Descriere": "Reglementari antitrust / investigatii", "Impact": 3, "Probabilitate": 25, "Orizont": "6-18 luni"},
        {"ID": "R-A-04", "Tip": "Tehnic", "Categorie": "ACTIUNI", "Descriere": "RSI supraextins / divergenta bearish", "Impact": 2, "Probabilitate": 50, "Orizont": "0-1 luni"},
        {"ID": "R-A-05", "Tip": "Sectorial", "Categorie": "ACTIUNI", "Descriere": "Disruptie tehnologica / obsolescenta", "Impact": 4, "Probabilitate": 20, "Orizont": "12-36 luni"},
        {"ID": "R-A-06", "Tip": "Macro", "Categorie": "ACTIUNI", "Descriere": "Dolar puternic — impact venituri externe", "Impact": 3, "Probabilitate": 35, "Orizont": "3-6 luni"},
    ],
    "CRYPTO": [
        {"ID": "R-C-01", "Tip": "Reglementar", "Categorie": "CRYPTO", "Descriere": "Interdictie / restrictie legala crypto", "Impact": 5, "Probabilitate": 20, "Orizont": "0-6 luni"},
        {"ID": "R-C-02", "Tip": "Tehnic", "Categorie": "CRYPTO", "Descriere": "Spargere suport major / bear market", "Impact": 4, "Probabilitate": 40, "Orizont": "1-3 luni"},
        {"ID": "R-C-03", "Tip": "Hack", "Categorie": "CRYPTO", "Descriere": "Exploit exchange / protocol major", "Impact": 5, "Probabilitate": 15, "Orizont": "0-1 luni"},
        {"ID": "R-C-04", "Tip": "Macro", "Categorie": "CRYPTO", "Descriere": "Risk-off global / fuga spre siguranta", "Impact": 4, "Probabilitate": 35, "Orizont": "0-3 luni"},
        {"ID": "R-C-05", "Tip": "On-chain", "Categorie": "CRYPTO", "Descriere": "Whale dump / manipulare piata", "Impact": 3, "Probabilitate": 30, "Orizont": "0-1 luni"},
        {"ID": "R-C-06", "Tip": "Lichid.", "Categorie": "CRYPTO", "Descriere": "Criza stablecoin / de-peg major", "Impact": 5, "Probabilitate": 10, "Orizont": "0-1 luni"},
    ],
    "VALUTE": [
        {"ID": "R-V-01", "Tip": "Macro", "Categorie": "VALUTE", "Descriere": "Divergenta politici monetare FED/BCE", "Impact": 4, "Probabilitate": 40, "Orizont": "3-6 luni"},
        {"ID": "R-V-02", "Tip": "Geopolit.", "Categorie": "VALUTE", "Descriere": "Criza geopolitica / sanctiuni comerciale", "Impact": 3, "Probabilitate": 25, "Orizont": "0-3 luni"},
        {"ID": "R-V-03", "Tip": "Lichid.", "Categorie": "VALUTE", "Descriere": "Volatilitate extrema weekend / gap", "Impact": 2, "Probabilitate": 30, "Orizont": "0-1 saptamani"},
        {"ID": "R-V-04", "Tip": "Tehnic", "Categorie": "VALUTE", "Descriere": "Interventie banca centrala la nivel cheie", "Impact": 3, "Probabilitate": 20, "Orizont": "0-1 luni"},
        {"ID": "R-V-05", "Tip": "Macro", "Categorie": "VALUTE", "Descriere": "Surpriza CPI / NFP semnificativa", "Impact": 3, "Probabilitate": 35, "Orizont": "0-1 luni"},
        {"ID": "R-V-06", "Tip": "Sistemic", "Categorie": "VALUTE", "Descriere": "Criza valutara piata emergenta", "Impact": 4, "Probabilitate": 15, "Orizont": "3-12 luni"},
    ],
    "MATERII_PRIME": [
        {"ID": "R-M-01", "Tip": "Geopolit.", "Categorie": "MATERII", "Descriere": "Conflict OPEC+ / embargo petrol", "Impact": 5, "Probabilitate": 25, "Orizont": "0-3 luni"},
        {"ID": "R-M-02", "Tip": "Macro", "Categorie": "MATERII", "Descriere": "Incetinire economica China", "Impact": 4, "Probabilitate": 35, "Orizont": "3-12 luni"},
        {"ID": "R-M-03", "Tip": "Meteo", "Categorie": "MATERII", "Descriere": "Fenomene climatice extreme / seceta", "Impact": 3, "Probabilitate": 30, "Orizont": "0-6 luni"},
        {"ID": "R-M-04", "Tip": "USD", "Categorie": "MATERII", "Descriere": "Apreciere USD puternica", "Impact": 3, "Probabilitate": 35, "Orizont": "3-6 luni"},
        {"ID": "R-M-05", "Tip": "Tehnic", "Categorie": "MATERII", "Descriere": "Supraoferta / stocuri in exces", "Impact": 3, "Probabilitate": 25, "Orizont": "3-9 luni"},
        {"ID": "R-M-06", "Tip": "Reglementar", "Categorie": "MATERII", "Descriere": "Reglementari energie verde / carbune", "Impact": 3, "Probabilitate": 20, "Orizont": "12-36 luni"},
    ],
}

# Aliasing
RISK_LIBRARY["MATERII"] = RISK_LIBRARY["MATERII_PRIME"]


# ============================================================================
# 4. EXHAUSTIVE 95 INSTRUMENTS METADATA CATALOG
# ============================================================================

def _build_full_catalog() -> Dict[str, Instrument]:
    cat: Dict[str, Instrument] = {}

    # --- INDICI (14) ---
    indici_details = {
        "^GSPC": ("S&P 500", "US Large-Cap Equity Benchmark", "USD"),
        "^NDX": ("NASDAQ 100", "US Top 100 Non-Financial Tech Benchmark", "USD"),
        "^IXIC": ("NASDAQ Comp.", "All NASDAQ Listed Equities Index", "USD"),
        "^DJI": ("Dow Jones", "US 30 Industrial Mega-Caps", "USD"),
        "^RUT": ("Russell 2000", "US Small-Cap Equity Benchmark", "USD"),
        "^GDAXI": ("DAX Germany", "Germany Top 40 Blue-Chip Index", "EUR"),
        "^FTSE": ("FTSE 100", "UK Top 100 Blue-Chip Index", "GBP"),
        "^FCHI": ("CAC 40", "France Top 40 Benchmark Index", "EUR"),
        "^N225": ("Nikkei 225", "Japan Top 225 Equities Index", "JPY"),
        "^HSI": ("Hang Seng", "Hong Kong Blue-Chip Equity Index", "HKD"),
        "000001.SS": ("Shanghai", "Shanghai Stock Exchange Composite Index", "CNY"),
        "URTH": ("MSCI World", "iShares MSCI World Developed Markets ETF", "USD"),
        "EEM": ("MSCI EM", "iShares MSCI Emerging Markets ETF", "USD"),
        "BET.RO": ("BET Romania", "Bucharest Stock Exchange Top 20 Index", "RON"),
    }
    for sym, (name, desc, curr) in indici_details.items():
        cat[sym] = Instrument(
            name=name,
            symbol=sym,
            category="INDICI",
            sector="Equity Index",
            currency_base=curr,
            description=desc,
            competitors=COMPETITOR_MAP["INDICI"],
            calendar_events=CALENDAR_LIBRARY["INDICI"],
            risk_factors=[{k: str(v) for k, v in r.items()} for r in RISK_LIBRARY["INDICI"]],
        )

    # --- ACTIUNI (30) ---
    actiuni_details = {
        "AAPL": ("Apple", "Technology - Consumer Electronics & Ecosystem", "USD"),
        "MSFT": ("Microsoft", "Technology - Cloud & Enterprise Software", "USD"),
        "NVDA": ("NVIDIA", "Technology - Semiconductors & AI Acceleration", "USD"),
        "GOOGL": ("Alphabet", "Technology - Search, Ads & Cloud Infrastructure", "USD"),
        "AMZN": ("Amazon", "Consumer Discretionary - E-Commerce & AWS Cloud", "USD"),
        "META": ("Meta", "Technology - Social Media & Metaverse/AI", "USD"),
        "TSLA": ("Tesla", "Consumer Discretionary - EV & Clean Energy", "USD"),
        "BRK-B": ("Berkshire B", "Financials - Multi-Sector Conglomerate & Insurance", "USD"),
        "JPM": ("JPMorgan", "Financials - Global Investment Banking & Consumer Banking", "USD"),
        "V": ("Visa", "Financials - Global Digital Payments Network", "USD"),
        "UNH": ("UnitedHealth", "Healthcare - Managed Care & Optum Services", "USD"),
        "XOM": ("Exxon Mobil", "Energy - Integrated Oil & Gas Exploration", "USD"),
        "JNJ": ("Johnson&Johnson", "Healthcare - Pharmaceuticals & Medical Tech", "USD"),
        "PG": ("Procter&Gamble", "Consumer Staples - Household & Personal Care", "USD"),
        "ASML": ("ASML", "Technology - Semiconductor Lithography Equipment", "EUR"),
        "005930.KS": ("Samsung", "Technology - Memory Chips & Electronics", "KRW"),
        "TSM": ("TSMC", "Technology - Semiconductor Foundry Manufacturing", "USD"),
        "NFLX": ("Netflix", "Communication Services - Streaming Entertainment", "USD"),
        "ADBE": ("Adobe", "Technology - Creative & Digital Experience Software", "USD"),
        "CRM": ("Salesforce", "Technology - Enterprise Customer Relationship Management", "USD"),
        "PLTR": ("Palantir", "Technology - Enterprise Big Data & AI Platforms", "USD"),
        "AMD": ("AMD", "Technology - CPUs, GPUs & Data Center Hardware", "USD"),
        "INTC": ("Intel", "Technology - Semiconductor Fabrication & CPUs", "USD"),
        "AVGO": ("Broadcom", "Technology - Semiconductor & Infrastructure Software", "USD"),
        "QCOM": ("Qualcomm", "Technology - Wireless Telecommunications & 5G Chips", "USD"),
        "PYPL": ("PayPal", "Financials - Digital Wallets & Fintech Payments", "USD"),
        "COIN": ("Coinbase", "Financials - Crypto Exchange & Web3 Infrastructure", "USD"),
        "HOOD": ("Robinhood", "Financials - Retail Brokerage & Trading Platform", "USD"),
        "ARKK": ("Cathie Wood ARK", "Financials - ARK Innovation Disruptive Tech ETF", "USD"),
        "SPY": ("SPY ETF", "Financials - SPDR S&P 500 Trust ETF", "USD"),
    }
    for sym, (name, desc, curr) in actiuni_details.items():
        cat[sym] = Instrument(
            name=name,
            symbol=sym,
            category="ACTIUNI",
            sector=desc.split(" - ")[0],
            currency_base=curr,
            description=desc,
            competitors=COMPETITOR_MAP["ACTIUNI"],
            calendar_events=CALENDAR_LIBRARY["ACTIUNI"],
            risk_factors=[{k: str(v) for k, v in r.items()} for r in RISK_LIBRARY["ACTIUNI"]],
        )

    # --- CRYPTO (25) ---
    crypto_details = {
        "BTC-USD": ("Bitcoin", "Layer 1 - Digital Gold & Primary Settlement Asset", "USD"),
        "ETH-USD": ("Ethereum", "Layer 1 - Smart Contracts & Decentralized Finance", "USD"),
        "BNB-USD": ("BNB", "Layer 1 - Binance Ecosystem Utility & Governance Token", "USD"),
        "SOL-USD": ("Solana", "Layer 1 - High-Throughput Monolithic Blockchain", "USD"),
        "XRP-USD": ("XRP", "Payment Infrastructure - Cross-Border Settlement Network", "USD"),
        "ADA-USD": ("Cardano", "Layer 1 - Proof-of-Stake Academic Smart Contracts", "USD"),
        "AVAX-USD": ("Avalanche", "Layer 1 - Subnets & High-Performance Blockchain", "USD"),
        "DOT-USD": ("Polkadot", "Layer 0 - Heterogeneous Multi-Chain Interoperability", "USD"),
        "MATIC-USD": ("Polygon", "Layer 2 - Ethereum Scaling & zkEVM Solutions", "USD"),
        "LINK-USD": ("Chainlink", "Oracle Infrastructure - Decentralized Data Feeds", "USD"),
        "UNI-USD": ("Uniswap", "DeFi - Automated Market Maker & DEX Protocol", "USD"),
        "LTC-USD": ("Litecoin", "Payment - Peer-to-Peer Scrypt Cryptocurrency", "USD"),
        "DOGE-USD": ("Dogecoin", "Payment/Meme - Proof-of-Work Digital Currency", "USD"),
        "SHIB-USD": ("Shiba Inu", "Ecosystem/Meme - Ethereum-Based Decentralized Token", "USD"),
        "TRX-USD": ("TRON", "Layer 1 - High-Volume Stablecoin & Content Settlement", "USD"),
        "XLM-USD": ("Stellar", "Payment - Low-Cost Cross-Border Financial Network", "USD"),
        "ATOM-USD": ("Cosmos", "Layer 0 - Inter-Blockchain Communication Protocol", "USD"),
        "XMR-USD": ("Monero", "Privacy - Untraceable Proof-of-Work Cryptocurrency", "USD"),
        "FIL-USD": ("Filecoin", "Storage - Decentralized Cloud Storage Network", "USD"),
        "ICP-USD": ("Internet Computer", "Layer 1 - Decentralized Web Services & Smart Canisters", "USD"),
        "HBAR-USD": ("Hedera", "Enterprise DLT - Hashgraph Consensus Enterprise Network", "USD"),
        "VET-USD": ("VeChain", "Enterprise - Supply Chain & IoT Tracking Blockchain", "USD"),
        "ALGO-USD": ("Algorand", "Layer 1 - Pure Proof-of-Stake Financial Infrastructure", "USD"),
        "FTM-USD": ("Fantom", "Layer 1 - Directed Acyclic Graph (DAG) Smart Contracts", "USD"),
        "NEAR-USD": ("NEAR Protocol", "Layer 1 - Sharded Developer-Friendly Blockchain", "USD"),
    }
    for sym, (name, desc, curr) in crypto_details.items():
        cat[sym] = Instrument(
            name=name,
            symbol=sym,
            category="CRYPTO",
            sector=desc.split(" - ")[0],
            currency_base=curr,
            description=desc,
            competitors=COMPETITOR_MAP["CRYPTO"],
            calendar_events=CALENDAR_LIBRARY["CRYPTO"],
            risk_factors=[{k: str(v) for k, v in r.items()} for r in RISK_LIBRARY["CRYPTO"]],
        )

    # --- VALUTE (12) ---
    valute_details = {
        "EURUSD=X": ("EUR/USD", "Major FX Pair - Euro vs US Dollar", "USD"),
        "GBPUSD=X": ("GBP/USD", "Major FX Pair - British Pound vs US Dollar", "USD"),
        "USDJPY=X": ("USD/JPY", "Major FX Pair - US Dollar vs Japanese Yen", "JPY"),
        "USDCHF=X": ("USD/CHF", "Major FX Pair - US Dollar vs Swiss Franc", "CHF"),
        "AUDUSD=X": ("AUD/USD", "Major FX Pair - Australian Dollar vs US Dollar", "USD"),
        "USDCAD=X": ("USD/CAD", "Major FX Pair - US Dollar vs Canadian Dollar", "CAD"),
        "NZDUSD=X": ("NZD/USD", "Major FX Pair - New Zealand Dollar vs US Dollar", "USD"),
        "EURGBP=X": ("EUR/GBP", "Cross FX Pair - Euro vs British Pound", "GBP"),
        "EURJPY=X": ("EUR/JPY", "Cross FX Pair - Euro vs Japanese Yen", "JPY"),
        "USDCNY=X": ("USD/CNY", "Emerging FX Pair - US Dollar vs Chinese Yuan", "CNY"),
        "USDHUF=X": ("USD/HUF", "Emerging FX Pair - US Dollar vs Hungarian Forint", "HUF"),
        "USDTRY=X": ("USD/TRY", "Emerging FX Pair - US Dollar vs Turkish Lira", "TRY"),
    }
    for sym, (name, desc, curr) in valute_details.items():
        cat[sym] = Instrument(
            name=name,
            symbol=sym,
            category="VALUTE",
            sector="Foreign Exchange",
            currency_base=curr,
            description=desc,
            competitors=COMPETITOR_MAP["VALUTE"],
            calendar_events=CALENDAR_LIBRARY["VALUTE"],
            risk_factors=[{k: str(v) for k, v in r.items()} for r in RISK_LIBRARY["VALUTE"]],
        )

    # --- MATERII PRIME (14) ---
    materii_details = {
        "GC=F": ("Gold", "Precious Metals - Comex Gold Futures (100 oz)", "USD"),
        "SI=F": ("Silver", "Precious Metals - Comex Silver Futures (5,000 oz)", "USD"),
        "CL=F": ("Oil WTI", "Energy - Nymex Crude Oil WTI Futures (1,000 bbl)", "USD"),
        "BZ=F": ("Oil Brent", "Energy - ICE Brent Crude Futures", "USD"),
        "NG=F": ("Natural Gas", "Energy - Henry Hub Natural Gas Futures", "USD"),
        "HG=F": ("Copper", "Industrial Metals - Comex High Grade Copper Futures", "USD"),
        "PL=F": ("Platinum", "Precious Metals - Nymex Platinum Futures", "USD"),
        "PA=F": ("Palladium", "Precious Metals - Nymex Palladium Futures", "USD"),
        "ZC=F": ("Corn", "Agriculture - CBOT Corn Futures (5,000 bu)", "USD"),
        "ZW=F": ("Wheat", "Agriculture - CBOT Wheat Futures (5,000 bu)", "USD"),
        "ZS=F": ("Soybeans", "Agriculture - CBOT Soybean Futures (5,000 bu)", "USD"),
        "KC=F": ("Coffee", "Agriculture - ICE Coffee C Futures", "USD"),
        "SB=F": ("Sugar", "Agriculture - ICE Sugar No. 11 Futures", "USD"),
        "CT=F": ("Cotton", "Agriculture - ICE Cotton No. 2 Futures", "USD"),
    }
    for sym, (name, desc, curr) in materii_details.items():
        cat[sym] = Instrument(
            name=name,
            symbol=sym,
            category="MATERII_PRIME",
            sector=desc.split(" - ")[0],
            currency_base=curr,
            description=desc,
            competitors=COMPETITOR_MAP["MATERII_PRIME"],
            calendar_events=CALENDAR_LIBRARY["MATERII_PRIME"],
            risk_factors=[{k: str(v) for k, v in r.items()} for r in RISK_LIBRARY["MATERII_PRIME"]],
        )

    return cat


_FULL_CATALOG: Dict[str, Instrument] = _build_full_catalog()


# ============================================================================
# 5. PUBLIC QUERY API
# ============================================================================

def get_catalog() -> Dict[str, Instrument]:
    """Returns a dictionary of all 95 instruments indexed by their ticker symbol."""
    return _FULL_CATALOG


def get_instrument(symbol_or_name: str) -> Optional[Instrument]:
    """
    Finds an instrument by its ticker symbol or friendly display name.
    Case-insensitive matching.
    """
    if not symbol_or_name:
        return None
    
    # 1. Direct symbol lookup
    if symbol_or_name in _FULL_CATALOG:
        return _FULL_CATALOG[symbol_or_name]
    
    query = symbol_or_name.strip().lower()
    
    # 2. Case-insensitive symbol lookup
    for sym, inst in _FULL_CATALOG.items():
        if sym.lower() == query:
            return inst
            
    # 3. Case-insensitive name lookup
    for inst in _FULL_CATALOG.values():
        if inst.name.lower() == query:
            return inst
            
    # 4. Friendly mapping in ACTIVE
    for friendly_name, sym in ACTIVE.items():
        if friendly_name.lower() == query and sym in _FULL_CATALOG:
            return _FULL_CATALOG[sym]
            
    return None


def get_instruments_by_category(category: str) -> List[Instrument]:
    """Returns all instruments within a specific asset category."""
    cat_upper = category.strip().upper()
    if cat_upper == "MATERII":
        cat_upper = "MATERII_PRIME"
    return [inst for inst in _FULL_CATALOG.values() if inst.category == cat_upper]


def get_macro_tickers() -> Dict[str, MacroTicker]:
    """Returns all 5 macroeconomic benchmark tickers."""
    return MACRO_METADATA


def get_fred_series() -> Dict[str, FREDSeries]:
    """Returns all 4 Federal Reserve Economic Data series."""
    return FRED_SERIES


def get_competitors_for_category(category: str) -> List[str]:
    """Returns competitor peer names for a given category."""
    cat_upper = category.strip().upper()
    return COMPETITOR_MAP.get(cat_upper, [])


def get_risks_for_category(category: str) -> List[Dict[str, object]]:
    """Returns risk matrix records for a given category."""
    cat_upper = category.strip().upper()
    return RISK_LIBRARY.get(cat_upper, [])


def get_calendar_events(category: str) -> List[str]:
    """Returns standard economic calendar events for a given category."""
    cat_upper = category.strip().upper()
    return CALENDAR_LIBRARY.get(cat_upper, [])
