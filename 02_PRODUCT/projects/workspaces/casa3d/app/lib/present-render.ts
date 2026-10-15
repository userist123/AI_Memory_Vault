// Cât de mari și cât de scumpe sunt imaginile 3D ale prezentării. Pe linkul pentru client (adesea un telefon) imaginile
// se fac la rezoluția ecranului, fără ambient occlusion, câte una pe rând și doar pentru planșele văzute; tipărirea
// („Salvează ca PDF”) primește mereu rezoluția completă, ca înainte.
export const FULL_W = 1600;
export const ROOM_ASPECT = 1000 / 1600, OVERVIEW_ASPECT = 900 / 1600;
/** Densitatea pe care o folosește motorul 3D (viewer3d-engine.js: setPixelRatio(min(devicePixelRatio, 2))). */
export const enginePixelRatio = (dpr: number) => Math.min(Number.isFinite(dpr) && dpr > 0 ? dpr : 1, 2);

/** Lățimea logică cerută lui renderView. Motorul o înmulțește cu densitatea, deci imaginea are
 *  min(dpr, 2) × lățimea CSS pixeli, cel mult 1600. La tipărire: 1600 logic, exact ca înainte. */
export function renderWidth(o: { cssWidth: number; dpr: number; print?: boolean }){
  if (o.print) return FULL_W;
  const pr = enginePixelRatio(o.dpr), css = Number.isFinite(o.cssWidth) && o.cssWidth > 0 ? o.cssWidth : 800;
  const px = Math.min(FULL_W, Math.round(pr * css));
  return Math.max(160, Math.round(px / pr));
}
export const renderHeight = (w: number, overview = false) => Math.round(w * (overview ? OVERVIEW_ASPECT : ROOM_ASPECT));

/** Ambient occlusion (calitatea „high”) doar pentru designer, pe un calculator; pe link sau pe telefon, calitatea normală. */
export const presentQuality = (o: { share: boolean; mobile: boolean }): 'high' | 'normal' => o.share || o.mobile ? 'normal' : 'high';

type Win = { matchMedia?: (q: string) => { matches: boolean }; innerWidth?: number };
/** Telefon sau tabletă: pointer grosier (deget) sau ecran îngust. */
export function isMobileEnv(w: Win | undefined){
  if (!w) return false;
  try { if (w.matchMedia?.('(pointer: coarse)').matches) return true; } catch { /* fără matchMedia */ }
  return typeof w.innerWidth === 'number' && w.innerWidth > 0 && w.innerWidth <= 700;
}

/** Cedează firul principal între două randări (requestIdleCallback dacă există, altfel setTimeout). */
export function yieldToBrowser(w: { requestIdleCallback?: (cb: () => void, o?: { timeout: number }) => unknown; setTimeout: (cb: () => void, ms: number) => unknown } = globalThis as any): Promise<void> {
  return new Promise(res => { if (typeof w.requestIdleCallback === 'function') w.requestIdleCallback(() => res(), { timeout: 300 }); else w.setTimeout(() => res(), 30); });
}

/** O imagine e destul de bună dacă are cel puțin lățimea cerută (pentru tipărire: FULL_W). */
export const goodEnough = (have: number | undefined, need: number) => typeof have === 'number' && have >= need;
