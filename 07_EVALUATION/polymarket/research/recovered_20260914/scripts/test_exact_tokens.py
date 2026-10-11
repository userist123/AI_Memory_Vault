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

# Exact token IDs from market 4537965
# outcome 0: 61132307481967276988002364894936713973810275981590478309714562700370376249141
# outcome 1: 104093803534517426712740679855443122353765698182657011070351298955004240391517

# And from an Ethereum market: ID 4532843
with open("C:/Users/Marius/Documents/Codex/AI_Memory_Vault_CODEX_READY/07_EVALUATION/polymarket/fixtures/historical_dataset_50markets.json", "r", encoding="utf-8") as f:
    d = json.load(f)

# Find market 4532843
m_eth = [m for m in d["markets"] if m["market_id"] == "4532843"][0]
tok_eth = m_eth["outcome_ids"][0]

tok_bb = "61132307481967276988002364894936713973810275981590478309714562700370376249141"

test_tokens = [
    ("Baseball_4537965_tok0", tok_bb),
    ("ETH_4532843_tok0", tok_eth),
]

param_variations = [
    {"interval": "all"},
    {"interval": "max"},
    {"interval": "1d"},
    {"interval": "6h"},
    {"interval": "1h"},
    {"interval": "all", "fidelity": 1},
    {"interval": "all", "fidelity": 5},
    {"interval": "all", "fidelity": 60},
    {"interval": "1d", "fidelity": 1},
]

print("=== Probing CLOB /prices-history with exact token IDs ===")
for label, tok in test_tokens:
    print(f"\n--- Testing {label} (Token {tok}) ---")
    for params in param_variations:
        p_copy = dict(params)
        p_copy["market"] = tok
        url = "https://clob.polymarket.com/prices-history?" + urllib.parse.urlencode(p_copy)
        req = urllib.request.Request(url, headers={"Accept": "application/json", "User-Agent": "research"})
        try:
            with urllib.request.urlopen(req, timeout=10) as resp:
                data = json.loads(resp.read().decode("utf-8"))
                history = data.get("history", [])
                print(f"  GET {params} -> points: {len(history)}")
                if history:
                    print(f"    first: {history[0]} | last: {history[-1]}")
        except Exception as e:
            print(f"  GET {params} -> Error: {e}")

# Also test POST batch-prices-history with the same parameters
print("\n=== Probing CLOB /batch-prices-history ===")
for label, tok in test_tokens:
    print(f"\n--- Testing batch {label} ---")
    for params in [
        {"interval": "all"},
        {"interval": "max"},
        {"interval": "1d"},
        {"interval": "all", "fidelity": 1},
        {"interval": "1d", "fidelity": 1},
    ]:
        body = dict(params)
        body["markets"] = [tok]
        req = urllib.request.Request(
            "https://clob.polymarket.com/batch-prices-history",
            data=json.dumps(body).encode("utf-8"),
            headers={"Accept": "application/json", "Content-Type": "application/json", "User-Agent": "research"},
            method="POST"
        )
        try:
            with urllib.request.urlopen(req, timeout=10) as resp:
                data = json.loads(resp.read().decode("utf-8"))
                history = data.get("history", {}).get(tok, [])
                print(f"  POST {params} -> points: {len(history)}")
        except Exception as e:
            print(f"  POST {params} -> Error: {e}")
