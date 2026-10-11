import socket
import urllib.request
import urllib.parse
import json
import time
import math
from datetime import datetime, timezone

orig_getaddrinfo = socket.getaddrinfo
def patched_getaddrinfo(host, port, *args, **kwargs):
    if "polymarket.com" in host:
        return orig_getaddrinfo("172.64.153.51", port, *args, **kwargs)
    return orig_getaddrinfo(host, port, *args, **kwargs)
socket.getaddrinfo = patched_getaddrinfo

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

# We will sample 5 offsets: 0, 300, 600, 900, 1200 with limit=100
offsets = [0, 300, 600, 900, 1200]
sampled_markets = []

print("Fetching 500 CLOB markets across 5 slices (offsets 0, 300, 600, 900, 1200)...")
for off in offsets:
    url = f"https://gamma-api.polymarket.com/markets?closed=true&limit=100&offset={off}&order=id&ascending=false"
    req = urllib.request.Request(url, headers={"Accept": "application/json", "User-Agent": "research"})
    with urllib.request.urlopen(req, timeout=15) as resp:
        batch = json.loads(resp.read().decode("utf-8"))
        print(f"  Offset {off}: fetched {len(batch)} markets (first ID: {batch[0].get('id') if batch else 'none'})")
        for item in batch:
            item["_slice_offset"] = off
        sampled_markets.extend(batch)
    time.sleep(0.4)

print(f"Total raw fetched: {len(sampled_markets)}")

# Filter for binary CLOB markets with valid outcomes and unambiguous winners
qualified_markets = []
for m in sampled_markets:
    if m.get("closed") is not True:
        continue
    if m.get("enableOrderBook") is not True:
        continue
    
    # Check outcomes, clobTokenIds, outcomePrices
    try:
        toks = json.loads(m["clobTokenIds"]) if isinstance(m.get("clobTokenIds"), str) else m.get("clobTokenIds")
        outcomes = json.loads(m["outcomes"]) if isinstance(m.get("outcomes"), str) else m.get("outcomes")
        prices = json.loads(m["outcomePrices"]) if isinstance(m.get("outcomePrices"), str) else m.get("outcomePrices")
        if not (isinstance(toks, list) and isinstance(outcomes, list) and isinstance(prices, list)):
            continue
        if len(toks) != 2 or len(outcomes) != 2 or len(prices) != 2:
            continue
        parsed_prices = [float(p) for p in prices]
        if any(not math.isfinite(p) or p < 0 or p > 1 for p in parsed_prices):
            continue
        winners = [outcomes[i] for i, p in enumerate(parsed_prices) if p >= 0.95]
        losers = [p for p in parsed_prices if p <= 0.05]
        if len(winners) == 1 and len(losers) == 1:
            win_idx = [i for i, p in enumerate(parsed_prices) if p >= 0.95][0]
            qualified_markets.append((m, toks, win_idx))
    except Exception:
        continue

print(f"Qualified binary CLOB markets: {len(qualified_markets)} / {len(sampled_markets)}")

# Save raw qualified markets to scratch
scratch_path = "C:/Users/Marius/.gemini/antigravity/brain/aebf6032-0fa2-438b-bb11-3eda139a64e3/scratch"
with open(f"{scratch_path}/qualified_clob_markets_500.json", "w", encoding="utf-8") as f:
    json.dump([m for m, toks, win_idx in qualified_markets], f, indent=2)

# Fetch price history for outcome 0 of all qualified markets
# We test batch-prices-history in chunks of 20
tok0_list = [toks[0] for m, toks, win_idx in qualified_markets]
unique_tok0 = list(dict.fromkeys(tok0_list))
print(f"Fetching batch price history for {len(unique_tok0)} tokens...")

history_all = {}
for i in range(0, len(unique_tok0), 20):
    chunk = unique_tok0[i:i+20]
    time.sleep(0.3)
    req = urllib.request.Request(
        "https://clob.polymarket.com/batch-prices-history",
        data=json.dumps({"markets": chunk, "interval": "all"}).encode("utf-8"),
        headers={"Accept": "application/json", "Content-Type": "application/json", "User-Agent": "research"},
        method="POST"
    )
    try:
        with urllib.request.urlopen(req, timeout=12) as resp:
            d = json.loads(resp.read().decode("utf-8"))
            history_all.update(d.get("history", {}))
    except Exception as e:
        print(f"  Chunk {i} error: {e}")

print(f"Successfully fetched price history for {len(history_all)} tokens.")

with open(f"{scratch_path}/batch_history_all_expanded.json", "w", encoding="utf-8") as f:
    json.dump(history_all, f)

print("Saved batch history to scratch/batch_history_all_expanded.json")
