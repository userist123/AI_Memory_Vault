// Chestionarul clientului (core/client-brief.ts), pe server. Designerul creează un link public; clientul completează fără
// cont, cu salvare automată, și poate adăuga poze de inspirație. Scrierea publică cere tokenul unui link activ și netrimis,
// e validată strict și limitată; după trimitere linkul nu mai arată nimic. Răspunsurile și pozele le vede doar proprietarul
// proiectului, care alege câmp cu câmp ce preia în brief-ul lui. Pozele se păstrează doar ca JPEG reconstruit (fără EXIF/GPS).
import { randomBytes } from 'node:crypto';
import { getDb, type Query } from './db';
import { HttpError, getProject } from './repo';
import { TOKEN_BYTES, isShareToken, shareActive, shareDays, publicBase } from './share';
import { checkBrief, DEFAULT_BRIEF, type DesignBrief } from '../core/brief';
import { isUuid } from '../core/site';
import { checkClientBrief, briefFromClient, stripJpeg, cleanText, ClientBriefError, JpegError, type ClientBrief } from '../core/client-brief';
import type { Snapshot } from '../core/types';

export const BRIEF_DEFAULT_DAYS = 30;
export const BRIEF_LIMITS = { bodyBytes: 24 * 1024, photoBytes: 1.5 * 1024 * 1024, photos: 12, totalBytes: 12 * 1024 * 1024, note: 200 };
export const PHOTO_VERDICTS = ['love', 'ok', 'dislike'] as const;
export type PhotoVerdict = typeof PHOTO_VERDICTS[number];
export interface BriefPhoto { id: string; verdict: PhotoVerdict | null; note: string; width?: number; height?: number }

const NOT_FOUND = () => new HttpError(404, 'Link inexistent.');
const SUBMITTED = () => new HttpError(409, 'Chestionarul a fost deja trimis.', { submitted: true });
const iso = (v: unknown) => v ? new Date(v as string).toISOString() : null;
const v = <T>(fn: () => T): T => { try { return fn(); } catch (e: any){ if (e instanceof ClientBriefError) throw new HttpError(400, e.message, (e as any).missing ? { missing: (e as any).missing } : undefined); if (e instanceof JpegError) throw new HttpError(400, e.message, { code: e.code }); throw e; } };
const photoRow = (r: any, dims = false): BriefPhoto => ({ id: r.id, verdict: r.verdict ?? null, note: r.note ?? '', ...(dims ? { width: r.width, height: r.height } : {}) });
const isObj = (x: unknown): x is Record<string, unknown> => !!x && typeof x === 'object' && !Array.isArray(x);

// ---------- designer ----------
async function latest(q: Query, id: string){ return (await q(`select *, (${shareActive()}) as active from client_briefs where project_id=$1 order by created_at desc limit 1`, [id])).rows[0] ?? null; }
async function stateOf(q: Query, id: string){
  const r = await latest(q, id); if (!r) return null;
  const photos = (await q('select id, verdict, note, width, height from brief_photos where token=$1 order by created_at, id', [r.token])).rows.map(p => photoRow(p, true));
  const base = publicBase(), active = !!r.active, path = `/brief/${r.token}`;
  // cheia linkului se întoarce doar cât linkul e activ (ca să poată fi copiat); un link revocat sau expirat nu mai are ce arăta
  return { active, ...(active ? { token: r.token as string, path, url: base ? base + path : null } : {}), publicUrl: !!base, createdAt: iso(r.created_at)!, updatedAt: iso(r.updated_at)!, submittedAt: iso(r.submitted_at),
    expiresAt: iso(r.expires_at), revokedAt: iso(r.revoked_at), importedAt: iso(r.imported_at), answered: Object.keys(r.data ?? {}).some(k => k !== 'v'), data: (r.data ?? {}) as Partial<ClientBrief>, photos };
}
/** Starea chestionarului pentru designer: linkul, răspunsurile, pozele și versiunea proiectului (pentru „Preia în brief”). */
export async function getClientBrief(owner: string, id: string){
  const p = await getProject(owner, id), { q } = await getDb();
  return { brief: await stateOf(q, id), projectRev: p.draftRev, currency: await currencyOf(q, id) };
}
const currencyOf = async (q: Query, id: string) => String((await q('select currency from projects where id=$1', [id])).rows[0]?.currency ?? 'RON');
/** Link nou. Proiectul are un singur chestionar: linkul vechi (activ, revocat sau expirat) încetează, iar răspunsurile și
 *  pozele strânse până acum trec pe linkul nou, redeschise pentru client. Ca să pornești de la zero, șterge întâi chestionarul. */
export async function createBriefLink(owner: string, id: string, expiresInDays?: unknown){
  const days = expiresInDays == null ? BRIEF_DEFAULT_DAYS : shareDays(expiresInDays);
  await getProject(owner, id); const { tx } = await getDb(), token = randomBytes(TOKEN_BYTES).toString('base64url');
  return tx(async q => {
    // două cereri simultane nu creează două linkuri; un proiect mutat între timp în coș nu primește link nou
    if (!(await q('select id from projects where id=$1 and deleted_at is null for update', [id])).rows[0]) throw new HttpError(404, 'Proiect inexistent.');
    const old = (await q('select token, data, imported_at from client_briefs where project_id=$1 order by created_at desc', [id])).rows;
    await q('update client_briefs set revoked_at=now() where project_id=$1 and revoked_at is null', [id]);
    await q(`insert into client_briefs(token, project_id, data, imported_at, expires_at) values($1,$2,$3,$4, case when $5::int = 0 then null else now() + make_interval(days => $5::int) end)`,
      [token, id, JSON.stringify(old[0]?.data ?? {}), old[0]?.imported_at ?? null, days]);
    if (old.length){ const tokens = old.map(o => o.token);
      await q('update brief_photos set token=$1 where token = any($2::text[])', [token, tokens]);
      await q('delete from client_briefs where token = any($1::text[])', [tokens]); }
    return { brief: (await stateOf(q, id))! };
  });
}
/** Revocarea oprește linkul; răspunsurile și pozele rămân la designer. */
export async function revokeBriefLink(owner: string, id: string){
  await getProject(owner, id); const { q } = await getDb();
  const { rows } = await q('update client_briefs set revoked_at=now() where project_id=$1 and revoked_at is null returning token', [id]);
  if (!rows[0] && !(await latest(q, id))) throw new HttpError(404, 'Proiectul nu are chestionar.');
  return { brief: await stateOf(q, id) };
}
/** Redeschide un chestionar trimis: clientul îl poate completa din nou pe același link (dacă linkul mai e activ). */
export async function reopenClientBrief(owner: string, id: string){
  await getProject(owner, id); const { q } = await getDb(), r = await latest(q, id);
  if (!r) throw new HttpError(404, 'Proiectul nu are chestionar.');
  await q('update client_briefs set submitted_at=null, updated_at=now() where token=$1', [r.token]);
  return { brief: await stateOf(q, id) };
}
/** Șterge chestionarul cu totul: linkul, răspunsurile și pozele clientului. Ce a fost preluat în brief rămâne în proiect. */
export async function deleteClientBrief(owner: string, id: string){
  await getProject(owner, id); const { q } = await getDb();
  const { rows } = await q('delete from client_briefs where project_id=$1 returning token', [id]);
  if (!rows[0]) throw new HttpError(404, 'Proiectul nu are chestionar.');
  return { brief: null };
}
/** Poza de inspirație, pentru designer: doar dacă aparține unui chestionar al acestui proiect. */
export async function readBriefPhoto(owner: string, id: string, photoId: unknown){
  if (!isUuid(photoId)) throw new HttpError(404, 'Poză inexistentă.'); await getProject(owner, id);
  const { q } = await getDb(), { rows } = await q('select p.mime, p.data from brief_photos p join client_briefs b on b.token = p.token where p.id=$1 and b.project_id=$2', [photoId, id]);
  if (!rows[0]) throw new HttpError(404, 'Poză inexistentă.'); return { mime: rows[0].mime as string, data: new Uint8Array(rows[0].data) };
}
/** „Preia în brief”: scrie în brief-ul proiectului doar câmpurile bifate, recalculate pe server din răspunsurile stocate.
 *  Conflictul se verifică fie pe `base` = brief-ul văzut ca „actual” în previzualizare (editorul își salvează singur ciorna,
 *  deci versiunea proiectului crește și fără vreo schimbare de brief: contează doar brief-ul), fie pe `rev` = draft_rev. */
export async function importClientBrief(owner: string, id: string, input: unknown){
  const b = input as any; if (!isObj(b) || !Array.isArray(b.fields) || !b.fields.every((k: unknown) => typeof k === 'string' && Object.hasOwn(DEFAULT_BRIEF, k)) || new Set(b.fields).size !== b.fields.length) throw new HttpError(400, 'Lista câmpurilor de preluat este invalidă.');
  if (!b.fields.length) throw new HttpError(400, 'Alege cel puțin un câmp de preluat.');
  const hasBase = isObj(b.base); if (!hasBase && !isRev(b.rev)) throw new HttpError(400, 'Lipsește versiunea proiectului.');
  let base: DesignBrief | null = null; if (hasBase){ try { base = checkBrief({ ...DEFAULT_BRIEF, ...(b.base as object) }); } catch { throw new HttpError(400, 'Brief-ul de referință este invalid.'); } }
  await getProject(owner, id); const { tx } = await getDb();
  return tx(async q => {
    const { rows } = await q('select draft, draft_rev, currency from projects where id=$1 and owner=$2 and deleted_at is null for update', [id, owner]);
    if (!rows[0]) throw new HttpError(404, 'Proiect inexistent.');
    const r = await latest(q, id); if (!r) throw new HttpError(404, 'Proiectul nu are chestionar.');
    const draft = rows[0].draft as Snapshot; let current: DesignBrief; try { current = checkBrief({ ...DEFAULT_BRIEF, ...(draft.brief ?? {}) }); } catch { current = { ...DEFAULT_BRIEF }; }
    const conflict = base ? JSON.stringify(base) !== JSON.stringify(current) : rows[0].draft_rev !== b.rev;
    if (conflict) throw new HttpError(409, base ? 'Brief-ul a fost schimbat între timp (alt dispozitiv sau o revizie restaurată). Deschide din nou previzualizarea.' : 'Proiectul a fost modificat în altă parte (alt dispozitiv sau o revizie restaurată).', { conflict: true });
    // răspunsurile stocate au trecut validarea la salvare; se validează din nou (ca ciornă), ca nimic nevalidat să nu ajungă în brief
    const { fields } = briefFromClient(v(() => checkClientBrief(r.data ?? {}, { partial: true })), current, String(rows[0].currency ?? 'RON'));
    const picked = fields.filter(f => (b.fields as string[]).includes(f.key));
    let next: DesignBrief; try { next = checkBrief({ ...current, ...Object.fromEntries(picked.map(f => [f.key, f.proposed])) }); } catch (e: any){ throw new HttpError(400, e.message); }
    draft.brief = next;
    const up = await q('update projects set draft=$1, draft_rev=draft_rev+1, updated_at=now() where id=$2 returning draft_rev', [JSON.stringify(draft), id]);
    await q('update client_briefs set imported_at=now() where token=$1', [r.token]);
    return { brief: next, draftRev: up.rows[0].draft_rev as number, applied: picked.map(f => f.key) };
  });
}

// ---------- public (doar cu tokenul) ----------
/** Versiune validă: întreg nenegativ care încape în coloana int (altfel 400, nu o eroare SQL). */
const isRev = (x: unknown): x is number => Number.isInteger(x) && (x as number) >= 0 && (x as number) <= 2147483647;
/** Chestionarul unui link activ (altfel 404, fără detalii); `lock` îl blochează până la sfârșitul tranzacției. */
async function open(q: Query, token: unknown, o: { lock?: boolean; allowSubmitted?: boolean } = {}){
  if (!isShareToken(token)) throw NOT_FOUND();
  // un proiect din coș are linkul revocat (lib/trash.ts); verificarea pe proiect rămâne ca plasă de siguranță
  const { rows } = await q(`select b.token, b.data, b.rev, b.submitted_at, b.project_id from client_briefs b where b.token=$1 and ${shareActive('b')}
    and exists (select 1 from projects p where p.id = b.project_id and p.deleted_at is null)${o.lock ? ' for update of b' : ''}`, [token]);
  if (!rows[0]) throw NOT_FOUND(); if (rows[0].submitted_at && !o.allowSubmitted) throw SUBMITTED();
  return rows[0];
}
/** Ce vede clientul pe link: ciorna lui și pozele lui. După trimitere, doar faptul că a fost trimis. */
export async function getPublicBrief(token: unknown){
  const { q } = await getDb(), r = await open(q, token, { allowSubmitted: true });
  if (r.submitted_at) return { submitted: true as const };
  const photos = (await q('select id, verdict, note from brief_photos where token=$1 order by created_at, id', [r.token])).rows.map(p => photoRow(p));
  return { submitted: false as const, data: (r.data ?? {}) as Partial<ClientBrief>, rev: r.rev as number, currency: await currencyOf(q, r.project_id), photos };
}
/** Există linkul și e activ? (pentru pagina publică) */
export async function publicBriefExists(token: unknown){ const { q } = await getDb(); await open(q, token, { allowSubmitted: true }); return true; }
/** Salvarea automată: documentul întreg + versiunea de la care a pornit formularul. Două file deschise nu se suprascriu: a doua primește 409. */
export async function savePublicBrief(token: unknown, body: unknown){
  if (!isShareToken(token)) throw NOT_FOUND();
  if (!isObj(body) || !isRev(body.rev) || Object.keys(body).some(k => k !== 'rev' && k !== 'data')) throw new HttpError(400, 'Cerere invalidă.');
  const data = v(() => checkClientBrief(body.data, { partial: true })), { q } = await getDb();
  const { rows } = await q(`update client_briefs set data=$1, rev=rev+1, updated_at=now() where token=$2 and ${shareActive()} and submitted_at is null and rev=$3
    and exists (select 1 from projects pr where pr.id = client_briefs.project_id and pr.deleted_at is null) returning rev`, [JSON.stringify(data), token, body.rev]);
  if (!rows[0]){ await open(q, token); throw new HttpError(409, 'Chestionarul a fost schimbat în altă filă sau pe alt dispozitiv. Reîncarcă pagina ca să vezi ultima variantă.', { conflict: true }); }
  return { rev: rows[0].rev as number };
}
/** Trimiterea: răspunsurile salvate trebuie să fie complete. După ea linkul nu mai arată și nu mai primește nimic. */
export async function submitPublicBrief(token: unknown, body: unknown){
  if (!isShareToken(token)) throw NOT_FOUND();
  if (!isObj(body) || !isRev(body.rev) || Object.keys(body).some(k => k !== 'rev')) throw new HttpError(400, 'Cerere invalidă.');
  const { tx } = await getDb();
  return tx(async q => { const r = await open(q, token, { lock: true });
    if (r.rev !== body.rev) throw new HttpError(409, 'Chestionarul a fost schimbat în altă filă sau pe alt dispozitiv. Reîncarcă pagina ca să vezi ultima variantă.', { conflict: true });
    const data = v(() => checkClientBrief(r.data ?? {}));
    await q('update client_briefs set data=$1, submitted_at=now(), updated_at=now() where token=$2', [JSON.stringify(data), token]);
    return { submitted: true as const }; });
}
/** Poză de inspirație: doar JPEG, reconstruit fără metadate (core/client-brief.ts stripJpeg). Numărul și mărimea totală
 *  se verifică în aceeași tranzacție cu rândul chestionarului blocat, ca încărcările simultane să nu treacă de limită. */
export async function addPublicPhoto(token: unknown, bytes: Uint8Array): Promise<BriefPhoto> {
  if (!isShareToken(token)) throw NOT_FOUND();
  if (bytes.length > BRIEF_LIMITS.photoBytes) throw new HttpError(413, 'Poza e prea mare.');
  const { q, tx } = await getDb(); await open(q, token);   // linkul se verifică înainte de a prelucra imaginea
  const img = v(() => stripJpeg(bytes));
  return tx(async q => { await open(q, token, { lock: true });
    const { rows: [c] } = await q('select count(*)::int as n, coalesce(sum(bytes), 0)::int as total from brief_photos where token=$1', [token]);
    if (c.n >= BRIEF_LIMITS.photos) throw new HttpError(409, `Poți adăuga cel mult ${BRIEF_LIMITS.photos} poze.`, { limit: true });
    if (c.total + img.data.length > BRIEF_LIMITS.totalBytes) throw new HttpError(409, 'Pozele adăugate ocupă deja tot spațiul disponibil.', { limit: true });
    const { rows } = await q(`insert into brief_photos(token, mime, data, bytes, width, height) values($1,'image/jpeg',$2,$3,$4,$5) returning id, verdict, note`, [token, Buffer.from(img.data), img.data.length, img.width, img.height]);
    await q('update client_briefs set updated_at=now() where token=$1', [token]);
    return photoRow(rows[0]); });
}
const OPEN_JOIN = `b.token = p.token and ${shareActive('b')} and b.submitted_at is null and exists (select 1 from projects pr where pr.id = b.project_id and pr.deleted_at is null)`;
/** Părerea clientului despre o poză: verdict („îmi place mult” / „e ok” / „nu-mi place”, sau fără) și o notă scurtă. */
export async function patchPublicPhoto(token: unknown, photoId: unknown, body: unknown): Promise<BriefPhoto> {
  if (!isShareToken(token)) throw NOT_FOUND();
  if (!isObj(body) || Object.keys(body).some(k => k !== 'verdict' && k !== 'note') || !Object.keys(body).length) throw new HttpError(400, 'Cerere invalidă.');
  const sets: string[] = [], vals: unknown[] = [photoId, token];
  if (body.verdict !== undefined){ if (body.verdict !== null && !(PHOTO_VERDICTS as readonly unknown[]).includes(body.verdict)) throw new HttpError(400, 'Părere necunoscută.'); vals.push(body.verdict); sets.push(`verdict=$${vals.length}`); }
  if (body.note !== undefined){ if (typeof body.note !== 'string') throw new HttpError(400, 'Nota este invalidă.'); const n = cleanText(body.note, BRIEF_LIMITS.note);
    if (n.length > BRIEF_LIMITS.note) throw new HttpError(400, `Nota e prea lungă (cel mult ${BRIEF_LIMITS.note} de caractere).`); vals.push(n); sets.push(`note=$${vals.length}`); }
  const { q } = await getDb(); if (!isUuid(photoId)){ await open(q, token); throw new HttpError(404, 'Poză inexistentă.'); }
  const { rows } = await q(`update brief_photos p set ${sets.join(', ')} from client_briefs b where p.id=$1 and p.token=$2 and ${OPEN_JOIN} returning p.id, p.verdict, p.note`, vals);
  if (!rows[0]){ await open(q, token); throw new HttpError(404, 'Poză inexistentă.'); }
  return photoRow(rows[0]);
}
export async function deletePublicPhoto(token: unknown, photoId: unknown){
  const { q } = await getDb(); if (!isShareToken(token)) throw NOT_FOUND(); if (!isUuid(photoId)){ await open(q, token); throw new HttpError(404, 'Poză inexistentă.'); }
  const { rows } = await q(`delete from brief_photos p using client_briefs b where p.id=$1 and p.token=$2 and ${OPEN_JOIN} returning p.id`, [photoId, token]);
  if (!rows[0]){ await open(q, token); throw new HttpError(404, 'Poză inexistentă.'); }
  return { ok: true };
}
/** Clientul își vede propria poză doar cât linkul e activ și chestionarul netrimis. */
export async function readPublicPhoto(token: unknown, photoId: unknown){
  const { q } = await getDb(); await open(q, token, { allowSubmitted: true }); if (!isUuid(photoId)) throw new HttpError(404, 'Poză inexistentă.');
  const { rows } = await q(`select p.mime, p.data from brief_photos p join client_briefs b on ${OPEN_JOIN} where p.id=$1 and p.token=$2`, [photoId, token]);
  if (!rows[0]) throw new HttpError(404, 'Poză inexistentă.'); return { mime: rows[0].mime as string, data: new Uint8Array(rows[0].data) };
}
