// Randări fotorealiste pe calculatorul pe care rulează aplicația: scena din vederea 3D → Blender Cycles (tools/blender/casa3d_render.py)
// → câte o imagine pe cameră. Doar dacă CASA3D_BLENDER arată spre blender.exe; un singur job odată; fișierele stau în
// CASA3D_RENDER_DIR (implicit .data/renders/<proiect>/<job>/). Numele de job și de fișier sunt validate strict.
import { spawn, type ChildProcess } from 'node:child_process';
import { promises as fs, existsSync, createWriteStream } from 'node:fs';
import path from 'node:path';
import { HttpError, getProject } from './repo';

const ROOT = () => process.env.CASA3D_RENDER_DIR || path.join(process.cwd(), '.data', 'renders');
const SCRIPT = () => path.join(process.cwd(), 'tools', 'blender', 'casa3d_render.py');
export const JOB_RE = /^\d{13}$/, FILE_RE = /^[\w-]{1,80}\.png$/, ID_RE = /^[\w-]{1,80}$/;
const MAX_SCENE = 40 * 1024 * 1024, TIMEOUT_MS = 45 * 60 * 1000;
const running = new Map<string, ChildProcess>();   // proiect → procesul Blender

/** Șterge toate randările unui proiect de pe disc (la ștergerea definitivă). */
export async function removeRenders(projectId: string){ if (!ID_RE.test(projectId)) return; await fs.rm(path.join(ROOT(), projectId), { recursive: true, force: true }); }

export function blenderAvailable(){ const b = process.env.CASA3D_BLENDER; return !!b && existsSync(b); }
export interface RenderJob { job: string; status: 'running' | 'done' | 'failed'; images: string[]; startedAt: string; samples: number }

export async function startRender(owner: string, projectId: string, body: string, opts: { samples?: unknown; rooms?: unknown }): Promise<RenderJob> {
  if (!ID_RE.test(projectId)) throw new HttpError(400, 'Proiect invalid.');
  await getProject(owner, projectId);                                   // proiectul trebuie să fie al tău
  if (!blenderAvailable()) throw new HttpError(501, 'Blender nu e configurat pe acest calculator (CASA3D_BLENDER).');
  if (running.size > 0) throw new HttpError(409, 'O randare e deja în lucru; încearcă după ce se termină.');
  if (body.length > MAX_SCENE) throw new HttpError(413, 'Scena e prea mare.');
  let scene: any; try { scene = JSON.parse(body); } catch { throw new HttpError(400, 'Scena nu e JSON valid.'); }
  if (!scene || scene.format !== 'casa3d-scene' || !Array.isArray(scene.rooms) || !Array.isArray(scene.meshes)) throw new HttpError(400, 'Scena nu vine din vederea 3D.');
  const samples = Number.isInteger(opts.samples) && (opts.samples as number) >= 16 && (opts.samples as number) <= 2048 ? opts.samples as number : 256;
  const rooms = typeof opts.rooms === 'string' && /^(all|[\w-]{1,80}(,[\w-]{1,80}){0,40})$/.test(opts.rooms) ? opts.rooms : 'all';
  const job = String(Date.now()), dir = path.join(ROOT(), projectId, job); await fs.mkdir(dir, { recursive: true });
  const scenePath = path.join(dir, 'scene.casa3d.json'); await fs.writeFile(scenePath, body);
  await fs.writeFile(path.join(dir, 'status.json'), JSON.stringify({ status: 'running', startedAt: new Date().toISOString(), samples }));
  const log = createWriteStream(path.join(dir, 'blender.log'));
  // un fișier .js/.mjs (înlocuitor de Blender, ex. în teste) rulează cu Node-ul curent; altfel e executabilul Blender
  const bin = process.env.CASA3D_BLENDER!, viaNode = /\.m?js$/i.test(bin);
  const p = spawn(viaNode ? process.execPath : bin, [...(viaNode ? [bin] : []), '-b', '-P', SCRIPT(), '--', scenePath, dir, '--samples', String(samples), '--width', '1600', '--height', '1000', '--rooms', rooms], { windowsHide: true });
  running.set(projectId, p); p.stdout?.pipe(log); p.stderr?.pipe(log);
  const timer = setTimeout(() => p.kill(), TIMEOUT_MS);
  p.on('close', async code => { clearTimeout(timer); running.delete(projectId);
    await fs.writeFile(path.join(dir, 'status.json'), JSON.stringify({ status: code === 0 ? 'done' : 'failed', code, startedAt: new Date(Number(job)).toISOString(), samples })).catch(() => {}); });
  p.on('error', () => { running.delete(projectId); });
  return { job, status: 'running', images: [], startedAt: new Date(Number(job)).toISOString(), samples };
}

export async function listRenders(owner: string, projectId: string): Promise<{ available: boolean; jobs: RenderJob[] }> {
  if (!ID_RE.test(projectId)) throw new HttpError(400, 'Proiect invalid.'); await getProject(owner, projectId);
  const base = path.join(ROOT(), projectId); let names: string[] = []; try { names = (await fs.readdir(base)).filter(n => JOB_RE.test(n)).sort().reverse().slice(0, 12); } catch { /* niciun job */ }
  const jobs = await Promise.all(names.map(async job => { const dir = path.join(base, job); let st: any = {}; try { st = JSON.parse(await fs.readFile(path.join(dir, 'status.json'), 'utf8')); } catch { /* lipsă */ }
    const images = (await fs.readdir(dir).catch(() => [] as string[])).filter(f => FILE_RE.test(f)).sort();
    const status: RenderJob['status'] = st.status === 'running' && !running.has(projectId) ? 'failed' : st.status ?? 'failed';
    return { job, status, images, startedAt: st.startedAt ?? new Date(Number(job)).toISOString(), samples: st.samples ?? 0 }; }));
  return { available: blenderAvailable(), jobs };
}

export async function readRender(owner: string, projectId: string, job: string, file: string): Promise<Buffer> {
  if (!ID_RE.test(projectId) || !JOB_RE.test(job) || !FILE_RE.test(file)) throw new HttpError(404, 'Nu există.');
  await getProject(owner, projectId);
  try { return await fs.readFile(path.join(ROOT(), projectId, job, file)); } catch { throw new HttpError(404, 'Nu există.'); }
}
