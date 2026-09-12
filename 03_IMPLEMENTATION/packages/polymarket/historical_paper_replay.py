"""Leakage-safe replay/settlement adapter for real historical Polymarket data."""
from __future__ import annotations

from dataclasses import dataclass
from datetime import datetime, timezone
import json
import math
from typing import Any, Mapping, Sequence
from urllib.parse import urlencode
from urllib.request import Request, urlopen

from .paper_simulation import PaperFill, PaperInstruction, PaperQuote, PortfolioLedger, SIDE_BUY, simulate_fill

GAMMA_MARKETS_URL = "https://gamma-api.polymarket.com/markets"
CLOB_PRICES_HISTORY_URL = "https://clob.polymarket.com/prices-history"
CLOB_BATCH_PRICES_HISTORY_URL = "https://clob.polymarket.com/batch-prices-history"
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
        if math.isfinite(self.price) is False or not 0.0 < self.price < 1.0:
            raise ValueError("price must be strictly between 0 and 1")
        _parse_iso(self.observed_at)

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
        if not self.resolution_outcome_ids or not set(self.resolution_outcome_ids).issubset(set(self.outcome_ids)):
            raise ValueError("resolution outcome is not one of the market outcomes")
        _parse_iso(self.resolution_known_at)
        for point in self.price_history:
            point.validate()
            if point.market_id != self.market_id:
                raise ValueError("price point market_id mismatch")
            if point.outcome_id not in self.outcome_ids:
                raise ValueError("price point outcome_id is not in market metadata")
    def first_point(self, outcome_id: str) -> HistoricalTapePoint:
        points = [p for p in self.price_history if p.outcome_id == outcome_id]
        if not points:
            raise ValueError(f"no price history for outcome {outcome_id}")
        return min(points, key=lambda p: _parse_iso(p.observed_at))

def _parse_iso(value: str) -> datetime:
    parsed = datetime.fromisoformat(value.replace("Z", "+00:00"))
    if parsed.tzinfo is None:
        raise ValueError("timestamp must include timezone")
    return parsed.astimezone(timezone.utc)

def _http_json(url: str, params: Mapping[str, Any], timeout: float = 30.0) -> Any:
    query = urlencode({k: v for k, v in params.items() if v is not None})
    request = Request(f"{url}?{query}" if query else url, headers={"Accept": "application/json", "User-Agent": "ai-memory-vault-research"})
    with urlopen(request, timeout=timeout) as response:
        return json.loads(response.read().decode("utf-8"))

def _http_json_post(url: str, payload: Mapping[str, Any], timeout: float = 30.0) -> Any:
    request = Request(url, method="POST", data=json.dumps(payload).encode("utf-8"), headers={"Accept": "application/json", "Content-Type": "application/json", "User-Agent": "ai-memory-vault-research"})
    with urlopen(request, timeout=timeout) as response:
        return json.loads(response.read().decode("utf-8"))

def fetch_closed_markets(*, limit: int = 20, offset: int = 0, order: str | None = None, ascending: bool | None = None, timeout: float = 30.0) -> list[Mapping[str, Any]]:
    if not 1 <= limit <= 100 or offset < 0:
        raise ValueError("invalid pagination")
    payload = _http_json(
        GAMMA_MARKETS_URL,
        {"closed": "true", "limit": limit, "offset": offset, "order": order, "ascending": str(ascending).lower() if ascending is not None else None},
        timeout,
    )
    if not isinstance(payload, list):
        raise ValueError("Gamma /markets must return a JSON array")
    return payload

def fetch_price_history(token_id: str, *, interval: str = "max", fidelity: int = 1440, timeout: float = 30.0) -> tuple[Mapping[str, Any], ...]:
    if not token_id or fidelity <= 0:
        raise ValueError("invalid price-history request")
    payload = _http_json(CLOB_PRICES_HISTORY_URL, {"market": token_id, "interval": interval, "fidelity": fidelity}, timeout)
    history = payload.get("history") if isinstance(payload, Mapping) else None
    if not isinstance(history, list):
        raise ValueError("CLOB prices-history response must contain history[]")
    return tuple(item for item in history if isinstance(item, Mapping))

def fetch_batch_price_history(token_ids: Sequence[str], *, interval: str = "max", fidelity: int = 1440, timeout: float = 30.0) -> Mapping[str, Sequence[Mapping[str, Any]]]:
    unique = tuple(dict.fromkeys(token_ids))
    if not unique or len(unique) > 20 or fidelity <= 0:
        raise ValueError("batch price history accepts 1-20 token ids and positive fidelity")
    payload = _http_json_post(CLOB_BATCH_PRICES_HISTORY_URL, {"markets": list(unique), "interval": interval, "fidelity": fidelity}, timeout)
    history = payload.get("history") if isinstance(payload, Mapping) else None
    if not isinstance(history, Mapping):
        raise ValueError("CLOB batch-prices-history response must contain history{}")
    return {token_id: tuple(item for item in history.get(token_id, []) if isinstance(item, Mapping)) for token_id in unique}

def _decode_json_array(value: Any, field_name: str) -> list[str]:
    raw = json.loads(value) if isinstance(value, str) else value
    if not isinstance(raw, list):
        raise ValueError(f"{field_name} must be a JSON array or array")
    return [str(x) for x in raw]

def _resolved_outcomes(payload: Mapping[str, Any], outcomes: Sequence[str], prices: Sequence[str]) -> tuple[str, ...]:
    if not payload.get("closed"):
        raise ValueError("market is not closed")
    if len(outcomes) != len(prices):
        raise ValueError("outcomes and outcomePrices length mismatch")
    parsed = [float(x) for x in prices]
    if any(not math.isfinite(x) or not 0.0 <= x <= 1.0 for x in parsed):
        raise ValueError("outcomePrices must contain probabilities in [0, 1]")
    winners = [outcomes[i] for i, price in enumerate(parsed) if price >= 0.95]
    losers = [price for price in parsed if price <= 0.05]
    if len(winners) != 1 or len(losers) != len(outcomes) - 1:
        raise ValueError("closed market does not expose an unambiguous terminal outcome")
    return tuple(winners)

def build_market_bundle(payload: Mapping[str, Any], *, resolution_known_at: str, history_by_token: Mapping[str, Sequence[Mapping[str, Any]]]) -> HistoricalMarketBundle:
    market_id = str(payload.get("id") or "")
    question = str(payload.get("question") or "")
    outcomes = _decode_json_array(payload.get("outcomes"), "outcomes")
    outcome_ids = _decode_json_array(payload.get("clobTokenIds"), "clobTokenIds")
    prices = _decode_json_array(payload.get("outcomePrices"), "outcomePrices")
    if len(outcomes) != len(outcome_ids):
        raise ValueError("outcomes and clobTokenIds length mismatch")
    winner_names = set(_resolved_outcomes(payload, outcomes, prices))
    winner_ids = tuple(outcome_ids[i] for i, name in enumerate(outcomes) if name in winner_names)
    points: list[HistoricalTapePoint] = []
    for outcome_id in outcome_ids:
        for raw in history_by_token.get(outcome_id, ()):
            if "t" not in raw or "p" not in raw:
                continue
            observed_at = datetime.fromtimestamp(float(raw["t"]), tz=timezone.utc).isoformat().replace("+00:00", "Z")
            points.append(HistoricalTapePoint(market_id, outcome_id, observed_at, float(raw["p"]), f"{CLOB_PRICES_HISTORY_URL}?market={outcome_id}"))
    bundle = HistoricalMarketBundle(market_id, question, tuple(outcome_ids), tuple(outcomes), winner_ids, resolution_known_at, tuple(sorted(points, key=lambda p: (_parse_iso(p.observed_at), p.outcome_id))), f"{GAMMA_MARKETS_URL}?id={market_id}")
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
    settlement_value = fill.filled_units if outcome_id in bundle.resolution_outcome_ids else 0.0
    pnl = ledger.cash + settlement_value - 100.0
    return ControlReplayResult(bundle.market_id, bundle.question, outcome_id, point.observed_at, fill.fill_price or point.price, fill.filled_notional, fill.fee, settlement_value, pnl, bundle.resolution_known_at)

def collect_resolved_bundles(*, target_markets: int = 3, scan_pages: int = 5, fidelity: int = 1440, timeout: float = 30.0) -> tuple[HistoricalMarketBundle, ...]:
    if target_markets < 1:
        raise ValueError("target_markets must be positive")
    bundles: list[HistoricalMarketBundle] = []
    attempted = 0
    rejected: dict[str, int] = {}
    last_error: str | None = None
    for page in range(scan_pages):
        payloads = fetch_closed_markets(limit=100, offset=page * 100, order="endDate", ascending=True, timeout=timeout)
        if not payloads:
            break
        acquired = datetime.now(timezone.utc).isoformat().replace("+00:00", "Z")
        for payload in payloads:
            if len(bundles) >= target_markets:
                return tuple(bundles)
            attempted += 1
            try:
                if payload.get("enableOrderBook") is not True:
                    raise ValueError("market does not expose CLOB order book")
                outcomes = _decode_json_array(payload.get("outcomes"), "outcomes")
                token_ids = _decode_json_array(payload.get("clobTokenIds"), "clobTokenIds")
                prices = _decode_json_array(payload.get("outcomePrices"), "outcomePrices")
                if len(outcomes) != 2 or len(token_ids) != 2 or len(prices) != 2:
                    raise ValueError("non-binary market")
                _resolved_outcomes(payload, outcomes, prices)
                history = fetch_batch_price_history(token_ids, fidelity=fidelity, timeout=timeout)
                bundle = build_market_bundle(payload, resolution_known_at=acquired, history_by_token=history)
                if len(bundle.price_history) < 2:
                    raise ValueError("insufficient CLOB history")
            except (KeyError, TypeError, ValueError, OSError) as exc:
                last_error = str(exc)
                rejected[last_error] = rejected.get(last_error, 0) + 1
                continue
            bundles.append(bundle)
    if len(bundles) < target_markets:
        raise RuntimeError(json.dumps({"reason":"insufficient_suitable_markets","collected":len(bundles),"required":target_markets,"attempted":attempted,"rejected":rejected,"last_error":last_error}, sort_keys=True))
    return tuple(bundles)

__all__ = ["HistoricalTapePoint", "HistoricalMarketBundle", "ControlReplayResult", "fetch_closed_markets", "fetch_price_history", "fetch_batch_price_history", "build_market_bundle", "collect_resolved_bundles", "run_mechanical_control", "GAMMA_MARKETS_URL", "CLOB_PRICES_HISTORY_URL", "CLOB_BATCH_PRICES_HISTORY_URL", "HISTORICAL_PAPER_REPLAY_SCHEMA_VERSION"]
