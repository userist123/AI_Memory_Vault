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

# Pick a token from the Ethereum series: ID 4532843 token 0
token_id = "113837943534571997380183060790906806963496053351917637841445778847683936081577"
# Pick a token from a baseball game: ID 4537965 token 0
token_id_bb = "104118029053896590875323862660421251918342461877995185966601423192023577583647"

test_tokens = [
    ("ETH_above_2430", token_id),
    ("Baseball_OU_11.5", token_id_bb),
]

param_variations = [
    {"interval": "all"},
    {"interval": "max"},
    {"interval": "all", "fidelity": 1},
    {"interval": "all", "fidelity": 5},
    {"interval": "all", "fidelity": 60},
    {"interval": "all", "fidelity": 1440},
    {"interval": "1d", "fidelity": 1},
    {"interval": "1d", "fidelity": 5},
    {"interval": "1d", "fidelity": 60},
    {"interval": "6h", "fidelity": 1},
    {"interval": "1h", "fidelity": 1},
]

print("=== Probing CLOB /prices-history parameter variations ===")
for label, tok in test_tokens:
    print(f"\n--- Testing {label} (Token {tok[:15]}...) ---")
    for params in param_variations:
        p_copy = dict(params)
        p_copy["market"] = tok
        url = "https://clob.polymarket.com/prices-history?" + urllib.parse.urlencode(p_copy)
        req = urllib.request.Request(url, headers={"Accept": "application/json", "User-Agent": "research"})
        try:
            with urllib.request.urlopen(req, timeout=10) as resp:
                data = json.loads(resp.read().decode("utf-8"))
                history = data.get("history", [])
                print(f"  params: {params} -> status 200, points: {len(history)}")
                if history and params == {"interval": "all"}:
                    print(f"    first: {history[0]}, last: {history[-1]}")
        except urllib.error.HTTPError as e:
            print(f"  params: {params} -> HTTP Error {e.code}: {e.read().decode('utf-8')[:100]}")
        except Exception as e:
            print(f"  params: {params} -> Error: {e}")
