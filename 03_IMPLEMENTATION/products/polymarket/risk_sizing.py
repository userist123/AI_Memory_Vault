"""Deterministic analysis-only risk limits and position sizing.

This module computes a *notional research size* and the limits that constrain
it. It creates no orders, contacts no venue, holds no credentials and has no
path to execution. Its output is a number in a report.

## The two things it exists to prevent

**Kelly overriding a cap.** The Kelly criterion maximises the expected log of
terminal wealth under assumptions this system cannot honour: that the
probability is known rather than estimated, that the bet is repeated
independently, and that size does not move the price. A calibrated forecaster
with a Brier score good enough to be worth trading is still wrong often enough
that full Kelly produces drawdowns most operators abandon before the edge
arrives. So Kelly is computed, then reduced by a fraction, then clamped — and
the clamp is applied last, unconditionally, so no sequence of parameters can
route around it.

**A positive expected value becoming permission.** `RiskEngine.evaluate`
returns a refusal for reasons that have nothing to do with EV: exposure already
committed, a category already concentrated, stale data, a calibration sample
too small to mean anything, a drawdown breach, a kill switch. Every one of them
can refuse a trade whose EV is positive, and that is the point. Section 15 of
the brief states it directly — *"The system must be able to refuse a trade even
when EV appears positive."*

## What it deliberately does not do

No order construction, no venue adapter, no wallet, no signing, no credential
handling, no network. Those belong behind the human approval boundary described
in the execution protocol, and nothing here should be imported by a component
that crosses it.
"""
from __future__ import annotations

import math
from dataclasses import dataclass, field
from typing import Optional

RISK_SIZING_SCHEMA_VERSION = "polymarket-risk-sizing.v1"

SIZE_APPROVED = "APPROVED"
SIZE_REFUSED = "REFUSED"

#: Sizing policies, in the order of how much they trust the probability estimate.
SIZING_FIXED_FRACTIONAL = "fixed_fractional"
SIZING_FRACTIONAL_KELLY = "fractional_kelly"
SIZING_LIQUIDITY_CONSTRAINED = "liquidity_constrained"

_SIZING_POLICIES = (
    SIZING_FIXED_FRACTIONAL,
    SIZING_FRACTIONAL_KELLY,
    SIZING_LIQUIDITY_CONSTRAINED,
)


@dataclass(frozen=True)
class RiskLimits:
    """Hard caps. Every one of them can refuse a positive-EV trade.

    The defaults are deliberately timid. A research system with no realised
    track record has no basis for anything else, and a limit loosened once the
    evidence supports it is a decision someone made; a limit that started loose
    is one nobody made.
    """

    #: Fraction of bankroll in a single position, after every other rule.
    max_position_fraction: float = 0.02
    #: Below this, decline instead of taking a token position. A book with a
    #: cap nearly consumed will otherwise allocate whatever slice remains —
    #: 0.001% of bankroll on one occasion here — and that position pays fees
    #: and occupies an exposure slot to express no view at all.
    min_position_fraction: float = 0.002
    #: Fraction of bankroll committed across all open positions.
    max_portfolio_fraction: float = 0.20
    #: Fraction of bankroll in any one market category.
    max_category_fraction: float = 0.08
    #: Fraction of bankroll in positions whose markets have not resolved.
    max_unresolved_fraction: float = 0.15
    #: Share of visible depth this position may take. Above it, the size is not
    #: executable at the modelled price and the edge is arithmetic, not money.
    max_depth_fraction: float = 0.10
    #: Realised loss today, as a fraction of bankroll, past which nothing trades.
    max_daily_loss_fraction: float = 0.05
    #: Peak-to-trough decline past which nothing trades.
    max_drawdown_fraction: float = 0.15
    #: Net edge below this is noise once fees and slippage are taken out.
    min_net_edge: float = 0.03
    #: Resolutions this ambiguous are refused whatever the edge.
    max_resolution_risk: float = 0.30
    #: Quotes older than this are not a market state, they are a memory of one.
    max_quote_age_seconds: float = 300.0
    #: Calibration measured on fewer resolved forecasts than this is not
    #: calibration. Sizing on it is sizing on noise.
    min_calibration_samples: int = 50

    def validate(self) -> None:
        fractions = {
            "max_position_fraction": self.max_position_fraction,
            "max_portfolio_fraction": self.max_portfolio_fraction,
            "max_category_fraction": self.max_category_fraction,
            "max_unresolved_fraction": self.max_unresolved_fraction,
            "max_depth_fraction": self.max_depth_fraction,
            "max_daily_loss_fraction": self.max_daily_loss_fraction,
            "max_drawdown_fraction": self.max_drawdown_fraction,
            "min_net_edge": self.min_net_edge,
            "max_resolution_risk": self.max_resolution_risk,
        }
        for name, value in fractions.items():
            if not math.isfinite(value) or not 0.0 <= value <= 1.0:
                raise ValueError(f"{name} must be a finite fraction between 0 and 1")
        if not math.isfinite(self.max_quote_age_seconds) or self.max_quote_age_seconds < 0:
            raise ValueError("max_quote_age_seconds must be finite and non-negative")
        if self.min_calibration_samples < 0:
            raise ValueError("min_calibration_samples must be non-negative")
        if not math.isfinite(self.min_position_fraction) or self.min_position_fraction < 0:
            raise ValueError("min_position_fraction must be finite and non-negative")
        if self.min_position_fraction > self.max_position_fraction:
            raise ValueError(
                "min_position_fraction cannot exceed max_position_fraction; "
                "nothing would ever be sizeable"
            )
        if self.max_position_fraction > self.max_portfolio_fraction:
            raise ValueError(
                "max_position_fraction cannot exceed max_portfolio_fraction; a "
                "single position would be allowed to breach the book's own cap"
            )


@dataclass(frozen=True)
class SizingPolicy:
    """How much of the theoretically optimal size to actually take."""

    method: str = SIZING_FRACTIONAL_KELLY
    #: Multiplier on the Kelly fraction. A quarter is the conventional floor for
    #: an estimated rather than known probability; this system's probabilities
    #: are estimated by a council and then recalibrated, so they are further
    #: from known than that convention assumes.
    kelly_fraction: float = 0.25
    #: Size for the fixed-fractional method, before the caps.
    fixed_fraction: float = 0.01

    def validate(self) -> None:
        if self.method not in _SIZING_POLICIES:
            raise ValueError(f"unknown sizing method: {self.method}")
        for name, value in (("kelly_fraction", self.kelly_fraction),
                            ("fixed_fraction", self.fixed_fraction)):
            if not math.isfinite(value) or not 0.0 < value <= 1.0:
                raise ValueError(f"{name} must be finite and in (0, 1]")
        if self.kelly_fraction > 0.5:
            raise ValueError(
                "kelly_fraction above 0.5 is not a research setting; half Kelly "
                "already produces drawdowns most operators abandon"
            )


@dataclass(frozen=True)
class PortfolioState:
    """What is already committed. Read-only; this module never mutates it."""

    bankroll: float
    open_exposure: float = 0.0
    unresolved_exposure: float = 0.0
    category_exposure: float = 0.0
    realised_loss_today: float = 0.0
    drawdown_fraction: float = 0.0
    calibration_samples: int = 0
    kill_switch_engaged: bool = False

    def validate(self) -> None:
        if not math.isfinite(self.bankroll) or self.bankroll <= 0:
            raise ValueError("bankroll must be finite and positive")
        for name in ("open_exposure", "unresolved_exposure", "category_exposure",
                     "realised_loss_today"):
            value = getattr(self, name)
            if not math.isfinite(value) or value < 0:
                raise ValueError(f"{name} must be finite and non-negative")
        if not math.isfinite(self.drawdown_fraction) or not 0.0 <= self.drawdown_fraction <= 1.0:
            raise ValueError("drawdown_fraction must be between 0 and 1")
        if self.calibration_samples < 0:
            raise ValueError("calibration_samples must be non-negative")


@dataclass(frozen=True)
class MarketConditions:
    """What the venue looks like right now, as far as the snapshot can say."""

    price: float
    available_depth: float
    quote_age_seconds: float
    resolution_risk: float
    net_edge: float

    def validate(self) -> None:
        if not math.isfinite(self.price) or not 0.0 < self.price < 1.0:
            raise ValueError("price must be strictly between 0 and 1")
        if not math.isfinite(self.available_depth) or self.available_depth < 0:
            raise ValueError("available_depth must be finite and non-negative")
        if not math.isfinite(self.quote_age_seconds) or self.quote_age_seconds < 0:
            raise ValueError("quote_age_seconds must be finite and non-negative")
        for name in ("resolution_risk", "net_edge"):
            value = getattr(self, name)
            if not math.isfinite(value):
                raise ValueError(f"{name} must be finite")
        if not 0.0 <= self.resolution_risk <= 1.0:
            raise ValueError("resolution_risk must be between 0 and 1")


@dataclass(frozen=True)
class SizingDecision:
    schema_version: str
    outcome: str
    #: Notional research size, in the same units as bankroll. Never an order.
    size: float
    size_fraction: float
    #: What Kelly alone would have suggested, kept so a reader can see how much
    #: the caps actually bound — a size never touched by a cap is a size whose
    #: caps are untested.
    uncapped_fraction: float
    binding_constraint: str
    refusals: tuple[str, ...] = field(default_factory=tuple)

    def validate(self) -> None:
        if self.schema_version != RISK_SIZING_SCHEMA_VERSION:
            raise ValueError("unsupported risk-sizing schema version")
        if self.outcome not in (SIZE_APPROVED, SIZE_REFUSED):
            raise ValueError("outcome must be APPROVED or REFUSED")
        if not math.isfinite(self.size) or self.size < 0:
            raise ValueError("size must be finite and non-negative")
        if self.outcome == SIZE_REFUSED and self.size != 0.0:
            raise ValueError("a refused decision must carry zero size")
        if self.outcome == SIZE_REFUSED and not self.refusals:
            raise ValueError("a refusal must say why")

    def as_dict(self) -> dict[str, object]:
        self.validate()
        return {
            "schema_version": self.schema_version,
            "outcome": self.outcome,
            "size": self.size,
            "size_fraction": self.size_fraction,
            "uncapped_fraction": self.uncapped_fraction,
            "binding_constraint": self.binding_constraint,
            "refusals": list(self.refusals),
        }


def kelly_fraction(probability: float, price: float) -> float:
    """The Kelly stake for a binary contract, before any reduction.

    A contract bought at `price` pays 1 and costs `price`, so net odds are
    (1 - price) / price and the Kelly fraction reduces to (p - price) /
    (1 - price).

    Returned raw and unclamped on purpose. The reduction and the caps are
    applied by the caller, visibly, and a reader of the decision can see what
    was proposed before they bit.
    """
    if not 0.0 < price < 1.0:
        raise ValueError("price must be strictly between 0 and 1")
    if not 0.0 <= probability <= 1.0:
        raise ValueError("probability must be between 0 and 1")
    edge = probability - price
    if edge <= 0.0:
        return 0.0
    return edge / (1.0 - price)


def check_limits(
    limits: RiskLimits,
    portfolio: PortfolioState,
    conditions: MarketConditions,
) -> tuple[str, ...]:
    """Every reason this trade is refused, not merely the first one found.

    Returning all of them matters: a report saying "refused: stale quote" sends
    someone to fix the data feed, and they discover the drawdown breach only
    after they have. The refusals are independent of expected value by design.
    """
    limits.validate()
    portfolio.validate()
    conditions.validate()

    refusals: list[str] = []

    if portfolio.kill_switch_engaged:
        refusals.append("kill_switch_engaged")
    if conditions.quote_age_seconds > limits.max_quote_age_seconds:
        refusals.append("stale_quote")
    if conditions.net_edge < limits.min_net_edge:
        refusals.append("net_edge_below_minimum")
    if conditions.resolution_risk > limits.max_resolution_risk:
        refusals.append("resolution_risk_above_maximum")
    if portfolio.calibration_samples < limits.min_calibration_samples:
        refusals.append("insufficient_calibration_sample")
    if portfolio.drawdown_fraction > limits.max_drawdown_fraction:
        refusals.append("drawdown_limit_breached")
    if portfolio.realised_loss_today > limits.max_daily_loss_fraction * portfolio.bankroll:
        refusals.append("daily_loss_limit_breached")
    if portfolio.open_exposure >= limits.max_portfolio_fraction * portfolio.bankroll:
        refusals.append("portfolio_exposure_exhausted")
    if portfolio.unresolved_exposure >= limits.max_unresolved_fraction * portfolio.bankroll:
        refusals.append("unresolved_exposure_exhausted")
    if portfolio.category_exposure >= limits.max_category_fraction * portfolio.bankroll:
        refusals.append("category_exposure_exhausted")
    if conditions.available_depth <= 0.0:
        refusals.append("no_available_depth")

    return tuple(refusals)


def size_position(
    probability: float,
    conditions: MarketConditions,
    portfolio: PortfolioState,
    limits: RiskLimits | None = None,
    policy: SizingPolicy | None = None,
) -> SizingDecision:
    """A notional research size, or a refusal with its reasons.

    The order is load-bearing. Limits are checked first, so a refusal never
    depends on the arithmetic that follows it; then the method proposes a
    fraction; then the caps bind, last and unconditionally, so no combination
    of kelly_fraction and fixed_fraction can produce a size above
    max_position_fraction or above the remaining room in the book.

    This returns a number for a report. It is not an order and there is nothing
    here that could become one.
    """
    limits = limits or RiskLimits()
    policy = policy or SizingPolicy()
    policy.validate()

    refusals = check_limits(limits, portfolio, conditions)
    if refusals:
        decision = SizingDecision(
            RISK_SIZING_SCHEMA_VERSION, SIZE_REFUSED, 0.0, 0.0, 0.0,
            "risk_limits", refusals,
        )
        decision.validate()
        return decision

    if policy.method == SIZING_FIXED_FRACTIONAL:
        proposed = policy.fixed_fraction
    else:
        proposed = kelly_fraction(probability, conditions.price) * policy.kelly_fraction

    uncapped = proposed
    binding = "sizing_method"

    #: Remaining room under each cap, as a fraction of bankroll. A cap already
    #: consumed leaves zero room, which is a refusal rather than a small trade.
    room = {
        "max_position_fraction": limits.max_position_fraction,
        "portfolio_room": limits.max_portfolio_fraction
        - portfolio.open_exposure / portfolio.bankroll,
        "category_room": limits.max_category_fraction
        - portfolio.category_exposure / portfolio.bankroll,
        "unresolved_room": limits.max_unresolved_fraction
        - portfolio.unresolved_exposure / portfolio.bankroll,
        "depth_ceiling": (limits.max_depth_fraction * conditions.available_depth)
        / portfolio.bankroll,
    }
    for name, ceiling in room.items():
        if ceiling < proposed:
            proposed = max(ceiling, 0.0)
            binding = name

    if proposed < limits.min_position_fraction:
        reason = ("no_room_under_risk_limits" if proposed <= 0.0
                  else "remaining_room_below_minimum_position")
        decision = SizingDecision(
            RISK_SIZING_SCHEMA_VERSION, SIZE_REFUSED, 0.0, 0.0, uncapped,
            binding, (reason,),
        )
        decision.validate()
        return decision

    decision = SizingDecision(
        RISK_SIZING_SCHEMA_VERSION,
        SIZE_APPROVED,
        proposed * portfolio.bankroll,
        proposed,
        uncapped,
        binding,
    )
    decision.validate()
    return decision


__all__ = [
    "RISK_SIZING_SCHEMA_VERSION",
    "SIZE_APPROVED",
    "SIZE_REFUSED",
    "SIZING_FIXED_FRACTIONAL",
    "SIZING_FRACTIONAL_KELLY",
    "SIZING_LIQUIDITY_CONSTRAINED",
    "RiskLimits",
    "SizingPolicy",
    "PortfolioState",
    "MarketConditions",
    "SizingDecision",
    "kelly_fraction",
    "check_limits",
    "size_position",
]
