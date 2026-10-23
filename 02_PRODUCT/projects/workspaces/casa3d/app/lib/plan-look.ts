// Planul pentru motorul 3D, îmbogățit cu aspectul ales: culorile fețelor de perete, nuanța podelei, tavanul și ramele.
import type { Snapshot } from '../core/types';
import { floorToPlan } from '../features/migration/legacy';
import { roomLook, openingColor, normalizeHex, DEFAULT_LOOK } from '../core/appearance';

export function planWithLook(snap: Snapshot): any {
  // motorul colorează fiecare bucată de perete după camera din dreptul ei: culoarea camerei sau accentul feței
  const plan: any = floorToPlan(snap.floor, snap.name), faces = snap.appearance?.wallFaces || {};
  plan.pereti.forEach((p: any, i: number) => { const w = snap.floor.walls[i]!; const acc: Record<string, string> = {};
    for (const [k, f] of Object.entries(faces)){ const [wid, rid] = k.split('@'); const c = normalizeHex(f.color); if (wid === w.id && rid && c) acc[rid] = c; }
    if (Object.keys(acc).length) p.accente = acc;
    p.goluri.forEach((g: any, j: number) => { const c = openingColor(snap, w.openings[j]!.id); if (c !== DEFAULT_LOOK.frame) g.culoare = c; }); });
  plan.camere.forEach((c: any) => { const l = roomLook(snap, c.id); if (l.walls !== DEFAULT_LOOK.wall) c.pereti = l.walls; if (l.floorTint !== DEFAULT_LOOK.floorTint) c.podea = l.floorTint; if (l.ceiling !== DEFAULT_LOOK.ceiling) c.tavan = l.ceiling; });
  return plan;
}
