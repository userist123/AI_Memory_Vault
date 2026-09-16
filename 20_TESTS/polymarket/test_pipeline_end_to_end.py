"""The whole chain, once, with one market.

Every module in this package is tested alone, and no test before this one
touched more than three of them. Nine phases were written against fixtures that
each phase chose for itself, which is exactly the arrangement where every
component passes and the contracts between them do not fit.

This walks one market from a Gamma payload to a Brier score:

    Gamma payload
      -> ingest_payload          snapshot in an immutable store
      -> HistoricalReplay        what was knowable at a cutoff
      -> build_prediction        a record with provenance
      -> select_latest_eligible  the market price at that cutoff
      -> evaluate_prediction     BET or ABSTAIN
      -> size_position           a notional size, or a refusal
      -> Decision + LeakageProof the backtest's unit
      -> ResolutionRecord        the outcome, and when it became knowable
      -> score_decisions         a Brier score, or a counted refusal to score

The assertions that matter are the two at the seams: a decision made before the
resolution scores, and the same decision made after it does not.
"""
from __future__ import annotations

import json

import pytest

from packages.polymarket.backtest import (
    BACKTEST_SCHEMA_VERSION,
    SPLIT_TEST,
    Decision,
    Outcome,
    build_leakage_proof,
    score_decisions,
)
from packages.polymarket.gamma_ingest import ingest_payload
from packages.polymarket.historical_replay import HistoricalPricePoint, HistoricalReplay
from packages.polymarket.market_model_edge import select_latest_eligible_price
from packages.polymarket.market_snapshot import SnapshotStore
from packages.polymarket.prediction_ledger import (
    PredictionProvenance,
    build_prediction,
)
from packages.polymarket.resolutions import (
    RESOLUTION_SCHEMA_VERSION,
    SOURCE_ONCHAIN_SETTLEMENT,
    STATUS_RESOLVED,
    ResolutionRecord,
)
from packages.polymarket.risk_abstention import DECISION_BET, evaluate_prediction
from packages.polymarket.risk_sizing import (
    SIZE_APPROVED,
    MarketConditions,
    PortfolioState,
    size_position,
)

MARKET_ID = "512345"
OUTCOME_ID = "tok-yes"

INGESTED_AT = "2026-03-01T09:00:00Z"
CUTOFF = "2026-03-01T12:00:00Z"
RESOLVED_AT = "2026-03-20T14:32:11Z"


GAMMA_PAYLOAD = {
    "id": MARKET_ID,
    "conditionId": "0xabc",
    "question": "Will the rate be cut in March?",
    "description": "Resolves per the published decision.",
    "category": "Economics",
    "marketType": "normal",
    "outcomes": json.dumps(["Yes", "No"]),
    "outcomePrices": json.dumps(["0.55", "0.45"]),
    "clobTokenIds": json.dumps([OUTCOME_ID, "tok-no"]),
    "active": True,
    "closed": False,
    "createdAt": "2026-01-05T10:00:00Z",
    "startDate": "2026-01-05T10:00:00Z",
    "endDate": "2026-03-20T00:00:00Z",
    "resolutionSource": "Federal Reserve",
    "liquidity": "84000",
    "volume": "120000",
    "slug": "rate-cut-march",
}


def price_point(observed_at: str, price: str) -> HistoricalPricePoint:
    return HistoricalPricePoint(
        outcome_id=OUTCOME_ID,
        observed_at=observed_at,
        price=price,
        requested_fidelity_minutes=None,
        source_type="gamma-api",
        source_ref=MARKET_ID,
        acquired_at=observed_at,
        known_as_of=observed_at,
    )


@pytest.fixture
def snapshot(tmp_path):
    """Stage one: a real-shaped payload becomes an immutable snapshot."""
    store = SnapshotStore(tmp_path / "snapshots")
    result = ingest_payload([GAMMA_PAYLOAD], acquired_at=INGESTED_AT, store=store)
    assert result.written == 1, "the chain starts with a snapshot or not at all"
    return json.loads(next(store.root.glob("*.json")).read_text(encoding="utf-8"))


def run_pipeline(*, cutoff: str, model_probability: float, snapshot_id: str):
    """Stages two through seven. Returns the backtest Decision."""
    points = [
        price_point("2026-03-01T08:00:00Z", "0.50"),
        price_point("2026-03-01T11:00:00Z", "0.55"),
        #: After the cutoff. Everything downstream must behave as if it does
        #: not exist, and the leakage proof must say it was seen and rejected.
        price_point("2026-03-01T18:00:00Z", "0.80"),
    ]

    replayed = HistoricalReplay().replay(
        market_id=MARKET_ID, as_of=cutoff, snapshots=[], price_points=points
    )
    visible = replayed.price_points

    prediction = build_prediction(
        market_id=MARKET_ID,
        outcome_id=OUTCOME_ID,
        probability=model_probability,
        predicted_at=cutoff,
        known_as_of=cutoff,
        provenance=PredictionProvenance(
            evidence_bundle_hash="ev-e2e",
            snapshot_ids=(snapshot_id,),
            source_refs=(f"snapshot:{MARKET_ID}",),
            source_types=("snapshot",),
            model_id="pipeline-test",
            model_version="1",
        ),
    )

    price = select_latest_eligible_price(
        prediction=prediction, price_points=visible
    )
    gate = evaluate_prediction(prediction, market_price=float(price.price))

    sizing = size_position(
        probability=prediction.probability,
        conditions=MarketConditions(
            price=float(price.price), available_depth=100_000.0,
            quote_age_seconds=60.0, resolution_risk=0.05,
            net_edge=prediction.probability - float(price.price),
        ),
        portfolio=PortfolioState(bankroll=100_000.0, calibration_samples=500),
    )

    proof = build_leakage_proof(cutoff, [p.known_as_of for p in points])

    return Decision(
        BACKTEST_SCHEMA_VERSION, MARKET_ID, cutoff, SPLIT_TEST,
        gate.decision, gate.reason, prediction.probability,
        float(price.price), gate.edge,
        sizing.size if sizing.outcome == SIZE_APPROVED else 0.0,
        proof,
    ), gate, sizing, visible


def resolution() -> ResolutionRecord:
    return ResolutionRecord(
        schema_version=RESOLUTION_SCHEMA_VERSION,
        market_id=MARKET_ID,
        status=STATUS_RESOLVED,
        winning_outcome_ids=(OUTCOME_ID,),
        known_at=RESOLVED_AT,
        source=SOURCE_ONCHAIN_SETTLEMENT,
        source_ref="0xdeadbeef",
    )


# --- the chain holds together ------------------------------------------------

def test_a_market_walks_from_gamma_payload_to_a_brier_score(snapshot):
    decision, gate, sizing, _ = run_pipeline(
        cutoff=CUTOFF, model_probability=0.75, snapshot_id=snapshot["snapshot_id"]
    )
    decision.validate()

    assert gate.decision == DECISION_BET
    assert sizing.outcome == SIZE_APPROVED
    assert decision.size > 0

    outcome = resolution()
    report = score_decisions(
        [decision],
        [Outcome(outcome.market_id, outcome.known_at,
                 OUTCOME_ID in outcome.winning_outcome_ids)],
    )
    assert report.scored == 1
    #: The market resolved YES and the model said 0.75, so the loss is 0.0625.
    assert report.brier == pytest.approx((0.75 - 1.0) ** 2)


def test_both_cutoff_filters_run_and_neither_is_load_bearing_alone(snapshot):
    """Two layers filter by cutoff, and that is deliberate rather than
    redundant — but it means a bug in either is masked by the other.

    `select_latest_eligible_price` was checked directly: given all three
    points, including the one three hours past the cutoff, it still returns
    0.55. So asserting the final price alone would prove the selector works and
    say nothing about whether the replay stage filtered.

    Both are therefore asserted separately: `visible` is the replay's output,
    `decision.market_price` is the selector's.
    """
    decision, gate, _, visible = run_pipeline(
        cutoff=CUTOFF, model_probability=0.75, snapshot_id=snapshot["snapshot_id"]
    )
    assert [p.price for p in visible] == ["0.50", "0.55"]
    assert decision.market_price == 0.55
    assert gate.edge == pytest.approx(0.20)

    assert decision.proof.inputs_considered == 3
    assert decision.proof.inputs_rejected_as_future == 1
    assert decision.proof.latest_input_known_as_of == "2026-03-01T11:00:00Z"


def test_the_same_decision_made_after_the_resolution_is_not_scored(snapshot):
    """The one seam that matters most. Everything else could be right and this
    still wrong, and the run would report a Brier score built from hindsight.
    """
    late = "2026-03-25T00:00:00Z"
    decision, _, _, _ = run_pipeline(
        cutoff=late, model_probability=0.75, snapshot_id=snapshot["snapshot_id"]
    )
    outcome = resolution()
    report = score_decisions(
        [decision],
        [Outcome(outcome.market_id, outcome.known_at, True)],
    )
    assert report.scored == 0
    assert report.unscored_resolution_not_after_decision == 1
    assert report.brier is None


def test_a_later_cutoff_sees_more_of_the_history(snapshot):
    """Replay is a function of the cutoff, and the chain must carry that
    through rather than caching the first answer."""
    _, early_gate, _, early = run_pipeline(
        cutoff="2026-03-01T09:30:00Z", model_probability=0.75,
        snapshot_id=snapshot["snapshot_id"],
    )
    _, late_gate, _, later = run_pipeline(
        cutoff=CUTOFF, model_probability=0.75, snapshot_id=snapshot["snapshot_id"]
    )
    assert len(early) == 1 and len(later) == 2
    assert early_gate.edge == pytest.approx(0.25)
    assert late_gate.edge == pytest.approx(0.20)


# --- the chain refuses in the right places -----------------------------------

def test_a_thin_edge_abstains_and_carries_no_size(snapshot):
    decision, gate, sizing, _ = run_pipeline(
        cutoff=CUTOFF, model_probability=0.56, snapshot_id=snapshot["snapshot_id"]
    )
    assert gate.decision != DECISION_BET
    assert decision.size == 0.0
    assert sizing.outcome != SIZE_APPROVED


def test_provenance_survives_the_whole_chain(snapshot):
    """A prediction that cannot name the snapshot it read is not traceable, and
    the brief's provenance chain is the reason every stage carries ids."""
    prediction = build_prediction(
        market_id=MARKET_ID, outcome_id=OUTCOME_ID, probability=0.75,
        predicted_at=CUTOFF, known_as_of=CUTOFF,
        provenance=PredictionProvenance(
            evidence_bundle_hash="ev-e2e",
            snapshot_ids=(snapshot["snapshot_id"],),
            source_refs=(f"snapshot:{MARKET_ID}",),
            source_types=("snapshot",),
            model_id="pipeline-test", model_version="1",
        ),
    )
    assert prediction.provenance.snapshot_ids == (snapshot["snapshot_id"],)
    assert snapshot["snapshot_id"], "the store assigned a content-addressed id"


def test_the_ingested_snapshot_carries_no_resolution(snapshot):
    """The seam between ingestion and scoring: a live listing must not supply
    the outcome, or the backtest scores against something it fetched."""
    assert snapshot.get("resolution") is None
    assert snapshot["known_as_of"] == INGESTED_AT


def test_the_replay_stage_filters_on_its_own():
    """The independent half of the defence above.

    Given the future point, replay must drop it without help from anything
    downstream — because a caller that uses replay and not the selector, or the
    reverse, must still be blind.
    """
    points = [
        price_point("2026-03-01T08:00:00Z", "0.50"),
        price_point("2026-03-01T11:00:00Z", "0.55"),
        price_point("2026-03-01T18:00:00Z", "0.80"),
    ]
    replayed = HistoricalReplay().replay(
        market_id=MARKET_ID, as_of=CUTOFF, snapshots=[], price_points=points
    )
    assert [p.price for p in replayed.price_points] == ["0.50", "0.55"]


def test_the_selector_filters_on_its_own():
    """And the other half. Measured, not assumed: this is what made the
    integration assertion above ambiguous until it was split."""
    prediction = build_prediction(
        market_id=MARKET_ID, outcome_id=OUTCOME_ID, probability=0.75,
        predicted_at=CUTOFF, known_as_of=CUTOFF,
        provenance=PredictionProvenance(
            evidence_bundle_hash="ev", snapshot_ids=("s1",),
            source_refs=("r",), source_types=("snapshot",),
            model_id="m", model_version="1",
        ),
    )
    unfiltered = [
        price_point("2026-03-01T08:00:00Z", "0.50"),
        price_point("2026-03-01T11:00:00Z", "0.55"),
        price_point("2026-03-01T18:00:00Z", "0.80"),
    ]
    chosen = select_latest_eligible_price(
        prediction=prediction, price_points=unfiltered
    )
    assert chosen.price == "0.55"
