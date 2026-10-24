// Dulap pe comandă din PAL (corpul și ușile), cu listă de debitare pentru firmele de debitare din România: piesele (lungime ×
// lățime în mm, bucăți, cant pe fiecare latură, fibra pe lungime), plăcile necesare, metrii de cant, feroneria.
// Construcție uzuală (de verificat cu atelierul): laterale pe toată înălțimea, blat și bază între laterale, soclu retras,
// separatoare între blat și bază, spate HDF 3 mm prins în spate, rafturi retrase 20 mm de la față.
export const BOARD_MM = 18, BACK_MM = 3, SHEET = { l: 2800, w: 2070 }, SHEET_WASTE = 0.15;
export type SectionKind = 'hang' | 'shelves' | 'hangShelf' | 'drawers';
export interface WardrobeSection { kind: SectionKind; shelves?: number; drawers?: number }
export interface WardrobeSpec { sections: WardrobeSection[]; doors: 'hinged' | 'sliding' | 'none'; plinthMm: number; decor?: string; edge: 'abs04' | 'abs2' }
export interface CutPart { name: string; l: number; w: number; qty: number; edges: [0 | 1, 0 | 1, 0 | 1, 0 | 1]; material: 'pal' | 'hdf'; grain: boolean }
export interface Hardware { name: string; qty: number; unit: string }
export class JoineryError extends Error {}

export const DEFAULT_WARDROBE: WardrobeSpec = { sections: [{ kind: 'hang' }, { kind: 'shelves', shelves: 4 }], doors: 'hinged', plinthMm: 80, edge: 'abs2' };
export function checkWardrobe(x: unknown): WardrobeSpec {
  const o = x as any; if (!o || typeof o !== 'object' || !Array.isArray(o.sections) || o.sections.length < 1 || o.sections.length > 8) throw new JoineryError('Dulapul are între 1 și 8 corpuri.');
  const sections: WardrobeSection[] = o.sections.map((s: any) => { if (!s || !['hang', 'shelves', 'hangShelf', 'drawers'].includes(s.kind)) throw new JoineryError('Tip de corp necunoscut.');
    const out: WardrobeSection = { kind: s.kind };
    if (s.kind === 'shelves' || s.kind === 'hangShelf'){ const n = s.shelves ?? (s.kind === 'shelves' ? 4 : 1); if (!Number.isInteger(n) || n < 0 || n > 12) throw new JoineryError('Rafturi: 0–12.'); out.shelves = n; }
    if (s.kind === 'drawers'){ const n = s.drawers ?? 3; if (!Number.isInteger(n) || n < 1 || n > 8) throw new JoineryError('Sertare: 1–8.'); out.drawers = n; }
    return out; });
  if (!['hinged', 'sliding', 'none'].includes(o.doors)) throw new JoineryError('Uși: batante, glisante sau fără.');
  if (!Number.isInteger(o.plinthMm) || o.plinthMm < 0 || o.plinthMm > 150) throw new JoineryError('Soclul are 0–150 mm.');
  const decor = typeof o.decor === 'string' ? o.decor.replace(/[\u0000-\u001f\u007f]/g, '').trim().slice(0, 80) : undefined;
  return { sections, doors: o.doors, plinthMm: o.plinthMm, edge: o.edge === 'abs04' ? 'abs04' : 'abs2', ...(decor ? { decor } : {}) };
}
const hingesFor = (h: number) => h <= 1000 ? 2 : h <= 1600 ? 3 : h <= 2000 ? 4 : 5;
const merge = (parts: CutPart[]) => { const m = new Map<string, CutPart>(); for (const p of parts){ const k = `${p.name}|${p.l}|${p.w}|${p.edges.join('')}|${p.material}`; const c = m.get(k); if (c) c.qty += p.qty; else m.set(k, { ...p }); } return [...m.values()]; };

/** Lista de debitare pentru un dulap de W × H × D (mm). */
export function wardrobeCutList(W: number, H: number, D: number, spec: WardrobeSpec){
  const t = BOARD_MM, n = spec.sections.length, P = spec.plinthMm, parts: CutPart[] = [], hw: Hardware[] = [];
  const innerH = H - P - 2 * t, innerW = (W - 2 * t - (n - 1) * t) / n, shelfD = D - 20 - BACK_MM, R = (v: number) => Math.round(v);
  if (innerW < 150 || innerH < 300) throw new JoineryError('Dulapul e prea mic pentru câte corpuri are.');
  parts.push({ name: 'Lateral', l: R(H), w: R(D), qty: 2, edges: [1, 0, 0, 0], material: 'pal', grain: true });
  parts.push({ name: 'Blat', l: R(W - 2 * t), w: R(D), qty: 1, edges: [1, 0, 0, 0], material: 'pal', grain: true });
  parts.push({ name: 'Bază', l: R(W - 2 * t), w: R(D), qty: 1, edges: [1, 0, 0, 0], material: 'pal', grain: true });
  if (n > 1) parts.push({ name: 'Separator', l: R(innerH), w: R(D - BACK_MM), qty: n - 1, edges: [1, 0, 0, 0], material: 'pal', grain: true });
  if (P > 0) parts.push({ name: 'Soclu', l: R(W - 2 * t), w: R(P), qty: 1, edges: [1, 0, 0, 0], material: 'pal', grain: true });
  parts.push({ name: 'Spate HDF', l: R(H - P - 2), w: R(W - 2), qty: 1, edges: [0, 0, 0, 0], material: 'hdf', grain: false });
  let rails = 0, shelfPins = 0, drawers = 0;
  for (const s of spec.sections){
    const shelves = s.kind === 'hang' ? 1 : s.shelves ?? 0;   // la „haine lungi” un raft sus, peste bară
    if (shelves > 0) parts.push({ name: 'Raft', l: R(innerW - 2), w: R(shelfD), qty: shelves, edges: [1, 0, 0, 0], material: 'pal', grain: true });
    shelfPins += shelves * 4; if (s.kind === 'hang' || s.kind === 'hangShelf') rails++;
    if (s.kind === 'drawers'){ const k = s.drawers ?? 3, fh = R((innerH * 0.45) / k - 3); drawers += k;
      parts.push({ name: 'Front sertar', l: R(innerW - 4), w: fh, qty: k, edges: [1, 1, 1, 1], material: 'pal', grain: true }); }
  }
  // uși
  if (spec.doors === 'hinged'){ const doorH = R(H - P - 4); let doors = 0;
    for (let i = 0; i < n; i++){ const outerW = innerW + (i === 0 || i === n - 1 ? t * 1.5 : t), k = outerW > 600 ? 2 : 1; doors += k;
      parts.push({ name: 'Ușă', l: doorH, w: R(outerW / k - 3), qty: k, edges: [1, 1, 1, 1], material: 'pal', grain: true }); }
    hw.push({ name: 'Balamale aplicate cu amortizor', qty: doors * hingesFor(doorH), unit: 'buc' }, { name: 'Mânere', qty: doors, unit: 'buc' }); }
  if (spec.doors === 'sliding'){ const k = W <= 2000 ? 2 : 3; parts.push({ name: 'Ușă glisantă (panou)', l: R(H - P - 50), w: R(W / k + 25), qty: k, edges: [1, 1, 1, 1], material: 'pal', grain: true });
    hw.push({ name: 'Sistem uși glisante (șină sus + jos)', qty: 1, unit: 'set' }, { name: 'Profil șină', qty: Math.round(W / 100) / 10, unit: 'm' }); }
  if (rails) hw.push({ name: 'Bară umerașe + suporți', qty: rails, unit: 'buc' });
  if (shelfPins) hw.push({ name: 'Suporți raft', qty: shelfPins, unit: 'buc' });
  if (drawers) hw.push({ name: 'Glisiere sertar (pereche)', qty: drawers, unit: 'set' }, { name: 'Cutie sertar (laterale + spate + fund)', qty: drawers, unit: 'buc' });
  hw.push({ name: 'Picioare reglabile', qty: Math.max(4, (n + 1) * 2), unit: 'buc' }, { name: 'Confirmate / excentrice', qty: 8 + n * 8, unit: 'buc' });
  const list = merge(parts), pal = list.filter(p => p.material === 'pal');
  const palM2 = Math.round(pal.reduce((a, p) => a + p.l * p.w * p.qty, 0) / 1e4) / 100, hdfM2 = Math.round(list.filter(p => p.material === 'hdf').reduce((a, p) => a + p.l * p.w * p.qty, 0) / 1e4) / 100;
  const edgeM = Math.round(pal.reduce((a, p) => a + p.qty * ((p.edges[0] + p.edges[1]) * p.l + (p.edges[2] + p.edges[3]) * p.w), 0) / 10) / 100;
  const sheets = Math.ceil(palM2 * (1 + SHEET_WASTE) / (SHEET.l * SHEET.w / 1e6));
  return { parts: list, hardware: hw, palM2, hdfM2, edgeM, sheets, innerW: R(innerW), innerH: R(innerH) };
}
/** CSV pentru debitare (separator „;”): nr, denumire, lungime, lățime, buc, cant L1/L2/l1/l2, material, fibra pe lungime. */
export function cutListCsv(parts: CutPart[], spec: Pick<WardrobeSpec, 'decor' | 'edge'>): string {
  const head = ['Nr', 'Denumire', 'Lungime (mm)', 'Latime (mm)', 'Buc', 'Cant L1', 'Cant L2', 'Cant l1', 'Cant l2', 'Material', 'Fibra pe lungime'];
  const cant = spec.edge === 'abs2' ? 'ABS 2mm' : 'ABS 0,4mm', mat = (p: CutPart) => p.material === 'hdf' ? 'HDF 3mm' : `PAL ${BOARD_MM}mm${spec.decor ? ' ' + spec.decor : ''}`;
  const esc = (v: string | number) => { const s = String(v); return /[";\n]/.test(s) ? `"${s.replace(/"/g, '""')}"` : s; };
  return [head, ...parts.map((p, i) => [i + 1, p.name, p.l, p.w, p.qty, ...p.edges.map(e => e ? cant : ''), mat(p), p.grain ? 'da' : 'nu'])].map(r => r.map(esc).join(';')).join('\n');
}
