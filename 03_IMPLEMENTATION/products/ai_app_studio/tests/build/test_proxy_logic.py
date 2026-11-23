"""proxy.js logic without Docker: the isPublic table and DNS-rebinding refusal (needs a local `node`; skipped otherwise)."""
from __future__ import annotations

import json
import shutil
import subprocess
from pathlib import Path

import pytest

PROXY_JS = Path(__file__).resolve().parents[2] / "images" / "proxy" / "proxy.js"
NODE = shutil.which("node") or ""
pytestmark = pytest.mark.skipif(not NODE, reason="node not available")


def node(script: str, *args: str) -> dict:  # type: ignore[type-arg]
    r = subprocess.run([NODE, "-e", script, str(PROXY_JS), *args], capture_output=True, text=True, timeout=60)  # noqa: S603
    assert r.returncode == 0, r.stderr
    return json.loads(r.stdout.strip().splitlines()[-1])


REFUSED = [
    "64:ff9b::a00:1", "64:ff9b::101:101", "64:ff9b:1::1",  # NAT64 (even when wrapping a public IPv4)
    "2002:a00:1::1", "2002:101:101::1",  # 6to4
    "ff02::1", "ff0e::1", "224.0.0.1", "239.255.255.250",  # multicast
    "fec0::1",  # site-local
    "::ffff:10.0.0.1", "::ffff:1.1.1.1", "::ffff:127.0.0.1", "::10.0.0.1", "::1.1.1.1",  # IPv4-mapped / -compatible
    "198.18.0.1", "198.19.255.255",  # benchmarking 198.18/15
    "10.0.0.1", "172.16.0.1", "172.31.255.255", "192.168.1.1",  # RFC1918
    "127.0.0.1", "127.8.9.10", "::1", "::",  # loopback / unspecified
    "169.254.169.254", "fe80::1", "febf::1",  # link-local (cloud metadata)
    "100.64.0.1", "100.127.255.255",  # CGNAT
    "0.0.0.0", "255.255.255.255", "240.0.0.1", "192.0.2.1", "203.0.113.9", "fc00::1", "fd12::1", "2001:db8::1",
    "2001::1", "100::1", "not-an-ip", "",
]
ALLOWED = ["1.1.1.1", "104.16.6.34", "2606:4700::1111", "8.8.8.8", "172.32.0.1", "100.128.0.1", "198.20.0.1"]

TABLE = """
const { isPublic } = require(process.argv[1]);
const addrs = JSON.parse(process.argv[2]);
console.log(JSON.stringify(Object.fromEntries(addrs.map((a) => [a, isPublic(a)]))));
"""


def test_is_public_table() -> None:
    res = node(TABLE, json.dumps(REFUSED + ALLOWED))
    wrong_refused = [a for a in REFUSED if res[a] is not False]
    wrong_allowed = [a for a in ALLOWED if res[a] is not True]
    assert not wrong_refused, f"must be refused: {wrong_refused}"
    assert not wrong_allowed, f"must be allowed: {wrong_allowed}"


REBIND = """
const dns = require('node:dns');
const net = require('node:net');
const answers = JSON.parse(process.argv[2]);
const realLookup = dns.lookup;
dns.lookup = (host, opts, cb) => { if (host !== 'registry.npmjs.org') return realLookup(host, opts, cb); if (typeof opts === 'function') { cb = opts; opts = {}; } cb(null, answers.map((a) => ({ address: a, family: net.isIPv6(a) ? 6 : 4 }))); };
const { safeLookup, start } = require(process.argv[1]);
const out = {};
safeLookup('registry.npmjs.org', { all: true }, (err, addrs) => {
  out.lookupErr = err ? String(err.message) : null;
  out.lookup = addrs ? addrs.map((a) => a.address) : null;
  if (process.argv[3] !== 'connect') { console.log(JSON.stringify(out)); return; }
  const srv = start().listen(0, '127.0.0.1', () => {
    const s = net.connect(srv.address().port, '127.0.0.1', () => s.write('CONNECT registry.npmjs.org:443 HTTP/1.1\\r\\nHost: registry.npmjs.org:443\\r\\n\\r\\n'));
    s.setTimeout(10000, () => { out.status = 'timeout'; console.log(JSON.stringify(out)); process.exit(0); });
    s.once('data', (d) => { out.status = String(d).split('\\r\\n')[0]; console.log(JSON.stringify(out)); process.exit(0); });
    s.on('error', (e) => { out.status = 'error ' + e.code; console.log(JSON.stringify(out)); process.exit(0); });
  });
});
"""


@pytest.mark.parametrize("answers", [["10.0.0.5"], ["127.0.0.1"], ["169.254.169.254", "::1"], ["::ffff:192.168.0.1"], ["64:ff9b::a00:1"]])
def test_dns_rebinding_to_private_address_is_403(answers: list[str]) -> None:
    res = node(REBIND, json.dumps(answers), "connect")
    assert res["lookupErr"] == "non-public address refused", res
    assert res["status"] == "HTTP/1.1 403 Forbidden", res  # refused before any connection attempt, not a 502


def test_mixed_answers_keep_only_public_addresses() -> None:  # [+] positive control, no network needed
    res = node(REBIND, json.dumps(["10.0.0.5", "104.16.6.34", "fd00::1", "2606:4700::1111"]))
    assert res["lookupErr"] is None and res["lookup"] == ["104.16.6.34", "2606:4700::1111"], res
