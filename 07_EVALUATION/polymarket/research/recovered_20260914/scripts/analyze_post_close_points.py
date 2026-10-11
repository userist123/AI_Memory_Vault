import json
from datetime import datetime, timezone

with open("C:/Users/Marius/Documents/Codex/AI_Memory_Vault_CODEX_READY/07_EVALUATION/polymarket/fixtures/historical_dataset_50markets.json", "r", encoding="utf-8") as f:
    dataset = json.load(f)

def parse_iso(ts):
    return datetime.fromisoformat(ts.replace("Z", "+00:00")).astimezone(timezone.utc)

post_close_points = []
first_pt_is_post_close = []

for m in dataset["markets"]:
    mid = m["market_id"]
    res_iso = m["resolution_known_at"]
    res_dt = parse_iso(res_iso)
    
    # check first point for outcome 0
    pts_o0 = [p for p in m["price_history"] if p["outcome_id"] == m["outcome_ids"][0]]
    pts_o0.sort(key=lambda x: x["observed_at"])
    if pts_o0:
        first_p0 = pts_o0[0]
        if parse_iso(first_p0["observed_at"]) > res_dt:
            first_pt_is_post_close.append((mid, first_p0))
            
    for p in m["price_history"]:
        p_dt = parse_iso(p["observed_at"])
        if p_dt > res_dt:
            post_close_points.append({
                "market_id": mid,
                "question": m["question"],
                "outcome_id": p["outcome_id"],
                "observed_at": p["observed_at"],
                "closedTime": res_iso,
                "seconds_after_close": (p_dt - res_dt).total_seconds(),
                "price": p["price"],
                "is_winner": p["outcome_id"] in m["resolution_outcome_ids"]
            })

print(f"Total post-close points: {len(post_close_points)} / {dataset['total_price_points']}")
print(f"Markets where FIRST point was post-close: {len(first_pt_is_post_close)}")

print("\n--- Price distribution of the 22 post-close points ---")
prices = [p["price"] for p in post_close_points]
for pt in post_close_points:
    print(f"Market {pt['market_id']} | +{pt['seconds_after_close']:.0f}s after close | price={pt['price']} | is_winner={pt['is_winner']} | Q: {pt['question'][:45]}")

with open("C:/Users/Marius/.gemini/antigravity/brain/aebf6032-0fa2-438b-bb11-3eda139a64e3/scratch/post_close_breakdown.json", "w", encoding="utf-8") as f:
    json.dump(post_close_points, f, indent=2)
