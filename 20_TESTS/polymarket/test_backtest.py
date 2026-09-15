"""A backtest that cannot show it was blind is worse than no backtest.

Every test here is about one of the four ways this driver could produce a
number that looks like evidence and is not: an input from after the cutoff, a
resolution the decision could have read, a calibrator fitted on its own test
set, or a population chosen by which markets happened to resolve.
"""
from __future__ import annotations

import pytest

from packages.polymarket.backtest import (
    BACKTEST_SCHEMA_VERSION,
    SPLIT_CALIBRATION,
    SPLIT_TEST,
    SPLIT_TRAIN,
    Decision,
    LeakageProof,
    Outcome,
    Window,
    build_leakage_proof,
    score_decisions,
    walk_forward,
)

CUTOFF = "2026-03-01T12:00:00Z"


def proof(**overrides) -> LeakageProof:
    base = dict(as_of=CUTOFF, latest_input_known_as_of="2026-03-01T11:00:00Z",
                inputs_considered=3, inputs_rejected_as_future=0)
    base.update(overrides)
    return LeakageProof(**base)


def decision(**overrides) -> Decision:
    base = dict(
        schema_version=BACKTEST_SCHEMA_VERSION, market_id="m1", as_of=CUTOFF,
        split=SPLIT_TEST, decision="BET", reason="positive_edge_above_threshold",
        model_probability=0.70, market_price=0.50, edge=0.20, size=100.0,
        proof=proof(),
    )
    base.update(overrides)
    return Decision(**base)


# --- leakage: an input from after the cutoff ---------------------------------

def test_an_input_known_after_the_cutoff_cannot_be_built_into_a_decision():
    """The proof is checked at construction, not when someone remembers."""
    with pytest.raises(ValueError, match="leakage"):
        proof(latest_input_known_as_of="2026-03-01T12:00:01Z").validate()


def test_build_leakage_proof_drops_future_inputs_and_counts_them():
    result = build_leakage_proof(CUTOFF, [
        "2026-03-01T09:00:00Z",
        "2026-03-01T11:59:59Z",
        "2026-03-01T12:00:01Z",   # future
        "2026-03-05T00:00:00Z",   # future
    ])
    assert result.latest_input_known_as_of == "2026-03-01T11:59:59Z"
    assert result.inputs_considered == 4
    assert result.inputs_rejected_as_future == 2


def test_an_input_exactly_at_the_cutoff_is_available():
    """At the cutoff is known by the cutoff. The boundary belongs to the past,
    which is the opposite of the resolution rule below — and deliberately so:
    an input observed at the instant is readable, an outcome known at the
    instant cannot be ordered against the decision."""
    result = build_leakage_proof(CUTOFF, [CUTOFF])
    assert result.latest_input_known_as_of == CUTOFF
    assert result.inputs_rejected_as_future == 0


def test_a_decision_that_consumed_nothing_says_so_rather_than_implying_a_time():
    """No data before the cutoff is a real state, and distinguishable from
    data that was filtered out — both differ from never having looked."""
    result = build_leakage_proof(CUTOFF, ["2026-04-01T00:00:00Z"])
    assert result.latest_input_known_as_of is None
    assert result.inputs_considered == 1
    assert result.inputs_rejected_as_future == 1


def test_the_proof_is_revalidated_when_the_decision_is():
    """A decision reaching a report without its proof holding is the failure
    this module exists to prevent, so the check is not only at the door.

    LeakageProof validates on demand rather than in __post_init__, so one
    carrying a future timestamp can exist; what must not happen is a Decision
    validating around it.
    """
    leaked = LeakageProof(CUTOFF, "2027-01-01T00:00:00Z", 1, 0)
    with pytest.raises(ValueError, match="leakage"):
        decision(proof=leaked).validate()


# --- leakage: a resolution the decision could have read ----------------------

def test_a_resolution_known_before_the_decision_is_not_scored():
    report = score_decisions(
        [decision()],
        [Outcome("m1", "2026-03-01T09:00:00Z", True)],
    )
    assert report.scored == 0
    assert report.unscored_resolution_not_after_decision == 1
    assert report.brier is None


def test_a_resolution_known_at_the_same_instant_is_not_scored():
    """Equal timestamps cannot be ordered from the data, and ordering them
    favourably is exactly the error this check exists for."""
    report = score_decisions([decision()], [Outcome("m1", CUTOFF, True)])
    assert report.unscored_resolution_not_after_decision == 1
    assert report.scored == 0


def test_a_resolution_known_strictly_after_is_scored():
    report = score_decisions(
        [decision()],
        [Outcome("m1", "2026-03-01T12:00:01Z", True)],
    )
    assert report.scored == 1
    assert report.brier == pytest.approx((0.70 - 1.0) ** 2)


# --- leakage: scoring the split the system was fitted on ----------------------

@pytest.mark.parametrize("split", [SPLIT_TRAIN, SPLIT_CALIBRATION])
def test_train_and_calibration_decisions_are_not_scored_by_default(split):
    """Scoring them reports how well the system fitted what it was given."""
    report = score_decisions(
        [decision(split=split)],
        [Outcome("m1", "2026-03-02T00:00:00Z", True)],
    )
    assert report.scored == 0
    assert report.decisions_by_split[split] == 1


def test_the_splits_cannot_overlap():
    with pytest.raises(ValueError, match="strictly after"):
        Window("2026-01-01T00:00:00Z", "2026-02-01T00:00:00Z",
               "2026-02-01T00:00:00Z", "2026-03-01T00:00:00Z").validate()


def test_a_timestamp_belongs_to_exactly_one_split():
    window = Window("2026-01-01T00:00:00Z", "2026-02-01T00:00:00Z",
                    "2026-02-15T00:00:00Z", "2026-03-01T00:00:00Z")
    assert window.split_for("2025-12-31T23:59:59Z") is None
    assert window.split_for("2026-01-01T00:00:00Z") == SPLIT_TRAIN
    assert window.split_for("2026-01-31T23:59:59Z") == SPLIT_TRAIN
    #: The boundary instant belongs to the later split, not to both.
    assert window.split_for("2026-02-01T00:00:00Z") == SPLIT_CALIBRATION
    assert window.split_for("2026-02-15T00:00:00Z") == SPLIT_TEST
    assert window.split_for("2026-03-01T00:00:00Z") is None


# --- survivorship ------------------------------------------------------------

def test_markets_without_a_resolution_are_counted_not_dropped():
    """A run that scores 40 of 100 and reports only the 40 is reporting a hit
    rate for a population nobody chose."""
    decisions = [decision(market_id=f"m{i}") for i in range(5)]
    report = score_decisions(
        decisions,
        [Outcome("m0", "2026-03-02T00:00:00Z", True),
         Outcome("m1", "2026-03-02T00:00:00Z", False)],
        universe_size=5,
    )
    assert report.scored == 2
    assert report.unscored_no_resolution == 3
    assert report.markets_in_universe == 5
    assert report.markets_scored == 2


def test_a_duplicate_resolution_is_refused():
    """Two resolutions for one market means one of them is wrong, and picking
    either silently is how a backtest gets the answer it wanted."""
    with pytest.raises(ValueError, match="duplicate resolution"):
        score_decisions(
            [decision()],
            [Outcome("m1", "2026-03-02T00:00:00Z", True),
             Outcome("m1", "2026-03-02T00:00:00Z", False)],
        )


# --- windows -----------------------------------------------------------------

def test_rolling_windows_step_and_keep_a_fixed_training_length():
    windows = walk_forward(
        "2026-01-01T00:00:00Z", "2026-06-01T00:00:00Z",
        train_days=60, calibration_days=15, test_days=15, step_days=30,
    )
    assert len(windows) >= 2
    assert windows[0].train_start == "2026-01-01T00:00:00Z"
    assert windows[1].train_start == "2026-01-31T00:00:00Z"
    for window in windows:
        window.validate()


def test_expanding_windows_pin_the_start_so_training_grows():
    windows = walk_forward(
        "2026-01-01T00:00:00Z", "2026-06-01T00:00:00Z",
        train_days=60, calibration_days=15, test_days=15, step_days=30,
        expanding=True,
    )
    assert {w.train_start for w in windows} == {"2026-01-01T00:00:00Z"}
    assert windows[-1].test_end > windows[0].test_end


def test_a_period_too_short_for_one_whole_window_yields_none():
    """A partial final window is the quiet way to report a shorter test period
    and compare the results as though they were alike."""
    windows = walk_forward(
        "2026-01-01T00:00:00Z", "2026-02-01T00:00:00Z",
        train_days=60, calibration_days=15, test_days=15, step_days=30,
    )
    assert windows == ()


@pytest.mark.parametrize("bad", [
    dict(train_days=0), dict(calibration_days=-1), dict(test_days=0), dict(step_days=0),
])
def test_window_lengths_must_be_positive(bad):
    params = dict(train_days=30, calibration_days=10, test_days=10, step_days=10)
    params.update(bad)
    with pytest.raises(ValueError, match="must be positive"):
        walk_forward("2026-01-01T00:00:00Z", "2026-06-01T00:00:00Z", **params)


def test_the_report_serialises():
    payload = score_decisions(
        [decision()], [Outcome("m1", "2026-03-02T00:00:00Z", True)]
    ).as_dict()
    assert payload["schema_version"] == BACKTEST_SCHEMA_VERSION
    assert payload["brier"] == pytest.approx(0.09)
    assert "unscored_resolution_not_after_decision" in payload


def test_the_module_has_no_route_to_execution_or_a_live_clock():
    """A backtest that reads the wall clock is not replaying anything, and one
    that can reach a venue is not a backtest."""
    import pathlib
    source = (pathlib.Path(__file__).resolve().parents[2]
              / "03_IMPLEMENTATION" / "packages" / "polymarket"
              / "backtest.py").read_text(encoding="utf-8")
    for forbidden in ("requests", "httpx", "urllib", "socket",
                      "datetime.now", "utcnow", "time.time", "random"):
        assert forbidden not in source, f"{forbidden} has no business in a backtest"
