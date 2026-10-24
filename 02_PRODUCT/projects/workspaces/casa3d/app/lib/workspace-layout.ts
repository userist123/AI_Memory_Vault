// Aspectul spațiului de lucru (A1-3): lățimile panourilor, outliner-ul și modul de aranjare, păstrate în localStorage.
export type LayoutMode = 'studio' | 'classic';
export interface WorkspaceState { v: 1; left: number; right: number; outliner: boolean }

export const WS_KEY = 'casa3d.workspace', MODE_KEY = 'casa3d.layout';
export const LIMITS = { left: { min: 200, max: 420, def: 260 }, right: { min: 320, max: 760, def: 380 }, canvasMin: 360, rail: 64 } as const;
export const DEFAULT_WS: WorkspaceState = { v: 1, left: LIMITS.left.def, right: LIMITS.right.def, outliner: true };

/** Lățimea întreagă, în limitele panoului; valorile nefinite revin la implicit. */
export function clampWidth(side: 'left' | 'right', px: number): number {
  const l = LIMITS[side];
  return Number.isFinite(px) ? Math.min(l.max, Math.max(l.min, Math.round(px))) : l.def;
}

/** Citește starea salvată; orice abatere (JSON stricat, altă versiune, tipuri greșite) dă valorile implicite. Nu aruncă. */
export function parseWorkspace(raw: string | null): WorkspaceState {
  if (raw === null) return { ...DEFAULT_WS };
  let o: unknown;
  try { o = JSON.parse(raw); } catch { return { ...DEFAULT_WS }; }
  if (typeof o !== 'object' || o === null || (o as { v?: unknown }).v !== 1) return { ...DEFAULT_WS };
  const r = o as Record<string, unknown>;
  const w = (side: 'left' | 'right') => clampWidth(side, typeof r[side] === 'number' ? r[side] as number : NaN);
  return { v: 1, left: w('left'), right: w('right'), outliner: typeof r.outliner === 'boolean' ? r.outliner : true };
}

export const serializeWorkspace = (s: WorkspaceState): string => JSON.stringify(s);

/** Încape panourile în fereastră: întâi se strânge cel din dreapta, apoi cel din stânga; peste minime nu se coboară. */
export function fitToViewport(s: Pick<WorkspaceState, 'left' | 'right'>, viewport: number, outlinerShown: boolean): { left: number; right: number } {
  const { left: L, right: R } = LIMITS, fixed = LIMITS.rail + LIMITS.canvasMin;
  let { left, right } = s;
  const over = () => fixed + (outlinerShown ? left : 0) + right - viewport;
  if (over() > 0) right = Math.max(R.min, right - over());
  if (over() > 0 && outlinerShown) left = Math.max(L.min, left - over());
  return { left, right };
}

export const layoutDefault = (flag: string | undefined): LayoutMode => flag?.trim().toLowerCase() === 'classic' ? 'classic' : 'studio';
export const resolveMode = (stored: string | null, flag: string | undefined): LayoutMode => stored === 'studio' || stored === 'classic' ? stored : layoutDefault(flag);

/** Tastatura pe separator: săgețile mută separatorul vizual cu 16 px (64 cu Shift); Home/End = minim/maxim. Altă tastă: null. */
export function stepWidth(side: 'left' | 'right', cur: number, key: string, shift: boolean): number | null {
  const step = shift ? 64 : 16, l = LIMITS[side];
  // separatorul panoului din dreapta e pe marginea lui stângă: mișcarea spre stânga îl lărgește
  const grow = side === 'left' ? 'ArrowRight' : 'ArrowLeft', shrink = side === 'left' ? 'ArrowLeft' : 'ArrowRight';
  if (key === 'Home') return l.min;
  if (key === 'End') return l.max;
  if (key === grow) return clampWidth(side, cur + step);
  if (key === shrink) return clampWidth(side, cur - step);
  return null;
}
