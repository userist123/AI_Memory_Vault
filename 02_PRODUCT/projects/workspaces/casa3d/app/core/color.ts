// Utilitare de culoare pure (fără dependențe), folosite de Consilier. Formulele luminanței/contrastului sunt cele WCAG 2.x.
export interface Rgb { r: number; g: number; b: number }
export interface Hsl { h: number; s: number; l: number }

// Praguri empirice pentru „neutru”: reguli de bun-simț, nu norme.
export const NEUTRAL_MAX_SATURATION = 0.15; // sub această saturație culoarea se citește ca gri/alb/negru
export const NEUTRAL_MIN_LIGHTNESS = 0.9;   // foarte deschis: aproape alb, indiferent de nuanță
export const NEUTRAL_MAX_LIGHTNESS = 0.1;   // foarte închis: aproape negru, indiferent de nuanță

/** `#rgb` sau `#rrggbb` → canale 0..255; `null` dacă formatul nu e valid. */
export function hexToRgb(hex: string): Rgb | null {
  if (typeof hex !== 'string') return null;
  const m = /^#?([0-9a-f]{3}|[0-9a-f]{6})$/i.exec(hex.trim()); if (!m) return null;
  const h = m[1].length === 3 ? m[1].split('').map(c => c + c).join('') : m[1];
  return { r: parseInt(h.slice(0, 2), 16), g: parseInt(h.slice(2, 4), 16), b: parseInt(h.slice(4, 6), 16) };
}
/** Luminanța relativă WCAG, 0 (negru) … 1 (alb). */
export function relativeLuminance(hex: string): number {
  const c = hexToRgb(hex); if (!c) return 0;
  const lin = (v: number) => { const s = v / 255; return s <= 0.03928 ? s / 12.92 : Math.pow((s + 0.055) / 1.055, 2.4); };
  return 0.2126 * lin(c.r) + 0.7152 * lin(c.g) + 0.0722 * lin(c.b);
}
/** Raportul de contrast WCAG, 1 … 21 (simetric). */
export function contrastRatio(a: string, b: string): number {
  const la = relativeLuminance(a), lb = relativeLuminance(b), hi = Math.max(la, lb), lo = Math.min(la, lb);
  return (hi + 0.05) / (lo + 0.05);
}
/** RGB (0..255) → h în grade [0,360), s și l în [0,1]. */
export function rgbToHsl(c: Rgb): Hsl {
  const r = c.r / 255, g = c.g / 255, b = c.b / 255, max = Math.max(r, g, b), min = Math.min(r, g, b), l = (max + min) / 2, d = max - min;
  if (d === 0) return { h: 0, s: 0, l };
  const s = d / (1 - Math.abs(2 * l - 1));
  let h = max === r ? ((g - b) / d) % 6 : max === g ? (b - r) / d + 2 : (r - g) / d + 4;
  h *= 60; if (h < 0) h += 360;
  return { h, s, l };
}
/** Distanța circulară între două nuanțe, 0..180 grade. */
export function hueDistance(h1: number, h2: number): number {
  const d = Math.abs((((h1 - h2) % 360) + 360) % 360); return d > 180 ? 360 - d : d;
}
/** Neutră = aproape gri, aproape albă sau aproape neagră. Culorile invalide sunt tratate ca neutre. */
export function isNeutral(hex: string): boolean {
  const rgb = hexToRgb(hex); if (!rgb) return true;
  const { s, l } = rgbToHsl(rgb);
  return s < NEUTRAL_MAX_SATURATION || l > NEUTRAL_MIN_LIGHTNESS || l < NEUTRAL_MAX_LIGHTNESS;
}
