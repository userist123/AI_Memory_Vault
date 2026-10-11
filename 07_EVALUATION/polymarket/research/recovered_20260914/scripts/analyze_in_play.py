import json
from datetime import datetime, timezone

with open("C:/Users/Marius/Documents/Codex/AI_Memory_Vault_CODEX_READY/07_EVALUATION/polymarket/fixtures/historical_dataset_50markets.json", "r", encoding="utf-8") as f:
    dataset = json.load(f)

with open("C:/Users/Marius/.gemini/antigravity/brain/aebf6032-0fa2-438b-bb11-3eda139a64e3/scratch/sampled_500_markets.json", "r", encoding="utf-8") as f:
    sampled = json.load(f)
raw_by_id = {str(m["id"]): m for m in sampled}

def parse_iso(ts_str):
    if not ts_str:
        return None
    ts = str(ts_str).strip().replace(" ", "T")
    if ts.endswith("+00"):
        ts = ts[:-3] + "Z"
    elif ts.endswith("+00:00"):
        ts = ts[:-6] + "Z"
    elif not ts.endswith("Z") and "+" not in ts:
        ts = ts + "Z"
    return datetime.fromisoformat(ts.replace("Z", "+00:00")).astimezone(timezone.utc)

in_play_count = 0
results = []

for m in dataset["markets"]:
    mid = m["market_id"]
    raw = raw_by_id[mid]
    
    created = parse_iso(raw.get("createdAt"))
    game_start = parse_iso(raw.get("gameStartTime"))
    closed = parse_iso(raw.get("closedTime"))
    end_d = parse_iso(raw.get("endDate"))
    
    # Entry point
    pts_o0 = [p for p in m["price_history"] if p["outcome_id"] == m["outcome_ids"][0]]
    pts_o0.sort(key=lambda x: x["observed_at"])
    entry_pt = pts_o0[0]
    entry_time = parse_iso(entry_pt["observed_at"])
    
    is_in_play = (game_start is not None and created is not None and created > game_start)
    if is_in_play:
        in_play_count += 1
        
    game_duration = (closed - game_start).total_seconds() if (closed and game_start) else None
    market_duration = (closed - created).total_seconds() if (closed and created) else None
    time_into_game = (entry_time - game_start).total_seconds() if (entry_time and game_start) else None
    game_progress_pct = (time_into_game / game_duration) if (time_into_game and game_duration and game_duration > 0) else None
    
    results.append({
        "id": mid,
        "question": m["question"],
        "is_in_play": is_in_play,
        "game_start": game_start.isoformat() if game_start else None,
        "created": created.isoformat() if created else None,
        "entry": entry_time.isoformat(),
        "closed": closed.isoformat() if closed else None,
        "game_duration_min": game_duration / 60 if game_duration else None,
        "market_duration_min": market_duration / 60 if market_duration else None,
        "game_progress_pct_at_entry": game_progress_pct,
        "entry_price": entry_pt["price"],
        "won": m["outcome_ids"][0] in m["resolution_outcome_ids"]
    })

print(f"Total markets in dataset: {len(results)}")
print(f"Markets created AFTER gameStartTime (In-Play / Live): {in_play_count} / {len(results)} ({in_play_count/len(results):.1%})")

progress_pcts = [r["game_progress_pct_at_entry"] for r in results if r["game_progress_pct_at_entry"] is not None]
if progress_pcts:
    print(f"\nGame Progress at entry: min={min(progress_pcts):.1%}, max={max(progress_pcts):.1%}, mean={sum(progress_pcts)/len(progress_pcts):.1%}")

with open("C:/Users/Marius/.gemini/antigravity/brain/aebf6032-0fa2-438b-bb11-3eda139a64e3/scratch/in_play_breakdown.json", "w", encoding="utf-8") as f:
    json.dump(results, f, indent=2)
