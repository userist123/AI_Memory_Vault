/**
 * JARVIS Web Ecosystem — Local HTTP Dev Server
 * Zero external dependencies — Node.js stdlib only.
 * Serves the JARVIS dashboard and ES modules.
 */

const http = require('http');
const fs = require('fs');
const path = require('path');

const PORT = Number.parseInt(process.env.JARVIS_TEST_PORT || '3000', 10);
const HOST = '127.0.0.1';
const ROOT = path.resolve(__dirname);

const MIME = {
  '.html': 'text/html; charset=utf-8',
  '.css': 'text/css; charset=utf-8',
  '.js': 'application/javascript; charset=utf-8',
  '.json': 'application/json; charset=utf-8',
  '.png': 'image/png',
  '.jpg': 'image/jpeg',
  '.svg': 'image/svg+xml',
  '.ico': 'image/x-icon',
  '.woff2': 'font/woff2',
  '.woff': 'font/woff',
  '.ttf': 'font/ttf'
};

function safeFilePath(urlPath) {
  const decoded = decodeURIComponent(urlPath);
  const candidate = path.resolve(ROOT, `.${decoded}`);
  const relative = path.relative(ROOT, candidate);
  if (relative.startsWith('..') || path.isAbsolute(relative)) return null;
  return candidate;
}

const server = http.createServer((req, res) => {
  const rawPath = (req.url || '/').split('?')[0] || '/';
  const urlPath = rawPath === '/' ? '/index.html' : rawPath;
  let filePath;

  try {
    filePath = safeFilePath(urlPath);
  } catch {
    res.writeHead(400, {'Content-Type': 'text/plain; charset=utf-8'});
    res.end('Bad Request');
    return;
  }

  if (!filePath) {
    res.writeHead(403, {'Content-Type': 'text/plain; charset=utf-8'});
    res.end('Forbidden');
    return;
  }

  fs.readFile(filePath, (err, data) => {
    if (err) {
      const status = err.code === 'ENOENT' ? 404 : 500;
      res.writeHead(status, {'Content-Type': 'text/plain; charset=utf-8', 'Cache-Control': 'no-store'});
      res.end(status === 404 ? `404 Not Found: ${urlPath}` : '500 Internal Server Error');
      return;
    }

    const ext = path.extname(filePath).toLowerCase();
    res.writeHead(200, {
      'Content-Type': MIME[ext] || 'application/octet-stream',
      'Cache-Control': 'no-cache',
      'Access-Control-Allow-Origin': '*',
      'X-Content-Type-Options': 'nosniff'
    });
    res.end(data);
  });
});

server.listen(PORT, HOST, () => {
  console.log(`\n  🤖 JARVIS Web Ecosystem`);
  console.log(`  ─────────────────────────────────────`);
  console.log(`  ✅ HTTP Server: http://${HOST}:${PORT}`);
  console.log(`  ✅ Vault API expected: http://127.0.0.1:8000`);
  console.log(`  ─────────────────────────────────────`);
});

server.on('error', (err) => {
  if (err.code === 'EADDRINUSE') {
    console.error(`\n  ❌ Port ${PORT} already in use.\n`);
  } else {
    console.error('\n  ❌ Server error:', err.message);
  }
  process.exit(1);
});
