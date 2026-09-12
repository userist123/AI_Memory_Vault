"""A resolution without a trustworthy `known_at` is worse than no resolution.

It is not a weaker record — it is unusable, and dangerous because it will
happily produce a Brier score. These tests are mostly about what the module
refuses.
"""
from __future__ import annotations

import pytest

from packages.polymarket.resolutions import (
    ACCEPTABLE_SOURCES,
    RESOLUTION_SCHEMA_VERSION,
    SOURCE_MANUAL_ATTESTED,
    SOURCE_ONCHAIN_SETTLEMENT,
    STATUS_CANCELLED,
    STATUS_INVALID,
    STATUS_RESOLVED,
    ResolutionRecord,
    ResolutionSet,
    parse_resolution,
)


def record(**overrides) -> ResolutionRecord:
    base = dict(
        schema_version=RESOLUTION_SCHEMA_VERSION,
        market_id="512345",
        status=STATUS_RESOLVED,
        winning_outcome_ids=("tok-yes",),
        known_at="2026-03-20T14:32:11Z",
        source=SOURCE_ONCHAIN_SETTLEMENT,
        source_ref="0xdeadbeef",
    )
    base.update(overrides)
    return ResolutionRecord(**base)


# --- the timestamp that makes a record usable --------------------------------

def test_a_close_time_source_cannot_be_named():
    """Using a market's close time as its resolution time is the specific
    mistake this module prevents. The constant is absent so nobody adds it back
    as a convenience."""
    import packages.polymarket.resolutions as module
    assert not hasattr(module, "SOURCE_DERIVED_CLOSE_TIME")
    assert ACCEPTABLE_SOURCES == {SOURCE_ONCHAIN_SETTLEMENT, SOURCE_MANUAL_ATTESTED}


@pytest.mark.parametrize("source", ["close_time", "gamma-api", "guess", ""])
def test_an_unacceptable_source_is_refused(source):
    with pytest.raises(ValueError, match="not an acceptable resolution source|required"):
        record(source=source).validate()


def test_a_record_written_before_it_was_knowable_is_refused():
    with pytest.raises(ValueError, match="written down before it was knowable"):
        record(
            known_at="2026-03-20T14:32:11Z",
            recorded_at="2026-03-19T00:00:00Z",
        ).validate()


def test_a_traceless_record_is_refused():
    """A resolution nobody can trace back is an assertion, not a record."""
    with pytest.raises(ValueError, match="source_ref is required"):
        record(source_ref="").validate()


# --- statuses ----------------------------------------------------------------

def test_only_a_resolved_market_is_scorable():
    """Treating cancelled markets as losses would punish a system for
    questions that were never answerable."""
    assert record().is_scorable
    assert not record(status=STATUS_CANCELLED, winning_outcome_ids=()).is_scorable
    assert not record(status=STATUS_INVALID, winning_outcome_ids=()).is_scorable


def test_a_resolved_market_must_name_a_winner():
    with pytest.raises(ValueError, match="must name at least one winner"):
        record(winning_outcome_ids=()).validate()


@pytest.mark.parametrize("status", [STATUS_CANCELLED, STATUS_INVALID])
def test_a_cancelled_market_cannot_have_a_winner(status):
    """That is an answer where there is none."""
    with pytest.raises(ValueError, match="an answer where there is none"):
        record(status=status, winning_outcome_ids=("tok-yes",)).validate()


def test_a_non_terminal_status_has_no_resolution():
    with pytest.raises(ValueError, match="has no resolution"):
        record(status="open").validate()


def test_duplicate_winners_are_refused():
    with pytest.raises(ValueError, match="duplicates"):
        record(winning_outcome_ids=("tok-yes", "tok-yes")).validate()


# --- the set -----------------------------------------------------------------

def test_two_conflicting_resolutions_are_refused_rather_than_chosen_between():
    """One of them is wrong, and choosing silently is how a backtest gets the
    answer it wanted."""
    resolutions = ResolutionSet([record()])
    with pytest.raises(ValueError, match="conflicting resolutions"):
        resolutions.add(record(winning_outcome_ids=("tok-no",)))


def test_an_identical_repeat_is_accepted():
    """Ingesting the same source twice is not a conflict."""
    resolutions = ResolutionSet([record(), record()])
    assert len(resolutions) == 1


def test_the_set_reports_how_much_rested_on_attestation():
    """A benchmark resting mostly on attested outcomes is a different claim
    from one resting on chain data, and the difference should not have to be
    dug out."""
    resolutions = ResolutionSet([
        record(market_id="a"),
        record(market_id="b"),
        record(market_id="c", source=SOURCE_MANUAL_ATTESTED, source_ref="note-1"),
    ])
    assert resolutions.by_source() == {
        SOURCE_ONCHAIN_SETTLEMENT: 2,
        SOURCE_MANUAL_ATTESTED: 1,
    }


def test_the_set_separates_scorable_from_terminal_but_unanswerable():
    resolutions = ResolutionSet([
        record(market_id="a"),
        record(market_id="b", status=STATUS_CANCELLED, winning_outcome_ids=()),
        record(market_id="c", status=STATUS_INVALID, winning_outcome_ids=()),
    ])
    assert len(resolutions) == 3
    assert len(resolutions.scorable()) == 1
    assert resolutions.by_status() == {
        STATUS_RESOLVED: 1, STATUS_CANCELLED: 1, STATUS_INVALID: 1,
    }


# --- parsing -----------------------------------------------------------------

def test_parse_accepts_a_complete_payload():
    parsed = parse_resolution({
        "market_id": "512345",
        "status": "resolved",
        "winning_outcome_ids": ["tok-yes"],
        "known_at": "2026-03-20T14:32:11Z",
        "source": SOURCE_ONCHAIN_SETTLEMENT,
        "source_ref": "0xdeadbeef",
    })
    assert parsed.market_id == "512345"
    assert parsed.is_scorable


@pytest.mark.parametrize("missing", ["market_id", "status", "known_at", "source", "source_ref"])
def test_nothing_is_defaulted(missing):
    """A defaulted known_at or source is the shortcut that turns an unusable
    resolution into one that scores."""
    payload = {
        "market_id": "1", "status": "resolved", "winning_outcome_ids": ["y"],
        "known_at": "2026-03-20T14:32:11Z", "source": SOURCE_ONCHAIN_SETTLEMENT,
        "source_ref": "0xabc",
    }
    del payload[missing]
    with pytest.raises(ValueError, match="missing required field"):
        parse_resolution(payload)


def test_a_string_of_winners_is_not_a_sequence_of_them():
    """"yes" would become ('y','e','s') and score against three outcomes."""
    with pytest.raises(ValueError, match="must be a sequence, not a string"):
        parse_resolution({
            "market_id": "1", "status": "resolved", "winning_outcome_ids": "yes",
            "known_at": "2026-03-20T14:32:11Z", "source": SOURCE_ONCHAIN_SETTLEMENT,
            "source_ref": "0xabc",
        })


def test_the_record_serialises():
    payload = record().as_dict()
    assert payload["schema_version"] == RESOLUTION_SCHEMA_VERSION
    assert payload["winning_outcome_ids"] == ["tok-yes"]
    assert payload["source"] == SOURCE_ONCHAIN_SETTLEMENT


def test_the_module_touches_no_network():
    """Fetching belongs to an adapter that can reach a source carrying
    settlement timestamps, and that adapter must not be written by guessing a
    response shape."""
    import pathlib
    source = (pathlib.Path(__file__).resolve().parents[2] / "03_IMPLEMENTATION"
              / "packages" / "polymarket" / "resolutions.py").read_text(encoding="utf-8")
    for forbidden in ("urlopen", "urllib", "requests", "socket", "httpx"):
        assert forbidden not in source
