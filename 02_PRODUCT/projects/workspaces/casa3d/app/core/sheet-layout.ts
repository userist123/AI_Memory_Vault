// Așezarea pieselor pe plăci (PAL / HDF / fronturi) pentru debitare, comună dulapului și bucătăriei. Utilajul de debitat taie
// doar cap-coadă (ghilotină), deci fiecare plasare trebuie să se poată obține din tăieturi care traversează toată bucata.
// Algoritm: dreptunghiuri libere cu împărțire ghilotină (fără lipirea resturilor), piesele în ordine descrescătoare; se
// încearcă mai multe ordonări / reguli de alegere / reguli de împărțire și se păstrează varianta cu cele mai puține plăci
// (la egalitate: ultima placă cea mai liberă). Determinist. Cote în mm; x pe lungimea plăcii, y pe lățime.
// Fibra: piesa cu `grain` nu se rotește (L rămâne pe lungimea plăcii). Între piese rămâne lățimea pânzei (kerf); marginile
// plăcii se tund (`trim` pe fiecare latură) și nu se folosesc.
export interface LayoutPart { id: string; name: string; l: number; w: number; qty: number; grain: boolean; material: string }
export interface LayoutOptions { sheetL: number; sheetW: number; kerf: number; trim: number }
export interface Placement { id: string; name: string; x: number; y: number; l: number; w: number; rotated: boolean }
export interface SheetLayout { material: string; placements: Placement[]; usedArea: number; waste: number }
/** tooBig = nu încape în zona utilă; grain = ar încăpea doar rotită, dar are fibră; invalid = cote sau bucăți greșite; limit = prea multe piese. */
export type UnplacedReason = 'tooBig' | 'grain' | 'invalid' | 'limit';
export interface Unplaced { id: string; name: string; qty: number; reason: UnplacedReason }
export interface LayoutStats { sheets: Record<string, number>; total: number; pieces: number; partArea: number; sheetArea: number; utilization: number }
export interface LayoutResult { sheets: SheetLayout[]; unplaced: Unplaced[]; stats: LayoutStats }
export const LAYOUT_DEFAULT: LayoutOptions = { sheetL: 2800, sheetW: 2070, kerf: 4, trim: 10 };
export const MAX_LAYOUT_PIECES = 5000;

const EPS = 1e-6;
interface Item { id: string; name: string; l: number; w: number; grain: boolean; seq: number }
interface Rect { x: number; y: number; l: number; w: number }
interface Bin { free: Rect[]; placed: Placement[]; used: number }
type Order = (a: Item, b: Item) => number;
const ORDERS: Order[] = [
  (a, b) => b.l * b.w - a.l * a.w,                                       // aria
  (a, b) => Math.max(b.l, b.w) - Math.max(a.l, a.w) || b.l * b.w - a.l * a.w,   // latura mare
  (a, b) => b.w - a.w || b.l - a.l,                                      // lățimea (fâșii pe lungimea plăcii)
  (a, b) => b.l - a.l || b.w - a.w,                                      // lungimea
];
type Score = (f: Rect, l: number, w: number) => number;
const SCORES: Score[] = [
  (f, l, w) => Math.min(f.l - l, f.w - w),      // cel mai mic rest pe latura scurtă
  (f, l, w) => f.l * f.w - l * w,               // cea mai mică arie rămasă
];

function pack(items: Item[], UL: number, UW: number, kerf: number, order: Order, score: Score, shorterAxis: boolean): Bin[] {
  const bins: Bin[] = [], sorted = [...items].sort((a, b) => order(a, b) || a.seq - b.seq);
  const find = (bin: Bin, it: Item) => { let best: { i: number; l: number; w: number; rot: boolean; s: number } | null = null;
    for (let i = 0; i < bin.free.length; i++){ const f = bin.free[i]!;
      for (let r = 0; r < 2; r++){ if (r === 1 && (it.grain || it.l === it.w)) break; const l = r ? it.w : it.l, w = r ? it.l : it.w;
        if (l > f.l + EPS || w > f.w + EPS) continue; const s = score(f, l, w); if (!best || s < best.s - EPS) best = { i, l, w, rot: r === 1, s }; } }
    return best; };
  for (const it of sorted){
    let bin: Bin | undefined, hit: ReturnType<typeof find> = null;
    for (const b of bins){ hit = find(b, it); if (hit){ bin = b; break; } }
    if (!hit){ bin = { free: [{ x: 0, y: 0, l: UL, w: UW }], placed: [], used: 0 }; bins.push(bin); hit = find(bin, it); if (!hit) continue; }   // piesele prea mari sunt scoase dinainte
    const f = bin!.free.splice(hit.i, 1)[0]!, dl = f.l - hit.l - kerf, dw = f.w - hit.w - kerf;
    bin!.placed.push({ id: it.id, name: it.name, x: f.x, y: f.y, l: hit.l, w: hit.w, rotated: hit.rot }); bin!.used += hit.l * hit.w;
    // împărțire ghilotină: prima tăietură traversează tot dreptunghiul liber, a doua desparte piesa de restul fâșiei ei
    const along = shorterAxis ? dl <= dw : dl > dw;     // true: prima tăietură e pe lungime (restul de deasupra ia toată lungimea)
    if (dl > EPS) bin!.free.push({ x: f.x + hit.l + kerf, y: f.y, l: dl, w: along ? hit.w : f.w });
    if (dw > EPS) bin!.free.push({ x: f.x, y: f.y + hit.w + kerf, l: along ? f.l : hit.l, w: dw });
  }
  return bins;
}

/** Așază piesele pe plăci, pe materiale. Piesele care nu încap (sau au cote greșite) apar în `unplaced`, fără excepție. */
export function layoutSheets(parts: LayoutPart[], opt: LayoutOptions = LAYOUT_DEFAULT): LayoutResult {
  const num = (v: unknown): v is number => typeof v === 'number' && Number.isFinite(v);
  const okOpt = num(opt.sheetL) && num(opt.sheetW) && num(opt.kerf) && num(opt.trim) && opt.kerf >= 0 && opt.trim >= 0;
  const UL = okOpt ? opt.sheetL - 2 * opt.trim : 0, UW = okOpt ? opt.sheetW - 2 * opt.trim : 0, kerf = okOpt ? opt.kerf : 0, trim = okOpt ? opt.trim : 0;
  const unplaced: Unplaced[] = [], byMat = new Map<string, Item[]>(); let seq = 0, pieces = 0;
  for (const p of parts){
    const bad = (reason: UnplacedReason) => unplaced.push({ id: p.id, name: p.name, qty: Number.isInteger(p.qty) && p.qty > 0 ? p.qty : 0, reason });
    if (!num(p.l) || !num(p.w) || p.l <= 0 || p.w <= 0 || !Number.isInteger(p.qty) || p.qty < 1){ bad('invalid'); continue; }
    const fits = p.l <= UL + EPS && p.w <= UW + EPS, fitsRot = p.w <= UL + EPS && p.l <= UW + EPS;
    if (!fits && (p.grain || !fitsRot)){ bad(p.grain && fitsRot ? 'grain' : 'tooBig'); continue; }
    if (pieces + p.qty > MAX_LAYOUT_PIECES){ bad('limit'); continue; }
    pieces += p.qty; let list = byMat.get(p.material); if (!list) byMat.set(p.material, list = []);
    for (let k = 0; k < p.qty; k++) list.push({ id: p.id, name: p.name, l: p.l, w: p.w, grain: !!p.grain, seq: seq++ });
  }
  const sheets: SheetLayout[] = [], count: Record<string, number> = {}, sheetArea = okOpt ? opt.sheetL * opt.sheetW : 0; let partArea = 0;
  for (const [material, items] of byMat){
    let best: Bin[] | null = null, bestKey = [Infinity, Infinity];
    for (const order of ORDERS) for (const score of SCORES) for (const sla of [true, false]){
      const bins = pack(items, UL, UW, kerf, order, score, sla), key = [bins.length, bins[bins.length - 1]?.used ?? 0];
      if (key[0]! < bestKey[0]! || (key[0] === bestKey[0] && key[1]! < bestKey[1]! - EPS)){ best = bins; bestKey = key; } }
    count[material] = best!.length;
    for (const b of best!){ partArea += b.used;
      sheets.push({ material, placements: b.placed.map(q => ({ ...q, x: q.x + trim, y: q.y + trim })), usedArea: b.used, waste: sheetArea > 0 ? Math.round((1 - b.used / sheetArea) * 1e4) / 1e4 : 0 }); }
  }
  return { sheets, unplaced, stats: { sheets: count, total: sheets.length, pieces, partArea, sheetArea, utilization: sheets.length ? Math.round(partArea / (sheets.length * sheetArea) * 1e4) / 1e4 : 0 } };
}

/** Se poate obține placa doar din tăieturi cap-coadă? Caută recursiv o tăietură (de lățime ≥ kerf) care desparte piesele în două
 *  grupuri fără să atingă vreuna; fiecare grup trebuie să fie la rândul lui tăiabil. Piesele suprapuse dau `false`. */
export function isGuillotine(sheet: Pick<SheetLayout, 'placements'>, kerf = 0): boolean {
  const cut = (rs: Placement[]): boolean => {
    if (rs.length <= 1) return true;
    for (const ax of ['x', 'y'] as const){ const len = ax === 'x' ? 'l' : 'w', s = [...rs].sort((a, b) => a[ax] - b[ax]); let end = s[0]![ax] + s[0]![len];
      for (let i = 1; i < s.length; i++){ if (s[i]![ax] >= end + kerf - 1e-6) return cut(s.slice(0, i)) && cut(s.slice(i)); end = Math.max(end, s[i]![ax] + s[i]![len]); } }
    return false; };
  return cut(sheet.placements);
}
/** Piesele unei liste de debitare, numerotate ca în lista pentru atelier (id = numărul de ordine, de la 1). */
export function layoutCutParts(parts: { name: string; l: number; w: number; qty: number; grain: boolean; material: string }[], opt: LayoutOptions = LAYOUT_DEFAULT): LayoutResult {
  return layoutSheets(parts.map((p, i) => ({ id: String(i + 1), name: p.name, l: p.l, w: p.w, qty: p.qty, grain: p.grain, material: p.material })), opt);
}
