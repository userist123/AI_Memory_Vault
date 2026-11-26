// Captură deterministă cadru-cu-cadru a unei pagini three.js -> PNG-uri.
//   node web_capture.mjs --dir <folder_proiect> --page scene.html --out <folder_cadre_abs>
//        [--fps 30] [--seconds 4] [--w 1920] [--h 1080] [--frames 0,30,60] [--alpha]
// Pagina trebuie să expună window.renderAt(t) și window.ready (vezi web_template.html).
// Playwright se ia din folderul proiectului (npm i playwright three) sau global.
// Browser: Edge de sistem (channel msedge) - build-ul Chromium al Playwright poate lipsi pe acest PC.
import fs from 'fs';
import path from 'path';
import http from 'http';
import { createRequire } from 'module';

const args = Object.fromEntries(process.argv.slice(2).reduce((acc, a, i, arr) => {
  if (a.startsWith('--')) acc.push([a.slice(2), arr[i + 1] && !arr[i + 1].startsWith('--') ? arr[i + 1] : true]);
  return acc;
}, []));
const dir = path.resolve(args.dir || process.cwd());
const page = args.page || 'scene.html';
const out = path.resolve(args.out || path.join(dir, 'frames'));
const fps = +(args.fps || 30), seconds = +(args.seconds || 4);
const W = +(args.w || 1920), H = +(args.h || 1080);
const total = Math.round(fps * seconds);
const only = args.frames && args.frames !== true ? String(args.frames).split(',').map(Number) : null;

const require = createRequire(path.join(dir, 'package.json'));
let chromium;
try { ({ chromium } = require('playwright')); } catch { ({ chromium } = await import('playwright')); }

fs.mkdirSync(out, { recursive: true });
const types = { '.html': 'text/html', '.js': 'text/javascript', '.mjs': 'text/javascript', '.json': 'application/json',
  '.png': 'image/png', '.jpg': 'image/jpeg', '.jpeg': 'image/jpeg', '.glb': 'model/gltf-binary', '.gltf': 'model/gltf+json',
  '.hdr': 'application/octet-stream', '.wasm': 'application/wasm', '.ttf': 'font/ttf', '.woff2': 'font/woff2' };
const srv = http.createServer((q, s) => {
  if (q.url.startsWith('/favicon.ico')) { s.writeHead(204); return s.end(); }
  const f = path.join(dir, decodeURIComponent(q.url.split('?')[0]));
  if (!f.startsWith(dir) || !fs.existsSync(f) || fs.statSync(f).isDirectory()) { s.writeHead(404); return s.end(); }
  s.writeHead(200, { 'Content-Type': types[path.extname(f).toLowerCase()] || 'application/octet-stream' });
  fs.createReadStream(f).pipe(s);
});
await new Promise(r => srv.listen(0, '127.0.0.1', r));
const port = srv.address().port;

let browser;
try {
  browser = await chromium.launch({ channel: 'msedge', args: ['--use-angle=d3d11', '--enable-gpu', '--ignore-gpu-blocklist'] });
} catch {
  browser = await chromium.launch({ args: ['--use-angle=d3d11', '--enable-gpu', '--ignore-gpu-blocklist'] });
}
const ctx = await browser.newContext({ viewport: { width: W, height: H }, deviceScaleFactor: 1 });
const pg = await ctx.newPage();
const errors = [];
pg.on('pageerror', e => errors.push(String(e)));
pg.on('console', m => { if (m.type() === 'error') errors.push(m.text()); });
const qs = `w=${W}&h=${H}${args.alpha ? '&alpha=1' : ''}`;
await pg.goto(`http://127.0.0.1:${port}/${page}?${qs}`);
try {
  await pg.waitForFunction('window.ready === true', null, { timeout: 60000 });
} catch (e) {
  console.error('PAGINA NU E GATA (window.ready). Erori:', errors);
  await browser.close(); srv.close(); process.exit(2);
}
const gl = await pg.evaluate(() => {
  const c = document.querySelector('canvas'); const g = c.getContext('webgl2') || c.getContext('webgl');
  const d = g && g.getExtension('WEBGL_debug_renderer_info');
  return d ? g.getParameter(d.UNMASKED_RENDERER_WEBGL) : 'necunoscut';
});

const t0 = Date.now();
const list = only || [...Array(total).keys()];
for (const i of list) {
  const data = await pg.evaluate(t => { window.renderAt(t); return document.querySelector('canvas').toDataURL('image/png'); }, i / fps);
  fs.writeFileSync(path.join(out, `f_${String(i + 1).padStart(4, '0')}.png`), Buffer.from(data.split(',')[1], 'base64'));
}
await browser.close(); srv.close();
console.log('WEB_CAPTURE_DONE', JSON.stringify({ out, frames: list.length, fps, size: [W, H], gpu: gl,
  seconds: (Date.now() - t0) / 1000, errors }));
if (errors.length) process.exit(3);
