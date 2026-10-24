// Gesturi cu două degete pe planul 2D (telefon, tabletă): ciupire = zoom în jurul punctului dintre degete, mutarea
// ambelor degete = deplasare. Pur, ca să fie testat: primește viewBox-ul și matricea ecranului de la începutul gestului.
export interface ViewBox { x: number; y: number; w: number; h: number }
export interface ScreenCtm { a: number; d: number; e: number; f: number }   // ecran = lume * a + e (x), lume * d + f (y)
export interface Pt { x: number; y: number }
export const ZOOM_LIMITS = { minW: 1.5, maxW: 80 };

/** Noul viewBox: punctul din plan aflat între degete la început rămâne sub degete, la scara dată de distanța dintre ele. */
export function pinchViewBox(vb0: ViewBox, ctm0: ScreenCtm, mid0: Pt, dist0: number, mid1: Pt, dist1: number): ViewBox {
  const wx = (mid0.x - ctm0.e) / ctm0.a, wy = (mid0.y - ctm0.f) / ctm0.d;
  const w = Math.min(ZOOM_LIMITS.maxW, Math.max(ZOOM_LIMITS.minW, vb0.w * (dist1 > 0 ? dist0 / dist1 : 1))), r = w / vb0.w;
  const a1 = ctm0.a / r, d1 = ctm0.d / r;
  return { x: (ctm0.e + vb0.x * ctm0.a - mid1.x) / a1 + wx, y: (ctm0.f + vb0.y * ctm0.d - mid1.y) / d1 + wy, w, h: vb0.h * r };
}
export const midpoint = (a: Pt, b: Pt): Pt => ({ x: (a.x + b.x) / 2, y: (a.y + b.y) / 2 });
export const distance = (a: Pt, b: Pt) => Math.hypot(a.x - b.x, a.y - b.y);
