// Adaptor pentru Claude (Anthropic Messages API). AI-ul primește doar ID-uri din catalog și întoarce JSON;
// orice răspuns e validat de core/proposal.ts înainte să ajungă la utilizator.
import type { Catalog, Snapshot, MaterialsCatalog } from '../core/types';
import { groups } from '../core/catalog';
import { floors } from '../core/levels';
import { computeBudget } from '../core/boq';
import type { DesignBrief } from '../core/brief';
export const aiConfigured = () => !!process.env.ANTHROPIC_API_KEY;
export const aiModel = () => process.env.ANTHROPIC_MODEL || 'claude-sonnet-5-5';

export function buildPrompt(snap: Snapshot, cat: Catalog, mc: MaterialsCatalog, brief: DesignBrief){
  const G = groups(cat), present = new Set(snap.placements.map(p => p.group)); if (present.has('bucatarie')) ['plita', 'cuptor', 'hota'].forEach(g => present.add(g));
  const lines = [...present].filter(g => G[g]).flatMap(g => G[g].variants.map(v => { const o = cat.offers.find(x => x.variantId === v.id), d = v.dimensionsCm;
    return `${v.id} | grupa=${g} | ${v.name} | ${o ? o.price + ' lei' : 'preț necunoscut'} | ${o?.provenance.source || '?'} | ${d ? `${d.w}x${d.d}x${d.h} cm` : 'dimensiuni necunoscute'} | stil=${JSON.stringify(v.style || {})}`; }));
  const mats = mc.materials.map(m => `${m.id} | ${m.category} | ${m.name} | ${m.unitPrice} lei/${m.unit} | ${m.supplier}`);
  const rooms = floors(snap).flatMap(f => f.rooms).map(r => `${r.id} | ${r.name} | tip=${r.type} | ${((r.rect.x1 - r.rect.x0) * (r.rect.z1 - r.rect.z0)).toFixed(1)} m²`);
  const system = `Ești designer de interior. Propui variante de amenajare DOAR prin alegeri din catalogul dat.
Reguli obligatorii:
- Folosești numai ID-urile din listele de mai jos. Nu inventezi produse, prețuri, dimensiuni, disponibilitate sau certificări.
- Nu dai coordonate: pozițiile le calculează motorul geometric al aplicației, care verifică dacă piesele încap.
- Generezi exact 3 variante cu tier "economic", "echilibrat", "premium". Nu declari niciuna „cea mai bună”.
- Fiecare motiv se bazează doar pe atributele din listă (preț, dimensiuni, stil, magazin) și pe brief.
- Răspunzi DOAR cu JSON valid, fără text în jur, după schema:
{"variants":[{"tier":"economic|echilibrat|premium","title":"…","summary":"…","palette":["#rrggbb"],"selections":{"<grupa>":"<variantId>"},"finishes":{"<roomId>":{"floor":"<materialId>","wallPaint":"<materialId>","wallTile":"<materialId>|null","baseboard":"<materialId>|null","light":"<materialId>"}},"reasons":[{"target":"…","reason":"…"}]}]}`;
  const user = `BRIEF: ${JSON.stringify(brief)}
BUGET ACTUAL ESTIMAT: ${Math.round(computeBudget(snap, cat, mc).chosen.total)} lei
CAMERE:\n${rooms.join('\n')}
PRODUSE (id | grupa | nume | preț | magazin | dimensiuni | stil):\n${lines.join('\n')}
MATERIALE (id | categorie | nume | preț | magazin):\n${mats.join('\n')}`;
  return { system, user };
}
export async function proposeWithClaude(snap: Snapshot, cat: Catalog, mc: MaterialsCatalog, brief: DesignBrief, fetchImpl: typeof fetch = fetch): Promise<unknown> {
  const { system, user } = buildPrompt(snap, cat, mc, brief);
  const r = await fetchImpl('https://api.anthropic.com/v1/messages', { method: 'POST', headers: { 'content-type': 'application/json', 'x-api-key': process.env.ANTHROPIC_API_KEY || '', 'anthropic-version': '2023-06-01' },
    body: JSON.stringify({ model: aiModel(), max_tokens: 4000, system, messages: [{ role: 'user', content: user }] }) });
  if (!r.ok) throw new Error(`AI a răspuns cu ${r.status}`);
  const data: any = await r.json(); const text = (data.content || []).filter((c: any) => c.type === 'text').map((c: any) => c.text).join('\n');
  const json = text.replace(/```json|```/g, '').trim(); const s = json.indexOf('{'), e = json.lastIndexOf('}');
  if (s < 0 || e < 0) throw new Error('AI nu a întors JSON.'); return JSON.parse(json.slice(s, e + 1));
}
