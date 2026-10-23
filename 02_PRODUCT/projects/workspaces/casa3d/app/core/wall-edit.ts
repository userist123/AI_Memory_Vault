// Lungimea unui perete (PLAN_A1_2, felia 1): o singură funcție pentru inspector și pentru cota editată în plan.
// Capătul `a` rămâne fix, `b` se mută pe direcția peretelui; capetele altor pereți lipite de vechiul `b` îl urmează,
// deci colțul rămâne închis. Pur: întoarce un etaj nou, nu-l modifică pe cel primit.
import type { Floor } from './types';
import { r3, wallLength } from './geometry';

const MIN_M = .2, MAX_M = 1000, JOIN = 1e-3;
export class WallEditError extends Error { constructor(readonly key: string){ super(key); } }

export function setWallLength(floor: Floor, wallId: string, lengthM: number): Floor {
  const w = floor.walls.find(x => x.id === wallId); if (!w) throw new WallEditError('wallEdit.missing');
  if (!(lengthM >= MIN_M - 1e-9)) throw new WallEditError('wallEdit.tooShort');
  if (lengthM > MAX_M) throw new WallEditError('wallEdit.tooLong');
  if (w.openings.some(o => o.offset + o.width > lengthM + 1e-9)) throw new WallEditError('wallEdit.openingOut');
  const L = wallLength(w.a, w.b), ux = (w.b[0] - w.a[0]) / L, uz = (w.b[1] - w.a[1]) / L;
  const old = w.b, nb: [number, number] = [r3(w.a[0] + ux * lengthM), r3(w.a[1] + uz * lengthM)];
  const at = (p: [number, number]) => Math.hypot(p[0] - old[0], p[1] - old[1]) < JOIN;
  return { ...floor, walls: floor.walls.map(x => x.id === wallId ? { ...x, b: nb }
    : at(x.a) || at(x.b) ? { ...x, a: at(x.a) ? nb : x.a, b: at(x.b) ? nb : x.b } : x) };
}
