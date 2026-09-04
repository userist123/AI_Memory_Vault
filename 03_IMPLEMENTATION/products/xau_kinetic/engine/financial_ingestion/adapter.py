"""
Canonical Memory Adapter & Deduplication Engine.
Transforms market analysis, asset profiles, macroeconomic snapshots, technical setups,
and trade executions into Draft7 schema-valid atomic canonical memory notes
(knowledge, resource, decision, experience, error, lesson) adhering strictly to
AGENTS.md and P0-P18 trust boundary invariants.
"""

import uuid
import hashlib
import json
import yaml
from datetime import datetime, timezone
from typing import Dict, List, Optional, Any, Tuple

from .catalog import Instrument, get_instrument, ACTIVE, INDICI, ACTIUNI, CRYPTO, VALUTE, MATERII_PRIME
from .indicators import fmt_price, fmt_pct, rr_text

try:
    from memory_controller.validation.schema import validate_frontmatter
except ImportError:
    validate_frontmatter = None


# ============================================================================
# 1. HASHING & DEDUPLICATION HELPERS
# ============================================================================

def calculate_content_hash(data: Any) -> str:
    """
    Computes a deterministic SHA-256 hexadecimal hash over a normalized dictionary
    or string payload to guarantee content-based deduplication.
    """
    if isinstance(data, dict):
        # Normalize dict to deterministic json string
        serialized = json.dumps(data, sort_keys=True, ensure_ascii=True, default=str)
    elif isinstance(data, str):
        serialized = data.strip()
    else:
        serialized = str(data)
    return hashlib.sha256(serialized.encode("utf-8")).hexdigest()


class MemoryDeduplicator:
    """
    Stateful deduplication and contradiction registry for incoming financial notes.
    Adheres to AGENTS.md §4, 9, 10:
    - Tracks content hashes to reject identical notes.
    - Matches subject entities (e.g., asset ticker + date).
    - Detects conflicting signals and creates contradiction records.
    """

    def __init__(self):
        self._content_hashes: Dict[str, str] = {}  # hash -> note_id
        self._entity_registry: Dict[str, Dict[str, Any]] = {}  # key (e.g. "AAPL:2026-08-25") -> note_data
        self._conflict_records: List[Dict[str, Any]] = []

    def is_duplicate(self, note_data: Dict[str, Any]) -> bool:
        """Checks if an identical note has already been processed based on content hash."""
        chash = note_data.get("content_hash") or calculate_content_hash(note_data.get("content", note_data))
        return chash in self._content_hashes

    def register_note(self, note_data: Dict[str, Any]) -> Tuple[bool, Optional[str]]:
        """
        Registers a note.
        Returns (is_new, existing_note_id). If already present, returns (False, existing_note_id).
        """
        chash = note_data.get("content_hash") or calculate_content_hash(note_data.get("content", note_data))
        note_id = note_data.get("id") or str(uuid.uuid4())

        if chash in self._content_hashes:
            return False, self._content_hashes[chash]

        self._content_hashes[chash] = note_id

        # Track by entity key if available (e.g. ticker + date + type)
        entity_key = note_data.get("entity_key")
        if entity_key:
            self._entity_registry[entity_key] = note_data

        return True, None

    def detect_contradictions(
        self,
        new_note: Dict[str, Any],
        existing_notes: Optional[List[Dict[str, Any]]] = None
    ) -> List[Dict[str, Any]]:
        """
        Detects contradictions between incoming note and registered/existing notes.
        For example: a BUY signal for an asset on the same date when an active SELL signal exists.
        """
        conflicts = []
        notes_pool = existing_notes or list(self._entity_registry.values())

        new_ticker = new_note.get("ticker")
        new_date = new_note.get("created")
        new_signal = new_note.get("signal")

        if not new_ticker or not new_signal:
            return conflicts

        for ext in notes_pool:
            ext_ticker = ext.get("ticker")
            ext_date = ext.get("created")
            ext_signal = ext.get("signal")

            if ext_ticker == new_ticker and ext_date == new_date and ext_signal:
                if (new_signal == "BUY" and ext_signal == "SELL") or (new_signal == "SELL" and ext_signal == "BUY"):
                    conflict = self.create_conflict_record(
                        note_a=ext,
                        note_b=new_note,
                        conflict_reason=f"Opposing signals on {new_ticker} for date {new_date}: '{ext_signal}' vs '{new_signal}'"
                    )
                    conflicts.append(conflict)
                    self._conflict_records.append(conflict)

        return conflicts

    def create_conflict_record(
        self,
        note_a: Dict[str, Any],
        note_b: Dict[str, Any],
        conflict_reason: str
    ) -> Dict[str, Any]:
        """
        Generates an atomic contradiction record linking both conflicting notes
        without erasing either claim per AGENTS.md §10.
        """
        today = datetime.now(timezone.utc).strftime("%Y-%m-%d")
        conflict_id = str(uuid.uuid4())
        id_a = note_a.get("id", str(uuid.uuid4()))
        id_b = note_b.get("id", str(uuid.uuid4()))

        frontmatter = {
            "id": conflict_id,
            "type": "hypothesis",
            "lifecycle": "REVIEW",
            "category": "financial-conflict-record",
            "tags": ["finance", "contradiction", "signal-conflict", "review-required"],
            "created": today,
            "updated": today,
            "provenance": {
                "source_type": "execution",
                "source_ref": "financial_memory_adapter:contradiction_handler",
                "source_date": today,
                "extraction_date": today,
                "redaction": "none",
                "provenance_status": "complete",
            },
            "confidence": "low",
            "verification": "unverified",
            "relations": [
                {"relation": "conflicts_with", "target": f"[[{note_a.get('title', id_a)}]]", "target_id": id_a},
                {"relation": "conflicts_with", "target": f"[[{note_b.get('title', id_b)}]]", "target_id": id_b},
            ],
        }

        content = (
            f"# Contradiction Record: {conflict_reason}\n\n"
            f"## Context\n"
            f"Two conflicting financial assessments were produced for the same instrument.\n\n"
            f"### Claim A (Note ID: `{id_a}`)\n"
            f"- Title: {note_a.get('title', 'N/A')}\n"
            f"- Signal: {note_a.get('signal', 'N/A')}\n"
            f"- Source: {note_a.get('provenance', {}).get('source_ref', 'N/A')}\n\n"
            f"### Claim B (Note ID: `{id_b}`)\n"
            f"- Title: {note_b.get('title', 'N/A')}\n"
            f"- Signal: {note_b.get('signal', 'N/A')}\n"
            f"- Source: {note_b.get('provenance', {}).get('source_ref', 'N/A')}\n\n"
            f"## Resolution Action Required\n"
            f"Preserve both records. Flag for human trader attestation and review.\n"
        )

        return {
            "frontmatter": frontmatter,
            "title": f"Conflict_{note_a.get('ticker', 'Asset')}_{today}",
            "content": content,
            "markdown": render_markdown_note(frontmatter, content),
        }


# ============================================================================
# 2. CANONICAL NOTE GENERATORS
# ============================================================================

def render_markdown_note(frontmatter: Dict[str, Any], content: str) -> str:
    """
    Renders frontmatter as a valid YAML header enclosed in '---' markers
    followed by the markdown body.
    """
    # Clean dictionary to ensure pure YAML serializable types
    clean_fm = json.loads(json.dumps(frontmatter, default=str))
    fm_yaml = yaml.dump(clean_fm, sort_keys=False, default_flow_style=False, allow_unicode=True)
    return f"---\n{fm_yaml}---\n\n{content.strip()}\n"


def generate_asset_profile_note(
    asset_data: Dict[str, Any],
    metadata: Optional[Instrument] = None
) -> Dict[str, Any]:
    """
    Generates a canonical 'knowledge' note for a financial asset profile.
    Complies with Draft7 schema, P0 (unverified for AI), P1 (source_type=execution).
    """
    ticker = str(asset_data.get("ticker", "UNKNOWN"))
    name = str(asset_data.get("name", ticker))
    inst = metadata or get_instrument(ticker) or get_instrument(name)

    today = datetime.now(timezone.utc).strftime("%Y-%m-%d")
    note_id = str(uuid.uuid4())

    category_slug = inst.category.lower() if inst else "general"
    tags = ["finance", "asset-profile", category_slug, ticker.lower().replace("^", "").replace("=x", "").replace("=f", "")]

    relations = [
        {"relation": "related_to", "target": "[[Macro_Regime_Current]]"},
        {"relation": "implements", "target": "[[Model_Multi_Confluence_Scoring]]"},
    ]

    competitors = inst.competitors if inst else []
    for comp in competitors[:3]:
        comp_clean = comp.replace("/", "").replace(" ", "_")
        relations.append({"relation": "related_to", "target": f"[[Asset_{comp_clean}]]"})

    frontmatter = {
        "id": note_id,
        "type": "knowledge",
        "lifecycle": "REVIEW",
        "category": "financial-asset-profile",
        "tags": tags,
        "created": today,
        "updated": today,
        "provenance": {
            "source_type": "execution",
            "source_ref": "financial_ingestion_pipeline:get_full_data",
            "source_date": today,
            "extraction_date": today,
            "redaction": "none",
            "provenance_status": "complete",
        },
        "confidence": "high",
        "verification": "unverified",
        "relations": relations,
    }

    price = fmt_price(asset_data.get("inchidere"))
    rsi = asset_data.get("rsi", "N/A")
    rsi_status = asset_data.get("rsi_status", "N/A")
    trend = asset_data.get("trend", "N/A")
    semnal = asset_data.get("semnal", "WAIT")
    confluente = asset_data.get("confluente", 0)
    score = asset_data.get("score", 0)
    atr = fmt_price(asset_data.get("atr"))
    rvol = asset_data.get("rvol", 1.0)
    support = fmt_price(asset_data.get("support"))
    resistance = fmt_price(asset_data.get("resistance"))

    content = (
        f"# Asset Profile: {name} ({ticker})\n\n"
        f"## 1. Overview & Classification\n"
        f"- **Asset Class**: {inst.category if inst else 'N/A'}\n"
        f"- **Sector**: {inst.sector if inst else 'N/A'}\n"
        f"- **Base Currency**: {inst.currency_base if inst else 'USD'}\n"
        f"- **Description**: {inst.description if inst else 'Quantitative tracked instrument'}\n\n"
        f"## 2. Technical Posture Snapshot ({today})\n"
        f"- **Close Price**: {price}\n"
        f"- **Trend**: {trend}\n"
        f"- **RSI (14)**: {rsi} ({rsi_status})\n"
        f"- **MACD Cross**: {asset_data.get('macd_cross', 'N/A')}\n"
        f"- **MA Structure**: MA20={fmt_price(asset_data.get('ma20'))} | MA50={fmt_price(asset_data.get('ma50'))} | MA200={fmt_price(asset_data.get('ma200'))} -> {asset_data.get('macross', 'N/A')}\n"
        f"- **Bollinger Bands**: Lower={fmt_price(asset_data.get('bb_inf'))} | Mid={fmt_price(asset_data.get('bb_mid'))} | Upper={fmt_price(asset_data.get('bb_sup'))}\n"
        f"- **ATR (14)**: {atr}\n"
        f"- **Relative Volume (RVOL)**: {rvol}x\n"
        f"- **20-Day Range**: Support={support} | Resistance={resistance}\n\n"
        f"## 3. Quantitative Confluence Engine\n"
        f"- **Signal**: `{semnal}` (Score: {score:+d} | Confluences: {confluente}/5)\n"
        f"- **Dynamic ATR Sizing**: SL={fmt_price(asset_data.get('sl'))} | TP={fmt_price(asset_data.get('tp'))} (Target R/R: {rr_text(asset_data.get('inchidere'), asset_data.get('sl'), asset_data.get('tp'))})\n"
        f"- **Statistical Probability**: {asset_data.get('probabilitate', 50):.0f}%\n"
    )

    if validate_frontmatter:
        validate_frontmatter(frontmatter)

    return {
        "id": note_id,
        "frontmatter": frontmatter,
        "title": f"Asset_{name.replace(' ', '_')}_{ticker.replace('^', '').replace('=', '_')}",
        "content": content,
        "content_hash": calculate_content_hash(content),
        "ticker": ticker,
        "signal": semnal,
        "created": today,
        "markdown": render_markdown_note(frontmatter, content),
    }


def generate_macro_regime_note(
    macro_data: Dict[str, Any],
    fred_data: Dict[str, Any],
    sentiment_data: Dict[str, Any]
) -> Dict[str, Any]:
    """
    Generates a canonical 'knowledge' note capturing the macroeconomic regime,
    yield curve posture, inflation rate, unemployment, and market fear/greed.
    """
    today = datetime.now(timezone.utc).strftime("%Y-%m-%d")
    note_id = str(uuid.uuid4())

    frontmatter = {
        "id": note_id,
        "type": "knowledge",
        "lifecycle": "REVIEW",
        "category": "macroeconomic-regime",
        "tags": ["finance", "macroeconomics", "interest-rates", "inflation", "vix", "sentiment"],
        "created": today,
        "updated": today,
        "provenance": {
            "source_type": "execution",
            "source_ref": "financial_ingestion_pipeline:macro_snapshot",
            "source_date": today,
            "extraction_date": today,
            "redaction": "none",
            "provenance_status": "complete",
        },
        "confidence": "high",
        "verification": "unverified",
        "relations": [
            {"relation": "related_to", "target": "[[Resource_Financial_Ticker_Catalog]]"},
            {"relation": "related_to", "target": "[[Resource_FRED_Macro_Series]]"},
        ],
    }

    # Extract macro values
    vix = macro_data.get("VIX", {}).get("inchidere", macro_data.get("^VIX", {}).get("inchidere", "N/A"))
    tnx = macro_data.get("Yield 10Y US", {}).get("inchidere", macro_data.get("^TNX", {}).get("inchidere", "N/A"))
    irx = macro_data.get("Yield 2Y US", {}).get("inchidere", macro_data.get("^IRX", {}).get("inchidere", "N/A"))
    dxy = macro_data.get("USD Index", {}).get("inchidere", macro_data.get("DX-Y.NYB", {}).get("inchidere", "N/A"))

    fedfunds = fred_data.get("FEDFUNDS", {}).get("current", "N/A")
    cpi = fred_data.get("CPIAUCSL", {}).get("current", "N/A")
    unrate = fred_data.get("UNRATE", {}).get("current", "N/A")
    gdp = fred_data.get("GDP", {}).get("current", "N/A")

    fng_val = sentiment_data.get("value", "N/A")
    fng_disp = sentiment_data.get("display", "N/A")

    content = (
        f"# Macroeconomic Regime Snapshot: {today}\n\n"
        f"## 1. Interest Rates & Sovereign Yields\n"
        f"- **Effective Fed Funds Rate (FEDFUNDS)**: {fedfunds}%\n"
        f"- **US 10-Year Treasury Yield (^TNX)**: {tnx}%\n"
        f"- **US 2-Year Treasury Yield / 13W Bill (^IRX)**: {irx}%\n"
        f"- **US Dollar Index (DXY / DX-Y.NYB)**: {dxy}\n\n"
        f"## 2. Macro Indicators & Growth\n"
        f"- **Consumer Price Index (CPIAUCSL)**: {cpi}\n"
        f"- **Civilian Unemployment Rate (UNRATE)**: {unrate}%\n"
        f"- **US Gross Domestic Product (GDP)**: ${gdp} B\n\n"
        f"## 3. Market Volatility & Sentiment\n"
        f"- **CBOE Volatility Index (VIX)**: {vix}\n"
        f"- **Crypto Fear & Greed Index**: {fng_disp}\n"
    )

    if validate_frontmatter:
        validate_frontmatter(frontmatter)

    return {
        "id": note_id,
        "frontmatter": frontmatter,
        "title": f"Macro_Regime_{today.replace('-', '_')}",
        "content": content,
        "content_hash": calculate_content_hash(content),
        "created": today,
        "markdown": render_markdown_note(frontmatter, content),
    }


def generate_technical_setup_note(
    asset_data: Dict[str, Any],
    setup_name: str = "Confluence Signal"
) -> Dict[str, Any]:
    """
    Generates a canonical 'decision' note documenting a quantitative trade setup,
    confluence criteria, stop loss, take profit, and planned R/R.
    """
    ticker = str(asset_data.get("ticker", "UNKNOWN"))
    name = str(asset_data.get("name", ticker))
    semnal = str(asset_data.get("semnal", "WAIT"))
    price = asset_data.get("inchidere", 0.0)
    sl = asset_data.get("sl")
    tp = asset_data.get("tp")
    rr = asset_data.get("rr_ratio")
    prob = asset_data.get("probabilitate", 50.0)
    score = asset_data.get("score", 0)

    today = datetime.now(timezone.utc).strftime("%Y-%m-%d")
    note_id = str(uuid.uuid4())

    frontmatter = {
        "id": note_id,
        "type": "decision",
        "lifecycle": "REVIEW",
        "category": "technical-trading-setup",
        "tags": ["finance", "trade-setup", semnal.lower(), ticker.lower().replace("^", "").replace("=x", "").replace("=f", "")],
        "created": today,
        "updated": today,
        "provenance": {
            "source_type": "execution",
            "source_ref": "financial_ingestion_pipeline:calc_signal",
            "source_date": today,
            "extraction_date": today,
            "redaction": "none",
            "provenance_status": "complete",
        },
        "confidence": "high" if abs(score) >= 4 else "medium",
        "verification": "unverified",
        "relations": [
            {"relation": "related_to", "target": f"[[Asset_{name.replace(' ', '_')}]]"},
            {"relation": "implements", "target": "[[Model_Dynamic_ATR_Position_Sizing]]"},
        ],
    }

    content = (
        f"# Trading Decision: {semnal} on {name} ({ticker})\n\n"
        f"## 1. Setup Identification\n"
        f"- **Strategy Setup**: {setup_name}\n"
        f"- **Direction**: `{semnal}`\n"
        f"- **Confluence Score**: {score:+d} / 5\n"
        f"- **Statistical Win Probability**: {prob:.0f}%\n\n"
        f"## 2. Order Execution Parameters\n"
        f"- **Entry Price**: {fmt_price(price)}\n"
        f"- **Planned Stop Loss**: {fmt_price(sl)}\n"
        f"- **Planned Take Profit**: {fmt_price(tp)}\n"
        f"- **Planned Risk/Reward**: {rr_text(price, sl, tp)}\n\n"
        f"## 3. Technical Trigger Conditions\n"
        f"- **RSI (14)**: {asset_data.get('rsi')} ({asset_data.get('rsi_status')})\n"
        f"- **MACD Cross**: {asset_data.get('macd_cross')}\n"
        f"- **MA Structure**: {asset_data.get('macross')}\n"
        f"- **Volume (RVOL)**: {asset_data.get('rvol')}x\n"
    )

    if validate_frontmatter:
        validate_frontmatter(frontmatter)

    return {
        "id": note_id,
        "frontmatter": frontmatter,
        "title": f"Decision_{semnal}_{ticker.replace('^', '').replace('=', '_')}_{today}",
        "content": content,
        "content_hash": calculate_content_hash(content),
        "ticker": ticker,
        "signal": semnal,
        "created": today,
        "markdown": render_markdown_note(frontmatter, content),
    }


def generate_trade_experience_note(trade_data: Dict[str, Any]) -> Dict[str, Any]:
    """
    Generates a canonical 'experience' note from a completed trade in the Trading Journal.
    """
    trade_id = str(trade_data.get("trade_id", trade_data.get("id", "T001")))
    asset = str(trade_data.get("asset", "Asset"))
    direction = str(trade_data.get("direction", "LONG"))
    pnl_currency = float(trade_data.get("pnl_currency", 0.0))
    pnl_percent = float(trade_data.get("pnl_percent", 0.0))
    realized_rr = float(trade_data.get("realized_rr", 0.0))

    today = datetime.now(timezone.utc).strftime("%Y-%m-%d")
    note_id = str(uuid.uuid4())

    frontmatter = {
        "id": note_id,
        "type": "experience",
        "lifecycle": "REVIEW",
        "category": "trade-execution-log",
        "tags": ["finance", "trade-log", "trading-journal", direction.lower(), asset.lower()],
        "created": today,
        "updated": today,
        "provenance": {
            "source_type": "execution",
            "source_ref": "trading_journal:close_trade",
            "source_date": today,
            "extraction_date": today,
            "redaction": "none",
            "provenance_status": "complete",
        },
        "confidence": "high",
        "verification": "unverified",
        "relations": [
            {"relation": "related_to", "target": f"[[Asset_{asset}]]"},
        ],
    }

    content = (
        f"# Trade Experience Log: {trade_id} ({asset} {direction})\n\n"
        f"## 1. Execution Summary\n"
        f"- **Trade ID**: {trade_id}\n"
        f"- **Asset**: {asset}\n"
        f"- **Direction**: {direction}\n"
        f"- **Setup**: {trade_data.get('setup', 'Technical Confluence')}\n"
        f"- **Entry Price**: {trade_data.get('entry_price')}\n"
        f"- **Exit Price**: {trade_data.get('exit_price')}\n"
        f"- **Position Size**: {trade_data.get('position_size')}\n\n"
        f"## 2. Financial & Risk Outcome\n"
        f"- **Realized P&L ($)**: ${pnl_currency:+,.2f}\n"
        f"- **Realized P&L (%)**: {pnl_percent:+.2f}%\n"
        f"- **Realized R/R**: {realized_rr:+.2f}x\n"
        f"- **Execution Quality Score**: {trade_data.get('execution_quality', 8)}/10\n"
        f"- **Psychological State**: {trade_data.get('emotion', 'Calm & Disciplined')}\n"
        f"- **Plan Adherence**: {'Strictly Yes' if trade_data.get('plan_adhered', True) else 'No (Rule Deviation)'}\n\n"
        f"## 3. Extracted Insight\n"
        f"{trade_data.get('lesson', 'Trade executed according to strategy rules.')}\n"
    )

    if validate_frontmatter:
        validate_frontmatter(frontmatter)

    return {
        "id": note_id,
        "frontmatter": frontmatter,
        "title": f"Experience_Trade_{trade_id}_{asset}_{today}",
        "content": content,
        "content_hash": calculate_content_hash(content),
        "created": today,
        "markdown": render_markdown_note(frontmatter, content),
    }


def generate_trade_error_note(error_data: Dict[str, Any]) -> Dict[str, Any]:
    """
    Generates a canonical 'error' note for a failed trade setup, risk breach,
    or psychological deviation per AGENTS.md §16.
    """
    title_short = str(error_data.get("title", "Discipline Breach"))
    asset = str(error_data.get("asset", "General"))
    today = datetime.now(timezone.utc).strftime("%Y-%m-%d")
    note_id = str(uuid.uuid4())

    frontmatter = {
        "id": note_id,
        "type": "error",
        "lifecycle": "REVIEW",
        "category": "trading-discipline-error",
        "tags": ["finance", "trading-error", "post-mortem", "risk-breach"],
        "created": today,
        "updated": today,
        "provenance": {
            "source_type": "execution",
            "source_ref": "trading_journal:reflexion_loop",
            "source_date": today,
            "extraction_date": today,
            "redaction": "none",
            "provenance_status": "complete",
        },
        "confidence": "high",
        "verification": "unverified",
        "relations": [
            {"relation": "related_to", "target": f"[[Asset_{asset}]]"},
        ],
    }

    content = (
        f"# Trading Error Post-Mortem: {title_short}\n\n"
        f"## 1. Description & Context\n"
        f"- **Asset**: {asset}\n"
        f"- **Incident**: {error_data.get('description', 'Trade executed outside predetermined risk parameters.')}\n"
        f"- **Financial Impact**: {error_data.get('impact', '-1.0R loss')}\n\n"
        f"## 2. Root Cause Analysis\n"
        f"- **Root Cause**: {error_data.get('root_cause', 'Premature exit driven by FOMO and lack of stop-loss adherence.')}\n"
        f"- **Psychological Factor**: {error_data.get('emotion', 'Anxiety / Impatience')}\n\n"
        f"## 3. Remediation & Prevention Protocol\n"
        f"- **Immediate Fix**: {error_data.get('fix', 'Re-establish bracket OCO orders upon entry.')}\n"
        f"- **Prevention Rule**: {error_data.get('prevention', 'Mandate 15-minute cooling-off period before manual trade adjustments.')}\n"
    )

    if validate_frontmatter:
        validate_frontmatter(frontmatter)

    return {
        "id": note_id,
        "frontmatter": frontmatter,
        "title": f"Error_{title_short.replace(' ', '_')}_{today}",
        "content": content,
        "content_hash": calculate_content_hash(content),
        "created": today,
        "markdown": render_markdown_note(frontmatter, content),
    }


def generate_trading_lesson_note(lesson_data: Dict[str, Any]) -> Dict[str, Any]:
    """
    Generates a canonical 'lesson' note capturing an institutional trading edge or heuristic.
    """
    title_short = str(lesson_data.get("title", "Trading Heuristic Edge"))
    today = datetime.now(timezone.utc).strftime("%Y-%m-%d")
    note_id = str(uuid.uuid4())

    frontmatter = {
        "id": note_id,
        "type": "lesson",
        "lifecycle": "REVIEW",
        "category": "trading-heuristic-lesson",
        "tags": ["finance", "trading-lesson", "heuristics", "quantitative-edge"],
        "created": today,
        "updated": today,
        "provenance": {
            "source_type": "execution",
            "source_ref": "financial_ingestion_pipeline:lesson_distillation",
            "source_date": today,
            "extraction_date": today,
            "redaction": "none",
            "provenance_status": "complete",
        },
        "confidence": "high",
        "verification": "unverified",
        "relations": [
            {"relation": "related_to", "target": "[[Model_Multi_Confluence_Scoring]]"},
        ],
    }

    content = (
        f"# Trading Lesson: {title_short}\n\n"
        f"## 1. Actionable Heuristic\n"
        f"{lesson_data.get('heuristic', 'A high-volume breakout confirmed by MACD crossover has a >70% follow-through rate.')}\n\n"
        f"## 2. Prerequisite Market Conditions\n"
        f"{lesson_data.get('conditions', 'RVOL > 1.5x, 20-day resistance broken, daily RSI between 55 and 70.')}\n\n"
        f"## 3. Invalidation & Risk Limit\n"
        f"{lesson_data.get('invalidation', 'Close below breakout candle midpoint or 1.5x ATR trailing stop.')}\n"
    )

    if validate_frontmatter:
        validate_frontmatter(frontmatter)

    return {
        "id": note_id,
        "frontmatter": frontmatter,
        "title": f"Lesson_{title_short.replace(' ', '_')}_{today}",
        "content": content,
        "content_hash": calculate_content_hash(content),
        "created": today,
        "markdown": render_markdown_note(frontmatter, content),
    }


def generate_catalog_resource_note() -> Dict[str, Any]:
    """
    Generates a canonical 'resource' note cataloging all 95 financial instruments
    and 5 macro benchmarks in the vault.
    """
    today = datetime.now(timezone.utc).strftime("%Y-%m-%d")
    note_id = str(uuid.uuid4())

    frontmatter = {
        "id": note_id,
        "type": "resource",
        "lifecycle": "REVIEW",
        "category": "financial-instrument-catalog",
        "tags": ["finance", "catalog", "ticker-registry", "assets", "macro"],
        "created": today,
        "updated": today,
        "provenance": {
            "source_type": "execution",
            "source_ref": "financial_ingestion_pipeline:catalog",
            "source_date": today,
            "extraction_date": today,
            "redaction": "none",
            "provenance_status": "complete",
        },
        "confidence": "very_high",
        "verification": "unverified",
        "relations": [
            {"relation": "related_to", "target": "[[Knowledge Graph Home]]"},
        ],
    }

    content = (
        f"# Financial Instrument Catalog & Asset Index ({today})\n\n"
        f"## 1. Summary Statistics\n"
        f"- **Total Tracked Instruments**: 95\n"
        f"- **Indices**: 14 ({', '.join(INDICI.keys())})\n"
        f"- **Equities**: 30 ({', '.join(ACTIUNI.keys())})\n"
        f"- **Cryptocurrencies**: 25 ({', '.join(CRYPTO.keys())})\n"
        f"- **Foreign Exchange (FX)**: 12 ({', '.join(VALUTE.keys())})\n"
        f"- **Commodities**: 14 ({', '.join(MATERII_PRIME.keys())})\n"
        f"- **Macro Benchmark Tickers**: 5 (^VIX, ^TNX, ^IRX, ^TYX, DX-Y.NYB)\n"
        f"- **FRED Macroeconomic Series**: 4 (FEDFUNDS, CPIAUCSL, UNRATE, GDP)\n"
    )

    if validate_frontmatter:
        validate_frontmatter(frontmatter)

    return {
        "id": note_id,
        "frontmatter": frontmatter,
        "title": "Resource_Financial_Ticker_Catalog",
        "content": content,
        "content_hash": calculate_content_hash(content),
        "created": today,
        "markdown": render_markdown_note(frontmatter, content),
    }


# ============================================================================
# 3. HIGH-LEVEL ADAPTER CLASS
# ============================================================================

class FinancialMemoryAdapter:
    """
    High-level orchestrator transforming raw market and macro data feeds
    into schema-valid canonical memory notes with integrated deduplication.
    """

    def __init__(self):
        self.deduplicator = MemoryDeduplicator()

    def process_asset(self, asset_data: Dict[str, Any]) -> Dict[str, Any]:
        """Transforms single asset market data into an atomic profile note."""
        note = generate_asset_profile_note(asset_data)
        is_new, existing_id = self.deduplicator.register_note(note)
        note["is_new"] = is_new
        note["existing_id"] = existing_id
        return note

    def process_macro_regime(
        self,
        macro_data: Dict[str, Any],
        fred_data: Dict[str, Any],
        sentiment_data: Dict[str, Any]
    ) -> Dict[str, Any]:
        """Transforms macro environment into a canonical macro regime note."""
        note = generate_macro_regime_note(macro_data, fred_data, sentiment_data)
        is_new, existing_id = self.deduplicator.register_note(note)
        note["is_new"] = is_new
        note["existing_id"] = existing_id
        return note

    def process_trade_setup(self, asset_data: Dict[str, Any]) -> Dict[str, Any]:
        """Transforms a technical trade trigger into a decision note."""
        note = generate_technical_setup_note(asset_data)
        is_new, existing_id = self.deduplicator.register_note(note)
        note["is_new"] = is_new
        note["existing_id"] = existing_id
        return note
