// Planul pentru motorul 3D, îmbogățit cu aspectul ales: culorile fețelor de perete, nuanța podelei, tavanul și ramele.
import type { Snapshot } from '../core/types';
import { floorToPlan } from '../features/migration/legacy';
import { wallLook, roomLook, openingColor, DEFAULT_LOOK } from '../core/appearance';

export function planWithLook(snap: Snapshot): any {
  const plan: any = floorToPlan(snap.floor, snap.name), walls = wallLook(snap);
  plan.pereti.forEach((p: any, i: number) => { const w = snap.floor.walls[i]!; p.fete = walls[i];
    p.goluri.forEach((g: any, j: number) => { const c = openingColor(snap, w.openings[j]!.id); if (c !== DEFAULT_LOOK.frame) g.culoare = c; }); });
  plan.camere.forEach((c: any) => { const l = roomLook(snap, c.id); if (l.floorTint !== DEFAULT_LOOK.floorTint) c.podea = l.floorTint; if (l.ceiling !== DEFAULT_LOOK.ceiling) c.tavan = l.ceiling; });
  return plan;
}
