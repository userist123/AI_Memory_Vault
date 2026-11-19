// Calendarul anual al soarelui pe cameră și „clasa de lumină” a locuinței.
//
// Calendarul: în ziua de 21 a fiecărei luni, câte minute din fiecare oră intră soare direct prin ferestrele camerei
// (punctul de calcul din insolation.ts). Arată dintr-o privire anotimpurile camerei: dormitorul cu soare dimineața tot anul,
// livingul fără soare iarna după-amiaza.
//
// Clasa de lumină (A–E) e un indice Casa3D, nu o certificare: adună verificările pe care aplicația le face deja —
// art. 3 (1½ h la solstițiu), SR EN 17037 (expunerea la soare), FLN pe cer acoperit, vara (AD O și protecția solară)
// și efectul asupra vecinilor — într-o notă ușor de comunicat clientului sau într-un anunț imobiliar.
import type { Floor } from './types';
import { insolationStudy, en17037Level, type RoomSun } from './insolation';
import type { Obstacle } from './neighbours';
import type { SiteInfo } from './site-location';
import type { RoomDaylight } from './daylight';
import type { RoomSummer } from './overheating';
import type { NeighbourSun } from './neighbours';

export interface RoomCalendar { roomId: string; name: string; type: string; /** [lună 0–11][ora 0–23] = minute de soare */ grid: number[][]; /** ore pe zi, pe lună */ hours: number[] }
export function sunCalendar(fl: Floor, site: SiteInfo, toUtc: (d: string, m: number) => Date, opts: { year: number; obs?: readonly Obstacle[]; stepMin?: number }): RoomCalendar[] {
  const step = opts.stepMin ?? 10, out = new Map<string, RoomCalendar>();
  for (let m = 0; m < 12; m++){
    const date = `${opts.year}-${String(m + 1).padStart(2, '0')}-21`, st = insolationStudy(fl, site, toUtc, { date, stepMin: step, floorGrid: false, obs: opts.obs });
    for (const r of st.rooms){ if (!r.windows) continue;
      const c = out.get(r.roomId) ?? { roomId: r.roomId, name: r.name, type: r.type, grid: Array.from({ length: 12 }, () => new Array(24).fill(0)), hours: new Array(12).fill(0) };
      const ws = st.windows.filter(w => w.roomId === r.roomId);
      st.times.forEach((t, i) => { if (ws.some(w => w.litMask[i])) c.grid[m]![Number(t.slice(0, 2))]! += step; });
      c.hours[m] = r.hours; out.set(r.roomId, c); } }
  return [...out.values()];
}

export type LightClass = 'A' | 'B' | 'C' | 'D' | 'E';
/** `max` cuprinde doar verificările făcute (cele „neverificate” nu dau și nu iau puncte); `capped` = clasa e limitată la C
 *  pentru că norma legală (art. 3) nu e îndeplinită în toate camerele de locuit. */
export interface LightScore { /** null = fără camere de locuit pe nivel: clasa nu are sens (baie, garaj, pod) */ cls: LightClass | null; points: number; max: number; capped: boolean;
  parts: { key: 'art3' | 'en17037' | 'daylight' | 'summer' | 'neighbours'; got: number; max: number; known: boolean }[] }
/** Indicele de lumină al unui nivel sau al casei (camerele tuturor nivelurilor puse împreună). */
export function lightScore(i: { norm: RoomSun[]; en: RoomSun[]; daylight: RoomDaylight[]; summer: RoomSummer[]; nb: NeighbourSun[] | null }): LightScore {
  const hab = i.norm.filter(r => r.habitable), ok = hab.filter(r => r.verdict === 'ok').length;
  const art3 = !hab.length ? 0 : ok === hab.length ? 2 : ok * 2 >= hab.length ? 1 : 0;
  // EN 17037 cere nivelul în cel puțin o cameră de locuit: contează cea mai bună
  const en = Math.max(0, ...i.en.filter(r => r.habitable && r.windows).map(r => en17037Level(r.hours)));
  const dl = i.daylight.filter(r => r.target != null), dok = dl.filter(r => r.verdict === 'ok').length;
  const day = !dl.length ? 0 : dok === dl.length ? 2 : dok * 2 >= dl.length ? 1 : 0;
  const sum = i.summer.some(r => r.verdict === 'over' || r.unshaded.length) ? 0 : 1;
  const nbKnown = !!i.nb?.length, nb = nbKnown && !i.nb!.some(n => n.status === 'affected') ? 1 : 0;
  const parts: LightScore['parts'] = [{ key: 'art3', got: art3, max: 2, known: hab.length > 0 }, { key: 'en17037', got: en, max: 3, known: hab.length > 0 },
    { key: 'daylight', got: day, max: 2, known: dl.length > 0 }, { key: 'summer', got: sum, max: 1, known: i.summer.some(r => r.verdict !== 'na') }, { key: 'neighbours', got: nb, max: 1, known: nbKnown }];
  const done = parts.filter(p => p.known), points = done.reduce((a, p) => a + p.got, 0), max = done.reduce((a, p) => a + p.max, 0), k = max ? points / max : 0;
  let cls: LightClass | null = !hab.length ? null : k >= .85 ? 'A' : k >= .72 ? 'B' : k >= .55 ? 'C' : k >= .33 ? 'D' : 'E';
  // o casă care nu respectă norma legală de însorire nu poate ieși „foarte luminoasă”
  const capped = hab.length > 0 && art3 < 2 && (cls === 'A' || cls === 'B'); if (capped) cls = 'C';
  return { cls, points, max, capped, parts };
}
