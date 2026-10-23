// UI/UX P1-4: istoricul deciziilor lizibil — aprobările în bloc se grupează, deciziile singulare își arată camera.
import { test } from 'vitest';
import assert from 'node:assert/strict';
import { feedbackLog, type LogInput } from '../core/feedback-log';
const room = (id: string | null) => ({ hol: 'Hol', living: 'Living' } as Record<string, string>)[id ?? ''] ?? 'General';
const f = (id: string, o: Partial<LogInput>): LogInput => ({ id, roomId: 'hol', verdict: 'like', author: 'Ana', text: '', createdAt: '2026-10-23T01:26:10.000Z', item: { label: 'Parchet' }, ...o });

test('aprobările date împreună devin un rând cu numărul pe camere; cel mai nou primul', () => {
  const log = feedbackLog([f('1', {}), f('2', { roomId: 'living', item: { label: 'Canapea' } }), f('3', { roomId: 'living', createdAt: '2026-10-23T01:26:40.000Z' }),
    f('4', { createdAt: '2026-10-23T02:00:00.000Z', verdict: 'change', text: 'Mai deschis', item: { label: 'Vopsea' } })], room);
  assert.equal(log.length, 2);
  assert.deepEqual({ ...log[0], id: '' }, { id: '', at: '2026-10-23T02:00:00.000Z', author: 'Ana', verdict: 'change', count: 1, rooms: [{ name: 'Hol', count: 1 }], label: 'Vopsea', text: 'Mai deschis' });
  assert.equal(log[1]!.count, 3); assert.equal(log[1]!.label, null);
  assert.deepEqual(log[1]!.rooms.sort((a, b) => a.name.localeCompare(b.name)), [{ name: 'Hol', count: 1 }, { name: 'Living', count: 2 }]);
});
test('alt autor, alt minut sau un text despart grupul; părerile generale (fără element) rămân singure', () => {
  const log = feedbackLog([f('1', {}), f('2', { author: 'Ion' }), f('3', { createdAt: '2026-10-23T01:27:00.000Z' }), f('4', { text: 'Frumos' }), f('5', { item: null, roomId: null, text: 'Îmi place tot' })], room);
  assert.equal(log.length, 5); assert.ok(log.every(e => e.count <= 1));
  assert.equal(log.find(e => e.text === 'Îmi place tot')!.rooms[0]!.name, 'General');
});
