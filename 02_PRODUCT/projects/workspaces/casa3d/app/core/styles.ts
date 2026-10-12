// Pachete de stil: un set coerent de finisaje pentru toată casa, aplicat dintr-un clic — pardoseală și modul de așezare, culorile
// pereților și tavanului, peretele de accent (în spatele canapelei sau al patului), tavan, uși și mânere, textile, covoare,
// bucătăria și baia ca sisteme, corpurile de iluminat. Folosește doar produse din catalogul de materiale; ce lipsește se sare.
// Aplicarea înlocuiește alegerile de finisaj existente (cu confirmare în interfață) și nu atinge mobilierul.
import type { Catalog, MaterialsCatalog, Snapshot, Room, RoomFinishes, WallFeature, LightFixture, KitchenSpec, BathSpec, WindowTreatment } from './types';
import { floors } from './levels';
import { defaultFinishes, computeBOQ } from './boq';
import { footprintOf } from './validate';
import { roomWindows } from './textiles';
import { doorOpenings } from './doors';
import { materialOf } from './finishes';
import { backSide } from './fixtures';

type Side = WallFeature['side'];
interface Accent { kind: WallFeature['kind']; material: string; color?: string; heightM?: number }
export interface StylePackage { id: 'scandi' | 'modern' | 'japandi' | 'industrial' | 'classic';
  /** pardoseala: camerele uscate / camerele umede */
  floor: { dry: string; dryLayout?: RoomFinishes['floorLayout']; wet: string; wetLayout?: RoomFinishes['floorLayout'] };
  walls: string; ceiling: string; paint: string; wallTile: string; baseboard: string | null; light: string;
  /** peretele din spatele canapelei (living) și al patului (dormitor) */
  accentLiving?: Accent[]; accentBed?: Accent[];
  /** lambriu + baghetă pe tot holul (stil clasic) */
  hallWainscot?: Accent[];
  ceilingLiving?: RoomFinishes['ceiling']; cornice?: string;
  door: string; handle: string; curtain?: string; sheer?: string; blind?: 'fonsterblad' | 'ringblomma' | 'vecklarfly' | null;
  rugLiving?: string; rugBed?: string;
  kitchen: KitchenSpec; bath: BathSpec; fixtureColor: string; pendant?: string; sconce?: string; counterPendant?: string; pendantTable: boolean; trackLiving: boolean; bedSconces: boolean; counterPendants: boolean }

export const STYLE_PACKAGES: StylePackage[] = [
  { id: 'scandi', floor: { dry: 'parchet-egger-h2099', dryLayout: { pattern: 'third' }, wet: 'gresie-mckinley' }, walls: '#f4f3ef', ceiling: '#fbfbf9', paint: 'vopsea-innenweiss', wallTile: 'faianta-lane-blanco', baseboard: 'plinta-mdf-90', light: 'lampa-virrmo',
    accentLiving: [{ kind: 'paint', material: 'vopsea-savana-super', color: '#c9d3cc' }], door: 'usa-megadoor-clasic-alb-87', handle: 'maner-sterk-sm1703-inox', curtain: 'draperie-annakajsa-bej', sheer: 'perdea-bergnejlika-alb', blind: null,
    rugLiving: 'covor-stoense-200x300', rugBed: 'covor-morum-bej-160x230',
    kitchen: { frontColor: '#f2f1ec', frontFinish: 'matt', handle: 'bar', handleColor: '#c9c9c6', upper: 'closed', countertop: 'dedeman-kronodesign-k003fp-blat-stejar-3040x600x28', countertopColor: '#c9a77c', countertopMm: 28, backsplash: 'tile', underLed: 'banda-led-hoff-3000k', sink: 'dedeman-sandonna-lena-chiuveta-quartz-alb-rotunda', tap: 'dedeman-kadda-clover-my1708-42-bucatarie-cromat' },
    bath: { metal: 'chrome', tileZone: 'h210', showerType: 'cabin', wc: 'floor', tap: 'dedeman-hansgrohe-logis-e-71160000-lavoar-cromat', shower: 'dedeman-kadda-girard-my2061-77c-sistem-dus-cromat', towelRadiator: 'dedeman-radox-scala-portprosop-alb-500x1200', mirror: 'ikea-faxalven-oglinda-iluminat-60x95' }, pendant: 'ikea-akterspring-lustra-opalin-alamit-73', sconce: 'dedeman-smarter-kaya-01-4741-aplica-e14-negru-alb', fixtureColor: '#f2f1ec', pendantTable: true, trackLiving: false, bedSconces: true, counterPendants: false },
  { id: 'modern', floor: { dry: 'gresie-emarble-60x120', dryLayout: { pattern: 'brick', groutMm: 2, groutColor: '#9a9894' }, wet: 'gresie-emarble-60x120', wetLayout: { pattern: 'straight', groutMm: 2, groutColor: '#9a9894' } },
    walls: '#ecebe8', ceiling: '#fbfbf9', paint: 'vopsea-caparol-alb', wallTile: 'faianta-lane-blanco', baseboard: 'plinta-mdf-60', light: 'lampa-kabomba',
    accentLiving: [{ kind: 'slats', material: 'riflaj-mdf-unic-alb', color: '#3c3f41' }], accentBed: [{ kind: 'paint', material: 'vopsea-savana-super', color: '#5e646b' }],
    ceilingLiving: { type: 'cove', dropCm: 10, coveCm: 30, led: 'banda-led-hoff-3000k', spot: 'spot-mt143-9w', spots: 6 },
    door: 'usa-bestimp-g6-gri-88', handle: 'maner-kuchinox-sombra-negru', sheer: 'perdea-bergnejlika-alb', blind: 'fonsterblad', rugLiving: 'covor-arende-gri-160x230', rugBed: 'covor-arende-gri-160x230',
    kitchen: { frontColor: '#3c3f41', frontFinish: 'matt', handle: 'profile', handleColor: '#1c1c1c', upper: 'closed', countertop: 'ikea-ekbacken-blat-alb-aspect-marmura-246x2-8', countertopColor: '#e8e4dc', countertopMm: 28, backsplash: 'countertop', underLed: 'banda-led-hoff-3000k', sink: 'dedeman-franke-bsg-611-78-s-chiuveta-fragranit-negru', tap: 'ikea-almaren-baterie-bucatarie-negru' },
    bath: { metal: 'black', tileZone: 'full', showerType: 'walkin', wc: 'wall', tap: 'dedeman-grohe-start-235502432-lavoar-negru-mat', shower: 'dedeman-ideal-standard-ceratherm-alu-bd583-sistem-dus-negru-mat', towelRadiator: 'dedeman-purmo-banga-portprosop-negru-500x1222', mirror: 'dedeman-savini-due-br-80-60-oglinda-led-80x60' }, pendant: 'dedeman-smarter-boom-01-3480-suspensie-3xgu10-negru-mat', counterPendant: 'dedeman-smarter-boom-01-3480-suspensie-3xgu10-negru-mat', sconce: 'dedeman-smarter-kaya-01-4741-aplica-e14-negru-alb', fixtureColor: '#1c1c1c', pendantTable: true, trackLiving: true, bedSconces: true, counterPendants: false },
  { id: 'japandi', floor: { dry: 'parchet-pergo-5006', dryLayout: { pattern: 'brick' }, wet: 'gresie-glocal-bianco' }, walls: '#ebe5da', ceiling: '#f6f3ee', paint: 'vopsea-innenweiss', wallTile: 'faianta-grafen', baseboard: 'plinta-mdf-60', light: 'lampa-virrmo',
    accentLiving: [{ kind: 'plaster', material: 'tencuiala-kober-bob-orez', color: '#cfc6b8' }], accentBed: [{ kind: 'slats', material: 'riflaj-mdf-unic-alb', color: '#b08a62' }],
    door: 'usa-r80-stejar-gri-86', handle: 'maner-kuchinox-sombra-negru', curtain: 'draperie-annakajsa-bej', blind: 'ringblomma', rugLiving: 'covor-bronden-170x240', rugBed: 'covor-morum-bej-160x230',
    kitchen: { frontColor: '#b8936a', frontFinish: 'wood', handle: 'profile', handleColor: '#55585c', upper: 'open', countertop: 'ikea-karlby-blat-stejar-furnir-186x3-8', countertopColor: '#c49a6c', countertopMm: 38, backsplash: 'countertop', sink: 'ikea-kilsviken-chiuveta-1-cuva-gri-bej-compozit-cuart', tap: 'ikea-taksjon-baterie-bucatarie-aspect-inox' },
    bath: { metal: 'gunmetal', tileZone: 'h120', showerType: 'cabin', wc: 'wall', tap: 'dedeman-kadda-girard-my2061-p2h-lavoar-gri-periat', towelRadiator: 'dedeman-radox-scala-portprosop-alb-500x1200', mirror: 'dedeman-kadda-nw-18-oglinda-led-rotunda-60' }, pendant: 'ikea-narrkolv-lustra-ratan-56', fixtureColor: '#c8b79a', pendantTable: true, trackLiving: false, bedSconces: false, counterPendants: false },
  { id: 'industrial', floor: { dry: 'parchet-krono-herringbone-k450', dryLayout: { pattern: 'herringbone' }, wet: 'gresie-glocal-bianco' }, walls: '#dcdad5', ceiling: '#f2f1ee', paint: 'vopsea-caparol-alb', wallTile: 'faianta-lane-blanco', baseboard: 'plinta-mdf-60', light: 'lampa-kabomba',
    accentLiving: [{ kind: 'brick', material: 'caramida-bronx-60' }], accentBed: [{ kind: 'plaster', material: 'tencuiala-kober-bob-orez', color: '#8d8b86' }],
    door: 'usa-bestimp-g6-gri-88', handle: 'maner-kuchinox-sombra-negru', curtain: 'draperie-annakajsa-bej', blind: 'vecklarfly', rugLiving: 'covor-arende-gri-160x230', rugBed: 'covor-morum-80x200',
    kitchen: { frontColor: '#2b2b2b', frontFinish: 'matt', handle: 'bar', handleColor: '#1c1c1c', upper: 'open', countertop: 'dedeman-kronodesign-k003fp-blat-stejar-3040x600x28', countertopColor: '#8f6243', countertopMm: 28, backsplash: 'tile', underLed: 'banda-led-hoff-3000k', sink: 'dedeman-evido-quadro-6-chiuveta-granit-antracit', tap: 'dedeman-ferro-fitness-nero-bfs4blb-bucatarie-negru' },
    bath: { metal: 'black', tileZone: 'h210', showerType: 'walkin', wc: 'floor', tap: 'ikea-dalskar-baterie-lavoar-negru', shower: 'dedeman-kadda-girard-my2061-77b-sistem-dus-negru', towelRadiator: 'dedeman-radox-scala-portprosop-negru-mat-600x1200', mirror: 'dedeman-savini-due-br-80-60-oglinda-led-80x60' }, pendant: 'dedeman-smarter-boom-01-3480-suspensie-3xgu10-negru-mat', counterPendant: 'dedeman-smarter-boom-01-3480-suspensie-3xgu10-negru-mat', sconce: 'dedeman-smarter-timber-01-1663-aplica-e27-negru-fag', fixtureColor: '#1c1c1c', pendantTable: true, trackLiving: true, bedSconces: true, counterPendants: false },
  { id: 'classic', floor: { dry: 'parchet-classen-herringbone-ve96', dryLayout: { pattern: 'herringbone' }, wet: 'gresie-mckinley', wetLayout: { pattern: 'diagonal' } }, walls: '#efe9df', ceiling: '#fbfaf6', paint: 'vopsea-savana-super', wallTile: 'faianta-lumiere', baseboard: 'plinta-mdf-90', light: 'lampa-virrmo',
    accentLiving: [{ kind: 'panel', material: 'riflaj-mdf-unic-alb', color: '#e6dfd2', heightM: 1 }, { kind: 'rail', material: 'dedeman-bagheta-polimer-dur-wood-class-chp2040-alb-200cm', color: '#f3f1ec', heightM: 1 }, { kind: 'paint', material: 'vopsea-savana-super', color: '#d6c8b0' }],
    accentBed: [{ kind: 'wallpaper', material: 'tapet-grandeco-marmor' }], hallWainscot: [{ kind: 'panel', material: 'riflaj-mdf-unic-alb', color: '#e6dfd2', heightM: 1 }, { kind: 'rail', material: 'dedeman-bagheta-polimer-dur-wood-class-chp2040-alb-200cm', color: '#f3f1ec', heightM: 1 }],
    cornice: 'cornisa-nmc-nc109', door: 'usa-megadoor-clasic-alb-87', handle: 'maner-sterk-sm1703-inox', curtain: 'draperie-pelarkorsbar-floral', sheer: 'perdea-bergnejlika-alb', blind: null,
    rugLiving: 'covor-arende-alb-200x300', rugBed: 'covor-bronden-170x240',
    kitchen: { frontColor: '#e9e4d8', frontFinish: 'matt', handle: 'knob', handleColor: '#b08d57', upper: 'closed', countertop: 'ikea-ekbacken-blat-alb-aspect-marmura-246x2-8', countertopColor: '#e0deda', countertopMm: 28, backsplash: 'tile', sink: 'dedeman-franke-bsg-611-78-chiuveta-fragranit-avena-bej', tap: 'dedeman-grohe-minta-31375gn0-bucatarie-auriu-mat' },
    bath: { metal: 'brass', tileZone: 'h120', showerType: 'cabin', wc: 'floor', tap: 'ikea-runskar-baterie-lavoar-alama', shower: 'dedeman-kadda-girard-my2061-77g-sistem-dus-auriu', towelRadiator: 'dedeman-radox-scala-portprosop-auriu-600x1200', mirror: 'ikea-faxalven-oglinda-iluminat-60x95' }, pendant: 'ikea-stockholm-2025-lustra-sticla-alamit-38', fixtureColor: '#b08d57', pendantTable: true, trackLiving: false, bedSconces: true, counterPendants: false },
];
export const styleById = (id: string) => STYLE_PACKAGES.find(s => s.id === id) ?? null;

const WET = new Set(['baie', 'bucatarie']);
/** Peretele lângă care stă o piesă (cel mai apropiat de amprenta ei). */
export function wallBehind(snap: Snapshot, cat: Catalog, room: Room, group: string): Side | null {
  const p = snap.placements.find(x => x.roomId === room.id && x.group === group); return p && footprintOf(cat, p) ? backSide(p.rotation) : null;
}
const bands = (side: Side, xs: Accent[]): WallFeature[] => { let from = 0;
  return xs.map(a => { const w: WallFeature = { side, kind: a.kind, material: a.material, ...(a.color ? { color: a.color } : {}), ...(from > 0 && a.kind !== 'rail' ? { fromM: from } : {}), ...(a.heightM ? { heightM: a.heightM } : {}) }; if (a.kind !== 'rail') from = a.heightM ?? from; return w; }); };
/** Storul potrivit: cel mai îngust care acoperă fereastra (sau cel mai lat din serie). */
function blindFor(mc: MaterialsCatalog, family: string, widthM: number): string | null {
  const xs = mc.materials.filter(m => m.category === 'blind' && m.id.includes(family) && m.specs?.sizeCm).sort((a, b) => a.specs!.sizeCm![0] - b.specs!.sizeCm![0]);
  return (xs.find(m => m.specs!.sizeCm![0] / 100 >= widthM - .05) ?? xs.at(-1))?.id ?? null;
}

/** Aplică pachetul pe toată casa (toate nivelurile). Întoarce un snapshot nou; mobilierul rămâne neatins. */
export function applyStyle(snap: Snapshot, cat: Catalog, mc: MaterialsCatalog, pkg: StylePackage): Snapshot {
  const s = structuredClone(snap), has = (id: string | null | undefined): id is string => !!id && !!materialOf(mc, id), finishes: Record<string, RoomFinishes> = {};
  const rooms: Record<string, { walls?: { color: string }; ceiling?: { color: string } }> = {};
  for (const fl of floors(s)) for (const room of fl.rooms){
    const wet = WET.has(room.type), d = defaultFinishes(room), f: RoomFinishes = { ...d };
    const floorId = wet ? pkg.floor.wet : pkg.floor.dry, lay = wet ? pkg.floor.wetLayout : pkg.floor.dryLayout;
    if (has(floorId)){ f.floor = floorId; if (lay) f.floorLayout = lay; }
    if (has(pkg.paint)) f.wallPaint = pkg.paint; if (has(pkg.light)) f.light = pkg.light;
    if (wet && has(pkg.wallTile)) f.wallTile = pkg.wallTile;
    if (!wet) f.baseboard = has(pkg.baseboard) ? pkg.baseboard : d.baseboard ?? null;
    const feats: WallFeature[] = [], add = (side: Side | null, xs?: Accent[]) => { if (side && xs) feats.push(...bands(side, xs.filter(a => has(a.material)))); };
    if (room.type === 'living') add(wallBehind(s, cat, room, 'canapea'), pkg.accentLiving);
    if (room.type === 'dormitor') add(wallBehind(s, cat, room, 'pat'), pkg.accentBed);
    if (room.type === 'hol' && pkg.hallWainscot) for (const side of ['N', 'S', 'E', 'W'] as const) add(side, pkg.hallWainscot);
    if (feats.length) f.wallFeatures = feats;
    if (room.type === 'living' && pkg.ceilingLiving) f.ceiling = { ...pkg.ceilingLiving, ...(pkg.ceilingLiving.led && !has(pkg.ceilingLiving.led) ? { led: null } : {}), ...(pkg.ceilingLiving.spot && !has(pkg.ceilingLiving.spot) ? { spot: null, spots: 0 } : {}) };
    if (pkg.cornice && has(pkg.cornice) && !wet) f.ceiling = { type: 'flat', ...(f.ceiling || {}), cornice: pkg.cornice };
    // textile pe ferestrele camerelor de locuit
    if (room.type === 'living' || room.type === 'dormitor'){ const win: WindowTreatment[] = roomWindows(fl, room).map(w => ({ openingId: w.openingId,
        ...(has(pkg.curtain) ? { curtain: pkg.curtain } : {}), ...(has(pkg.sheer) ? { sheer: pkg.sheer } : {}), ...(pkg.blind ? { blind: blindFor(mc, pkg.blind, w.widthM) } : {}) }));
      if (win.length) f.windows = win;
      const rug = room.type === 'living' ? pkg.rugLiving : pkg.rugBed; if (has(rug)) f.rug = { material: rug }; }
    // produsele care lipsesc din catalog rămân neprecizate (null), nu se inventează
    const only = <T extends object>(o: T, keys: (keyof T)[]): T => ({ ...o, ...Object.fromEntries(keys.filter(k => o[k] != null && !has(o[k] as string)).map(k => [k, null])) });
    if (room.type === 'bucatarie') f.kitchen = only(pkg.kitchen, ['countertop', 'sink', 'tap', 'underLed']);
    if (room.type === 'baie') f.bath = only(pkg.bath, ['tap', 'shower', 'towelRadiator', 'mirror']);
    const fx: LightFixture[] = [], here = (g: string) => s.placements.some(p => p.roomId === room.id && p.group === g), color = pkg.fixtureColor;
    const prod = (id?: string) => has(id) ? { material: id } : {};
    if (pkg.pendantTable && here('masa')) fx.push({ kind: 'pendant', anchor: 'table', color, ...prod(pkg.pendant) });
    if (pkg.trackLiving && room.type === 'living') fx.push({ kind: 'track', anchor: 'center', lengthM: 2, color });
    if (pkg.bedSconces && here('pat')) fx.push({ kind: 'sconce', anchor: 'bed', color, ...prod(pkg.sconce) });
    if (pkg.counterPendants && here('bucatarie') && room.type === 'bucatarie') fx.push({ kind: 'pendant', anchor: 'counter', color, ...prod(pkg.counterPendant) });
    if (fx.length) f.fixtures = fx;
    finishes[room.id] = f; rooms[room.id] = { walls: { color: pkg.walls }, ceiling: { color: pkg.ceiling } };
  }
  s.finishes = finishes;
  s.appearance = { ...(s.appearance || {}), rooms: { ...(s.appearance?.rooms || {}), ...Object.fromEntries(Object.entries(rooms).map(([k, v]) => [k, { ...(s.appearance?.rooms?.[k] || {}), ...v }])) } };
  // ușile de interior și mânerele lor (ușa de intrare rămâne cum e)
  if (has(pkg.door)){ const doors = { ...(s.doors || {}) }; for (const o of doorOpenings(s)) if (!o.entrance) doors[o.id] = { product: pkg.door, handle: has(pkg.handle) ? pkg.handle : null }; s.doors = doors; }
  return s;
}
/** Cât costă fiecare stil pe casa asta: materialele de finisaj, iluminat și textile, manopera estimată și câte poziții rămân fără preț. */
export function styleCosts(snap: Snapshot, cat: Catalog, mc: MaterialsCatalog){
  return STYLE_PACKAGES.map(pkg => { const b = computeBOQ(applyStyle(snap, cat, mc, pkg), cat, mc), mine = b.items.filter(i => i.category === 'finishes' || i.category === 'lighting' || i.category === 'textiles');
    return { id: pkg.id, materials: Math.round(mine.reduce((a, i) => a + (i.total ?? 0), 0)), labor: Math.round(b.labor.reduce((a, l) => a + l.expected, 0)), unknown: b.unknown.length }; });
}
