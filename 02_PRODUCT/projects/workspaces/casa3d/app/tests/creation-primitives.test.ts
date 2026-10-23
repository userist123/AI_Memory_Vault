import { test, describe, it, expect } from 'vitest';
import catalogJson from '../data/catalog.v1.json';
import { newSnapshot } from '../core/project';
import { viewerInput } from '../lib/viewer-input';
import { addLevel, floors } from '../core/levels';
import type { Catalog, Snapshot, Column, Beam, GlassWall, Stair } from '../core/types';

const cat = catalogJson as unknown as Catalog;
const base = (): Snapshot => newSnapshot(cat, 'Casa Test', 'two-bedroom', 'en');

describe('Architectural Creation Primitives', () => {
  it('creates structural columns (rectangular and circular) and feeds 3D viewer', () => {
    const s = base();
    const c1: Column = { id: 'col-1', x: 2.5, z: 3.0, w: 0.4, d: 0.4, shape: 'rect' };
    const c2: Column = { id: 'col-2', x: 4.0, z: 5.0, w: 0.35, d: 0.35, shape: 'round', rotation: 45 };
    s.floor.columns = [c1, c2];

    const { plan } = viewerInput(s, cat);
    expect(plan.stalpi).toBeDefined();
    expect(plan.stalpi).toHaveLength(2);
    expect(plan.stalpi![0]).toEqual({
      id: 'col-1',
      x: 2.5,
      z: 3.0,
      w: 0.4,
      d: 0.4,
      shape: 'rect',
    });
    expect(plan.stalpi![1]).toEqual({
      id: 'col-2',
      x: 4.0,
      z: 5.0,
      w: 0.35,
      d: 0.35,
      shape: 'round',
      rotation: 45,
    });
  });

  it('rotates rectangular columns swapping w and d', () => {
    const col: Column = { id: 'col-rect', x: 1.0, z: 2.0, w: 0.6, d: 0.3, shape: 'rect' };
    const rotated: Column = {
      ...col,
      w: col.d,
      d: col.w,
      rotation: ((col.rotation ?? 0) + 90) % 360,
    };
    expect(rotated.w).toBe(0.3);
    expect(rotated.d).toBe(0.6);
    expect(rotated.rotation).toBe(90);
  });

  it('creates ceiling beams with span and depth and feeds 3D viewer', () => {
    const s = base();
    const b1: Beam = { id: 'beam-1', a: [1.0, 2.0], b: [5.0, 2.0], width: 0.25, depth: 0.45 };
    s.floor.beams = [b1];

    const { plan } = viewerInput(s, cat);
    expect(plan.grinzi).toBeDefined();
    expect(plan.grinzi).toHaveLength(1);
    expect(plan.grinzi![0]).toEqual({
      id: 'beam-1',
      a: [1.0, 2.0],
      b: [5.0, 2.0],
      width: 0.25,
      depth: 0.45,
    });
  });

  it('creates glass partition walls with thickness and mullions', () => {
    const s = base();
    const gw: GlassWall = {
      id: 'gw-1',
      a: [0, 0],
      b: [3, 0],
      thickness: 0.08,
      mullions: 3,
    };
    s.floor.glassWalls = [gw];

    const { plan } = viewerInput(s, cat);
    expect(plan.peretiSticla).toBeDefined();
    expect(plan.peretiSticla).toHaveLength(1);
    expect(plan.peretiSticla![0]).toEqual({
      id: 'gw-1',
      a: [0, 0],
      b: [3, 0],
      thickness: 0.08,
      mullions: 3,
    });
  });

  it('preserves columns, beams, and glass walls across level operations with new IDs', () => {
    const s = base();
    s.floor.columns = [{ id: 'col-1', x: 2, z: 2, w: 0.3, d: 0.3, shape: 'rect' }];
    s.floor.beams = [{ id: 'beam-1', a: [0, 0], b: [4, 0], width: 0.2, depth: 0.4 }];
    s.floor.glassWalls = [{ id: 'gw-1', a: [1, 1], b: [3, 1], thickness: 0.08, mullions: 2 }];

    // Add level with explicit name
    const nextHouse = addLevel(s, { name: 'Etaj 1' });
    const fls = floors(nextHouse);
    expect(fls).toHaveLength(2);

    // Level 0 has original elements
    expect(fls[0].columns).toHaveLength(1);
    expect(fls[0].columns![0].id).toBe('col-1');
    expect(fls[0].beams).toHaveLength(1);
    expect(fls[0].glassWalls).toHaveLength(1);

    // Newly created upper level replicates structural elements with fresh IDs
    expect(fls[1].columns).toHaveLength(1);
    expect(fls[1].columns![0].id).not.toBe('col-1');
    expect(fls[1].columns![0].w).toBe(0.3);

    expect(fls[1].beams).toHaveLength(1);
    expect(fls[1].beams![0].id).not.toBe('beam-1');

    expect(fls[1].glassWalls).toHaveLength(1);
    expect(fls[1].glassWalls![0].id).not.toBe('gw-1');
  });

  it('handles direct stair placement on plan', () => {
    const s = base();
    const stair: Stair = {
      id: 'stair-direct',
      x: 3.2,
      z: 1.8,
      width: 0.9,
      length: 2.8,
      rotation: 0,
    };
    s.floor.stairs = [stair];

    const { plan } = viewerInput(s, cat);
    expect(plan.scari).toHaveLength(1);
    expect(plan.scari[0].id).toBe('stair-direct');
    expect(plan.scari[0].x).toBe(3.2);
    expect(plan.scari[0].z).toBe(1.8);
    expect(plan.scari[0].w).toBe(0.9);
    expect(plan.scari[0].l).toBe(2.8);
    expect(plan.scari[0].steps).toBeGreaterThan(0);
  });
});
