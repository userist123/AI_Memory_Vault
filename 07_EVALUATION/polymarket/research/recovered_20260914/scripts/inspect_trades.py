import json
from datetime import datetime

with open("C:/Users/Marius/.gemini/antigravity/brain/aebf6032-0fa2-438b-bb11-3eda139a64e3/scratch/mechanical_control_summary.json", "r", encoding="utf-8") as f:
    summary = json.load(f)

trades = summary["trades"]
print(f"Total trades: {len(trades)}")

# Check timing difference: entry_at vs resolution_known_at
time_diffs_sec = []
for t in trades:
    entry_dt = datetime.fromisoformat(t["entry_at"].replace("Z", "+00:00"))
    res_dt = datetime.fromisoformat(t["resolution_known_at"].replace("Z", "+00:00"))
    diff = (res_dt - entry_dt).total_seconds()
    time_diffs_sec.append(diff)

print(f"Time from entry to resolution: min={min(time_diffs_sec):.0f}s ({min(time_diffs_sec)/3600:.1f}h), max={max(time_diffs_sec):.0f}s ({max(time_diffs_sec)/3600:.1f}h), mean={sum(time_diffs_sec)/len(time_diffs_sec)/3600:.1f}h")

# Check trades where entry price was > 0.90
late_entries = [t for t in trades if t["entry_price"] > 0.90]
print(f"\nTrades with entry price > 0.90: {len(late_entries)} / {len(trades)}")
for t in late_entries[:5]:
    print(f"  ID: {t['market_id']} | Price: {t['entry_price']} | Won: {t['won']} | Q: {t['question'][:50]}")

# Check trades where entry price was < 0.10
low_entries = [t for t in trades if t["entry_price"] < 0.10]
print(f"\nTrades with entry price < 0.10: {len(low_entries)} / {len(trades)}")
for t in low_entries[:5]:
    print(f"  ID: {t['market_id']} | Price: {t['entry_price']} | Won: {t['won']} | Q: {t['question'][:50]}")
