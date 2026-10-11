import json
import sys
from datetime import datetime, timezone

sys.path.insert(0, "C:/Users/Marius/Documents/Codex/AI_Memory_Vault_CODEX_READY/03_IMPLEMENTATION/packages")

from polymarket.historical_paper_replay import HistoricalMarketBundle, HistoricalTapePoint, run_mechanical_control
from polymarket.phase12_temporal_contract import TemporalReplayCase, validate_temporal_replay_case
from polymarket.historical_replay import HistoricalReplay, HistoricalPricePoint
from polymarket.resolutions import ResolutionRecord, parse_resolution
from polymarket.backtest import score_decisions, build_leakage_proof, LeakageProof, Decision, Outcome, BACKTEST_SCHEMA_VERSION

# Load real dataset
with open("C:/Users/Marius/Documents/Codex/AI_Memory_Vault_CODEX_READY/07_EVALUATION/polymarket/fixtures/historical_dataset_50markets.json", "r", encoding="utf-8") as f:
    dataset = json.load(f)

first_market = dataset["markets"][0]
print(f"Testing on market {first_market['market_id']}: {first_market['question']}")

# CASE 1: Boundary condition at exact time T (semi-open vs closed)
print("\n--- CASE 1: Exact timestamp T boundary test ---")
cutoff_T = "2026-09-13T20:40:00Z"

# 1A: HistoricalReplay._available
hr_avail = HistoricalReplay._available(cutoff_T, cutoff_T, cutoff_T, datetime.fromisoformat(cutoff_T.replace("Z", "+00:00")))
print(f"1A. HistoricalReplay._available when observed_at == cutoff: {hr_avail}")

# 1B: LeakageProof validation
proof = build_leakage_proof(cutoff_T, [cutoff_T])
print(f"1B. build_leakage_proof when known_as_of == cutoff: rejected={proof.inputs_rejected_as_future}, latest={proof.latest_input_known_as_of}")

# 1C: phase12_temporal_contract with observation at cutoff
case_obs_equal = TemporalReplayCase(
    prediction_market_id="m1",
    bundle_market_id="m1",
    prediction_cutoff=cutoff_T,
    observation_at=cutoff_T,
    observation_known_as_of=cutoff_T,
    observation_acquired_at=cutoff_T,
    resolution_known_at="2026-09-13T20:41:00Z"
)
try:
    validate_temporal_replay_case(case_obs_equal)
    case_1c_res = "ACCEPTED (observed == cutoff is permitted)"
except Exception as e:
    case_1c_res = f"REJECTED: {e}"
print(f"1C. validate_temporal_replay_case (observation_at == cutoff): {case_1c_res}")

# 1D: phase12_temporal_contract with resolution at cutoff
case_res_equal = TemporalReplayCase(
    prediction_market_id="m1",
    bundle_market_id="m1",
    prediction_cutoff=cutoff_T,
    observation_at="2026-09-13T20:39:00Z",
    observation_known_as_of="2026-09-13T20:39:00Z",
    observation_acquired_at="2026-09-13T20:39:00Z",
    resolution_known_at=cutoff_T
)
try:
    validate_temporal_replay_case(case_res_equal)
    case_1d_res = "ACCEPTED"
except Exception as e:
    case_1d_res = f"REJECTED: {e}"
print(f"1D. validate_temporal_replay_case (resolution_known_at == cutoff): {case_1d_res}")

# 1E: backtest.score_decisions with resolution at cutoff
dec = Decision(
    schema_version=BACKTEST_SCHEMA_VERSION,
    market_id="m1",
    as_of=cutoff_T,
    split="test",
    decision="buy",
    reason="model",
    model_probability=0.8,
    market_price=0.5,
    edge=0.3,
    size=1.0,
    proof=proof
)
out_equal = Outcome(
    market_id="m1",
    known_at=cutoff_T,
    resolved_yes=True
)
rep_equal = score_decisions([dec], [out_equal])
print(f"1E. score_decisions when resolution.known_at == decision.as_of: scored={rep_equal.scored}, unscored_resolution_not_after_decision={rep_equal.unscored_resolution_not_after_decision}")


# CASE 2: Resolution or Price Point with known_as_of = None / null
print("\n--- CASE 2: known_as_of = None / null ---")
# 2A: parse_resolution with known_at = None
try:
    parse_resolution({
        "market_id": "m1",
        "status": "resolved",
        "winning_outcome_ids": ["tok1"],
        "known_at": None,
        "source": "manual_attested",
        "source_ref": "ref"
    })
    res_2a = "ACCEPTED"
except Exception as e:
    res_2a = f"REJECTED: {e}"
print(f"2A. parse_resolution with known_at=None: {res_2a}")

# 2B: HistoricalTapePoint in historical_paper_replay with known_as_of=None
try:
    pt_null = HistoricalTapePoint(
        market_id="m1",
        outcome_id="tok1",
        observed_at="2026-09-13T20:00:00Z",
        price=0.55,
        source_ref="ref",
        acquired_at=None,
        known_as_of=None
    )
    pt_null.validate()
    res_2b = "ACCEPTED (known_as_of=None passes validate())"
except Exception as e:
    res_2b = f"REJECTED: {e}"
print(f"2B. HistoricalTapePoint with known_as_of=None: {res_2b}")

# 2C: run_mechanical_control on bundle with known_as_of=None
try:
    bundle_null = HistoricalMarketBundle(
        market_id="m1",
        question="Q?",
        outcome_ids=("tok1", "tok2"),
        outcomes=("Yes", "No"),
        resolution_outcome_ids=("tok1",),
        resolution_known_at="2026-09-13T21:00:00Z",
        price_history=(pt_null,),
        metadata_source_ref="ref"
    )
    bundle_null.validate()
    ctrl_res = run_mechanical_control(bundle_null, notional=1.0)
    res_2c = f"EXECUTED CLEANLY! PnL={ctrl_res.pnl:+.4f}"
except Exception as e:
    res_2c = f"FAILED: {e}"
print(f"2C. run_mechanical_control with known_as_of=None: {res_2c}")


# CASE 3: Two markets on same event, one resolved before the other
print("\n--- CASE 3: Cross-market event correlation / leakage ---")
dec_b = Decision(
    schema_version=BACKTEST_SCHEMA_VERSION,
    market_id="market_b",
    as_of="2026-09-13T20:30:00Z",
    split="test",
    decision="buy",
    reason="model",
    model_probability=0.9,
    market_price=0.5,
    edge=0.4,
    size=1.0,
    proof=build_leakage_proof("2026-09-13T20:30:00Z", ["2026-09-13T20:00:00Z"])
)
out_a = Outcome(market_id="market_a", known_at="2026-09-13T20:00:00Z", resolved_yes=True)
out_b = Outcome(market_id="market_b", known_at="2026-09-13T21:00:00Z", resolved_yes=True)
rep_cross = score_decisions([dec_b], [out_a, out_b])
print(f"3. score_decisions cross-market test: scored={rep_cross.scored}, unscored={rep_cross.unscored_resolution_not_after_decision}")
print("   Check: Is there any event_id or cross-market correlation check in backtest.py? None. Decisions match strictly on market_id.")


# CASE 4: Tape point where observed_at > endDate of the market
print("\n--- CASE 4: Tape point where observed_at > endDate ---")
raw_m = dataset["markets"][0]
print(f"Market {raw_m['market_id']} has resolution_known_at = {raw_m['resolution_known_at']}")
pt_future = HistoricalTapePoint(
    market_id=raw_m["market_id"],
    outcome_id=raw_m["outcome_ids"][0],
    observed_at="2026-09-13T22:00:00Z", # after resolution_known_at!
    price=0.99,
    source_ref="ref",
    acquired_at=None,
    known_as_of=None
)
try:
    pt_future.validate()
    print("4A. pt_future.validate() alone: VALID")
except Exception as e:
    print(f"4A. pt_future.validate() failed: {e}")

try:
    bundle_future = HistoricalMarketBundle(
        market_id=raw_m["market_id"],
        question=raw_m["question"],
        outcome_ids=tuple(raw_m["outcome_ids"]),
        outcomes=tuple(raw_m["outcomes"]),
        resolution_outcome_ids=tuple(raw_m["resolution_outcome_ids"]),
        resolution_known_at="2026-09-13T20:46:54Z", # earlier than pt_future!
        price_history=(pt_future,),
        metadata_source_ref=raw_m["metadata_source_ref"]
    )
    bundle_future.validate()
    res_4b = "ACCEPTED (HistoricalMarketBundle does NOT check if observed_at <= resolution_known_at!)"
except Exception as e:
    res_4b = f"REJECTED: {e}"
print(f"4B. bundle_future.validate() with point observed_at > resolution_known_at: {res_4b}")

post_resolution_points = 0
for m in dataset["markets"]:
    res_dt = datetime.fromisoformat(m["resolution_known_at"].replace("Z", "+00:00"))
    for p in m["price_history"]:
        p_dt = datetime.fromisoformat(p["observed_at"].replace("Z", "+00:00"))
        if p_dt > res_dt:
            post_resolution_points += 1
print(f"4C. Real points in captured dataset with observed_at > resolution_known_at: {post_resolution_points} / {dataset['total_price_points']}")
