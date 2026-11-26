/**
 * Pure camera maths for the 3D view (no three.js import, plain tuples).
 *
 * Conventions, taken from the engine (components/viewer3d-engine.js):
 *  - World is three.js Y-up, meters; the 2D plan maps to x/z (core/dimensions.ts:9 Bounds{x0,x1,z0,z1}).
 *    Plan north is -z, so the top view uses up = (0,0,-1).
 *  - Building bounds: W = maxX-minX, D = maxZ-minZ, centre C = ((minX+maxX)/2, 0, (minZ+maxZ)/2)
 *    (viewer3d-engine.js:951); orbit radius is based on Math.max(W, D, 4) (engine :955, :1363).
 *  - Orbit: eye = F + (sin(th)*sin(ph), cos(ph), cos(th)*sin(ph)) * r  (engine :1365), ph measured
 *    from +Y, so th=0 puts the eye on +z ("front"). Engine presets: top th=0 ph=.05 (:1454),
 *    iso th=-PI/4 ph=PI/4 (:1458), side th=0 ph=PI/2-.08 (:1462). ISO dir below equals that iso.
 *  - Perspective camera: vertical fov in degrees, aspect = w/h (engine :57, :1344).
 */
export type Vec3 = { x: number; y: number; z: number };
export type Bounds = { minX: number; maxX: number; minZ: number; maxZ: number; height: number };
export type ViewId = 'top' | 'front' | 'back' | 'left' | 'right' | 'iso';
export type Pose = { eye: Vec3; target: Vec3; up: Vec3 };
export type OrthoFrustum = { left: number; right: number; top: number; bottom: number; near: number; far: number };

const S = Math.SQRT1_2;
/** dir: unit vector from target to eye. */
export const VIEW_PRESETS: Record<ViewId, { dir: Vec3; up: Vec3 }> = {
  top:   { dir: { x: 0, y: 1, z: 0 },  up: { x: 0, y: 0, z: -1 } },
  front: { dir: { x: 0, y: 0, z: 1 },  up: { x: 0, y: 1, z: 0 } },
  back:  { dir: { x: 0, y: 0, z: -1 }, up: { x: 0, y: 1, z: 0 } },
  left:  { dir: { x: -1, y: 0, z: 0 }, up: { x: 0, y: 1, z: 0 } },
  right: { dir: { x: 1, y: 0, z: 0 },  up: { x: 0, y: 1, z: 0 } },
  iso:   { dir: { x: -0.5, y: S, z: 0.5 }, up: { x: 0, y: 1, z: 0 } },
};

const dot = (a: Vec3, b: Vec3) => a.x * b.x + a.y * b.y + a.z * b.z;
const cross = (a: Vec3, b: Vec3): Vec3 => ({ x: a.y * b.z - a.z * b.y, y: a.z * b.x - a.x * b.z, z: a.x * b.y - a.y * b.x });
const norm = (a: Vec3): Vec3 => { const l = Math.hypot(a.x, a.y, a.z) || 1; return { x: a.x / l, y: a.y / l, z: a.z / l }; };

function basis(id: ViewId) {
  const { dir, up } = VIEW_PRESETS[id];
  const fwd = { x: -dir.x, y: -dir.y, z: -dir.z };
  const right = norm(cross(fwd, up));
  const camUp = cross(right, fwd);
  return { dir, right, camUp };
}

export function boundsCenter(b: Bounds): Vec3 {
  return { x: (b.minX + b.maxX) / 2, y: b.height / 2, z: (b.minZ + b.maxZ) / 2 };
}

function corners(b: Bounds): Vec3[] {
  const out: Vec3[] = [];
  for (const x of [b.minX, b.maxX]) for (const y of [0, b.height]) for (const z of [b.minZ, b.maxZ]) out.push({ x, y, z });
  return out;
}

/** Eye at target + dir*distance, so every corner is inside both the vertical and horizontal fov (times margin). */
export function presetPose(id: ViewId, bounds: Bounds, fovDeg: number, aspect: number, margin = 1.1): Pose {
  const target = boundsCenter(bounds);
  const { dir, right, camUp } = basis(id);
  const tv = Math.tan((fovDeg * Math.PI) / 360), th = tv * aspect;
  let dist = 0.1;
  for (const c of corners(bounds)) {
    const v = { x: c.x - target.x, y: c.y - target.y, z: c.z - target.z };
    const depth = dot(v, dir);
    dist = Math.max(dist, depth + (Math.abs(dot(v, right)) * margin) / th, depth + (Math.abs(dot(v, camUp)) * margin) / tv);
  }
  return { eye: { x: target.x + dir.x * dist, y: target.y + dir.y * dist, z: target.z + dir.z * dist }, target, up: VIEW_PRESETS[id].up };
}

/** Eye distance assumed by orthoFrustum: the full box diagonal (twice the half-diagonal), from its centre. */
export function orthoEyeDistance(b: Bounds): number {
  return Math.hypot(b.maxX - b.minX, b.height, b.maxZ - b.minZ);
}

/** Frustum (view space, eye at centre + dir*orthoEyeDistance) containing all 8 corners; aspect is expanded, never cropped. */
export function orthoFrustum(bounds: Bounds, id: ViewId, aspect: number, margin = 1.05): OrthoFrustum {
  const t = boundsCenter(bounds);
  const { dir, right, camUp } = basis(id);
  let minX = Infinity, maxX = -Infinity, minY = Infinity, maxY = -Infinity, minD = Infinity, maxD = -Infinity;
  for (const c of corners(bounds)) {
    const v = { x: c.x - t.x, y: c.y - t.y, z: c.z - t.z };
    const x = dot(v, right), y = dot(v, camUp), d = dot(v, dir);
    minX = Math.min(minX, x); maxX = Math.max(maxX, x); minY = Math.min(minY, y); maxY = Math.max(maxY, y);
    minD = Math.min(minD, d); maxD = Math.max(maxD, d);
  }
  let hw = Math.max(((maxX - minX) / 2) * margin, 1e-6), hh = Math.max(((maxY - minY) / 2) * margin, 1e-6);
  if (hw / hh < aspect) hw = hh * aspect; else hh = hw / aspect;
  const cx = (maxX + minX) / 2, cy = (maxY + minY) / 2, E = orthoEyeDistance(bounds), pad = 0.5;
  return { left: cx - hw, right: cx + hw, top: cy + hh, bottom: cy - hh, near: Math.max(0.01, E - maxD - pad), far: E - minD + pad };
}

/** Screen drag (px) to world pan along camera right/up; result clamped to bounds +- slack (y to [0,height]).
 *  Perspective scale only: for an orthographic camera use the frustum height / zoom instead of 2*distance*tan(fov/2).
 *  Returns only the new target: the caller moves the eye by the same (clamped) delta so the orbit is preserved. */
export function panTarget(
  target: Vec3, dxPx: number, dyPx: number, viewportHeightPx: number, distance: number, fovDeg: number,
  cameraRight: Vec3, cameraUp: Vec3, bounds: Bounds, slack = 2,
): Vec3 {
  const wpp = (2 * distance * Math.tan((fovDeg * Math.PI) / 360)) / Math.max(1, viewportHeightPx);
  // dragging right moves the scene right, so the target moves left; dragging down moves the target up.
  const mx = -dxPx * wpp, my = dyPx * wpp;
  const clamp = (v: number, lo: number, hi: number) => Math.min(hi, Math.max(lo, v));
  return {
    x: clamp(target.x + cameraRight.x * mx + cameraUp.x * my, bounds.minX - slack, bounds.maxX + slack),
    y: clamp(target.y + cameraRight.y * mx + cameraUp.y * my, 0, bounds.height),
    z: clamp(target.z + cameraRight.z * mx + cameraUp.z * my, bounds.minZ - slack, bounds.maxZ + slack),
  };
}

const KEY_VIEWS: Record<string, ViewId> = { '1': 'top', '2': 'front', '3': 'right', '4': 'back', '5': 'left', '6': 'iso' };

export function viewKeyFor(e: { key: string; ctrlKey?: boolean; metaKey?: boolean; altKey?: boolean; targetTag?: string }): ViewId | null {
  if (e.ctrlKey || e.metaKey || e.altKey) return null;
  const tag = (e.targetTag || '').toUpperCase();
  if (tag === 'INPUT' || tag === 'TEXTAREA' || tag === 'SELECT') return null;
  return KEY_VIEWS[e.key] ?? null;
}
