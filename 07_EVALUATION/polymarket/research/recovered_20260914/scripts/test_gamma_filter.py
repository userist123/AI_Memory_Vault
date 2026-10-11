import socket
import urllib.request
import urllib.parse
import json

orig_getaddrinfo = socket.getaddrinfo
def patched_getaddrinfo(host, port, *args, **kwargs):
    if "polymarket.com" in host:
        return orig_getaddrinfo("172.64.153.51", port, *args, **kwargs)
    return orig_getaddrinfo(host, port, *args, **kwargs)
socket.getaddrinfo = patched_getaddrinfo

# 1. Does Gamma API support enableOrderBook=true?
url1 = "https://gamma-api.polymarket.com/markets?closed=true&enableOrderBook=true&limit=10"
req1 = urllib.request.Request(url1, headers={"Accept": "application/json", "User-Agent": "test"})
with urllib.request.urlopen(req1, timeout=10) as resp:
    d1 = json.loads(resp.read().decode("utf-8"))
    print(f"Filter enableOrderBook=true returned: {len(d1)} markets. First ID: {d1[0]['id'] if d1 else None}")
    if d1:
        print("All have enableOrderBook=True?", all(m.get("enableOrderBook") is True for m in d1))

# 2. What about the transition point? When did enableOrderBook become true?
# Let's inspect the 1 market in offset 1500 that had enableOrderBook=True
with open("C:/Users/Marius/.gemini/antigravity/brain/aebf6032-0fa2-438b-bb11-3eda139a64e3/scratch/sampled_500_markets.json", "r", encoding="utf-8") as f:
    sampled = json.load(f)

era_1500_clob = [m for m in sampled if m.get("_era") == "era_2024_offset1500" and m.get("enableOrderBook") is True]
if era_1500_clob:
    m = era_1500_clob[0]
    print(f"Offset 1500 market with enableOrderBook=True: ID={m.get('id')}, question={m.get('question')}, slug={m.get('slug')}")
    print(f"clobTokenIds: {m.get('clobTokenIds')}")
    print(f"startDate: {m.get('startDate')}, endDate: {m.get('endDate')}")
