import { describe, expect, it } from 'vitest';
import { emptyTwin, rectangleRoom, type Twin } from '../src/twin';
import { createProject } from '../src/approval';
import { createShare, resolveShare, revokeShare } from '../src/share';
import { parseFeed, importFeed } from '../src/catalog-feed';

const twin = (): Twin => ({ ...emptyTwin('t'), rooms: [rectangleRoom('r', 'R', 0, 0, 4, 3)] });

describe('share', () => {
  it('shares exactly one revision, read-only, and later changes do not leak', () => {
    const p1 = createProject('p', twin(), '2026-10-10T00:00:00Z');
    const share = createShare(p1, 1, { now: '2026-10-10T00:00:00Z' });
    expect(share.token).toMatch(/^[A-Za-z0-9_-]{43}$/);
    const p2 = { ...p1, twin: { ...p1.twin, placements: [{ id: 'x', catalogId: 'c', roomId: 'r', x: 0, y: 0, w: 1, d: 1, h: 1, rotation: 0 as const }] },
      revisions: [...p1.revisions, { number: 2, twin: p1.twin, fingerprint: 'f2', createdAt: 'now' }] };
    const view = resolveShare([share], [p2], share.token);
    expect(view).not.toBeNull();
    expect(view!.revisionNumber).toBe(1);
    expect(view!.twin.placements).toEqual([]);
    expect(view!.readOnly).toBe(true);
    view!.twin.rooms.push(rectangleRoom('z', 'Z', 9, 9, 1, 1)); // mutating the view must not touch the project
    expect(p2.revisions[0]!.twin.rooms).toHaveLength(1);
  });
  it('returns null for unknown, revoked, expired tokens and missing revisions', () => {
    const p = createProject('p', twin());
    const s = createShare(p, 1, { expiresAt: '2026-10-11T00:00:00Z' });
    expect(resolveShare([s], [p], 'short')).toBeNull();
    expect(resolveShare([s], [p], s.token + 'x')).toBeNull();
    expect(resolveShare([s], [p], s.token, '2026-10-12T00:00:00Z')).toBeNull();
    expect(resolveShare([revokeShare(s)], [p], s.token, '2026-10-10T00:00:00Z')).toBeNull();
    expect(resolveShare([s], [], s.token, '2026-10-10T00:00:00Z')).toBeNull();
    expect(() => createShare(p, 7)).toThrow(RangeError);
  });
});

describe('supplier feed', () => {
  const feed = {
    supplier: 'dedeman', basis: 'supplier_file', exportedAt: '2026-10-09',
    rows: [
      { id: 'ded-1', name: 'Masa', width_m: 1.2, depth_m: 0.8, height_m: 0.75, price: 599.9, currency: 'RON', url: 'https://www.dedeman.ro/x', verifiedAt: '2026-10-09', role: 'table' },
      { id: 'ded-2', name: 'Scaun', width_m: 0.45, depth_m: 0.45, height_m: 0.9 },            // no price -> UNKNOWN
      { id: 'ded-3', name: 'Lampa', width_m: 0.2, depth_m: 0.2, height_m: 0.5, price: 99, currency: 'RON' }, // price without date -> rejected
      { id: 'ded-1', name: 'Dublura', width_m: 1, depth_m: 1, height_m: 1 },
      { id: 'bad id!', name: 'x', width_m: 1, depth_m: 1, height_m: 1 },
      { id: 'ded-4', name: 'Gigant', width_m: 50, depth_m: 1, height_m: 1 },
      'junk',
    ],
  };
  it('parses only non-scraped feeds', () => {
    expect(parseFeed({ ...feed, basis: 'scraped' }).error).toMatch(/scraping/);
    expect(parseFeed({ ...feed, exportedAt: 'yesterday' }).error).toMatch(/exportedAt/);
    expect(parseFeed(feed).feed!.supplier).toBe('dedeman');
  });
  it('accepts valid rows with provenance, keeps UNKNOWN prices and rejects the rest with reasons', () => {
    const r = importFeed(parseFeed(feed).feed!);
    expect(r.accepted.map(i => i.id)).toEqual(['ded-1', 'ded-2']);
    expect(r.accepted[0]).toMatchObject({ price: { amount: 599.9, currency: 'RON' }, retailer: 'dedeman', role: 'table',
      provenance: { source: 'feed:dedeman', verificationType: 'feed', sourceUrl: 'https://www.dedeman.ro/x', verifiedAt: '2026-10-09', confidence: 'medium' } });
    expect(r.accepted[1]!.price).toBe('UNKNOWN');
    expect(r.unknownPriceCount).toBe(1);
    expect(r.rejected.map(x => x.reason)).toEqual(['ded-3: pret fara verifiedAt', 'id duplicat: ded-1', 'id lipsa sau invalid', 'ded-4: dimensiuni lipsa sau neplauzibile', 'rand invalid']);
  });
});
