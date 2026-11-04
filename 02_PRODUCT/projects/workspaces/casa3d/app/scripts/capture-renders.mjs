// Imaginile 3D de referință pentru upgrade-ul three.js (UI/UX P3-15): același proiect demo, aceeași fereastră, aceleași
// imagini de prezentare (vederea de ansamblu și fiecare cameră, motorul 3D real în Chrome). Se rulează înainte și după
// fiecare pas de upgrade, apoi scripts/compare-renders.mjs arată cât s-a schimbat fiecare imagine.
//   node scripts/capture-renders.mjs http://127.0.0.1:3370 D:/w/renders/r128
import { mkdirSync, writeFileSync } from 'node:fs';
import { join } from 'node:path';
import { launch, page, sleep } from './lib/cdp.mjs';

const BASE = (process.argv[2] || 'http://127.0.0.1:3370').replace(/\/$/, ''), OUT = process.argv[3] || 'renders';
mkdirSync(OUT, { recursive: true });
const cdp = await launch(); let failed = false;
try {
  const p = await page(cdp);
  await p.s('Emulation.setDeviceMetricsOverride', { width: 1600, height: 1000, deviceScaleFactor: 1, mobile: false });
  await p.go(`${BASE}/`); await sleep(1500);
  const id = await p.evaluate(async () => { await fetch('/api/projects'); const r = await fetch('/api/projects', { method: 'POST', headers: { 'content-type': 'application/json' }, body: JSON.stringify({ name: 'Referință randări', template: 'demo', lang: 'ro' }) }); return (await r.json()).id; });
  await p.go(`${BASE}/p/${id}/present`);
  // fiecare planșă cere imaginea când intră în ecran: le trecem pe toate, apoi așteptăm să fie gata
  const n = await p.evaluate(async () => { for (let i = 0; i < 100 && !document.querySelector('.slide'); i++) await new Promise(r => setTimeout(r, 100));
    for (const s of document.querySelectorAll('.slide')){ s.scrollIntoView(); await new Promise(r => setTimeout(r, 250)); } return document.querySelectorAll('[data-shot]').length; });
  const shots = await p.evaluate(async n => { for (let i = 0; i < 240; i++){ const imgs = [...document.querySelectorAll('[data-shot] img[src^="data:image"]')]; if (imgs.length >= n) break; await new Promise(r => setTimeout(r, 500)); }
    return [...document.querySelectorAll('[data-shot]')].map(el => ({ key: el.getAttribute('data-shot'), src: el.querySelector('img')?.getAttribute('src') || '' })); }, n);
  for (const s of shots){ if (!s.src.startsWith('data:image/png;base64,')){ console.log(`FAIL ${s.key}: fără imagine`); failed = true; continue; }
    writeFileSync(join(OUT, `${s.key}.png`), Buffer.from(s.src.slice('data:image/png;base64,'.length), 'base64')); console.log(`OK   ${s.key}.png`); }
} catch (e){ console.error(e); failed = true; }
finally { cdp.close(); }
process.exit(failed ? 1 : 0);
