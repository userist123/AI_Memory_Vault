import socket
import urllib.request
import json

orig_getaddrinfo = socket.getaddrinfo
def patched_getaddrinfo(host, port, *args, **kwargs):
    if "polymarket.com" in host:
        return orig_getaddrinfo("172.64.153.51", port, *args, **kwargs)
    return orig_getaddrinfo(host, port, *args, **kwargs)
socket.getaddrinfo = patched_getaddrinfo

# Probe /markets/keyset
url = "https://gamma-api.polymarket.com/markets/keyset?limit=5"
req = urllib.request.Request(url, headers={"Accept": "application/json", "User-Agent": "research"})
try:
    with urllib.request.urlopen(req, timeout=10) as resp:
        d = json.loads(resp.read().decode("utf-8"))
        print("keyset endpoint status: 200 OK")
        print("type:", type(d))
        if isinstance(d, dict):
            print("keys:", list(d.keys()))
        elif isinstance(d, list):
            print(f"list length: {len(d)}, first item keys: {list(d[0].keys()) if d else []}")
except Exception as e:
    print(f"keyset endpoint error: {e}")
