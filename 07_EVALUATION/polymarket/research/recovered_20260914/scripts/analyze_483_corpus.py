import json
import math
from datetime import datetime, timezone

scratch_path = "C:/Users/Marius/.gemini/antigravity/brain/aebf6032-0fa2-438b-bb11-3eda139a64e3/scratch"

with open(f"{scratch_path}/qualified_clob_markets_500.json", "r", encoding="utf-8") as f:
    raw_markets = json.load(f)

with open(f"{scratch_path}/batch_history_all_expanded.json", "r", encoding="utf-8") as f:
    history_by_token = json.load(f)

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
    try:
        return datetime.fromisoformat(ts.replace("Z", "+00:00")).astimezone(timezone.utc)
    except Exception:
        return None

def wilson_ci(k, n, confidence=0.95):
    if n == 0:
        return (0.0, 0.0)
    z = 1.95996  # 95%
    p = k / n
    denom = 1 + (z**2) / n
    centre = (p + (z**2) / (2 * n)) / denom
    spread = (z * math.sqrt((p * (1 - p) / n) + (z**2) / (4 * (n**2)))) / denom
    return (max(0.0, centre - spread), min(1.0, centre + spread))

records = []
for m in raw_markets:
    mid = str(m["id"])
    toks = json.loads(m["clobTokenIds"]) if isinstance(m["clobTokenIds"], str) else m["clobTokenIds"]
    outcomes = json.loads(m["outcomes"]) if isinstance(m["outcomes"], str) else m["outcomes"]
    prices = json.loads(m["outcomePrices"]) if isinstance(m["outcomePrices"], str) else m["outcomePrices"]
    
    parsed_prices = [float(p) for p in prices]
    win_idx = 0 if parsed_prices[0] >= 0.95 else 1
    won = (win_idx == 0)
    
    tok0 = toks[0]
    pts = history_by_token.get(tok0, [])
    # Filter valid price points
    valid_pts = [p for p in pts if "t" in p and "p" in p and 0.0 < float(p["p"]) < 1.0]
    if not valid_pts:
        continue
    valid_pts.sort(key=lambda p: float(p["t"]))
    first_pt = valid_pts[0]
    entry_price = float(first_pt["p"])
    entry_time = datetime.fromtimestamp(float(first_pt["t"]), tz=timezone.utc)
    
    created_at = parse_iso(m.get("createdAt"))
    start_date = parse_iso(m.get("startDate"))
    game_start = parse_iso(m.get("gameStartTime"))
    closed_time = parse_iso(m.get("closedTime"))
    end_date = parse_iso(m.get("endDate"))
    
    is_in_play = (game_start is not None and created_at is not None and created_at > game_start)
    
    t_start = start_date or created_at
    t_end = closed_time or end_date
    ratio = None
    if t_start and t_end and (t_end - t_start).total_seconds() > 0:
        ratio = (entry_time - t_start).total_seconds() / (t_end - t_start).total_seconds()
        
    # Event key for clustering/deduplication:
    # e.g., slug prefix or question prefix
    q = m.get("question", "")
    slug = m.get("slug", "")
    event_key = slug.split("-202")[0] if "-202" in slug else q[:30]
    
    records.append({
        "id": mid,
        "question": q,
        "slug": slug,
        "event_key": event_key,
        "entry_price": entry_price,
        "entry_time": entry_time.isoformat(),
        "won": won,
        "is_in_play": is_in_play,
        "ratio_in_market": ratio,
        "points_count": len(valid_pts),
        "created_at": created_at.isoformat() if created_at else None,
        "game_start": game_start.isoformat() if game_start else None,
        "closed_time": closed_time.isoformat() if closed_time else None,
    })

print(f"Total analyzed records with valid price history: {len(records)}")

def evaluate_calibration(subset, label=""):
    print(f"\n==========================================")
    print(f"CALIBRATION REPORT: {label} (N = {len(subset)})")
    print(f"==========================================")
    
    bins = [
        ("0.0 - 0.2", 0.0, 0.2),
        ("0.2 - 0.4", 0.2, 0.4),
        ("0.4 - 0.6", 0.4, 0.6),
        ("0.6 - 0.8", 0.6, 0.8),
        ("0.8 - 1.0", 0.8, 1.0),
    ]
    
    total_wins = sum(1 for r in subset if r["won"])
    total_pnl = sum((1.0 / r["entry_price"] - 1.0) if r["won"] else -1.0 for r in subset)
    mean_price = sum(r["entry_price"] for r in subset) / len(subset)
    print(f"Overall Win Rate: {total_wins} / {len(subset)} ({total_wins/len(subset):.1%}) | Mean Price: {mean_price:.4f} | Total PnL: {total_pnl:+.2f}")
    
    print(f"{'Bin':<12} | {'Count':<6} | {'Wins':<5} | {'Win Rate':<9} | {'Mean Price':<10} | {'Gap':<8} | {'Wilson 95% CI':<18}")
    print("-" * 80)
    
    for bin_name, low, high in bins:
        in_bin = [r for r in subset if (low <= r["entry_price"] < high if high < 1.0 else low <= r["entry_price"] <= high)]
        n = len(in_bin)
        if n == 0:
            print(f"{bin_name:<12} | {0:<6} | {0:<5} | {'N/A':<9} | {'N/A':<10} | {'N/A':<8} | {'N/A':<18}")
            continue
        k = sum(1 for r in in_bin if r["won"])
        rate = k / n
        avg_p = sum(r["entry_price"] for r in in_bin) / n
        gap = rate - avg_p
        ci_low, ci_high = wilson_ci(k, n)
        print(f"{bin_name:<12} | {n:<6} | {k:<5} | {rate:<9.1%} | {avg_p:<10.4f} | {gap:<+8.1%} | [{ci_low:.1%}, {ci_high:.1%}]")

# 1. Full 483-market dataset
evaluate_calibration(records, "FULL 483 CLOB CORPUS")

# 2. In-play vs Pre-event breakdown
in_play_sub = [r for r in records if r["is_in_play"]]
pre_event_sub = [r for r in records if not r["is_in_play"]]
evaluate_calibration(in_play_sub, "IN-PLAY LIVE PROPS ONLY (created AFTER game started)")
evaluate_calibration(pre_event_sub, "PRE-EVENT MARKETS ONLY (created BEFORE event)")

# 3. Deduplicated events (1 market per event_key)
dedup_dict = {}
for r in records:
    k = r["event_key"]
    if k not in dedup_dict:
        dedup_dict[k] = r
dedup_sub = list(dedup_dict.values())
evaluate_calibration(dedup_sub, "EVENT-DEDUPLICATED MARKETS (1 per event cluster)")

# 4. Early entry only (ratio_in_market <= 0.33)
early_sub = [r for r in records if r["ratio_in_market"] is not None and r["ratio_in_market"] <= 0.333]
evaluate_calibration(early_sub, "EARLY ENTRY ONLY (first 33% of market lifetime)")

# Save full processed dataset
with open(f"{scratch_path}/calibration_analysis_483.json", "w", encoding="utf-8") as f:
    json.dump({
        "total_records": len(records),
        "records": records
    }, f, indent=2)
