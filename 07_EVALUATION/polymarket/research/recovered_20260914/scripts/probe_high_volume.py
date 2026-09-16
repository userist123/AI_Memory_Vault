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

# Query closed markets ordered by volume descending
url = "https://gamma-api.polymarket.com/markets?closed=true&limit=20&order=volume&ascending=false"
req = urllib.request.Request(url, headers={"Accept": "application/json", "User-Agent": "test"})
with urllib.request.urlopen(req, timeout=10) as resp:
    data = json.loads(resp.read().decode("utf-8"))
    print(f"Fetched {len(data)} top-volume closed markets:")
    for m in data[:5]:
        print(f"  ID: {m.get('id')} | Vol: ${float(m.get('volume', 0)):,.0f} | CLOB: {m.get('enableOrderBook')} | Q: {m.get('question')}")
