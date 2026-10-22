// Pregătirea imaginilor linkului pentru client (UI/UX P1-3), în browserul designerului: un motor 3D în afara ecranului
// randează vederea de ansamblu și fiecare cameră a reviziei, la mărimea planșei tipărite (aceeași formă ca pe ecran), iar
// imaginea se trimite ca WebP (JPEG unde browserul nu codează WebP) la 1600 și 800 px. Doar ce lipsește; câte una pe rând.
import type { Catalog, MaterialsCatalog, Snapshot } from '@/core/types';
import { lighting, sunDirection } from '@/core/lighting';
import { viewerInput } from '@/lib/viewer-input';
import { floors, levelView, stairwells } from '@/core/levels';
import { FULL_W, renderSize, presentQuality, isMobileEnv, yieldToBrowser } from '@/lib/present-render';

const WIDTHS = [800, 1600] as const;
const nextFrame = () => new Promise<void>(res => requestAnimationFrame(() => res()));
type State = { revisionNumber: number; snapshot: Snapshot | null; keys: string[]; have: Record<string, number[]> };

/** Ce lipsește: cheile care nu au ambele lățimi. Funcție pură, testată. */
export const missingRenders = (keys: readonly string[], have: Record<string, number[]>) => keys.filter(k => !WIDTHS.every(w => have[k]?.includes(w)));

/** Imaginea PNG din motor, redimensionată la `w` și codată compact. */
async function encode(png: string, w: number): Promise<string> {
  const img = new Image(); img.src = png; await img.decode();
  const h = Math.round(img.naturalHeight * w / img.naturalWidth), c = document.createElement('canvas'); c.width = w; c.height = h;
  const g = c.getContext('2d')!; g.imageSmoothingQuality = 'high'; g.drawImage(img, 0, 0, w, h);
  const webp = c.toDataURL('image/webp', .82); return webp.startsWith('data:image/webp') ? webp : c.toDataURL('image/jpeg', .85);
}

const running = new Set<string>();
/**
 * Randează și trimite imaginile care lipsesc pentru revizia `rev`. Întoarce câte imagini s-au trimis (0 = erau deja toate).
 * Un singur lanț pe revizie; fără WebGL sau la o eroare de rețea se oprește liniștit (linkul randează atunci singur).
 */
export async function prepareShareImages(projectId: string, rev: number, onProgress?: (done: number, total: number) => void): Promise<number> {
  const tag = `${projectId}:${rev}`; if (running.has(tag)) return 0; running.add(tag);
  const canvas = document.createElement('canvas'); let viewer: any = null, sent = 0;
  try {
    const st: State = await fetch(`/api/projects/${projectId}/revisions/${rev}/renders`).then(r => r.ok ? r.json() : Promise.reject(r));
    const todo = missingRenders(st.keys, st.have), snap = st.snapshot; if (!todo.length || !snap) return 0;
    const [catalog, mc]: [Catalog, MaterialsCatalog] = await Promise.all([fetch('/api/catalog').then(r => r.json()), fetch('/api/materials').then(r => r.json())]);
    Object.assign(canvas.style, { position: 'fixed', left: '-10000px', top: '0', width: '1600px', height: '1000px', pointerEvents: 'none' }); canvas.width = 1600; canvas.height = 1000;
    canvas.setAttribute('aria-hidden', 'true'); document.body.appendChild(canvas);
    const m = await import('@/components/viewer3d-engine.js'); viewer = m.createViewer(canvas); const lp = lighting('day');
    viewer.setLighting({ ...lp, dir: sunDirection(lp.azimuthDeg, lp.elevationDeg) }); viewer.setQuality?.(presentQuality({ share: false, mobile: isMobileEnv(window) }));
    const levelOf: Record<string, number> = { overview: 0 };
    for (let i = 0; i < floors(snap).length; i++) for (const r of levelView(snap, i).floor.rooms) levelOf[r.id] ??= i;
    let level = -1;
    for (const key of todo.sort((a, b) => (levelOf[a] ?? 0) - (levelOf[b] ?? 0))){
      const L = levelOf[key] ?? 0;
      if (L !== level){ const lv = levelView(snap, L), { plan, items } = viewerInput(lv, catalog, undefined, { voids: stairwells(snap, L), mc });
        viewer.setState(plan, items); level = L; await nextFrame(); await nextFrame(); }
      const size = renderSize({ cssWidth: FULL_W, dpr: 1, print: true, overview: key === 'overview' });
      const png: string | null = key === 'overview' ? viewer.renderView(undefined, size.w, size.h) : viewer.renderView({ roomId: key }, size.w, size.h);
      if (png) for (const w of WIDTHS){ if (st.have[key]?.includes(w)) continue;
        const r = await fetch(`/api/projects/${projectId}/revisions/${rev}/renders`, { method: 'PUT', headers: { 'content-type': 'application/json' }, body: JSON.stringify({ key, width: w, image: await encode(png, w) }) });
        if (!r.ok) return sent; sent++; }
      onProgress?.(todo.indexOf(key) + 1, todo.length); await yieldToBrowser();
    }
    return sent;
  } catch { return sent; }
  // contextul WebGL se eliberează explicit: browserul ține doar câteva deodată, iar editorul are deja unul pentru vederea 3D
  finally { running.delete(tag); try { viewer?.dispose(); } catch { /* deja eliberat */ }
    if (viewer) try { ((canvas.getContext('webgl2') || canvas.getContext('webgl')) as WebGLRenderingContext | null)?.getExtension('WEBGL_lose_context')?.loseContext(); } catch { /* fără extensie */ }
    canvas.remove(); }
}
