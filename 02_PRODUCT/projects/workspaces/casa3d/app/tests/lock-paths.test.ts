// Căile pe care o piesă blocată (`locked: true`) era totuși ștearsă, înlocuită sau își pierdea marcajele: mobilarea automată
// (core/project.ts, cu apelanții din Editor și core/proposal.ts), amenajarea de la redimensionarea camerei, geamănul de design
// (lib/twin.ts, lib/design.ts), umbra de contact a piesei oglindite în 3D și butoanele din inspector.
import { test, beforeAll } from 'vitest';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import path from 'node:path';
// @ts-expect-error three nu are tipuri în acest proiect
import * as THREE from 'three';
process.env.PGLITE_MEMORY = '1'; delete process.env.DATABASE_URL;
import catalogJson from '../data/catalog.v1.json';
import materialsJson from '../data/materials.v1.json';
import { resetDbForTests } from '../lib/db';
import * as repo from '../lib/repo';
import * as design from '../lib/design';
import { snapshotToTwin, placementsFromTwin } from '../lib/twin';
import { autoLayout, newSnapshot } from '../core/project';
import { evaluateVariant } from '../core/proposal';
import { templateIds } from '../core/templates';
import { hasPinnedPieces, toggleLock } from '../core/lock';
import { contactQuadIndices } from '../core/mirror';
import { validatePlacement, footprintOf } from '../core/validate';
import { rectHit } from '../core/geometry';
import type { Catalog, FurniturePlacement, MaterialsCatalog, Snapshot } from '../core/types';

beforeAll(() => resetDbForTests());
const cat = catalogJson as unknown as Catalog, mc = materialsJson as unknown as MaterialsCatalog;
const OWNER = '66666666-6666-4666-8666-666666666666';
const EDITOR = readFileSync(path.join(import.meta.dirname, '../components/Editor.tsx'), 'utf8');
const ENGINE = readFileSync(path.join(import.meta.dirname, '../components/viewer3d-engine.js'), 'utf8');

/** Un proiect mobilat automat care are un dormitor cu pat. */
function furnished(): { snap: Snapshot; bed: FurniturePlacement } {
  for (const id of templateIds()){ const snap = newSnapshot(cat, 'Blocare', id);
    const bed = snap.placements.find(p => p.group === 'pat' && snap.floor.rooms.find(r => r.id === p.roomId)?.type === 'dormitor');
    if (bed) return { snap, bed }; }
  throw new Error('niciun șablon cu dormitor și pat');
}
const lockPiece = (snap: Snapshot, id: string, patch: Partial<FurniturePlacement> = {}): Snapshot =>
  ({ ...snap, placements: snap.placements.map(p => p.id === id ? { ...p, ...patch, locked: true as const } : p) });
/** Piesele (altele decât `l`) care se suprapun nepermis cu piesa `l`, după validatorul aplicației. */
const overlapping = (snap: Snapshot, l: FurniturePlacement) => snap.placements.filter(p => p.id !== l.id && p.roomId === l.roomId
  && validatePlacement(snap, cat, p).some(i => i.code === 'OVERLAP' && i.with === l.id));

test('mobilarea automată a întregii case: piesa blocată rămâne identică și nu apare o a doua piesă în locul ei', () => {
  const { snap, bed } = furnished(), s = lockPiece(snap, bed.id, { mirrored: true }), mine = s.placements.find(p => p.id === bed.id)!;
  const r = autoLayout(s, cat), after = r.snapshot.placements.find(p => p.id === bed.id);
  assert.deepEqual(after, mine, 'id, poziție, rotație, variantă, oglindire și blocare neschimbate');
  assert.equal(r.snapshot.placements.filter(p => p.roomId === bed.roomId && p.group === 'pat').length, 1, 'patul blocat ține locul patului generat');
  assert.deepEqual(overlapping(r.snapshot, mine).map(p => p.group), [], 'nicio piesă generată peste piesa blocată');
  assert.ok(Array.isArray(r.displaced));
  assert.ok(r.snapshot.placements.some(p => p.roomId !== bed.roomId && p.source === 'auto'), 'restul casei e mobilat');
});

test('mobilarea automată a unei camere: o piesă blocată pusă în locul patului e obstacol fix, celelalte camere rămân neatinse', () => {
  const { snap, bed } = furnished();
  // o măsuță blocată chiar unde motorul pune patul: patul generat nu are voie peste ea
  const table: FurniturePlacement = { id: 'masuta-blocata', roomId: bed.roomId, group: 'masuta', variantId: 'masuta-0', x: bed.x, z: bed.z, rotation: 0, source: 'manual', locked: true, mirrored: true };
  const s: Snapshot = { ...snap, placements: [...snap.placements.filter(p => p.id !== bed.id), table] };
  for (const r of [autoLayout(s, cat, { roomId: bed.roomId }), autoLayout(s, cat)]){
    assert.deepEqual(r.snapshot.placements.find(p => p.id === table.id), table);
    const tfp = footprintOf(cat, table)!;
    for (const p of r.snapshot.placements.filter(p => p.roomId === bed.roomId && p.id !== table.id))
      assert.equal(rectHit(footprintOf(cat, p)!, tfp), false, `${p.group} stă peste măsuța blocată`);
    assert.deepEqual(overlapping(r.snapshot, table), []);
    // ce nu a mai avut loc e raportat cu grupa și camera, nu ascuns
    for (const d of r.displaced){ assert.equal(typeof d.key, 'string'); assert.equal(d.room, snap.floor.rooms.find(x => x.id === bed.roomId)!.name); }
  }
  const one = autoLayout(s, cat, { roomId: bed.roomId });
  assert.deepEqual(one.snapshot.placements.filter(p => p.roomId !== bed.roomId), s.placements.filter(p => p.roomId !== bed.roomId));
});

test('mobilarea automată fără piese blocate dă același rezultat ca înainte (nimic scos)', () => {
  const { snap } = furnished(), r = autoLayout(snap, cat);
  assert.deepEqual(r.displaced, []);
  const key = (p: FurniturePlacement) => `${p.roomId}:${p.variantId}:${p.x}:${p.z}:${p.rotation}`;
  assert.deepEqual(r.snapshot.placements.map(key).sort(), snap.placements.map(key).sort());
});

test('aplicarea unei propuneri de design (core/proposal.ts) păstrează piesa blocată', () => {
  const { snap, bed } = furnished(), s = lockPiece(snap, bed.id, { mirrored: true }), mine = s.placements.find(p => p.id === bed.id)!;
  const ev = evaluateVariant(s, cat, mc, { tier: 'echilibrat', title: 'T', summary: '', palette: [], selections: {}, finishes: {}, reasons: [] });
  assert.deepEqual(ev.candidate.placements.find(p => p.id === bed.id), mine);
  assert.equal(ev.candidate.placements.filter(p => p.roomId === bed.roomId && p.group === 'pat').length, 1);
});

test('redimensionarea camerei: piesa blocată contează ca piesă pusă de mână (camera nu se reamenajează), fără să i se schimbe sursa', () => {
  const { snap, bed } = furnished(), room = bed.roomId, autoOnly = { ...snap, placements: snap.placements.filter(p => p.source === 'auto') };
  assert.equal(hasPinnedPieces(autoOnly, room), false);
  const locked = toggleLock(autoOnly, [bed.id]).snapshot;
  assert.equal(hasPinnedPieces(locked, room), true);
  assert.equal(locked.placements.find(p => p.id === bed.id)!.source, 'auto', 'blocarea nu rescrie sursa: deblocarea readuce piesa exact cum era');
  assert.equal(hasPinnedPieces(toggleLock(locked, [bed.id]).snapshot, room), false);
  assert.equal(hasPinnedPieces({ placements: [{ ...bed, source: 'manual' }] }, room), true);
  assert.equal(hasPinnedPieces(locked, 'alta-camera'), false);
  // ambele căi din editor (mutarea pereților și lățimea/lungimea din panou) folosesc aceeași regulă
  assert.equal(EDITOR.match(/const hasManual = hasPinnedPieces\(/g)?.length, 2);
  assert.equal(/const hasManual = [^;]*source === 'manual'/.test(EDITOR), false);
});

test('geamănul de design: dus-întors păstrează blocarea și oglindirea; piesa blocată nu e mutată, înlocuită sau scoasă', () => {
  const { snap, bed } = furnished(), other = snap.placements.find(p => p.id !== bed.id && p.roomId === bed.roomId)!;
  const s: Snapshot = { ...lockPiece(snap, bed.id, { mirrored: true }), }; s.placements = s.placements.map(p => p.id === other.id ? { ...p, mirrored: true as const } : p);
  const mine = s.placements.find(p => p.id === bed.id)!, twin = snapshotToTwin(s, cat);
  const back = placementsFromTwin(s, cat, twin);
  assert.deepEqual(back.find(p => p.id === bed.id), mine);
  assert.equal(back.find(p => p.id === other.id)!.mirrored, true, 'oglindirea unei piese neblocate se păstrează');
  assert.equal('locked' in back.find(p => p.id === other.id)!, false);
  // solver-ul mută și înlocuiește piesa blocată (MOVE / REPLACE): în proiect rămâne cum era
  const alt = cat.variants.find(v => v.id !== mine.variantId && v.id.startsWith('pat-'))!.id;
  const moved = { ...twin, placements: twin.placements.map(q => q.id === bed.id ? { ...q, x: q.x + 0.5, rotation: ((q.rotation + 90) % 360) as typeof q.rotation, catalogId: alt } : q) };
  assert.deepEqual(placementsFromTwin(s, cat, moved).find(p => p.id === bed.id), mine);
  // solver-ul o scoate (REMOVE): rămâne
  const removed = { ...twin, placements: twin.placements.filter(q => q.id !== bed.id) }, kept = placementsFromTwin(s, cat, removed);
  assert.deepEqual(kept.filter(p => p.id === bed.id), [mine]);
  assert.equal(kept.length, s.placements.length);
});

test('designul cu „înlocuiește”: piesele blocate din camera vizată nu se șterg', async () => {
  const { snap, bed } = furnished(), s = lockPiece(snap, bed.id);
  const base = design.baseFor(s, { roomId: bed.roomId, replace: true });
  assert.deepEqual(base.placements.filter(p => p.roomId === bed.roomId).map(p => p.id), [bed.id]);
  assert.equal(design.baseFor(s, { roomId: bed.roomId, replace: false }), s);
  // cap-coadă, pe proiectul demo: dulapul blocat și oglindit din dormitor rămâne în toate variantele
  const id = await repo.createProject(OWNER, 'Blocat', 'demo'), p = await repo.getProject(OWNER, id);
  const wd = p.draft.placements.find(q => q.roomId === 'dormitor' && q.group === 'dulap')!;
  const draft = { ...p.draft, placements: p.draft.placements.map(q => q.id === wd.id ? { ...q, locked: true as const, mirrored: true as const } : q) };
  await repo.saveDraft(OWNER, id, draft);
  const saved = (await repo.getProject(OWNER, id)).draft.placements.find(q => q.id === wd.id)!;
  assert.equal(saved.locked, true);
  const r = await design.generateDesign(OWNER, id, { roomId: 'dormitor', wants: ['pat', 'noptiera'], replace: true });
  assert.equal(r.alternatives.length, 3);
  for (const a of r.alternatives.filter(x => x.placements.length)){
    assert.deepEqual(a.placements.find(q => q.id === wd.id), saved, `${a.title}: dulapul blocat a rămas`);
    assert.equal(a.placements.filter(q => q.roomId === 'dormitor' && q.group === 'dulap').length, 1);
  }
  assert.ok(r.alternatives.some(x => x.placements.length), 'cel puțin o variantă se poate așeza în jurul dulapului blocat');
});

test('3D: umbra de contact a piesei oglindite privește în sus (nu e eliminată de materialul cu o singură față)', () => {
  const corners = [[-.6, -.4], [.6, -.4], [.6, .4], [-.6, .4]] as const;
  const up = (mirrored: boolean, rot: number) => { const m = new THREE.Object3D(); m.position.set(2, 0, 3); m.rotation.y = rot; if (mirrored) m.scale.x = -1; m.updateMatrixWorld(true);
    const flipped = m.matrixWorld.determinant() < 0; assert.equal(flipped, mirrored);
    const pts = corners.map(([x, z]) => new THREE.Vector3(x, 0, z).applyMatrix4(m.matrixWorld)), idx = contactQuadIndices(0, flipped);
    return [0, 3].map(k => new THREE.Vector3().subVectors(pts[idx[k + 1]!]!, pts[idx[k]!]!).cross(new THREE.Vector3().subVectors(pts[idx[k + 2]!]!, pts[idx[k]!]!)).y); };
  for (const rot of [0, Math.PI / 2, 1.1, Math.PI]) for (const mirrored of [false, true]) for (const ny of up(mirrored, rot)) assert.ok(ny > 0, `rot ${rot}, oglindit ${mirrored}: normala ${ny}`);
  // piesa neoglindită păstrează exact indicii de până acum (hash-urile de aur nu se schimbă)
  assert.deepEqual(contactQuadIndices(8, false), [8, 10, 9, 8, 11, 10]);
  assert.deepEqual(contactQuadIndices(8, true), [8, 9, 10, 8, 10, 11]);
  assert.ok(ENGINE.includes('idx.push(...contactQuadIndices(base, m.matrixWorld.determinant() < 0))'), 'motorul 3D folosește ordinea după determinant');
  assert.equal(ENGINE.includes('idx.push(base, base + 2, base + 1, base, base + 3, base + 2)'), false);
});

test('inspector: Oglindește e dezactivat pe piesa blocată, iar mutarea din câmpuri e refuzată cu mesajul de blocare', () => {
  const panel = EDITOR.slice(EDITOR.indexOf('function PlacementPanel('));
  const mirrorBtn = panel.match(/<button className="btn"([^>]*)onClick=\{onMirror\}/)?.[1] ?? '';
  assert.ok(mirrorBtn.includes('disabled={p.locked === true}'), mirrorBtn);
  const onMove = EDITOR.match(/onMove=\{\(x, z\) => \{([^\n]*)/)?.[1] ?? '';
  assert.ok(/^\s*if \(isLocked\(snap, selPl\.id\)\)\{ say\(t\('edit\.locked'\)\); return; \}/.test(onMove), onMove.slice(0, 120));
});
