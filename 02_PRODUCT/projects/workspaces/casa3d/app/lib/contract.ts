// Contractul și oferta proiectului (doar proprietarul): datele se salvează în project_contract, documentele se construiesc din proiect.
import { getDb } from './db';
import { HttpError, getProject, getCatalog, getMaterials } from './repo';
import { getProfile } from './studio-profile';
import { listCustomItems, type CustomItem } from './custom-items';
import { checkContract, buildContract, buildOffer, feeFor, effectiveFee, paymentSchedule, ContractError, DEFAULT_CONTRACT, type ContractData, type DocContext } from '../core/contract';
import { computeBudget } from '../core/boq';
import { floors } from '../core/levels';
import { area } from '../core/geometry';

/** Rândul stocat, brut (null dacă nu există). */
export async function readStoredContract(id: string): Promise<unknown> {
  const { q } = await getDb(), { rows } = await q('select data from project_contract where project_id=$1', [id]); return rows[0]?.data ?? null;
}
/**
 * Datele salvate (sau implicite); 404 pentru alt proprietar. Un rând stocat care nu mai trece validarea NU e înlocuit tăcut
 * cu valorile implicite (interfața le-ar salva peste el): 409, iar rândul rămâne neatins.
 */
export async function getContractData(owner: string, id: string): Promise<ContractData> {
  await getProject(owner, id); const raw = await readStoredContract(id);
  if (raw == null) return { ...DEFAULT_CONTRACT, phases: [...DEFAULT_CONTRACT.phases] };
  try { return checkContract(raw); } catch (e: any){
    if (e instanceof ContractError) throw new HttpError(409, `Datele contractului salvate nu trec validarea (${e.message}). Nu au fost modificate; corectează-le sau salvează din nou contractul.`);
    throw e; }
}
export async function saveContract(owner: string, id: string, input: unknown): Promise<ContractData> {
  await getProject(owner, id); let d: ContractData; try { d = checkContract(input); } catch (e: any){ if (e instanceof ContractError) throw new HttpError(400, e.message); throw e; }
  const { q } = await getDb(); await q('insert into project_contract(project_id, data) values($1,$2) on conflict(project_id) do update set data=excluded.data, updated_at=now()', [id, JSON.stringify(d)]); return d;
}
type Project = Awaited<ReturnType<typeof getProject>>;
/** Bugetul proiectului cu produsele proprii incluse (aceeași bază pentru contract, profit și proformă). */
export async function projectBudget(p: Project): Promise<{ budget: ReturnType<typeof computeBudget>; custom: CustomItem[] }> {
  const [cat, mc, custom] = await Promise.all([getCatalog(), getMaterials(), listCustomItems(p.id)]);
  return { budget: computeBudget(p.draft, cat, mc, custom), custom };
}
/** Contextul documentelor: studio, client, camere, suprafață și baza pentru onorariul procentual (mobilare + finisaje + produse proprii, fără manoperă). */
export async function contractContext(owner: string, id: string, today = new Date().toISOString().slice(0, 10), pre?: { p: Project; budget: ReturnType<typeof computeBudget> }): Promise<DocContext> {
  const p = pre?.p ?? await getProject(owner, id), [data, studio, b] = await Promise.all([getContractData(owner, id), getProfile(owner), pre ? pre.budget : projectBudget(p).then(x => x.budget)]);
  const rooms = floors(p.draft).flatMap(f => f.rooms).map(r => ({ name: r.name, areaM2: Math.round(area(r.rect) * 100) / 100 })), areaM2 = Math.round(rooms.reduce((a, r) => a + r.areaM2, 0) * 100) / 100;
  const base = Math.round(Object.values(b.categories).reduce((a, v) => a + v, 0) * 100) / 100;
  return { studio, client: p.meta.client ?? {}, siteAddress: p.meta.siteAddress, projectName: p.draft.name || p.name, rooms, areaM2, meta: p.meta, data, budgetBase: base > 0 ? base : null, today };
}
/** Tot ce trebuie paginilor /contract și /offer și panoului: datele, calculul onorariului, planul de plată și cele două documente. */
export async function getContractBundle(owner: string, id: string, today?: string) {
  const ctx = await contractContext(owner, id, today), fee = feeFor(ctx);
  return { data: ctx.data, effectiveFee: effectiveFee(ctx.meta, ctx.data) ?? null, areaM2: ctx.areaM2, budgetBase: ctx.budgetBase, calc: fee, schedule: paymentSchedule(fee.gross, ctx.data.depositPct, ctx.data.installments), contract: buildContract(ctx), offer: buildOffer(ctx) };
}
