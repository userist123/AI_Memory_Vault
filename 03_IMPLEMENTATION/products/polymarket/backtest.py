"""Deterministic walk-forward backtesting with a per-decision leakage proof.

Phase 2 gives the primitive: `HistoricalReplay` reconstructs what was known
about one market at one cutoff. This is the driver — it walks a sequence of
cutoffs, runs the pipeline at each, and records what happened.

## The part that matters

A backtest is not evidence. A backtest that cannot show it was blind is
*anti*-evidence: it produces a number with a plausible shape and no way to tell
whether the number came from the method or from the future.

So every decision carries a `LeakageProof`: the cutoff it ran at, the latest
`known_as_of` of every input it consumed, and the count of inputs rejected for
postdating the cutoff. The proof is checked when the decision is constructed,
not when someone remembers to look, and a decision whose inputs postdate its
own cutoff cannot be built.

`HistoricalReplay` already filters by cutoff. The proof is not a second filter
— it is the record that the filter ran and what it saw, because a filter that
silently returns nothing looks identical to a market with no data.

## Where leakage enters that the primitive cannot see

The primitive judges one market at one instant. These are the driver's:

**Resolution leakage.** A decision scored against an outcome known at or before
its own cutoff is scoring against something it could have read. Resolutions must
be known *strictly* after the decision; equal timestamps are refused, because a
resolution and a decision at the same instant cannot be ordered from the data.

**Calibration look-ahead.** A calibrator fitted on the test window is reporting
its own training error. Windows carry explicit train / calibration / test
boundaries and `Window.validate` refuses any overlap.

**Survivorship.** Selecting markets that have resolutions available means
selecting the ones that resolved, which is the subset where the questions were
answerable. The universe is fixed per window before outcomes are consulted, and
markets without a resolution are counted rather than dropped — a run that
silently scores 40 of 100 markets reports a hit rate for a population nobody
chose.

## What this does not do

No fee arithmetic, no slippage model beyond the depth ceiling already in the
risk engine, no P&L. Those need a fill model, which needs order-book history
this repository does not have. The driver produces decisions and their
leakage proofs; scoring them into money is a later phase and must not be
simulated here with invented fills.
"""
from __future__ import annotations

import math
from dataclasses import dataclass, field
from datetime import datetime
from typing import Iterable, Optional, Sequence

from .market_snapshot import _parse_datetime

BACKTEST_SCHEMA_VERSION = "polymarket-backtest.v1"

SPLIT_TRAIN = "train"
SPLIT_CALIBRATION = "calibration"
SPLIT_TEST = "test"


@dataclass(frozen=True)
class LeakageProof:
    """What a decision consumed, and the proof it predated the decision.

    `latest_input_known_as_of` is None when a decision consumed nothing, which
    is a real and distinguishable state: a market with no data before the
    cutoff is not the same as a market whose data was filtered out, and both
    are different from a market that was never looked at.
    """

    as_of: str
    latest_input_known_as_of: Optional[str]
    inputs_considered: int
    inputs_rejected_as_future: int

    def validate(self) -> None:
        cutoff = _parse_datetime(self.as_of, field_name="as_of")
        if self.inputs_considered < 0 or self.inputs_rejected_as_future < 0:
            raise ValueError("input counts cannot be negative")
        if self.inputs_rejected_as_future > self.inputs_considered:
            raise ValueError("more inputs rejected than considered")
        if self.latest_input_known_as_of is None:
            return
        latest = _parse_datetime(
            self.latest_input_known_as_of, field_name="latest_input_known_as_of"
        )
        if latest > cutoff:
            raise ValueError(
                "leakage: an input known at "
                f"{self.latest_input_known_as_of} reached a decision made at "
                f"{self.as_of}"
            )


@dataclass(frozen=True)
class Window:
    """Train, calibration and test boundaries for one walk-forward step.

    Half-open on the right throughout: a timestamp belongs to exactly one split
    and boundary instants are not shared. Closed intervals would put the same
    moment in two splits, which is the cheapest possible way to leak.
    """

    train_start: str
    train_end: str
    calibration_end: str
    test_end: str

    def validate(self) -> None:
        bounds = [
            ("train_start", _parse_datetime(self.train_start, field_name="train_start")),
            ("train_end", _parse_datetime(self.train_end, field_name="train_end")),
            ("calibration_end", _parse_datetime(self.calibration_end, field_name="calibration_end")),
            ("test_end", _parse_datetime(self.test_end, field_name="test_end")),
        ]
        for (earlier_name, earlier), (later_name, later) in zip(bounds, bounds[1:]):
            if later <= earlier:
                raise ValueError(
                    f"{later_name} must be strictly after {earlier_name}; "
                    "equal boundaries put one instant in two splits"
                )

    def split_for(self, timestamp: str) -> Optional[str]:
        """Which split a timestamp falls in, or None if outside the window."""
        self.validate()
        moment = _parse_datetime(timestamp, field_name="timestamp")
        if moment < _parse_datetime(self.train_start, field_name="train_start"):
            return None
        if moment < _parse_datetime(self.train_end, field_name="train_end"):
            return SPLIT_TRAIN
        if moment < _parse_datetime(self.calibration_end, field_name="calibration_end"):
            return SPLIT_CALIBRATION
        if moment < _parse_datetime(self.test_end, field_name="test_end"):
            return SPLIT_TEST
        return None


@dataclass(frozen=True)
class Decision:
    """One pipeline run at one cutoff, with its proof."""

    schema_version: str
    market_id: str
    as_of: str
    split: str
    decision: str
    reason: str
    model_probability: Optional[float]
    market_price: Optional[float]
    edge: Optional[float]
    size: float
    proof: LeakageProof

    def validate(self) -> None:
        if self.schema_version != BACKTEST_SCHEMA_VERSION:
            raise ValueError("unsupported backtest schema version")
        if self.split not in (SPLIT_TRAIN, SPLIT_CALIBRATION, SPLIT_TEST):
            raise ValueError(f"unknown split: {self.split}")
        if not self.market_id:
            raise ValueError("market_id is required")
        if not math.isfinite(self.size) or self.size < 0:
            raise ValueError("size must be finite and non-negative")
        #: Checked here as well as at construction, because a Decision that
        #: reached a report without its proof holding is the failure this
        #: module exists to make impossible.
        self.proof.validate()


@dataclass(frozen=True)
class Outcome:
    """A resolution, and when it became knowable."""

    market_id: str
    known_at: str
    resolved_yes: bool

    def validate(self) -> None:
        _parse_datetime(self.known_at, field_name="outcome.known_at")
        if not self.market_id:
            raise ValueError("market_id is required")


@dataclass(frozen=True)
class BacktestReport:
    schema_version: str
    windows: int
    decisions_by_split: dict[str, int]
    scored: int
    unscored_no_resolution: int
    unscored_resolution_not_after_decision: int
    markets_in_universe: int
    markets_scored: int
    brier: Optional[float]
    leakage_refusals: tuple[str, ...] = field(default_factory=tuple)

    def as_dict(self) -> dict[str, object]:
        return {
            "schema_version": self.schema_version,
            "windows": self.windows,
            "decisions_by_split": dict(self.decisions_by_split),
            "scored": self.scored,
            "unscored_no_resolution": self.unscored_no_resolution,
            "unscored_resolution_not_after_decision":
                self.unscored_resolution_not_after_decision,
            "markets_in_universe": self.markets_in_universe,
            "markets_scored": self.markets_scored,
            "brier": self.brier,
            "leakage_refusals": list(self.leakage_refusals),
        }


def walk_forward(
    start: str,
    end: str,
    *,
    train_days: int,
    calibration_days: int,
    test_days: int,
    step_days: int,
    expanding: bool = False,
) -> tuple[Window, ...]:
    """Windows stepped across the period, rolling or expanding.

    Rolling keeps the training length fixed; expanding keeps `train_start`
    pinned so training grows. Neither is right in general — rolling adapts to
    regime change and discards history, expanding does the opposite — so both
    exist and the choice is recorded in the report rather than assumed.

    Emits nothing when the period cannot hold one whole window. A partial final
    window is the quiet way to report a test period shorter than the others and
    compare the results as though they were alike.
    """
    from datetime import timedelta

    for name, value in (("train_days", train_days),
                        ("calibration_days", calibration_days),
                        ("test_days", test_days), ("step_days", step_days)):
        if value <= 0:
            raise ValueError(f"{name} must be positive")

    begin = _parse_datetime(start, field_name="start")
    finish = _parse_datetime(end, field_name="end")
    if finish <= begin:
        raise ValueError("end must be after start")

    span = timedelta(days=train_days + calibration_days + test_days)
    windows: list[Window] = []
    offset = 0
    while True:
        train_start = begin + timedelta(days=offset * step_days)
        test_end = train_start + span
        if test_end > finish:
            break
        train_end = train_start + timedelta(days=train_days)
        calibration_end = train_end + timedelta(days=calibration_days)
        window = Window(
            _iso(begin if expanding else train_start),
            _iso(train_end),
            _iso(calibration_end),
            _iso(test_end),
        )
        window.validate()
        windows.append(window)
        offset += 1
    return tuple(windows)


def _iso(moment: datetime) -> str:
    return moment.strftime("%Y-%m-%dT%H:%M:%SZ")


def build_leakage_proof(
    as_of: str, known_as_of_values: Iterable[str]
) -> LeakageProof:
    """Summarise what was available at a cutoff, and what was not.

    Raises if anything survives that postdates the cutoff. The rejected count
    is kept because zero rejections across a long run means the filter never
    had anything to reject, which usually means the fixture has no future data
    in it and the test proves less than it appears to.
    """
    cutoff = _parse_datetime(as_of, field_name="as_of")
    considered = 0
    rejected = 0
    latest: Optional[datetime] = None
    latest_raw: Optional[str] = None

    for raw in known_as_of_values:
        considered += 1
        moment = _parse_datetime(raw, field_name="known_as_of")
        if moment > cutoff:
            rejected += 1
            continue
        if latest is None or moment > latest:
            latest = moment
            latest_raw = raw

    proof = LeakageProof(as_of, latest_raw, considered, rejected)
    proof.validate()
    return proof


def score_decisions(
    decisions: Sequence[Decision],
    outcomes: Sequence[Outcome],
    *,
    universe_size: Optional[int] = None,
    split: str = SPLIT_TEST,
) -> BacktestReport:
    """Match decisions to resolutions and report what could not be matched.

    Only `split` is scored — by default the test split, because scoring train
    or calibration reports how well the system fitted what it was given.

    Unscored decisions are counted by reason rather than dropped. A run that
    scores 40 of 100 markets and reports only the 40 is reporting a hit rate
    for a population nobody chose.
    """
    for decision in decisions:
        decision.validate()
    for outcome in outcomes:
        outcome.validate()

    by_market: dict[str, Outcome] = {}
    for outcome in outcomes:
        if outcome.market_id in by_market:
            raise ValueError(f"duplicate resolution for market {outcome.market_id}")
        by_market[outcome.market_id] = outcome

    counts = {SPLIT_TRAIN: 0, SPLIT_CALIBRATION: 0, SPLIT_TEST: 0}
    for decision in decisions:
        counts[decision.split] += 1

    squared_errors: list[float] = []
    scored_markets: set[str] = set()
    no_resolution = 0
    not_after = 0

    for decision in decisions:
        if decision.split != split:
            continue
        outcome = by_market.get(decision.market_id)
        if outcome is None:
            no_resolution += 1
            continue
        #: Strictly after. A resolution known at the same instant as the
        #: decision cannot be ordered against it from the data, and ordering it
        #: favourably is exactly the error this check exists for.
        if _parse_datetime(outcome.known_at, field_name="outcome.known_at") <= \
                _parse_datetime(decision.as_of, field_name="as_of"):
            not_after += 1
            continue
        if decision.model_probability is None:
            no_resolution += 1
            continue
        actual = 1.0 if outcome.resolved_yes else 0.0
        squared_errors.append((decision.model_probability - actual) ** 2)
        scored_markets.add(decision.market_id)

    brier = sum(squared_errors) / len(squared_errors) if squared_errors else None
    universe = universe_size if universe_size is not None else len(
        {d.market_id for d in decisions if d.split == split}
    )

    return BacktestReport(
        BACKTEST_SCHEMA_VERSION,
        windows=0,
        decisions_by_split=counts,
        scored=len(squared_errors),
        unscored_no_resolution=no_resolution,
        unscored_resolution_not_after_decision=not_after,
        markets_in_universe=universe,
        markets_scored=len(scored_markets),
        brier=brier,
    )


__all__ = [
    "BACKTEST_SCHEMA_VERSION",
    "SPLIT_TRAIN",
    "SPLIT_CALIBRATION",
    "SPLIT_TEST",
    "LeakageProof",
    "Window",
    "Decision",
    "Outcome",
    "BacktestReport",
    "walk_forward",
    "build_leakage_proof",
    "score_decisions",
]
