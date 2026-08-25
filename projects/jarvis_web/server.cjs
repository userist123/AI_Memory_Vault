/**
 * JARVIS Web Ecosystem — Local HTTP Dev Server
 * Zero external dependencies — Node.js stdlib only.
 * Serves ES6 modules with correct MIME types.
 *
 * Usage: node server.js
 * Serves: http://localhost:3000
 */

const http = require('http');
const fs   = require('fs');
const path = require('path');

const PORT = parseInt(process.env.JARVIS_TEST_PORT || '3000', 10);
const HOST = process.env.JARVIS_TEST_PORT ? '127.0.0.1' : '127.0.0.1';
const ROOT = __dirname;

const MIME = {
  '.html': 'text/html; charset=utf-8',
  '.css':  'text/css; charset=utf-8',
  '.js':   'application/javascript; charset=utf-8',
  '.json': 'application/json; charset=utf-8',
  '.png':  'image/png',
  '.jpg':  'image/jpeg',
  '.svg':  'image/svg+xml',
  '.ico':  'image/x-icon',
  '.woff2':'font/woff2',
  '.woff': 'font/woff',
  '.ttf':  'font/ttf',
};

const server = http.createServer((req, res) => {
  // Normalize URL — strip query string
  let urlPath = req.url.split('?')[0];
  if (urlPath === '/' || urlPath === '') urlPath = '/index.html';

  const filePath = path.join(ROOT, urlPath);

  // Security: prevent path traversal outside ROOT
  if (!filePath.startsWith(ROOT)) {
    res.writeHead(403);
    res.end('Forbidden');
    return;
  }

  fs.readFile(filePath, (err, data) => {
    if (err) {
      if (err.code === 'ENOENT') {
        res.writeHead(404, { 'Content-Type': 'text/plain' });
        res.end(`404 Not Found: ${urlPath}`);
      } else {
        res.writeHead(500, { 'Content-Type': 'text/plain' });
        res.end('500 Internal Server Error');
      }
      return;
    }

    const ext  = path.extname(filePath).toLowerCase();
    const mime = MIME[ext] || 'application/octet-stream';

    res.writeHead(200, {
      'Content-Type': mime,
      'Cache-Control': 'no-cache',
      'Access-Control-Allow-Origin': '*',
    });
    res.end(data);
  });
});

server.listen(PORT, HOST, () => {
  console.log(`\n  🤖 JARVIS Web Ecosystem`);
  console.log(`  ─────────────────────────────────────`);
  console.log(`  ✅ HTTP Server running at:`);
  console.log(`     http://localhost:${PORT}`);
  console.log(`  ─────────────────────────────────────`);
  console.log(`  Press Ctrl+C to stop.\n`);
});

server.on('error', (err) => {
  if (err.code === 'EADDRINUSE') {
    console.error(`\n  ❌ Port ${PORT} already in use. Kill the existing process and retry.\n`);
  } else {
    console.error('\n  ❌ Server error:', err.message);
  }
  process.exit(1);
});
