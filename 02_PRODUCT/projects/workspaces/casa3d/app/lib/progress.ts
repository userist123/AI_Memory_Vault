// Situațiile de lucrări ale proiectului (doar proprietarul): devizul de bază înghețat (progress_baseline) și situațiile pe
// perioade (progress_statements). Calculele sunt în core/progress.ts; aici e doar persistența și „înghețarea” ofertei curente.
// Nimic de aici nu ajunge pe linkurile clientului (rutele publice /api/share/* nu citesc aceste tabele).
import { randomUUID } from 'node:crypto';
import { getDb } from './db';
import { HttpError, getProject } from './repo';
import { getProfile } from './studio-profile';
import { getCommercialData } from './commercial';
import { projectBudget } from './contract';
import { floors } from '../core/levels';
import { commercialSources, profitLine } from '../core/commercial';
import { checkBaseline, checkStatement, statementIssues, ProgressError, PROGRESS_LIMITS, type Baseline, type BaseLine, type StatementInput } from '../core/progress';

type Q = (s: string, p?: unknown[]) => Promise<{ rows: any[] }>;
const r2 = (n: number) => Math.round((n + Number.EPSILON) * 100) / 100;
const bad = <T>(fn: () => T, status = 400): T => { try { return fn(); } catch (e: any){ if (e instanceof ProgressError) throw new HttpError(status, e.message); throw e; } };
const SID_RE = /^[\w:.@#/-]{1,120}$/;
const notFound = () => new HttpError(404, 'Situație inexistentă.');

async function readBaseline(q: Q, id: string): Promise<Baseline | null> {
  const { rows } = await q('select data from progress_baseline where project_id=$1', [id]); if (!rows[0]) return null;
  // un rând care nu mai trece validarea nu e tratat ca „fără deviz” (s-ar putea fixa altul peste el): 409, rândul rămâne neatins
  return bad(() => checkBaseline(rows[0].data), 409);
}
async function readStatements(q: Q, id: string, base: Baseline | null): Promise<StatementInput[]> {
  if (!base) return [];
  const { rows } = await q('select id, number, data from progress_statements where project_id=$1 order by number', [id]);
  return rows.map(r => bad(() => checkStatement({ ...r.data, id: r.id, number: r.number }, base), 409));
}

/** Devizul de bază (sau null), situațiile în ordinea numărului și avertismentele de numerotare / perioade. */
export async function getProgress(owner: string, id: string){
  const p = await getProject(owner, id), { q } = await getDb(), baseline = await readBaseline(q as Q, id), statements = await readStatements(q as Q, id, baseline);
  return { name: p.name, client: p.meta?.client ?? null, siteAddress: p.meta?.siteAddress ?? null, baseline, statements, issues: statementIssues(statements) };
}
/** La fel, plus executantul (profilul studioului), pentru documentul tipăribil. */
export async function getProgressDoc(owner: string, id: string){ return { ...await getProgress(owner, id), seller: await getProfile(owner) }; }

/** Codul unei linii de ofertă, adus la forma acceptată de core/progress.ts (fără „|”, spații etc.), unic în deviz. */
function lineKey(prefix: string, key: string, seen: Set<string>): string {
  const b = (prefix + ':' + key.replace(/[^\w:.@#/-]+/g, '_')).slice(0, 110); let k = b, i = 2; while (seen.has(k)) k = `${b}.${i++}`;
  seen.add(k); return k;
}
/**
 * Liniile ofertei CURENTE, la prețul către client (adaosul din termenii comerciali): produsele (și cele proprii), manopera
 * (preț unitar = valoare / cantitate) și serviciile (transport, montaj: cantitate 1). Liniile fără preț sau fără cantitate
 * nu intră; numărul lor e raportat. Onorariul de proiectare nu e lucrare executată pe șantier, deci nu intră.
 */
export async function offerLines(owner: string, id: string): Promise<{ lines: BaseLine[]; skipped: number }> {
  const p = await getProject(owner, id), data = await getCommercialData(owner, id), { budget: b, custom } = await projectBudget(p);
  const { items, labor, extras } = commercialSources(b, custom), seen = new Set<string>(), lines: BaseLine[] = []; let skipped = b.extras.filter(e => e.amount == null).length;
  // aceeași lucrare apare în mai multe camere: denumirea primește camera, ca pe situație să se știe ce s-a executat unde
  const roomName = new Map(floors(p.draft).flatMap(f => f.rooms).map(r => [r.id, r.name] as const)), itemRoom = new Map(b.items.map(i => [i.key, i.roomId] as const));
  // fără dublare: liniile de materiale poartă deja camera („Pardoseală · Hol (decalat 1/3)”), ca segment după „ · ”
  const hasRoom = (label: string, n: string) => label.split(' · ').slice(1).some(seg => seg === n || seg.startsWith(n + ' (') || seg.startsWith(n + ','));
  const withRoom = (label: string, roomId: string | null | undefined) => { const n = roomId ? roomName.get(roomId) : undefined; return n && !hasRoom(label, n) ? `${label} · ${n}` : label; };
  for (const s of items){ const l = profitLine(data, s);
    if (l.client == null || l.clientUnit == null || !(l.qty > 0)){ skipped++; continue; }
    lines.push({ key: lineKey('m', s.key, seen), label: withRoom(s.label, itemRoom.get(s.key)), unit: s.unit || 'buc', qty: l.qty, unitPrice: l.clientUnit, kind: 'material' }); }
  for (const l of labor){ if (!(l.qty > 0) || !(l.amount > 0)){ skipped++; continue; }
    lines.push({ key: lineKey('l', l.key, seen), label: withRoom(l.label, l.key.split(':')[0]), unit: l.unit || 'buc', qty: l.qty, unitPrice: r2(l.amount / l.qty), kind: 'labor' }); }
  for (const e of extras) lines.push({ key: lineKey('s', e.key, seen), label: e.label, unit: 'serv', qty: 1, unitPrice: r2(e.amount), kind: 'service' });
  return { lines, skipped };
}
/**
 * Fixează devizul de bază: copia înghețată a ofertei de acum. Cât nu există situații se poate re-fixa (înlocuiește copia);
 * după prima situație, re-fixarea e refuzată (409): cantitățile decontate se referă la liniile vechi.
 */
export async function fixBaseline(owner: string, id: string, now = new Date()){
  const { lines, skipped } = await offerLines(owner, id);
  if (!lines.length) throw new HttpError(400, 'Oferta nu are nicio linie cu preț și cantitate; nu există ce fixa ca deviz de bază.');
  // prețurile ofertei către client includ TVA: devizul de bază o ține minte, iar situațiile nu mai adaugă TVA
  const baseline = bad(() => checkBaseline({ createdAt: now.toISOString(), lines, vatIncluded: true }));
  const { tx } = await getDb();
  await tx(async q => {
    await q('select id from projects where id=$1 for update', [id]);
    const { rows: [c] } = await q('select count(*)::int as n from progress_statements where project_id=$1', [id]);
    if (c.n > 0) throw new HttpError(409, `Devizul de bază nu se mai poate re-fixa: există ${c.n === 1 ? 'o situație de lucrări întocmită' : `${c.n} situații de lucrări întocmite`} pe el. Șterge întâi situațiile (de la ultima spre prima), apoi fixează din nou.`);
    await q('insert into progress_baseline(project_id, data) values($1,$2) on conflict(project_id) do update set data=excluded.data, created_at=now()', [id, JSON.stringify(baseline)]);
  });
  return { ...await getProgress(owner, id), skipped };
}

/**
 * Salvează o situație. Fără `sid`: situație nouă (id-ul și numărul lipsă le dă serverul: următorul număr); numărul trebuie
 * să fie unic. Cu `sid`: înlocuiește situația existentă, păstrându-i numărul (numerotarea nu se schimbă la editare).
 */
export async function saveStatement(owner: string, id: string, input: unknown, sid?: string){
  await getProject(owner, id); const { tx } = await getDb();
  if (sid != null && !SID_RE.test(sid)) throw notFound();
  if (!input || typeof input !== 'object' || Array.isArray(input)) throw new HttpError(400, 'Situația nu e validă.');
  const saved = await tx(async q => {
    await q('select id from projects where id=$1 for update', [id]);
    const base = await readBaseline(q as Q, id); if (!base) throw new HttpError(409, 'Fixează întâi devizul de bază; situațiile se întocmesc față de el.');
    const { rows } = await q('select id, number from progress_statements where project_id=$1 order by number', [id]);
    const o = { ...(input as any) };
    if (sid != null){ const cur = rows.find((r: any) => r.id === sid); if (!cur) throw notFound(); o.id = sid; o.number = cur.number; }
    else { if (rows.length >= PROGRESS_LIMITS.statements) throw new HttpError(409, `Cel mult ${PROGRESS_LIMITS.statements} situații pe proiect.`);
      if (o.id == null) o.id = 's' + randomUUID(); if (o.number == null) o.number = rows.reduce((a: number, r: any) => Math.max(a, r.number), 0) + 1; }
    if (Array.isArray(o.extra) && o.extra.length > PROGRESS_LIMITS.extra) throw new HttpError(400, `Cel mult ${PROGRESS_LIMITS.extra} lucrări suplimentare pe o situație.`);
    const s = bad(() => checkStatement(o, base));
    if (new Set(s.extra.map(l => l.key)).size !== s.extra.length) throw new HttpError(400, 'Două lucrări suplimentare au același cod.');
    if (sid == null){ if (rows.some((r: any) => r.id === s.id)) throw new HttpError(409, 'Există deja o situație cu acest identificator.');
      if (rows.some((r: any) => r.number === s.number)) throw new HttpError(409, `Există deja situația nr. ${s.number}.`); }
    const { id: _i, number: _n, ...data } = s;
    await q('insert into progress_statements(id, project_id, number, data) values($1,$2,$3,$4) on conflict(project_id, id) do update set data=excluded.data, updated_at=now()', [s.id, id, s.number, JSON.stringify(data)]);
    return s; });
  return { statement: saved, ...await getProgress(owner, id) };
}
/** Șterge o situație: doar ultima (numărul cel mai mare), ca numerotarea să rămână consecutivă. */
export async function deleteStatement(owner: string, id: string, sid: string){
  await getProject(owner, id); if (!SID_RE.test(sid)) throw notFound(); const { tx } = await getDb();
  await tx(async q => {
    await q('select id from projects where id=$1 for update', [id]);
    const { rows } = await q('select id, number from progress_statements where project_id=$1 order by number desc', [id]);
    if (!rows.some((r: any) => r.id === sid)) throw notFound();
    if (rows[0].id !== sid) throw new HttpError(409, `Se poate șterge doar ultima situație (nr. ${rows[0].number}), ca numerotarea să rămână consecutivă.`);
    await q('delete from progress_statements where project_id=$1 and id=$2', [id, sid]);
  });
  return getProgress(owner, id);
}
