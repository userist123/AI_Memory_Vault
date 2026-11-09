"""Resolution records, and the one field that makes them usable.

A backtest scores a decision against what actually happened. That requires
knowing *when* what happened became knowable, and it is the only hard part.

## Why `known_at` is the whole problem

`score_decisions` refuses to score a decision against a resolution known at or
before the decision's own cutoff — an outcome the decision could have read is
not a test of the decision. So a resolution without a trustworthy `known_at` is
not a weaker resolution; it is unusable, and worse than absent because it will
happily produce a Brier score.

Three timestamps get confused here, and only one of them is right:

    when the market closed          trading stopped; the answer may not exist yet
    when someone fetched it         says more about the fetcher than the event
    when the resolution settled     the answer became knowable — this one

A live listing gives the first two. `gamma_ingest` therefore records no
resolution at all, and this module exists so resolutions arrive from a source
that can supply the third.

## Acceptable sources

`SOURCE_ONCHAIN_SETTLEMENT` — an oracle settlement transaction. Its block
timestamp is when the answer entered the public record, which is exactly the
definition wanted. This is the source to prefer.

`SOURCE_MANUAL_ATTESTED` — a human recorded the outcome and the moment they can
defend as the moment it was knowable. Allowed, marked, and never silently
mixed with the first: `ResolutionSet.by_source()` exists so a run can report
how much of its scoring rested on attestation.

`SOURCE_DERIVED_CLOSE_TIME` is deliberately **not** a constant here. Using a
market's close time as its resolution time is the specific mistake this module
is built to prevent, and there is a test asserting the name is absent so nobody
adds it back as a convenience.

## What this does not do

No network. It validates and holds records; fetching them belongs to whichever
adapter can reach a source that carries settlement timestamps, and that adapter
must not be written by guessing a response shape.
"""
from __future__ import annotations

from dataclasses import dataclass
from typing import Iterable, Iterator, Mapping, Optional

from .market_snapshot import _parse_datetime

RESOLUTION_SCHEMA_VERSION = "polymarket-resolution.v1"

SOURCE_ONCHAIN_SETTLEMENT = "onchain_settlement"
SOURCE_MANUAL_ATTESTED = "manual_attested"

ACCEPTABLE_SOURCES = frozenset({SOURCE_ONCHAIN_SETTLEMENT, SOURCE_MANUAL_ATTESTED})

STATUS_RESOLVED = "resolved"
STATUS_INVALID = "invalid"
STATUS_CANCELLED = "cancelled"

TERMINAL_STATUSES = frozenset({STATUS_RESOLVED, STATUS_INVALID, STATUS_CANCELLED})


@dataclass(frozen=True)
class ResolutionRecord:
    """One market's outcome, with the moment it became knowable.

    `winning_outcome_ids` is empty for `invalid` and `cancelled`, and that is a
    real distinction rather than missing data: a cancelled market has no answer,
    which is different from an answer nobody recorded. A backtest must not score
    either, and must not confuse them when reporting why.
    """

    schema_version: str
    market_id: str
    status: str
    winning_outcome_ids: tuple[str, ...]
    known_at: str
    source: str
    source_ref: str
    recorded_at: Optional[str] = None

    def validate(self) -> None:
        if self.schema_version != RESOLUTION_SCHEMA_VERSION:
            raise ValueError("unsupported resolution schema version")
        if not self.market_id:
            raise ValueError("market_id is required")
        if self.status not in TERMINAL_STATUSES:
            raise ValueError(
                f"status must be one of {sorted(TERMINAL_STATUSES)}; a market "
                "that has not reached a terminal state has no resolution"
            )
        if self.source not in ACCEPTABLE_SOURCES:
            raise ValueError(
                f"{self.source!r} is not an acceptable resolution source. A "
                "resolution time derived from a market's close time is not a "
                "resolution time — see the module docstring."
            )
        if not self.source_ref:
            raise ValueError(
                "source_ref is required: a resolution nobody can trace back is "
                "an assertion, not a record"
            )

        known = _parse_datetime(self.known_at, field_name="known_at")
        if self.recorded_at is not None:
            recorded = _parse_datetime(self.recorded_at, field_name="recorded_at")
            if recorded < known:
                raise ValueError(
                    "recorded_at precedes known_at: the resolution was written "
                    "down before it was knowable"
                )

        if self.status == STATUS_RESOLVED and not self.winning_outcome_ids:
            raise ValueError("a resolved market must name at least one winner")
        if self.status != STATUS_RESOLVED and self.winning_outcome_ids:
            raise ValueError(
                f"a {self.status} market cannot have winning outcomes; that is "
                "an answer where there is none"
            )
        if len(set(self.winning_outcome_ids)) != len(self.winning_outcome_ids):
            raise ValueError("winning_outcome_ids contains duplicates")

    @property
    def is_scorable(self) -> bool:
        """Only `resolved` markets carry an answer to score against.

        Invalid and cancelled markets are terminal and unscorable. Treating
        them as losses would punish a system for questions that were never
        answerable, and dropping them silently would hide how many there were.
        """
        return self.status == STATUS_RESOLVED

    def as_dict(self) -> dict[str, object]:
        self.validate()
        return {
            "schema_version": self.schema_version,
            "market_id": self.market_id,
            "status": self.status,
            "winning_outcome_ids": list(self.winning_outcome_ids),
            "known_at": self.known_at,
            "source": self.source,
            "source_ref": self.source_ref,
            "recorded_at": self.recorded_at,
        }


class ResolutionSet:
    """Validated resolutions, one per market.

    Refuses a second record for a market rather than picking one. Two
    resolutions for one question means one of them is wrong, and choosing
    silently is how a backtest gets the answer it wanted.
    """

    def __init__(self, records: Iterable[ResolutionRecord] = ()) -> None:
        self._by_market: dict[str, ResolutionRecord] = {}
        for record in records:
            self.add(record)

    def add(self, record: ResolutionRecord) -> None:
        record.validate()
        existing = self._by_market.get(record.market_id)
        if existing is not None and existing != record:
            raise ValueError(
                f"conflicting resolutions for market {record.market_id}: "
                f"{existing.status}@{existing.known_at} from {existing.source} "
                f"and {record.status}@{record.known_at} from {record.source}"
            )
        self._by_market[record.market_id] = record

    def get(self, market_id: str) -> Optional[ResolutionRecord]:
        return self._by_market.get(market_id)

    def scorable(self) -> tuple[ResolutionRecord, ...]:
        return tuple(r for r in self._by_market.values() if r.is_scorable)

    def by_source(self) -> Mapping[str, int]:
        """How many records came from where.

        Reported so a run can say how much of its scoring rested on human
        attestation rather than settlement. A benchmark resting mostly on
        attested outcomes is a different claim from one resting on chain data,
        and the difference should not have to be dug out.
        """
        counts: dict[str, int] = {}
        for record in self._by_market.values():
            counts[record.source] = counts.get(record.source, 0) + 1
        return counts

    def by_status(self) -> Mapping[str, int]:
        counts: dict[str, int] = {}
        for record in self._by_market.values():
            counts[record.status] = counts.get(record.status, 0) + 1
        return counts

    def __len__(self) -> int:
        return len(self._by_market)

    def __iter__(self) -> Iterator[ResolutionRecord]:
        return iter(self._by_market.values())


def parse_resolution(payload: Mapping[str, object]) -> ResolutionRecord:
    """Build a record from a mapping, validating before returning it.

    Every field is required except `recorded_at`. Nothing is defaulted: a
    defaulted `known_at` or a defaulted `source` is precisely the shortcut that
    turns an unusable resolution into one that scores.
    """
    missing = [
        key for key in ("market_id", "status", "known_at", "source", "source_ref")
        if not payload.get(key)
    ]
    if missing:
        raise ValueError(f"resolution is missing required field(s): {missing}")

    winners = payload.get("winning_outcome_ids") or ()
    if isinstance(winners, (str, bytes)):
        raise ValueError("winning_outcome_ids must be a sequence, not a string")

    record = ResolutionRecord(
        schema_version=RESOLUTION_SCHEMA_VERSION,
        market_id=str(payload["market_id"]),
        status=str(payload["status"]),
        winning_outcome_ids=tuple(str(w) for w in winners),
        known_at=str(payload["known_at"]),
        source=str(payload["source"]),
        source_ref=str(payload["source_ref"]),
        recorded_at=str(payload["recorded_at"]) if payload.get("recorded_at") else None,
    )
    record.validate()
    return record


__all__ = [
    "RESOLUTION_SCHEMA_VERSION",
    "SOURCE_ONCHAIN_SETTLEMENT",
    "SOURCE_MANUAL_ATTESTED",
    "ACCEPTABLE_SOURCES",
    "STATUS_RESOLVED",
    "STATUS_INVALID",
    "STATUS_CANCELLED",
    "TERMINAL_STATUSES",
    "ResolutionRecord",
    "ResolutionSet",
    "parse_resolution",
]
