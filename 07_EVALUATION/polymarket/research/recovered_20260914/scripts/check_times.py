import json
from datetime import datetime, timezone, timedelta

with open("C:/Users/Marius/Documents/Codex/AI_Memory_Vault_CODEX_READY/07_EVALUATION/polymarket/fixtures/historical_dataset_50markets.json", "r", encoding="utf-8") as f:
    dataset = json.load(f)

now_utc = datetime.now(timezone.utc)
print(f"Current UTC: {now_utc.isoformat()}")

diffs = []
for m in dataset["markets"]:
    res_iso = m["resolution_known_at"]
    res_dt = datetime.fromisoformat(res_iso.replace("Z", "+00:00")).astimezone(timezone.utc)
    diff = (now_utc - res_dt).total_seconds()
    diffs.append((diff, m["market_id"], res_iso))

diffs.sort()
print(f"Min time since closedTime: {diffs[0][0]:.0f}s (ID {diffs[0][1]} @ {diffs[0][2]})")
print(f"Max time since closedTime: {diffs[-1][0]:.0f}s (ID {diffs[-1][1]} @ {diffs[-1][2]})")
