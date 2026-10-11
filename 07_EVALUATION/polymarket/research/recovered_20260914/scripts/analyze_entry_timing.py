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

details = []
for m in dataset["markets"]:
    mid = m["market_id"]
    raw = raw_by_id[mid]
    
    created_at = parse_iso(raw.get("createdAt"))
    start_date = parse_iso(raw.get("startDate"))
    game_start_time = parse_iso(raw.get("gameStartTime"))
    closed_time = parse_iso(raw.get("closedTime"))
    end_date = parse_iso(raw.get("endDate"))
    
    pts_o0 = [p for p in m["price_history"] if p["outcome_id"] == m["outcome_ids"][0]]
    pts_o0.sort(key=lambda x: x["observed_at"])
    first_pt = pts_o0[0]
    entry_time = parse_iso(first_pt["observed_at"])
    
    all_pts = sorted(m["price_history"], key=lambda x: x["observed_at"])
    earliest_time = parse_iso(all_pts[0]["observed_at"])
    
    t_start = start_date or created_at
    t_end = closed_time or end_date
    
    total_sec = (t_end - t_start).total_seconds() if (t_start and t_end) else None
    elapsed_to_entry = (entry_time - t_start).total_seconds() if (t_start and entry_time) else None
    remaining_to_close = (t_end - entry_time).total_seconds() if (t_end and entry_time) else None
    
    ratio = (elapsed_to_entry / total_sec) if (total_sec and total_sec > 0 and elapsed_to_entry is not None) else None
    
    details.append({
        "id": mid,
        "question": m["question"],
        "createdAt": created_at.isoformat() if created_at else None,
        "startDate": start_date.isoformat() if start_date else None,
        "gameStartTime": game_start_time.isoformat() if game_start_time else None,
        "closedTime": closed_time.isoformat() if closed_time else None,
        "entry_time": entry_time.isoformat(),
        "earliest_time": earliest_time.isoformat(),
        "entry_price": first_pt["price"],
        "total_duration_sec": total_sec,
        "elapsed_to_entry_sec": elapsed_to_entry,
        "remaining_to_close_sec": remaining_to_close,
        "ratio_in_life": ratio,
        "won": m["outcome_ids"][0] in m["resolution_outcome_ids"]
    })

ratios = [d["ratio_in_life"] for d in details if d["ratio_in_life"] is not None]
print(f"Total evaluated: {len(details)}")
print(f"Ratio stats: min={min(ratios):.4f}, max={max(ratios):.4f}, mean={sum(ratios)/len(ratios):.4f}")
b_early = len([r for r in ratios if r <= 0.333])
b_mid = len([r for r in ratios if 0.333 < r <= 0.666])
b_late = len([r for r in ratios if r > 0.666])
print(f"First third (<= 33%): {b_early} ({b_early/len(ratios):.1%})")
print(f"Middle third (33% - 66%): {b_mid} ({b_mid/len(ratios):.1%})")
print(f"Last third (> 66%): {b_late} ({b_late/len(ratios):.1%})")

print("\nFirst 10 markets detailed timing:")
for d in details[:10]:
    r_str = f"{d['ratio_in_life']:.2f}" if d['ratio_in_life'] is not None else "N/A"
    print(f"ID {d['id']} | ratio={r_str} | start={d['startDate']} | entry={d['entry_time']} | close={d['closedTime']} | rem={d['remaining_to_close_sec']:.0f}s | p={d['entry_price']} | won={d['won']} | Q: {d['question'][:45]}")

with open("C:/Users/Marius/.gemini/antigravity/brain/aebf6032-0fa2-438b-bb11-3eda139a64e3/scratch/entry_timing_details.json", "w", encoding="utf-8") as f:
    json.dump(details, f, indent=2)
