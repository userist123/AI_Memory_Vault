import { sanitizeAppearance, SIZE_LIMITS_CM } from '../core/appearance';
import { sanitizeTech } from '../core/technical';
import catalogSeed from '../data/catalog.v1.json';
// grupă → model 3D, ca materialele permise pe piesă să fie verificate fără a citi catalogul din baza de date
const MODEL_OF_GROUP: Record<string, string> = Object.fromEntries((catalogSeed as any).products.map((p: any) => [p.group, p.model3d]));
import { getDb } from './db';
import { newSnapshot } from '../core/project';
import { getTemplate } from '../core/templates';
import { validateFloor, validatePlacement } from '../core/validate';
import type { Catalog, Snapshot, MaterialsCatalog, Underlay } from '../core/types';
import { checkBrief } from '../core/brief';
import { diffSnapshots } from '../core/diff';
import { parseProposal, evaluateProposal, applyVariant, TIERS, type Tier, type RawProposal } from '../core/proposal';
import { proposeByRules } from '../core/proposer-rules';
import { aiConfigured, aiModel, proposeWithClaude } from './ai';

export class HttpError extends Error { constructor(public status: number, msg: string, public details?: unknown){ super(msg); } }
const MAX_SNAPSHOT = 1_000_000; // proiectul fără imaginea de calc
export const MAX_UNDERLAY_BYTES = 1_500_000; // imaginea de calc, după decodare

const cleanName = (s: unknown) => { const v = String(s ?? '').replace(/[\u0000-\u001f<>]/g, '').trim().slice(0, 120); if (!v) throw new HttpError(400, 'Numele proiectului lipsește.'); return v; };

export async function getCatalog(): Promise<Catalog> {
  const { q } = await getDb();
  const [s, p, v, o] = await Promise.all([q('select * from suppliers'), q('select * from products'), q('select * from variants order by legacy_index'), q('select * from offers')]);
  return { suppliers: s.rows.map(r => ({ id: r.id, name: r.name, country: r.country, website: r.website })),
    products: p.rows.map(r => ({ id: r.id, group: r.grp, name: r.name, brand: r.brand, category: r.category, model3d: r.model3d })),
    variants: v.rows.map(r => ({ id: r.id, productId: r.product_id, name: r.name, legacyIndex: r.legacy_index, dimensionsCm: r.dims, dimensionsConfidence: r.dims_confidence, style: r.style || {}, ...(r.chairs ? { chairs: r.chairs } : {}), ...(r.included_with ? { includedWith: r.included_with } : {}) })),
    offers: o.rows.map(r => ({ id: r.id, variantId: r.variant_id, supplierId: r.supplier_id, price: Number(r.price), currency: r.currency, availability: r.availability, affiliateUrl: r.affiliate_url,
      provenance: { source: r.source, sourceUrl: r.source_url, verifiedAt: r.verified_at ? new Date(r.verified_at).toISOString().slice(0, 10) : null, verificationType: r.verification_type, confidence: r.confidence } })) };
}
export async function getMaterials(): Promise<MaterialsCatalog> {
  const { q } = await getDb(); const [m, l, sv] = await Promise.all([q('select * from materials order by category, unit_price'), q('select * from labor_rates'), q('select * from services')]);
  const d = (v: any) => v ? new Date(v).toISOString().slice(0, 10) : null, n = (v: any) => v == null ? undefined : Number(v);
  return { verifiedAt: d(m.rows[0]?.verified_at) || '', materials: m.rows.map(r => ({ id: r.id, category: r.category, name: r.name, supplier: r.supplier, unit: r.unit, unitPrice: Number(r.unit_price), ...(r.pack ? { pack: { ...r.pack, ...(r.pack.price != null ? { price: Number(r.pack.price) } : {}) } } : {}), ...(r.coverage != null ? { coverage: n(r.coverage) } : {}), ...(r.consumption != null ? { consumption: n(r.consumption) } : {}), sourceUrl: r.source_url, verificationType: r.verification_type, confidence: r.confidence, ...(r.note ? { note: r.note } : {}) })),
    labor: l.rows.map(r => ({ id: r.id, label: r.label, unit: r.unit, low: Number(r.low), expected: Number(r.expected), high: Number(r.high), sources: r.sources, confidence: r.confidence })),
    services: sv.rows.map(r => ({ id: r.id, label: r.label, supplier: r.supplier, ...(r.price != null ? { price: Number(r.price) } : {}), ...(r.price_per_meter != null ? { pricePerMeter: Number(r.price_per_meter) } : {}), sourceUrl: r.source_url, verificationType: r.verification_type, confidence: r.confidence, ...(r.note ? { note: r.note } : {}) })) } as MaterialsCatalog;
}
/** Imaginea de calc: doar PNG/JPEG în data URL, ≤ 1,5 MB decodat, numere finite. Orice altceva e respins cu 400. */
export function checkUnderlay(u: any): Underlay {
  const bad = (m: string) => new HttpError(400, m), fin = (v: unknown) => typeof v === 'number' && Number.isFinite(v);
  if (!u || typeof u !== 'object' || Array.isArray(u)) throw bad('Imaginea de calc este invalidă.');
  const m = typeof u.dataUrl === 'string' ? /^data:image\/(png|jpeg);base64,([A-Za-z0-9+/]*={0,2})$/.exec(u.dataUrl) : null;
  if (!m) throw bad('Imaginea de calc trebuie să fie PNG sau JPEG.');
  const b64 = m[2]!; if (b64.length < 8 || b64.length % 4 !== 0) throw bad('Imaginea de calc este coruptă.');
  const bytes = b64.length / 4 * 3 - (b64.endsWith('==') ? 2 : b64.endsWith('=') ? 1 : 0);
  if (bytes > MAX_UNDERLAY_BYTES) throw bad('Imaginea de calc depășește 1,5 MB.');
  const head = Buffer.from(b64.slice(0, 16), 'base64'), isPng = head[0] === 0x89 && head[1] === 0x50 && head[2] === 0x4e && head[3] === 0x47, isJpg = head[0] === 0xff && head[1] === 0xd8;
  if ((m[1] === 'png' && !isPng) || (m[1] === 'jpeg' && !isJpg)) throw bad('Conținutul imaginii de calc nu corespunde tipului declarat.');
  if (!fin(u.x) || !fin(u.z) || Math.abs(u.x) > 1000 || Math.abs(u.z) > 1000) throw bad('Poziția imaginii de calc este invalidă.');
  if (!fin(u.widthM) || u.widthM < 1 || u.widthM > 100) throw bad('Lățimea imaginii de calc trebuie să fie între 1 și 100 m.');
  if (!fin(u.opacity) || u.opacity < 0.1 || u.opacity > 1) throw bad('Opacitatea imaginii de calc trebuie să fie între 0,1 și 1.');
  if (typeof u.locked !== 'boolean') throw bad('Starea de blocare a imaginii de calc este invalidă.');
  return { dataUrl: u.dataUrl, x: u.x, z: u.z, widthM: u.widthM, opacity: u.opacity, locked: u.locked };
}
export function checkSnapshot(s: any): Snapshot {
  if (!s || typeof s !== 'object' || !s.floor || !Array.isArray(s.floor.rooms) || !Array.isArray(s.floor.walls) || !Array.isArray(s.placements) || typeof s.selections !== 'object') throw new HttpError(400, 'Structura proiectului e invalidă.');
  const { underlay: rawUnderlay, ...rest } = s;
  if (JSON.stringify(rest).length > MAX_SNAPSHOT) throw new HttpError(413, 'Proiectul e prea mare.');
  if (rawUnderlay != null) s.underlay = checkUnderlay(rawUnderlay);
  const num = (v: unknown) => typeof v === 'number' && Number.isFinite(v);
  for (const r of s.floor.rooms) if (!['x0', 'z0', 'x1', 'z1'].every(k => num(r.rect?.[k])) || r.rect.x1 <= r.rect.x0 || r.rect.z1 <= r.rect.z0) throw new HttpError(400, `Camera ${r.id} are dimensiuni invalide.`);
  for (const w of s.floor.walls) if (!num(w.a?.[0]) || !num(w.a?.[1]) || !num(w.b?.[0]) || !num(w.b?.[1]) || !Array.isArray(w.openings)) throw new HttpError(400, `Peretele ${w.id} e invalid.`);
  for (const p of s.placements) if (!num(p.x) || !num(p.z) || !num(p.rotation) || typeof p.variantId !== 'string') throw new HttpError(400, 'O piesă de mobilier e invalidă.');
  // dimensiuni pe comandă (cm) și goluri cu înălțime/parapet: doar valori plauzibile
  const cm = (v: unknown) => num(v) && (v as number) >= SIZE_LIMITS_CM.min && (v as number) <= SIZE_LIMITS_CM.max;
  for (const p of s.placements){ if (p.size == null){ delete p.size; continue; }
    if (!(cm(p.size?.w) && cm(p.size?.d) && cm(p.size?.h))) throw new HttpError(400, `Dimensiunile pe comandă trebuie să fie între ${SIZE_LIMITS_CM.min} și ${SIZE_LIMITS_CM.max} cm.`);
    p.size = { w: Math.round(p.size.w), d: Math.round(p.size.d), h: Math.round(p.size.h) }; } // doar w/d/h, în cm întregi
  for (const w of s.floor.walls) for (const o of w.openings){ if (o.height != null && !(num(o.height) && o.height >= 0.3 && o.height <= 3)) throw new HttpError(400, 'Înălțimea golului trebuie să fie între 30 și 300 cm.');
    if (o.sill != null && !(num(o.sill) && o.sill >= 0 && o.sill <= 2)) throw new HttpError(400, 'Parapetul ferestrei trebuie să fie între 0 și 200 cm.');
    // golul trebuie să încapă sub tavan (ușa de la podea, fereastra de la parapet)
    const top = (o.kind === 'window' ? (o.sill ?? 0.9) : 0) + (o.height ?? (o.kind === 'door' ? 2.1 : 1.3)), ceil = num(s.floor.ceilingHeight) ? s.floor.ceilingHeight : 2.6;
    if ((o.height != null || o.sill != null) && top > ceil + 1e-9) throw new HttpError(400, `Golul depășește tavanul: are ${Math.round(top * 100)} cm, iar tavanul ${Math.round(ceil * 100)} cm.`); }
  if (s.tech != null){ const t = sanitizeTech(s.tech, s); if (t) s.tech = t; else delete s.tech; }
  if (s.appearance != null){ const a = sanitizeAppearance(s.appearance, s, p => MODEL_OF_GROUP[p.group] ?? ''); if (a) s.appearance = a; else delete s.appearance; }
  if (s.finishes != null && (typeof s.finishes !== 'object' || Array.isArray(s.finishes))) throw new HttpError(400, 'Finisajele sunt invalide.');
  if (s.budget != null){ const b = s.budget; const okNum = (v: unknown) => v == null || (typeof v === 'number' && Number.isFinite(v) && v >= 0);
    if (typeof b !== 'object' || !okNum(b.target) || !okNum(b.contingencyPct) || !okNum(b.deliveryDedeman) || !okNum(b.furnitureAssembly) || !okNum(b.design) || (b.contingencyPct ?? 0) > 100) throw new HttpError(400, 'Setările de buget sunt invalide.'); }
  if (s.brief != null){ try { s.brief = checkBrief(s.brief); } catch { delete s.brief; } }
  s.name = cleanName(s.name); s.picked = Array.isArray(s.picked) ? s.picked.filter((x: unknown) => typeof x === 'string') : [];
  return s as Snapshot;
}
export async function listProjects(owner: string){
  const { q } = await getDb(); const { rows } = await q('select id, name, updated_at, current_revision from projects where owner=$1 order by updated_at desc', [owner]); return rows;
}
export async function createProject(owner: string, name: unknown, template: unknown = 'demo'){
  if (typeof template !== 'string' || (template !== 'blank' && !getTemplate(template))) throw new HttpError(400, 'Șablon necunoscut. Alege unul dintre șabloanele din listă sau „Plan gol”.');
  const { q } = await getDb(); const cat = await getCatalog(); const n = cleanName(name);
  const snap = newSnapshot(cat, n, template);
  const { rows } = await q('insert into projects(owner, name, draft) values($1,$2,$3) returning id', [owner, n, JSON.stringify(snap)]);
  return rows[0].id as string;
}
async function own(owner: string, id: string){
  if (!/^[0-9a-f-]{36}$/i.test(id)) throw new HttpError(404, 'Proiect inexistent.');
  const { q } = await getDb(); const { rows } = await q('select * from projects where id=$1 and owner=$2', [id, owner]);
  if (!rows[0]) throw new HttpError(404, 'Proiect inexistent.'); return rows[0];
}
export async function getProject(owner: string, id: string){ const r = await own(owner, id); return { id: r.id, name: r.name, updatedAt: r.updated_at, currentRevision: r.current_revision, draft: r.draft as Snapshot }; }
export async function saveDraft(owner: string, id: string, snapshot: unknown){
  const s = checkSnapshot(snapshot); await own(owner, id); const { q } = await getDb();
  const { rows } = await q('update projects set draft=$1, name=$2, updated_at=now() where id=$3 and owner=$4 returning updated_at', [JSON.stringify(s), s.name, id, owner]);
  return { updatedAt: rows[0].updated_at };
}
/** Erorile care împiedică o revizie, pe tot proiectul (aceeași regulă și pentru aplicarea unui design). */
export function projectErrors(snap: Snapshot, cat: Catalog){
  return [...validateFloor(snap.floor).filter(i => i.severity === 'ERROR'), ...snap.placements.flatMap(pl => validatePlacement(snap, cat, pl).filter(i => i.severity === 'ERROR'))];
}
// O revizie se creează doar dacă proiectul nu are erori (constituția: ERROR blochează aplicarea).
export async function createRevision(owner: string, id: string, note: unknown){
  const p = await own(owner, id), cat = await getCatalog(), snap = p.draft as Snapshot;
  const errors = projectErrors(snap, cat);
  if (errors.length) throw new HttpError(409, 'Proiectul are erori care trebuie rezolvate înainte de a salva o revizie.', errors);
  // Numărul reviziei și instantaneul se iau în aceeași tranzacție, din același rând: două salvări concurente nu se ciocnesc.
  const { tx } = await getDb();
  return tx(async q => {
    // Se salvează exact draftul validat mai sus; dacă între timp s-a schimbat, cererea se repetă (409), nu se salvează nevalidat.
    const { rows } = await q('update projects set current_revision=current_revision+1, updated_at=now() where id=$1 and owner=$2 and draft = $3::jsonb returning current_revision, draft', [id, owner, JSON.stringify(snap)]);
    if (!rows[0]) throw new HttpError(409, 'Proiectul s-a schimbat în timpul salvării; încearcă din nou.');
    const n = rows[0].current_revision as number;
    await q('insert into revisions(project_id, number, note, snapshot) values($1,$2,$3,$4)', [id, n, String(note ?? '').slice(0, 300), JSON.stringify(rows[0].draft)]);
    return { number: n };
  });
}
export async function listRevisions(owner: string, id: string){ await own(owner, id); const { q } = await getDb(); const { rows } = await q('select number, note, created_at from revisions where project_id=$1 order by number desc', [id]); return rows; }
export async function restoreRevision(owner: string, id: string, number: number){
  await own(owner, id); const { q } = await getDb();
  const { rows } = await q('select snapshot from revisions where project_id=$1 and number=$2', [id, number]); if (!rows[0]) throw new HttpError(404, 'Revizie inexistentă.');
  await q('update projects set draft=$1, updated_at=now() where id=$2', [JSON.stringify(rows[0].snapshot), id]); return rows[0].snapshot as Snapshot;
}
/** Ce s-a schimbat între două revizii (sau între o revizie și draftul curent). Aceleași verificări de proprietate ca restaurarea. */
export async function diffRevisions(owner: string, id: string, from: number, to: number | 'draft'){
  const okN = (n: unknown): n is number => typeof n === 'number' && Number.isInteger(n) && n >= 1 && n <= 2_000_000_000;
  if (!okN(from) || (to !== 'draft' && !okN(to))) throw new HttpError(400, 'Numere de revizie invalide.');
  const p = await own(owner, id), { q } = await getDb();
  const load = async (n: number) => { const { rows } = await q('select snapshot from revisions where project_id=$1 and number=$2', [id, n]); if (!rows[0]) throw new HttpError(404, 'Revizie inexistentă.'); return rows[0].snapshot as Snapshot; };
  const before = await load(from), after = to === 'draft' ? p.draft as Snapshot : await load(to);
  return { from, to, ...diffSnapshots(before, after, await getCatalog()) };
}
export async function deleteProject(owner: string, id: string){ await own(owner, id); const { q } = await getDb(); await q('delete from projects where id=$1', [id]); }

// ---------- Faza 3: propuneri de design ----------
const publicVariants = (evs: ReturnType<typeof evaluateProposal>) => evs.map(e => ({ ...e }));
export async function generateProposal(owner: string, id: string, briefInput: unknown, deps: { fetchImpl?: typeof fetch } = {}){
  let brief; try { brief = checkBrief(briefInput); } catch (e: any){ throw new HttpError(400, e.message); }
  const p = await own(owner, id), cat = await getCatalog(), mc = await getMaterials(), snap = p.draft as Snapshot;
  snap.brief = brief; const { q } = await getDb(); await q('update projects set draft=$1, updated_at=now() where id=$2', [JSON.stringify(snap), id]);
  let raw: RawProposal | null = null, source = 'rules', model: string | null = null; const notes: string[] = [];
  if (aiConfigured()){
    try { const out = await proposeWithClaude(snap, cat, mc, brief, deps.fetchImpl); const parsed = parseProposal(out); notes.push(...parsed.errors);
      if (parsed.proposal){ raw = parsed.proposal; source = 'ai'; model = aiModel(); } else notes.push('Răspunsul AI nu a respectat formatul; am folosit motorul de reguli.'); }
    catch (e: any){ notes.push(`AI indisponibil (${e.message}); am folosit motorul de reguli.`); }
  } else notes.push('AI neconfigurat pe server (lipsește ANTHROPIC_API_KEY): variantele sunt generate de motorul de reguli.');
  if (!raw) raw = proposeByRules(snap, cat, mc, brief);
  const evs = evaluateProposal(snap, cat, mc, raw, brief);
  const { rows } = await q('insert into proposals(project_id, source, model, brief, raw, notes) values($1,$2,$3,$4,$5,$6) returning id, created_at', [id, source, model, JSON.stringify(brief), JSON.stringify(raw), JSON.stringify(notes)]);
  return { id: rows[0].id as string, createdAt: rows[0].created_at, source, model, aiGenerated: source === 'ai', notes, variants: publicVariants(evs) };
}
async function proposalOf(id: string, pid: string){ if (!/^[0-9a-f-]{36}$/i.test(pid)) throw new HttpError(404, 'Propunere inexistentă.');
  const { q } = await getDb(); const { rows } = await q('select * from proposals where id=$1 and project_id=$2', [pid, id]); if (!rows[0]) throw new HttpError(404, 'Propunere inexistentă.'); return rows[0]; }
// Aplicarea se re-validează pe server, pe proiectul curent (clientul nu e de încredere).
export async function applyProposal(owner: string, id: string, pid: string, tier: string, confirmWarnings: boolean){
  if (!TIERS.includes(tier as Tier)) throw new HttpError(400, 'Variantă necunoscută.');
  const p = await own(owner, id), row = await proposalOf(id, pid), cat = await getCatalog(), mc = await getMaterials();
  const raw = row.raw as RawProposal, v = raw.variants.find(x => x.tier === tier); if (!v) throw new HttpError(404, 'Varianta nu există în propunere.');
  const [ev] = evaluateProposal(p.draft as Snapshot, cat, mc, { variants: [v] }, row.brief);
  const res = applyVariant(ev, confirmWarnings); if (!res.ok) throw new HttpError(409, res.reason, ev.issues);
  const { q } = await getDb(); await q('update projects set draft=$1, updated_at=now() where id=$2', [JSON.stringify(res.snapshot), id]);
  await q("update proposals set decisions = decisions || $1::jsonb where id=$2", [JSON.stringify({ [tier]: { decision: 'approved', at: new Date().toISOString() } }), pid]);
  const rev = await createRevision(owner, id, `Varianta ${tier} aplicată (${row.source === 'ai' ? 'propusă de AI' : 'propusă de motorul de reguli'})`);
  return { snapshot: res.snapshot, revision: rev.number };
}
export async function rejectProposal(owner: string, id: string, pid: string, tier: string){
  await own(owner, id); await proposalOf(id, pid); if (!TIERS.includes(tier as Tier)) throw new HttpError(400, 'Variantă necunoscută.');
  const { q } = await getDb(); await q("update proposals set decisions = decisions || $1::jsonb where id=$2", [JSON.stringify({ [tier]: { decision: 'rejected', at: new Date().toISOString() } }), pid]); return { ok: true };
}
export async function listProposals(owner: string, id: string){ await own(owner, id); const { q } = await getDb();
  const { rows } = await q('select id, source, model, decisions, created_at from proposals where project_id=$1 order by created_at desc limit 20', [id]); return rows; }
