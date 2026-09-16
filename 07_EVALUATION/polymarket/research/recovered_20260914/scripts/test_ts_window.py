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

tok_eth = "77833700798057549775711193025735912171018908225624826409125393953469904877895"

for params in [
    {"market": tok_eth, "startTs": 1789325000, "endTs": 1789331000},
    {"market": tok_eth, "startTs": 1789325000, "endTs": 1789331000, "fidelity": 1},
    {"market": tok_eth, "interval": "max", "fidelity": 1},
    {"market": tok_eth, "interval": "all", "fidelity": 1},
]:
    url = "https://clob.polymarket.com/prices-history?" + urllib.parse.urlencode(params)
    req = urllib.request.Request(url, headers={"Accept": "application/json", "User-Agent": "research"})
    try:
        with urllib.request.urlopen(req, timeout=10) as resp:
            data = json.loads(resp.read().decode("utf-8"))
            pts = data.get("history", [])
            print(f"params: {params} -> points: {len(pts)}")
    except Exception as e:
        print(f"params: {params} -> Error: {e}")
