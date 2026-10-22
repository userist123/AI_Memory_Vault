// Verificări de meserie pentru bucătărie și baie, cu valori din surse citite (vezi research_rules.md, 2026-10-11):
// NKBA Kitchen & Bathroom Planning Guidelines (bune practici, nu lege în România), I5-2022 (ventilare, normativ RO),
// I7 (volumele din baie, din surse secundare: rămâne avertisment de verificat cu electricianul).
import type { Catalog, FurniturePlacement, Room, RoomRect, Snapshot } from './types';
import type { Advice } from './advisor';

export const NKBA = {
  triangleLegMin: 1.2, triangleLegMax: 2.7, triangleSumMax: 7.9,     // G3: fiecare latură 1,2–2,7 m, suma ≤ 7,9 m
  fixtureCentreMin: 0.381, fixtureCentreRec: 0.457,                   // B20 (WC), B5 (lavoar: minim 381 mm, recomandat 508)
  basinCentreRec: 0.508, showerMin: 0.762, showerRec: 0.914,          // B9: interior duș ≥ 762 mm, recomandat 914
};
export const BATH_ZONE2_M = 0.6;                                      // I7 / HD 60364-7-701: volumul 2 = 0,60 m în jurul dușului/căzii
/** I5-2022 tabelul 6.1.1.1: debit de evacuare (m³/h) după numărul de camere principale. */
export function i5ExtractFlow(mainRooms: number): { kitchen: number; bath: number } {
  const n = Math.max(1, Math.min(5, mainRooms)); return { kitchen: [75, 90, 105, 120, 135][n - 1]!, bath: n >= 3 ? 30 : 15 };
}
const MAIN_ROOM_TYPES = new Set(['living', 'dormitor', 'birou']);
type Out = { rule: string; a: Omit<Advice, 'id'> };
const r2 = (v: number) => Math.round(v * 100) / 100;

/** Distanța de la axa piesei la primul obstacol lateral (perete sau altă piesă), pe direcția lățimii ei. */
function lateralClearance(room: Room, p: FurniturePlacement, fp: RoomRect, others: RoomRect[]): number {
  const widthAlongX = Math.round(p.rotation / (Math.PI / 2)) % 2 === 0, cx = (fp.x0 + fp.x1) / 2, cz = (fp.z0 + fp.z1) / 2;
  let d = widthAlongX ? Math.min(cx - room.rect.x0, room.rect.x1 - cx) : Math.min(cz - room.rect.z0, room.rect.z1 - cz);
  for (const o of others){
    if (widthAlongX){ if (o.z1 <= fp.z0 + 1e-3 || o.z0 >= fp.z1 - 1e-3) continue; if (o.x0 >= fp.x1 - 1e-3) d = Math.min(d, o.x0 - cx); else if (o.x1 <= fp.x0 + 1e-3) d = Math.min(d, cx - o.x1); }
    else { if (o.x1 <= fp.x0 + 1e-3 || o.x0 >= fp.x1 - 1e-3) continue; if (o.z0 >= fp.z1 - 1e-3) d = Math.min(d, o.z0 - cz); else if (o.z1 <= fp.z0 + 1e-3) d = Math.min(d, cz - o.z1); }
  }
  return d;
}
const distToRect = (x: number, z: number, r: RoomRect) => Math.hypot(Math.max(r.x0 - x, 0, x - r.x1), Math.max(r.z0 - z, 0, z - r.z1));

export function tradeChecks(snap: Snapshot, cat: Catalog, fps: Map<string, RoomRect>, nameOf: (p: FurniturePlacement) => string): Out[] {
  const out: Out[] = [], tech = snap.tech ?? [];
  const mainRooms = snap.floor.rooms.filter(r => MAIN_ROOM_TYPES.has(r.type)).length;
  for (const room of snap.floor.rooms){
    const rn = room.name, inRoom = snap.placements.filter(p => p.roomId === room.id && fps.has(p.id));
    if (room.type === 'bucatarie'){
      // triunghiul de lucru: chiuveta (punctul de apă), plita (circuitul de gătit), frigiderul
      const sink = tech.find(t => t.roomId === room.id && t.kind === 'water_cold'), hob = tech.find(t => t.roomId === room.id && t.kind === 'cooker'), fridge = inRoom.find(p => p.group === 'frigider');
      if (sink && hob && fridge){
        const legs = [Math.hypot(sink.x - hob.x, sink.z - hob.z), Math.hypot(hob.x - fridge.x, hob.z - fridge.z), Math.hypot(fridge.x - sink.x, fridge.z - sink.z)].map(r2), sum = r2(legs[0]! + legs[1]! + legs[2]!);
        const short = Math.min(...legs), long = Math.max(...legs);
        if (sum > NKBA.triangleSumMax || long > NKBA.triangleLegMax) out.push({ rule: 'KITCHEN_TRIANGLE_LONG', a: { severity: 'WARNING', category: 'ergonomie', refs: { roomId: room.id, placementIds: [fridge.id] }, code: 'KITCHEN_TRIANGLE_LONG', vars: { rn, sum_m: sum, long_m: long } } });
        else if (short < NKBA.triangleLegMin) out.push({ rule: 'KITCHEN_TRIANGLE_SHORT', a: { severity: 'TIP', category: 'ergonomie', refs: { roomId: room.id, placementIds: [fridge.id] }, code: 'KITCHEN_TRIANGLE_SHORT', vars: { rn, short_m: short } } });
      }
      // ventilare (I5-2022): debitul cerut pentru bucătărie, de comparat cu hota (tubulatura nu în canalul comun al blocului)
      out.push({ rule: 'VENT_KITCHEN', a: { severity: 'TIP', category: 'instalatii', refs: { roomId: room.id }, code: 'VENT_KITCHEN', vars: { rn, flow: i5ExtractFlow(mainRooms).kitchen, rooms: mainRooms } } });
    }
    if (room.type === 'baie'){
      for (const p of inRoom.filter(q => q.group === 'wc' || q.group === 'lavoar')){ const fp = fps.get(p.id)!, d = r2(lateralClearance(room, p, fp, inRoom.filter(o => o.id !== p.id).map(o => fps.get(o.id)!)));
        if (d < NKBA.fixtureCentreMin) out.push({ rule: 'FIXTURE_SIDE', a: { severity: 'WARNING', category: 'ergonomie', refs: { roomId: room.id, placementIds: [p.id] }, code: 'FIXTURE_SIDE', vars: { nm: nameOf(p), rn, d_m: d, min_m: NKBA.fixtureCentreMin, rec_m: p.group === 'lavoar' ? NKBA.basinCentreRec : NKBA.fixtureCentreRec } } }); }
      const showers = inRoom.filter(q => q.group === 'dus');
      for (const p of showers){ const fp = fps.get(p.id)!, side = r2(Math.min(fp.x1 - fp.x0, fp.z1 - fp.z0));
        if (side < NKBA.showerMin) out.push({ rule: 'SHOWER_SMALL', a: { severity: 'WARNING', category: 'ergonomie', refs: { roomId: room.id, placementIds: [p.id] }, code: 'SHOWER_SMALL', vars: { nm: nameOf(p), rn, side_m: side, min_m: NKBA.showerMin, rec_m: NKBA.showerRec } } }); }
      // volumele electrice din baie: prize și întrerupătoare la peste 0,60 m de duș/cadă
      for (const t of tech.filter(t => t.roomId === room.id && ['outlet', 'outlet_double', 'switch', 'data'].includes(t.kind))){
        const sh = showers.find(p => distToRect(t.x, t.z, fps.get(p.id)!) < BATH_ZONE2_M);
        if (sh) out.push({ rule: `BATH_ZONE:${t.id}`, a: { severity: 'WARNING', category: 'siguranta', refs: { roomId: room.id, placementIds: [sh.id] }, code: 'BATH_ZONE', vars: { rn, d_m: r2(distToRect(t.x, t.z, fps.get(sh.id)!)), min_m: BATH_ZONE2_M } } });
      }
      // baie fără fereastră: ventilator de evacuare cu debitul din I5-2022
      const windowless = !snap.floor.walls.some(w => w.openings.some(o => o.kind === 'window' && openingInRoom(w, o, room)));
      if (windowless) out.push({ rule: 'VENT_BATH', a: { severity: 'WARNING', category: 'instalatii', refs: { roomId: room.id }, code: 'VENT_BATH', vars: { rn, flow: i5ExtractFlow(mainRooms).bath } } });
    }
  }
  return out;
}
function openingInRoom(w: Snapshot['floor']['walls'][number], o: { offset: number; width: number }, room: Room){
  const L = Math.hypot(w.b[0] - w.a[0], w.b[1] - w.a[1]) || 1, m = o.offset + o.width / 2, x = w.a[0] + (w.b[0] - w.a[0]) / L * m, z = w.a[1] + (w.b[1] - w.a[1]) / L * m, r = room.rect;
  return x >= r.x0 - 0.2 && x <= r.x1 + 0.2 && z >= r.z0 - 0.2 && z <= r.z1 + 0.2;
}
