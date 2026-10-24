// Bucătăria pe module: corpurile de jos în ordine, de la stânga la dreapta frontului (lățimi în cm). Din ele: verificări
// după ghidul NKBA (mașina de vase lângă chiuvetă, blat liber lângă chiuvetă și plită, fără corp înalt între chiuvetă și
// plită, suma = lungimea bucătăriei) și blatul: bucăți din lungimi standard, îmbinări pe granițele corpurilor, decupaje.
export const MODULE_TYPES = ['base', 'drawers', 'sink', 'dishwasher', 'hob', 'oven', 'tall', 'fridge', 'corner', 'filler'] as const;
export type ModuleType = typeof MODULE_TYPES[number];
export interface KitchenModule { type: ModuleType; w: number }
export const DEFAULT_W: Record<ModuleType, number> = { base: 60, drawers: 60, sink: 80, dishwasher: 60, hob: 60, oven: 60, tall: 60, fridge: 60, corner: 90, filler: 5 };
const TALL = new Set<ModuleType>(['tall', 'fridge']);
export const NKBA_K = { dwToSink: 91, sinkLandMin: 46, sinkLandMax: 61, hobLandMin: 30, hobLandMax: 38, fridgeLand: 38 };
export const WORKTOP_STOCK_CM = [300, 410];

export function checkModules(x: unknown): KitchenModule[] {
  if (!Array.isArray(x) || x.length > 30) throw new Error('Modulele bucătăriei sunt invalide.');
  return x.map(m => { if (!m || !(MODULE_TYPES as readonly string[]).includes(m.type) || typeof m.w !== 'number' || !Number.isFinite(m.w) || m.w < 3 || m.w > 150) throw new Error('Modul invalid.');
    return { type: m.type, w: Math.round(m.w * 10) / 10 }; });
}
/** O propunere de pornire: corp – chiuvetă – mașină de vase – corp – plită – corp, cu blat liber de ambele părți ale chiuvetei
 *  și plitei; corpurile flexibile primesc restul (multipli de 5 cm), ce rămâne devine completare. */
export function defaultModules(runCm: number): KitchenModule[] {
  const sinkW = runCm >= 340 ? 80 : 60, fixed = sinkW + 60 + 60, mins = [45, 40, 40];
  if (runCm < fixed + mins.reduce((a, b) => a + b, 0)){
    // bucătărie scurtă (fără mașină de vase): corp – chiuvetă – corp – plită – corp, corpurile își împart restul
    const rest = runCm - 120; if (rest < 90){ const out: KitchenModule[] = [{ type: 'sink', w: 60 }]; if (rest > 0) out.push({ type: 'base', w: Math.round(rest * 10) / 10 }); out.push({ type: 'hob', w: 60 }); return runCm >= 120 ? out : [{ type: 'base', w: runCm }]; }
    const b = Math.floor(rest / 3 / 5) * 5, last = Math.round((rest - 2 * b) * 10) / 10;
    return [{ type: 'base', w: b }, { type: 'sink', w: 60 }, { type: 'base', w: b }, { type: 'hob', w: 60 }, { type: 'base', w: last }]; }
  let extra = runCm - fixed - mins.reduce((a, b) => a + b, 0); const flex = [...mins];
  for (let i = 0; extra >= 5; i = (i + 1) % 3){ const add = Math.min(5, extra); if (flex[i]! + add > 90){ if (flex.every(v => v + 5 > 90)) break; continue; } flex[i] = flex[i]! + add; extra -= add; }
  const out: KitchenModule[] = [{ type: 'base', w: flex[0]! }, { type: 'sink', w: sinkW }, { type: 'dishwasher', w: 60 }, { type: 'drawers', w: flex[1]! }, { type: 'hob', w: 60 }, { type: 'base', w: flex[2]! }];
  if (extra >= 3) out.push({ type: 'filler', w: Math.round(extra * 10) / 10 }); else if (extra > 0) out[out.length - 1]!.w = Math.round((out[out.length - 1]!.w + extra) * 10) / 10;
  return out;
}
const pos = (ms: KitchenModule[]) => { let x = 0; return ms.map(m => { const a = x; x += m.w; return { ...m, a, b: x }; }); };
/** Blat liber (fără corpuri înalte și fără chiuvetă/plită) la stânga și la dreapta modulului i. */
function landing(ms: ReturnType<typeof pos>, i: number){
  let l = 0; for (let k = i - 1; k >= 0 && !TALL.has(ms[k]!.type); k--){ if (ms[k]!.type === 'sink' || ms[k]!.type === 'hob') break; l += ms[k]!.w; }
  let r = 0; for (let k = i + 1; k < ms.length && !TALL.has(ms[k]!.type); k++){ if (ms[k]!.type === 'sink' || ms[k]!.type === 'hob') break; r += ms[k]!.w; }
  return [l, r] as const;
}
export interface ModuleIssue { key: string; vars?: Record<string, string | number> }
export function moduleIssues(modules: KitchenModule[], runCm: number): ModuleIssue[] {
  const out: ModuleIssue[] = [], ms = pos(modules), sum = Math.round(ms.reduce((a, m) => a + m.w, 0) * 10) / 10;
  if (Math.abs(sum - runCm) > 1) out.push({ key: 'kmod.sum', vars: { sum, run: Math.round(runCm * 10) / 10 } });
  const sink = ms.findIndex(m => m.type === 'sink'), hob = ms.findIndex(m => m.type === 'hob'), dw = ms.findIndex(m => m.type === 'dishwasher');
  if (sink >= 0 && dw >= 0){ const s = ms[sink]!, d = ms[dw]!, gap = d.a >= s.b ? d.a - s.b : s.a - d.b; if (gap > NKBA_K.dwToSink) out.push({ key: 'kmod.dwFar', vars: { cm: Math.round(gap) } }); }
  if (sink >= 0 && hob >= 0){ const [a, b] = sink < hob ? [sink, hob] : [hob, sink]; if (ms.slice(a + 1, b).some(m => TALL.has(m.type))) out.push({ key: 'kmod.tallBetween' }); }
  if (sink >= 0){ const [l, r] = landing(ms, sink), big = Math.max(l, r), small = Math.min(l, r);
    if (big < NKBA_K.sinkLandMax || small < NKBA_K.sinkLandMin) out.push({ key: 'kmod.sinkLanding', vars: { l: Math.round(l), r: Math.round(r) } }); }
  if (hob >= 0){ const [l, r] = landing(ms, hob), big = Math.max(l, r), small = Math.min(l, r);
    if (big < NKBA_K.hobLandMax || small < NKBA_K.hobLandMin) out.push({ key: 'kmod.hobLanding', vars: { l: Math.round(l), r: Math.round(r) } }); }
  const fr = ms.findIndex(m => m.type === 'fridge'); if (fr >= 0){ const [l, r] = landing(ms, fr); if (Math.max(l, r) < NKBA_K.fridgeLand) out.push({ key: 'kmod.fridgeLanding' }); }
  return out;
}
export interface WorktopPiece { from: number; to: number; length: number; stock: number; cutouts: { type: 'sink' | 'hob'; at: number; w: number }[] }
/** Bucățile de blat: fiecare porțiune continuă dintre corpurile înalte; peste lungimea maximă se îmbină pe granița de corp cea
 *  mai apropiată de mijloc care nu cade lângă chiuvetă sau plită. */
export function worktop(modules: KitchenModule[]): WorktopPiece[] {
  const ms = pos(modules), segs: { a: number; b: number; ms: typeof ms }[] = []; let cur: typeof ms = [];
  for (const m of ms){ if (TALL.has(m.type)){ if (cur.length) segs.push({ a: cur[0]!.a, b: cur[cur.length - 1]!.b, ms: cur }); cur = []; } else cur.push(m); }
  if (cur.length) segs.push({ a: cur[0]!.a, b: cur[cur.length - 1]!.b, ms: cur });
  const maxStock = Math.max(...WORKTOP_STOCK_CM), out: WorktopPiece[] = [];
  const split = (a: number, b: number, list: typeof ms): [number, number][] => {
    if (b - a <= maxStock) return [[a, b]] as [number, number][];
    const joints = list.slice(0, -1).map((m, i) => ({ x: m.b, ok: !['sink', 'hob'].includes(m.type) && !['sink', 'hob'].includes(list[i + 1]!.type) })).filter(j => j.ok && j.x > a && j.x < b && j.x - a <= maxStock);
    const j = joints.sort((p, q) => Math.abs(p.x - (a + b) / 2) - Math.abs(q.x - (a + b) / 2))[0]; if (!j) return [[a, b]] as [number, number][];
    return [...split(a, j.x, list.filter(m => m.b <= j.x)), ...split(j.x, b, list.filter(m => m.a >= j.x))];
  };
  for (const s of segs) for (const [a, b] of split(s.a, s.b, s.ms)){ const L = Math.round((b - a) * 10) / 10;
    out.push({ from: a, to: b, length: L, stock: WORKTOP_STOCK_CM.find(x => x >= L) ?? maxStock,
      cutouts: s.ms.filter(m => (m.type === 'sink' || m.type === 'hob') && m.a >= a && m.b <= b).map(m => ({ type: m.type as 'sink' | 'hob', at: Math.round((m.a + m.w / 2 - a) * 10) / 10, w: m.type === 'sink' ? Math.max(30, m.w - 10) : 56 })) }); }
  return out;
}
