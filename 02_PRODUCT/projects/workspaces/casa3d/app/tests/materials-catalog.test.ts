// Catalogul de materiale: fiecare produs are sursă, preț, ambalaj coerent și specificații de tipul așteptat.
import { test } from 'vitest';
import assert from 'node:assert/strict';
import materials from '../data/materials.v1.json';
import evidence from '../data/materials-evidence.2026-10-11.json';
import type { MaterialsCatalog } from '../core/types';

const mc = materials as unknown as MaterialsCatalog;
test('id-uri unice, prețuri pozitive, surse https, ambalaj coerent', () => {
  const ids = mc.materials.map(m => m.id); assert.equal(new Set(ids).size, ids.length);
  for (const m of mc.materials){ assert.ok(m.unitPrice > 0, m.id); assert.ok(['m2', 'ml', 'L', 'kg', 'buc'].includes(m.unit), m.id);
    if (m.sourceUrl) assert.match(m.sourceUrl, /^https:\/\/(www\.)?(dedeman\.ro|ikea\.com|jysk\.ro|mobexpert\.ro)\//, m.id);
    if (m.pack){ assert.ok(m.pack.size > 0, m.id); if (m.pack.price != null) assert.ok(Math.abs(m.pack.price - m.pack.size * m.unitPrice) / m.pack.price < .06, `${m.id}: ${m.pack.price} vs ${m.pack.size} × ${m.unitPrice}`); }
    const s = m.specs || {}; if (s.cctK != null) assert.equal(typeof s.cctK, 'number', m.id); if (s.color) assert.match(s.color, /^#[0-9a-f]{6}$/, m.id);
    if (s.sizeCm) assert.ok(s.sizeCm.length === 2 && s.sizeCm.every(x => x > 0), m.id); }
});
test('produsele noi au dovadă (preț + specificații citite de pe pagină)', () => {
  const ev = new Map((evidence as any).items.map((x: any) => [x.id, x]));
  for (const m of mc.materials.filter(x => x.verifiedAt === '2026-10-11')){ const e: any = ev.get(m.id); assert.ok(e && e.sourceUrl === m.sourceUrl && e.evidence, m.id); }
  for (const c of ['bath_tap', 'shower_set', 'towel_radiator', 'led_mirror', 'kitchen_sink', 'kitchen_tap', 'countertop', 'pendant', 'wall_light', 'moulding'] as const)
    assert.ok(mc.materials.filter(m => m.category === c).length >= 4, c);
  for (const m of mc.materials.filter(x => x.category === 'led_mirror')) assert.equal(m.specs?.ip, 'IP44', m.id);
});
