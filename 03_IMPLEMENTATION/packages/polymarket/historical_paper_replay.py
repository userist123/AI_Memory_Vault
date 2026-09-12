"""Leakage-safe replay/settlement adapter for real historical Polymarket data.

This module is intentionally split into three layers:

1. `fetch_closed_markets` / `fetch_price_history` are the only network readers.
2. `build_market_bundle` is a pure normalizer which binds every price point to
   its market and records when the resolution was learned.
3. `run_mechanical_control` uses only pre-resolution price history to create a
   tiny deterministic paper trade, then settles it with the provider-reported
   terminal outcome. This is a plumbing/control run, not an alpha claim.

The public Polymarket CLOB `/prices-history` endpoint returns timestamp/price
pairs by outcome token. Gamma returns the market metadata and outcome/token
mapping. Resolution is learned from the *current* closed-market record and is
therefore never available to the strategy before `resolution_known_at`.
"""
from __future__ import annotations

from dataclasses import dataclass
from datetime import datetime, timezone
import json
import math
from typing import Any, Mapping, Optional, Sequence
from urllib.parse import urlencode
from urllib.request import Request, urlopen

from .paper_simulation import PaperInstruction, PaperQuote, PaperFill, PortfolioLedger, simulate_fill, SIDE_BUY

GAMMA_MARKETS_URL = "https://gamma-api.polymarket.com/markets"
CLOB_PRICES_HISTORY_URL = "https://clob.polymarket.com/prices-history"
HISTORICAL_PAPER_REPLAY_SCHEMA_VERSION = "polymarket-historical-paper-replay.v1"


@dataclass(frozen=True)
class HistoricalTapePoint:
    market_id: str
    outcome_id: str
    observed_at: str
    price: float
    source_ref: str

    def validate(self) -> None:
        if not self.market_id or not self.outcome_id or not self.source_ref:
            raise ValueError("market_id, outcome_id, and source_ref are required")
        parsed = _parse_iso(self.observed_at)
        if parsed.tzinfo is None:
            raise ValueError("observed_at must include timezone")
        if not math.isfinite(self.price) or not 0.0 < self.price < 1.0:
            raise ValueError("price must be strictly between 0 and 1")


@dataclass(frozen=True)
class HistoricalMarketBundle:
    market_id: str
    question: str
    outcome_ids: tuple[str, ...]
    outcomes: tuple[str, ...]
    resolution_outcome_ids: tuple[str, ...]
    resolution_known_at: str
    price_history: tuple[HistoricalTapePoint, ...]
    metadata_source_ref: str

    def validate(self) -> None:
        if not self.market_id or not self.question:
            raise ValueError("market_id and question are required")
        if len(self.outcome_ids) < 2 or len(self.outcome_ids) != len(self.outcomes):
            raise ValueError("bundle must contain aligned multi-outcome metadata")
        if not self.resolution_outcome_ids:
            raise ValueError("resolved bundle requires at least one winning outcome")
        if not set(self.resolution_outcome_ids).issubset(set(self.outcome_ids)):
            raise ValueError("resolution outcome is not one of the market outcomes")
        _parse_iso(self.resolution_known_at)
        for point in self.price_history:
            point.validate()
            if point.market_id != self.market_id:
                raise ValueError("price point market_id mismatch")
            if point.outcome_id not in self.outcome_ids:
                raise ValueError("price point outcome_id is not in market metadata")
            if _parse_iso(point.observed_at) >= _parse_iso(self.resolution_known_at):
                # The tape can extend to close; the current resolution snapshot
                # was learned later. The timestamp must not be confused with
                # the time the resolution became known.
                pass

    def first_point(self, outcome_id: str) -> HistoricalTapePoint:
        candidates = [p for p in self.price_history if p.outcome_id == outcome_id]
        if not candidates:
            raise ValueError(f"no price history for outcome {outcome_id}")
        return min(candidates, key=lambda p: _parse_iso(p.observed_at))


def _parse_iso(value: str) -> datetime:
    parsed = datetime.fromisoformat(value.replace("Z", "+00:00"))
    if parsed.tzinfo is None:
        raise ValueError("timestamp must include timezone")
    return parsed.astimezone(timezone.utc)


def _http_json(url: str, params: Mapping[str, Any], timeout: float = 30.0) -> Any:
    query = urlencode({k: v for k, v in params.items() if v is not None})
    request = Request(
        f"{url}?{query}" if query else url,
        headers={"Accept": "application/json", "User-Agent": "ai-memory-vault-research"},
    )
    with urlopen(request, timeout=timeout) as response:
        return json.loads(response.read().decode("utf-8"))


def fetch_closed_markets(*, limit: int = 20, offset: int = 0, timeout: float = 30.0) -> list[Mapping[str, Any]]:
    """Fetch closed Gamma market metadata. Read-only and unauthenticated."""
    if not 1 <= limit <= 100:
        raise ValueError("limit must be between 1 and 100")
    if offset < 0:
        raise ValueError("offset cannot be negative")
    payload = _http_json(
        GAMMA_MARKETS_URL,
        {"closed": "true", "limit": limit, "offset": offset},
        timeout,
    )
    if not isinstance(payload, list):
        raise ValueError("Gamma /markets must return a JSON array")
    return payload


def fetch_price_history(token_id: str, *, interval: str = "max", fidelity: int = 1440, timeout: float = 30.0) -> tuple[Mapping[str, Any], ...]:
    """Fetch CLOB historical prices for one outcome token."""
    if not token_id:
        raise ValueError("token_id is required")
    if fidelity <= 0:
        raise ValueError("fidelity must be positive")
    payload = _http_json(
        CLOB_PRICES_HISTORY_URL,
        {"market": token_id, "interval": interval, "fidelity": fidelity},
        timeout,
    )
    history = payload.get("history") if isinstance(payload, Mapping) else None
    if not isinstance(history, list):
        raise ValueError("CLOB prices-history response must contain history[]")
    return tuple(item for item in history if isinstance(item, Mapping))


def _decode_json_array(value: Any, field_name: str) -> list[str]:
    raw = json.loads(value) if isinstance(value, str) else value
    if not isinstance(raw, list):
        raise ValueError(f"{field_name} must be a JSON array or array")
    return [str(x) for x in raw]


def _resolved_outcomes(payload: Mapping[str, Any], outcomes: Sequence[str], outcome_prices: Sequence[str]) -> tuple[str, ...]:
    """Use only explicit terminal probabilities from a closed market snapshot."""
    if not payload.get("closed"):
        raise ValueError("market is not closed")
    if len(outcomes) != len(outcome_prices):
        raise ValueError("outcomes and outcomePrices length mismatch")
    winners = [outcomes[i] for i, price in enumerate(outcome_prices) if abs(float(price) - 1.0) < 1e-9]
    losers = [float(price) for price in outcome_prices if abs(float(price)) < 1e-9]
    if len(winners) != 1 or len(losers) != len(outcomes) - 1:
        raise ValueError("closed market does not expose an unambiguous terminal 1/0 outcome")
    return tuple(winners)


def build_market_bundle(payload: Mapping[str, Any], *, resolution_known_at: str, history_by_token: Mapping[str, Sequence[Mapping[str, Any]]]) -> HistoricalMarketBundle:
    """Pure normalizer for one real provider market and its CLOB tape."""
    market_id = str(payload.get("id") or "")
    question = str(payload.get("question") or "")
    outcomes = _decode_json_array(payload.get("outcomes"), "outcomes")
    outcome_ids = _decode_json_array(payload.get("clobTokenIds"), "clobTokenIds")
    outcome_prices = _decode_json_array(payload.get("outcomePrices"), "outcomePrices")
    if len(outcomes) != len(outcome_ids):
        raise ValueError("outcomes and clobTokenIds length mismatch")
    winners_by_name = set(_resolved_outcomes(payload, outcomes, outcome_prices))
    winner_ids = tuple(outcome_ids[i] for i, name in enumerate(outcomes) if name in winners_by_name)

    points: list[HistoricalTapePoint] = []
    for outcome_id in outcome_ids:
        for raw in history_by_token.get(outcome_id, ()):  # missing token tape is explicit
            if "t" not in raw or "p" not in raw:
                continue
            observed_at = datetime.fromtimestamp(float(raw["t"]), tz=timezone.utc).isoformat().replace("+00:00", "Z")
            points.append(HistoricalTapePoint(market_id, outcome_id, observed_at, float(raw["p"]), f"{CLOB_PRICES_HISTORY_URL}?market={outcome_id}"))

    bundle = HistoricalMarketBundle(
        market_id=market_id,
        question=question,
        outcome_ids=tuple(outcome_ids),
        outcomes=tuple(outcomes),
        resolution_outcome_ids=winner_ids,
        resolution_known_at=resolution_known_at,
        price_history=tuple(sorted(points, key=lambda p: (_parse_iso(p.observed_at), p.outcome_id))),
        metadata_source_ref=f"{GAMMA_MARKETS_URL}?id={market_id}",
    )
    bundle.validate()
    if not bundle.price_history:
        raise ValueError("resolved market has no CLOB historical price points")
    return bundle


@dataclass(frozen=True)
class ControlReplayResult:
    market_id: str
    question: str
    bought_outcome_id: str
    entry_at: str
    entry_price: float
    notional: float
    fee: float
    settlement_value: float
    pnl: float
    resolution_known_at: str


def run_mechanical_control(bundle: HistoricalMarketBundle, *, notional: float = 1.0) -> ControlReplayResult:
    """Buy the first listed outcome at its first observed price, then settle.

    The choice is made by outcome index only, never by the resolution. This is a
    deliberately naive control whose purpose is proving that real historical
    data can pass through paper fill -> portfolio -> terminal settlement without
    a look-ahead leak. It is not a model and must not be interpreted as alpha.
    """
    bundle.validate()
    if not math.isfinite(notional) or notional <= 0:
        raise ValueError("notional must be positive")
    outcome_id = bundle.outcome_ids[0]
    point = bundle.first_point(outcome_id)
    instruction = PaperInstruction(bundle.market_id, outcome_id, SIDE_BUY, point.observed_at, notional, 0.0)
    quote = PaperQuote(bundle.market_id, outcome_id, point.observed_at, point.price, max(notional * 10.0, 1.0), point.source_ref)
    fill: PaperFill = simulate_fill(instruction, quote, fee_bps=0.0, slippage_bps_per_depth=0.0)
    if fill.status not in ("FILLED", "PARTIAL"):
        raise ValueError(f"control fill refused: {fill.reason}")
    ledger = PortfolioLedger(cash=100.0).apply(instruction, fill)
    is_winner = outcome_id in bundle.resolution_outcome_ids
    settlement_value = fill.filled_units if is_winner else 0.0
    final_cash = ledger.cash + settlement_value
    pnl = final_cash - 100.0
    return ControlReplayResult(bundle.market_id, bundle.question, outcome_id, point.observed_at, fill.fill_price or point.price, fill.filled_notional, fill.fee, settlement_value, pnl, bundle.resolution_known_at)


def collect_resolved_bundles(*, target_markets: int = 3, scan_pages: int = 5, fidelity: int = 1440, timeout: float = 30.0) -> tuple[HistoricalMarketBundle, ...]:
    """Collect a small real closed/resolved multi-market research sample."""
    if target_markets < 1:
        raise ValueError("target_markets must be positive")
    bundles: list[HistoricalMarketBundle] = []
    for page in range(scan_pages):
        payloads = fetch_closed_markets(limit=100, offset=page * 100, timeout=timeout)
        if not payloads:
            break
        acquired = datetime.now(timezone.utc).isoformat().replace("+00:00", "Z")
        for payload in payloads:
            if len(bundles) >= target_markets:
                return tuple(bundles)
            try:
                outcomes = _decode_json_array(payload.get("outcomes"), "outcomes")
                token_ids = _decode_json_array(payload.get("clobTokenIds"), "clobTokenIds")
                prices = _decode_json_array(payload.get("outcomePrices"), "outcomePrices")
                if len(outcomes) != 2 or len(token_ids) != 2 or len(prices) != 2:
                    continue
                _resolved_outcomes(payload, outcomes, prices)
                history = {token_id: fetch_price_history(token_id, fidelity=fidelity, timeout=timeout) for token_id in token_ids}
                bundle = build_market_bundle(payload, resolution_known_at=acquired, history_by_token=history)
            except (KeyError, TypeError, ValueError, OSError):
                continue
            if len(bundle.price_history) < 2:
                continue
            bundles.append(bundle)
    return tuple(bundles)


__all__ = [
    "HistoricalTapePoint",
    "HistoricalMarketBundle",
    "ControlReplayResult",
    "fetch_closed_markets",
    "fetch_price_history",
    "build_market_bundle",
    "collect_resolved_bundles",
    "run_mechanical_control",
    "GAMMA_MARKETS_URL",
    "CLOB_PRICES_HISTORY_URL",
    "HISTORICAL_PAPER_REPLAY_SCHEMA_VERSION",
]
