// Iluminarea scenei 3D: momentul zilei și orientarea soarelui, ca parametri puri (motorul 3D doar îi aplică).
export type TimeOfDay = 'day' | 'evening' | 'night';
export interface LightingParams { background: string; hemi: number; sunColor: string; sun: number; interior: number; exposure: number; elevationDeg: number; azimuthDeg: number }
const PRESETS: Record<TimeOfDay, Omit<LightingParams, 'azimuthDeg'>> = {
  day: { background: '#e9ebe7', hemi: 0.22, sunColor: '#fff3e2', sun: 1.1, interior: 0.35, exposure: 0.78, elevationDeg: 50 },
  evening: { background: '#e7d3c0', hemi: 0.12, sunColor: '#ffb070', sun: 0.7, interior: 0.6, exposure: 0.85, elevationDeg: 12 },
  night: { background: '#1b2029', hemi: 0.04, sunColor: '#9fb3d9', sun: 0.08, interior: 0.9, exposure: 1.0, elevationDeg: 35 },
};
/** Parametrii pentru momentul zilei; azimutul (0 = nord, 90 = est) mută soarele în jurul casei. */
export function lighting(time: TimeOfDay, azimuthDeg = 135): LightingParams {
  const a = ((azimuthDeg % 360) + 360) % 360;
  return { ...PRESETS[time], azimuthDeg: a };
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
