// Verificarea automată a planșelor de prezentare (UI/UX P1-2): niciun conținut tăiat sau ieșit din planșă, la 16:9
// (planșa are overflow:hidden, deci un text prea lung dispare fără urmă) și pe telefon (390 px, fără scroll orizontal).
// Fără dependențe: Chrome headless prin CDP, cu WebSocket-ul din Node. Serverul trebuie să ruleze deja:
//   node scripts/check-slides.mjs [http://127.0.0.1:3370]      (CHROME_PATH=… dacă Chrome nu e în locul obișnuit)
// Creează un proiect de test cu nume lungi (proiect, camere), salvează o revizie, deschide linkul clientului și verifică
// fiecare tip de planșă la fiecare dimensiune. Iese cu 1 la orice problemă; capturile merg în SHOTS_DIR, dacă e setat.
import { mkdirSync, writeFileSync } from 'node:fs';
import { join } from 'node:path';
import { launch, page, makeShare, sleep } from './lib/cdp.mjs';

const BASE = (process.argv[2] || 'http://127.0.0.1:3370').replace(/\/$/, '');
const VIEWPORTS = [
  { name: 'telefon-390', width: 390, height: 844, mobile: true, scale: 3 },
  { name: '16x9-1024', width: 1024, height: 576 },
  { name: '16x9-1280', width: 1280, height: 720 },
  { name: '16x9-1920', width: 1920, height: 1080 },
];
const TYPES = ['cover', 'pboard', 'room', 'planslide', 'budgetslide'];
const LONG_PROJECT = 'Apartament cu trei camere și terasă, strada Fântânele de Sus nr. 143, bloc C2, scara 4';
const LONG_ROOMS = ['Dormitorul matrimonial cu dressing integrat și baie proprie', 'Bucătăria deschisă spre sufragerie, cu insulă centrală'];

// în pagină: pentru fiecare planșă, elementele vizibile care ies din ea (16:9) sau din lățimea ecranului (telefon)
function audit(types){
  const out = { hOverflow: document.documentElement.scrollWidth > innerWidth + 1, seen: [], problems: [] };
  const name = el => el.tagName.toLowerCase() + (el.className && typeof el.className === 'string' ? '.' + el.className.trim().split(/\s+/).join('.') : '');
  const slides = [...document.querySelectorAll('.slide')], phone = innerWidth <= 700;
  for (const sl of slides){
    const type = types.find(t => sl.classList.contains(t)) || 'altul'; if (!out.seen.includes(type)) out.seen.push(type);
    const s = sl.getBoundingClientRect();
    for (const el of sl.querySelectorAll('*')){
      const cs = getComputedStyle(el); if (cs.display === 'none' || cs.visibility === 'hidden' || cs.position === 'fixed') continue;
      if (el.closest('svg') && el.tagName.toLowerCase() !== 'svg') continue; // conținutul SVG se scalează cu viewBox
      const r = el.getBoundingClientRect(); if (r.width < 1 || r.height < 1) continue;
      const out1 = phone ? (r.left < -1 || r.right > innerWidth + 1) : (r.left < s.left - 1 || r.right > s.right + 1 || r.top < s.top - 1 || r.bottom > s.bottom + 1);
      // text tăiat în interiorul unui element cu overflow ascuns (fără trunchiere voită: puncte de suspensie, line-clamp)
      const clipped = (cs.overflow === 'hidden' || cs.overflowX === 'hidden') && cs.textOverflow !== 'ellipsis' && cs.webkitLineClamp === 'none' && el.children.length === 0 && el.textContent.trim()
        && (el.scrollWidth > el.clientWidth + 1 || el.scrollHeight > el.clientHeight + 2);
      if (out1 || clipped) out.problems.push({ type, el: name(el), text: (el.textContent || '').trim().slice(0, 50), why: out1 ? 'iese din planșă' : 'text tăiat',
        box: [Math.round(r.left - s.left), Math.round(r.top - s.top), Math.round(r.width), Math.round(r.height)], slide: [Math.round(s.width), Math.round(s.height)] });
    }
  }
  // doar elementul cel mai de sus din fiecare lanț (copiii unui element ieșit ies și ei)
  out.problems = out.problems.filter((p, i, a) => a.findIndex(q => q.type === p.type && q.text === p.text && q.why === p.why) === i).slice(0, 40);
  return out;
}

const cdp = await launch(); let failed = false;
try {
  const p = await page(cdp), { token } = await makeShare(p, BASE, LONG_PROJECT, LONG_ROOMS);
  console.log(`link: ${BASE}/share/${token}`);
  for (const v of VIEWPORTS){
    await p.s('Emulation.setDeviceMetricsOverride', { width: v.width, height: v.height, deviceScaleFactor: v.scale ?? 1, mobile: !!v.mobile });
    await p.go(`${BASE}/share/${token}`);
    for (let i = 0; i < 100 && !(await p.evaluate(() => document.querySelectorAll('.slide').length > 3)); i++) await sleep(150);
    await p.evaluate(() => document.fonts.ready.then(() => true)); await sleep(400);
    const r = await p.evaluate(audit, TYPES), missing = TYPES.filter(t => !r.seen.includes(t));
    const ok = !r.hOverflow && !r.problems.length && !missing.length; failed ||= !ok;
    console.log(`${ok ? 'OK  ' : 'FAIL'} ${v.name}: ${r.seen.length} tipuri de planșe${r.hOverflow ? ', scroll orizontal' : ''}${missing.length ? `, lipsesc: ${missing.join(', ')}` : ''}`);
    for (const x of r.problems) console.log(`     ${x.type} · ${x.why} · ${x.el} „${x.text}” la ${x.box.join(',')} în ${x.slide.join('×')}`);
    if (process.env.SHOTS_DIR){ mkdirSync(process.env.SHOTS_DIR, { recursive: true });
      const { data } = await p.s('Page.captureScreenshot', { format: 'png', captureBeyondViewport: true, clip: { x: 0, y: 0, width: v.width, height: Math.min(await p.evaluate(() => document.documentElement.scrollHeight), 16000), scale: 1 } });
      writeFileSync(join(process.env.SHOTS_DIR, `slides-${v.name}.png`), Buffer.from(data, 'base64')); }
  }
} catch (e){ console.error(e); failed = true; }
finally { cdp.close(); }
process.exit(failed ? 1 : 0);
