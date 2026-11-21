import { snapshotToIfc } from '@/core/ifc-export';
import * as repo from './repo';
/** Exportul IFC4 (experimental, docs/bim/GHID_BIM.md faza 7) al unui proiect al proprietarului (404 dacă nu există sau e al altcuiva). */
export async function exportProjectIfc(owner: string, id: string){
  const p = await repo.getProject(owner, id), cat = await repo.getCatalog();
  const safe = (p.name || 'proiect').normalize('NFKD').replace(/[^\x20-\x7e]/g, '').replace(/[^A-Za-z0-9._-]+/g, '_').replace(/^[._]+|[._]+$/g, '').slice(0, 80) || 'proiect';
  const filename = `${safe}.ifc`;
  return { filename, body: snapshotToIfc(p.draft, cat, { fileName: filename, timestamp: new Date().toISOString().slice(0, 19), projectId: id }).text };
}
