// Chrome headless prin Chrome DevTools Protocol, fără dependențe (WebSocket-ul din Node): folosit de verificările
// automate din scripts/ (planșe, accesibilitate). CHROME_PATH=… dacă Chrome nu e într-un loc obișnuit.
import { spawn } from 'node:child_process';
import { existsSync, mkdtempSync, readFileSync, rmSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';

export const sleep = ms => new Promise(r => setTimeout(r, ms));
export function chromePath(){
  const c = [process.env.CHROME_PATH, 'C:/Program Files/Google/Chrome/Application/chrome.exe', 'C:/Program Files (x86)/Google/Chrome/Application/chrome.exe',
    '/opt/pw-browsers/chromium', '/usr/bin/google-chrome', '/usr/bin/google-chrome-stable', '/usr/bin/chromium', '/usr/bin/chromium-browser', '/Applications/Google Chrome.app/Contents/MacOS/Google Chrome'];
  const p = c.find(x => x && existsSync(x)); if (!p) throw new Error('Chrome negăsit; setați CHROME_PATH.'); return p;
}
export async function launch(){
  const dir = mkdtempSync(join(tmpdir(), 'casa3d-cdp-'));
  const args = ['--headless=new', '--remote-debugging-port=0', `--user-data-dir=${dir}`, '--no-first-run', '--no-default-browser-check', '--hide-scrollbars', '--lang=ro-RO'];   // româna: limba principală a clienților (pe link, prima randare urmează Accept-Language)
  if (process.platform === 'linux') args.push('--no-sandbox');   // runner-ele CI rulează fără user namespaces
  const proc = spawn(chromePath(), [...args, 'about:blank'], { stdio: 'ignore' });
  const portFile = join(dir, 'DevToolsActivePort');
  for (let i = 0; i < 150 && !existsSync(portFile); i++) await sleep(100);
  const [port, path] = readFileSync(portFile, 'utf8').trim().split('\n');
  const ws = new WebSocket(`ws://127.0.0.1:${port}${path}`); await new Promise((ok, ko) => { ws.onopen = ok; ws.onerror = ko; });
  let seq = 0; const waiting = new Map();
  ws.onmessage = m => { const d = JSON.parse(m.data); if (d.id && waiting.has(d.id)){ const w = waiting.get(d.id); waiting.delete(d.id); d.error ? w.ko(new Error(d.error.message)) : w.ok(d.result); } };
  const send = (method, params = {}, sessionId) => new Promise((ok, ko) => { const id = ++seq; waiting.set(id, { ok, ko }); ws.send(JSON.stringify({ id, method, params, sessionId })); });
  const close = () => { try { ws.close(); } catch {} proc.kill(); setTimeout(() => { try { rmSync(dir, { recursive: true, force: true }); } catch {} }, 500); };
  return { send, close };
}
export async function page(cdp){
  const { targetId } = await cdp.send('Target.createTarget', { url: 'about:blank' });
  const { sessionId } = await cdp.send('Target.attachToTarget', { targetId, flatten: true });
  const s = (m, p) => cdp.send(m, p, sessionId);
  await s('Page.enable'); await s('Runtime.enable');
  const evaluate = async (fn, ...args) => { const r = await s('Runtime.evaluate', { expression: `(${fn})(...${JSON.stringify(args)})`, awaitPromise: true, returnByValue: true });
    if (r.exceptionDetails) throw new Error(r.exceptionDetails.exception?.description || r.exceptionDetails.text); return r.result.value; };
  const script = async src => { const r = await s('Runtime.evaluate', { expression: src }); if (r.exceptionDetails) throw new Error(r.exceptionDetails.text); };
  const go = async url => { await s('Page.navigate', { url }); for (let i = 0; i < 200; i++){ await sleep(100); if (await evaluate(() => document.readyState === 'complete').catch(() => false)) return; } };
  return { s, evaluate, script, go };
}
/** În pagină (aceeași origine, cookie-ul de proprietar): proiect demo → revizie → link pentru client. */
export async function makeShare(p, base, name = 'Verificare automată', roomNames = []){
  await p.go(`${base}/`);
  return p.evaluate(async (name, rooms) => {
    const j = async (url, method = 'GET', body) => { const r = await fetch(url, { method, headers: { 'content-type': 'application/json' }, body: body && JSON.stringify(body) });
      const d = await r.json().catch(() => null); if (!r.ok) throw new Error(`${method} ${url}: ${r.status} ${JSON.stringify(d)}`); return d; };
    const { id } = await j('/api/projects', 'POST', { name, template: 'demo', lang: 'ro' });
    const pr = await j(`/api/projects/${id}`), snap = pr.draft; snap.name = name;
    snap.floor.rooms.slice(0, rooms.length).forEach((r, i) => { r.name = rooms[i]; });
    await j(`/api/projects/${id}`, 'PUT', { snapshot: snap, base: pr.draftRev });
    const rev = await j(`/api/projects/${id}/revisions`, 'POST', { note: 'verificare automată' });
    const sh = await j(`/api/projects/${id}/shares`, 'POST', { revision: rev.number });
    return { id, token: sh.token };
  }, name, roomNames);
}
