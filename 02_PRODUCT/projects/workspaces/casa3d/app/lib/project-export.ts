// Exportul complet al unui proiect (portabilitate, GDPR art. 20): tot ce a lucrat designerul, în formate deschise —
// proiectul și reviziile în JSON, comenzile și devizul în CSV, pozele de pe șantier și randările ca fișiere.
// Linkurile clientului apar fără cheie (doar revizia și datele), ca arhiva să nu deschidă nimic dacă ajunge la altcineva.
import { promises as fs } from 'node:fs';
import path from 'node:path';
import { getDb } from './db';
import { getProject, getCatalog, getMaterials } from './repo';
import { getOrders } from './procurement';
import { listCustomItems } from './custom-items';
import { safeValue, quoteCell } from '../core/csv-safe';
import { listSnags } from './site';
import { listChanges } from './change-orders';
import { getTimesheet } from './timesheet';
import { getContractData, readStoredContract } from './contract';
import { listSurveys } from './surveys';
import { listFeedback } from './feedback';
import { listChoices } from './room-choices';
import { listTasks } from './tasks';
import { getCommercialData, readStoredCommercial } from './commercial';
import { computeBOQ } from '../core/boq';
import { zip, type ZipEntry } from './zip';

/** Tabel CSV (; și BOM): fiecare celulă text trece prin protecția comună împotriva formulelor (core/csv-safe.ts). */
export const csvTable = (rows: (string | number | null | undefined)[][]) => '﻿' + rows.map(r => r.map(v => quoteCell(safeValue(v))).join(';')).join('\r\n') + '\r\n';
const csv = csvTable;
/** Datele validate; dacă rândul stocat nu mai trece validarea (409), arhiva păstrează rândul brut, marcat, în loc să cadă. */
const orRaw = async <T>(get: () => Promise<T>, raw: () => Promise<unknown>) => { try { return await get(); } catch (e: any){ if (e?.status === 409) return { invalid: true, error: e.message, raw: await raw() }; throw e; } };
const safe = (s: string) => s.normalize('NFD').replace(/[̀-ͯ]/g, '').replace(/[^\w.-]+/g, '-').replace(/^-+|-+$/g, '').slice(0, 60) || 'proiect';

export async function exportProject(owner: string, id: string): Promise<{ filename: string; data: Buffer }> {
  const p = await getProject(owner, id), { q } = await getDb(), files: ZipEntry[] = [];
  files.push({ name: 'proiect.json', data: JSON.stringify({ id: p.id, name: p.name, meta: p.meta, draft: p.draft, exportedAt: new Date().toISOString(), format: 'casa3d-project-export', version: 1 }, null, 2) });
  const revs = (await q('select number, note, created_at, snapshot from revisions where project_id=$1 order by number', [id])).rows;
  for (const r of revs) files.push({ name: `revizii/revizia-${r.number}.json`, data: JSON.stringify({ number: r.number, note: r.note, createdAt: r.created_at, snapshot: r.snapshot }, null, 2) });
  const shares = (await q('select revision_number, created_at, revoked_at from shares where project_id=$1 order by created_at', [id])).rows;
  files.push({ name: 'linkuri-client.json', data: JSON.stringify(shares.map(s => ({ revision: s.revision_number, createdAt: s.created_at, revokedAt: s.revoked_at })), null, 2) });
  const orders = await getOrders(owner, id);
  files.push({ name: 'comenzi.csv', data: csv([['Produs', 'Cod', 'Camere', 'Furnizor', 'Cantitate', 'UM', 'Preț unitar', 'Total', 'Stare', 'Nr. comandă', 'Comandat', 'Livrare estimată', 'Livrat', 'Necesar pe șantier', 'Link'],
    ...orders.rows.map(r => [r.product, r.code, r.rooms.join(', '), r.supplier, r.qty, r.unit, r.unitPrice, r.total, r.state.status, r.state.ref, r.state.orderedAt, r.state.eta, r.state.deliveredAt, r.state.neededBy, r.url])]) });
  const boq = computeBOQ(p.draft, await getCatalog(), await getMaterials());
  files.push({ name: 'deviz.csv', data: csv([['Categorie', 'Cameră', 'Articol', 'Cantitate', 'UM', 'Preț unitar', 'Total', 'Furnizor', 'Sursă'],
    ...boq.items.map(i => [i.category, i.roomId ?? '', i.label, i.orderedQty, i.unit, i.unitPrice, i.total, i.supplier, i.sourceUrl ?? '']),
    ...(await listCustomItems(id)).map(i => ['produs propriu', i.roomId ?? '', i.name, i.qty, i.unit, i.price, i.price != null ? Math.round(i.price * i.qty * 100) / 100 : '', i.supplier, i.url]),
    ...boq.labor.map(l => ['manoperă', l.roomId, l.label, l.qty, l.unit, '', l.expected, '', '']),
    ...boq.unknown.map(u => ['de stabilit', '', u, '', '', '', '', '', ''])]) });
  const snags = await listSnags(owner, id); files.push({ name: 'santier/remedieri.json', data: JSON.stringify(snags, null, 2) });
  const photos = (await q('select id, mime, data from site_photos where project_id=$1', [id])).rows;
  for (const ph of photos) files.push({ name: `santier/poze/${ph.id}.${ph.mime === 'image/png' ? 'png' : ph.mime === 'image/webp' ? 'webp' : 'jpg'}`, data: Buffer.from(ph.data) });
  files.push({ name: 'modificari.json', data: JSON.stringify(await listChanges(owner, id), null, 2) });
  files.push({ name: 'pontaj.json', data: JSON.stringify(await getTimesheet(owner, id), null, 2) });
  files.push({ name: 'releveu.json', data: JSON.stringify(await listSurveys(owner, id), null, 2) });
  files.push({ name: 'pareri-client.json', data: JSON.stringify(await listFeedback(owner, id), null, 2) });
  files.push({ name: 'alegeri-variante.json', data: JSON.stringify(await listChoices(owner, id), null, 2) });
  files.push({ name: 'contract.json', data: JSON.stringify(await orRaw(() => getContractData(owner, id), () => readStoredContract(id)), null, 2) });
  files.push({ name: 'sarcini.json', data: JSON.stringify(await listTasks(owner, id), null, 2) });
  files.push({ name: 'comercial.json', data: JSON.stringify(await orRaw(() => getCommercialData(owner, id), () => readStoredCommercial(id)), null, 2) });   // discount, adaos, grafic de plăți (doar pentru designer)
  // randările de pe disc (doar imaginile, nu scena intermediară)
  const root = path.join(process.env.CASA3D_RENDER_DIR || path.join(process.cwd(), '.data', 'renders'), id);
  for (const job of await fs.readdir(root).catch(() => [] as string[])){ if (!/^\d{13}$/.test(job)) continue;
    for (const f of await fs.readdir(path.join(root, job)).catch(() => [] as string[])) if (/^[\w-]{1,80}\.png$/.test(f)) files.push({ name: `randari/${job}/${f}`, data: await fs.readFile(path.join(root, job, f)) }); }
  files.push({ name: 'CITESTE-MA.txt', data: `Exportul complet al proiectului „${p.name}” din Casa3D (${new Date().toISOString().slice(0, 10)}).\r\n\r\nproiect.json — planul, finisajele, mobilierul și fișa proiectului (se poate reimporta)\r\nrevizii/ — fiecare revizie salvată\r\ncomenzi.csv, deviz.csv — se deschid în Excel (separator ;)\r\nsantier/ — remedieri și poze\r\nrandari/ — imaginile randate\r\nmodificari, pontaj, releveu, pareri-client, alegeri-variante, sarcini, comercial — JSON\r\nlinkuri-client.json — doar datele linkurilor, fără cheile de acces\r\n` });
  return { filename: `${safe(p.name)}-casa3d-${new Date().toISOString().slice(0, 10)}.zip`, data: zip(files) };
}
