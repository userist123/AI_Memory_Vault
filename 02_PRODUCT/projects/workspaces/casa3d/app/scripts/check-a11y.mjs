// Verificarea automată de accesibilitate (UI/UX P3-17): axe-core (reguli WCAG 2.0/2.1/2.2, nivel A și AA) pe paginile
// reale ale aplicației, în Chrome headless prin CDP: pagina principală, editorul în mai multe etape, linkul clientului
// (desktop și telefon) și documentele de tipărit. Serverul trebuie să ruleze deja:
//   node scripts/check-a11y.mjs [http://127.0.0.1:3370]
// Iese cu 1 la orice încălcare care nu e în scripts/a11y-known.json (fiecare excepție are motivul ei scris).
import { readFileSync } from 'node:fs';
import { createRequire } from 'node:module';
import { launch, page, makeShare, sleep } from './lib/cdp.mjs';

const BASE = (process.argv[2] || 'http://127.0.0.1:3370').replace(/\/$/, '');
const AXE = readFileSync(createRequire(import.meta.url).resolve('axe-core/axe.min.js'), 'utf8');
const KNOWN = JSON.parse(readFileSync(new URL('./a11y-known.json', import.meta.url), 'utf8'));
const TAGS = ['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa', 'wcag22aa'];
const DESKTOP = { width: 1600, height: 900, mobile: false, scale: 1 }, PHONE = { width: 390, height: 844, mobile: true, scale: 3 };

async function audit(p, name){
  await p.evaluate(() => document.fonts.ready.then(() => true)); await sleep(600);
  if (!(await p.evaluate(() => typeof window.axe === 'object'))) await p.script(AXE);
  const v = await p.evaluate(async tags => { const r = await window.axe.run(document, { runOnly: { type: 'tag', values: tags }, resultTypes: ['violations'] });
    return r.violations.map(x => ({ id: x.id, impact: x.impact, help: x.help, n: x.nodes.length, nodes: x.nodes.slice(0, 3).map(n => n.target.join(' ')) })); }, TAGS);
  const known = KNOWN[name] || {}, bad = v.filter(x => !known[x.id]), allowed = v.filter(x => known[x.id]);
  console.log(`${bad.length ? 'FAIL' : 'OK  '} ${name}: ${v.length} reguli încălcate${allowed.length ? ` (${allowed.length} cunoscute: ${allowed.map(x => x.id).join(', ')})` : ''}`);
  for (const x of bad) console.log(`     ${x.impact} · ${x.id} · ${x.help} · ${x.n} elemente · ${x.nodes.join(' | ')}`);
  return bad.length;
}
const view = (p, v) => p.s('Emulation.setDeviceMetricsOverride', { width: v.width, height: v.height, deviceScaleFactor: v.scale, mobile: v.mobile });

const cdp = await launch(); let failures = 0;
try {
  const p = await page(cdp); await view(p, DESKTOP);
  const { id, token } = await makeShare(p, BASE);
  await p.go(`${BASE}/`); failures += await audit(p, 'home');
  await p.go(`${BASE}/accesibilitate`); failures += await audit(p, 'declaratie');
  // un element care lipsește (planul, o etapă, lista) e un eșec, nu o stare sărită: altfel CI ar rămâne verde verificând mai puțin
  const need = (ok, what) => { if (!ok){ console.log(`FAIL ${what}: elementul nu există`); failures++; } return ok; };
  await p.go(`${BASE}/p/${id}`); need(await p.evaluate(async () => { for (let i = 0; i < 100 && !document.querySelector('svg.plan'); i++) await new Promise(r => setTimeout(r, 100)); return !!document.querySelector('svg.plan'); }), 'editor (svg.plan)');
  failures += await audit(p, 'editor-releveu');
  // lista obiectelor planului deschisă (primește focus; UI/UX P3-16)
  need(await p.evaluate(async () => { const b = document.querySelector('.plan-mirror button[tabindex="0"]'); b?.focus(); await new Promise(r => setTimeout(r, 300)); return !!b && document.activeElement === b; }), 'lista obiectelor planului');
  failures += await audit(p, 'editor-lista-obiecte');
  await p.evaluate(() => { document.activeElement?.blur(); return true; });
  for (const st of ['finishes', 'ffe', 'quote', 'present', 'orders']){
    const ok = await p.evaluate(async st => { const b = document.querySelector(`.stagebar [data-stage="${st}"]`); if (!b) return false; b.click(); await new Promise(r => setTimeout(r, 900)); return true; }, st);
    if (need(ok, `etapa ${st}`)) failures += await audit(p, `editor-${st}`);
  }
  // paleta Ctrl+K deschisă (dialog modal)
  await p.evaluate(async () => { document.dispatchEvent(new KeyboardEvent('keydown', { key: 'k', ctrlKey: true, bubbles: true })); await new Promise(r => setTimeout(r, 500)); return true; });
  failures += await audit(p, 'editor-ctrlk');
  for (const doc of ['print', 'offer', 'contract', 'proforma', 'handover', 'site-report', 'board']){ await p.go(`${BASE}/p/${id}/${doc}`); await sleep(1200); failures += await audit(p, `doc-${doc}`); }
  await p.go(`${BASE}/share/${token}`); await sleep(1500); failures += await audit(p, 'link-desktop');
  await view(p, PHONE); await p.go(`${BASE}/share/${token}`); await sleep(1500); failures += await audit(p, 'link-telefon');
} catch (e){ console.error(e); failures++; }
finally { cdp.close(); }
process.exit(failures ? 1 : 0);
