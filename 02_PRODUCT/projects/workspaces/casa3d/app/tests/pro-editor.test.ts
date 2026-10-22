import { test } from 'vitest';
import assert from 'node:assert/strict';
import catalogJson from '../data/catalog.v1.json';
import materialsJson from '../data/materials.v1.json';
import type { Catalog, MaterialsCatalog } from '../core/types';
import { newSnapshot } from '../core/project';
import { splitRoom, mergeRooms } from '../core/room-edit';
import { scaleFromPoints, traceRoomFromUnderlay } from '../core/underlay';
import { STYLE_PACKAGES, previewStyle, applyStyleScoped, createCustomStylePackage } from '../core/styles';
import { createParametricItem, saveToUserLibrary, instantiateFromLibrary, validateGlbAsset } from '../core/user-library';
import { suggestTechPoints, placeTechPoint, assignSwitchGroup, buildTechnicalSchedule } from '../core/technical';
import { customSurfaceSettingOut } from '../core/setting-out';
import { reviewProjectCoordination } from '../core/coordination-review';
import { snapshotToTwin } from '../lib/twin';
import { checkSnapshot } from '../lib/repo';

const cat = catalogJson as unknown as Catalog;
const mc = materialsJson as unknown as MaterialsCatalog;

function buildValidGlbBytes(jsonObj: object): Uint8Array {
  const raw = new TextEncoder().encode(JSON.stringify(jsonObj));
  const pad = (4 - (raw.byteLength % 4)) % 4;
  const chunkLen = raw.byteLength + pad;
  const totalLen = 12 + 8 + chunkLen;
  const out = new Uint8Array(totalLen);
  const dv = new DataView(out.buffer);
  dv.setUint32(0, 0x46546c67, true); // 'glTF'
  dv.setUint32(4, 2, true); // version 2
  dv.setUint32(8, totalLen, true);
  dv.setUint32(12, chunkLen, true);
  dv.setUint32(16, 0x4e4f534a, true); // 'JSON'
  out.set(raw, 20);
  for (let i = 0; i < pad; i++) out[20 + raw.byteLength + i] = 0x20;
  return out;
}

test('Sec. 14.1 — proiect nou de la zero (empty), trasare din releveu calibrat și împărțire/unire camere (splitRoom / mergeRooms)', () => {
  const snap = newSnapshot(cat, 'Proiect de la zero', 'empty');
  assert.equal(snap.floor.rooms.length, 0);
  assert.equal(snap.floor.walls.length, 0);
  assert.equal(snap.placements.length, 0);

  // Releveul neconfirmat este respins la conversia în pereți autoritativi
  const unconfirmedUnderlay = {
    dataUrl: 'data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==',
    x: 0,
    z: 0,
    widthM: 10,
    opacity: 0.7,
    locked: true,
    confirmed: false,
  };
  assert.throws(() => traceRoomFromUnderlay(unconfirmedUnderlay, { x0: 0, z0: 0, x1: 6, z1: 4 }, { id: 'r1', name: 'Open Space', type: 'living' }), /UNDERLAY_SCALE_UNCONFIRMED/);

  // Calibrare din două puncte și confirmare
  const calibratedWidth = scaleFromPoints([0, 0], [3, 0], 600, 10);
  assert.equal(calibratedWidth, 20);
  const confirmedUnderlay = { ...unconfirmedUnderlay, widthM: calibratedWidth!, confirmed: true };
  const traced = traceRoomFromUnderlay(confirmedUnderlay, { x0: 0, z0: 0, x1: 6, z1: 4 }, { id: 'r1', name: 'Open Space', type: 'living' });
  snap.floor.rooms.push(traced.room);
  snap.floor.walls.push(...traced.walls);

  // Împărțim camera Open Space (6x4m) la x = 3.6m în Living (3.6x4m) și Bucătărie (2.4x4m)
  const splitFloor = splitRoom(snap.floor, 'r1', 'x', 3.6, { id: 'r2', name: 'Bucătărie', type: 'bucatarie' }, 'wall-part-1');
  assert.equal(splitFloor.rooms.length, 2);
  assert.deepEqual(splitFloor.rooms.find(r => r.id === 'r1')!.rect, { x0: 0, z0: 0, x1: 3.6, z1: 4 });
  assert.deepEqual(splitFloor.rooms.find(r => r.id === 'r2')!.rect, { x0: 3.6, z0: 0, x1: 6, z1: 4 });
  assert.ok(splitFloor.walls.some(w => w.id === 'wall-part-1' && !w.exterior));

  // Împărțirea prea aproape de margine (< 60 cm) este refuzată
  assert.throws(() => splitRoom(splitFloor, 'r2', 'x', 3.9, { id: 'r3', name: 'Nișă', type: 'hol' }, 'wall-part-2'));

  // Sincronizare cu Digital Twin
  const twin = snapshotToTwin({ ...snap, floor: splitFloor }, cat);
  assert.equal(twin.rooms.length, 2);

  // Unirea camerelor adiacente reface camera inițială și elimină peretele despărțitor
  const mergedFloor = mergeRooms(splitFloor, 'r1', 'r2');
  assert.equal(mergedFloor.rooms.length, 1);
  assert.deepEqual(mergedFloor.rooms[0]!.rect, { x0: 0, z0: 0, x1: 6, z1: 4 });
  assert.ok(!mergedFloor.walls.some(w => w.id === 'wall-part-1'));
});

test('Sec. 14.2 — concept pe toată locuința, excepții pe cameră, previzualizare și păstrarea excepțiilor (preserveOverrides)', () => {
  let snap = newSnapshot(cat, 'Concept Test', 'demo', 'ro', { materials: mc });
  const scandi = STYLE_PACKAGES.find(p => p.id === 'scandi')!;
  const industrial = STYLE_PACKAGES.find(p => p.id === 'industrial')!;

  // Aplicăm conceptul Scandi pe toată casa
  snap = applyStyleScoped(snap, cat, mc, scandi, { scope: { kind: 'project' } });
  assert.equal(snap.appearance?.rooms?.living?.walls?.color, scandi.walls);
  assert.equal(snap.appearance?.rooms?.baie?.walls?.color, scandi.walls);

  // Suprascriem deliberat doar baia cu un stil personalizat
  const customBathStyle = createCustomStylePackage('modern', { walls: '#123456', wetFloor: 'gresie-emarble-60x120', bathMetal: 'black' });
  snap = applyStyleScoped(snap, cat, mc, customBathStyle, { scope: { kind: 'room', roomId: 'baie' } });
  assert.equal(snap.appearance?.rooms?.baie?.walls?.color, '#123456');
  assert.equal(snap.appearance?.rooms?.living?.walls?.color, scandi.walls);

  // Previzualizarea globală raportează camerele cu finisaje personalizate
  const prev = previewStyle(snap, industrial, { kind: 'project' });
  assert.equal(prev.affectedRoomIds.length, snap.floor.rooms.length);
  assert.ok(prev.overriddenRoomIds.includes('baie'));

  // Dacă schimbăm doar livingul pe Industrial, baia își păstrează excepția (#123456)
  const livingUpdated = applyStyleScoped(snap, cat, mc, industrial, { scope: { kind: 'room', roomId: 'living' } });
  assert.equal(livingUpdated.appearance?.rooms?.living?.walls?.color, industrial.walls);
  assert.equal(livingUpdated.appearance?.rooms?.baie?.walls?.color, '#123456');
});

test('Sec. 14.3 & 14.7 — componente parametrice în biblioteca utilizatorului și instanțe independente + import GLB validat', () => {
  let snap = newSnapshot(cat, 'Library Test', 'demo');
  const customWardrobe = createParametricItem({
    id: 'lib-wardrobe-210',
    name: 'Dressing la comandă 210x62x255',
    shape: 'wardrobe',
    group: 'dulap',
    baseVariantId: 'dulap-0',
    dimensionsCm: { w: 210, d: 62, h: 255 },
    colorHex: '#d8cbb8',
    materialId: 'parchet-egger-h2099',
  });
  snap.userLibrary = saveToUserLibrary(snap.userLibrary, customWardrobe);
  assert.equal(snap.userLibrary.length, 1);

  // Plasăm două instanțe în camere diferite; modificarea celei de-a doua nu modifică nici prima instanță, nici biblioteca
  snap = instantiateFromLibrary(snap, customWardrobe, 'dormitor', 2.0, 2.0, 'pl-custom-1');
  snap = instantiateFromLibrary(snap, customWardrobe, 'hol', 1.2, 1.2, 'pl-custom-2', {
    dimensionsCm: { w: 120, d: 45, h: 255 },
    colorHex: '#445566',
  });
  const p1 = snap.placements.find(p => p.id === 'pl-custom-1')!;
  const p2 = snap.placements.find(p => p.id === 'pl-custom-2')!;
  assert.deepEqual(p1.size, { w: 210, d: 62, h: 255 });
  assert.deepEqual(p2.size, { w: 120, d: 45, h: 255 });
  assert.equal(snap.appearance?.items?.['pl-custom-1']?.color, '#d8cbb8');
  assert.equal(snap.appearance?.items?.['pl-custom-2']?.color, '#445566');
  assert.deepEqual(snap.userLibrary![0]!.dimensionsCm, { w: 210, d: 62, h: 255 });

  // Validare GLB: respinge URL-uri externe (SSRF), extensii greșite și fișiere corupte
  const glbBytes = buildValidGlbBytes({
    asset: { version: '2.0' },
    accessors: [{ min: [0, 0, 0], max: [0.85, 0.9, 0.8] }],
  });
  assert.equal(validateGlbAsset(glbBytes, { id: 'g1', name: 'Scaun', fileName: 'https://evil.example/chair.glb' }).ok, false);
  assert.equal(validateGlbAsset(glbBytes, { id: 'g1', name: 'Scaun', fileName: 'chair.obj' }).ok, false);
  assert.equal(validateGlbAsset(new Uint8Array([1, 2, 3, 4]), { id: 'g1', name: 'Scaun', fileName: 'chair.glb' }).ok, false);

  // GLB valid fără licență/sursă -> ok: true, dar measurable: false (exclus din BOQ până la confirmarea metadatelor)
  const unverifiedGlb = validateGlbAsset(glbBytes, { id: 'g-unverified', name: 'Fotoliu importat', fileName: 'armchair.glb' });
  assert.equal(unverifiedGlb.ok, true);
  if (unverifiedGlb.ok) {
    assert.deepEqual(unverifiedGlb.boundingBoxCm, { w: 85, d: 80, h: 90 });
    assert.equal(unverifiedGlb.item.measurable, false);
    assert.equal(unverifiedGlb.item.price, null);
  }

  // GLB valid cu sursă și licență -> measurable: true
  const verifiedGlb = validateGlbAsset(glbBytes, {
    id: 'g-verified',
    name: 'Fotoliu licențiat',
    fileName: 'armchair.glb',
    source: 'Producător client',
    license: 'CC-BY-4.0',
  });
  assert.equal(verifiedGlb.ok, true);
  if (verifiedGlb.ok) assert.equal(verifiedGlb.item.measurable, true);
});

test('Sec. 14.4 — planuri tehnice conceptuale: grupuri întrerupător-lumini, puncte HVAC și etichetare preliminară', () => {
  const snap = newSnapshot(cat, 'Tech Test', 'demo');
  snap.tech = suggestTechPoints(snap);
  const hvac = placeTechPoint(snap, 'hvac_split', 2.0, 2.0)!;
  const thermo = placeTechPoint(snap, 'thermostat', 2.5, 2.0)!;
  snap.tech.push(hvac, thermo);

  const livingSw = snap.tech.find(p => p.roomId === 'living' && p.kind === 'switch')!;
  const livingLights = snap.tech.filter(p => p.roomId === 'living' && p.kind === 'light_point').map(p => p.id);
  snap.tech = assignSwitchGroup(snap.tech, livingSw.id, livingLights, 'Living-Circuit-L1');

  const checked = checkSnapshot(structuredClone(snap));
  const sched = buildTechnicalSchedule(checked);
  assert.equal(sched.status, 'PRELIMINARY_CONCEPT');
  assert.ok(sched.disclaimer.includes('preliminară'));
  assert.equal(sched.byCategory.hvac.length, 2);
  assert.ok(sched.switchGroups.some(g => g.group === 'Living-Circuit-L1' && g.switchId === livingSw.id && g.lightIds.length === livingLights.length));
  assert.ok(!sched.unlinkedSwitches.includes(livingSw.id));
});

test('Sec. 14.6 & 14.8 — plan de placare cu origine/model (customSurfaceSettingOut) și panoul de coordonare (reviewProjectCoordination)', () => {
  const gridPlan = customSurfaceSettingOut({
    roomId: 'baie',
    roomName: 'Baie',
    surface: 'floor',
    material: 'Gresie 60x60',
    widthM: 2.5,
    heightM: 2.0,
    tileCm: [60, 60],
    groutMm: 2,
    pattern: 'straight',
    originCm: [15, 0],
  });
  const herringbonePlan = customSurfaceSettingOut({
    roomId: 'baie',
    roomName: 'Baie',
    surface: 'floor',
    material: 'Gresie 60x60',
    widthM: 2.5,
    heightM: 2.0,
    tileCm: [60, 60],
    groutMm: 2,
    pattern: 'herringbone',
  });
  assert.equal(gridPlan.netAreaM2, 5);
  assert.equal(gridPlan.wastePct, 10);
  assert.equal(herringbonePlan.wastePct, 15);
  assert.ok(herringbonePlan.grossAreaM2! > gridPlan.grossAreaM2!);

  const snap = newSnapshot(cat, 'Coordination Test', 'demo');
  snap.underlay = {
    dataUrl: 'data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==',
    x: 0,
    z: 0,
    widthM: 12,
    opacity: 0.5,
    locked: false,
    confirmed: false,
  };
  snap.tech = suggestTechPoints(snap).filter(p => !(p.roomId === 'baie' && p.kind === 'drain'));
  const findings = reviewProjectCoordination(snap, cat, mc);
  assert.ok(findings.some(f => f.code === 'UNDERLAY_SCALE_UNCONFIRMED' && f.severity === 'WARNING'));
  assert.ok(findings.some(f => f.code === 'TECH_SANITARY_UNCONNECTED' && f.severity === 'WARNING'));
  assert.ok(findings.some(f => f.code === 'TECH_SWITCH_UNLINKED' && f.severity === 'WARNING'));
  assert.ok(findings.some(f => f.code === 'PRELIMINARY_TECHNICAL_PLANS' && f.severity === 'INFO'));
});
