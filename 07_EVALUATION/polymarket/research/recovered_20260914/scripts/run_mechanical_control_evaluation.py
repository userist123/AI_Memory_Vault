import json
import math
import sys
sys.path.insert(0, "C:/Users/Marius/Documents/Codex/AI_Memory_Vault_CODEX_READY/03_IMPLEMENTATION/packages")

from polymarket.historical_paper_replay import HistoricalMarketBundle, HistoricalTapePoint, run_mechanical_control

# Load dataset
dataset_path = "C:/Users/Marius/Documents/Codex/AI_Memory_Vault_CODEX_READY/07_EVALUATION/polymarket/fixtures/historical_dataset_50markets.json"
with open(dataset_path, "r", encoding="utf-8") as f:
    data = json.load(f)

results = []
errors = []

for m in data["markets"]:
    pts = [
        HistoricalTapePoint(
            market_id=p["market_id"],
            outcome_id=p["outcome_id"],
            observed_at=p["observed_at"],
            price=p["price"],
            source_ref=p["source_ref"],
            acquired_at=p["acquired_at"],
            known_as_of=p["known_as_of"],
        )
        for p in m["price_history"]
    ]
    bundle = HistoricalMarketBundle(
        market_id=m["market_id"],
        question=m["question"],
        outcome_ids=tuple(m["outcome_ids"]),
        outcomes=tuple(m["outcomes"]),
        resolution_outcome_ids=tuple(m["resolution_outcome_ids"]),
        resolution_known_at=m["resolution_known_at"],
        price_history=tuple(pts),
        metadata_source_ref=m["metadata_source_ref"],
    )
    try:
        res = run_mechanical_control(bundle, notional=1.0)
        results.append({
            "market_id": res.market_id,
            "question": res.question,
            "bought_outcome_id": res.bought_outcome_id,
            "entry_at": res.entry_at,
            "entry_price": res.entry_price,
            "notional": res.notional,
            "settlement_value": res.settlement_value,
            "pnl": res.pnl,
            "won": res.bought_outcome_id in bundle.resolution_outcome_ids,
            "resolution_known_at": res.resolution_known_at
        })
    except Exception as e:
        errors.append((m["market_id"], str(e)))

print(f"Executed mechanical control on {len(results)} markets. Errors: {len(errors)}")

# Compute statistics
pnls = [r["pnl"] for r in results]
wins = [r for r in results if r["won"]]
losses = [r for r in results if not r["won"]]

pnls_sorted = sorted(pnls)
n = len(pnls)
median_pnl = (pnls_sorted[n//2 - 1] + pnls_sorted[n//2]) / 2 if n % 2 == 0 else pnls_sorted[n//2]
mean_pnl = sum(pnls) / n
total_pnl = sum(pnls)
win_rate = len(wins) / n

# Percentiles
def percentile(vals, p):
    k = (len(vals) - 1) * p
    f = math.floor(k)
    c = math.ceil(k)
    if f == c:
        return vals[int(k)]
    d0 = vals[int(f)] * (c - k)
    d1 = vals[int(c)] * (k - f)
    return d0 + d1

p10 = percentile(pnls_sorted, 0.10)
p25 = percentile(pnls_sorted, 0.25)
p50 = median_pnl
p75 = percentile(pnls_sorted, 0.75)
p90 = percentile(pnls_sorted, 0.90)

print("\n=== Mechanical Control Results ===")
print(f"Total Markets: {n}")
print(f"Wins: {len(wins)} ({win_rate:.1%})")
print(f"Losses: {len(losses)} ({len(losses)/n:.1%})")
print(f"Total PnL ($1 notional / trade): {total_pnl:+.4f}")
print(f"Mean PnL: {mean_pnl:+.4f}")
print(f"Median PnL: {median_pnl:+.4f}")
print(f"Min PnL: {min(pnls):+.4f}")
print(f"Max PnL: {max(pnls):+.4f}")
print(f"Percentiles: p10={p10:+.4f}, p25={p25:+.4f}, p50={p50:+.4f}, p75={p75:+.4f}, p90={p90:+.4f}")

# Entry price distribution
entry_prices = [r["entry_price"] for r in results]
print(f"\nEntry price distribution: min={min(entry_prices):.4f}, max={max(entry_prices):.4f}, mean={sum(entry_prices)/len(entry_prices):.4f}")

# Save detailed results to scratch
scratch_path = "C:/Users/Marius/.gemini/antigravity/brain/aebf6032-0fa2-438b-bb11-3eda139a64e3/scratch"
with open(f"{scratch_path}/mechanical_control_summary.json", "w", encoding="utf-8") as f:
    json.dump({
        "total_markets": n,
        "wins": len(wins),
        "losses": len(losses),
        "win_rate": win_rate,
        "total_pnl": total_pnl,
        "mean_pnl": mean_pnl,
        "median_pnl": median_pnl,
        "min_pnl": min(pnls),
        "max_pnl": max(pnls),
        "p10": p10,
        "p25": p25,
        "p50": p50,
        "p75": p75,
        "p90": p90,
        "entry_prices": {
            "min": min(entry_prices),
            "max": max(entry_prices),
            "mean": sum(entry_prices)/len(entry_prices)
        },
        "trades": results
    }, f, indent=2)

print("\nSaved mechanical control summary to scratch/mechanical_control_summary.json")
