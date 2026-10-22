// Părerile clientului pe linkul de prezentare: pe o cameră sau pe tot proiectul („îmi place”, „aș schimba”, „am o întrebare”).
// Scrierea e publică (doar cu tokenul unui link activ), deci totul e validat strict și limitat pe link; citirea completă
// și bifarea „rezolvat” sunt doar ale proprietarului proiectului.
import { getDb } from './db';
import { HttpError, getProject, getCatalog, getMaterials } from './repo';
import { resolveShare, type SharedView } from './share';
import { presentation, approvalItems } from '../core/presentation';

export const FEEDBACK_VERDICTS = ['like', 'change', 'question'] as const;
export type FeedbackVerdict = typeof FEEDBACK_VERDICTS[number];
/**
 * `perShare` = plafonul pe link pentru păreri și întrebări; `approvalsPerShare` = plafon separat pentru aprobările pe produs
 * („îmi place” pe un finisaj sau o piesă), ca un șir de aprobări să nu poată bloca întrebările clientului.
 */
export const FEEDBACK_LIMITS = { author: 60, text: 2000, perShare: 200, approvalsPerShare: 200 };
// aprobare pe produs = „îmi place” cu produs; restul rândurilor (inclusiv „aș schimba” pe un produs) sunt păreri
const IS_APPROVAL = `verdict = 'like' and item is not null`;
const tooMany = () => new HttpError(429, 'S-au trimis prea multe păreri pe acest link.');
/** Cheile produselor din cameră a căror ultimă părere pe acest link e o aprobare (nu se mai aprobă încă o dată). */
async function approvedKeys(q: (sql: string, p?: unknown[]) => Promise<{ rows: any[] }>, token: string, roomId: string): Promise<Map<string, any>> {
  const { rows } = await q(`select distinct on (item->>'key') * from share_feedback where token=$1 and room_id=$2 and item is not null order by item->>'key', created_at desc, id desc`, [token, roomId]);
  return new Map(rows.filter(r => r.verdict === 'like').map(r => [r.item.key as string, r] as const));
}
/** item = un finisaj sau o piesă anume din cameră (aprobare pe produs); lipsă = părere despre cameră sau proiect */
export interface FeedbackItem { key: string; label: string; role: string | null }
/** Pin pe imaginea din prezentare: suprafața (camera sau vederea de ansamblu) și poziția relativă u,v în [0,1]. */
export const PIN_SURFACES = ['room', 'overview'] as const;
export interface PinAnchor { surface: typeof PIN_SURFACES[number]; u: number; v: number }
export interface Feedback { id: string; roomId: string | null; verdict: FeedbackVerdict; author: string; text: string; createdAt: string; resolvedAt: string | null; item: FeedbackItem | null; anchor: PinAnchor | null }

const ID_RE = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
// fără caractere de control (în afară de rând nou) și fără caractere invizibile / de inversare a direcției textului
// (un nume nu poate să pară altul în lista designerului); spații de la capete tăiate
const clean = (v: unknown) => typeof v === 'string' ? v.replace(/[\u0000-\u0009\u000b-\u001f\u007f-\u009f\u200b-\u200f\u202a-\u202e\u2060-\u2069\ufeff]/g, '').trim() : null;
const row = (r: any): Feedback => ({ id: r.id, roomId: r.room_id, verdict: r.verdict, author: r.author, text: r.body, createdAt: new Date(r.created_at).toISOString(), resolvedAt: r.resolved_at ? new Date(r.resolved_at).toISOString() : null, item: r.item ?? null, anchor: r.anchor ?? null });
const roomIdsOf = (view: SharedView) => view.snapshot.floor.rooms.concat(...(view.snapshot.levels ?? []).map(l => l.rooms)).map(r => r.id);

/**
 * Ancora unui pin, construită pe server (cheile în plus se ignoră): suprafață din listă, u,v numere finite în [0,1]
 * rotunjite la 4 zecimale; pe o cameră ⇒ camera există în revizie, pe ansamblu ⇒ fără cameră.
 */
export function parseAnchor(a: unknown, roomId: unknown, roomIds: readonly string[]): PinAnchor {
  const bad = () => new HttpError(400, 'Poziția pinului nu e validă.');
  if (!a || typeof a !== 'object' || Array.isArray(a)) throw bad();
  const x = a as Record<string, unknown>, surface = x.surface;
  const coord = (n: unknown) => typeof n === 'number' && Number.isFinite(n) && n >= 0 && n <= 1 ? Math.round(n * 1e4) / 1e4 : null;
  if (!(PIN_SURFACES as readonly unknown[]).includes(surface)) throw bad();
  const u = coord(x.u), v = coord(x.v); if (u === null || v === null) throw bad();
  if (surface === 'room' ? !(typeof roomId === 'string' && roomIds.includes(roomId)) : roomId !== null) throw bad();
  return { surface: surface as PinAnchor['surface'], u, v };
}
type Valid = { verdict: FeedbackVerdict; author: string; text: string; roomId: string | null; item: FeedbackItem | null };
/** Validarea comună a unei păreri (părere simplă sau pin): aceleași reguli, aceleași mesaje. */
async function validateFeedback(view: SharedView, input: unknown, allowItem: boolean): Promise<Valid> {
  const x = input as any; if (!x || typeof x !== 'object') throw new HttpError(400, 'Părerea nu e validă.');
  const verdict = x.verdict, author = clean(x.author), text = clean(x.text ?? ''), roomId = x.roomId ?? null;
  if (!(FEEDBACK_VERDICTS as readonly string[]).includes(verdict)) throw new HttpError(400, 'Alege: îmi place, aș schimba sau am o întrebare.');
  if (!author || author.length > FEEDBACK_LIMITS.author) throw new HttpError(400, 'Scrie-ți numele (cel mult 60 de caractere).');
  if (text == null || text.length > FEEDBACK_LIMITS.text) throw new HttpError(400, 'Textul e prea lung.');
  if (verdict !== 'like' && !text) throw new HttpError(400, 'Scrie ce ai schimba sau ce vrei să întrebi.');
  if (roomId !== null && !(typeof roomId === 'string' && roomIdsOf(view).includes(roomId))) throw new HttpError(400, 'Camera nu există în prezentare.');
  // aprobare pe produs: produsul trebuie să fie chiar în camera din revizia partajată; numele îl pune serverul, nu clientul
  let item: FeedbackItem | null = null;
  if (allowItem && x.item != null){ const k = x.item?.key; if (roomId === null || typeof k !== 'string' || k.length > 120) throw new HttpError(400, 'Produsul nu există în prezentare.');
    const [cat, mc] = await Promise.all([getCatalog(), getMaterials()]), slide = presentation(view.snapshot, cat, mc).rooms.find(r => r.id === roomId);
    item = (slide ? approvalItems(slide) : []).find(i => i.key === k) ?? null; if (!item) throw new HttpError(400, 'Produsul nu există în prezentare.'); }
  return { verdict, author, text, roomId, item };
}
/** Scrierea comună: linkul se blochează pe durata tranzacției (numărătoarea și inserarea nu se pot intercala între cereri simultane). */
async function saveFeedback(t: string, f: Valid, anchor: PinAnchor | null): Promise<Feedback> {
  const { tx } = await getDb(), { verdict, author, text, roomId, item } = f;
  return tx(async q => {
    const { rows: [s] } = await q('select project_id from shares where token=$1 and revoked_at is null for update', [t]);
    if (!s) throw new HttpError(404, 'Link inexistent.');
    const approval = verdict === 'like' && item !== null;
    // a doua aprobare a aceluiași produs (ultima părere despre el e deja „îmi place”) nu mai scrie un rând nou
    if (approval){ const prev = (await approvedKeys(q, t, roomId!)).get(item!.key); if (prev) return row(prev); }
    const { rows: [c] } = await q(`select count(*)::int as n from share_feedback where token=$1 and ${approval ? '' : 'not '}(${IS_APPROVAL})`, [t]);
    if (c.n >= (approval ? FEEDBACK_LIMITS.approvalsPerShare : FEEDBACK_LIMITS.perShare)) throw tooMany();
    const { rows } = await q('insert into share_feedback(token, project_id, room_id, verdict, author, body, item, anchor) values($1,$2,$3,$4,$5,$6,$7,$8) returning *',
      [t, s.project_id, roomId, verdict, author, text, item ? JSON.stringify(item) : null, anchor ? JSON.stringify(anchor) : null]);
    return row(rows[0]); });
}
export async function addFeedback(token: unknown, input: unknown): Promise<Feedback> {
  const view = await resolveShare(token);                                 // link necunoscut sau revocat → 404
  // un pin pe imagine se pune doar prin /pins (după PIN): câmpul `anchor` aici nu ocolește PIN-ul
  if (input && typeof input === 'object' && 'anchor' in input) throw new HttpError(400, 'Pinii pe imagine se trimit cu PIN.');
  return saveFeedback(token as string, await validateFeedback(view, input, true), null);
}
/**
 * Pin pe imagine (ruta /pins, după PIN): aceeași validare ca o părere simplă, fără produs, plus ancora; intră în plafonul
 * de păreri (perShare). Sesiunea PIN o verifică ruta înainte (lib/share-pin.ts).
 */
export async function addPinnedFeedback(token: unknown, input: unknown): Promise<Feedback> {
  const view = await resolveShare(token);
  const f = await validateFeedback(view, input, false);
  return saveFeedback(token as string, f, parseAnchor((input as any).anchor, f.roomId, roomIdsOf(view)));
}
/**
 * „Aprobă camera”: mai multe aprobări pe produs dintr-o singură cerere (aceeași cameră, același nume). Fiecare cheie trebuie
 * să fie un finisaj sau o piesă din camera reviziei partajate; numele produselor le pune serverul. Produsele deja aprobate pe
 * acest link (ultima lor părere e „îmi place”) se sar, deci aceeași cerere repetată nu adaugă rânduri; un produs se poate
 * aproba din nou doar după un „aș schimba”, care intră în plafonul de păreri. Rândurile noi intră în plafonul separat de
 * aprobări (approvalsPerShare): aprobările nu pot umple plafonul întrebărilor (perShare). Întoarce doar rândurile noi.
 */
export const APPROVE_MAX = 60;
export async function addApprovals(token: unknown, input: unknown): Promise<Feedback[]> {
  const view = await resolveShare(token);
  const x = input as any; if (!x || typeof x !== 'object' || !Array.isArray(x.items)) throw new HttpError(400, 'Aprobarea nu e validă.');
  const author = clean(x.author), roomId = x.roomId;
  if (!author || author.length > FEEDBACK_LIMITS.author) throw new HttpError(400, 'Scrie-ți numele (cel mult 60 de caractere).');
  if (typeof roomId !== 'string' || !roomIdsOf(view).includes(roomId)) throw new HttpError(400, 'Camera nu există în prezentare.');
  const keys = [...new Set(x.items.map((i: any) => i?.key))];
  if (!keys.length || keys.length > APPROVE_MAX || keys.some(k => typeof k !== 'string' || k.length > 120)) throw new HttpError(400, 'Produsul nu există în prezentare.');
  const [cat, mc] = await Promise.all([getCatalog(), getMaterials()]), slide = presentation(view.snapshot, cat, mc).rooms.find(r => r.id === roomId);
  const known = new Map((slide ? approvalItems(slide) : []).map(i => [i.key, i] as const)), items = keys.map(k => known.get(k as string));
  if (items.some(i => !i)) throw new HttpError(400, 'Produsul nu există în prezentare.');
  const { tx } = await getDb(), t = token as string;
  return tx(async q => {
    const { rows: [s] } = await q('select project_id from shares where token=$1 and revoked_at is null for update', [t]);
    if (!s) throw new HttpError(404, 'Link inexistent.');
    const done = await approvedKeys(q, t, roomId), fresh = items.filter(it => !done.has(it!.key));
    if (!fresh.length) return [];
    const { rows: [c] } = await q(`select count(*)::int as n from share_feedback where token=$1 and ${IS_APPROVAL}`, [t]);
    if (c.n + fresh.length > FEEDBACK_LIMITS.approvalsPerShare) throw tooMany();
    const out: Feedback[] = [];
    for (const it of fresh){ const { rows } = await q('insert into share_feedback(token, project_id, room_id, verdict, author, body, item) values($1,$2,$3,$4,$5,$6,$7) returning *', [t, s.project_id, roomId, 'like', author, '', JSON.stringify(it)]); out.push(row(rows[0])); }
    return out; });
}
/** Ce vede clientul pe link: părerile trimise pe acest link, cu starea lor. */
export async function listFeedbackForShare(token: unknown): Promise<Feedback[]> {
  await resolveShare(token); const { q } = await getDb();
  const { rows } = await q('select * from share_feedback where token=$1 order by created_at', [token]);
  return rows.map(row);
}
/** Ce vede designerul: toate părerile proiectului, de pe toate linkurile (și cele revocate), cu revizia la care se referă. */
export async function listFeedback(owner: string, projectId: string): Promise<(Feedback & { revisionNumber: number; token: string })[]> {
  await getProject(owner, projectId); const { q } = await getDb();
  const { rows } = await q(`select f.*, s.revision_number from share_feedback f join shares s on s.token = f.token where f.project_id=$1 order by f.created_at desc`, [projectId]);
  return rows.map(r => ({ ...row(r), revisionNumber: r.revision_number, token: r.token as string }));
}
export async function setFeedbackResolved(owner: string, projectId: string, id: unknown, resolved: unknown){
  await getProject(owner, projectId); if (typeof id !== 'string' || !ID_RE.test(id)) throw new HttpError(404, 'Părere inexistentă.');
  const { q } = await getDb();
  const { rows } = await q(`update share_feedback set resolved_at = case when $3 then coalesce(resolved_at, now()) else null end where id=$1 and project_id=$2 returning resolved_at`, [id, projectId, resolved === true]);
  if (!rows[0]) throw new HttpError(404, 'Părere inexistentă.');
  return { resolvedAt: rows[0].resolved_at ? new Date(rows[0].resolved_at).toISOString() : null };
}
