"""
Trading Bot — World Monitor Integration
Preia date geopolitice, stiri, conflicte, macro radar, si CII din World Monitor API
+ fallback pe surse RSS publice (Reuters, Bloomberg, FT, etc.)
Datele sunt folosite in Ghid Practic pentru a explica CE s-a intamplat si DE CE.
"""
import logging
import json
from datetime import datetime, timedelta
from dataclasses import dataclass, field
from typing import List, Dict, Optional
import requests
import xml.etree.ElementTree as ET

log = logging.getLogger("tradingbot.worldmonitor")

# ══════════════════════════════════════════════════════════════════
# CONFIG
# ══════════════════════════════════════════════════════════════════

WM_API_BASE = "https://api.worldmonitor.app"
WM_TIMEOUT = 10

# World Monitor agrega 435+ RSS feeds. Acestea sunt sursele directe publice
# pe care le putem accesa fara restrictii CORS.
FINANCE_RSS = [
    ("Reuters Business", "https://www.reutersagency.com/feed/?best-topics=business-finance"),
    ("CNBC Top", "https://search.cnbc.com/rs/search/combinedcms/view.xml?partnerId=wrss01&id=100003114"),
    ("MarketWatch", "https://feeds.marketwatch.com/marketwatch/topstories/"),
    ("Bloomberg Markets", "https://feeds.bloomberg.com/markets/news.rss"),
    ("Financial Times", "https://www.ft.com/rss/home"),
    ("WSJ Markets", "https://feeds.a.wsj.com/rss/RSSMarketsMain.xml"),
    ("Yahoo Finance", "https://finance.yahoo.com/news/rssindex"),
    ("Investing.com", "https://www.investing.com/rss/news.rss"),
]

GEOPOLITICAL_RSS = [
    ("Reuters World", "https://www.reutersagency.com/feed/?best-topics=political-general"),
    ("BBC World", "http://feeds.bbci.co.uk/news/world/rss.xml"),
    ("Al Jazeera", "https://www.aljazeera.com/xml/rss/all.xml"),
    ("AP News", "https://rsshub.app/apnews/topics/world-news"),
    ("DW News", "https://rss.dw.com/rdf/rss-en-all"),
]

CRYPTO_RSS = [
    ("CoinDesk", "https://www.coindesk.com/arc/outboundfeeds/rss/"),
    ("CoinTelegraph", "https://cointelegraph.com/rss"),
    ("The Block", "https://www.theblock.co/rss.xml"),
]

# Cuvinte cheie care afecteaza pietele
MARKET_KEYWORDS = {
    "bearish": ["war", "conflict", "sanctions", "recession", "inflation", "crash",
                 "default", "crisis", "tariff", "downgrade", "layoffs", "shutdown",
                 "attack", "missile", "nuclear", "pandemic", "collapse", "bankrupt",
                 "investigation", "fraud", "hack", "breach", "sell-off", "bearish"],
    "bullish": ["deal", "agreement", "peace", "growth", "rally", "stimulus",
                "rate cut", "bullish", "upgrade", "breakthrough", "profit",
                "earnings beat", "IPO", "merger", "acquisition", "innovation",
                "record high", "recovery", "expansion", "ceasefire"],
}

# Mapare sector → active afectate
SECTOR_IMPACT = {
    "oil": ["OIL", "BRENT", "XOM", "CVX", "SHEL"],
    "energy": ["OIL", "BRENT", "NATGAS", "XOM", "CVX"],
    "gold": ["GOLD", "SILVER", "GLD"],
    "crypto": ["BTC", "ETH", "SOL", "XRP"],
    "tech": ["NVDA", "AAPL", "MSFT", "GOOGL", "META", "TSLA", "AMD"],
    "finance": ["JPM", "GS", "V", "MA"],
    "china": ["BABA", "TSM", "ASML", "COPPER"],
    "war": ["GOLD", "OIL", "BRENT", "VIX"],
    "fed": ["SP500", "NASDAQ", "DOW", "TLT", "GOLD"],
    "tariff": ["SP500", "NASDAQ", "AAPL", "TSLA", "COPPER"],
}


@dataclass
class NewsItem:
    title: str
    source: str
    url: str = ""
    published: str = ""
    category: str = ""  # "finance", "geopolitical", "crypto"
    sentiment: str = ""  # "bearish", "bullish", "neutral"
    affected_assets: List[str] = field(default_factory=list)
    relevance: float = 0.0  # 0-1


@dataclass
class WorldContext:
    """Context global care afecteaza pietele."""
    timestamp: str = ""
    # Stiri
    top_finance_news: List[NewsItem] = field(default_factory=list)
    top_geopolitical_news: List[NewsItem] = field(default_factory=list)
    top_crypto_news: List[NewsItem] = field(default_factory=list)
    # Analiza
    overall_sentiment: str = "NEUTRAL"  # RISK_ON / RISK_OFF / NEUTRAL
    risk_factors: List[str] = field(default_factory=list)
    opportunities: List[str] = field(default_factory=list)
    # WM specific (daca API-ul raspunde)
    hotspots: List[dict] = field(default_factory=list)
    conflicts: List[dict] = field(default_factory=list)
    macro_radar: dict = field(default_factory=dict)
    cii_scores: List[dict] = field(default_factory=list)  # Country Instability Index


class WorldMonitorFetcher:
    """
    Preia context global din World Monitor API + surse RSS publice.
    Folosit de Ghid Practic pentru a explica impactul evenimentelor pe piete.
    """

    def __init__(self, api_key: str = ""):
        self.api_key = api_key
        self.headers = {
            "User-Agent": "TradingBot/2.0",
            "Accept": "application/json",
        }
        if api_key:
            self.headers["Authorization"] = f"Bearer {api_key}"
        self._cache = {}
        self._cache_time = {}

    def fetch_full_context(self) -> WorldContext:
        """Preia tot contextul disponibil."""
        ctx = WorldContext(timestamp=datetime.now().isoformat())

        # 1. Incearca World Monitor API
        wm_data = self._try_wm_api()
        if wm_data:
            ctx.hotspots = wm_data.get("hotspots", [])
            ctx.conflicts = wm_data.get("conflicts", [])
            ctx.macro_radar = wm_data.get("macro_radar", {})
            ctx.cii_scores = wm_data.get("cii_scores", [])

        # 2. RSS feeds (functioneaza intotdeauna)
        ctx.top_finance_news = self._fetch_rss_category(FINANCE_RSS, "finance", limit=15)
        ctx.top_geopolitical_news = self._fetch_rss_category(GEOPOLITICAL_RSS, "geopolitical", limit=10)
        ctx.top_crypto_news = self._fetch_rss_category(CRYPTO_RSS, "crypto", limit=8)

        # 3. Analiza sentiment global
        all_news = ctx.top_finance_news + ctx.top_geopolitical_news + ctx.top_crypto_news
        ctx.overall_sentiment = self._calculate_global_sentiment(all_news)
        ctx.risk_factors = self._extract_risk_factors(all_news, ctx)
        ctx.opportunities = self._extract_opportunities(all_news, ctx)

        return ctx

    # ── World Monitor API ─────────────────────────────────────────

    def _try_wm_api(self) -> dict:
        """Incearca endpoint-urile WM API. Returneaza {} daca nu functioneaza."""
        result = {}

        # Macro Radar (7-signal market verdict)
        try:
            r = requests.get(
                f"{WM_API_BASE}/api/markets/v1/macro-radar",
                headers=self.headers, timeout=WM_TIMEOUT
            )
            if r.status_code == 200:
                data = r.json()
                result["macro_radar"] = data.get("data", data)
                log.info("WM API: macro-radar OK")
        except Exception as e:
            log.debug(f"WM macro-radar fail: {e}")

        # Conflicts / Hotspots
        try:
            r = requests.get(
                f"{WM_API_BASE}/api/conflicts/v1/active",
                headers=self.headers, timeout=WM_TIMEOUT
            )
            if r.status_code == 200:
                data = r.json()
                result["conflicts"] = data.get("data", data) if isinstance(data, dict) else data
                log.info("WM API: conflicts OK")
        except Exception as e:
            log.debug(f"WM conflicts fail: {e}")

        # CII (Country Instability Index)
        try:
            r = requests.get(
                f"{WM_API_BASE}/api/intelligence/v1/cii",
                headers=self.headers, timeout=WM_TIMEOUT
            )
            if r.status_code == 200:
                data = r.json()
                result["cii_scores"] = data.get("data", data) if isinstance(data, dict) else data
                log.info("WM API: CII OK")
        except Exception as e:
            log.debug(f"WM CII fail: {e}")

        # News / Intelligence
        try:
            r = requests.get(
                f"{WM_API_BASE}/api/news/v1/latest",
                headers=self.headers, timeout=WM_TIMEOUT
            )
            if r.status_code == 200:
                data = r.json()
                hotspots = data.get("data", data) if isinstance(data, dict) else data
                if isinstance(hotspots, list):
                    result["hotspots"] = hotspots[:20]
                log.info("WM API: news OK")
        except Exception as e:
            log.debug(f"WM news fail: {e}")

        return result

    # ── RSS Feeds ─────────────────────────────────────────────────

    def _fetch_rss_category(self, feeds: list, category: str, limit: int = 10) -> List[NewsItem]:
        """Preia si parseaza RSS feeds."""
        items = []
        for name, url in feeds:
            try:
                r = requests.get(url, timeout=8, headers={
                    "User-Agent": "TradingBot/2.0 RSS Reader"
                })
                if r.status_code != 200:
                    continue
                items.extend(self._parse_rss(r.text, name, category))
            except Exception as e:
                log.debug(f"RSS fail {name}: {e}")
                continue

        # Sort by relevance, deduplicate
        seen_titles = set()
        unique = []
        for item in items:
            title_key = item.title.lower()[:50]
            if title_key not in seen_titles:
                seen_titles.add(title_key)
                unique.append(item)

        # Score relevance
        for item in unique:
            item.sentiment = self._classify_sentiment(item.title)
            item.affected_assets = self._find_affected_assets(item.title)
            item.relevance = self._score_relevance(item)

        unique.sort(key=lambda x: x.relevance, reverse=True)
        return unique[:limit]

    def _parse_rss(self, xml_text: str, source: str, category: str) -> List[NewsItem]:
        """Parseaza XML RSS in NewsItem list."""
        items = []
        try:
            root = ET.fromstring(xml_text)
            # Standard RSS 2.0
            for item in root.findall(".//item"):
                title = item.findtext("title", "").strip()
                link = item.findtext("link", "").strip()
                pub = item.findtext("pubDate", "").strip()
                if title:
                    items.append(NewsItem(
                        title=title, source=source, url=link,
                        published=pub, category=category
                    ))
            # Atom format
            if not items:
                ns = {"atom": "http://www.w3.org/2005/Atom"}
                for entry in root.findall(".//atom:entry", ns):
                    title = entry.findtext("atom:title", "", ns).strip()
                    link_el = entry.find("atom:link", ns)
                    link = link_el.get("href", "") if link_el is not None else ""
                    if title:
                        items.append(NewsItem(
                            title=title, source=source, url=link,
                            category=category
                        ))
        except ET.ParseError:
            pass
        return items[:20]

    # ── Sentiment Analysis (keyword-based, nu AI) ─────────────────

    def _classify_sentiment(self, title: str) -> str:
        title_lower = title.lower()
        bear_score = sum(1 for kw in MARKET_KEYWORDS["bearish"] if kw in title_lower)
        bull_score = sum(1 for kw in MARKET_KEYWORDS["bullish"] if kw in title_lower)
        if bear_score > bull_score:
            return "bearish"
        elif bull_score > bear_score:
            return "bullish"
        return "neutral"

    def _find_affected_assets(self, title: str) -> List[str]:
        title_lower = title.lower()
        affected = set()
        for sector, assets in SECTOR_IMPACT.items():
            if sector in title_lower:
                affected.update(assets)
        # Direct ticker mentions
        for ticker in ["AAPL", "TSLA", "NVDA", "MSFT", "META", "AMZN", "GOOGL",
                        "BTC", "ETH", "SOL", "XRP", "GOLD", "OIL"]:
            if ticker.lower() in title_lower or ticker in title:
                affected.add(ticker)
        # Company names
        name_map = {
            "apple": "AAPL", "tesla": "TSLA", "nvidia": "NVDA", "microsoft": "MSFT",
            "google": "GOOGL", "alphabet": "GOOGL", "amazon": "AMZN", "meta": "META",
            "facebook": "META", "bitcoin": "BTC", "ethereum": "ETH", "solana": "SOL",
        }
        for name, ticker in name_map.items():
            if name in title_lower:
                affected.add(ticker)
        return list(affected)

    def _score_relevance(self, item: NewsItem) -> float:
        score = 0.5
        if item.sentiment != "neutral":
            score += 0.2
        if item.affected_assets:
            score += 0.1 * min(len(item.affected_assets), 3)
        # Boost for high-quality sources
        top_sources = ["Reuters", "Bloomberg", "CNBC", "Financial Times", "WSJ"]
        if any(s in item.source for s in top_sources):
            score += 0.1
        return min(score, 1.0)

    def _calculate_global_sentiment(self, all_news: List[NewsItem]) -> str:
        if not all_news:
            return "NEUTRAL"
        bear = sum(1 for n in all_news if n.sentiment == "bearish")
        bull = sum(1 for n in all_news if n.sentiment == "bullish")
        total = len(all_news)
        if bear > total * 0.4:
            return "RISK_OFF"
        elif bull > total * 0.4:
            return "RISK_ON"
        return "NEUTRAL"

    def _extract_risk_factors(self, news: List[NewsItem], ctx: WorldContext) -> List[str]:
        risks = []
        bearish = [n for n in news if n.sentiment == "bearish"]
        for n in bearish[:5]:
            risks.append(f"[{n.source}] {n.title}")

        # WM conflicts
        if ctx.conflicts:
            for c in ctx.conflicts[:3]:
                if isinstance(c, dict):
                    name = c.get("name", c.get("title", "Conflict activ"))
                    risks.append(f"[CONFLICT] {name}")

        return risks

    def _extract_opportunities(self, news: List[NewsItem], ctx: WorldContext) -> List[str]:
        opps = []
        bullish = [n for n in news if n.sentiment == "bullish"]
        for n in bullish[:5]:
            opps.append(f"[{n.source}] {n.title}")
        return opps


def format_context_for_ghid(ctx: WorldContext) -> str:
    """Formateaza contextul global ca text pentru Ghid Practic."""
    lines = [
        f"═══ CONTEXT GLOBAL — {datetime.now().strftime('%d.%m.%Y %H:%M')} ═══",
        f"Sentiment global: {ctx.overall_sentiment}",
        "",
    ]

    if ctx.macro_radar:
        lines.append("MACRO RADAR (World Monitor):")
        if isinstance(ctx.macro_radar, dict):
            verdict = ctx.macro_radar.get("verdict", "N/A")
            lines.append(f"  Verdict: {verdict}")
            signals = ctx.macro_radar.get("signals", [])
            if isinstance(signals, list):
                for s in signals:
                    if isinstance(s, dict):
                        lines.append(f"  • {s.get('name', '?')}: {s.get('value', '?')}")
        lines.append("")

    if ctx.risk_factors:
        lines.append("FACTORI DE RISC:")
        for r in ctx.risk_factors[:7]:
            lines.append(f"  ⚠ {r}")
        lines.append("")

    if ctx.opportunities:
        lines.append("OPORTUNITATI:")
        for o in ctx.opportunities[:5]:
            lines.append(f"  ✓ {o}")
        lines.append("")

    if ctx.top_finance_news:
        lines.append("STIRI FINANCIARE TOP:")
        for n in ctx.top_finance_news[:8]:
            emoji = "🔴" if n.sentiment == "bearish" else "🟢" if n.sentiment == "bullish" else "⚪"
            assets_str = f" → {', '.join(n.affected_assets)}" if n.affected_assets else ""
            lines.append(f"  {emoji} [{n.source}] {n.title}{assets_str}")
        lines.append("")

    if ctx.top_geopolitical_news:
        lines.append("GEOPOLITICA:")
        for n in ctx.top_geopolitical_news[:5]:
            emoji = "🔴" if n.sentiment == "bearish" else "🟢" if n.sentiment == "bullish" else "⚪"
            lines.append(f"  {emoji} [{n.source}] {n.title}")
        lines.append("")

    if ctx.top_crypto_news:
        lines.append("CRYPTO NEWS:")
        for n in ctx.top_crypto_news[:5]:
            emoji = "🔴" if n.sentiment == "bearish" else "🟢" if n.sentiment == "bullish" else "⚪"
            lines.append(f"  {emoji} [{n.source}] {n.title}")
        lines.append("")

    if ctx.conflicts:
        lines.append("CONFLICTE ACTIVE (World Monitor):")
        for c in ctx.conflicts[:5]:
            if isinstance(c, dict):
                lines.append(f"  🔥 {c.get('name', c.get('title', str(c)))}")
        lines.append("")

    return "\n".join(lines)


def get_impact_on_asset(ctx: WorldContext, symbol: str) -> str:
    """Genereaza explicatie a impactului global pe un activ specific."""
    symbol_upper = symbol.upper()
    lines = []

    # Cauta stiri care afecteaza direct acest activ
    all_news = ctx.top_finance_news + ctx.top_geopolitical_news + ctx.top_crypto_news
    relevant = [n for n in all_news if symbol_upper in n.affected_assets]

    if relevant:
        lines.append(f"STIRI CARE AFECTEAZA DIRECT {symbol_upper}:")
        for n in relevant[:5]:
            emoji = "🔴" if n.sentiment == "bearish" else "🟢" if n.sentiment == "bullish" else "⚪"
            lines.append(f"  {emoji} {n.title} ({n.source})")
    else:
        lines.append(f"Nu sunt stiri care sa afecteze direct {symbol_upper} in acest moment.")

    # Context general
    if ctx.overall_sentiment == "RISK_OFF":
        lines.append(f"\nContext global RISK-OFF — presiune generala pe active riscante.")
        if symbol_upper in ["BTC", "ETH", "SOL", "TSLA", "NVDA"]:
            lines.append("Active riscante (crypto, tech growth) tind sa scada in risk-off.")
        elif symbol_upper in ["GOLD", "SILVER"]:
            lines.append("Activele safe-haven (aur, argint) tind sa creasca in risk-off.")
    elif ctx.overall_sentiment == "RISK_ON":
        lines.append(f"\nContext global RISK-ON — apetit pentru risc crescut.")
        if symbol_upper in ["BTC", "ETH", "SOL", "TSLA", "NVDA"]:
            lines.append("Active riscante beneficiaza de risk-on.")
        elif symbol_upper in ["GOLD"]:
            lines.append("Aurul poate stagna sau scadea cand apetitul de risc e ridicat.")

    # Conflicte
    if ctx.conflicts and symbol_upper in ["GOLD", "OIL", "BRENT", "VIX"]:
        lines.append(f"\nConflicte active pot sustine pretul {symbol_upper} (safe-haven / supply disruption).")

    return "\n".join(lines) if lines else "Nu exista context global relevant pentru acest activ."
