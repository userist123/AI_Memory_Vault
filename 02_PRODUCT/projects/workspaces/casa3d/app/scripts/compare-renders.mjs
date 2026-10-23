// Compară două seturi de imagini 3D (UI/UX P3-15): pentru fiecare imagine cu același nume, luminozitatea medie, culoarea
// medie (R, G, B) și ce procent din pixeli diferă vizibil (ΔE aproximativ > 6 în RGB). Decodarea se face în Chrome
// (fără dependențe). Ieșire: un tabel; codul de ieșire nu depinde de rezultat (e un instrument de calibrare, nu o poartă).
//   node scripts/compare-renders.mjs D:/w/renders/r128 D:/w/renders/r186
import { readdirSync, readFileSync } from 'node:fs';
import { join } from 'node:path';
import { launch, page } from './lib/cdp.mjs';

const [A, B] = process.argv.slice(2); if (!A || !B){ console.error('Folosire: compare-renders.mjs dirA dirB'); process.exit(2); }
const names = readdirSync(A).filter(f => f.endsWith('.png') && readdirSync(B).includes(f));
const cdp = await launch();
try {
  const p = await page(cdp); await p.go('about:blank');
  console.log('imagine'.padEnd(16), 'lum A', 'lum B', '  ΔL', '  RGB A        ', 'RGB B        ', 'pixeli diferiți');
  for (const f of names){
    const a = 'data:image/png;base64,' + readFileSync(join(A, f)).toString('base64'), b = 'data:image/png;base64,' + readFileSync(join(B, f)).toString('base64');
    const r = await p.evaluate(async (a, b) => {
      const load = src => new Promise((ok, ko) => { const i = new Image(); i.onload = () => ok(i); i.onerror = ko; i.src = src; });
      const [ia, ib] = await Promise.all([load(a), load(b)]), w = Math.min(ia.width, ib.width), h = Math.min(ia.height, ib.height);
      const px = img => { const c = document.createElement('canvas'); c.width = w; c.height = h; const g = c.getContext('2d'); g.drawImage(img, 0, 0, w, h); return g.getImageData(0, 0, w, h).data; };
      const da = px(ia), db = px(ib); let sa = [0, 0, 0], sb = [0, 0, 0], diff = 0; const n = w * h;
      for (let k = 0; k < da.length; k += 4){ for (let c = 0; c < 3; c++){ sa[c] += da[k + c]; sb[c] += db[k + c]; }
        const d = Math.hypot(da[k] - db[k], da[k + 1] - db[k + 1], da[k + 2] - db[k + 2]); if (d > 15) diff++; }
      const m = s => s.map(v => Math.round(v / n)), L = ([r, g, b]) => Math.round(.2126 * r + .7152 * g + .0722 * b);
      return { a: m(sa), b: m(sb), la: L(m(sa)), lb: L(m(sb)), diff: +(diff / n * 100).toFixed(1) };
    }, a, b);
    console.log(f.replace('.png', '').padEnd(16), String(r.la).padStart(5), String(r.lb).padStart(5), String(r.lb - r.la).padStart(5), ' ', r.a.join(',').padEnd(13), r.b.join(',').padEnd(13), `${r.diff}%`);
  }
} finally { cdp.close(); }
