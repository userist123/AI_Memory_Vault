// Pe http simplu (telefonul din aceeași rețea) browserul nu are crypto.randomUUID: randomId() trebuie să dea tot un UUID v4 valid.
import { test } from 'vitest';
import assert from 'node:assert/strict';
import { randomId } from '../core/uuid';
const V4 = /^[0-9a-f]{8}-[0-9a-f]{4}-4[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/;

test('randomId: UUID v4 cu și fără crypto.randomUUID (context nesecurizat), fără repetări', () => {
  assert.match(randomId(), V4);
  const real = globalThis.crypto, desc = Object.getOwnPropertyDescriptor(globalThis, 'crypto')!;
  Object.defineProperty(globalThis, 'crypto', { configurable: true, value: { getRandomValues: real.getRandomValues.bind(real) } });
  try { const ids = new Set(Array.from({ length: 2000 }, () => randomId())); assert.equal(ids.size, 2000); for (const id of ids) assert.match(id, V4); }
  finally { Object.defineProperty(globalThis, 'crypto', desc); }
  assert.equal(typeof globalThis.crypto.randomUUID, 'function', 'crypto real restaurat');
});
