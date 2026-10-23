import type { Floor, Room, RoomRect } from './types';
export const r3 = (v: number) => Math.round(v * 1000) / 1000;          // rotunjire la mm: evită erorile de virgulă mobilă
export const area = (r: RoomRect) => (r.x1 - r.x0) * (r.z1 - r.z0);
export const rectHit = (A: RoomRect, B: RoomRect) => A.x0 < B.x1 - 1e-3 && A.x1 > B.x0 + 1e-3 && A.z0 < B.z1 - 1e-3 && A.z1 > B.z0 + 1e-3;
export const insideRect = (room: RoomRect, r: RoomRect) => r.x0 >= room.x0 - 1e-3 && r.x1 <= room.x1 + 1e-3 && r.z0 >= room.z0 - 1e-3 && r.z1 <= room.z1 + 1e-3;
export function footprintAt(x: number, z: number, rot: number, w: number, d: number){ const q = Math.round(rot / (Math.PI / 2)) % 2 !== 0; const hw = (q ? d : w) / 2, hd = (q ? w : d) / 2; return { x0: x - hw, x1: x + hw, z0: z - hd, z1: z + hd }; }
// direcția feței: rotația 0 privește spre +z (convenția modelelor 3D din prototip)
export function frontRect(fp: RoomRect, rot: number, depth: number): RoomRect | null {
  if (depth <= 0) return null; const k = ((Math.round(rot / (Math.PI / 2)) % 4) + 4) % 4;
  if (k === 0) return { x0: fp.x0, x1: fp.x1, z0: fp.z1, z1: fp.z1 + depth };
  if (k === 2) return { x0: fp.x0, x1: fp.x1, z0: fp.z0 - depth, z1: fp.z0 };
  if (k === 1) return { x0: fp.x1, x1: fp.x1 + depth, z0: fp.z0, z1: fp.z1 };
  return { x0: fp.x0 - depth, x1: fp.x0, z0: fp.z0, z1: fp.z1 };
}
export const wallLength = (a: [number, number], b: [number, number]) => Math.hypot(b[0] - a[0], b[1] - a[1]);
export const roomOfPoint = (floor: Floor, x: number, z: number): Room | undefined => floor.rooms.find(r => x >= r.rect.x0 && x <= r.rect.x1 && z >= r.rect.z0 && z <= r.rect.z1);
export function snapPoint(p: [number, number], floor: Floor, grid = .05, radius = .2): [number, number] {
  let best: [number, number] | null = null, bd = radius;
  for (const w of floor.walls) for (const e of [w.a, w.b]){ const d = Math.hypot(e[0] - p[0], e[1] - p[1]); if (d < bd){ bd = d; best = [e[0], e[1]]; } }
  return best || [r3(Math.round(p[0] / grid) * grid), r3(Math.round(p[1] / grid) * grid)];
}
