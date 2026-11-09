"""Deterministic paper-fill simulation with an explicit portfolio ledger.

This module is a research simulator. It never contacts a venue, never signs,
and never creates a live trading instruction. Given a historical quote and a
research size, it produces a deterministic synthetic fill using declared fee,
slippage, and visible-depth assumptions.

The simulator deliberately refuses to invent missing execution inputs: a quote
without positive depth, an invalid price, or a non-positive research size does
not produce a fill. Every state transition is represented by an immutable
record, so a paper run can be replayed and audited.
"""
from __future__ import annotations

from dataclasses import dataclass
import hashlib
import json
import math
from typing import Iterable, Optional

SCHEMA_VERSION = "polymarket-paper-simulation.v1"
SIDE_BUY = "BUY"
SIDE_SELL = "SELL"
STATUS_FILLED = "FILLED"
STATUS_PARTIAL = "PARTIAL"
STATUS_REFUSED = "REFUSED"


def _canonical(value: object) -> str:
    return json.dumps(value, sort_keys=True, separators=(",", ":"), ensure_ascii=True)


def _hash(value: object) -> str:
    return hashlib.sha256(_canonical(value).encode("utf-8")).hexdigest()


def _finite_fraction(name: str, value: float) -> None:
    if not math.isfinite(value) or not 0.0 <= value <= 1.0:
        raise ValueError(f"{name} must be finite and between 0 and 1")


@dataclass(frozen=True)
class PaperQuote:
    market_id: str
    outcome_id: str
    observed_at: str
    price: float
    available_depth: float
    source_ref: str

    def validate(self) -> None:
        if not self.market_id or not self.outcome_id or not self.source_ref:
            raise ValueError("market_id, outcome_id, and source_ref are required")
        if not self.observed_at:
            raise ValueError("observed_at is required")
        if not math.isfinite(self.price) or not 0.0 < self.price < 1.0:
            raise ValueError("price must be finite and strictly between 0 and 1")
        if not math.isfinite(self.available_depth) or self.available_depth <= 0:
            raise ValueError("available_depth must be finite and positive")


@dataclass(frozen=True)
class PaperInstruction:
    market_id: str
    outcome_id: str
    side: str
    submitted_at: str
    research_size: float
    max_slippage_bps: float = 50.0

    def canonical_payload(self) -> dict[str, object]:
        return {
            "schema_version": SCHEMA_VERSION,
            "market_id": self.market_id,
            "outcome_id": self.outcome_id,
            "side": self.side,
            "submitted_at": self.submitted_at,
            "research_size": self.research_size,
            "max_slippage_bps": self.max_slippage_bps,
        }

    def instruction_id(self) -> str:
        return f"PSI-{_hash(self.canonical_payload())[:24]}"

    def validate(self) -> None:
        if not self.market_id or not self.outcome_id or not self.submitted_at:
            raise ValueError("market and submission fields are required")
        if self.side not in (SIDE_BUY, SIDE_SELL):
            raise ValueError("side must be BUY or SELL")
        if not math.isfinite(self.research_size) or self.research_size <= 0:
            raise ValueError("research_size must be positive")
        if not math.isfinite(self.max_slippage_bps) or self.max_slippage_bps < 0:
            raise ValueError("max_slippage_bps must be finite and non-negative")


@dataclass(frozen=True)
class PaperFill:
    instruction_id: str
    status: str
    filled_notional: float
    filled_units: float
    fill_price: Optional[float]
    fee: float
    slippage_bps: float
    unfilled_notional: float
    reason: Optional[str] = None

    def validate(self) -> None:
        if not self.instruction_id:
            raise ValueError("instruction_id is required")
        if self.status not in (STATUS_FILLED, STATUS_PARTIAL, STATUS_REFUSED):
            raise ValueError("invalid fill status")
        for name, value in (
            ("filled_notional", self.filled_notional),
            ("filled_units", self.filled_units),
            ("fee", self.fee),
            ("slippage_bps", self.slippage_bps),
            ("unfilled_notional", self.unfilled_notional),
        ):
            if not math.isfinite(value) or value < 0:
                raise ValueError(f"{name} must be finite and non-negative")
        if self.status == STATUS_REFUSED:
            if self.filled_notional != 0 or self.filled_units != 0 or self.fee != 0:
                raise ValueError("refused fill must contain zero execution quantities")
            if not self.reason:
                raise ValueError("refused fill must include a reason")
        else:
            if self.fill_price is None or not 0.0 < self.fill_price < 1.0:
                raise ValueError("filled result requires a valid fill_price")
            if self.filled_notional <= 0 or self.filled_units <= 0:
                raise ValueError("filled result requires positive execution quantities")

    def as_dict(self) -> dict[str, object]:
        self.validate()
        return {
            "schema_version": SCHEMA_VERSION,
            "instruction_id": self.instruction_id,
            "status": self.status,
            "filled_notional": self.filled_notional,
            "filled_units": self.filled_units,
            "fill_price": self.fill_price,
            "fee": self.fee,
            "slippage_bps": self.slippage_bps,
            "unfilled_notional": self.unfilled_notional,
            "reason": self.reason,
        }


def simulate_fill(
    instruction: PaperInstruction,
    quote: PaperQuote,
    *,
    fee_bps: float = 0.0,
    slippage_bps_per_depth: float = 50.0,
    max_depth_fraction: float = 0.10,
) -> PaperFill:
    """Produce one deterministic synthetic fill from a quote.

    The execution price is not claimed to be historical truth. Slippage is an
    explicit model input and is capped by the instruction's tolerance. Visible
    depth limits the notional that can be filled. No randomisation is used.
    """
    instruction.validate()
    quote.validate()
    _finite_fraction("max_depth_fraction", max_depth_fraction)
    if not math.isfinite(fee_bps) or fee_bps < 0:
        raise ValueError("fee_bps must be finite and non-negative")
    if not math.isfinite(slippage_bps_per_depth) or slippage_bps_per_depth < 0:
        raise ValueError("slippage_bps_per_depth must be finite and non-negative")
    if instruction.market_id != quote.market_id or instruction.outcome_id != quote.outcome_id:
        return PaperFill(instruction.instruction_id(), STATUS_REFUSED, 0.0, 0.0, None, 0.0, 0.0, instruction.research_size, "quote_identity_mismatch")

    depth_budget = quote.available_depth * max_depth_fraction
    filled_notional = min(instruction.research_size, depth_budget)
    if filled_notional <= 0:
        return PaperFill(instruction.instruction_id(), STATUS_REFUSED, 0.0, 0.0, None, 0.0, 0.0, instruction.research_size, "insufficient_visible_depth")

    depth_fraction = filled_notional / quote.available_depth
    model_slippage_bps = min(slippage_bps_per_depth * depth_fraction, instruction.max_slippage_bps)
    direction = 1.0 if instruction.side == SIDE_BUY else -1.0
    raw_price = quote.price * (1.0 + direction * model_slippage_bps / 10_000.0)
    fill_price = min(max(raw_price, 1e-12), 1.0 - 1e-12)
    fee = filled_notional * fee_bps / 10_000.0
    filled_units = filled_notional / fill_price
    unfilled = instruction.research_size - filled_notional
    status = STATUS_FILLED if unfilled <= 1e-12 else STATUS_PARTIAL
    fill = PaperFill(
        instruction.instruction_id(), status, filled_notional, filled_units,
        fill_price, fee, model_slippage_bps, max(0.0, unfilled),
    )
    fill.validate()
    return fill


@dataclass(frozen=True)
class PortfolioLedger:
    cash: float
    positions: tuple[tuple[str, float], ...] = ()
    fees_paid: float = 0.0

    def validate(self) -> None:
        if not math.isfinite(self.cash):
            raise ValueError("cash must be finite")
        if not math.isfinite(self.fees_paid) or self.fees_paid < 0:
            raise ValueError("fees_paid must be finite and non-negative")
        for key, units in self.positions:
            if not key or not math.isfinite(units):
                raise ValueError("positions must contain finite units and non-empty keys")

    def units_for(self, position_key: str) -> float:
        for key, units in self.positions:
            if key == position_key:
                return units
        return 0.0

    def apply(self, instruction: PaperInstruction, fill: PaperFill) -> "PortfolioLedger":
        instruction.validate()
        fill.validate()
        if fill.instruction_id != instruction.instruction_id():
            raise ValueError("fill does not belong to instruction")
        if fill.status == STATUS_REFUSED or fill.filled_notional == 0:
            return self
        key = f"{instruction.market_id}:{instruction.outcome_id}"
        delta_units = fill.filled_units if instruction.side == SIDE_BUY else -fill.filled_units
        cash_delta = -fill.filled_notional - fill.fee if instruction.side == SIDE_BUY else fill.filled_notional - fill.fee
        current = dict(self.positions)
        current[key] = current.get(key, 0.0) + delta_units
        ordered = tuple(sorted((k, v) for k, v in current.items() if abs(v) > 1e-15))
        next_ledger = PortfolioLedger(self.cash + cash_delta, ordered, self.fees_paid + fill.fee)
        next_ledger.validate()
        return next_ledger


class PaperRun:
    """Append-only deterministic paper simulation state."""

    def __init__(self, ledger: PortfolioLedger):
        ledger.validate()
        self._ledger = ledger
        self._fills: list[PaperFill] = []

    @property
    def ledger(self) -> PortfolioLedger:
        return self._ledger

    @property
    def fills(self) -> tuple[PaperFill, ...]:
        return tuple(self._fills)

    def execute_simulation(self, instruction: PaperInstruction, quote: PaperQuote, **kwargs: float) -> PaperFill:
        fill = simulate_fill(instruction, quote, **kwargs)
        self._ledger = self._ledger.apply(instruction, fill)
        self._fills.append(fill)
        return fill


__all__ = [
    "PaperQuote",
    "PaperInstruction",
    "PaperFill",
    "PortfolioLedger",
    "PaperRun",
    "simulate_fill",
    "SCHEMA_VERSION",
    "SIDE_BUY",
    "SIDE_SELL",
    "STATUS_FILLED",
    "STATUS_PARTIAL",
    "STATUS_REFUSED",
]
