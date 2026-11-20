// Datele comerciale ale proiectului (discount, adaos, grafic de plăți, setările proformei): doar proprietarul le citește
// și le scrie. Nu ajung niciodată pe linkurile clientului (rutele publice /api/share/* nu citesc tabelul project_commercial).
import { getDb } from './db';
import { HttpError, getProject } from './repo';
import { getProfile } from './studio-profile';
import { contractContext, projectBudget } from './contract';
import { feeFor } from '../core/contract';
import { checkCommercial, emptyCommercial, CommercialError, profitReport, paymentSchedule, buildProforma, proformaCsv, commercialSources, projectChain, COMMERCIAL_LIMITS,
  type CommercialData } from '../core/commercial';

// data zilei în România (YYYY-MM-DD): după miezul nopții ora României, UTC e încă „ieri”, iar data emiterii rămâne înghețată în document
const today = () => new Intl.DateTimeFormat('en-CA', { timeZone: 'Europe/Bucharest', year: 'numeric', month: '2-digit', day: '2-digit' }).format(new Date());

/** Rândul stocat, brut (null dacă nu există). */
export async function readStoredCommercial(id: string): Promise<unknown> {
  const { q } = await getDb(); return (await q('select data from project_commercial where project_id=$1', [id])).rows[0]?.data ?? null;
}
/**
 * Datele salvate (sau goale dacă nu există rând). Un rând care nu mai trece validarea NU e înlocuit tăcut cu date goale
 * (interfața le-ar salva peste el): 409 cu un mesaj clar, iar rândul rămâne neatins.
 */
export async function getCommercialData(owner: string, id: string): Promise<CommercialData> {
  await getProject(owner, id); const raw = await readStoredCommercial(id);
  if (raw == null) return emptyCommercial();
  try { return checkCommercial(raw); } catch (e: any){
    if (e instanceof CommercialError) throw new HttpError(409, `Datele comerciale salvate nu trec validarea (${e.message}). Nu au fost modificate; corectează-le înainte de a lucra mai departe.`);
    throw e; }
}
export async function saveCommercial(owner: string, id: string, input: unknown): Promise<CommercialData> {
  let d: CommercialData; try { d = checkCommercial(input); } catch (e: any){ if (e instanceof CommercialError) throw new HttpError(400, e.message); throw e; }
  await getProject(owner, id); const { q } = await getDb();
  await q('insert into project_commercial(project_id, data) values($1,$2) on conflict(project_id) do update set data=excluded.data, updated_at=now()', [id, JSON.stringify(d)]);
  return d;
}

/**
 * Datele, raportul de profit și graficul de plăți, calculate din planul curent (ciorna) al proiectului.
 * Bugetul include produsele proprii; onorariul se calculează O SINGURĂ DATĂ, prin feeFor din contract (onorariul contractului
 * sau al fișei, suprafața, baza bugetului, plătitor de TVA sau nu), deci contractul și proforma dau aceleași sume.
 */
export async function getCommercial(owner: string, id: string, now = today()){
  const p = await getProject(owner, id), data = await getCommercialData(owner, id), { budget: b, custom } = await projectBudget(p);
  const fee = feeFor(await contractContext(owner, id, now, { p, budget: b }));
  // aceleași linii ca bugetul (core/commercial.ts commercialSources): produse, produse proprii, manoperă, transport și montaj
  const { items, labor, extras } = commercialSources(b, custom);
  const report = profitReport(data, items, labor, fee.net, fee.vat, extras), schedule = paymentSchedule(data.milestones, report.totals.client, now);
  const chain = projectChain(report, b.settings.contingencyPct);
  return { name: p.name, meta: p.meta, data, report, schedule, labor, extras, chain, fee: { kind: fee.kind, net: fee.net, vat: fee.vat, gross: fee.gross, vatPayer: fee.vatPayer, reason: fee.reason ?? null },
    vatPayer: fee.vatPayer, suppliers: Object.keys(report.bySupplier).sort() };
}

/** Proforma pentru client: doar prețuri de client (fără cost, discount, adaos sau profit). */
export async function getProforma(owner: string, id: string, now = today()){
  const c = await getCommercial(owner, id, now), ms = c.data.proforma.milestoneId ? c.schedule.rows.find(r => r.id === c.data.proforma.milestoneId) ?? null : null;
  const proforma = buildProforma(c.report, c.labor, 'Onorariu proiectare design interior', ms, undefined, c.vatPayer, c.extras);
  const issueDate = c.data.proforma.issueDate ?? now, dueDate = c.data.proforma.dueDate ?? ms?.due ?? null;
  return { name: c.name, client: c.meta.client ?? null, siteAddress: c.meta.siteAddress ?? null, seller: await getProfile(owner),
    series: c.data.proforma.series, number: c.data.proforma.number, issueDate, dueDate, proforma,
    // doar pentru designer (bara „no-print”): cum se leagă proforma de totalul proiectului din buget
    chain: { invoiceable: c.chain.invoiceable, contingency: c.chain.contingency, contingencyPct: c.chain.contingencyPct, total: c.chain.total } };
}
/** Ciorna sau, cu serie și număr, proforma emisă (înghețată) pentru CSV-ul de facturare. */
export async function getProformaCsv(owner: string, id: string, issued?: { series: string; number: number }){
  const p = issued ? await getIssuedProforma(owner, id, issued.series, issued.number) : await getProforma(owner, id);
  return { filename: `proforma-${safeName(p.series)}-${p.number}.csv`, data: proformaCsv(p.proforma) };
}
const safeName = (s: string) => s.replace(/[^\w.-]+/g, '-').slice(0, 20) || 'PF';

// ---------- proforma emisă ----------
// Emiterea îngheață documentul: ce vede clientul atunci (linii, prețuri cu data și sursa lor, vânzător, cumpărător) rămâne exact
// așa, oricât s-ar schimba planul, catalogul sau profilul studioului. Doar inserare (fără UPDATE/DELETE), cheie unică
// (proiect, serie, număr): a doua emitere cu același număr — inclusiv simultană — primește 409 și nu atinge prima.
const MAX_ISSUED_BYTES = 1_000_000;
export async function issueProforma(owner: string, id: string, now = today()){
  const p = await getProforma(owner, id, now);
  if (!p.proforma.groups.length) throw new HttpError(400, 'Proforma nu are nicio linie cu preț: nu se poate emite.');
  const { chain: _chain, ...doc } = p; void _chain;
  const frozen = { ...doc, issueDate: p.issueDate, frozen: true as const }, json = JSON.stringify(frozen);
  if (json.length > MAX_ISSUED_BYTES) throw new HttpError(413, 'Proforma e prea mare pentru a fi emisă.');
  const { q } = await getDb();
  const r = await q('insert into issued_proformas(project_id, series, number, data) values($1,$2,$3,$4) on conflict(project_id, series, number) do nothing returning issued_at', [id, p.series, p.number, json]);
  if (!r.rows[0]) throw new HttpError(409, `Proforma ${p.series} nr. ${p.number} a fost deja emisă. Alege alt număr în setările proformei.`);
  // numărul următor: o singură instrucțiune care atinge doar câmpurile proformei (alte setări salvate între timp rămân) și doar
  // dacă seria și numărul sunt încă cele emise; data emiterii și scadența se golesc, ca proforma următoare să nu le moștenească.
  // Dacă nu se poate (număr maxim, setări schimbate între timp), răspunsul o spune: nextSaved=false.
  const nextNo = p.number + 1; let nextSaved = false;
  if (nextNo <= COMMERCIAL_LIMITS.numberMax){
    const u = await q(`update project_commercial set data = jsonb_set((data #- '{proforma,issueDate}') #- '{proforma,dueDate}', '{proforma,number}', to_jsonb($2::int)), updated_at = now()
      where project_id = $1 and coalesce(data->'proforma'->>'series', '') = $3 and coalesce((data->'proforma'->>'number')::int, 1) = $4::int returning 1`, [id, nextNo, p.series, p.number]);
    if (u.rows[0]) nextSaved = true;
    else if ((await readStoredCommercial(id)) == null){ const e = emptyCommercial(); await saveCommercial(owner, id, { ...e, proforma: { ...e.proforma, series: p.series, number: nextNo } }); nextSaved = true; } }
  return { series: p.series, number: p.number, issuedAt: r.rows[0].issued_at, nextSaved };
}
export async function listIssuedProformas(owner: string, id: string){
  await getProject(owner, id); const { q } = await getDb();
  return (await q(`select series, number, issued_at, data->'proforma'->>'gross' as gross from issued_proformas where project_id=$1 order by issued_at desc`, [id])).rows
    .map((r: any) => ({ series: r.series as string, number: Number(r.number), issuedAt: r.issued_at, gross: r.gross == null ? null : Number(r.gross) }));
}
export async function getIssuedProforma(owner: string, id: string, series: string, number: number){
  await getProject(owner, id); const { q } = await getDb();
  const r = (await q('select data, issued_at from issued_proformas where project_id=$1 and series=$2 and number=$3', [id, series, number])).rows[0];
  if (!r) throw new HttpError(404, 'Proforma emisă nu există.');
  const data = typeof r.data === 'string' ? JSON.parse(r.data) : r.data;
  return { ...data, issuedAt: r.issued_at, frozen: true as const } as Awaited<ReturnType<typeof getProforma>> & { issuedAt: string; frozen: true };
}
