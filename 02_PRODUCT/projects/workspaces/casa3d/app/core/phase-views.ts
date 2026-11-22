// Planșele pe faze ale unei renovări: situația existentă, demolările și situația propusă, scoase din același plan după
// starea pereților (`Wall.status`: lipsă/`existing`, `new`, `demolish`). Funcții pure, fără React și fără I/O.
//  • existent  = pereții existenți + cei de demolat, desenați ca înainte de lucrări (fără pereții noi);
//  • demolări  = pereții păstrați desenați normal + cei de demolat marcați (fără pereții noi);
//  • propus    = pereții păstrați + cei noi marcați (fără cei demolați).
// Golurile (uși, ferestre) nu au stare proprie în model: urmează peretele lor. Nișele la fel.
// Camerele, zonele de pardoseală, adnotările și mobilierul descriu situația propusă, deci apar doar pe planșa „propus”.
import type { Floor, Snapshot, Wall, WallStatus } from './types';
import { floors, levelView } from './levels';
import { wallStatus } from './renovation';

export type Phase = 'existing' | 'demolition' | 'proposed';
export const PHASES: readonly Phase[] = ['existing', 'demolition', 'proposed'];

/** Nivelul are lucrări pe pereți (cel puțin un perete nou sau de demolat)? */
export const hasPhaseWork = (fl: Pick<Floor, 'walls'>): boolean => fl.walls.some(w => w.status === 'new' || w.status === 'demolish');
/** Condiția de apariție a planșelor pe faze: proiectul are cel puțin un perete nou sau de demolat, pe orice nivel. */
export const hasPhases = (snap: Pick<Snapshot, 'floor' | 'levels'>): boolean => floors(snap as Snapshot).some(hasPhaseWork);
/** Nivelurile care primesc planșe pe faze (0 = parter): doar cele cu lucrări, altfel cele trei planșe ar fi identice. */
export const phaseLevels = (snap: Pick<Snapshot, 'floor' | 'levels'>): number[] =>
  floors(snap as Snapshot).flatMap((f, i) => hasPhaseWork(f) ? [i] : []);
/** Planșele de tipărit, în ordine: pe fiecare nivel cu lucrări, existent → demolări → propus. Listă goală = nicio planșă. */
export const phaseSheets = (snap: Pick<Snapshot, 'floor' | 'levels'>): { level: number; phase: Phase }[] =>
  phaseLevels(snap).flatMap(level => PHASES.map(phase => ({ level, phase })));

/** Peretele apare pe planșa fazei? */
export function wallInPhase(w: Pick<Wall, 'status'>, phase: Phase): boolean {
  const s = wallStatus(w); return phase === 'proposed' ? s !== 'demolish' : s !== 'new';
}

/** Nivelul filtrat pe o fază. Pe „existent” pereții de demolat își pierd marcajul (se desenează ca orice perete existent);
 *  pe „demolări” și „propus” marcajul rămâne (`demolish`, respectiv `new`). Pe „existent” și „demolări” nu rămân camere,
 *  zone de pardoseală sau adnotări (țin de situația propusă); nișele rămân doar pe pereții păstrați pe planșă. */
export function phaseFloor(fl: Floor, phase: Phase): Floor {
  const walls = fl.walls.filter(w => wallInPhase(w, phase)).map(w => { if (phase !== 'existing' || w.status == null) return w;
    const { status: _s, ...rest } = w; return rest; });
  const ids = new Set(walls.map(w => w.id)), out: Floor = { ...fl, walls };
  if (fl.niches) out.niches = fl.niches.filter(n => ids.has(n.wallId));
  if (phase !== 'proposed'){ out.rooms = []; if (fl.zones) out.zones = []; if (fl.annotations) out.annotations = []; }
  return out;
}

export interface PhaseView { phase: Phase; level: number;
  /** vedere de un singur nivel (ca `levelView`), cu `floor` filtrat pe fază; mobilierul doar pe „propus” */
  snap: Snapshot;
  /** starea din proiect a fiecărui perete de pe planșă (pe id), pentru marcaje și legendă */
  marks: Record<string, WallStatus>;
  /** id-urile golurilor de pe planșă (golurile urmează peretele lor) */
  openingIds: string[];
  /** etichetele camerelor și mobilierul se desenează doar pe „propus” */
  showRooms: boolean; showFurniture: boolean }

/** Planșa unei faze pentru nivelul `level` (0 = parter). Aruncă `RangeError` pentru un nivel inexistent. */
export function phaseView(snap: Snapshot, phase: Phase, level = 0): PhaseView {
  const v = levelView(snap, level), src = v.floor, floor = phaseFloor(src, phase), proposed = phase === 'proposed';
  const marks: Record<string, WallStatus> = {}; for (const w of src.walls) if (wallInPhase(w, phase)) marks[w.id] = wallStatus(w);
  return { phase, level, snap: { ...v, floor, placements: proposed ? v.placements : [] }, marks,
    openingIds: floor.walls.flatMap(w => w.openings.map(o => o.id)), showRooms: proposed, showFurniture: proposed };
}
