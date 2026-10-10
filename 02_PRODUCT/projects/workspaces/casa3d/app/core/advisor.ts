// Consilierul de design: funcție pură și deterministă care spune ce nu e bine în proiect, de ce și cum se repară.
// Toate pragurile de mai jos sunt reguli de bun-simț din proiectarea interioară, NU norme legale.
import type { Catalog, FurniturePlacement, Room, RoomRect, Snapshot } from './types';
import { resolve } from './catalog';
import { area, insideRect, rectHit } from './geometry';
import { doorZones, footprintOf, openingsOnSide, validateFloor, validatePlacement } from './validate';
import { finishesOf, roomGeometry } from './boq';
import { FRONT_CLEARANCE, WALL_PROXIMITY_M } from './rules';
import { WINDOW_HEIGHT, LIGHTS_EXTRA_PER_M2 } from './rules.boq';
import { formatMoney } from './format';
import { contrastRatio, hexToRgb, hueDistance, isNeutral, relativeLuminance, rgbToHsl } from './color';

export type AdviceSeverity = 'BLOCKER' | 'WARNING' | 'TIP';
export type AdviceCategory = 'circulatie' | 'proportii' | 'culori' | 'lumina' | 'ergonomie' | 'buget' | 'siguranta';
export interface AdviceRefs { roomId?: string; placementIds?: string[]; wallIds?: string[] }
export interface Advice { id: string; severity: AdviceSeverity; category: AdviceCategory; title: string; why: string; fix: string; refs: AdviceRefs }
export interface ColorEntry { roomId: string; kind: 'wall' | 'floor' | 'item'; refId: string; hex: string }
export interface AdvisorOptions {
  accessibility?: boolean;
  colorsOf?: (snap: Snapshot, cat: Catalog) => ColorEntry[];
  budget?: { total: number | null; target: number | null; unknown: number };
}

// ---- Praguri (reguli de bun-simț, nu norme legale) ----
export const WARDROBE_CLEARANCE_M = 0.9;      // spațiu liber uzual în fața dulapurilor/comodelor, ca să se deschidă ușile și sertarele
export const BED_SIDE_CLEARANCE_M = 0.6;      // culoar uzual de ~60 cm pe cel puțin o latură a patului
export const ACCESSIBLE_CLEARANCE_M = 1.2;    // culoar uzual pentru scaun cu rotile / mers asistat
export const SOFA_TABLE_MIN_M = 0.35;         // distanță uzuală sofa–măsuță de cafea: sub 35 cm nu treci, peste 60 cm nu ajungi la ea
export const SOFA_TABLE_MAX_M = 0.6;
export const CROWDED_RATIO = 0.55;            // peste 55% din podea ocupată de mobilier = cameră aglomerată
export const EMPTY_LIVING_RATIO = 0.15;       // sub 15% într-un living = cameră „goală”
export const BIG_BED_WIDTH_M = 1.6;           // pat matrimonial (160 cm+)
export const SMALL_BEDROOM_M2 = 9;            // dormitor mic pentru un pat mare
export const SOFA_WALL_RATIO = 2 / 3;         // canapeaua nu ar trebui să ocupe mai mult de 2/3 din peretele de care stă lipită
export const WINDOW_TO_FLOOR_MIN = 1 / 8;     // aria ferestrelor ≥ 1/8 din aria podelei (regulă uzuală de lumină naturală)
export const TV_DISTANCE_MIN_M = 1.5;         // distanță uzuală de vizionare TV
export const TV_DISTANCE_MAX_M = 3.5;
export const DESK_WINDOW_MAX_M = 1.5;         // fereastră în spatele scaunului, la mai puțin de 1,5 m = reflexii pe ecran
export const MAX_DISTINCT_HUES = 4;           // mai mult de 4 nuanțe vii într-o cameră obosește privirea
export const HUE_CLUSTER_DEG = 30;            // două nuanțe la mai puțin de 30° se socotesc aceeași familie
export const MIN_ITEM_FLOOR_CONTRAST = 1.3;   // sub 1,3:1 piesa „se topește” în podea
export const LARGE_ITEM_M2 = 0.8;             // piesă mare = amprentă de cel puțin 0,8 m²
export const DARK_WALL_LUMINANCE = 0.15;      // pereți închiși la culoare (luminanță WCAG)
export const SMALL_ROOM_DARK_M2 = 10;         // camera mică pentru pereți închiși
export const SATURATED_MIN = 0.6;             // saturație peste care o culoare e „vie”
export const CLASH_HUE_MIN = 150;             // nuanțe aproape opuse (complementare)
export const CLASH_HUE_MAX = 210;
export const ASSUMED_WALL_HEX = '#f2efe8';    // ipoteză: pereți vopsiți deschis
export const ASSUMED_FLOOR_HEX = '#d8c9ae';   // ipoteză: pardoseală deschisă (parchet/gresie)

type Side = 'N' | 'S' | 'W' | 'E';
const SEV_ORDER: Record<AdviceSeverity, number> = { BLOCKER: 0, WARNING: 1, TIP: 2 };
const LIT_ROOMS = new Set(['living', 'dormitor', 'bucatarie']);
const QUARTER = Math.PI / 2;
const kOf = (rot: number) => ((Math.round(rot / QUARTER) % 4) + 4) % 4;
const FRONT_SIDE: Side[] = ['S', 'E', 'N', 'W'];            // direcția feței pentru k = 0..3 (convenția din frontRect)
const OPPOSITE: Record<Side, Side> = { N: 'S', S: 'N', W: 'E', E: 'W' };
const frontSideOf = (rot: number): Side => FRONT_SIDE[kOf(rot)];
const gapToWall = (r: RoomRect, fp: RoomRect, s: Side) => s === 'N' ? fp.z0 - r.z0 : s === 'S' ? r.z1 - fp.z1 : s === 'W' ? fp.x0 - r.x0 : r.x1 - fp.x1;
const sideStrip = (fp: RoomRect, s: Side, depth: number): RoomRect =>
  s === 'N' ? { x0: fp.x0, x1: fp.x1, z0: fp.z0 - depth, z1: fp.z0 } : s === 'S' ? { x0: fp.x0, x1: fp.x1, z0: fp.z1, z1: fp.z1 + depth }
  : s === 'W' ? { x0: fp.x0 - depth, x1: fp.x0, z0: fp.z0, z1: fp.z1 } : { x0: fp.x1, x1: fp.x1 + depth, z0: fp.z0, z1: fp.z1 };
const overlapsSpan = (s: Side, fp: RoomRect, o: { a: number; b: number }) => { const a = s === 'N' || s === 'S' ? fp.x0 : fp.z0, b = s === 'N' || s === 'S' ? fp.x1 : fp.z1; return b > o.a + .01 && a < o.b - .01; };
const rectGap = (a: RoomRect, b: RoomRect) => Math.hypot(Math.max(0, a.x0 - b.x1, b.x0 - a.x1), Math.max(0, a.z0 - b.z1, b.z0 - a.z1));
const centre = (r: RoomRect) => ({ x: (r.x0 + r.x1) / 2, z: (r.z0 + r.z1) / 2 });
const cm = (m: number) => Math.round(m * 100);
const cmUp = (m: number) => Math.max(1, Math.ceil(m * 100 - 1e-6));
const overlapDepth = (a: RoomRect, b: RoomRect) => Math.min(Math.min(a.x1, b.x1) - Math.max(a.x0, b.x0), Math.min(a.z1, b.z1) - Math.max(a.z0, b.z0));
const sq = (m2: number) => `${(Math.round(m2 * 10) / 10).toLocaleString('ro-RO')} m²`;

/** Culorile implicite: culoarea mobilierului din catalog (style.col) + ipoteza unor pereți și pardoseli deschise. */
export function defaultColors(snap: Snapshot, cat: Catalog): ColorEntry[] {
  const out: ColorEntry[] = [];
  for (const r of snap.floor.rooms){ out.push({ roomId: r.id, kind: 'wall', refId: r.id, hex: ASSUMED_WALL_HEX }, { roomId: r.id, kind: 'floor', refId: r.id, hex: ASSUMED_FLOOR_HEX }); }
  for (const p of snap.placements){ const col = resolve(cat, p.variantId)?.variant.style?.col; if (typeof col === 'string' && hexToRgb(col)) out.push({ roomId: p.roomId, kind: 'item', refId: p.id, hex: col }); }
  return out;
}

export function adviseProject(snap: Snapshot, cat: Catalog, opts: AdvisorOptions = {}): Advice[] {
  const out: Advice[] = [], seen = new Set<string>();
  const clear = opts.accessibility ? ACCESSIBLE_CLEARANCE_M : null;
  const roomOf = (id: string) => snap.floor.rooms.find(r => r.id === id);
  const nameOf = (p: FurniturePlacement) => resolve(cat, p.variantId)?.product.name ?? 'Piesa';
  const add = (rule: string, a: Omit<Advice, 'id'>) => {
    const id = `${rule}:${a.refs.roomId ?? ''}:${(a.refs.placementIds ?? []).slice().sort().join(',')}${a.refs.wallIds?.length ? ':' + a.refs.wallIds.join(',') : ''}`;
    if (seen.has(id)) return; seen.add(id); out.push({ id, ...a });
  };
  const fps = new Map<string, RoomRect>(); for (const p of snap.placements){ const f = footprintOf(cat, p); if (f) fps.set(p.id, f); }
  const inRoom = (room: Room) => snap.placements.filter(p => p.roomId === room.id && fps.has(p.id));
  const hasClearanceIssue = new Set<string>();

  // ---------- 1) Validare ----------
  for (const p of snap.placements){
    const room = roomOf(p.roomId), nm = nameOf(p), rn = room?.name ?? 'cameră', fp = fps.get(p.id);
    for (const i of validatePlacement(snap, cat, p)){
      const sev: AdviceSeverity = i.severity === 'ERROR' ? 'BLOCKER' : 'WARNING', other = i.with ? snap.placements.find(o => o.id === i.with) : undefined;
      const refs: AdviceRefs = { roomId: p.roomId, placementIds: other ? [p.id, other.id] : [p.id] };
      switch (i.code){
        case 'UNKNOWN_VARIANT': add('UNKNOWN_VARIANT', { severity: sev, category: 'siguranta', refs, title: 'Produs dispărut din catalog', why: 'Piesa din plan nu mai există în catalog, așa că nu îi putem verifica dimensiunile și nici prețul.', fix: 'Înlocuiește piesa cu un produs din catalog sau șterge-o din plan.' }); break;
        case 'OUT_OF_ROOM': add('OUT_OF_ROOM', { severity: sev, category: 'siguranta', refs, title: `${nm} iese din ${rn}`, why: room ? `Piesa depășește pereții camerei ${rn}, deci nu poate fi montată acolo.` : 'Piesa nu aparține niciunei camere din plan.', fix: room ? 'Mută piesa spre interiorul camerei sau alege un model mai mic.' : 'Mută piesa într-o cameră existentă sau șterge-o.' }); break;
        case 'OVERLAP': { const d = other && fp && fps.get(other.id) ? overlapDepth(fp, fps.get(other.id)!) : 0.1;
          add('OVERLAP', { severity: sev, category: 'siguranta', refs: { ...refs, placementIds: [p.id, ...(other ? [other.id] : [])].sort() }, title: `${nm} se suprapune cu ${other ? nameOf(other) : 'altă piesă'}`, why: 'Două piese nu pot ocupa același loc în cameră.', fix: `Mută una dintre piese cu cel puțin ${cmUp(d)} cm sau alege un model mai mic.` }); break; }
        case 'DOOR_ZONE': { let d = 0.1; if (room && fp) for (const z of doorZones(snap.floor, room)) if (rectHit(z, fp)) { d = Math.max(d, overlapDepth(z, fp)); }
          add('DOOR_ZONE', { severity: sev, category: 'siguranta', refs, title: 'Ușa nu se poate deschide complet', why: `${nm} stă în zona în care se deschide o ușă din ${rn}.`, fix: `Mută piesa cu cel puțin ${cmUp(d)} cm (departe de ușă) sau în altă parte a camerei.` }); break; }
        case 'WINDOW_BLOCKED': add('WINDOW_BLOCKED', { severity: sev, category: 'lumina', refs, title: `${nm} acoperă o fereastră`, why: 'O piesă mai înaltă de 95 cm lipită de perete blochează lumina și accesul la fereastră.', fix: 'Mută piesa lângă un perete fără fereastră sau alege un model mai jos de 95 cm.' }); break;
        case 'CLEARANCE': { hasClearanceIssue.add(p.id);
          if (other) add('CLEARANCE', { severity: sev, category: 'circulatie', refs, title: `${nm} și ${nameOf(other)} sunt prea apropiate`, why: 'Spațiul de folosire dintre cele două piese e prea strâmt (uși, sertare sau scaune nu se pot folosi comod).', fix: 'Depărtează piesele cu cel puțin 30 cm una de alta sau mută una dintre ele în altă zonă a camerei.' });
          else { const need = cm(FRONT_CLEARANCE[p.group] ?? 0.6); add('CLEARANCE', { severity: sev, category: 'circulatie', refs, title: `${nm}: prea puțin loc în față`, why: `În fața piesei ar trebui să rămână în jur de ${need} cm liberi, iar aici nu rămân.`, fix: `Mută piesa spre interior până rămân cel puțin ${need} cm liberi în față.` }); }
          break; }
      }
    }
  }
  for (const i of validateFloor(snap.floor)){
    const esc = (t: string) => t.replace(/[.*+?^${}()|[\]\\]/g, '\\$&'), wallId = snap.floor.walls.find(w => new RegExp(`(^|[^\\w-])${esc(w.id)}(?![\\w-])`).test(i.message))?.id;
    const refs: AdviceRefs = wallId ? { wallIds: [wallId] } : {};
    if (i.code === 'WALL_TOO_SHORT') add('WALL_TOO_SHORT', { severity: 'BLOCKER', category: 'siguranta', refs, title: 'Perete prea scurt', why: 'Un perete sub 20 cm nu poate fi construit și strică desenul planului.', fix: 'Prelungește peretele la cel puțin 20 cm sau șterge-l.' });
    else add('OPENING_OUTSIDE_WALL', { severity: 'BLOCKER', category: 'siguranta', refs, title: 'Ușa sau fereastra iese din perete', why: 'Golul este mai lat decât peretele în care a fost pus.', fix: 'Mută golul spre interiorul peretelui sau micșorează-i lățimea.' });
  }

  // ---------- 2) Pe cameră ----------
  const mainClear = (base: number) => clear ? Math.max(base, clear) : base;
  const freeRect = (room: Room, r: RoomRect, ignore: Set<string>) => insideRect(room.rect, r) && !inRoom(room).some(o => !ignore.has(o.id) && rectHit(r, fps.get(o.id)!));
  for (const room of snap.floor.rooms){
    const ps = inRoom(room), g = roomGeometry(snap, room), A = g.floorArea, rid = room.id, rn = room.name;
    const ofGroup = (grp: string) => ps.filter(p => p.group === grp);

    // Circulație
    const wc = mainClear(WARDROBE_CLEARANCE_M);
    for (const p of ofGroup('dulap')){ if (!clear && hasClearanceIssue.has(p.id)) continue;
      const fp = fps.get(p.id)!, fr = sideStrip(fp, frontSideOf(p.rotation), wc);
      if (!freeRect(room, fr, new Set([p.id]))) add('WARDROBE_FRONT', { severity: 'WARNING', category: 'circulatie', refs: { roomId: rid, placementIds: [p.id] }, title: `Prea puțin loc în fața dulapului din ${rn}`, why: `În fața dulapurilor ar trebui să rămână cel puțin ${cm(wc)} cm liberi, ca să se deschidă ușile și să te poți mișca.`, fix: `Mută dulapul sau piesa din fața lui cu cel puțin ${cm(wc)} cm mai departe, ca să rămână ${cm(wc)} cm liberi.` }); }
    const bc = mainClear(BED_SIDE_CLEARANCE_M);
    for (const p of ofGroup('pat')){ const fp = fps.get(p.id)!, back = OPPOSITE[frontSideOf(p.rotation)];
      const lateral = (['N', 'S', 'W', 'E'] as Side[]).filter(s => s !== back && s !== OPPOSITE[back] && s !== frontSideOf(p.rotation));
      const ignore = new Set([p.id, ...ofGroup('noptiera').map(n => n.id)]);
      if (!lateral.some(s => freeRect(room, sideStrip(fp, s, bc), ignore))) add('BED_ACCESS', { severity: 'WARNING', category: 'circulatie', refs: { roomId: rid, placementIds: [p.id] }, title: `Patul din ${rn} nu are loc să te urci în el`, why: `Pe cel puțin o latură a patului ar trebui să rămână ${cm(bc)} cm liberi, altfel accesul e incomod.`, fix: `Depărtează patul de perete sau de mobilă cu cel puțin ${cm(bc)} cm pe una dintre laturile lungi.` }); }
    for (const s of ofGroup('canapea')){ const masutas = ofGroup('masuta'); if (!masutas.length) continue;
      const fs = fps.get(s.id)!, best = masutas.map(m => ({ m, d: rectGap(fs, fps.get(m.id)!) })).sort((a, b) => a.d - b.d || (a.m.id < b.m.id ? -1 : 1))[0];
      if (best.d < SOFA_TABLE_MIN_M - 1e-6) add('SOFA_TABLE', { severity: 'TIP', category: 'circulatie', refs: { roomId: rid, placementIds: [s.id, best.m.id] }, title: `Măsuța e prea aproape de canapea în ${rn}`, why: `Între canapea și măsuță se lasă de obicei ${cm(SOFA_TABLE_MIN_M)}–${cm(SOFA_TABLE_MAX_M)} cm; acum sunt ${cm(best.d)} cm și nu mai poți trece sau te așeza comod.`, fix: `Depărtează măsuța cu cel puțin ${cmUp(SOFA_TABLE_MIN_M - best.d)} cm de canapea.` });
      else if (best.d > SOFA_TABLE_MAX_M + 1e-6) add('SOFA_TABLE', { severity: 'TIP', category: 'circulatie', refs: { roomId: rid, placementIds: [s.id, best.m.id] }, title: `Măsuța e prea departe de canapea în ${rn}`, why: `Între canapea și măsuță se lasă de obicei ${cm(SOFA_TABLE_MIN_M)}–${cm(SOFA_TABLE_MAX_M)} cm; acum sunt ${cm(best.d)} cm și nu ajungi la ea din șezut.`, fix: `Apropie măsuța de canapea cu cel puțin ${cmUp(best.d - SOFA_TABLE_MAX_M)} cm.` }); }

    // Proporții
    const used = ps.filter(p => p.group !== 'scaunBirou').reduce((a, p) => { const f = fps.get(p.id)!; return a + area(f); }, 0);
    if (A > 0 && used / A > CROWDED_RATIO) add('CROWDED', { severity: 'WARNING', category: 'proportii', refs: { roomId: rid }, title: `${rn} e aglomerată`, why: `Mobilierul ocupă ${Math.round(used / A * 100)}% din podea (${sq(used)} din ${sq(A)}); peste ${Math.round(CROWDED_RATIO * 100)}% camera devine strâmtă.`, fix: `Scoate sau micșorează piese cu cel puțin ${sq(used - CROWDED_RATIO * A)} de amprentă ca să cobori sub ${Math.round(CROWDED_RATIO * 100)}%.` });
    if (room.type === 'living' && A > 0 && used / A < EMPTY_LIVING_RATIO) add('EMPTY_LIVING', { severity: 'TIP', category: 'proportii', refs: { roomId: rid }, title: `${rn} pare goală`, why: `Mobilierul ocupă doar ${Math.round(used / A * 100)}% din podea; un living are de obicei cel puțin ${Math.round(EMPTY_LIVING_RATIO * 100)}%.`, fix: 'Adaugă o canapea, o măsuță sau un corp TV din tabul Catalog.' });
    if (room.type === 'dormitor' && A < SMALL_BEDROOM_M2) for (const p of ofGroup('pat')){ const rv = resolve(cat, p.variantId)!;
      if (Math.min(rv.w, rv.d) >= BIG_BED_WIDTH_M - 1e-6) add('BIG_BED_SMALL_ROOM', { severity: 'WARNING', category: 'proportii', refs: { roomId: rid, placementIds: [p.id] }, title: `Pat mare într-un dormitor mic (${rn})`, why: `Un pat de ${cm(Math.min(rv.w, rv.d))} cm într-o cameră de ${sq(A)} (sub ${SMALL_BEDROOM_M2} m²) lasă prea puțin loc pentru circulație și dulap.`, fix: 'Alege un pat de 140 cm sau mai îngust, sau mută patul într-o cameră mai mare.' }); }
    for (const s of ofGroup('canapea')){ const rv = resolve(cat, s.variantId)!, fp = fps.get(s.id)!, back = OPPOSITE[frontSideOf(s.rotation)];
      if (gapToWall(room.rect, fp, back) > WALL_PROXIMITY_M) continue;
      const wl = back === 'N' || back === 'S' ? room.rect.x1 - room.rect.x0 : room.rect.z1 - room.rect.z0, len = Math.max(rv.w, rv.d);
      if (len > wl * SOFA_WALL_RATIO + 1e-6) add('SOFA_LONG', { severity: 'TIP', category: 'proportii', refs: { roomId: rid, placementIds: [s.id] }, title: `Canapeaua domină peretele din ${rn}`, why: `Canapeaua are ${cm(len)} cm, mai mult de 2/3 din peretele de ${cm(wl)} cm de care stă lipită; peretele pare înghesuit.`, fix: `Alege o canapea de cel mult ${cm(wl * SOFA_WALL_RATIO)} cm lungime sau mut-o pe un perete mai lung.` }); }

    // Lumină
    if (LIT_ROOMS.has(room.type)){
      if (g.windowWidth <= 0) add('NO_WINDOW', { severity: 'WARNING', category: 'lumina', refs: { roomId: rid }, title: `${rn} nu are fereastră`, why: 'Living-ul, dormitorul și bucătăria au nevoie de lumină naturală și aerisire.', fix: 'Adaugă o fereastră pe un perete exterior cu unealta Fereastră, sau compensează cu iluminat suplimentar.' });
      else { const wa = g.windowWidth * WINDOW_HEIGHT;
        if (A > 0 && wa < A * WINDOW_TO_FLOOR_MIN - 1e-6) add('SMALL_WINDOW', { severity: 'TIP', category: 'lumina', refs: { roomId: rid }, title: `Lumină naturală puțină în ${rn}`, why: `Ferestrele însumează ${sq(wa)}, adică ${Math.round(wa / A * 100)}% din podea; se recomandă cel puțin 1/8 (${sq(A * WINDOW_TO_FLOOR_MIN)}).`, fix: `Mărește ferestrele cu cel puțin ${cmUp((A * WINDOW_TO_FLOOR_MIN - wa) / WINDOW_HEIGHT)} cm lățime în total sau adaugă o fereastră.` }); }
    }
    const lights = finishesOf(snap, room).lights, need = 1 + Math.max(0, Math.ceil((A - LIGHTS_EXTRA_PER_M2) / LIGHTS_EXTRA_PER_M2));
    if (lights != null && lights < need) add('FEW_LIGHTS', { severity: 'TIP', category: 'lumina', refs: { roomId: rid }, title: `Prea puține corpuri de iluminat în ${rn}`, why: `Pentru ${sq(A)} se recomandă un corp de iluminat la fiecare ${LIGHTS_EXTRA_PER_M2} m² (minim ${need}); ai ${lights}.`, fix: `Crește numărul de lumini la ${need} în panoul camerei, secțiunea Finisaje.` });

    // Ergonomie
    for (const t of ofGroup('comodaTv')){ const sofas = ofGroup('canapea'); if (!sofas.length) continue;
      const ct = centre(fps.get(t.id)!), best = sofas.map(s => { const c = centre(fps.get(s.id)!); return { s, d: Math.hypot(c.x - ct.x, c.z - ct.z) }; }).sort((a, b) => a.d - b.d || (a.s.id < b.s.id ? -1 : 1))[0];
      if (best.d < TV_DISTANCE_MIN_M - 1e-6) add('TV_DISTANCE', { severity: 'TIP', category: 'ergonomie', refs: { roomId: rid, placementIds: [t.id, best.s.id] }, title: `Televizorul e prea aproape de canapea în ${rn}`, why: `Distanța e ${(Math.round(best.d * 10) / 10).toLocaleString('ro-RO')} m; pentru confort ar trebui să fie ${TV_DISTANCE_MIN_M}–${TV_DISTANCE_MAX_M} m.`, fix: `Depărtează canapeaua de corpul TV cu cel puțin ${cmUp(TV_DISTANCE_MIN_M - best.d)} cm.` });
      else if (best.d > TV_DISTANCE_MAX_M + 1e-6) add('TV_DISTANCE', { severity: 'TIP', category: 'ergonomie', refs: { roomId: rid, placementIds: [t.id, best.s.id] }, title: `Televizorul e prea departe de canapea în ${rn}`, why: `Distanța e ${(Math.round(best.d * 10) / 10).toLocaleString('ro-RO')} m; pentru confort ar trebui să fie ${TV_DISTANCE_MIN_M}–${TV_DISTANCE_MAX_M} m.`, fix: `Apropie canapeaua de corpul TV cu cel puțin ${cmUp(best.d - TV_DISTANCE_MAX_M)} cm.` }); }
    for (const d of ofGroup('birou')){ const fp = fps.get(d.id)!, s = frontSideOf(d.rotation), gap = gapToWall(room.rect, fp, s);
      if (gap <= DESK_WINDOW_MAX_M && openingsOnSide(snap.floor, room, s).some(o => o.kind === 'window' && overlapsSpan(s, fp, o))) add('DESK_GLARE', { severity: 'TIP', category: 'ergonomie', refs: { roomId: rid, placementIds: [d.id] }, title: `Fereastra e în spatele scaunului de la birou (${rn})`, why: 'Lumina din spate dă reflexii pe ecran și te obosește la ochi.', fix: 'Rotește biroul astfel încât fereastra să fie în lateral, sau pune jaluzele.' }); }
    for (const b of ofGroup('pat')){ const fp = fps.get(b.id)!, back = OPPOSITE[frontSideOf(b.rotation)];
      if (gapToWall(room.rect, fp, back) <= WALL_PROXIMITY_M && openingsOnSide(snap.floor, room, back).some(o => o.kind === 'window' && overlapsSpan(back, fp, o))) add('HEADBOARD_WINDOW', { severity: 'TIP', category: 'ergonomie', refs: { roomId: rid, placementIds: [b.id] }, title: `Patul are fereastra deasupra tăbliei (${rn})`, why: 'Curentul de aer, frigul și lumina de dimineață ajung direct la cap.', fix: 'Mută patul pe un perete fără fereastră sau pune draperii groase.' }); }
  }

  // ---------- 3) Culori ----------
  const colors = (opts.colorsOf ?? defaultColors)(snap, cat);
  for (const room of snap.floor.rooms){
    const cs = colors.filter(c => c.roomId === room.id && hexToRgb(c.hex)), A = roomGeometry(snap, room).floorArea, rid = room.id, rn = room.name;
    const vivid = cs.filter(c => !isNeutral(c.hex)).map(c => ({ c, hsl: rgbToHsl(hexToRgb(c.hex)!) })).sort((a, b) => a.hsl.h - b.hsl.h || (a.c.hex < b.c.hex ? -1 : a.c.hex > b.c.hex ? 1 : 0));
    const reps: number[] = []; for (const v of vivid) if (!reps.some(h => hueDistance(h, v.hsl.h) < HUE_CLUSTER_DEG)) reps.push(v.hsl.h);
    if (reps.length > MAX_DISTINCT_HUES) add('TOO_MANY_HUES', { severity: 'WARNING', category: 'culori', refs: { roomId: rid }, title: `Prea multe culori în ${rn}`, why: `Camera are ${reps.length} nuanțe diferite; peste ${MAX_DISTINCT_HUES} privirea nu mai are un punct de odihnă.`, fix: `Păstrează cel mult ${MAX_DISTINCT_HUES} nuanțe: înlocuiește ${reps.length - MAX_DISTINCT_HUES} piese colorate cu variante albe, gri sau bej.` });
    const floor = cs.filter(c => c.kind === 'floor').at(-1);
    if (floor) for (const c of cs.filter(c => c.kind === 'item')){ const p = snap.placements.find(x => x.id === c.refId), fp = p && fps.get(p.id); if (!p || !fp || area(fp) < LARGE_ITEM_M2) continue;
      const ratio = contrastRatio(c.hex, floor.hex); if (ratio < MIN_ITEM_FLOOR_CONTRAST) add('LOW_CONTRAST', { severity: 'TIP', category: 'culori', refs: { roomId: rid, placementIds: [p.id] }, title: `${nameOf(p)} se pierde în pardoseală (${rn})`, why: `Contrastul dintre piesă și podea e doar ${(Math.round(ratio * 100) / 100).toLocaleString('ro-RO')}:1 (sub ${MIN_ITEM_FLOOR_CONTRAST}:1); piesa mare nu se mai distinge.`, fix: 'Alege o piesă mai deschisă sau mai închisă decât podeaua, sau schimbă finisajul pardoselii.' }); }
    if (A < SMALL_ROOM_DARK_M2 && cs.some(c => c.kind === 'wall' && relativeLuminance(c.hex) < DARK_WALL_LUMINANCE)) add('DARK_WALLS', { severity: 'TIP', category: 'culori', refs: { roomId: rid }, title: `Pereți închiși la culoare într-o cameră mică (${rn})`, why: `Camera are ${sq(A)} (sub ${SMALL_ROOM_DARK_M2} m²), iar pereții închiși o fac să pară și mai mică.`, fix: 'Vopsește pereții într-o culoare deschisă sau pune culoarea închisă doar pe un singur perete.' });
    const sat = vivid.filter(v => v.hsl.s >= SATURATED_MIN); let clash = false;
    for (let i = 0; i < sat.length && !clash; i++) for (let j = i + 1; j < sat.length; j++){ const d = hueDistance(sat[i].hsl.h, sat[j].hsl.h);
      if (d >= CLASH_HUE_MIN && d <= CLASH_HUE_MAX){ clash = true; break; } }
    if (clash) add('COLOR_CLASH', { severity: 'TIP', category: 'culori', refs: { roomId: rid }, title: `Contrast puternic de culori în ${rn}`, why: 'Două culori vii și aproape opuse pe roata culorilor atrag privirea în direcții diferite; contrast puternic, folosește-l intenționat.', fix: 'Dacă nu e intenționat, înlocuiește una dintre piese cu o variantă neutră sau mai estompată.' });
  }

  // ---------- 4) Buget ----------
  const b = opts.budget;
  if (b){
    if (b.total != null && b.target != null && b.total > b.target) add('OVER_BUDGET', { severity: 'WARNING', category: 'buget', refs: {}, title: 'Proiectul depășește bugetul', why: `Totalul estimat e ${formatMoney(b.total, 'RON')}, cu ${formatMoney(b.total - b.target, 'RON')} peste ținta de ${formatMoney(b.target, 'RON')}.`, fix: `Redu cu cel puțin ${formatMoney(b.total - b.target, 'RON')}: alege variante mai ieftine la piesele mari sau scoate piese opționale.` });
    if (b.unknown > 0) add('UNKNOWN_PRICES', { severity: 'TIP', category: 'buget', refs: {}, title: `${b.unknown} ${b.unknown === 1 ? 'produs nu are' : 'produse nu au'} preț cunoscut`, why: 'Totalul din buget nu include piesele fără preț, deci costul real e mai mare.', fix: 'Verifică prețul acestor piese la magazin sau înlocuiește-le cu produse care au preț în catalog.' });
  }

  const roomIdx = new Map(snap.floor.rooms.map((r, i) => [r.id, i])), rank = (a: Advice) => a.refs.roomId != null && roomIdx.has(a.refs.roomId) ? roomIdx.get(a.refs.roomId)! : 1e6;
  return out.sort((a, b) => SEV_ORDER[a.severity] - SEV_ORDER[b.severity] || rank(a) - rank(b) || (a.id < b.id ? -1 : a.id > b.id ? 1 : 0));
}
