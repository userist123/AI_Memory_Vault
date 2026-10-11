// Corpul unei cereri publice se citește cu limită reală de octeți, chiar fără content-length (transfer „chunked”).
import { test } from 'vitest';
import assert from 'node:assert/strict';
import { readJsonLimited } from '../lib/body-limit';
const chunked = (chunks: string[]) => new Request('http://x/', { method: 'POST', body: new ReadableStream({ start(c){ for (const s of chunks) c.enqueue(new TextEncoder().encode(s)); c.close(); } }), duplex: 'half' } as any);

test('peste limită: 413 și fără să citească tot; în limită: JSON; JSON stricat: 400', async () => {
  let pulled = 0; const big = new Request('http://x/', { method: 'POST', body: new ReadableStream({ pull(c){ pulled++; c.enqueue(new Uint8Array(4096)); } }), duplex: 'half' } as any);
  await assert.rejects(readJsonLimited(big, 16 * 1024), (e: any) => e.status === 413); assert.ok(pulled < 10, 'citirea se oprește la limită');
  assert.equal(big.headers.get('content-length'), null);
  assert.deepEqual(await readJsonLimited(chunked(['{"a":', '1}']), 1024), { a: 1 });
  await assert.rejects(readJsonLimited(chunked(['{"a":']), 1024), (e: any) => e.status === 400);
  await assert.rejects(readJsonLimited(new Request('http://x/', { method: 'POST', body: '{}', headers: { 'content-length': '999999' } }), 1024), (e: any) => e.status === 413);
});
