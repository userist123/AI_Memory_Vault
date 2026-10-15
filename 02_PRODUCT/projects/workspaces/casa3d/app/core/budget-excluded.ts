// „Neincluse în total” din buget, ca listă curată pentru designer: fără dubluri (aceeași ușă de 4 ori → „×4”), grupată pe tip,
// cu etichete omenești (pereții N/S/E/V, finisajul „gloss” → „lucios”) și fără coduri hex brute: o culoare #rrggbb devine
// numele ei, iar interfața arată lângă el o pastilă cu culoarea. Intrarea sunt textele din core/boq.ts (unknownLines + unknownItems).
import { hexToRgb, rgbToHsl } from './color';

export type ExcludedKind = 'services' | 'labor' | 'kitchen' | 'bath' | 'lighting' | 'finishes' | 'products';
export interface ExcludedItem { key: string; label: string; count: number; color?: string; note?: 'paintYield' | 'onOrder' }
export interface ExcludedGroup { kind: ExcludedKind; items: ExcludedItem[] }
const ORDER: ExcludedKind[] = ['products', 'finishes', 'kitchen', 'bath', 'lighting', 'labor', 'services'];

const NAMED: [string, string][] = [['#f4f4f1', 'alb'], ['#ffffff', 'alb'], ['#222222', 'negru'], ['#000000', 'negru'], ['#cdb592', 'stejar deschis'], ['#b8936a', 'stejar'], ['#8d8a85', 'gri cald']];
/** Numele unei culori: din paleta aplicației, altfel o descriere după nuanță și luminozitate („gri deschis”, „verde închis”). */
export function colorName(hex: string): string {
  const h = hex.toLowerCase(), known = NAMED.find(([k]) => k === h); if (known) return known[1];
  const rgb = hexToRgb(h); if (!rgb) return 'culoare personalizată';
  const { h: hue, s, l } = rgbToHsl(rgb), shade = l > 0.8 ? ' deschis' : l < 0.3 ? ' închis' : '';
  if (l > 0.93) return 'alb'; if (l < 0.08) return 'negru';
  if (s < 0.15) return 'gri' + shade;
  const base = hue < 15 || hue >= 345 ? 'roșu' : hue < 45 ? (l < 0.5 ? 'maro' : 'bej') : hue < 70 ? 'galben' : hue < 170 ? 'verde' : hue < 200 ? 'turcoaz' : hue < 260 ? 'albastru' : hue < 300 ? 'mov' : 'roz';
  return base + (base === 'maro' || base === 'bej' ? '' : shade);
}
const SIDE: Record<string, string> = { N: 'nord', S: 'sud', E: 'est', W: 'vest' };
const CODES: Record<string, string> = { gloss: 'lucios', matt: 'mat', wood: 'lemn', bar: 'bară', knob: 'buton', profile: 'profil', none: 'fără',
  paint: 'vopsea', panel: 'lambriu', rail: 'baghetă', wallpaper: 'tapet', slats: 'riflaj', plaster: 'tencuială decorativă', brick: 'cărămidă aparentă', stone: 'piatră decorativă', tile: 'faianță' };

function humanize(raw: string): { label: string; color?: string; note?: ExcludedItem['note'] } {
  let color: string | undefined, note: ExcludedItem['note'];
  let s = raw.replace(/#[0-9a-fA-F]{6}\b/g, m => { color ??= m.toLowerCase(); return colorName(m); });
  s = s.replace(/\bperete ([NSEW])\b/g, (_, k) => `perete ${SIDE[k]}`).replace(/: (\w+) ([NSEW])$/, (_, k, d) => `: ${CODES[k] ?? k}, perete ${SIDE[d]}`);
  s = s.replace(/, (gloss|matt|wood),/g, (_, k) => `, ${CODES[k]},`).replace(/\((bar|knob|profile|none)\)/g, (_, k) => `(${CODES[k]})`);
  if (/randamentul nu e publicat/.test(s)){ note = 'paintYield'; s = s.replace(/: randamentul nu e publicat \([^)]*\)/, ''); }
  if (/\((ofertă|la comandă)\)/.test(s)){ note = 'onOrder'; s = s.replace(/ \((ofertă|la comandă)\)/, ''); }
  return { label: s.replace(/\s+/g, ' ').trim(), color, note };
}
function kindOf(s: string): ExcludedKind {
  if (/^(Transport|Montaj mobilier|Montaj bucătărie)/.test(s)) return 'services';
  if (/^(Manoperă|Montaj ușă)/.test(s)) return 'labor';
  if (/bucătărie/i.test(s)) return 'kitchen';
  if (/WC|Duș|walk-in/.test(s)) return 'bath';
  if (/produs neales|Punct electric|aplic/i.test(s)) return 'lighting';
  if (/^Vopsea|: (\w+ )?(baghetă|paint|panel|rail|wallpaper|slats|plaster|brick|stone|tile|vopsea|lambriu|tapet|riflaj)/i.test(s)) return 'finishes';
  return 'products';
}
/** Grupele, în ordinea de lucru a designerului; liniile identice se adună într-una singură cu numărul lor. */
export function excludedGroups(lines: string[], items: string[]): ExcludedGroup[] {
  const by = new Map<ExcludedKind, Map<string, ExcludedItem>>();
  for (const raw of [...lines, ...items]){ if (!raw || !raw.trim()) continue;
    const kind = kindOf(raw), h = humanize(raw), g = by.get(kind) ?? new Map<string, ExcludedItem>(); by.set(kind, g);
    const prev = g.get(h.label); if (prev) prev.count++; else g.set(h.label, { key: `${kind}:${h.label}`, label: h.label, count: 1, ...(h.color ? { color: h.color } : {}), ...(h.note ? { note: h.note } : {}) }); }
  return ORDER.filter(k => by.has(k)).map(kind => ({ kind, items: [...by.get(kind)!.values()] }));
}
