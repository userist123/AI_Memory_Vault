import { describe, it, expect } from 'vitest';
import { VIEW_PRESETS, presetPose, orthoFrustum, panTarget, viewKeyFor, boundsCenter, type Bounds, type ViewId, type Vec3 } from '../core/view-presets';

const B: Bounds = { minX: -3, maxX: 9, minZ: 1, maxZ: 7, height: 2.8 };
const IDS: ViewId[] = ['top', 'front', 'back', 'left', 'right', 'iso'];
const sub = (a: Vec3, b: Vec3): Vec3 => ({ x: a.x - b.x, y: a.y - b.y, z: a.z - b.z });
const dot = (a: Vec3, b: Vec3) => a.x * b.x + a.y * b.y + a.z * b.z;
const cross = (a: Vec3, b: Vec3): Vec3 => ({ x: a.y * b.z - a.z * b.y, y: a.z * b.x - a.x * b.z, z: a.x * b.y - a.y * b.x });
const norm = (a: Vec3): Vec3 => { const l = Math.hypot(a.x, a.y, a.z); return { x: a.x / l, y: a.y / l, z: a.z / l }; };
const corners = (b: Bounds): Vec3[] => [b.minX, b.maxX].flatMap(x => [0, b.height].flatMap(y => [b.minZ, b.maxZ].map(z => ({ x, y, z }))));
function view(id: ViewId, aspect: number, fov = 50) {
  const p = presetPose(id, B, fov, aspect);
  const f = norm(sub(p.target, p.eye)), r = norm(cross(f, p.up)), u = cross(r, f);
  return { p, f, r, u };
}

describe('view presets', () => {
  it('eye is on the right side of the target', () => {
    const want: Record<string, [string, number]> = { top: ['y', 1], front: ['z', 1], back: ['z', -1], left: ['x', -1], right: ['x', 1] };
    for (const [id, [axis, sign]] of Object.entries(want)) {
      const p = presetPose(id as ViewId, B, 50, 1.5), d = sub(p.eye, p.target) as Record<string, number>;
      expect(Math.sign(d[axis])).toBe(sign);
      for (const o of ['x', 'y', 'z'].filter(a => a !== axis)) expect(Math.abs(d[o])).toBeLessThan(1e-9);
    }
    const iso = sub(presetPose('iso', B, 50, 1.5).eye, boundsCenter(B));
    expect(iso.x).toBeLessThan(0); expect(iso.y).toBeGreaterThan(0); expect(iso.z).toBeGreaterThan(0);
  });
  it('top up is -z and plan north stays up, east right', () => {
    expect(VIEW_PRESETS.top.up).toEqual({ x: 0, y: 0, z: -1 });
    const { r, u } = view('top', 1.5);
    expect(r.x).toBeCloseTo(1); expect(u.z).toBeCloseTo(-1);
  });
  it('dirs are unit vectors', () => { for (const id of IDS) { const d = VIEW_PRESETS[id].dir; expect(Math.hypot(d.x, d.y, d.z)).toBeCloseTo(1); } });
  for (const aspect of [0.5, 1, 1.78, 2.4]) {
    it(`all corners inside perspective and ortho frustum, aspect ${aspect}`, () => {
      const fov = 50, tv = Math.tan((fov * Math.PI) / 360);
      for (const id of IDS) {
        const { p, f, r, u } = view(id, aspect, fov);
        let fill = 0;   // how close the tightest corner comes to the frustum edge (1 = on the edge)
        for (const c of corners(B)) {
          const v = sub(c, p.eye), z = dot(v, f);
          expect(z).toBeGreaterThan(0);
          expect(Math.abs(dot(v, u)) / z).toBeLessThanOrEqual(tv + 1e-9);
          expect(Math.abs(dot(v, r)) / z).toBeLessThanOrEqual(tv * aspect + 1e-9);
          fill = Math.max(fill, Math.abs(dot(v, u)) / z / tv, Math.abs(dot(v, r)) / z / (tv * aspect));
        }
        // the framing is tight, not merely containing: with the default margin 1.1 the binding corner sits at ~1/1.1
        expect(fill).toBeGreaterThan(0.85);
        const o = orthoFrustum(B, id, aspect), t = boundsCenter(B);
        const E = Math.hypot(B.maxX - B.minX, B.height, B.maxZ - B.minZ);
        for (const c of corners(B)) {
          const v = sub(c, t), x = dot(v, r), y = dot(v, u), depth = E - dot(v, VIEW_PRESETS[id].dir);
          expect(x).toBeGreaterThanOrEqual(o.left); expect(x).toBeLessThanOrEqual(o.right);
          expect(y).toBeGreaterThanOrEqual(o.bottom); expect(y).toBeLessThanOrEqual(o.top);
          expect(depth).toBeGreaterThanOrEqual(o.near); expect(depth).toBeLessThanOrEqual(o.far);
        }
        expect((o.right - o.left) / (o.top - o.bottom)).toBeCloseTo(aspect);
        // tight too: one axis is filled up to the 1.05 margin
        const hw = (o.right - o.left) / 2, hh = (o.top - o.bottom) / 2, cx = (o.right + o.left) / 2, cy = (o.top + o.bottom) / 2;
        let ofill = 0;
        for (const c of corners(B)) { const v = sub(c, t); ofill = Math.max(ofill, Math.abs(dot(v, r) - cx) / hw, Math.abs(dot(v, u) - cy) / hh); }
        expect(ofill).toBeGreaterThan(0.9);
      }
    });
  }
});

describe('panTarget', () => {
  const R = { x: 1, y: 0, z: 0 }, U = { x: 0, y: 0, z: -1 };
  const t0 = boundsCenter(B);
  it('drag then reverse returns to start', () => {
    const a = panTarget(t0, 40, -25, 800, 10, 50, R, U, B), b = panTarget(a, -40, 25, 800, 10, 50, R, U, B);
    expect(a.x).not.toBeCloseTo(t0.x);
    expect(b.x).toBeCloseTo(t0.x); expect(b.y).toBeCloseTo(t0.y); expect(b.z).toBeCloseTo(t0.z);
  });
  it('maps pixels to world units', () => {
    const a = panTarget(t0, -100, 0, 800, 10, 50, R, U, B, 2);
    expect(a.x - t0.x).toBeCloseTo(100 * (2 * 10 * Math.tan((25 * Math.PI) / 180)) / 800);
  });
  it('clamps to bounds plus slack and y to [0,height]', () => {
    const Y = { x: 0, y: 1, z: 0 };
    const a = panTarget(t0, -1e6, 1e6, 800, 10, 50, R, Y, B, 2);
    expect(a.x).toBe(B.maxX + 2); expect(a.y).toBe(B.height);
    const b = panTarget(t0, 1e6, -1e6, 800, 10, 50, R, Y, B, 2);
    expect(b.x).toBe(B.minX - 2); expect(b.y).toBe(0);
    const c = panTarget(t0, 0, 1e6, 800, 10, 50, R, U, B, 2);
    expect(c.z).toBe(B.minZ - 2);
  });
});

describe('viewKeyFor', () => {
  it('maps 1..6', () => {
    expect(['1', '2', '3', '4', '5', '6'].map(key => viewKeyFor({ key }))).toEqual(['top', 'front', 'right', 'back', 'left', 'iso']);
    expect(viewKeyFor({ key: '7' })).toBeNull();
  });
  it('ignores modifiers and form fields', () => {
    expect(viewKeyFor({ key: '1', ctrlKey: true })).toBeNull();
    expect(viewKeyFor({ key: '1', metaKey: true })).toBeNull();
    expect(viewKeyFor({ key: '1', altKey: true })).toBeNull();
    for (const targetTag of ['INPUT', 'TEXTAREA', 'SELECT', 'input']) expect(viewKeyFor({ key: '1', targetTag })).toBeNull();
    expect(viewKeyFor({ key: '1', targetTag: 'DIV' })).toBe('top');
  });
});
