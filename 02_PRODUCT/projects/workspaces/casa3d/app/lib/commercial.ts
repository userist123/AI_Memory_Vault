// Datele comerciale ale proiectului (discount, adaos, grafic de plăți, setările proformei): doar proprietarul le citește
// și le scrie. Nu ajung niciodată pe linkurile clientului (rutele publice /api/share/* nu citesc tabelul project_commercial).
import { getDb } from './db';
import { HttpError, getProject } from './repo';
import { getProfile } from './studio-profile';
import { contractContext, projectBudget } from './contract';
import { feeFor } from '../core/contract';
import { checkCommercial, emptyCommercial, CommercialError, profitReport, paymentSchedule, buildProforma, proformaCsv,
  type CommercialData, type SourceLine, type SourceLabor } from '../core/commercial';

const today = () => new Date().toISOString().slice(0, 10);

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
  const items: SourceLine[] = b.items.map(i => ({ key: i.key, category: i.category, label: i.label, supplier: i.supplier, qty: i.orderedQty, unit: i.unit, unitPrice: i.unitPrice, total: i.total }));
  // produsele proprii: `price` e prețul de listă / către client (cu TVA, ca în catalog); costul real, dacă e cunoscut, e costPrice
  for (const c of custom) items.push({ key: 'c|' + c.id, category: 'custom', label: c.name, supplier: c.supplier, qty: c.qty, unit: c.unit,
    unitPrice: c.price, total: c.price == null ? null : Math.round(c.price * c.qty * 100) / 100, costTotal: c.costPrice == null ? null : Math.round(c.costPrice * c.qty * 100) / 100 });
  const labor: SourceLabor[] = b.settings.includeLabor ? b.labor.map(l => ({ key: l.key, label: l.label, qty: l.qty, unit: l.unit, amount: l[b.settings.laborScenario] })) : [];
  const report = profitReport(data, items, labor, fee.net, fee.vat), schedule = paymentSchedule(data.milestones, report.totals.client, now);
  return { name: p.name, meta: p.meta, data, report, schedule, labor, vatPayer: fee.vatPayer, suppliers: Object.keys(report.bySupplier).sort() };
}

/** Proforma pentru client: doar prețuri de client (fără cost, discount, adaos sau profit). */
export async function getProforma(owner: string, id: string, now = today()){
  const c = await getCommercial(owner, id, now), ms = c.data.proforma.milestoneId ? c.schedule.rows.find(r => r.id === c.data.proforma.milestoneId) ?? null : null;
  const proforma = buildProforma(c.report, c.labor, 'Onorariu proiectare design interior', ms, undefined, c.vatPayer);
  const issueDate = c.data.proforma.issueDate ?? now, dueDate = c.data.proforma.dueDate ?? ms?.due ?? null;
  return { name: c.name, client: c.meta.client ?? null, siteAddress: c.meta.siteAddress ?? null, seller: await getProfile(owner),
    series: c.data.proforma.series, number: c.data.proforma.number, issueDate, dueDate, proforma };
}
export async function getProformaCsv(owner: string, id: string){
  const p = await getProforma(owner, id);
  return { filename: `proforma-${p.series}-${p.number}.csv`, data: proformaCsv(p.proforma) };
}
