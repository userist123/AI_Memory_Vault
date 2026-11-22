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

function isPublic(addr) {
  if (net.isIPv6(addr)) return !/^(::1?$|f[cd]|fe[89ab]|::ffff:)/i.test(addr);
  const p = addr.split('.').map(Number);
  return !(p[0] === 10 || p[0] === 127 || p[0] === 0 || (p[0] === 169 && p[1] === 254) || (p[0] === 172 && p[1] >= 16 && p[1] <= 31) ||
    (p[0] === 192 && p[1] === 168) || (p[0] === 100 && p[1] >= 64 && p[1] <= 127) || p[0] >= 224);
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
  upstream.on('error', () => { try { client.end('HTTP/1.1 502 Bad Gateway\r\nConnection: close\r\n\r\n'); } catch { /* closed */ } });
  client.on('error', () => upstream.destroy());
});
server.on('clientError', (_e, socket) => socket.destroy());
server.listen(PORT, HOST, () => console.log(`aas-proxy listening on ${HOST}:${PORT}`));
