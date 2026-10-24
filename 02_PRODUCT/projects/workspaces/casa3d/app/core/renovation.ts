// Starea pereților în proiectul de renovare (existent, de construit, de demolat): lucrările pe perete pentru deviz și
// avertismentele lor. Funcții pure, fără React și fără I/O. Lipsa câmpului `status` = perete existent (proiectele vechi
// rămân neschimbate: nicio linie nouă în deviz, niciun avertisment).
import type { Floor, Issue, Wall, WallStatus } from './types';
import { wallLength } from './geometry';
import { DOOR_HEIGHT, WINDOW_HEIGHT } from './rules.boq';

export const wallStatus = (w: Pick<Wall, 'status'>): WallStatus => w.status ?? 'existing';
export const isDemolished = (w: Pick<Wall, 'status'>) => w.status === 'demolish';
/** Golurile care mai contează: un perete de demolat dispare, deci și ușile și ferestrele lui (listă goală). */
export const activeOpenings = <W extends Pick<Wall, 'status' | 'openings'>>(w: W): W['openings'] => isDemolished(w) ? [] : w.openings;
/** Peretele nou până la 12,5 cm e considerat gips-carton pe structură metalică; mai gros = zidărie. */
export const DRYWALL_MAX_M = .125;

export interface WallWork { wallId: string; status: 'new' | 'demolish'; lengthM: number; heightM: number; thicknessM: number; exterior: boolean;
  /** suprafața netă (L × H fără goluri), m² */ areaM2: number; system: 'drywall' | 'masonry' }
const r2 = (v: number) => Math.round(v * 100) / 100;
/** Lucrările de pe un nivel: pereții noi și cei de demolat, cu suprafața netă (lungime × înălțimea nivelului, fără uși și ferestre). */
export function wallWorks(fl: Pick<Floor, 'walls' | 'ceilingHeight'>): WallWork[] {
  return fl.walls.flatMap(w => { if (w.status !== 'new' && w.status !== 'demolish') return [];
    const L = wallLength(w.a, w.b), H = fl.ceilingHeight; if (!(L > 0) || !(H > 0)) return [];
    const holes = w.openings.reduce((a, o) => a + Math.max(0, Math.min(o.width, L)) * Math.min(H, o.height ?? (o.kind === 'door' ? DOOR_HEIGHT : WINDOW_HEIGHT)), 0);
    return [{ wallId: w.id, status: w.status, lengthM: r2(L), heightM: r2(H), thicknessM: w.thickness, exterior: !!w.exterior, areaM2: r2(Math.max(0, L * H - holes)),
      system: w.thickness <= DRYWALL_MAX_M + 1e-9 ? 'drywall' as const : 'masonry' as const }]; });
}
/** Avertismentul de demolare: un perete exterior (de obicei structural) marcat de demolat cere verificarea unui inginer. */
export function renovationIssues(fl: Pick<Floor, 'walls'>): Issue[] {
  return fl.walls.filter(w => isDemolished(w) && w.exterior).map(w => ({ code: 'WALL_DEMOLISH' as const, severity: 'WARNING' as const,
    message: `Peretele exterior ${w.id} e marcat de demolat: verifică structura cu un inginer înainte de lucrări.`, key: 'issue.WALL_DEMOLISH', vars: { id: w.id } }));
}
