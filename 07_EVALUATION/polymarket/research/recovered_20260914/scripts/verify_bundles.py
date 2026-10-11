import json
import sys
sys.path.insert(0, "C:/Users/Marius/Documents/Codex/AI_Memory_Vault_CODEX_READY/03_IMPLEMENTATION/packages")

from polymarket.historical_paper_replay import HistoricalMarketBundle, HistoricalTapePoint

with open("C:/Users/Marius/Documents/Codex/AI_Memory_Vault_CODEX_READY/07_EVALUATION/polymarket/fixtures/historical_dataset_50markets.json", "r", encoding="utf-8") as f:
    data = json.load(f)

bundles = []
errors = []

for m in data["markets"]:
    try:
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
        bundle.validate()
        bundles.append(bundle)
    except Exception as e:
        errors.append((m["market_id"], str(e)))

print(f"Validated bundles: {len(bundles)} / {len(data['markets'])}")
if errors:
    print(f"Errors ({len(errors)}):")
    for mid, err in errors[:5]:
        print(f"  {mid}: {err}")
else:
    print("ALL 50 BUNDLES VALIDATED CLEANLY!")
