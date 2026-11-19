// Iluminarea scenei 3D: momentul zilei și orientarea soarelui, ca parametri puri (motorul 3D doar îi aplică).
export type TimeOfDay = 'day' | 'evening' | 'night';
export interface LightingParams { background: string; hemi: number; sunColor: string; sun: number; interior: number; exposure: number; elevationDeg: number; azimuthDeg: number }
const PRESETS: Record<TimeOfDay, Omit<LightingParams, 'azimuthDeg'>> = {
  day: { background: '#d9d3c8', hemi: 0.3, sunColor: '#fff0dc', sun: 1.5, interior: 0.35, exposure: 0.66, elevationDeg: 50 },
  evening: { background: '#e7d3c0', hemi: 0.12, sunColor: '#ffb070', sun: 0.7, interior: 0.6, exposure: 0.85, elevationDeg: 12 },
  night: { background: '#1b2029', hemi: 0.04, sunColor: '#9fb3d9', sun: 0.08, interior: 0.9, exposure: 0.8, elevationDeg: 35 },
};
/** Parametrii pentru momentul zilei; azimutul (0 = nord, 90 = est) mută soarele în jurul casei. */
export function lighting(time: TimeOfDay, azimuthDeg = 135): LightingParams {
  const a = ((azimuthDeg % 360) + 360) % 360;
  return { ...PRESETS[time], azimuthDeg: a };
}
/** Lumina pentru o poziție reală a soarelui (studiul de însorire): sub orizont = noapte, aproape de orizont = seară,
 *  apoi ziua cu intensitatea crescând cu înălțimea. `azimuthDeg` e deja față de plan (core/site-location.ts planAzimuth). */
export function solarLighting(elevationDeg: number, azimuthDeg: number): LightingParams {
  const a = ((azimuthDeg % 360) + 360) % 360;
  if (elevationDeg <= 0) return { ...PRESETS.night, azimuthDeg: a };
  const k = Math.min(1, elevationDeg / 15), d = PRESETS.day, e = PRESETS.evening, mix = (x: number, y: number) => x + (y - x) * k;
  const hex = (x: string, y: string) => '#' + [1, 3, 5].map(i => Math.round(mix(parseInt(x.slice(i, i + 2), 16), parseInt(y.slice(i, i + 2), 16))).toString(16).padStart(2, '0')).join('');
  return { background: hex(e.background, d.background), hemi: mix(e.hemi, d.hemi), sunColor: hex(e.sunColor, d.sunColor), sun: mix(e.sun, d.sun) * (0.55 + 0.45 * Math.sin(Math.min(90, elevationDeg) * Math.PI / 180)),
    interior: mix(e.interior, d.interior), exposure: mix(e.exposure, d.exposure), elevationDeg: Math.max(1, elevationDeg), azimuthDeg: a };
}
/** Direcția spre soare în coordonatele scenei (x = est, y = sus, z = sud; planul are nordul spre -z). */
export function sunDirection(azimuthDeg: number, elevationDeg: number): [number, number, number] {
  const az = azimuthDeg * Math.PI / 180, el = elevationDeg * Math.PI / 180;
  return [Math.sin(az) * Math.cos(el), Math.sin(el), -Math.cos(az) * Math.cos(el)];
}
/** Nume de fișier sigur pentru captură, derivat din numele proiectului. */
export function captureFileName(projectName: string, when = new Date()): string {
  const base = projectName.normalize('NFD').replace(/[̀-ͯ]/g, '').replace(/[^A-Za-z0-9]+/g, '-').replace(/^-+|-+$/g, '').slice(0, 60) || 'proiect';
  return `${base}-3d-${when.toISOString().slice(0, 10)}.png`;
}
