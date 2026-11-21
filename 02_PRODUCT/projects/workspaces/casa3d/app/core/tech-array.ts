// Repetarea în rând a punctelor tehnice (spoturi, prize, întrerupătoare…): copii la pas egal, ca la mobilier
// (core/arrange.ts arrayGroup). Spoturile de tavan se pot pune oriunde în cameră; punctele de perete (prize, apă,
// întrerupătoare) trebuie să rămână pe un perete al camerei lor — o copie care ar ajunge în mijlocul camerei e refuzată.
// Caloriferele și unitățile de aer condiționat nu se repetă (au corp, plafon de bucăți și reguli proprii). Totul sau nimic.
import type { Snapshot } from './types';
import type { TechPoint } from './technical';
import { MAX_TECH_POINTS, nearestWallPoint, insideOpening } from './technical';
import { ARRAY_LIMITS } from './arrange';

export type TechArrayResult = { ok: true; tech: TechPoint[]; ids: string[] } | { ok: false; key: 'edit.noPiece' | 'edit.arrayBad' | 'tech.arrayKind' | 'tech.arrayOutside' | 'tech.arrayOffWall' | 'tech.arrayTooMany' | 'tech.arrayInOpening'; id?: string };
const WALL_TOL = .05;   // cât de departe de perete poate sta un punct de perete (ca toleranța din sanitizeTech)
const NO_ARRAY = new Set(['radiator', 'hvac_split']);
const r3 = (n: number) => Math.round(n * 1000) / 1000;

/** `houseCount`: câte puncte are toată casa (toate nivelurile) — plafonul serverului e pe casă, nu pe nivelul afișat. */
export function arrayTech(snap: Pick<Snapshot, 'floor' | 'tech'>, ids: readonly string[], count: number, stepXCm: number, stepZCm: number, houseCount?: number): TechArrayResult {
  const tech = snap.tech ?? [], src = ids.flatMap(id => tech.filter(p => p.id === id)); if (!src.length) return { ok: false, key: 'edit.noPiece' };
  const okNum = (v: number) => Number.isFinite(v) && Math.abs(v) <= ARRAY_LIMITS.stepMaxCm;
  if (!Number.isInteger(count) || count < ARRAY_LIMITS.countMin || count > ARRAY_LIMITS.countMax || !okNum(stepXCm) || !okNum(stepZCm) || (stepXCm === 0 && stepZCm === 0)) return { ok: false, key: 'edit.arrayBad' };
  const bad = src.find(p => NO_ARRAY.has(p.kind)); if (bad) return { ok: false, key: 'tech.arrayKind', id: bad.id };
  if (Math.max(houseCount ?? 0, tech.length) + src.length * count > MAX_TECH_POINTS) return { ok: false, key: 'tech.arrayTooMany' };
  const used = new Set(tech.map(p => p.id)), out = [...tech], copies: string[] = [];
  const inRoom = (x: number, z: number) => snap.floor.rooms.find(r => x >= r.rect.x0 - 1e-9 && x <= r.rect.x1 + 1e-9 && z >= r.rect.z0 - 1e-9 && z <= r.rect.z1 + 1e-9);
  for (let k = 1; k <= count; k++) for (const p of src){
    const x = r3(p.x + k * stepXCm / 100), z = r3(p.z + k * stepZCm / 100), room = p.kind === 'light_point' ? inRoom(x, z) : snap.floor.rooms.find(r => r.id === p.roomId);
    if (!room || x < room.rect.x0 - 1e-9 || x > room.rect.x1 + 1e-9 || z < room.rect.z0 - 1e-9 || z > room.rect.z1 + 1e-9) return { ok: false, key: 'tech.arrayOutside', id: p.id };
    if (p.kind !== 'light_point'){ const w = nearestWallPoint(room, x, z); if (Math.hypot(w.x - x, w.z - z) > WALL_TOL) return { ok: false, key: 'tech.arrayOffWall', id: p.id };
      if (insideOpening(snap.floor, x, z)) return { ok: false, key: 'tech.arrayInOpening', id: p.id }; }
    let i = 1; while (used.has(`${p.kind}:${room.id}:m${i}`)) i++; const id = `${p.kind}:${room.id}:m${i}`; used.add(id); copies.push(id);
    out.push({ ...structuredClone(p), id, roomId: room.id, x, z, reason: 'manual' });
  }
  return { ok: true, tech: out, ids: copies };
}
