// Validarea propunerilor de design. Ordinea fixă: schemă → produse → geometrie → coliziuni/circulație → buget → aprobare.
import type { Catalog, Snapshot, MaterialsCatalog, RoomFinishes, Severity } from './types';
import { autoLayout } from './project';
import { validatePlacement, validateFloor, severityOf } from './validate';
import { computeBudget, finishesOf } from './boq';
import { resolve, groupOf, groups } from './catalog';
import type { DesignBrief } from './brief';

export type Tier = 'economic' | 'echilibrat' | 'premium';
export const TIERS: Tier[] = ['economic', 'echilibrat', 'premium'];
export const TIER_LABEL: Record<Tier, string> = { economic: 'Economic', echilibrat: 'Echilibrat', premium: 'Premium' };
export interface RawVariant { tier: Tier; title: string; summary: string; palette: string[]; selections: Record<string, string>; finishes: Record<string, Partial<RoomFinishes>>; reasons: { target: string; reason: string }[] }
export interface RawProposal { variants: RawVariant[] }
export interface PIssue { code: 'SCHEMA' | 'INVALID_PRODUCT' | 'INVALID_MATERIAL' | 'DOES_NOT_FIT' | 'PLACEMENT' | 'OVER_BUDGET' | 'FLOOR'; severity: 'ERROR' | 'WARNING'; message: string }
export interface Change { kind: 'product' | 'finish'; label: string; from: string; to: string; priceDelta: number | null; note?: string }
export interface EvaluatedVariant extends RawVariant { status: Severity; issues: PIssue[]; total: number; delta: number; changes: Change[]; candidate: Snapshot }

const HEX = /^#[0-9a-f]{6}$/i, str = (v: unknown, n: number) => typeof v === 'string' ? v.replace(/[\u0000-\u001f<>]/g, ' ').slice(0, n) : '';
// 1) Schemă: tot ce nu respectă formatul e respins înainte de orice altceva.
export function parseProposal(input: unknown): { proposal: RawProposal | null; errors: string[] } {
  const errors: string[] = []; const o = input as any;
  if (!o || !Array.isArray(o.variants) || o.variants.length < 1 || o.variants.length > 3) return { proposal: null, errors: ['Propunerea trebuie să conțină între 1 și 3 variante.'] };
  const variants: RawVariant[] = [];
  for (const [i, v] of o.variants.entries()){
    if (!v || !TIERS.includes(v.tier)){ errors.push(`Varianta ${i + 1}: nivel necunoscut.`); continue; }
    if (variants.some(x => x.tier === v.tier)){ errors.push(`Varianta ${i + 1}: nivel duplicat.`); continue; }
    if (!v.selections || typeof v.selections !== 'object' || Array.isArray(v.selections)){ errors.push(`Varianta ${i + 1}: lipsesc selecțiile.`); continue; }
    const sel: Record<string, string> = {}; for (const [k, val] of Object.entries(v.selections)) if (typeof val === 'string' && k.length < 40) sel[k] = val.slice(0, 60);
    const fin: Record<string, Partial<RoomFinishes>> = {}; if (v.finishes && typeof v.finishes === 'object') for (const [rid, f] of Object.entries<any>(v.finishes)){ if (!f || typeof f !== 'object') continue;
      fin[rid.slice(0, 60)] = Object.fromEntries(['floor', 'wallPaint', 'wallTile', 'baseboard', 'light'].filter(k => typeof f[k] === 'string' || f[k] === null).map(k => [k, f[k]])); }
    variants.push({ tier: v.tier, title: str(v.title, 80) || v.tier, summary: str(v.summary, 400), palette: Array.isArray(v.palette) ? v.palette.filter((c: unknown) => typeof c === 'string' && HEX.test(c)).slice(0, 6) : [],
      selections: sel, finishes: fin, reasons: Array.isArray(v.reasons) ? v.reasons.filter((r: any) => r && typeof r.reason === 'string').slice(0, 20).map((r: any) => ({ target: str(r.target, 60), reason: str(r.reason, 300) })) : [] });
  }
  return { proposal: variants.length ? { variants } : null, errors };
}
const SLOT_CATS: Record<string, string[]> = { floor: ['parquet', 'floor_tile'], wallPaint: ['paint'], wallTile: ['wall_tile'], baseboard: ['baseboard'], light: ['lighting'] };
// 2-6) Evaluare pe o copie a proiectului; proiectul real nu se atinge.
export function evaluateVariant(snap: Snapshot, cat: Catalog, mc: MaterialsCatalog, v: RawVariant, brief?: DesignBrief): EvaluatedVariant {
  const issues: PIssue[] = [], cand: Snapshot = structuredClone(snap), G = groups(cat), changes: Change[] = [];
  const priceOf = (vid: string) => resolve(cat, vid)?.offer?.price ?? null;
  // produse: id existent, din grupa potrivită, magazin permis
  const selections = { ...cand.selections }, picked = new Set(cand.picked);
  for (const [g, vid] of Object.entries(v.selections)){
    const rv = resolve(cat, vid);
    if (!G[g]){ issues.push({ code: 'INVALID_PRODUCT', severity: 'ERROR', message: `Grupa „${g}” nu există în catalog.` }); continue; }
    if (!rv || groupOf(vid) !== g){ issues.push({ code: 'INVALID_PRODUCT', severity: 'ERROR', message: `Produsul „${vid}” nu există în catalog pentru ${G[g].label}.` }); continue; }
    if (brief && rv.offer && !brief.suppliers.includes(rv.offer.provenance.source as any)){ issues.push({ code: 'INVALID_PRODUCT', severity: 'ERROR', message: `${rv.variant.name} e de la ${rv.offer.provenance.source}, magazin exclus din brief.` }); continue; }
    if (selections[g] !== vid){ selections[g] = vid; picked.add(g); }
  }
  cand.selections = selections; cand.picked = [...picked];
  // finisaje: material existent, potrivit slotului
  cand.finishes = { ...(cand.finishes || {}) };
  for (const [rid, f] of Object.entries(v.finishes)){
    const room = cand.floor.rooms.find(r => r.id === rid); if (!room){ issues.push({ code: 'INVALID_MATERIAL', severity: 'ERROR', message: `Camera „${rid}” nu există.` }); continue; }
    const cur = finishesOf(cand, room), next = { ...cur };
    for (const [slot, mid] of Object.entries(f)){ if (mid === null && (slot === 'wallTile' || slot === 'baseboard')){ (next as any)[slot] = null; continue; }
      const m = mc.materials.find(x => x.id === mid); if (!m || !SLOT_CATS[slot]?.includes(m.category)){ issues.push({ code: 'INVALID_MATERIAL', severity: 'ERROR', message: `Materialul „${mid}” nu e valid pentru ${room.name}.` }); continue; }
      if (brief && !brief.suppliers.includes(m.supplier as any)){ issues.push({ code: 'INVALID_MATERIAL', severity: 'ERROR', message: `${m.name} e de la ${m.supplier}, magazin exclus din brief.` }); continue; }
      (next as any)[slot] = mid; }
    cand.finishes[rid] = next;
  }
  // geometrie: motorul determinist așază piesele; AI-ul nu dă coordonate
  const before = new Set(snap.placements.map(p => `${p.roomId}:${p.group}`));
  const lay = autoLayout(cand, cat); Object.assign(cand, { placements: lay.snapshot.placements });
  for (const n of lay.notFit) issues.push({ code: 'DOES_NOT_FIT', severity: 'ERROR', message: `${G[n.key]?.label || n.key}: varianta aleasă nu încape în ${n.room}.` });
  const after = new Set(cand.placements.map(p => `${p.roomId}:${p.group}`));
  for (const k of before) if (!after.has(k)){ const [rid, g] = k.split(':'); if (!lay.notFit.some(n => n.key === g)) issues.push({ code: 'DOES_NOT_FIT', severity: 'ERROR', message: `${G[g]?.label || g}: varianta aleasă nu încape în ${cand.floor.rooms.find(r => r.id === rid)?.name}.` }); }
  for (const i of validateFloor(cand.floor)) issues.push({ code: 'FLOOR', severity: i.severity, message: i.message });
  const seen = new Set<string>();
  for (const p of cand.placements) for (const i of validatePlacement(cand, cat, p)){ const m = `${resolve(cat, p.variantId)?.product.name}: ${i.message}`; if (!seen.has(m)){ seen.add(m); issues.push({ code: 'PLACEMENT', severity: i.severity, message: m }); } }
  // buget
  if (brief?.budget) cand.budget = { ...(cand.budget || {} as any), ...computeBudget(cand, cat, mc).settings, target: brief.budget };
  const b0 = computeBudget(snap, cat, mc), b1 = computeBudget(cand, cat, mc), target = brief?.budget ?? b1.settings.target;
  if (target != null && b1.chosen.total > target) issues.push({ code: 'OVER_BUDGET', severity: 'WARNING', message: `Depășește bugetul de ${Math.round(target).toLocaleString('ro-RO')} lei cu ${Math.round(b1.chosen.total - target).toLocaleString('ro-RO')} lei.` });
  // ce se schimbă
  const curVid = (g: string) => snap.placements.find(p => p.group === g)?.variantId || snap.selections[g];
  for (const g of Object.keys(v.selections)){ const a = curVid(g), bvid = cand.selections[g]; if (!a || !bvid || a === bvid || !resolve(cat, bvid)) continue;
    const pa = priceOf(a), pb = priceOf(bvid), ra = resolve(cat, a), rb = resolve(cat, bvid);
    const dims = ra?.variant.dimensionsCm && rb?.variant.dimensionsCm ? `${rb.variant.dimensionsCm.w - ra.variant.dimensionsCm.w >= 0 ? '+' : ''}${rb.variant.dimensionsCm.w - ra.variant.dimensionsCm.w} cm lățime, ${rb.variant.dimensionsCm.d - ra.variant.dimensionsCm.d >= 0 ? '+' : ''}${rb.variant.dimensionsCm.d - ra.variant.dimensionsCm.d} cm adâncime` : undefined;
    changes.push({ kind: 'product', label: G[g].label, from: ra?.variant.name || a, to: rb!.variant.name, priceDelta: pa != null && pb != null ? Math.round((pb - pa) * 100) / 100 : null, note: dims }); }
  for (const room of cand.floor.rooms){ const fa = finishesOf(snap, room), fb = finishesOf(cand, room);
    for (const slot of ['floor', 'wallPaint', 'wallTile', 'baseboard', 'light'] as const){ if ((fa as any)[slot] === (fb as any)[slot]) continue; const nm = (id: any) => mc.materials.find(m => m.id === id)?.name || 'fără';
      changes.push({ kind: 'finish', label: `${room.name} · ${{ floor: 'pardoseală', wallPaint: 'vopsea', wallTile: 'faianță', baseboard: 'plintă', light: 'iluminat' }[slot]}`, from: nm((fa as any)[slot]), to: nm((fb as any)[slot]), priceDelta: null }); } }
  const status: Severity = issues.some(i => i.severity === 'ERROR') ? 'ERROR' : issues.length ? 'WARNING' : 'PASS';
  return { ...v, status, issues, total: b1.chosen.total, delta: Math.round((b1.chosen.total - b0.chosen.total) * 100) / 100, changes, candidate: cand };
}
export const evaluateProposal = (snap: Snapshot, cat: Catalog, mc: MaterialsCatalog, p: RawProposal, brief?: DesignBrief) => p.variants.map(v => evaluateVariant(snap, cat, mc, v, brief));
// Aplicarea respectă constituția: ERROR blochează; WARNING cere confirmare explicită.
export function applyVariant(ev: EvaluatedVariant, confirmWarnings: boolean): { ok: true; snapshot: Snapshot } | { ok: false; reason: string } {
  if (ev.status === 'ERROR') return { ok: false, reason: 'Varianta are erori și nu poate fi aplicată.' };
  if (ev.status === 'WARNING' && !confirmWarnings) return { ok: false, reason: 'Varianta are avertismente; confirmă-le înainte de aplicare.' };
  return { ok: true, snapshot: ev.candidate };
}
export { severityOf };
