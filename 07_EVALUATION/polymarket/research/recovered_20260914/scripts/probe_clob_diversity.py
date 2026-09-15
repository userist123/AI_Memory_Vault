import socket
import urllib.request
import urllib.parse
import json
import time

orig_getaddrinfo = socket.getaddrinfo
def patched_getaddrinfo(host, port, *args, **kwargs):
    if "polymarket.com" in host:
        return orig_getaddrinfo("172.64.153.51", port, *args, **kwargs)
    return orig_getaddrinfo(host, port, *args, **kwargs)
socket.getaddrinfo = patched_getaddrinfo

# Fetch 300 markets across 3 offsets (0, 100, 200) with order=id, ascending=false
all_markets = []
for offset in [0, 100, 200]:
    url = f"https://gamma-api.polymarket.com/markets?closed=true&limit=100&offset={offset}&order=id&ascending=false"
    req = urllib.request.Request(url, headers={"Accept": "application/json", "User-Agent": "research"})
    with urllib.request.urlopen(req, timeout=15) as resp:
        batch = json.loads(resp.read().decode("utf-8"))
        print(f"Offset {offset}: fetched {len(batch)} markets. First ID: {batch[0].get('id')} ({batch[0].get('question')[:40]}...)")
        all_markets.extend(batch)
    time.sleep(0.4)

print(f"Total fetched: {len(all_markets)}")

# Check enableOrderBook and outcome count
clob_markets = [m for m in all_markets if m.get("enableOrderBook") is True]
print(f"CLOB markets (enableOrderBook is True): {len(clob_markets)} / {len(all_markets)}")

# Check category distribution or keywords in questions
keywords = {}
for m in clob_markets:
    q = m.get("question", "").lower()
    if "vs." in q or "vs " in q or "o/u" in q or "spread" in q:
        cat = "sports"
    elif "ethereum" in q or "bitcoin" in q or "solana" in q or "crypto" in q:
        cat = "crypto"
    elif "trump" in q or "biden" in q or "harris" in q or "election" in q or "senate" in q:
        cat = "politics"
    else:
        cat = "other"
    keywords[cat] = keywords.get(cat, 0) + 1

print(f"Category breakdown: {keywords}")
