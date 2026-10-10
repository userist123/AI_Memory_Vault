// Adaptor între modelul aplicației (Snapshot: camere dreptunghiulare în x/z, piese cu centru și rotație în radiani,
// dimensiuni în cm) și Digital Twin v1.0 din @casa3d/twin-core (poligoane rectilinii, colț minim, rotație 0/90/180/270,
// metri). Twin-ul este sursa de adevăr geometrică pentru design; Snapshot-ul rămâne formatul persistat al aplicației.
import { emptyTwin, linkWalls, normalize, fingerprint, type Twin, type Room, type Wall, type Opening, type Placement, type Rotation, type CatalogItem } from '@casa3d/twin-core';
import type { Catalog, Snapshot, FurniturePlacement } from '../core/types';
import { resolve, groupOf } from '../core/catalog';
import { r3 } from '../core/geometry';
import { ALLOWED_OVERLAP } from '../core/rules';

// Aceleași valori ca doorZones() din core/validate.ts (0.95 m în fața ușii), ca aplicarea să treacă și validatorul aplicației.
export const DOOR_CLEAR_DEPTH = 0.95;
export const WINDOW_SILL = 0.9;

export const toRotation = (rad: number): Rotation => ((((Math.round(rad / (Math.PI / 2)) % 4) + 4) % 4) * 90) as Rotation;
export const toRadians = (rot: Rotation): number => r3((rot / 180) * Math.PI);

/** Camerele, pereții, golurile și piesele proiectului, ca Digital Twin. Piesele fără dimensiuni în catalog sunt omise. */
export function snapshotToTwin(snap: Snapshot, cat: Catalog, twinId = 'project'): Twin {
  const t = emptyTwin(twinId);
  const h = snap.floor.ceilingHeight || 2.6;
  t.rooms = snap.floor.rooms.map((r): Room => ({ id: r.id, name: r.name, height: h,
    polygon: [{ x: r.rect.x0, y: r.rect.z0 }, { x: r.rect.x1, y: r.rect.z0 }, { x: r.rect.x1, y: r.rect.z1 }, { x: r.rect.x0, y: r.rect.z1 }] }));
  t.walls = snap.floor.walls.map((w): Wall => ({ id: w.id, a: { x: w.a[0], y: w.a[1] }, b: { x: w.b[0], y: w.b[1] }, thickness: w.thickness, roomIds: [] }));
  t.openings = snap.floor.walls.flatMap(w => w.openings.map((o): Opening => ({ id: o.id, kind: o.kind, wallId: w.id, offset: o.offset, width: o.width,
    clearDepth: o.kind === 'door' ? DOOR_CLEAR_DEPTH : 0, sillHeight: o.kind === 'window' ? WINDOW_SILL : 0 })));
  t.placements = snap.placements.flatMap((p): Placement[] => {
    const rv = resolve(cat, p.variantId); if (!rv || !rv.w || !rv.d) return [];
    // piesa pe comandă intră în twin cu dimensiunile ei reale, ca suprapunerile și spațiile să fie verificate corect
    const w = p.size ? p.size.w / 100 : rv.w, d = p.size ? p.size.d / 100 : rv.d, h = p.size ? p.size.h / 100 : rv.h;
    const rotation = toRotation(p.rotation), swap = rotation === 90 || rotation === 270;
    const fw = swap ? d : w, fd = swap ? w : d;
    return [{ id: p.id, catalogId: p.variantId, roomId: p.roomId, x: r3(p.x - fw / 2), y: r3(p.z - fd / 2), w, d, h, rotation, role: p.group }];
  });
  t.policy = { allowedOverlaps: ALLOWED_OVERLAP.map(([a, b]) => [a, b] as [string, string]) };
  return normalize(linkWalls(t));
}

export const twinFingerprint = (snap: Snapshot, cat: Catalog): string => fingerprint(snapshotToTwin(snap, cat));

/** Piesele twin-ului, înapoi în formatul aplicației. Piesele existente își păstrează id-ul și sursa; cele noi sunt 'auto'. */
/** `ids` (opțional) primește corespondența id solver → id aplicație, ca BOQ-ul și problemele să poată fi raportate pe id-urile finale. */
export function placementsFromTwin(snap: Snapshot, cat: Catalog, twin: Twin, ids?: Map<string, string>): FurniturePlacement[] {
  const existing = new Map(snap.placements.map(p => [p.id, p]));
  return twin.placements.flatMap((q): FurniturePlacement[] => {
    const rv = resolve(cat, q.catalogId); if (!rv) return [];
    // dimensiunile din twin (egale cu ale variantei, sau cele pe comandă pentru piesele existente cu `size`)
    const swap = q.rotation === 90 || q.rotation === 270, qw = q.w || rv.w, qd = q.d || rv.d;
    const fw = swap ? qd : qw, fd = swap ? qw : qd;
    const prev = existing.get(q.id);
    // Piesele noi primesc un UUID, ca la orice piesă a aplicației: id-urile generate de solver nu ajung în proiect.
    const id = prev ? q.id : globalThis.crypto.randomUUID(); ids?.set(q.id, id);
    return [{ id, roomId: q.roomId, group: groupOf(q.catalogId), variantId: q.catalogId, x: r3(q.x + fw / 2), z: r3(q.y + fd / 2), rotation: toRadians(q.rotation), source: prev?.source ?? 'auto', ...(prev?.size ? { size: prev.size } : {}) }];
  });
}

/** Piesele pe care twin-ul nu le poate reprezenta (fără dimensiuni în catalog). Se păstrează neatinse la aplicare. */
export function untwinnablePlacements(snap: Snapshot, cat: Catalog): FurniturePlacement[] {
  return snap.placements.filter(p => { const rv = resolve(cat, p.variantId); return !rv || !rv.w || !rv.d; });
}

/** Grupele catalogului → rolurile pe care le știe motorul de reguli (lipit de perete, lângă pat etc.). */
export const ROLE_ALIASES = { pat: 'bed', noptiera: 'nightstand', dulap: 'wardrobe', canapea: 'sofa', birou: 'desk', scaunBirou: 'chair', masuta: 'table', biblioteca: 'bookcase', comodaTv: 'dresser' } as const;

/** Catalogul aplicației ca lista de produse a solver-ului: doar variante cu dimensiuni; prețul lipsă rămâne UNKNOWN. */
export function twinCatalogItems(cat: Catalog): CatalogItem[] {
  return cat.variants.flatMap((v): CatalogItem[] => {
    const rv = resolve(cat, v.id); if (!rv || !rv.w || !rv.d || !rv.h) return [];
    const item: CatalogItem = { id: v.id, name: v.name, w: rv.w, d: rv.d, h: rv.h, role: groupOf(v.id),
      price: rv.offer && rv.offer.currency === 'RON' ? { amount: rv.offer.price, currency: 'RON' } : 'UNKNOWN' };
    if (rv.offer){ item.retailer = rv.offer.provenance.source;
      item.provenance = { source: rv.offer.provenance.source, ...(rv.offer.provenance.sourceUrl ? { sourceUrl: rv.offer.provenance.sourceUrl } : {}), ...(rv.offer.provenance.verifiedAt ? { verifiedAt: rv.offer.provenance.verifiedAt } : {}),
        verificationType: 'manual', confidence: rv.offer.provenance.confidence === 'HIGH' ? 'high' : rv.offer.provenance.confidence === 'LOW' ? 'low' : 'medium' }; }
    return [item];
  });
}
