// Acceptarea ofertei pe linkul clientului (core/offer-acceptance.ts), pe server.
// Designerul o activează explicit, pe fiecare link în parte, și doar pe un link care are PIN; dezactivarea PIN-ului o oprește.
// Clientul vede oferta de acceptat doar cu sesiunea PIN a linkului (același cookie ca la pinii pe imagine și la deciziile cu
// bani), o acceptă cu numele tastat, iar serverul păstrează instantaneul exact, amprenta lui și momentul. Nu se păstrează
// adresa IP și nici agentul de browser.
//
// DIN CE SE CONSTRUIEȘTE OFERTA DE ACCEPTAT
// - din REVIZIA linkului (instantaneul reviziei partajate, ca restul linkului): numele lucrării, încăperile și suprafețele,
//   suprafața totală și baza onorariului procentual (bugetul de mobilare și finisaje al acelei revizii). Ce desenează
//   designerul în ciornă după publicare nu ajunge la client și nu schimbă oferta.
// - din datele curente ale proiectului, care nu fac parte din nicio revizie (stau lângă proiect, nu în plan): onorariul,
//   TVA-ul, etapele, avansul și tranșele, rundele de modificări și valabilitatea (project_contract), fișa proiectului
//   (clientul, adresa lucrării, datele de început și de predare), profilul studioului și produsele proprii cu prețurile de
//   catalog care intră în baza onorariului procentual. Acestea sunt chiar oferta comercială a designerului: când le schimbă,
//   oferta de pe link se schimbă, iar acceptarea veche rămâne în istoric și versiunea nouă cere altă acceptare.
// „Schimbată după acceptare” apare deci doar când oferta s-a schimbat pe aceeași revizie (date comerciale) sau când designerul
// a publicat o revizie nouă, cu acceptarea activată pe linkul ei, a cărei ofertă diferă de cea acceptată.
import { createHash } from 'node:crypto';
import { getDb } from './db';
import { HttpError, getProject } from './repo';
import { isShareToken, shareActive } from './share';
import { verifyPinSession } from './share-pin';
import { contractContext, projectBudget } from './contract';
import type { Snapshot } from '../core/types';
import { buildOfferSnapshot, offerHash, offerAcceptable, checkAcceptance, acceptanceState, offerDiff, confirmationText, confirmationFilename, roDay, personKey, AcceptError, acceptErrorStatus,
  ACCEPT_LIMITS, type OfferSnapshot, type Acceptance, type AcceptanceRecord, type AcceptanceState, type OfferChange, type NotAcceptable } from '../core/offer-acceptance';

export const sha256 = (text: string) => createHash('sha256').update(text, 'utf8').digest('hex');
const ID_RE = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i, DATE_RE = /^\d{4}-\d{2}-\d{2}$/;
const iso = (d: unknown) => d ? new Date(d as string).toISOString() : null;
const linkNotFound = () => new HttpError(404, 'Link inexistent.'), accNotFound = () => new HttpError(404, 'Acceptare inexistentă.');
const summary = (r: any): Acceptance => ({ id: r.id, hash: r.hash, revisionNumber: r.revision_number, name: r.accepted_name, contact: r.accepted_contact ?? null, onBehalf: r.accepted_for ?? null,
  earlyStart: !!r.early_start, acceptedAt: iso(r.accepted_at)!, withdrawnAt: iso(r.withdrawn_at) });
const record = (r: any): AcceptanceRecord => ({ ...summary(r), snapshot: r.snapshot as OfferSnapshot });
const strip = ({ snapshot: _s, ...a }: AcceptanceRecord): Acceptance => a;
const COLS = 'id, hash, revision_number, accepted_name, accepted_contact, accepted_for, early_start, accepted_at, withdrawn_at';
/** O acceptare împreună cu linkul pe care s-a făcut (intern: cheia linkului nu iese din acest modul). */
type Row = AcceptanceRecord & { shareToken: string };
/** Acceptările proiectului, cele mai noi întâi. */
async function acceptancesOf(projectId: string): Promise<Row[]> {
  const { q } = await getDb(), { rows } = await q(`select ${COLS}, snapshot, share_token from offer_acceptances where project_id=$1 order by accepted_at desc`, [projectId]);
  return rows.map(r => ({ ...record(r), shareToken: r.share_token as string }));
}
const pub = ({ shareToken: _t, ...a }: Row): AcceptanceRecord => a;

export interface CurrentOffer { snapshot: OfferSnapshot; hash: string; acceptable: { ok: true } | { ok: false; reason: NotAcceptable } }
/**
 * Oferta de acceptat pentru o revizie a proiectului, înghețată acum: instantaneul, amprenta și dacă se poate accepta azi
 * (ora României). Planul vine din revizie, datele comerciale din proiect (vezi antetul fișierului).
 */
export async function offerForRevision(owner: string, projectId: string, revisionNumber: number, now = new Date()): Promise<CurrentOffer> {
  const p = await getProject(owner, projectId), { q } = await getDb();
  const { rows: [rev] } = await q('select snapshot, created_at from revisions where project_id=$1 and number=$2', [projectId, revisionNumber]);
  if (!rev) throw new HttpError(404, 'Revizie inexistentă.');
  const published = { ...p, draft: rev.snapshot as Snapshot }, { budget } = await projectBudget(published);
  const ctx = await contractContext(owner, projectId, roDay(now), { p: published, budget });
  // fără dată de valabilitate, oferta e valabilă 30 de zile de la emitere = data reviziei partajate (fixă, deci amprenta nu se
  // schimbă de la o zi la alta); data limită intră în instantaneu și se aplică la acceptare
  if (!ctx.data.validUntil){ const d = new Date(rev.created_at); d.setUTCDate(d.getUTCDate() + 30); ctx.data = { ...ctx.data, validUntil: roDay(d) }; }
  const snapshot = buildOfferSnapshot(ctx);
  return { snapshot, hash: offerHash(snapshot, sha256), acceptable: offerAcceptable(snapshot, roDay(now)) };
}

// ---------- designer ----------
export interface AdminLink {
  token: string; revisionNumber: number; createdAt: string; expiresAt: string | null; pinEnabled: boolean;
  /** acceptarea online e pornită pe acest link (și linkul are PIN) */
  enabled: boolean;
  /** câte acceptări în vigoare s-au făcut pe acest link */
  accepted: number;
}
export interface AcceptanceAdmin {
  /** toate linkurile active ale proiectului, cele mai noi întâi: comutatorul e pe fiecare link */
  links: AdminLink[];
  /**
   * Oferta față de care se judecă starea: cea a celui mai nou link cu acceptarea pornită; fără niciunul, cea a reviziei
   * ultimei acceptări în vigoare; altfel a celui mai nou link (null dacă proiectul nu are niciun link și nicio acceptare).
   */
  offer: { revisionNumber: number; hash: string; acceptable: CurrentOffer['acceptable'] } | null;
  state: AcceptanceState; acceptance: Acceptance | null; changes: OfferChange[]; acceptances: Acceptance[];
}
const activeLinks = async (projectId: string) => { const { q } = await getDb();
  return (await q(`select token, revision_number, created_at, expires_at, (pin_hash is not null) as pin_enabled, offer_accept from shares where project_id=$1 and ${shareActive()} order by created_at desc`, [projectId])).rows; };
/** Panoul designerului: linkurile cu comutatorul lor, starea ofertei față de acceptări, ce s-a schimbat de la acceptare și istoricul. */
export async function acceptanceAdmin(owner: string, projectId: string): Promise<AcceptanceAdmin> {
  await getProject(owner, projectId);
  const [list, links] = await Promise.all([acceptancesOf(projectId), activeLinks(projectId)]);
  const ref: number | null = links.find(l => l.offer_accept && l.pin_enabled)?.revision_number ?? list.find(a => !a.withdrawnAt)?.revisionNumber ?? links[0]?.revision_number ?? null;
  const cur = ref == null ? null : await offerForRevision(owner, projectId, ref);
  const { state, acceptance } = cur ? acceptanceState(cur.hash, list) : { state: 'none' as AcceptanceState, acceptance: null };
  return { links: links.map(l => ({ token: l.token, revisionNumber: l.revision_number, createdAt: iso(l.created_at)!, expiresAt: iso(l.expires_at), pinEnabled: !!l.pin_enabled, enabled: !!l.offer_accept && !!l.pin_enabled,
      accepted: list.filter(a => a.shareToken === l.token && !a.withdrawnAt).length })),
    offer: cur && ref != null ? { revisionNumber: ref, hash: cur.hash, acceptable: cur.acceptable } : null, state, acceptance: acceptance ? strip(pub(acceptance)) : null,
    changes: cur && state === 'changed' && acceptance ? offerDiff(acceptance.snapshot, cur.snapshot) : [], acceptances: list.map(a => strip(pub(a))) };
}
/**
 * Comutatorul „Acceptare online” al unui link activ al proiectului. Activarea cere ca linkul să aibă PIN (altfel 409, cu
 * îndemnul de a-l seta); dezactivarea nu șterge acceptările deja făcute. Celelalte linkuri rămân cum sunt, iar panoul le arată.
 */
export async function setOfferAccept(owner: string, projectId: string, token: unknown, enabled: unknown): Promise<AcceptanceAdmin> {
  await getProject(owner, projectId); if (typeof enabled !== 'boolean') throw new HttpError(400, 'Cerere invalidă.');
  if (!isShareToken(token)) throw linkNotFound();
  const { q } = await getDb(), { rows: [s] } = await q(`select (pin_hash is not null) as pin_enabled from shares where token=$1 and project_id=$2 and ${shareActive()}`, [token, projectId]);
  if (!s) throw linkNotFound();
  if (enabled && !s.pin_enabled) throw new HttpError(409, 'Acceptarea online cere un PIN pe link. Generează întâi PIN-ul și trimite-l clientului.', { needsPin: true });
  await q(`update shares set offer_accept=$3 where token=$1 and project_id=$2 and ${shareActive()}${enabled ? ' and pin_hash is not null' : ''}`, [token, projectId, enabled]);
  return acceptanceAdmin(owner, projectId);
}
export async function listAcceptances(owner: string, projectId: string): Promise<Acceptance[]> {
  await getProject(owner, projectId); return (await acceptancesOf(projectId)).map(a => strip(pub(a)));
}
/** Acceptările cu instantaneul lor (exportul proiectului), fără cheia linkului. */
export async function listAcceptanceRecords(owner: string, projectId: string): Promise<AcceptanceRecord[]> {
  await getProject(owner, projectId); return (await acceptancesOf(projectId)).map(pub);
}
export async function getAcceptance(owner: string, projectId: string, id: unknown): Promise<AcceptanceRecord> {
  await getProject(owner, projectId); if (typeof id !== 'string' || !ID_RE.test(id)) throw accNotFound();
  const { q } = await getDb(), { rows } = await q(`select ${COLS}, snapshot from offer_acceptances where id=$1 and project_id=$2`, [id, projectId]);
  if (!rows[0]) throw accNotFound(); return record(rows[0]);
}
/**
 * Clientul s-a retras: designerul notează data (AAAA-LL-ZZ, între ziua acceptării și azi). Acceptarea iese din vigoare,
 * dar rămâne în istoric; o retragere deja notată nu se rescrie.
 */
export async function withdrawAcceptance(owner: string, projectId: string, id: unknown, date: unknown, now = new Date()): Promise<Acceptance> {
  const a = await getAcceptance(owner, projectId, id);
  if (typeof date !== 'string' || !DATE_RE.test(date) || Number.isNaN(Date.parse(date + 'T12:00:00Z'))) throw new HttpError(400, 'Data retragerii nu e validă (AAAA-LL-ZZ).');
  if (date < roDay(a.acceptedAt) || date > roDay(now)) throw new HttpError(400, 'Data retragerii trebuie să fie între ziua acceptării și azi.');
  const { q } = await getDb(), { rows } = await q(`update offer_acceptances set withdrawn_at=$3::timestamptz where id=$1 and project_id=$2 and withdrawn_at is null returning ${COLS}`, [a.id, projectId, `${date}T12:00:00Z`]);
  if (!rows[0]) throw new HttpError(409, 'Retragerea a fost deja notată.'); return summary(rows[0]);
}

// ---------- client (public, pe link) ----------
interface Gate { token: string; projectId: string; owner: string; revisionNumber: number; pinVerified: boolean }
/** Linkul trebuie să fie activ, al unui proiect care nu e în coș, cu acceptarea activată și cu PIN; orice altceva dă același 404. */
async function gate(token: unknown, pinValues: string[], now = Date.now()): Promise<Gate> {
  if (!isShareToken(token)) throw linkNotFound();
  const { q } = await getDb(), { rows: [s] } = await q(`select s.project_id, s.revision_number, s.pin_key, p.owner from shares s join projects p on p.id = s.project_id
    where s.token=$1 and ${shareActive('s')} and s.offer_accept and s.pin_key is not null and p.deleted_at is null`, [token]);
  if (!s) throw linkNotFound();
  return { token, projectId: s.project_id, owner: s.owner, revisionNumber: s.revision_number, pinVerified: pinValues.some(v => verifyPinSession(s.pin_key, token, v, now)) };
}
const needPin = () => new HttpError(401, 'Introdu PIN-ul primit de la designer.');
/** Datele contractului care nu mai trec validarea (409 la designer) nu se explică clientului. */
const offerOf = async (g: Gate, now?: Date) => { try { return await offerForRevision(g.owner, g.projectId, g.revisionNumber, now); } catch (e) { if (!(e instanceof HttpError)) throw e;
  throw e.status === 404 ? linkNotFound() : new HttpError(409, 'Oferta nu este disponibilă momentan. Luați legătura cu designerul.'); } };

export interface OfferForShare {
  state: AcceptanceState; offer: CurrentOffer;
  /** acceptarea la care se referă starea, cu tot conținutul ei înghețat (pentru confirmarea tipăribilă), dacă a fost făcută pe ACEST link */
  acceptance: AcceptanceRecord | null;
  /** starea se referă la o acceptare făcută pe alt link al proiectului: doar momentele ei, fără nume, conținut sau confirmare */
  elsewhere: { acceptedAt: string; withdrawnAt: string | null } | null;
  /** toate acceptările în vigoare ale versiunii curente făcute pe acest link (mai multe persoane pot accepta aceeași versiune) */
  acceptedBy: Acceptance[];
  changes: OfferChange[];
}
const RANK: Record<AcceptanceState, number> = { accepted: 3, changed: 2, withdrawn: 1, none: 0 };
/** Ce vede clientul în secțiunea „Oferta”: 404 dacă acceptarea nu e activată pe link, 401 fără sesiunea PIN. */
export async function offerForShare(token: unknown, pinValues: string[], now = new Date()): Promise<OfferForShare> {
  const g = await gate(token, pinValues, now.getTime()); if (!g.pinVerified) throw needPin();
  const [offer, all] = await Promise.all([offerOf(g, now), acceptancesOf(g.projectId)]), own = all.filter(a => a.shareToken === g.token);
  // starea ține cont de toate acceptările proiectului; detaliile se arată doar pentru cele făcute pe acest link
  const mine = acceptanceState(offer.hash, own), any = acceptanceState(offer.hash, all), pick = RANK[mine.state] >= RANK[any.state] ? mine : any;
  const here = pick.acceptance && pick.acceptance.shareToken === g.token ? pick.acceptance : null;
  return { state: pick.state, offer, acceptance: here ? pub(here) : null,
    elsewhere: pick.acceptance && !here ? { acceptedAt: pick.acceptance.acceptedAt, withdrawnAt: pick.acceptance.withdrawnAt } : null,
    acceptedBy: own.filter(a => !a.withdrawnAt && a.hash === offer.hash).map(a => strip(pub(a))),
    changes: pick.state === 'changed' && here ? offerDiff(here.snapshot, offer.snapshot) : [] };
}
/**
 * Clientul acceptă oferta. Cererea trebuie să poarte amprenta ofertei pe care a citit-o: dacă oferta s-a schimbat între
 * afișare și clic → 409. Se păstrează instantaneul construit de server (nu cel trimis de client). Aceeași versiune poate fi
 * acceptată de mai multe persoane (de exemplu ambii coproprietari); a doua acceptare a ACELEIAȘI persoane (după numele
 * normalizat) întoarce acceptarea existentă, neschimbată (`created: false`).
 */
export async function acceptOffer(token: unknown, pinValues: string[], input: unknown, now = new Date()): Promise<{ acceptance: AcceptanceRecord; created: boolean }> {
  const g = await gate(token, pinValues, now.getTime()); if (!g.pinVerified) throw needPin();
  const offer = await offerOf(g, now);
  let x; try { x = checkAcceptance(input, { pinVerified: g.pinVerified, currentHash: offer.hash }); }
  catch (e) { if (e instanceof AcceptError) throw new HttpError(acceptErrorStatus(e.code), e.message); throw e; }
  if (!offer.acceptable.ok) throw new HttpError(409, offer.acceptable.reason === 'expired' ? 'Termenul de valabilitate al ofertei a trecut. Cereți designerului o ofertă actualizată.' : 'Oferta nu este încă completă și nu poate fi acceptată. Luați legătura cu designerul.');
  const { tx } = await getDb(), key = personKey(x.name);
  return tx(async q => {
    const { rows: [n] } = await q('select count(*)::int as n from offer_acceptances where project_id=$1', [g.projectId]);
    const ins = n.n >= ACCEPT_LIMITS.perProject ? { rows: [] } : await q(`insert into offer_acceptances(project_id, share_token, revision_number, snapshot, hash, accepted_name, name_key, accepted_contact, accepted_for, early_start, accepted_at)
      values($1,$2,$3,$4,$5,$6,$7,$8,$9,$10, date_trunc('milliseconds', now())) on conflict (project_id, hash, name_key) where withdrawn_at is null do nothing returning ${COLS}, snapshot`,
      [g.projectId, g.token, g.revisionNumber, JSON.stringify(offer.snapshot), offer.hash, x.name, key, x.contact, x.onBehalf, x.earlyStart]);
    if (ins.rows[0]) return { acceptance: record(ins.rows[0]), created: true };
    const { rows } = await q(`select ${COLS}, snapshot from offer_acceptances where project_id=$1 and hash=$2 and name_key=$3 and withdrawn_at is null`, [g.projectId, offer.hash, key]);
    if (rows[0]) return { acceptance: record(rows[0]), created: false };
    throw new HttpError(429, 'Prea multe acceptări pe acest proiect. Luați legătura cu designerul.');
  });
}
/**
 * Confirmarea ca fișier text, pentru client: doar o acceptare făcută pe ACEST link (PIN-ul altui link al proiectului nu o
 * deschide). Cu `id`, acea acceptare; fără, cea la care se referă starea ofertei pe acest link.
 */
export async function confirmationForShare(token: unknown, pinValues: string[], id?: string | null): Promise<{ filename: string; text: string }> {
  const g = await gate(token, pinValues); if (!g.pinVerified) throw needPin();
  const own = (await acceptancesOf(g.projectId)).filter(a => a.shareToken === g.token);
  let a: Row | null | undefined;
  if (id){ if (!ID_RE.test(id)) throw accNotFound(); a = own.find(x => x.id === id.toLowerCase()); }
  else a = own.length ? acceptanceState((await offerOf(g)).hash, own).acceptance : null;
  if (!a) throw accNotFound();
  return confirmationFile(pub(a));
}
export const confirmationFile = (a: AcceptanceRecord) => ({ filename: confirmationFilename(a), text: confirmationText(a) });

// ---------- „De urmărit azi” ----------
/** Starea ofertei pentru proiectele care au o acceptare în vigoare (cel mult `max`); un proiect cu date nevalide e sărit. */
export async function offerStatesFor(owner: string, projectIds: string[], max = 20): Promise<Map<string, { state: 'accepted' | 'changed'; acceptedAt: string; name: string }>> {
  const out = new Map<string, { state: 'accepted' | 'changed'; acceptedAt: string; name: string }>(); if (!projectIds.length) return out;
  const { q } = await getDb(), { rows } = await q(`select project_id, hash, revision_number, accepted_name, accepted_at from offer_acceptances where project_id = any($1::uuid[]) and withdrawn_at is null order by accepted_at desc`, [projectIds]);
  if (!rows.length) return out;
  const by = new Map<string, any[]>(); for (const r of rows) by.set(r.project_id, [...(by.get(r.project_id) ?? []), r]);
  // revizia de referință, ca în panoul designerului: cel mai nou link cu acceptarea pornită, altfel revizia ultimei acceptări
  const { rows: on } = await q(`select distinct on (project_id) project_id, revision_number from shares where project_id = any($1::uuid[]) and ${shareActive()} and offer_accept and pin_hash is not null
    order by project_id, created_at desc`, [[...by.keys()]]);
  const ref = new Map(on.map(r => [r.project_id as string, r.revision_number as number]));
  for (const [pid, list] of [...by.entries()].slice(0, max)){
    try { const { hash } = await offerForRevision(owner, pid, ref.get(pid) ?? list[0].revision_number), same = list.find(r => r.hash === hash), a = same ?? list[0];
      out.set(pid, { state: same ? 'accepted' : 'changed', acceptedAt: iso(a.accepted_at)!, name: a.accepted_name }); } catch { /* proiectul rămâne fără elementul de ofertă */ }
  }
  return out;
}
