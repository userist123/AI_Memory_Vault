// Nivelurile de calitate ale viewport-ului 3D (plan A1, rândul 5). Clasarea e locală: nicio cerere către terți, nicio bază de
// date de GPU-uri. Adaptarea merge doar în jos într-o sesiune, deci nu poate oscila între niveluri.

export type Tier = 'phone' | 'laptop' | 'desktop';
export type TierPick = Tier | 'auto';
/** `ao`: ocluzia ambientală mereu, doar în repaus (imaginea finală după mișcare) sau deloc. */
export type TierParams = { maxDpr: number; composerDpr: number; ao: 'always' | 'rest' | 'off'; shadow: number; maxLights: number };
export const TIER_PARAMS: Record<Tier, TierParams> = {
  desktop: { maxDpr: 2, composerDpr: 1.5, ao: 'always', shadow: 2048, maxLights: 4 },
  laptop: { maxDpr: 1.5, composerDpr: 1.5, ao: 'rest', shadow: 2048, maxLights: 4 },
  phone: { maxDpr: 1.5, composerDpr: 1.5, ao: 'off', shadow: 1024, maxLights: 2 },
};
/** Numărul de cadre din bucla continuă după care se decide și pragul medianei lor: 40 ms lasă în pace un ecran de 30 Hz (33,3 ms). */
export const ADAPT_SAMPLES = 60, SLOW_MEDIAN_MS = 40;

export type TierSignals = { coarse: boolean; shortSide: number; cores?: number; memoryGb?: number; softwareGpu: boolean };
export function classifyTier(s: TierSignals): Tier {
  if (s.softwareGpu) return 'phone';
  if (s.coarse) return s.shortSide < 600 ? 'phone' : 'laptop';
  // `deviceMemory` lipsește în unele browsere: lipsa lui nu coboară nivelul (`hardwareConcurrency` e disponibil peste tot)
  return (s.cores ?? 0) >= 8 && (s.memoryGb === undefined || s.memoryGb >= 8) ? 'desktop' : 'laptop';
}
export const lowerTier = (t: Tier): Tier | null => (t === 'desktop' ? 'laptop' : t === 'laptop' ? 'phone' : null);
export function median(a: number[]){ if (!a.length) return 0; const s = [...a].sort((x, y) => x - y); return s[Math.floor(s.length / 2)]; }
/** Nivelul la care trebuie coborât, sau null. Decide mediana, nu p95: compilarea shaderelor și încărcările dau câteva cadre lungi și pe un GPU puternic (măsurat). */
export const adaptTier = (ms: number[], t: Tier): Tier | null => (ms.length >= ADAPT_SAMPLES && median(ms) > SLOW_MEDIAN_MS ? lowerTier(t) : null);
const PICKS: readonly unknown[] = ['auto', 'phone', 'laptop', 'desktop'];
export const parseTierPick = (raw: unknown): TierPick => (PICKS.includes(raw) ? raw as TierPick : 'auto');
export const TIER_KEY = 'casa3d.viewerTier';
