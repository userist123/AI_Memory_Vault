// Planul de la tastatură (audit UI F-26): Tab alege, săgețile mută, R rotește, Delete șterge, Tab iese după ultimul element.
import { test } from 'vitest';
import assert from 'node:assert/strict';
import { planItems, cycleSelection, planKeyAction, MOVE_CM, MOVE_BIG_CM } from '../core/plan-keys';

const snap: any = { floor: { rooms: [{ id: 'b', rect: { x0: 3, z0: 0, x1: 6, z1: 3 } }, { id: 'a', rect: { x0: 0, z0: 0, x1: 3, z1: 3 } }, { id: 'c', rect: { x0: 0, z0: 3, x1: 6, z1: 5 } }],
  stairs: [{ id: 's1', x: 5, z: 4 }] }, placements: [{ id: 'p2', x: 4, z: 1 }, { id: 'p1', x: 1, z: 1 }] };

test('ordinea Tab: camerele, apoi piesele, apoi scările, fiecare în ordinea citirii', () => {
  assert.deepEqual(planItems(snap).map(i => i.id), ['a', 'b', 'c', 'p1', 'p2', 's1']);
});
test('Tab / Shift+Tab trec prin elemente; după ultimul (sau înainte de primul) focusul iese din plan', () => {
  const it = planItems(snap);
  assert.equal(cycleSelection(it, null, 1)!.id, 'a'); assert.equal(cycleSelection(it, null, -1)!.id, 's1');
  assert.equal(cycleSelection(it, { kind: 'room', id: 'c' }, 1)!.id, 'p1'); assert.equal(cycleSelection(it, { kind: 'placement', id: 'p1' }, -1)!.id, 'c');
  assert.equal(cycleSelection(it, { kind: 'stair', id: 's1' }, 1), null); assert.equal(cycleSelection(it, { kind: 'room', id: 'a' }, -1), null);
  assert.equal(cycleSelection([], null, 1), null);
  assert.deepEqual(planKeyAction({ key: 'Tab' }, it, null), { type: 'select', item: it[0] });
  assert.deepEqual(planKeyAction({ key: 'Tab', shiftKey: true }, it, { kind: 'placement', id: 'p1' }), { type: 'select', item: it[2] });
  assert.deepEqual(planKeyAction({ key: 'Tab' }, it, { kind: 'stair', id: 's1' }), { type: 'leave' });
  assert.equal(cycleSelection(it, { kind: 'wall', id: 'w' }, 1)!.id, 'a', 'o selecție care nu e în listă (perete) pornește de la primul');
});
test('săgețile mută cu 5 cm (Shift: 50 cm), R rotește, Delete și Backspace șterg, Escape deselectează', () => {
  const it = planItems(snap), p = { kind: 'placement', id: 'p1' };
  assert.deepEqual(planKeyAction({ key: 'ArrowRight' }, it, p), { type: 'move', dxCm: MOVE_CM, dzCm: 0 });
  assert.deepEqual(planKeyAction({ key: 'ArrowUp', shiftKey: true }, it, p), { type: 'move', dxCm: 0, dzCm: -MOVE_BIG_CM });
  assert.deepEqual(planKeyAction({ key: 'r' }, it, p), { type: 'rotate' }); assert.deepEqual(planKeyAction({ key: 'R' }, it, { kind: 'stair', id: 's1' }), { type: 'rotate' });
  assert.equal(planKeyAction({ key: 'r' }, it, { kind: 'room', id: 'a' }), null, 'o cameră nu se rotește');
  assert.deepEqual(planKeyAction({ key: 'Delete' }, it, p), { type: 'delete' }); assert.deepEqual(planKeyAction({ key: 'Backspace' }, it, p), { type: 'delete' });
  assert.deepEqual(planKeyAction({ key: 'Escape' }, it, p), { type: 'deselect' });
});
test('fără selecție, săgețile, R și Delete nu fac nimic; combinațiile cu Ctrl rămân scurtăturile editorului', () => {
  const it = planItems(snap);
  for (const key of ['ArrowLeft', 'r', 'Delete', 'Escape']) assert.equal(planKeyAction({ key }, it, null), null, key);
  assert.equal(planKeyAction({ key: 'ArrowLeft' }, it, { kind: 'placement', id: 'dispărut' }), null);
  assert.equal(planKeyAction({ key: 'z', ctrlKey: true }, it, { kind: 'placement', id: 'p1' }), null);
});
