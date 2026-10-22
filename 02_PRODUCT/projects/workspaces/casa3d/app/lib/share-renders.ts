// Imaginile 3D ale linkului pentru client (UI/UX P1-3): randate o singură dată, în browserul designerului, la publicarea
// linkului; serverul le păstrează pe revizie și le dă linkului ca fișiere obișnuite. Clientul vede coperta imediat (LCP fără
// motorul 3D); three.js se încarcă pe link doar pentru imaginile care lipsesc (linkuri vechi, randare întreruptă).
import { getDb } from './db';
import { HttpError, getProject } from './repo';
import { shareActive, isShareToken } from './share';
import { floors, levelView } from '../core/levels';
import type { Snapshot } from '../core/types';

export const RENDER_WIDTHS = [800, 1600] as const;
const KEY_RE = /^(overview|[A-Za-z0-9_-]{1,80})$/;
export const isRenderKey = (k: unknown): k is string => typeof k === 'string' && KEY_RE.test(k);
export const isRenderWidth = (w: unknown): w is number => RENDER_WIDTHS.includes(w as 800 | 1600);
/** Cel mult 1,2 MB pe imagine (o imagine WebP de 1600 px are de obicei 100–300 KB). */
export const RENDER_MAX_BYTES = 1.2 * 1024 * 1024;

/** O imagine trimisă ca data URL: doar WebP sau JPEG, verificate după primii octeți, nu după ce declară textul. */
export function parseRenderImage(dataUrl: unknown): { mime: 'image/webp' | 'image/jpeg'; base64: string } | null {
  if (typeof dataUrl !== 'string' || dataUrl.length > RENDER_MAX_BYTES * 4 / 3 + 64) return null;
  const m = /^data:(image\/(?:webp|jpeg));base64,([A-Za-z0-9+/]+={0,2})$/.exec(dataUrl); if (!m) return null;
  const bytes = Buffer.from(m[2]!, 'base64'); if (!bytes.length || bytes.length > RENDER_MAX_BYTES) return null;
  const webp = bytes.length > 12 && bytes.toString('latin1', 0, 4) === 'RIFF' && bytes.toString('latin1', 8, 12) === 'WEBP';
  const jpeg = bytes.length > 3 && bytes[0] === 0xff && bytes[1] === 0xd8 && bytes[2] === 0xff;
  const mime = m[1] as 'image/webp' | 'image/jpeg';
  return (mime === 'image/webp' && webp) || (mime === 'image/jpeg' && jpeg) ? { mime, base64: m[2]! } : null;
}
/** Cheile care au nevoie de imagine într-o revizie: vederea de ansamblu și fiecare cameră, pe toate nivelurile. */
export function renderKeys(snap: Snapshot): string[] {
  const out = ['overview']; for (let i = 0; i < floors(snap).length; i++) for (const r of levelView(snap, i).floor.rooms) if (isRenderKey(r.id) && !out.includes(r.id)) out.push(r.id);
  return out;
}
/** { cheie: lățimile existente } — forma pe care o primește pagina linkului. */
export type RenderSet = Record<string, number[]>;
const toSet = (rows: { key: string; width: number }[]): RenderSet => { const out: RenderSet = {};
  for (const r of rows) (out[r.key] ??= []).push(Number(r.width)); for (const k in out) out[k]!.sort((a, b) => a - b); return out; };

async function revisionSnapshot(id: string, n: number): Promise<Snapshot> {
  const { q } = await getDb(); const { rows } = await q('select snapshot from revisions where project_id=$1 and number=$2', [id, n]);
  if (!rows[0]) throw new HttpError(404, 'Revizie inexistentă.'); return rows[0].snapshot as Snapshot;
}
const revNumber = (v: unknown) => { const n = typeof v === 'string' && /^\d{1,9}$/.test(v) ? Number(v) : v; if (!Number.isInteger(n) || (n as number) < 1) throw new HttpError(400, 'Revizie invalidă.'); return n as number; };

/** Designerul: ce imagini există deja și ce lipsește; instantaneul reviziei (poate avea și 1 MB, cu planul importat)
 *  se trimite doar când e ceva de randat, ca verificarea din fundal de la fiecare deschidere a editorului să fie ieftină. */
export async function revisionRenderState(owner: string, id: string, rev: unknown){
  const n = revNumber(rev); await getProject(owner, id); const snapshot = await revisionSnapshot(id, n), { q } = await getDb();
  const { rows } = await q('select key, width from revision_renders where project_id=$1 and revision_number=$2', [id, n]);
  const keys = renderKeys(snapshot), have = toSet(rows), missing = keys.filter(k => !RENDER_WIDTHS.every(w => have[k]?.includes(w)));
  return { revisionNumber: n, keys, have, missing, snapshot: missing.length ? snapshot : null };
}
/** Designerul salvează o imagine: cheia trebuie să existe în revizie, lățimea e una din RENDER_WIDTHS. */
export async function saveRevisionRender(owner: string, id: string, rev: unknown, b: { key?: unknown; width?: unknown; image?: unknown }){
  const n = revNumber(rev); await getProject(owner, id);
  if (!isRenderKey(b.key) || !isRenderWidth(b.width)) throw new HttpError(400, 'Imagine invalidă.');
  if (!renderKeys(await revisionSnapshot(id, n)).includes(b.key)) throw new HttpError(400, 'Cameră inexistentă în revizie.');
  const img = parseRenderImage(b.image); if (!img) throw new HttpError(400, 'Imaginea trebuie să fie WebP sau JPEG, de cel mult 1,2 MB.');
  const { q } = await getDb();
  await q(`insert into revision_renders(project_id, revision_number, key, width, mime, data) values($1,$2,$3,$4,$5,$6)
    on conflict (project_id, revision_number, key, width) do update set mime=excluded.mime, data=excluded.data, created_at=now()`, [id, n, b.key, b.width, img.mime, img.base64]);
  return { ok: true };
}
/** Pagina linkului: imaginile disponibile ale reviziei partajate (gol pentru un link inactiv). */
export async function shareRenderSet(token: string): Promise<RenderSet> {
  if (!isShareToken(token)) return {}; const { q } = await getDb();
  const { rows } = await q(`select rr.key, rr.width from shares s join revision_renders rr on rr.project_id = s.project_id and rr.revision_number = s.revision_number
    where s.token=$1 and ${shareActive('s')}`, [token]);
  return toSet(rows);
}
/** O imagine pentru link; null dacă linkul nu e activ sau imaginea nu există. */
export async function shareRender(token: unknown, key: unknown, width: unknown): Promise<{ mime: string; bytes: Buffer } | null> {
  if (!isShareToken(token) || !isRenderKey(key) || !isRenderWidth(width)) return null; const { q } = await getDb();
  const { rows } = await q(`select rr.mime, rr.data from shares s join revision_renders rr on rr.project_id = s.project_id and rr.revision_number = s.revision_number
    where s.token=$1 and ${shareActive('s')} and rr.key=$2 and rr.width=$3`, [token, key, width]);
  return rows[0] ? { mime: String(rows[0].mime), bytes: Buffer.from(String(rows[0].data), 'base64') } : null;
}
