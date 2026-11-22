// „De urmărit azi” (core/attention.ts), pe server: faptele tuturor proiectelor ACTIVE ale proprietarului, strânse cu câte o
// interogare pe tabel (nu câte una pe proiect), apoi lista sortată. Doar proprietarul își vede proiectele; cele din coș lipsesc.
//
// Proiect ACTIV = modificat în ultimele 120 de zile SAU cu ceva nefinalizat: o comandă ofertată / comandată și nelivrată,
// o remediere neverificată ori o tranșă neplătită cu scadență. Un proiect vechi fără nimic din acestea nu mai apare.
//
// Cost: liniile de comandă se calculează din planul proiectului + catalog (partea scumpă), deci doar pentru proiectele active
// la care pot ieși întârzieri (au data de început a lucrărilor sau o comandă cu „necesar la” / livrare promisă), cel mult
// ORDER_PROJECTS proiecte (cele mai recent modificate), fără imaginea de calc din ciornă. Rezultatul se ține în memorie
// CACHE_MS pe proprietar și cade la prima scriere în bază (lib/db.ts dbWrites) sau la schimbarea zilei.
import { getDb, dbWrites } from './db';
import { getCatalog, getMaterials } from './repo';
import { listCustomItemsFor } from './custom-items';
import { shareActive } from './share';
import { offerStatesFor } from './offer-acceptance';
import type { Snapshot } from '../core/types';
import { readMeta } from '../core/project-meta';
import { readCommercial } from '../core/commercial';
import { checkSchedule, type Phase } from '../core/schedule';
import { procurementLines, procurementRows, type OrderState } from '../core/procurement';
import { attentionReport, ATTENTION, type ProjectFacts } from '../core/attention';

export const ATTENTION_ACTIVE_DAYS = 120, ORDER_PROJECTS = 30, PROJECTS_MAX = 200, CACHE_MS = 45_000;
export type AttentionResult = ReturnType<typeof attentionReport> & { today: string; projects: number;
  /** proiecte active peste limita de calcul al comenzilor: întârzierile lor de comandă nu sunt în listă */
  ordersSkipped: number };

const day = (v: unknown) => v == null ? null : typeof v === 'string' ? v.slice(0, 10) : new Date(v as Date).toISOString().slice(0, 10);
const iso = (v: unknown) => v == null ? null : new Date(v as string).toISOString();
const group = <T>(rows: any[], fn: (r: any) => T) => { const m = new Map<string, T[]>(); for (const r of rows) m.set(r.project_id, [...(m.get(r.project_id) ?? []), fn(r)]); return m; };

const cache = new Map<string, { at: number; writes: number; value: AttentionResult }>();
/** Uită rezultatul ținut în memorie (al unui proprietar sau al tuturor). */
export function invalidateAttention(owner?: string){ if (owner) cache.delete(owner); else cache.clear(); }

/** Faptele proiectelor active ale proprietarului, pentru ziua `today`. */
export async function attentionFacts(owner: string, today: string): Promise<{ facts: ProjectFacts[]; ordersSkipped: number }> {
  const { q } = await getDb();
  const { rows: ps } = await q(`select p.id, p.name, p.meta from projects p where p.owner=$1 and p.deleted_at is null and (
      p.updated_at > now() - make_interval(days => $2::int)
      or exists (select 1 from procurement o where o.project_id=p.id and o.state->>'status' in ('quoted','ordered'))
      or exists (select 1 from snags s where s.project_id=p.id and s.status <> 'verified')
      or exists (select 1 from project_commercial c, jsonb_array_elements(case when jsonb_typeof(c.data->'milestones') = 'array' then c.data->'milestones' else '[]'::jsonb end) m
        where c.project_id=p.id and m->>'status' = 'pending' and m ? 'due'))
    order by p.updated_at desc limit $3`, [owner, ATTENTION_ACTIVE_DAYS, PROJECTS_MAX]);
  if (!ps.length) return { facts: [], ordersSkipped: 0 };
  const ids = ps.map(p => p.id as string), any = [ids];
  // câte o interogare pe tabel, pentru toate proiectele active
  const [com, ord, snags, sched, stm, briefs, rfqs, shares, fb] = await Promise.all([
    q('select project_id, data from project_commercial where project_id = any($1::uuid[])', any),
    q('select project_id, key, state from procurement where project_id = any($1::uuid[])', any),
    q(`select project_id, status, created_at from snags where project_id = any($1::uuid[]) and status <> 'verified'`, any),
    q('select project_id, data from project_schedule where project_id = any($1::uuid[])', any),
    q(`select project_id, number, data->>'from' as "from", data->>'to' as "to" from progress_statements where project_id = any($1::uuid[])`, any),
    q('select distinct on (project_id) project_id, submitted_at, imported_at from client_briefs where project_id = any($1::uuid[]) order by project_id, created_at desc', any),
    q(`select r.project_id, r.id, r.title, r.due_date::text as due, (r.closed_at is null and r.awarded_invite is null) as open,
        (select count(*)::int from rfq_invites i where i.request_id=r.id and i.submitted_at is not null) as offers
      from rfq_requests r where r.project_id = any($1::uuid[])`, any),
    q(`select distinct on (project_id) project_id, expires_at from shares where project_id = any($1::uuid[]) and ${shareActive()} order by project_id, created_at desc`, any),
    q(`select project_id, count(*) filter (where verdict <> 'like')::int as open, count(*) filter (where verdict = 'like')::int as approvals, max(created_at) as latest
      from share_feedback where project_id = any($1::uuid[]) and resolved_at is null group by project_id`, any),
  ]);
  const one = (rows: any[]) => new Map(rows.map(r => [r.project_id as string, r]));
  const comBy = one(com.rows), schedBy = one(sched.rows), briefBy = one(briefs.rows), shareBy = one(shares.rows), fbBy = one(fb.rows);
  const statesBy = group(ord.rows, r => [r.key as string, r.state as OrderState] as const), snagsBy = group(snags.rows, r => ({ status: r.status, createdAt: iso(r.created_at)! }));
  const stmBy = group(stm.rows, r => ({ number: Number(r.number), from: String(r.from ?? ''), to: String(r.to ?? '') }));
  const rfqBy = group(rfqs.rows, r => ({ id: r.id as string, title: r.title as string, dueDate: day(r.due), open: !!r.open, offers: Number(r.offers) }));
  const metas = new Map(ps.map(p => [p.id as string, readMeta(p.meta)]));
  // starea ofertei: doar pentru proiectele cu o acceptare în vigoare (oferta curentă se recalculează, deci cel mult 20)
  const offers = await offerStatesFor(owner, ids);

  // liniile de comandă: doar unde pot ieși întârzieri (fără nicio dată, toate liniile sunt „de comandat”, fără termen)
  const dated = ids.filter(id => metas.get(id)!.startDate || (statesBy.get(id) ?? []).some(([, s]) => s?.neededBy || s?.status === 'ordered' && s.eta));
  const calc = dated.slice(0, ORDER_PROJECTS), orders = new Map<string, ProjectFacts['orders']>();
  if (calc.length){
    const [cat, mc, custom, drafts] = await Promise.all([getCatalog(), getMaterials(), listCustomItemsFor(calc), q(`select id, draft - 'underlay' as draft from projects where id = any($1::uuid[])`, [calc])]);
    for (const d of drafts.rows){
      // o ciornă pe care calculul nu o mai poate citi nu oprește lista celorlalte proiecte
      try { orders.set(d.id, procurementRows(procurementLines(d.draft as Snapshot, cat, mc, custom.get(d.id) ?? []), Object.fromEntries(statesBy.get(d.id) ?? []), today, metas.get(d.id)!.startDate)); } catch { /* proiectul rămâne fără comenzi în listă */ }
    }
  }
  const facts = ps.map((p): ProjectFacts => { const id = p.id as string, meta = metas.get(id)!, b = briefBy.get(id), sh = shareBy.get(id), f = fbBy.get(id);
    let phases: Phase[] = []; try { if (schedBy.get(id)) phases = checkSchedule(schedBy.get(id).data).phases; } catch { phases = []; }
    return { projectId: id, projectName: p.name as string, status: meta.status, dueDate: meta.dueDate ?? null, milestones: readCommercial(comBy.get(id)?.data).milestones, orders: orders.get(id) ?? [],
      snags: snagsBy.get(id) ?? [], phases, statements: stmBy.get(id) ?? [], brief: b ? { submittedAt: iso(b.submitted_at), importedAt: iso(b.imported_at) } : null, rfqs: rfqBy.get(id) ?? [],
      share: sh ? { expiresAt: iso(sh.expires_at) } : null, feedback: f ? { open: Number(f.open), approvals: Number(f.approvals), latest: iso(f.latest) } : null, offer: offers.get(id) ?? null }; });
  return { facts, ordersSkipped: dated.length - calc.length };
}

/** Lista „De urmărit azi” a proprietarului. `fresh` ocolește rezultatul ținut în memorie. */
export async function getAttention(owner: string, opt: { today?: string; fresh?: boolean; max?: number } = {}): Promise<AttentionResult> {
  const today = opt.today ?? new Intl.DateTimeFormat('en-CA', { timeZone: 'Europe/Bucharest' }).format(new Date()), hit = cache.get(owner);
  if (!opt.fresh && opt.max == null && hit && hit.value.today === today && hit.writes === dbWrites() && Date.now() - hit.at < CACHE_MS) return hit.value;
  const writes = dbWrites(), { facts, ordersSkipped } = await attentionFacts(owner, today);
  const value: AttentionResult = { ...attentionReport(facts, today, opt.max ?? ATTENTION.max), today, projects: facts.length, ordersSkipped };
  if (opt.max == null){ if (cache.size > 500) cache.clear(); cache.set(owner, { at: Date.now(), writes, value }); }
  return value;
}
