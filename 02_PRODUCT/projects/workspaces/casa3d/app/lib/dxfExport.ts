import { planToDxf } from '@/core/dxf';
import * as repo from './repo';
/** Exportul DXF al unui proiect al proprietarului (404 dacă nu există sau e al altcuiva). */
export async function exportProjectDxf(owner: string, id: string, lang: 'ro' | 'en'){
  const p = await repo.getProject(owner, id), cat = await repo.getCatalog();
  const safe = (p.name || 'plan').normalize('NFKD').replace(/[^\x20-\x7e]/g, '').replace(/[^A-Za-z0-9._-]+/g, '_').replace(/^[._]+|[._]+$/g, '').slice(0, 80) || 'plan';
  return { filename: `${safe}.dxf`, body: planToDxf(p.draft, cat, { lang }) };
}
