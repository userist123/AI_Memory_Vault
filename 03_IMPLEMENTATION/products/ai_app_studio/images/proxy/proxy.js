'use strict';
// Tiny allow-list CONNECT proxy for the install stage (DESIGN 3.3; probe N5). No dependencies.
// Only `CONNECT registry.npmjs.org:443` is tunnelled; everything else gets 403. IP literals, other ports and other hosts
// are refused, and the resolved upstream address must be public (no DNS-rebinding to a private/loopback address).
const http = require('node:http');
const net = require('node:net');
const dns = require('node:dns');

const ALLOW = new Set(['registry.npmjs.org:443']);
const HOST = process.env.AAS_PROXY_HOST || '0.0.0.0';
const PORT = Number(process.env.AAS_PROXY_PORT || 3128);

// Two lists: Node maps IPv4 onto ::ffff:0:0/96 when checking, so an IPv6 deny rule would otherwise match every IPv4 address.
const DENY4 = new net.BlockList();
const DENY6 = new net.BlockList();
[
  ['0.0.0.0', 8], ['10.0.0.0', 8], ['100.64.0.0', 10], ['127.0.0.0', 8], ['169.254.0.0', 16], ['172.16.0.0', 12],
  ['192.0.0.0', 24], ['192.0.2.0', 24], ['192.168.0.0', 16], ['198.18.0.0', 15], ['198.51.100.0', 24], ['203.0.113.0', 24],
  ['224.0.0.0', 3], // multicast + reserved + broadcast
].forEach(([a, p]) => DENY4.addSubnet(a, p, 'ipv4'));
[
  ['::', 96], // unspecified, loopback, IPv4-compatible ::a.b.c.d
  ['::ffff:0:0', 96], // IPv4-mapped
  ['64:ff9b::', 96], ['64:ff9b:1::', 48], // NAT64
  ['2002::', 16], // 6to4
  ['100::', 64], ['2001:db8::', 32], ['2001::', 32], // discard, documentation, Teredo
  ['fc00::', 7], ['fe80::', 10], ['fec0::', 10], ['ff00::', 8], // ULA, link-local, site-local, multicast
].forEach(([a, p]) => DENY6.addSubnet(a, p, 'ipv6'));

function isPublic(addr) {
  if (net.isIPv4(addr)) return !DENY4.check(addr, 'ipv4');
  if (net.isIPv6(addr)) return !DENY6.check(addr, 'ipv6');
  return false;
}

function safeLookup(hostname, opts, cb) {
  // net.connect may ask for all addresses (autoSelectFamily); filter every candidate, refuse if none is public.
  dns.lookup(hostname, { ...opts, all: true }, (err, addrs) => {
    if (err) return cb(err);
    const ok = addrs.filter((a) => isPublic(a.address));
    if (ok.length === 0) return cb(new Error('non-public address refused'));
    if (opts && opts.all) return cb(null, ok);
    cb(null, ok[0].address, ok[0].family);
  });
}

function start() {
  const server = http.createServer((req, res) => { res.writeHead(403, { Connection: 'close' }); res.end('forbidden\n'); });
  server.on('connect', (req, client, head) => {
    const target = String(req.url || '');
    if (!ALLOW.has(target)) { client.end('HTTP/1.1 403 Forbidden\r\nConnection: close\r\n\r\n'); return; }
    const upstream = net.connect({ host: 'registry.npmjs.org', port: 443, lookup: safeLookup }, () => {
      client.write('HTTP/1.1 200 Connection Established\r\n\r\n');
      if (head && head.length) upstream.write(head);
      upstream.pipe(client); client.pipe(upstream);
    });
    upstream.setTimeout(120000, () => upstream.destroy());
    upstream.on('error', (err) => {
      const refused = err && /non-public/.test(String(err.message));
      const line = refused ? 'HTTP/1.1 403 Forbidden' : 'HTTP/1.1 502 Bad Gateway';
      try { client.end(line + '\r\nConnection: close\r\n\r\n'); } catch { /* closed */ }
    });
    client.on('error', () => upstream.destroy());
  });
  server.on('clientError', (_e, socket) => socket.destroy());
  return server;
}

module.exports = { isPublic, safeLookup, start };
if (require.main === module) start().listen(PORT, HOST, () => console.log(`aas-proxy listening on ${HOST}:${PORT}`));
