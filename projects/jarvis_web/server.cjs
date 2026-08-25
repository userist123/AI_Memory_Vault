/**
 * JARVIS Web Ecosystem — local command-center server.
 * Zero external dependencies. Static file host + strict path boundary.
 */
const http = require('http');
const fs = require('fs');
const path = require('path');

const PORT = Number(process.env.JARVIS_TEST_PORT || 3000);
const HOST = '127.0.0.1';
const ROOT = path.resolve(__dirname);
const MIME = {
  '.html':'text/html; charset=utf-8', '.css':'text/css; charset=utf-8', '.js':'application/javascript; charset=utf-8',
  '.json':'application/json; charset=utf-8', '.png':'image/png', '.jpg':'image/jpeg', '.svg':'image/svg+xml',
  '.ico':'image/x-icon', '.woff2':'font/woff2', '.woff':'font/woff', '.ttf':'font/ttf'
};

function safePath(urlPath) {
  let decoded;
  try { decoded = decodeURIComponent(urlPath); } catch { return null; }
  const normalized = path.normalize(decoded).replace(/^([.][.][\\/])+/, '');
  const candidate = path.resolve(ROOT, `.${path.sep}${normalized.replace(/^[/\\]+/, '')}`);
  if (candidate !== ROOT && !candidate.startsWith(`${ROOT}${path.sep}`)) return null;
  return candidate;
}

const server = http.createServer((req,res)=>{
  const raw = String(req.url || '/').split('?')[0];
  let urlPath = raw || '/';
  if (urlPath === '/') urlPath = '/index.html';
  const filePath = safePath(urlPath);
  if (!filePath) {
    res.writeHead(403, {'Content-Type':'text/plain; charset=utf-8'});
    res.end('Forbidden');
    return;
  }
  fs.readFile(filePath,(err,data)=>{
    if(err){
      if(err.code==='ENOENT') res.writeHead(404, {'Content-Type':'text/plain; charset=utf-8'});
      else res.writeHead(500, {'Content-Type':'text/plain; charset=utf-8'});
      res.end(err.code==='ENOENT' ? 'Not Found' : 'Internal Server Error');
      return;
    }
    const mime=MIME[path.extname(filePath).toLowerCase()] || 'application/octet-stream';
    res.writeHead(200, {'Content-Type':mime,'Cache-Control':'no-store','X-Content-Type-Options':'nosniff','X-Frame-Options':'SAMEORIGIN','Referrer-Policy':'no-referrer'});
    res.end(data);
  });
});

server.listen(PORT,HOST,()=>{
  console.log(`\n  🤖 JARVIS AI Command Center`);
  console.log(`  HTTP Server: http://${HOST}:${PORT}`);
  console.log(`  Memory API:  http://127.0.0.1:8000`);
  console.log(`  Root:        ${ROOT}\n`);
});
server.on('error',(err)=>{ console.error('JARVIS server error:',err.message); process.exit(1); });
