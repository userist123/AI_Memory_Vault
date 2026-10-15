// Datele comerciale ale proiectului (discount, adaos, grafic de plăți, setările proformei): doar proprietarul le citește
// și le scrie. Nu ajung niciodată pe linkurile clientului (rutele publice /api/share/* nu citesc tabelul project_commercial).
import { getDb } from './db';
import { HttpError, getProject, getCatalog, getMaterials } from './repo';
import { getProfile } from './studio-profile';
import { computeBudget } from '../core/boq';
import { feeEstimate } from '../core/project-meta';
import { floors } from '../core/levels';
import { area } from '../core/geometry';
import { checkCommercial, readCommercial, CommercialError, profitReport, paymentSchedule, buildProforma, proformaCsv,
  type CommercialData, type SourceLine, type SourceLabor } from '../core/commercial';

const today = () => new Date().toISOString().slice(0, 10);

export async function getCommercialData(owner: string, id: string): Promise<CommercialData> {
  await getProject(owner, id); const { q } = await getDb();
  return readCommercial((await q('select data from project_commercial where project_id=$1', [id])).rows[0]?.data);
}
export async function saveCommercial(owner: string, id: string, input: unknown): Promise<CommercialData> {
  let d: CommercialData; try { d = checkCommercial(input); } catch (e: any){ if (e instanceof CommercialError) throw new HttpError(400, e.message); throw e; }
  await getProject(owner, id); const { q } = await getDb();
  await q('insert into project_commercial(project_id, data) values($1,$2) on conflict(project_id) do update set data=excluded.data, updated_at=now()', [id, JSON.stringify(d)]);
  return d;
}

/** Datele, raportul de profit și graficul de plăți, calculate din planul curent (ciorna) al proiectului. */
export async function getCommercial(owner: string, id: string, now = today()){
  const p = await getProject(owner, id), data = await getCommercialData(owner, id);
  const b = computeBudget(p.draft, await getCatalog(), await getMaterials());
  const items: SourceLine[] = b.items.map(i => ({ key: i.key, category: i.category, label: i.label, supplier: i.supplier, qty: i.orderedQty, unit: i.unit, unitPrice: i.unitPrice, total: i.total }));
  const labor: SourceLabor[] = b.settings.includeLabor ? b.labor.map(l => ({ key: l.key, label: l.label, qty: l.qty, unit: l.unit, amount: l[b.settings.laborScenario] })) : [];
  const m2 = floors(p.draft).flatMap(f => f.rooms).reduce((a, r) => a + area(r.rect), 0);
  const report = profitReport(data, items, labor, feeEstimate(p.meta, m2)), schedule = paymentSchedule(data.milestones, report.totals.client, now);
  return { name: p.name, meta: p.meta, data, report, schedule, labor, suppliers: Object.keys(report.bySupplier).sort() };
}

/** Proforma pentru client: doar prețuri de client (fără cost, discount, adaos sau profit). */
export async function getProforma(owner: string, id: string, now = today()){
  const c = await getCommercial(owner, id, now), ms = c.data.proforma.milestoneId ? c.schedule.rows.find(r => r.id === c.data.proforma.milestoneId) ?? null : null;
  const proforma = buildProforma(c.report, c.labor, 'Onorariu proiectare design interior', ms);
  const issueDate = c.data.proforma.issueDate ?? now, dueDate = c.data.proforma.dueDate ?? ms?.due ?? null;
  return { name: c.name, client: c.meta.client ?? null, siteAddress: c.meta.siteAddress ?? null, seller: await getProfile(owner),
    series: c.data.proforma.series, number: c.data.proforma.number, issueDate, dueDate, proforma };
}
export async function getProformaCsv(owner: string, id: string){
  const p = await getProforma(owner, id);
  return { filename: `proforma-${p.series}-${p.number}.csv`, data: proformaCsv(p.proforma) };
}
