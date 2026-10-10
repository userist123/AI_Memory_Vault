// Propunător determinist (folosit când AI-ul nu e configurat sau răspunde invalid). Fiecare motiv afișat e o
// consecință verificabilă a datelor din catalog și a brief-ului — nu formulări inventate.
import type { Catalog, Snapshot, MaterialsCatalog, ProductVariant, RoomFinishes } from './types';
import { groups, resolve } from './catalog';
import { STYLES, PRIORITIES, type DesignBrief } from './brief';
import { TIERS, TIER_LABEL, evaluateVariant, type RawProposal, type RawVariant, type Tier } from './proposal';
import { floors } from './levels';

const lum = (hex?: string) => { if (!hex || !/^#[0-9a-f]{6}$/i.test(hex)) return null; const n = parseInt(hex.slice(1), 16), c = [(n >> 16) & 255, (n >> 8) & 255, n & 255].map(v => v / 255); return .2126 * c[0] + .7152 * c[1] + .0722 * c[2]; };
const colorOf = (v: ProductVariant) => v.style?.col || v.style?.top || v.style?.frame || v.style?.prof;
function styleScore(v: ProductVariant, style: DesignBrief['style']): { s: number; why?: string } {
  const L = lum(colorOf(v)), wood = !!v.style?.wood || /stejar|lemn|furnir/i.test(v.name), dark = L != null && L < .35, light = L != null && L > .7, n = v.name.toLowerCase();
  if (style === 'scandinav'){ if (light) return { s: 2, why: 'culoare deschisă (scandinav)' }; if (wood && !dark) return { s: 2, why: 'lemn deschis (scandinav)' }; if (dark) return { s: -1 }; }
  if (style === 'modern'){ if (dark || /antracit|negru/.test(n)) return { s: 2, why: 'nuanță închisă, contrast (modern)' }; if (light && !wood) return { s: 1, why: 'nuanță deschisă, simplă (modern)' }; }
  if (style === 'natural'){ if (wood || /bej|stejar|ratan/.test(n)) return { s: 2, why: 'material natural / nuanță caldă (natural)' }; if (dark) return { s: -1 }; }
  if (style === 'clasic'){ if ((wood && (dark || L == null)) || /negru-maro|maro/.test(n)) return { s: 2, why: 'lemn închis (clasic)' }; }
  return { s: 0 };
}
const vol = (v: ProductVariant) => v.dimensionsCm ? v.dimensionsCm.w * v.dimensionsCm.d * v.dimensionsCm.h : 0, foot = (v: ProductVariant) => v.dimensionsCm ? v.dimensionsCm.w * v.dimensionsCm.d : 0;
function candidates(cat: Catalog, g: string, brief: DesignBrief, banned: Set<string>){
  const avoid = brief.avoid.toLowerCase().split(/[,;]+/).map(s => s.trim()).filter(s => s.length > 2), notes: string[] = [];
  const list = groups(cat)[g].variants.filter(v => { const o = cat.offers.find(x => x.variantId === v.id); if (!o || banned.has(v.id)) return false;
    if (!brief.suppliers.includes(o.provenance.source as any)) return false;
    if (avoid.some(a => v.name.toLowerCase().includes(a))){ notes.push(`am exclus ${v.name} (cerință „de evitat”)`); return false; }
    if (brief.pets && ['velvet', 'leather'].includes(v.style?.mat)){ notes.push('fără catifea și piele, din cauza animalelor'); return false; }
    if (brief.children && (/sticl/i.test(v.name) || v.style?.type === 'vittsjo')){ notes.push('fără blat de sticlă, pentru copii'); return false; }
    return true; });
  return { list: list.map(v => ({ v, price: cat.offers.find(x => x.variantId === v.id)!.price })).sort((a, b) => a.price - b.price), notes: [...new Set(notes)] };
}
function pick(cat: Catalog, g: string, tier: Tier, brief: DesignBrief, banned: Set<string>){
  const { list, notes } = candidates(cat, g, brief, banned); if (!list.length) return null;
  const n = list.length, half = Math.ceil(n / 2), pool = tier === 'economic' ? list.slice(0, half) : tier === 'premium' ? list.slice(n - half) : list, mid = (n - 1) / 2;
  let best: { v: ProductVariant; price: number; score: number; why: string[] } | null = null;
  for (const [i, c] of pool.entries()){ const st = styleScore(c.v, brief.style), why = st.why ? [st.why] : []; let score = st.s;
    if (brief.priorities.includes('depozitare') && ['biblioteca', 'dulap', 'pantofar'].includes(g)){ score += vol(c.v) / Math.max(...pool.map(x => vol(x.v) || 1)); why.push('volum mare de depozitare'); }
    if (brief.priorities.includes('birou') && g === 'birou'){ score += foot(c.v) / Math.max(...pool.map(x => foot(x.v) || 1)); why.push('suprafață de lucru mai mare'); }
    if (brief.priorities.includes('spatiu') && ['canapea', 'masuta', 'masa'].includes(g)){ score += 1 - foot(c.v) / Math.max(...pool.map(x => foot(x.v) || 1)); why.push('amprentă mai mică pe podea'); }
    if (brief.priorities.includes('musafiri') && g === 'masa' && (c.v.chairs || 4) >= 4){ score += 1; why.push('4 scaune pentru musafiri'); }
    if (tier === 'echilibrat') score -= Math.abs(list.indexOf(c) - mid) * .6;
    const better = !best || score > best.score + 1e-9 || (Math.abs(score - best.score) < 1e-9 && (tier === 'premium' ? c.price > best.price : c.price < best.price));
    if (better) best = { v: c.v, price: c.price, score, why: i === 0 || true ? why : why }; }
  const tierWhy = tier === 'economic' ? 'printre cele mai accesibile variante potrivite' : tier === 'premium' ? 'printre variantele de gamă superioară' : 'preț în zona de mijloc a gamei';
  return best ? { id: best.v.id, name: best.v.name, price: best.price, why: [tierWhy, ...best.why, ...notes] } : null;
}
const FINISH_PLAN: Record<Tier, { parquet: number; tile: number; wall: number; paint: string[]; base: string; light: string[] }> = {
  economic: { parquet: 0, tile: 0, wall: 0, paint: ['vopsea-innenweiss'], base: 'plinta-mdf-60', light: ['lampa-virrmo', 'lampa-karrnocka'] },
  echilibrat: { parquet: 0, tile: 0, wall: 1, paint: ['vopsea-caparol-alb', 'vopsea-innenweiss'], base: 'plinta-mdf-60', light: ['lampa-virrmo'] },
  premium: { parquet: -1, tile: -1, wall: -1, paint: ['vopsea-savana-super', 'vopsea-caparol-alb'], base: 'plinta-mdf-90', light: ['lampa-kabomba'] } };
function finishesFor(snap: Snapshot, mc: MaterialsCatalog, tier: Tier, brief: DesignBrief): Record<string, Partial<RoomFinishes>> {
  const ok = (id: string) => mc.materials.some(m => m.id === id && brief.suppliers.includes(m.supplier as any));
  const byCat = (c: string) => mc.materials.filter(m => m.category === c && brief.suppliers.includes(m.supplier as any)).sort((a, b) => a.unitPrice - b.unitPrice);
  const at = (arr: any[], i: number) => arr.length ? arr[i < 0 ? arr.length + i : Math.min(i, arr.length - 1)].id : undefined, plan = FINISH_PLAN[tier], out: Record<string, Partial<RoomFinishes>> = {};
  for (const r of floors(snap).flatMap(f => f.rooms)){ const wet = r.type === 'baie' || r.type === 'bucatarie', f: Partial<RoomFinishes> = {};
    const floor = wet ? at(byCat('floor_tile'), plan.tile) : at(byCat('parquet'), plan.parquet); if (floor) f.floor = floor;
    const paint = plan.paint.find(ok); if (paint) f.wallPaint = paint;
    if (wet){ const t = at(byCat('wall_tile'), plan.wall); if (t) f.wallTile = t; } else if (ok(plan.base)) f.baseboard = plan.base;
    const l = plan.light.find(ok); if (l) f.light = l; out[r.id] = f; }
  return out;
}
export function proposeByRules(snap: Snapshot, cat: Catalog, mc: MaterialsCatalog, brief: DesignBrief): RawProposal {
  const G = groups(cat), present = new Set(snap.placements.map(p => p.group));
  if (snap.placements.some(p => p.group === 'bucatarie')) ['plita', 'cuptor', 'hota'].forEach(g => present.add(g));
  const variants: RawVariant[] = [];
  for (const tier of TIERS){
    const banned = new Set<string>(); let v: RawVariant | null = null;
    for (let attempt = 0; attempt < 12; attempt++){
      const selections: Record<string, string> = {}, reasons: RawVariant['reasons'] = [], colors: string[] = [];
      for (const g of [...present].filter(g => G[g])){ const p = pick(cat, g, tier, brief, banned); if (!p) continue; selections[g] = p.id;
        reasons.push({ target: G[g].label, reason: `${p.name} (${p.price.toLocaleString('ro-RO')} lei): ${p.why.join('; ')}.` });
        const c = colorOf(G[g].variants.find(x => x.id === p.id)!); if (c && !colors.includes(c)) colors.push(c); }
      v = { tier, title: `${TIER_LABEL[tier]} · ${STYLES[brief.style]}`, palette: colors.slice(0, 5), selections, finishes: finishesFor(snap, mc, tier, brief), reasons,
        summary: `Stil ${STYLES[brief.style].toLowerCase()}, ${tier === 'economic' ? 'variantele cele mai accesibile care se potrivesc' : tier === 'premium' ? 'variante de gamă superioară' : 'echilibru între preț și finisaje'}${brief.priorities.length ? `; priorități: ${brief.priorities.map(p => PRIORITIES[p].toLowerCase()).join(', ')}` : ''}. Pozițiile sunt calculate de motorul geometric, nu de propunere.` };
      const ev = evaluateVariant(snap, cat, mc, v, brief), misfit = ev.issues.filter(i => i.code === 'DOES_NOT_FIT');
      if (!misfit.length) break;
      // reîncearcă fără piesa care nu încape; scaunul nu încape doar pentru că biroul lui nu încape, deci rămâne în joc
      const bad = Object.keys(selections).filter(g => misfit.some(i => i.message.startsWith(G[g].label)));
      for (const g of bad) if (!(g === 'scaunBirou' && bad.includes('birou'))) banned.add(selections[g]!);
    }
    if (v) variants.push(v);
  }
  return { variants };
}
