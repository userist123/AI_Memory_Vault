// Cererile de ofertă către meseriași (core/rfq.ts), pe server. Designerul creează o cerere (lista de cantități se îngheață
// atunci) și câte un link public pentru fiecare meseriaș; meseriașul completează fără cont, cu salvare automată, și trimite.
// Scrierea publică cere tokenul unui link activ și netrimis, e validată strict și limitată. Linkul arată doar cererea și
// oferta PROPRIE (după trimitere, read-only): niciodată clientul, adresa, prețurile către client, bugetul, reperul de piață,
// celelalte oferte sau numele celorlalți meseriași. Designerul vede o ofertă doar după ce a fost trimisă.
// „Atribuie” scrie prețurile contractate pe proiect (Snapshot.laborContract), cu versiunea proiectului (409 la conflict).
import { randomBytes, createHash } from 'node:crypto';
import { getDb, type Query } from './db';
import { HttpError, getProject } from './repo';
import { TOKEN_BYTES, isShareToken, shareActive, shareDays, publicBase } from './share';
import { projectBudget } from './contract';
import { floors } from '../core/levels';
import { isUuid } from '../core/site';
import { VAT_RATE } from '../core/rules.boq';
import { TRADES, isTrade, packageLines, checkLines, publicLines, checkOffer, offerCoverage, contractFromOffer, sanitizeLaborContract, cleanLine, cleanNote, validDate, RfqError, RFQ_LIMITS,
  type Trade, type RfqLine, type RfqOffer, type LaborContract } from '../core/rfq';
import type { Snapshot } from '../core/types';

export const RFQ_DEFAULT_DAYS = 30;
export const RFQ_MAX = { requests: 20, invites: 12, bodyBytes: 16 * 1024 };

const NOT_FOUND = () => new HttpError(404, 'Link inexistent.');
const NO_REQUEST = () => new HttpError(404, 'Cerere de ofertă inexistentă.');
const NO_INVITE = () => new HttpError(404, 'Invitație inexistentă.');
const SUBMITTED = () => new HttpError(409, 'Oferta a fost deja trimisă.', { submitted: true });
const CLOSED = () => new HttpError(409, 'Cererea de ofertă a fost închisă; oferta nu mai poate fi schimbată.', { closed: true });
const CONFLICT = () => new HttpError(409, 'Oferta a fost schimbată în altă filă sau pe alt dispozitiv. Reîncarcă pagina ca să vezi ultima variantă.', { conflict: true });
const PROJECT_CONFLICT = () => new HttpError(409, 'Proiectul a fost modificat în altă parte (alt dispozitiv sau o revizie restaurată). Reîncarcă și încearcă din nou.', { conflict: true });
const iso = (v: unknown) => v ? new Date(v as string).toISOString() : null;
// coloana `date` se citește ca text (due_date::text): un obiect Date ar putea aluneca cu o zi după fusul orar
const day = (v: unknown) => typeof v === 'string' && v ? v.slice(0, 10) : null;
const isObj = (x: unknown): x is Record<string, unknown> => !!x && typeof x === 'object' && !Array.isArray(x);
/** Versiune validă: întreg nenegativ care încape în coloana int (altfel 400, nu o eroare SQL). */
const isRev = (x: unknown): x is number => Number.isInteger(x) && (x as number) >= 0 && (x as number) <= 2147483647;
const v = <T>(fn: () => T, status = 400): T => { try { return fn(); } catch (e: any){ if (e instanceof RfqError) throw new HttpError(status, e.message, e.missing ? { missing: e.missing } : undefined); throw e; } };
/** Numele public al unei invitații în API-ul designerului: nu e cheia linkului (un link oprit nu-și mai arată cheia). */
export const inviteKey = (token: string) => createHash('sha256').update(token).digest('hex').slice(0, 16);
const KEY_RE = /^[0-9a-f]{16}$/;

// ---------- designer ----------
type Invite = { key: string; label: string; active: boolean; token?: string; path?: string; url?: string | null; publicUrl: boolean; createdAt: string; updatedAt: string; submittedAt: string | null;
  expiresAt: string | null; revokedAt: string | null; started: boolean; offer: RfqOffer | null };
function inviteState(r: any, lines: RfqLine[]): Invite {
  const base = publicBase(), active = !!r.active, path = `/oferta/${r.token}`, started = Object.keys(r.data ?? {}).some(k => k !== 'v' && JSON.stringify(r.data[k]) !== '{}');
  // oferta ajunge la designer doar după trimitere; dacă rândul stocat nu mai trece validarea, apare ca netrimisă, nu ca eroare
  let offer: RfqOffer | null = null; if (r.submitted_at){ try { offer = checkOffer(r.data ?? {}, lines); } catch { offer = null; } }
  // cheia linkului se întoarce doar cât linkul e activ (ca să poată fi copiat)
  return { key: inviteKey(r.token), label: r.label, active, ...(active ? { token: r.token as string, path, url: base ? base + path : null } : {}), publicUrl: !!base, createdAt: iso(r.created_at)!, updatedAt: iso(r.updated_at)!,
    submittedAt: offer ? iso(r.submitted_at) : null, expiresAt: iso(r.expires_at), revokedAt: iso(r.revoked_at), started, offer };
}
async function requestsOf(q: Query, id: string){
  const reqs = (await q('select r.*, r.due_date::text as due from rfq_requests r where r.project_id=$1 order by r.created_at desc, r.id', [id])).rows; if (!reqs.length) return [];
  const inv = (await q(`select i.*, (${shareActive('i')}) as active from rfq_invites i where i.request_id = any($1::uuid[]) order by i.created_at, i.token`, [reqs.map(r => r.id)])).rows;
  return reqs.map(r => { let lines: RfqLine[] = []; try { lines = checkLines(r.lines); } catch { /* rând stocat invalid: cererea apare fără rânduri */ }
    return { id: r.id as string, title: r.title as string, trades: (Array.isArray(r.trades) ? r.trades.filter(isTrade) : []) as Trade[], lines, note: r.note as string, siteCity: (r.site_city ?? null) as string | null, dueDate: day(r.due),
      createdAt: iso(r.created_at)!, closedAt: iso(r.closed_at), awarded: r.awarded_invite ? inviteKey(r.awarded_invite) : null, invites: inv.filter(i => i.request_id === r.id).map(i => inviteState(i, lines)) }; });
}
/** Rândurile de manoperă ale proiectului de acum, pe meserii (pentru „Cerere nouă”): ce ar îngheța o cerere creată în acest moment. */
async function currentLines(p: Awaited<ReturnType<typeof getProject>>, trades: readonly Trade[] = TRADES){
  const { budget } = await projectBudget(p), names = Object.fromEntries(floors(p.draft).flatMap(f => f.rooms).map(r => [r.id, r.name] as const));
  return packageLines(budget.labor, trades, names);
}
/** Starea pentru designer: cererile cu invitațiile lor, rândurile disponibile acum, prețurile contractate și versiunea proiectului. */
export async function getRfq(owner: string, id: string){
  const p = await getProject(owner, id), { q } = await getDb();
  return { requests: await requestsOf(q, id), available: await currentLines(p), laborContract: sanitizeLaborContract(p.draft.laborContract) ?? {}, projectRev: p.draftRev, vatRate: VAT_RATE };
}
const stateOf = async (owner: string, id: string) => getRfq(owner, id);
/** Cerere nouă: titlu, meseriile alese, localitatea (opțional), termenul dorit, nota. Rândurile se îngheață acum, din devizul curent. */
export async function createRfq(owner: string, id: string, input: unknown){
  const b = input as any; if (!isObj(b) || Object.keys(b).some(k => !['title', 'trades', 'note', 'siteCity', 'dueDate'].includes(k))) throw new HttpError(400, 'Cerere invalidă.');
  if (typeof b.title !== 'string' || b.title.length > RFQ_LIMITS.title || !cleanLine(b.title, RFQ_LIMITS.title)) throw new HttpError(400, `Cererea are nevoie de un titlu (cel mult ${RFQ_LIMITS.title} de caractere).`);
  if (!Array.isArray(b.trades) || !b.trades.length || !b.trades.every(isTrade) || new Set(b.trades).size !== b.trades.length) throw new HttpError(400, 'Alege cel puțin o meserie.');
  if (b.note != null && (typeof b.note !== 'string' || b.note.length > RFQ_LIMITS.note)) throw new HttpError(400, `Nota e prea lungă (cel mult ${RFQ_LIMITS.note} de caractere).`);
  if (b.siteCity != null && (typeof b.siteCity !== 'string' || b.siteCity.length > RFQ_LIMITS.city)) throw new HttpError(400, 'Localitatea e prea lungă.');
  if (b.dueDate != null && b.dueDate !== '' && !validDate(b.dueDate)) throw new HttpError(400, 'Termenul dorit nu e o dată validă.');
  const p = await getProject(owner, id), lines = await currentLines(p, b.trades as Trade[]);
  if (!lines.length) throw new HttpError(400, 'Devizul nu are lucrări de manoperă pentru meseriile alese.');
  const frozen = v(() => checkLines(lines)), trades = TRADES.filter(t => frozen.some(l => l.trade === t)), { tx } = await getDb();
  const rid = await tx(async q => {
    if (!(await q('select id from projects where id=$1 and owner=$2 and deleted_at is null for update', [id, owner])).rows[0]) throw new HttpError(404, 'Proiect inexistent.');
    const { rows: [c] } = await q('select count(*)::int as n from rfq_requests where project_id=$1', [id]);
    if (c.n >= RFQ_MAX.requests) throw new HttpError(409, `Cel mult ${RFQ_MAX.requests} cereri de ofertă pe proiect. Șterge una veche.`);
    const { rows } = await q('insert into rfq_requests(project_id, title, trades, lines, note, site_city, due_date) values($1,$2,$3,$4,$5,$6,$7) returning id',
      [id, cleanLine(b.title, RFQ_LIMITS.title), JSON.stringify(trades), JSON.stringify(frozen), cleanNote(b.note, RFQ_LIMITS.note), cleanLine(b.siteCity, RFQ_LIMITS.city) || null, b.dueDate || null]);
    return rows[0].id as string; });
  return { id: rid, ...await stateOf(owner, id) };
}
async function requestRow(q: Query, id: string, rid: unknown, lock = false){
  if (!isUuid(rid)) throw NO_REQUEST();
  const { rows } = await q(`select * from rfq_requests where id=$1 and project_id=$2${lock ? ' for update' : ''}`, [rid, id]); if (!rows[0]) throw NO_REQUEST(); return rows[0];
}
async function inviteRow(q: Query, rid: string, key: unknown){
  if (typeof key !== 'string' || !KEY_RE.test(key)) throw NO_INVITE();
  const r = (await q(`select i.*, (${shareActive('i')}) as active from rfq_invites i where i.request_id=$1`, [rid])).rows.find(x => inviteKey(x.token) === key); if (!r) throw NO_INVITE(); return r;
}
/** Invitație nouă: eticheta designerului („Echipa X”) și un link propriu, valabil implicit 30 de zile. */
export async function addInvite(owner: string, id: string, rid: unknown, input: unknown){
  const b = input as any; if (!isObj(b) || Object.keys(b).some(k => !['label', 'expiresInDays'].includes(k))) throw new HttpError(400, 'Cerere invalidă.');
  if (typeof b.label !== 'string' || b.label.length > RFQ_LIMITS.label || !cleanLine(b.label, RFQ_LIMITS.label)) throw new HttpError(400, `Scrie numele meseriașului sau al echipei (cel mult ${RFQ_LIMITS.label} de caractere).`);
  const days = b.expiresInDays == null ? RFQ_DEFAULT_DAYS : shareDays(b.expiresInDays), label = cleanLine(b.label, RFQ_LIMITS.label);
  await getProject(owner, id); const { tx } = await getDb(), token = randomBytes(TOKEN_BYTES).toString('base64url');
  await tx(async q => {
    // un proiect mutat între timp în coș nu primește link nou; numărul invitațiilor se verifică sub blocarea cererii
    if (!(await q('select id from projects where id=$1 and deleted_at is null for update', [id])).rows[0]) throw new HttpError(404, 'Proiect inexistent.');
    const r = await requestRow(q, id, rid, true); if (r.closed_at) throw new HttpError(409, 'Cererea a fost atribuită; anulează atribuirea ca să mai inviți pe cineva.');
    const { rows: [c] } = await q('select count(*)::int as n from rfq_invites where request_id=$1', [r.id]);
    if (c.n >= RFQ_MAX.invites) throw new HttpError(409, `Cel mult ${RFQ_MAX.invites} invitații pe o cerere.`);
    await q(`insert into rfq_invites(token, request_id, label, expires_at) values($1,$2,$3, case when $4::int = 0 then null else now() + make_interval(days => $4::int) end)`, [token, r.id, label, days]); });
  return { key: inviteKey(token), ...await stateOf(owner, id) };
}
/** Revocarea oprește linkul; oferta trimisă rămâne la designer. */
export async function revokeInvite(owner: string, id: string, rid: unknown, key: unknown){
  await getProject(owner, id); const { q } = await getDb(), r = await requestRow(q, id, rid), i = await inviteRow(q, r.id, key);
  await q('update rfq_invites set revoked_at = coalesce(revoked_at, now()) where token=$1', [i.token]); return stateOf(owner, id);
}
/** Redeschide o ofertă trimisă: meseriașul o poate schimba pe același link (dacă linkul mai e activ), apoi o trimite din nou. */
export async function reopenInvite(owner: string, id: string, rid: unknown, key: unknown){
  await getProject(owner, id); const { tx } = await getDb();
  // rândul cererii blocat: o atribuire simultană nu poate lăsa oferta aleasă redeschisă (și invizibilă designerului)
  await tx(async q => { const r = await requestRow(q, id, rid, true), i = await inviteRow(q, r.id, key);
    if (r.closed_at) throw new HttpError(409, 'Cererea a fost atribuită; anulează întâi atribuirea.');
    await q('update rfq_invites set submitted_at=null, updated_at=now() where token=$1', [i.token]); });
  return stateOf(owner, id);
}
/** Șterge invitația cu tot cu oferta ei. Oferta aleasă nu se poate șterge cât cererea e atribuită. */
export async function deleteInvite(owner: string, id: string, rid: unknown, key: unknown){
  await getProject(owner, id); const { q } = await getDb(), r = await requestRow(q, id, rid), i = await inviteRow(q, r.id, key);
  if (r.awarded_invite === i.token) throw new HttpError(409, 'Aceasta e oferta aleasă; anulează întâi atribuirea.');
  await q('delete from rfq_invites where token=$1', [i.token]); return stateOf(owner, id);
}
/** Șterge cererea cu toate invitațiile și ofertele ei. O cerere atribuită se șterge doar după anularea atribuirii. */
export async function deleteRfq(owner: string, id: string, rid: unknown){
  await getProject(owner, id); const { q } = await getDb(), r = await requestRow(q, id, rid);
  if (r.awarded_invite) throw new HttpError(409, 'Cererea e atribuită; anulează întâi atribuirea (prețurile contractate ies din proiect).');
  await q('delete from rfq_requests where id=$1', [r.id]); return stateOf(owner, id);
}
/** Proiectul blocat, cu versiunea verificată: scrierea prețurilor contractate nu suprascrie o schimbare făcută între timp. */
async function lockedDraft(q: Query, owner: string, id: string, rev: unknown){
  if (!isRev(rev)) throw new HttpError(400, 'Lipsește versiunea proiectului.');
  const { rows } = await q('select draft, draft_rev from projects where id=$1 and owner=$2 and deleted_at is null for update', [id, owner]);
  if (!rows[0]) throw new HttpError(404, 'Proiect inexistent.'); if (rows[0].draft_rev !== rev) throw PROJECT_CONFLICT();
  return rows[0].draft as Snapshot;
}
async function writeContract(q: Query, id: string, draft: Snapshot, next: LaborContract){
  const c = sanitizeLaborContract(next); if (c) draft.laborContract = c; else delete draft.laborContract;
  const up = await q('update projects set draft=$1, draft_rev=draft_rev+1, updated_at=now() where id=$2 returning draft_rev', [JSON.stringify(draft), id]);
  return { laborContract: c ?? {}, draftRev: up.rows[0].draft_rev as number };
}
/**
 * „Atribuie”: oferta trimisă a unei invitații devine preț contractat pe proiect, pe fiecare rând cu preț (rândurile neofertate
 * rămân pe estimarea de piață). Cererea se închide: ceilalți meseriași nu-și mai pot schimba ofertele. `rev` = versiunea
 * proiectului văzută de designer (409 la conflict). Prețul către client nu se schimbă: doar costul (core/commercial.ts).
 */
export async function awardRfq(owner: string, id: string, rid: unknown, input: unknown){
  const b = input as any; if (!isObj(b)) throw new HttpError(400, 'Cerere invalidă.');
  await getProject(owner, id); const { tx } = await getDb();
  const out = await tx(async q => {
    const draft = await lockedDraft(q, owner, id, b.rev), r = await requestRow(q, id, rid, true);
    if (r.awarded_invite) throw new HttpError(409, 'Cererea e deja atribuită; anulează întâi atribuirea.');
    const i = await inviteRow(q, r.id, b.invite); if (!i.submitted_at) throw new HttpError(409, 'Oferta nu a fost trimisă încă.');
    const lines = v(() => checkLines(r.lines), 409), offer = v(() => checkOffer(i.data ?? {}, lines), 409);
    const mine = contractFromOffer(lines, offer, i.label, r.id, new Date().toISOString()), prev = sanitizeLaborContract(draft.laborContract) ?? {};
    const res = await writeContract(q, id, draft, { ...prev, ...mine });
    await q('update rfq_requests set awarded_invite=$1, closed_at=now() where id=$2', [i.token, r.id]);
    return { ...res, applied: Object.keys(mine), partial: offerCoverage(offer, lines).partial }; });
  return { ...out, ...await stateOf(owner, id) };
}
/** Anulează atribuirea: prețurile contractate din această cerere ies din proiect, iar cererea se redeschide. */
export async function unawardRfq(owner: string, id: string, rid: unknown, input: unknown){
  const b = input as any; if (!isObj(b)) throw new HttpError(400, 'Cerere invalidă.');
  await getProject(owner, id); const { tx } = await getDb();
  const out = await tx(async q => {
    const draft = await lockedDraft(q, owner, id, b.rev), r = await requestRow(q, id, rid, true);
    if (!r.awarded_invite) throw new HttpError(409, 'Cererea nu e atribuită.');
    const prev = sanitizeLaborContract(draft.laborContract) ?? {}, res = await writeContract(q, id, draft, Object.fromEntries(Object.entries(prev).filter(([, e]) => e.requestId !== r.id)));
    await q('update rfq_requests set awarded_invite=null, closed_at=null where id=$1', [r.id]); return res; });
  return { ...out, ...await stateOf(owner, id) };
}
/** Scoate din proiect toate prețurile contractate (și cele rămase de la cereri șterse sau aduse de o revizie veche); cererile se redeschid. */
export async function clearLaborContract(owner: string, id: string, input: unknown){
  const b = input as any; if (!isObj(b)) throw new HttpError(400, 'Cerere invalidă.');
  await getProject(owner, id); const { tx } = await getDb();
  const out = await tx(async q => { const draft = await lockedDraft(q, owner, id, b.rev), res = await writeContract(q, id, draft, {});
    await q('update rfq_requests set awarded_invite=null, closed_at=null where project_id=$1', [id]); return res; });
  return { ...out, ...await stateOf(owner, id) };
}

// ---------- public (doar cu tokenul) ----------
/** Invitația unui link activ (altfel 404, fără detalii), cu cererea ei; `lock` o blochează până la sfârșitul tranzacției. */
async function open(q: Query, token: unknown, o: { lock?: boolean; write?: boolean } = {}){
  if (!isShareToken(token)) throw NOT_FOUND();
  // un proiect din coș are linkurile revocate (lib/trash.ts); verificarea pe proiect rămâne ca plasă de siguranță
  const { rows } = await q(`select i.token, i.data, i.rev, i.submitted_at, r.id as request_id, r.title, r.lines, r.note, r.site_city, r.due_date::text as due, r.closed_at, r.project_id
    from rfq_invites i join rfq_requests r on r.id = i.request_id where i.token=$1 and ${shareActive('i')}
    and exists (select 1 from projects p where p.id = r.project_id and p.deleted_at is null)${o.lock ? ' for update of i' : ''}`, [token]);
  if (!rows[0]) throw NOT_FOUND();
  if (o.write){ if (rows[0].submitted_at) throw SUBMITTED(); if (rows[0].closed_at) throw CLOSED(); }
  return rows[0];
}
/**
 * Ce vede meseriașul: studioul, titlul cererii, localitatea (doar dacă designerul a completat-o), termenul dorit, nota, rândurile
 * (denumire, UM, cantitate, camerele) și oferta LUI — ciorna sau, după trimitere, varianta trimisă (read-only). Nimic despre
 * client, buget, prețurile către client, reperul de piață sau ceilalți meseriași.
 */
export async function getPublicOffer(token: unknown){
  const { q } = await getDb(), r = await open(q, token); let lines: RfqLine[]; try { lines = checkLines(r.lines); } catch { throw NOT_FOUND(); }
  const studio = (await q(`select s.data->>'name' as name from projects p join studio_profile s on s.owner = p.owner where p.id=$1`, [r.project_id])).rows[0]?.name;
  let offer: RfqOffer; try { offer = checkOffer(r.data ?? {}, lines, { partial: true }); } catch { offer = checkOffer({}, lines, { partial: true }); }
  return { studio: cleanLine(studio, 160) || null, title: r.title as string, city: (r.site_city ?? null) as string | null, dueDate: day(r.due), note: r.note as string, lines: publicLines(lines),
    offer, rev: r.rev as number, submitted: !!r.submitted_at, submittedAt: iso(r.submitted_at), closed: !!r.closed_at, vatRate: VAT_RATE };
}
/** Salvarea automată: oferta întreagă (ciornă) + versiunea de la care a pornit formularul. Două file deschise nu se suprascriu: a doua primește 409. */
export async function savePublicOffer(token: unknown, body: unknown){
  if (!isShareToken(token)) throw NOT_FOUND();
  if (!isObj(body) || !isRev(body.rev) || Object.keys(body).some(k => k !== 'rev' && k !== 'data')) throw new HttpError(400, 'Cerere invalidă.');
  const { q } = await getDb(), r = await open(q, token, { write: true }), data = v(() => checkOffer(body.data, checkLines(r.lines), { partial: true }));
  const { rows } = await q(`update rfq_invites set data=$1, rev=rev+1, updated_at=now() where token=$2 and ${shareActive()} and submitted_at is null and rev=$3
    and exists (select 1 from rfq_requests r join projects p on p.id = r.project_id where r.id = rfq_invites.request_id and r.closed_at is null and p.deleted_at is null) returning rev`, [JSON.stringify(data), token, body.rev]);
  if (!rows[0]){ await open(q, token, { write: true }); throw CONFLICT(); }
  return { rev: rows[0].rev as number };
}
/** Trimiterea: oferta salvată trebuie să fie completă (fiecare rând cu preț sau „nu ofertez”). După ea linkul rămâne doar de citit. */
export async function submitPublicOffer(token: unknown, body: unknown){
  if (!isShareToken(token)) throw NOT_FOUND();
  if (!isObj(body) || !isRev(body.rev) || Object.keys(body).some(k => k !== 'rev')) throw new HttpError(400, 'Cerere invalidă.');
  const { tx } = await getDb();
  return tx(async q => {
    // întâi cererea (aceeași ordine ca la atribuire), apoi invitația: o trimitere nu poate ajunge după închiderea cererii
    await q('select r.id from rfq_requests r join rfq_invites i on i.request_id = r.id where i.token=$1 for update of r', [token]);
    const r = await open(q, token, { lock: true, write: true });
    if (r.rev !== body.rev) throw CONFLICT();
    const lines = v(() => checkLines(r.lines)), data = v(() => checkOffer(r.data ?? {}, lines));
    await q('update rfq_invites set data=$1, submitted_at=now(), updated_at=now() where token=$2', [JSON.stringify(data), token]);
    return { submitted: true as const, partial: offerCoverage(data, lines).partial }; });
}
