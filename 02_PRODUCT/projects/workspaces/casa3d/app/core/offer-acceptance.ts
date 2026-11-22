// Acceptarea ofertei pe linkul clientului: modelul pur. Oferta de acceptat se îngheață într-un instantaneu canonic (oferta
// tipărită + prețul + graficul de plăți + studioul + clientul + textele afișate și textele bifelor), cu amprentă SHA-256.
// Clientul confirmă cu numele tastat; e cel mult o semnătură electronică simplă (probă, nu semnătură calificată), iar
// aplicația nu pretinde altceva. Textele sunt un MODEL ORIENTATIV în română și nu constituie consultanță juridică.
// Funcția de amprentă vine din afară (node:crypto pe server), ca modulul să poată fi folosit și în browser.
import { buildOffer, feeFor, paymentSchedule, roDate, type DocContext, type DocSection } from './contract';
import { formatMoney } from './format';
import type { StudioProfile } from './studio-profile';
import type { ClientInfo } from './project-meta';

export const ACCEPT_FORMAT = 'casa3d-offer-acceptance', ACCEPT_LEGAL_VERSION = 'v2';
export const ACCEPT_LIMITS = { nameMin: 2, nameMax: 120, contact: 160, onBehalf: 160, perProject: 50 };
export const WITHDRAWAL_DAYS = 14;

// ---------- textele afișate clientului (versiunea v2) ----------
export interface AcceptTexts {
  version: string;
  /** natura confirmării și avertismentul că textele nu sunt consultanță juridică */
  nature: string; disclaimer: string;
  /** ce înseamnă apăsarea butonului și pe cine angajează */
  effect: string; binds: string;
  /** cum se modifică ulterior lucrarea, prețul sau termenele */
  changes: string;
  /** dreptul de retragere: cui i se aplică, sursa textului, instrucțiunile și consecințele din anexă, pierderea dreptului (art. 16 lit. a), formularul */
  withdrawal: { scope: string; source: string; title: string; info: string[]; consequencesTitle: string; consequences: string[]; loss: string; form: { title: string; note: string; lines: string[]; footnote: string } };
  /** textele celor două bife și eticheta butonului */
  acceptLabel: string; earlyStartLabel: string; buttonLabel: string;
  /** ce se păstrează și cât */
  retention: string;
}
const studioContact = (s: StudioProfile) => [s.name, s.address, s.email && `e-mail ${s.email}`, s.phone && `tel. ${s.phone}`].filter(Boolean).join(', ');
/**
 * Textele v2. Instrucțiunile și formularul de retragere sunt textul oficial al anexei la OUG nr. 34/2014 (forma consolidată la
 * 27.03.2026), completat pentru servicii; câmpurile profesionistului vin din profilul studioului (nume, adresă, telefon, e-mail
 * în instrucțiuni; nume, adresă, e-mail în formular). Punctele 3–5 din instrucțiunile anexei nu se aplică (fără funcție de
 * retragere online, fără bunuri). Rămân de verificat cu avocatul.
 */
export function legalTexts(studio: StudioProfile, client: ClientInfo): AcceptTexts {
  const whoFull = [studio.name, studio.address, studio.phone && `telefon ${studio.phone}`, studio.email && `e-mail ${studio.email}`].filter(Boolean).join(', ') || 'Prestatorul';
  const whoForm = [studio.name, studio.address, studio.email].filter(Boolean).join(', ') || 'Prestator';
  return {
    version: ACCEPT_LEGAL_VERSION,
    nature: 'Confirmare online a acceptării ofertei (nu este semnătură electronică calificată).',
    disclaimer: 'Textele de pe această pagină sunt un model orientativ și nu constituie consultanță juridică.',
    effect: 'Prin apăsarea butonului de mai jos acceptați fără rezerve oferta afișată pe această pagină, împreună cu graficul de plăți. Acceptarea vă obligă la plata prețului, potrivit graficului.',
    binds: `Oferta este adresată: ${client.name || 'persoanei care o acceptă'}. Acceptarea o angajează doar pe persoana care o confirmă și, dacă este cazul, persoana sau firma în numele căreia declară că acceptă; nu angajează un alt membru al familiei sau un alt coproprietar care nu a acceptat.`,
    changes: 'Modificările ulterioare ale lucrării, ale prețului sau ale termenelor se fac numai prin devize suplimentare (modificări cu preț) sau acte adiționale, aprobate separat de dumneavoastră, pe acest link sau în scris.',
    withdrawal: {
      scope: 'Informațiile de mai jos privesc clienții consumatori (persoane fizice care acționează în afara activității lor profesionale), pentru contractul încheiat la distanță (OUG nr. 34/2014, art. 9). Persoanele juridice și cei care contractează în scop profesional nu au acest drept de retragere.',
      source: 'Instrucțiunile și formularul reproduc anexa la OUG nr. 34/2014 (forma consolidată la 27.03.2026), completate pentru un contract de prestări de servicii — de verificat cu avocatul.',
      // partea A a anexei, „Dreptul de retragere”, completată pentru servicii (punctul 1 lit. a) și cu datele Prestatorului
      title: 'Dreptul de retragere',
      info: [
        `Aveți dreptul de a vă retrage din prezentul contract, fără a preciza motivele, în termen de ${WITHDRAWAL_DAYS} zile. Perioada de retragere expiră după ${WITHDRAWAL_DAYS} zile începând de la ziua încheierii contractului.`,
        `Pentru a vă exercita dreptul de retragere trebuie să ne informați (${whoFull}) cu privire la decizia dumneavoastră de a vă retrage din prezentul contract, utilizând o declarație neechivocă, de exemplu, o scrisoare trimisă prin poștă, fax sau e-mail. În acest scop, puteți folosi modelul de retragere alăturat; folosirea lui nu este însă obligatorie.`,
        'Pentru a respecta termenul-limită de retragere este suficient să trimiteți comunicarea privind exercitarea dreptului de retragere înainte de expirarea perioadei de retragere.',
      ],
      // partea A a anexei, „Consecințele retragerii” (cu punctul 6, pentru servicii)
      consequencesTitle: 'Consecințele retragerii',
      consequences: [
        `Dacă vă retrageți, vom rambursa orice sumă pe care am primit-o de la dumneavoastră, inclusiv costurile livrării, cu excepția costurilor suplimentare determinate de faptul că ați ales altă modalitate de livrare decât cel mai ieftin tip de livrare standard oferit de noi, fără întârzieri nejustificate și, în orice caz, nu mai târziu de ${WITHDRAWAL_DAYS} zile de la data la care suntem informați cu privire la decizia dumneavoastră de a vă retrage din prezentul contract.`,
        'Vom efectua această rambursare folosind aceeași modalitate de plată ca și cea folosită pentru tranzacția inițială, cu excepția cazului în care v-ați exprimat acordul expres pentru o altă modalitate de rambursare; în orice caz, nu vi se vor percepe comisioane ca urmare a unei astfel de rambursări.',
        'Dacă ați solicitat începerea prestării serviciilor în perioada de retragere, ne datorați o sumă proporțională cu serviciile furnizate până în momentul în care ne-ați comunicat exercitarea dreptului de retragere din prezentul contract, în raport cu acoperirea totală a contractului.',
      ],
      // art. 16 lit. a) din OUG nr. 34/2014 (explicație, nu citat din anexă)
      loss: 'Dreptul de retragere nu se aplică serviciilor executate integral, dacă executarea a început cu acordul dumneavoastră prealabil expres și după ce ați confirmat că ați luat cunoștință că vă pierdeți dreptul de retragere după executarea completă a contractului (OUG nr. 34/2014, art. 16 lit. a).',
      // partea B a anexei, „Model de formular de retragere”
      form: {
        title: 'Model de formular de retragere',
        note: '(Prezentul formular se trimite înapoi completat doar dacă doriți să vă retrageți din contract.)',
        lines: [
          `Către ${whoForm}:`,
          'Vă informez/Vă informăm (*) prin prezenta cu privire la retragerea mea/noastră (*) din contractul referitor la vânzarea următoarelor bunuri (*)/prestarea următoarelor servicii (*)',
          'Comandate la data (*)/Primite la data (*)',
          'Numele consumatorului (consumatorilor)',
          'Adresa consumatorului (consumatorilor)',
          'Semnătura consumatorului (consumatorilor) (doar în cazul în care acest formular este notificat pe hârtie)',
          'Data',
        ],
        footnote: '(*) A se elimina mențiunea inutilă, după caz.',
      },
    },
    acceptLabel: 'Am citit și accept oferta și graficul de plăți.',
    earlyStartLabel: `Cer începerea lucrărilor înainte de expirarea termenului de retragere de ${WITHDRAWAL_DAYS} zile, înțeleg că, dacă mă retrag, plătesc proporțional ce s-a executat și confirm că am luat cunoștință că îmi pierd dreptul de retragere după executarea completă a contractului.`,
    buttonLabel: 'Accept oferta — cu obligație de plată',
    retention: 'Ce se păstrează: versiunea exactă a ofertei afișate aici (cu textele și cu bifele), amprenta ei SHA-256, data și ora acceptării, numele pe care îl scrieți și, dacă le completați, datele de contact. Nu se păstrează adresa IP și nici date despre dispozitiv sau browser. Datele se păstrează cât există proiectul la Prestator și se șterg odată cu el. Aplicația nu trimite e-mail: după acceptare, descărcați sau tipăriți confirmarea.',
  };
}

// ---------- instantaneul canonic ----------
export interface OfferPayRow { key: 'deposit' | 'installment' | 'final'; n: number; pct: number; amount: number; label: string }
export interface OfferSnapshot {
  format: typeof ACCEPT_FORMAT; version: 1;
  project: { name: string; siteAddress?: string };
  studio: Omit<StudioProfile, 'logo'>;
  client: Pick<ClientInfo, 'name' | 'address' | 'phone' | 'email'>;
  /** oferta așa cum se tipărește (fără dată: data emiterii nu face parte din conținutul acceptat) */
  offer: { title: string; notice: string; studioLines: string[]; sections: DocSection[] };
  price: { currency: 'RON'; net: number | null; vat: number; gross: number | null; vatPayer: boolean; vatRate: number };
  schedule: OfferPayRow[];
  terms: { startDate?: string; dueDate?: string; validUntil?: string };
  texts: AcceptTexts;
}
const present = <T extends object>(o: T): T => Object.fromEntries(Object.entries(o).filter(([, v]) => v !== undefined && v !== null && v !== '')) as T;
/** Instantaneul ofertei de acceptat, din același context din care se tipărește oferta. Nu depinde de data zilei. */
export function buildOfferSnapshot(ctx: DocContext): OfferSnapshot {
  const doc = buildOffer(ctx), r = feeFor(ctx), rows = paymentSchedule(r.gross, ctx.data.depositPct, ctx.data.installments);
  const labels = doc.sections.find(s => s.id === 'payment')?.table?.rows ?? [];
  const { logo: _logo, ...studio } = ctx.studio, c = ctx.client;
  return {
    format: ACCEPT_FORMAT, version: 1,
    project: present({ name: ctx.projectName, siteAddress: ctx.siteAddress }),
    studio: present(studio), client: present({ name: c.name, address: c.address, phone: c.phone, email: c.email }),
    offer: { title: doc.title, notice: doc.notice, studioLines: doc.studioLines, sections: doc.sections },
    price: { currency: 'RON', net: r.net, vat: r.vat, gross: r.gross, vatPayer: r.vatPayer, vatRate: r.vatRate },
    schedule: rows.map((p, k) => ({ key: p.key, n: p.n, pct: p.pct, amount: p.amount, label: labels[k]?.[0] ?? '' })),
    terms: present({ startDate: ctx.meta.startDate, dueDate: ctx.meta.dueDate, validUntil: ctx.data.validUntil }),
    texts: legalTexts(ctx.studio, ctx.client),
  };
}

/**
 * Serializare canonică: chei sortate la orice adâncime, fără spații, valorile `undefined` omise, șirurile în forma NFC.
 * Același conținut dă același text indiferent de ordinea cheilor (de exemplu după o trecere prin jsonb).
 */
export function canonicalJson(v: unknown): string {
  if (v === null) return 'null';
  if (typeof v === 'string') return JSON.stringify(v.normalize('NFC'));
  if (typeof v === 'boolean') return v ? 'true' : 'false';
  if (typeof v === 'number'){ if (!Number.isFinite(v)) throw new Error('Număr nereprezentabil în instantaneu.'); return JSON.stringify(v); }
  if (Array.isArray(v)) return '[' + v.map(x => x === undefined ? 'null' : canonicalJson(x)).join(',') + ']';
  if (typeof v === 'object'){ const o = v as Record<string, unknown>;
    return '{' + Object.keys(o).filter(k => o[k] !== undefined).sort().map(k => JSON.stringify(k.normalize('NFC')) + ':' + canonicalJson(o[k])).join(',') + '}'; }
  throw new Error('Valoare nereprezentabilă în instantaneu.');
}
export type Sha256 = (text: string) => string;
/** Amprenta instantaneului: SHA-256 (hex) peste serializarea canonică, în UTF-8. */
export const offerHash = (snapshot: OfferSnapshot, sha256: Sha256) => sha256(canonicalJson(snapshot));
export const isOfferHash = (h: unknown): h is string => typeof h === 'string' && /^[0-9a-f]{64}$/.test(h);

export type NotAcceptable = 'no_price' | 'no_studio' | 'expired';
/** Oferta se poate accepta doar cu un preț total și un grafic de plăți, cu studioul identificat și în termenul de valabilitate. */
export function offerAcceptable(s: OfferSnapshot, today: string): { ok: true } | { ok: false; reason: NotAcceptable } {
  if (s.price.gross == null || !(s.price.gross > 0) || !s.schedule.length) return { ok: false, reason: 'no_price' };
  if (!s.studio.name || !(s.studio.email || s.studio.phone || s.studio.address)) return { ok: false, reason: 'no_studio' };
  if (s.terms.validUntil && s.terms.validUntil < today) return { ok: false, reason: 'expired' };
  return { ok: true };
}

// ---------- cererea de acceptare ----------
export type AcceptErrorCode = 'input' | 'pin' | 'name' | 'contact' | 'onBehalf' | 'accept' | 'earlyStart' | 'stale';
export class AcceptError extends Error { constructor(public code: AcceptErrorCode, msg: string){ super(msg); } }
/** Starea HTTP a fiecărei erori: PIN lipsă → 401, ofertă schimbată între afișare și clic → 409, restul → 400. */
export const acceptErrorStatus = (c: AcceptErrorCode) => c === 'pin' ? 401 : c === 'stale' ? 409 : 400;
export const STALE_MESSAGE = 'Oferta s-a actualizat între timp. Recitiți oferta de pe pagină și acceptați din nou.';
export interface AcceptanceInput { hash: string; name: string; contact: string | null; onBehalf: string | null; earlyStart: boolean }
const clean = (v: string) => v.replace(/[\u0000-\u001f\u007f​-‏‪-‮⁠-⁩﻿]/g, ' ').replace(/\s+/g, ' ').trim();
const EMAIL_RE = /^[^\s@]+@[^\s@]+\.[^\s@]+$/, PHONE_RE = /^\+?[\d\s().-]{6,30}$/;
/**
 * Validează cererea trimisă de client: { hash, name, contact?, onBehalf?, accept: true, earlyStart?: boolean }.
 * `ctx.pinVerified` = sesiunea PIN a linkului e validă; `ctx.currentHash` = amprenta ofertei curente de pe server.
 * Bifa de acceptare e obligatorie; bifa de începere înainte de 14 zile e opțională, dar se înregistrează exact cum a fost trimisă.
 */
export function checkAcceptance(input: unknown, ctx: { pinVerified: boolean; currentHash: string }): AcceptanceInput {
  if (!ctx.pinVerified) throw new AcceptError('pin', 'Introdu PIN-ul primit de la designer.');
  if (!input || typeof input !== 'object' || Array.isArray(input)) throw new AcceptError('input', 'Cerere invalidă.');
  const o = input as Record<string, unknown>;
  if (typeof o.name !== 'string' || o.name.length > ACCEPT_LIMITS.nameMax * 4) throw new AcceptError('name', 'Scrieți numele dumneavoastră complet.');
  const name = clean(o.name);
  if (name.length < ACCEPT_LIMITS.nameMin || name.length > ACCEPT_LIMITS.nameMax || !/\p{L}/u.test(name)) throw new AcceptError('name', `Scrieți numele dumneavoastră complet (${ACCEPT_LIMITS.nameMin}–${ACCEPT_LIMITS.nameMax} de caractere).`);
  let contact: string | null = null;
  if (o.contact != null && o.contact !== ''){
    if (typeof o.contact !== 'string' || o.contact.length > ACCEPT_LIMITS.contact) throw new AcceptError('contact', 'Datele de contact sunt prea lungi.');
    contact = clean(o.contact) || null;
    if (contact && !EMAIL_RE.test(contact) && !PHONE_RE.test(contact)) throw new AcceptError('contact', 'Scrieți o adresă de e-mail sau un număr de telefon (sau lăsați câmpul gol).');
  }
  let onBehalf: string | null = null;
  if (o.onBehalf != null && o.onBehalf !== ''){
    if (typeof o.onBehalf !== 'string' || o.onBehalf.length > ACCEPT_LIMITS.onBehalf) throw new AcceptError('onBehalf', 'Textul „în numele” este prea lung.');
    onBehalf = clean(o.onBehalf) || null;
  }
  if (o.accept !== true) throw new AcceptError('accept', 'Bifați că ați citit și acceptați oferta și graficul de plăți.');
  if (o.earlyStart !== undefined && typeof o.earlyStart !== 'boolean') throw new AcceptError('earlyStart', 'Cerere invalidă.');
  if (!isOfferHash(o.hash)) throw new AcceptError('input', 'Cerere invalidă.');
  if (o.hash !== ctx.currentHash) throw new AcceptError('stale', STALE_MESSAGE);
  return { hash: o.hash, name, contact, onBehalf, earlyStart: o.earlyStart === true };
}

// ---------- acceptările și starea lor ----------
export interface Acceptance {
  id: string; hash: string; revisionNumber: number; name: string; contact: string | null; onBehalf: string | null; earlyStart: boolean;
  /** momentul serverului, ISO în UTC */
  acceptedAt: string; withdrawnAt: string | null;
}
export interface AcceptanceRecord extends Acceptance { snapshot: OfferSnapshot }
/**
 * Cheia persoanei: numele fără diacritice, cu litere mici și spații simple („Ana  POP” și „ana pop” sunt aceeași persoană).
 * Aceeași versiune a ofertei poate fi acceptată de mai multe persoane (de exemplu ambii coproprietari), câte o dată fiecare.
 */
export const personKey = (name: string) => name.normalize('NFD').replace(/[̀-ͯ]/g, '').toLowerCase().replace(/\s+/g, ' ').trim();
export type AcceptanceState = 'none' | 'accepted' | 'changed' | 'withdrawn';
/**
 * Starea ofertei față de acceptări: `accepted` = versiunea curentă e acceptată; `changed` = există o acceptare în vigoare, dar
 * pentru altă versiune (oferta s-a schimbat după); `withdrawn` = toate acceptările au fost retrase; `none` = nicio acceptare.
 * `acceptance` e acceptarea la care se referă starea (cea mai nouă din categoria ei).
 */
export function acceptanceState<T extends Acceptance>(currentHash: string, list: T[]): { state: AcceptanceState; acceptance: T | null } {
  const byNewest = [...list].sort((a, b) => b.acceptedAt.localeCompare(a.acceptedAt)), active = byNewest.filter(a => !a.withdrawnAt);
  const same = active.find(a => a.hash === currentHash);
  if (same) return { state: 'accepted', acceptance: same };
  if (active[0]) return { state: 'changed', acceptance: active[0] };
  if (byNewest[0]) return { state: 'withdrawn', acceptance: byNewest[0] };
  return { state: 'none', acceptance: null };
}

export type OfferChangePart = 'price' | 'schedule' | 'studio' | 'client' | 'project' | 'terms' | 'texts' | 'section';
export interface OfferChange { part: OfferChangePart; /** titlul secțiunii din ofertă, pentru `section` */ title?: string; before?: string; after?: string }
const money = (n: number | null) => n == null ? '—' : formatMoney(n, 'RON', 'ro');
/** Ce diferă între oferta acceptată și cea curentă, pe părți (gol = același conținut). */
export function offerDiff(accepted: OfferSnapshot, current: OfferSnapshot): OfferChange[] {
  const out: OfferChange[] = [], differs = (a: unknown, b: unknown) => canonicalJson(a ?? null) !== canonicalJson(b ?? null);
  const price = differs(accepted.price, current.price), schedule = differs(accepted.schedule, current.schedule);
  if (price) out.push({ part: 'price', before: money(accepted.price.gross), after: money(current.price.gross) });
  if (schedule) out.push({ part: 'schedule' });
  for (const part of ['studio', 'client', 'project', 'terms'] as const) if (differs(accepted[part], current[part])) out.push({ part });
  // secțiunile ofertei: onorariul și plata sunt deja spuse prin preț și grafic
  const skip = new Set([...(price ? ['fee'] : []), ...(price || schedule ? ['payment'] : [])]);
  const before = new Map(accepted.offer.sections.map(s => [s.id, s])), after = new Map(current.offer.sections.map(s => [s.id, s]));
  for (const id of new Set([...before.keys(), ...after.keys()])){ if (skip.has(id)) continue;
    if (differs(before.get(id), after.get(id))) out.push({ part: 'section', title: (after.get(id) ?? before.get(id))!.title }); }
  if (differs({ t: accepted.offer.title, n: accepted.offer.notice, l: accepted.offer.studioLines }, { t: current.offer.title, n: current.offer.notice, l: current.offer.studioLines }) && !out.some(c => c.part === 'studio')) out.push({ part: 'studio' });
  if (differs(accepted.texts, current.texts)) out.push({ part: 'texts' });
  return out;
}

// ---------- confirmarea acceptării (document tipăribil și text descărcabil) ----------
const TZ = 'Europe/Bucharest';
/** Data și ora în România: „22.11.2026, 14:05:09”. */
export function roDateTime(iso: string): string {
  const p = Object.fromEntries(new Intl.DateTimeFormat('en-GB', { timeZone: TZ, year: 'numeric', month: '2-digit', day: '2-digit', hour: '2-digit', minute: '2-digit', second: '2-digit', hourCycle: 'h23' }).formatToParts(new Date(iso)).map(x => [x.type, x.value]));
  return `${p.day}.${p.month}.${p.year}, ${p.hour}:${p.minute}:${p.second}`;
}
/** Ziua calendaristică din România (AAAA-LL-ZZ) a unui moment. */
export const roDay = (iso: string | Date) => new Intl.DateTimeFormat('en-CA', { timeZone: TZ }).format(new Date(iso));
/** Ultima zi a termenului de retragere: 14 zile de la ziua acceptării (ora României). */
export function withdrawalDeadline(acceptedAt: string): string {
  return new Date(Date.parse(roDay(acceptedAt) + 'T00:00:00Z') + WITHDRAWAL_DAYS * 864e5).toISOString().slice(0, 10);
}
export interface ConfirmationDoc { title: string; nature: string; disclaimer: string; facts: [string, string][]; studioLines: string[]; offerTitle: string; sections: DocSection[] }
/** Confirmarea acceptării: datele acceptării, apoi tot conținutul înghețat (oferta, graficul, informările, bifele, amprenta). */
export function confirmationDoc(r: AcceptanceRecord): ConfirmationDoc {
  const s = r.snapshot, t = s.texts, tick = (on: boolean) => on ? '[x]' : '[ ]';
  const facts: [string, string][] = [
    ['Acceptată de', r.name], ...(r.onBehalf ? [['În numele', r.onBehalf] as [string, string]] : []), ...(r.contact ? [['Contact indicat', r.contact] as [string, string]] : []),
    ['Data și ora acceptării', `${roDateTime(r.acceptedAt)} (ora României) · ${r.acceptedAt} (UTC)`],
    ['Lucrarea', s.project.name], ['Prestator', studioContact(s.studio) || '—'], ['Oferta adresată', s.client.name || '—'],
    ['Preț total de plată', `${money(s.price.gross)} (${s.price.vatPayer ? `include TVA ${Math.round(s.price.vatRate * 100)}%: ${money(s.price.vat)}` : 'Prestatorul nu este plătitor de TVA'})`],
    ['Începere înainte de 14 zile', r.earlyStart ? 'cerută expres de client' : 'necerută'],
    ['Termen de retragere (consumatori)', `până la ${roDate(withdrawalDeadline(r.acceptedAt))} inclusiv`],
    ['Revizia prezentată pe link', String(r.revisionNumber)], ['Amprenta SHA-256 a conținutului acceptat', r.hash], ['Identificatorul acceptării', r.id],
    ...(r.withdrawnAt ? [['Retragere înregistrată', roDate(r.withdrawnAt.slice(0, 10))] as [string, string]] : []),
  ];
  const sections: DocSection[] = [...s.offer.sections,
    { id: 'acc-terms', title: 'Termene și modificări', paras: [
      `Începerea serviciilor: ${s.terms.startDate ? roDate(s.terms.startDate) : 'la data convenită de părți'}. Predare: ${s.terms.dueDate ? roDate(s.terms.dueDate) : 'la data convenită de părți'}.${s.terms.validUntil ? ` Oferta a fost valabilă până la ${roDate(s.terms.validUntil)}.` : ''}`, t.changes] },
    { id: 'acc-withdrawal', title: t.withdrawal.title, paras: [t.withdrawal.scope, ...t.withdrawal.info] },
    { id: 'acc-consequences', title: t.withdrawal.consequencesTitle, paras: [...t.withdrawal.consequences, t.withdrawal.loss] },
    { id: 'acc-form', title: t.withdrawal.form.title, paras: [t.withdrawal.form.note], items: t.withdrawal.form.lines },
    { id: 'acc-form-note', title: 'Sursa textelor despre retragere', paras: [t.withdrawal.form.footnote, t.withdrawal.source] },
    { id: 'acc-consent', title: 'Ce a confirmat clientul', paras: [t.effect, t.binds], items: [`${tick(true)} ${t.acceptLabel}`, `${tick(r.earlyStart)} ${t.earlyStartLabel}`, `Butonul apăsat: „${t.buttonLabel}”`] },
    { id: 'acc-data', title: 'Date păstrate', paras: [t.retention] },
  ];
  return { title: 'Confirmarea acceptării ofertei', nature: t.nature, disclaimer: t.disclaimer, facts, studioLines: s.offer.studioLines, offerTitle: s.offer.title, sections };
}
/** Confirmarea ca text simplu (suport durabil descărcabil); la final, instantaneul canonic din care se recalculează amprenta. */
export function confirmationText(r: AcceptanceRecord): string {
  const d = confirmationDoc(r), L: string[] = [d.title.toUpperCase(), d.nature, d.disclaimer, ''];
  for (const [k, v] of d.facts) L.push(`${k}: ${v}`);
  L.push('', '-'.repeat(60), ...d.studioLines, '', d.offerTitle.toUpperCase());
  for (const s of d.sections){ L.push('', s.title.toUpperCase(), ...s.paras);
    for (const i of s.items ?? []) L.push(`  - ${i}`);
    if (s.table){ L.push(`  ${s.table.head.join(' | ')}`); for (const row of s.table.rows) L.push(`  ${row.join(' | ')}`); } }
  L.push('', '-'.repeat(60), 'VERIFICAREA AMPRENTEI', 'Amprenta este SHA-256 (hexazecimal) peste rândul de mai jos, în UTF-8, fără sfârșitul de rând:', canonicalJson(r.snapshot), '');
  return L.join('\r\n');
}
export const confirmationFilename = (r: Pick<Acceptance, 'acceptedAt' | 'hash'>) => `confirmare-acceptare-oferta-${roDay(r.acceptedAt)}-${r.hash.slice(0, 8)}.txt`;
