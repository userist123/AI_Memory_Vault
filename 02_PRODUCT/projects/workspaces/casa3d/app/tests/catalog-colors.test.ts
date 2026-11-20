// Precizia culorilor în randarea 3D: culoarea și materialul cu care motorul desenează o variantă (style.col, style.wood,
// style.top la mese) corespund culorii din numele produsului de pe pagina magazinului („negru”, „antracit”, „stejar sonoma”).
import { test } from 'vitest';
import assert from 'node:assert/strict';
import catalogJson from '../data/catalog.v1.json';
import type { Catalog } from '../core/types';

const cat = catalogJson as unknown as Catalog;
type Rgb = { l: number; s: number; r: number; g: number; b: number };
const rgb = (h: string): Rgb => { const c = h.replace('#', ''), [r, g, b] = [0, 2, 4].map(i => parseInt(c.slice(i, i + 2), 16) / 255) as [number, number, number];
  return { l: .2126 * r + .7152 * g + .0722 * b, s: Math.max(r, g, b) - Math.min(r, g, b), r, g, b }; };
// produsele numesc una sau mai multe culori („antracit/alb”, „negru + stejar”); culoarea desenată trebuie să fie una dintre ele.
// Verificarea e largă, pe luminozitate și nuanță.
const WORDS: [RegExp, (c: Rgb) => boolean][] = [
  [/\balb(ă|e)?\b/, c => c.l > .7 && c.s < .2], [/\bnegr(u|e|ă)\b/, c => c.l < .25], [/\bantracit\b/, c => c.l < .35 && c.s < .15],
  [/\bgri\b/, c => c.s < .15], [/\bbej\b/, c => c.l > .45 && c.r >= c.b], [/\bstejar\b/, c => c.r > c.b && c.l > .25],
  [/\bnuc\b/, c => c.r > c.b && c.l < .5], [/\bmaro\b/, c => c.r > c.b && c.l < .65], [/\bverde\b/, c => c.g >= c.r && c.g >= c.b],
  [/\balbastr/, c => c.b >= c.r], [/\bauri(u|e)\b|\balam/, c => c.r > c.b + .15], [/\bturcoaz\b/, c => c.g > c.r && c.b > c.r],
  [/\b(natur|frasin)\b/, c => c.r > c.b && c.l > .3], [/\binox\b/, c => c.s < .12 && c.l > .35 && c.l < .85]];
// grupele în care motorul ia culoarea piesei din style.col (fără ea pune o culoare implicită a modelului)
const COL_GROUPS = new Set(['birou', 'scaunBirou', 'oglinda', 'comoda', 'comodaTv', 'noptiera', 'dulap', 'biblioteca', 'pantofar', 'pat', 'canapea', 'fotoliu', 'masuta']);
const groupOf = (id: string) => id.replace(/-\d+$/, '');

test('culoarea desenată e una dintre culorile din numele produsului', () => {
  const bad: string[] = [];
  for (const v of cat.variants){ const st = (v.style ?? {}) as Record<string, unknown>, col = (st.col ?? st.top) as string | undefined;
    // „schimbător de căldură inox” descrie interiorul centralei, nu culoarea carcasei
    const nm = v.name.toLowerCase().replace(/schimb[aă]tor[^,]*/g, ''), named = WORDS.filter(([re]) => re.test(nm));
    if (!named.length) continue;
    if (!col){ if (COL_GROUPS.has(groupOf(v.id))) bad.push(`${v.id} fără culoare: ${v.name}`); continue; }
    if (/^#[0-9a-f]{6}$/i.test(col) && !named.some(([, ok]) => ok(rgb(col)))) bad.push(`${v.id} ${col}: ${v.name}`); }
  assert.deepEqual(bad, []);
});
test('produsele din lemn (stejar, nuc, wenge) se desenează cu fibra lemnului, cele vopsite albe nu', () => {
  const bad: string[] = [];
  for (const v of cat.variants){ const st = (v.style ?? {}) as Record<string, unknown>, g = groupOf(v.id), nm = v.name.toLowerCase();
    if (!COL_GROUPS.has(g) || g === 'masuta' || g === 'fotoliu' || g === 'canapea') continue;   // măsuțele și tapițeria au desenul lor de lemn
    const woodWord = /\b(stejar|nuc|wenge|sonoma|cannolo|artisan)\b/.test(nm) && !/metalic/.test(nm),   // patul metalic are doar detalii din lemn
          whitePaint = /\balb(ă)?\b/.test(nm) && !woodWord;
    if (woodWord && st.col && !st.wood) bad.push(`${v.id} fără lemn: ${v.name}`);
    if (whitePaint && st.wood && /^#f/i.test(String(st.col ?? ''))) bad.push(`${v.id} alb vopsit desenat ca lemn: ${v.name}`); }
  assert.deepEqual(bad, []);
});
