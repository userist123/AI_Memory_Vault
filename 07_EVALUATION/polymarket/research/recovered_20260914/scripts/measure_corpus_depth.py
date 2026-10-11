import socket
import urllib.request
import urllib.parse
import json
import time
import math

orig_getaddrinfo = socket.getaddrinfo
def patched_getaddrinfo(host, port, *args, **kwargs):
    if "polymarket.com" in host:
        return orig_getaddrinfo("172.64.153.51", port, *args, **kwargs)
    return orig_getaddrinfo(host, port, *args, **kwargs)
socket.getaddrinfo = patched_getaddrinfo

batches = [
    ("era_2020_offset0", {"closed": "true", "limit": 100, "offset": 0}),
    ("era_2022_offset500", {"closed": "true", "limit": 100, "offset": 500}),
    ("era_2023_offset1000", {"closed": "true", "limit": 100, "offset": 1000}),
    ("era_2024_offset1500", {"closed": "true", "limit": 100, "offset": 1500}),
    ("era_2026_recent", {"closed": "true", "limit": 100, "order": "id", "ascending": "false"}),
]

all_markets = []
print("Fetching 500 markets across 5 slices...")
for name, params in batches:
    url = "https://gamma-api.polymarket.com/markets?" + urllib.parse.urlencode(params)
    req = urllib.request.Request(url, headers={"Accept": "application/json", "User-Agent": "ai-memory-vault-research"})
    with urllib.request.urlopen(req, timeout=15) as resp:
        data = json.loads(resp.read().decode("utf-8"))
        for item in data:
            item["_era"] = name
        print(f"  {name}: fetched {len(data)} markets (first ID: {data[0].get('id') if data else 'none'})")
        all_markets.extend(data)
    time.sleep(0.5)

print(f"Total raw markets fetched: {len(all_markets)}")

# Save sampled 500 raw markets to scratch
scratch_path = "C:/Users/Marius/.gemini/antigravity/brain/aebf6032-0fa2-438b-bb11-3eda139a64e3/scratch"
with open(f"{scratch_path}/sampled_500_markets.json", "w", encoding="utf-8") as f:
    json.dump(all_markets, f, indent=2)

# Cascaded evaluation
f1_pass = []
f2_pass = []
f3_pass = []
f4_pass = []

for m in all_markets:
    if m.get("closed") is True:
        f1_pass.append(m)

for m in f1_pass:
    if m.get("enableOrderBook") is True:
        f2_pass.append(m)

for m in f2_pass:
    toks_raw = m.get("clobTokenIds")
    try:
        toks = json.loads(toks_raw) if isinstance(toks_raw, str) else toks_raw
        if isinstance(toks, list) and len(toks) > 0 and all(isinstance(t, str) and t.strip() for t in toks):
            f3_pass.append((m, toks))
    except Exception:
        pass

for m, toks in f3_pass:
    prices_raw = m.get("outcomePrices")
    outcomes_raw = m.get("outcomes")
    try:
        prices = json.loads(prices_raw) if isinstance(prices_raw, str) else prices_raw
        outcomes = json.loads(outcomes_raw) if isinstance(outcomes_raw, str) else outcomes_raw
        if isinstance(prices, list) and isinstance(outcomes, list) and len(prices) == len(outcomes) == len(toks):
            parsed = [float(x) for x in prices]
            if all(math.isfinite(x) and 0.0 <= x <= 1.0 for x in parsed):
                winners = [outcomes[i] for i, price in enumerate(parsed) if price >= 0.95]
                losers = [price for price in parsed if price <= 0.05]
                if len(winners) == 1 and len(losers) == len(outcomes) - 1:
                    winner_idx = [i for i, price in enumerate(parsed) if price >= 0.95][0]
                    f4_pass.append((m, toks, toks[winner_idx]))
    except Exception:
        pass

print("\n=== Global Cascade Results ===")
print(f"Total sampled: {len(all_markets)}")
print(f"F1 closed == True: {len(f1_pass)} ({len(f1_pass)/len(all_markets):.1%})")
print(f"F2 enableOrderBook is True: {len(f2_pass)} ({len(f2_pass)/len(all_markets):.1%})")
print(f"F3 clobTokenIds valid: {len(f3_pass)} ({len(f3_pass)/len(all_markets):.1%})")
print(f"F4 unambiguous winner: {len(f4_pass)} ({len(f4_pass)/len(all_markets):.1%})")

# Check price history for F4 winners
winning_tokens = [tok_win for m, toks, tok_win in f4_pass]
print(f"\nChecking CLOB batch-prices-history for {len(winning_tokens)} winning tokens...")

hist_all = {}
hist_1440 = {}

for i in range(0, len(winning_tokens), 20):
    chunk = winning_tokens[i:i+20]
    time.sleep(0.3)
    # Check interval=all
    req_all = urllib.request.Request(
        "https://clob.polymarket.com/batch-prices-history",
        data=json.dumps({"markets": chunk, "interval": "all"}).encode("utf-8"),
        headers={"Accept": "application/json", "Content-Type": "application/json", "User-Agent": "research"},
        method="POST"
    )
    try:
        with urllib.request.urlopen(req_all, timeout=10) as resp:
            d = json.loads(resp.read().decode("utf-8"))
            hist_all.update(d.get("history", {}))
    except Exception as e:
        print(f"  chunk {i} interval=all err: {e}")

    # Check fidelity=1440
    req_1440 = urllib.request.Request(
        "https://clob.polymarket.com/batch-prices-history",
        data=json.dumps({"markets": chunk, "interval": "max", "fidelity": 1440}).encode("utf-8"),
        headers={"Accept": "application/json", "Content-Type": "application/json", "User-Agent": "research"},
        method="POST"
    )
    try:
        with urllib.request.urlopen(req_1440, timeout=10) as resp:
            d = json.loads(resp.read().decode("utf-8"))
            hist_1440.update(d.get("history", {}))
    except Exception as e:
        print(f"  chunk {i} fidelity=1440 err: {e}")

f5_all = []
f5_1440 = []

for m, toks, tok_win in f4_pass:
    pts_all = hist_all.get(tok_win, [])
    pts_1440 = hist_1440.get(tok_win, [])
    if len(pts_all) > 0:
        f5_all.append((m, toks, tok_win, len(pts_all)))
    if len(pts_1440) > 0:
        f5_1440.append((m, toks, tok_win, len(pts_1440)))

print(f"\nF5 (interval=all > 0 points): {len(f5_all)} / {len(f4_pass)} ({len(f5_all)/len(all_markets):.1%} of sample)")
print(f"F5 (fidelity=1440 > 0 points): {len(f5_1440)} / {len(f4_pass)} ({len(f5_1440)/len(all_markets):.1%} of sample)")

# Per-era breakdown
print("\n=== Per-Era Breakdown ===")
summary_by_era = {}
for name, _ in batches:
    era_all = [m for m in all_markets if m.get("_era") == name]
    era_f1 = [m for m in f1_pass if m.get("_era") == name]
    era_f2 = [m for m in f2_pass if m.get("_era") == name]
    era_f3 = [m for m, toks in f3_pass if m.get("_era") == name]
    era_f4 = [m for m, toks, win in f4_pass if m.get("_era") == name]
    era_f5_all = [m for m, toks, win, count in f5_all if m.get("_era") == name]
    era_f5_1440 = [m for m, toks, win, count in f5_1440 if m.get("_era") == name]
    summary_by_era[name] = {
        "total": len(era_all),
        "f1_closed": len(era_f1),
        "f2_orderbook": len(era_f2),
        "f3_tokenids": len(era_f3),
        "f4_winner": len(era_f4),
        "f5_all": len(era_f5_all),
        "f5_1440": len(era_f5_1440),
    }
    print(f"Era {name}:")
    print(f"  Total: {len(era_all)} -> F1: {len(era_f1)} -> F2: {len(era_f2)} -> F3: {len(era_f3)} -> F4: {len(era_f4)} -> F5(all): {len(era_f5_all)} | F5(1440): {len(era_f5_1440)}")

# Save full results to json
results = {
    "sample_size": len(all_markets),
    "global_cascade": {
        "raw": len(all_markets),
        "f1_closed": len(f1_pass),
        "f2_enable_order_book": len(f2_pass),
        "f3_clob_token_ids": len(f3_pass),
        "f4_unambiguous_winner": len(f4_pass),
        "f5_history_interval_all": len(f5_all),
        "f5_history_fidelity_1440": len(f5_1440),
    },
    "era_breakdown": summary_by_era,
    "f5_qualifying_markets": [
        {
            "id": m.get("id"),
            "question": m.get("question"),
            "slug": m.get("slug"),
            "era": m.get("_era"),
            "tokens": toks,
            "winning_token": tok_win,
            "points_count": pts
        }
        for m, toks, tok_win, pts in f5_all
    ]
}

with open(f"{scratch_path}/corpus_depth_results.json", "w", encoding="utf-8") as f:
    json.dump(results, f, indent=2)

print("\nSaved full corpus depth results to scratch/corpus_depth_results.json")
