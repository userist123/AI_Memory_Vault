// Stadiul comenzilor pe linkul clientului: doar dacă designerul l-a activat în fișa proiectului. Se calculează din
// proiectul curent (nu din revizia partajată): livrările sunt viața de azi a proiectului, nu o versiune înghețată.
import { getDb } from './db';
import { readMeta } from '../core/project-meta';
import { clientProgress, type ClientProgress } from '../core/client-progress';
import { projectRows, ordersUpdatedAt } from './procurement';
import type { Snapshot } from '../core/types';
import { isShareToken } from './share';
/** null = linkul nu arată comenzile (opțiune oprită) sau linkul nu e activ. */
export async function progressForShare(token: unknown, today = new Date().toISOString().slice(0, 10)): Promise<ClientProgress | null> {
  if (!isShareToken(token)) return null;
  const { q } = await getDb();
  const { rows } = await q('select p.id, p.draft, p.meta from shares s join projects p on p.id = s.project_id where s.token=$1 and s.revoked_at is null', [token]);
  const p = rows[0], meta = readMeta(p?.meta); if (!p || !meta.shareOrders) return null;
  return clientProgress(await projectRows(p.id, p.draft as Snapshot, today, meta.startDate), await ordersUpdatedAt(p.id));
}
